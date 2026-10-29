// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// The entry point of the test assembly. The memory tests profile a generated stream in a fresh child process (this
/// assembly run as <c>dotnet squalify.Tests.dll --memory-probe BYTES COLUMNS</c>, with the default runtime settings),
/// because the peak working set of a process cannot be reset, and the test host holds memory that has nothing to do
/// with the profile. A normal test run
/// never calls <see cref="Main"/>: the test host loads the assembly directly.
/// </summary>
internal static class MemoryProbe
{
    /// <summary>The first argument that selects the probe.</summary>
    public const string Switch = "--memory-probe";

    /// <summary>The result of one probe run, as the child writes it.</summary>
    /// <param name="PeakWorkingSet">The peak working set of the child after the large profile, in bytes.</param>
    /// <param name="Baseline">The peak working set of the same child after a small profile, before the large one: the runtime, the loaded pipeline and a little data.</param>
    /// <param name="EstimatedBytes">The peak of the accumulators' estimated bytes.</param>
    /// <param name="CeilingBytes">The ceiling for the peak working set.</param>
    /// <param name="Seconds">The duration of the profile.</param>
    /// <param name="Approximate">The number of fields with an approximate distinct count.</param>
    /// <param name="Fields">The number of fields.</param>
    /// <param name="Diagnostics">Runtime and GC figures from the child at its start, at the baseline and at the end, one line each, for a failure message.</param>
    public sealed record Result(long PeakWorkingSet, long Baseline, long EstimatedBytes, long CeilingBytes, double Seconds, int Approximate, int Fields, string Diagnostics = "");

    /// <summary>The entry point: runs the probe when asked to, and does nothing otherwise.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code.</returns>
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == ThroughputBench.Switch)
        {
            return ThroughputBench.RunProbe(args);
        }

        if (args.Length != 3 || args[0] != Switch)
        {
            return 0;
        }

        var bytes = long.Parse(args[1], CultureInfo.InvariantCulture);
        var columns = int.Parse(args[2], CultureInfo.InvariantCulture);

        // The baseline: a small profile of the same shape, so that the runtime, the JIT code of the pipeline and the
        // read buffers are in the peak before the large profile starts. The peak working set only ever grows.
        var diagnostics = new StringBuilder();
        diagnostics.AppendLine(Snapshot("start"));
        Profiler.Profile(new SyntheticCsvSource(1_000, columns), ProfileOptions.Create(false, []), ProfileLimits.Default);
        var baseline = PeakWorkingSet();
        diagnostics.AppendLine(Snapshot("baseline"));
        var source = new SyntheticCsvSource(SyntheticCsvSource.RowsFor(bytes, columns), columns);
        var trace = new MemoryTrace();
        var clock = Stopwatch.StartNew();
        var descriptor = Profiler.Profile(source, ProfileOptions.Create(false, []), ProfileLimits.Default, trace);
        clock.Stop();
        var fields = descriptor.Table!.Fields;
        var approximate = fields.Count(static f => f.Distinct?.Approximate == true);
        Console.Out.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"peak={PeakWorkingSet()} baseline={baseline} estimated={trace.PeakEstimatedBytes} ceiling={trace.CeilingBytes} seconds={clock.Elapsed.TotalSeconds:F1} approximate={approximate} fields={fields.Count}"));
        diagnostics.AppendLine(Snapshot("end"));
        Console.Out.Write(diagnostics.ToString());
        return 0;
    }

    // One line of runtime and GC figures, for the message of a failing memory test. The GC figures are those of the
    // last collection (GetGCMemoryInfo), the others are current. Nothing here is asserted: it shows where the working
    // set of a process went when the growth over the baseline is not squalify's data.
    private static string Snapshot(string label)
    {
        var info = GC.GetGCMemoryInfo();
        using var process = Process.GetCurrentProcess();
        var line = new StringBuilder("diag ").Append(label).Append(':');
        void Add(string name, object? value) => line.Append(' ').Append(name).Append('=').Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        Add("workingSet", process.WorkingSet64);
        Add("peakWorkingSet", PeakWorkingSet());
        Add("privateMemory", process.PrivateMemorySize64); // 0 where the OS does not report it (macOS)
        Add("totalAllocated", GC.GetTotalAllocatedBytes(false));
        Add("gcIndex", info.Index);
        Add("gcHeap", info.HeapSizeBytes);
        Add("gcFragmented", info.FragmentedBytes);
        Add("gcCommitted", info.TotalCommittedBytes);
        Add("gcPromoted", info.PromotedBytes);
        for (var g = 0; g < info.GenerationInfo.Length; g++)
        {
            var generation = info.GenerationInfo[g];
            // Generations 0 to 2, then the large object heap (3) and the pinned object heap (4).
            var name = g switch { 3 => "loh", 4 => "poh", _ => "gen" + g.ToString(CultureInfo.InvariantCulture) };
            Add(name + "After", generation.SizeAfterBytes);
            Add(name + "FragAfter", generation.FragmentationAfterBytes);
        }

        for (var g = 0; g < 3; g++)
        {
            Add("collections" + g.ToString(CultureInfo.InvariantCulture), GC.CollectionCount(g));
        }

        Add("availableMemory", info.TotalAvailableMemoryBytes);
        Add("memoryLoad", info.MemoryLoadBytes);
        Add("serverGC", GCSettings.IsServerGC);
        Add("latencyMode", GCSettings.LatencyMode);
        Add("processors", Environment.ProcessorCount);
        Add("gen0Size", AppContext.GetData("System.GC.Gen0Size") ?? AppContext.GetData("GCgen0size") ?? Environment.GetEnvironmentVariable("DOTNET_GCgen0size") ?? "unset");
        Add("conserveMemory", AppContext.GetData("System.GC.ConserveMemory") ?? Environment.GetEnvironmentVariable("DOTNET_GCConserveMemory") ?? "unset");
        Add("concurrentGC", AppContext.GetData("System.GC.Concurrent") ?? "default");
        return line.ToString();
    }

    // The peak working set of this process. Process.PeakWorkingSet64 is 0 on macOS, where the kernel's maximum resident
    // size (getrusage, in bytes) is used instead.
    internal static long PeakWorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        var peak = process.PeakWorkingSet64;
        if (peak == 0 && OperatingSystem.IsMacOS())
        {
            var usage = new long[32];
            if (GetResourceUsage(0, usage) == 0)
            {
                // struct rusage: two timevals (16 bytes each), then ru_maxrss.
                peak = usage[4];
            }
        }

        return peak;
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getrusage")]
    private static extern int GetResourceUsage(int who, long[] usage);

    /// <summary>Runs the probe in a child process and parses its result.</summary>
    /// <param name="bytes">The size of the generated stream.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>The result.</returns>
    public static Result RunChild(long bytes, int columns, TimeSpan timeout)
    {
        var lines = RunProbeProcess([Switch, bytes.ToString(CultureInfo.InvariantCulture), columns.ToString(CultureInfo.InvariantCulture)], timeout);
        var fields = lines[0].Split(' ').Select(static p => p.Split('=')).ToDictionary(static p => p[0], static p => p[1]);
        return new Result(
            long.Parse(fields["peak"], CultureInfo.InvariantCulture),
            long.Parse(fields["baseline"], CultureInfo.InvariantCulture),
            long.Parse(fields["estimated"], CultureInfo.InvariantCulture),
            long.Parse(fields["ceiling"], CultureInfo.InvariantCulture),
            double.Parse(fields["seconds"], CultureInfo.InvariantCulture),
            int.Parse(fields["approximate"], CultureInfo.InvariantCulture),
            int.Parse(fields["fields"], CultureInfo.InvariantCulture),
            string.Join('\n', lines.Skip(1)));
    }

    /// <summary>Runs this assembly in a child process with the given probe arguments and returns its output lines.</summary>
    /// <param name="arguments">The arguments after the assembly path, the first of them a probe switch.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>The lines the child wrote to its standard output.</returns>
    internal static string[] RunProbeProcess(IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(HostPath())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add(typeof(MemoryProbe).Assembly.Location);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The probe did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The memory probe did not finish in {timeout}.");
        }

        Assert.True(process.ExitCode == 0, $"The memory probe failed with {process.ExitCode}: {error.Result}");
        return output.Result.Trim().Split('\n', StringSplitOptions.TrimEntries);
    }

    private static string HostPath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        // RuntimeEnvironment.GetRuntimeDirectory() is <root>/shared/Microsoft.NETCore.App/<version>/.
        var root = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(candidate) ? candidate : throw new InvalidOperationException($"Cannot find the dotnet host at '{candidate}'.");
    }
}
