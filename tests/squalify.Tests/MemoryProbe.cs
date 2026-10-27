// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// The entry point of the test assembly. The memory tests profile a generated stream in a fresh child process (this
/// assembly run as <c>dotnet squalify.Tests.dll --memory-probe BYTES COLUMNS</c>), because the peak working set of a
/// process cannot be reset, and the test host holds memory that has nothing to do with the profile. A normal test run
/// never calls <see cref="Main"/>: the test host loads the assembly directly.
/// </summary>
internal static class MemoryProbe
{
    /// <summary>The first argument that selects the probe.</summary>
    public const string Switch = "--memory-probe";

    /// <summary>The result of one probe run, as the child writes it.</summary>
    /// <param name="PeakWorkingSet">The peak working set of the child, in bytes.</param>
    /// <param name="EstimatedBytes">The peak of the accumulators' estimated bytes.</param>
    /// <param name="CeilingBytes">The ceiling for the peak working set.</param>
    /// <param name="Seconds">The duration of the profile.</param>
    /// <param name="Approximate">The number of fields with an approximate distinct count.</param>
    /// <param name="Fields">The number of fields.</param>
    public sealed record Result(long PeakWorkingSet, long EstimatedBytes, long CeilingBytes, double Seconds, int Approximate, int Fields);

    /// <summary>The entry point: runs the probe when asked to, and does nothing otherwise.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code.</returns>
    public static int Main(string[] args)
    {
        if (args.Length != 3 || args[0] != Switch)
        {
            return 0;
        }

        var bytes = long.Parse(args[1], CultureInfo.InvariantCulture);
        var columns = int.Parse(args[2], CultureInfo.InvariantCulture);
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
                $"peak={PeakWorkingSet()} estimated={trace.PeakEstimatedBytes} ceiling={trace.CeilingBytes} seconds={clock.Elapsed.TotalSeconds:F1} approximate={approximate} fields={fields.Count}"));
        return 0;
    }

    // The peak working set of this process. Process.PeakWorkingSet64 is 0 on macOS, where the kernel's maximum resident
    // size (getrusage, in bytes) is used instead.
    private static long PeakWorkingSet()
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
        var start = new ProcessStartInfo(HostPath())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Tiered compilation keeps tier-0 code and profiling data of the hot loops resident: about 5 to 9 MB more than
        // optimized code only, on top of the data, which is what the ceiling accounts for. The probe measures the
        // accumulators, so it runs without tiering (see "Bounded memory and sketches" in docs/design.md).
        start.Environment["DOTNET_TieredCompilation"] = "0";
        start.ArgumentList.Add(typeof(MemoryProbe).Assembly.Location);
        start.ArgumentList.Add(Switch);
        start.ArgumentList.Add(bytes.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(columns.ToString(CultureInfo.InvariantCulture));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The probe did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The memory probe did not finish in {timeout}.");
        }

        Assert.True(process.ExitCode == 0, $"The memory probe failed with {process.ExitCode}: {error.Result}");
        var fields = output.Result.Trim().Split(' ').Select(static p => p.Split('=')).ToDictionary(static p => p[0], static p => p[1]);
        return new Result(
            long.Parse(fields["peak"], CultureInfo.InvariantCulture),
            long.Parse(fields["estimated"], CultureInfo.InvariantCulture),
            long.Parse(fields["ceiling"], CultureInfo.InvariantCulture),
            double.Parse(fields["seconds"], CultureInfo.InvariantCulture),
            int.Parse(fields["approximate"], CultureInfo.InvariantCulture),
            int.Parse(fields["fields"], CultureInfo.InvariantCulture));
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
