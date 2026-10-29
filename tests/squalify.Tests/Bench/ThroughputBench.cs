// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>The shape of the generated stream.</summary>
public enum BenchShape
{
    /// <summary>Twelve narrow, unpadded columns, the shape of the memory tests.</summary>
    Plain,

    /// <summary>A wide table of right-padded fixed-width cells, with a key preamble and annotation rows.</summary>
    PaddedWide,
}

/// <summary>What the benchmark times.</summary>
public enum BenchPass
{
    /// <summary>Only reads the generated stream to its end: the cost of the generator, the floor for the other two.</summary>
    Drain,

    /// <summary>The byte pass alone (<c>ByteSniffer.Sniff</c>): one read of the whole stream, with its SHA-256 and byte counts.</summary>
    BytePass,

    /// <summary>The full profile: the byte pass, format detection (a scoring read) and the format profiler (the field read).</summary>
    Full,
}

/// <summary>
/// The measurement behind <see cref="ThroughputTests"/>. Each measurement runs in a fresh child process (this assembly
/// run as <c>dotnet squalify.Tests.dll --throughput-probe PASS SHAPE BYTES</c>, entered through
/// <see cref="MemoryProbe.Main"/>), because the peak working set of a process cannot be reset and the test host holds
/// memory of its own. The child first runs the same pass on a small stream of the same shape (the baseline: runtime,
/// JIT code, read buffers), then on the stream that is measured, and reports the wall time of the second run and the peak
/// working set after it. The peak working set is the one <see cref="MemoryProbe"/> reads: <c>Process.PeakWorkingSet64</c>,
/// and on macOS, where that is 0, the kernel's maximum resident size from <c>getrusage</c>. It is the peak over the whole
/// life of the child, not a sample.
/// </summary>
internal static class ThroughputBench
{
    /// <summary>The first argument that selects the throughput probe.</summary>
    public const string Switch = "--throughput-probe";

    /// <summary>The columns of the plain shape.</summary>
    public const int PlainColumns = 12;

    /// <summary>The columns of the padded wide shape.</summary>
    public const int WideColumns = 256;

    /// <summary>The width of every cell of the padded wide shape; about 89 percent of its bytes are padding.</summary>
    public const int WideCellWidth = 64;

    // The size of the baseline stream.
    private const long BaselineBytes = 1024 * 1024;

    /// <summary>The figures of one measurement.</summary>
    /// <param name="Shape">The stream shape.</param>
    /// <param name="Pass">What was timed.</param>
    /// <param name="Bytes">The length of the stream.</param>
    /// <param name="Rows">The data rows generated.</param>
    /// <param name="TableRows">The rows the profile reported (0 unless the pass is <see cref="BenchPass.Full"/>).</param>
    /// <param name="Fields">The fields the profile reported (0 unless the pass is <see cref="BenchPass.Full"/>).</param>
    /// <param name="ConstantLineLength">Whether the profile reported a constant line length.</param>
    /// <param name="Cells">The cells of the data rows: rows x columns.</param>
    /// <param name="Seconds">The wall time of the measured run.</param>
    /// <param name="PeakWorkingSet">The peak working set of the child after the measured run, in bytes.</param>
    /// <param name="Baseline">The peak working set of the child after the baseline run.</param>
    /// <param name="EstimatedBytes">The peak of the accumulators' estimated bytes (0 unless the pass is <see cref="BenchPass.Full"/>).</param>
    /// <param name="CeilingBytes">The ceiling for the growth of the peak working set over the baseline.</param>
    public sealed record Result(
        BenchShape Shape,
        BenchPass Pass,
        long Bytes,
        long Rows,
        long TableRows,
        int Fields,
        bool ConstantLineLength,
        long Cells,
        double Seconds,
        long PeakWorkingSet,
        long Baseline,
        long EstimatedBytes,
        long CeilingBytes)
    {
        /// <summary>The growth of the peak working set over the baseline.</summary>
        public long Growth => PeakWorkingSet - Baseline;

        /// <summary>Megabytes (10^6 bytes) of stream per second.</summary>
        public double MegabytesPerSecond => Bytes / 1e6 / Seconds;

        /// <summary>Cells of the data rows per second.</summary>
        public double CellsPerSecond => Cells / Seconds;
    }

    /// <summary>The generated source of a shape, of about the given size.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="bytes">The wanted size.</param>
    /// <returns>The source.</returns>
    public static SyntheticCsvSource Source(BenchShape shape, long bytes)
    {
        return shape == BenchShape.PaddedWide
            ? SyntheticCsvSource.PaddedWide(SyntheticCsvSource.PaddedRowsFor(bytes, WideColumns, WideCellWidth), WideColumns, WideCellWidth)
            : new SyntheticCsvSource(SyntheticCsvSource.RowsFor(bytes, PlainColumns), PlainColumns);
    }

    /// <summary>Runs one measurement in a child process.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="pass">What to time.</param>
    /// <param name="bytes">The size of the stream.</param>
    /// <param name="timeout">How long to wait for the child.</param>
    /// <returns>The figures.</returns>
    public static Result Run(BenchShape shape, BenchPass pass, long bytes, TimeSpan timeout)
    {
        var lines = MemoryProbe.RunProbeProcess(
            [Switch, pass.ToString(), shape.ToString(), bytes.ToString(CultureInfo.InvariantCulture)],
            timeout);
        var f = lines[0].Split(' ').Select(static p => p.Split('=')).ToDictionary(static p => p[0], static p => p[1]);
        long L(string key) => long.Parse(f[key], CultureInfo.InvariantCulture);
        return new Result(
            shape,
            pass,
            L("bytes"),
            L("rows"),
            L("tableRows"),
            (int)L("fields"),
            f["constantLineLength"] == "true",
            L("cells"),
            double.Parse(f["seconds"], CultureInfo.InvariantCulture),
            L("peak"),
            L("baseline"),
            L("estimated"),
            L("ceiling"));
    }

    /// <summary>
    /// Asserts that the growth of the peak working set over the baseline stays within the ceiling, and that the probe
    /// could read the peak at all. For the full profile the ceiling is <see cref="MemoryBudget.CeilingBytes(long)"/> of the
    /// accumulators' peak estimate; the byte pass keeps no accumulators, so its ceiling is the bare allowance.
    /// </summary>
    /// <param name="result">The figures.</param>
    /// <param name="ceilingBytes">A ceiling to use in place of the measured one; tests give a low one to see the assertion fail.</param>
    public static void AssertWithinCeiling(Result result, long? ceilingBytes = null)
    {
        Assert.True(result.PeakWorkingSet > 0 && result.Baseline > 0, "the probe could not read its peak working set");
        var ceiling = ceilingBytes ?? result.CeilingBytes;
        Assert.True(
            result.Growth <= ceiling,
            $"the peak working set grew {result.Growth} bytes over the baseline of {result.Baseline}, above the ceiling {ceiling}; {Describe(result)}");
    }

    /// <summary>One line of figures, for the test output.</summary>
    /// <param name="result">The figures.</param>
    /// <returns>The line.</returns>
    public static string Describe(Result result)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{result.Shape,-10} {result.Pass,-8} {result.Bytes / 1048576.0,8:F0} MiB  {result.Seconds,8:F2} s  {result.MegabytesPerSecond,8:F1} MB/s  {result.CellsPerSecond / 1e6,7:F2} Mcells/s  peak {result.PeakWorkingSet / 1e6:F1} MB  baseline {result.Baseline / 1e6:F1} MB  growth {result.Growth / 1e6:F1} MB  estimated {result.EstimatedBytes / 1e6:F1} MB  ceiling {result.CeilingBytes / 1e6:F1} MB  rows {result.Rows}  tableRows {result.TableRows}  fields {result.Fields}  constantLineLength {result.ConstantLineLength}");
    }

    /// <summary>The child's entry point: <c>--throughput-probe PASS SHAPE BYTES</c>. Writes one line of <c>key=value</c> figures.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code.</returns>
    public static int RunProbe(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("usage: --throughput-probe PASS SHAPE BYTES");
            return 2;
        }

        var pass = Enum.Parse<BenchPass>(args[1]);
        var shape = Enum.Parse<BenchShape>(args[2]);
        var bytes = long.Parse(args[3], CultureInfo.InvariantCulture);

        // The baseline: the same pass on a small stream of the same shape. The peak working set only ever grows.
        Execute(pass, Source(shape, BaselineBytes), new MemoryTrace(), out _);
        var baseline = MemoryProbe.PeakWorkingSet();

        var source = Source(shape, bytes);
        var trace = new MemoryTrace();
        var clock = Stopwatch.StartNew();
        Execute(pass, source, trace, out var descriptor);
        clock.Stop();
        var peak = MemoryProbe.PeakWorkingSet();
        var padding = (descriptor?.Format.Dialect as DelimitedDialect)?.Padding;
        Console.Out.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"bytes={source.Length} rows={source.Rows} tableRows={descriptor?.Table?.Rows ?? 0} fields={descriptor?.Table?.Fields.Count ?? 0} constantLineLength={(padding?.ConstantLineLength == true ? "true" : "false")} cells={source.Rows * source.Columns} seconds={clock.Elapsed.TotalSeconds:F3} peak={peak} baseline={baseline} estimated={trace.PeakEstimatedBytes} ceiling={trace.CeilingBytes}"));
        return 0;
    }

    private static void Execute(BenchPass pass, SyntheticCsvSource source, MemoryTrace trace, out Descriptor? descriptor)
    {
        descriptor = null;
        switch (pass)
        {
            case BenchPass.Drain:
                using (var stream = source.Open())
                {
                    var buffer = new byte[64 * 1024];
                    while (stream.Read(buffer) > 0)
                    {
                    }
                }

                break;
            case BenchPass.BytePass:
                ByteSniffer.Sniff(source, ProfileLimits.Default.ApplyTo(Limits.Default).CharsetTolerance);
                break;
            default:
                descriptor = Profiler.Profile(source, ProfileOptions.Create(false, []), ProfileLimits.Default, trace);
                break;
        }
    }
}
