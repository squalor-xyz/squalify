// SPDX-License-Identifier: Apache-2.0
using squalor.squalify;
using Xunit.Abstractions;

namespace squalor.squalify.Tests;

[CollectionDefinition("MemoryProbe", DisableParallelization = true)]
public sealed class MemoryProbeCollection;

[Collection("MemoryProbe")]
public sealed class MemoryBoundTests(ITestOutputHelper output)
{
    private const int Columns = 12;

    [Fact]
    public void MemoryAccounting_UnderBudget_256MbStream()
    {
        var result = MemoryProbe.RunChild(256L * 1024 * 1024, Columns, TimeSpan.FromMinutes(10));

        Report(result);
        Assert.Equal(Columns, result.Fields);
        Assert.True(result.PeakWorkingSet > 0 && result.Baseline > 0, "the probe could not read its peak working set");

        // Several columns have more than 10,000 distinct values, so the sketches were exercised.
        Assert.True(result.Approximate > 0, "no field crossed the distinct cap");
        Assert.True(result.EstimatedBytes > 0);
        Assert.Equal(MemoryBudget.CeilingBytes(result.EstimatedBytes), result.CeilingBytes);
        AssertWithinCeiling(result);
    }

    [Fact]
    [Trait("Category", "Large")]
    public void Memory_4GbStream_PeakWorkingSetBounded()
    {
        var result = MemoryProbe.RunChild(4L * 1024 * 1024 * 1024, Columns, TimeSpan.FromHours(2));

        Report(result);
        Assert.True(result.PeakWorkingSet > 0 && result.Baseline > 0, "the probe could not read its peak working set");
        Assert.True(result.Approximate > 0);
        AssertWithinCeiling(result);
    }

    // What squalify holds is the growth of the process over a small-profile baseline measured in the same child, with
    // the default runtime settings: the fixed cost of the runtime and of the loaded pipeline is not in it. The growth
    // may not exceed the peak of the accumulators' estimated bytes plus the allowance (read buffers and headroom).
    private static void AssertWithinCeiling(MemoryProbe.Result result)
    {
        var growth = result.PeakWorkingSet - result.Baseline;
        Assert.True(
            growth <= result.CeilingBytes,
            $"the peak working set grew {growth} bytes over the baseline of {result.Baseline}, above the ceiling {result.CeilingBytes} (estimated {result.EstimatedBytes} + allowance {MemoryBudget.AllowanceBytes}); {result.Seconds}s\n{result.Diagnostics}");
    }

    [Fact]
    public void Accounting_LiveHeapStaysWithinTheEstimate()
    {
        // At sampled points of a profile of 150,000 rows (past the quantile cap, so the exact buffers are freed and the
        // sketches take over), force a full compacting collection and compare the live heap that the profile added with
        // the estimate. The live heap may exceed the estimate by 25 percent plus a fixed 1 MiB for what the estimate
        // does not cover: the read buffers, the generated source's tables and the pass's own small lists. Measured, the
        // live heap is within 3 percent of the estimate plus a constant 0.4 MB.
        const double Ratio = 1.25;
        const long Slack = 1L * 1024 * 1024;
        var before = LiveHeap();
        var samples = new List<(long Estimate, long Added)>();
        var count = 0;
        var trace = new MemoryTrace
        {
            Observer = estimate =>
            {
                if (count++ % 8 == 0)
                {
                    samples.Add((estimate, LiveHeap() - before));
                }
            },
        };

        Profiler.Profile(new SyntheticCsvSource(150_000, Columns), ProfileOptions.Create(false, []), ProfileLimits.Default, trace);
        Assert.True(samples.Count >= 10, $"{samples.Count} samples");
        foreach (var (estimate, added) in samples)
        {
            output.WriteLine($"estimate {estimate / 1e6:F2} MB, live heap added {added / 1e6:F2} MB");
            Assert.True(added <= (estimate * Ratio) + Slack, $"the live heap added {added} bytes, above {Ratio} x the estimate {estimate} + {Slack}");
        }

        // Past the caps the estimate falls with the freed buffers, and so does the heap.
        Assert.True(samples[^1].Estimate < samples.Max(static s => s.Estimate) / 2);
    }

    // The bytes of live objects after a full compacting collection: the heap size without the free space inside it.
    private static long LiveHeap()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        return info.HeapSizeBytes - info.FragmentedBytes;
    }

    private void Report(MemoryProbe.Result result)
    {
        output.WriteLine(
            $"peak working set {result.PeakWorkingSet / 1e6:F1} MB over a baseline of {result.Baseline / 1e6:F1} MB (growth {(result.PeakWorkingSet - result.Baseline) / 1e6:F1} MB), estimated {result.EstimatedBytes / 1e6:F1} MB, ceiling {result.CeilingBytes / 1e6:F1} MB, {result.Seconds} s, {result.Approximate} of {result.Fields} fields approximate");
        output.WriteLine(result.Diagnostics);
    }
}
