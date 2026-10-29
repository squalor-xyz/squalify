// SPDX-License-Identifier: Apache-2.0
using Xunit.Abstractions;
using Xunit.Sdk;

namespace squalor.squalify.Tests;

/// <summary>
/// The throughput benchmark: how fast squalify reads a generated GB-scale stream, wide and right-padded or plain, for the
/// reading of the source alone, the byte pass alone and the full profile, with the peak working set of each. The Large
/// tests are run by hand (<c>--filter Category=Large</c>); the small ones run the same code on a few MiB, so that the
/// machinery and its assertions are checked on every run. A 1 GiB run asserts that the peak working set stays within
/// <see cref="MemoryBudget.CeilingBytes(long)"/> (under the default workstation concurrent GC); the 4 GiB run only records its figures. The small runs
/// assert that every pass counted every byte, not the ceiling. Timings are never asserted.
/// </summary>
[Collection("MemoryProbe")]
public sealed class ThroughputTests(ITestOutputHelper output)
{
    private const long Mib = 1024L * 1024;
    private const long Gib = 1024L * Mib;
    private const long SmallBytes = 16 * Mib;

    [Fact]
    [Trait("Category", "Large")]
    public void Bench_PaddedWide_1Gb()
    {
        RunAsserted(BenchShape.PaddedWide, Gib, TimeSpan.FromHours(2));
    }

    [Fact]
    [Trait("Category", "Large")]
    public void Bench_Plain_1Gb()
    {
        RunAsserted(BenchShape.Plain, Gib, TimeSpan.FromHours(2));
    }

    [Fact]
    [Trait("Category", "Large")]
    public void Bench_PaddedWide_4Gb()
    {
        // Recorded only: the figures go into the slicer note, nothing is asserted about them.
        foreach (var pass in Enum.GetValues<BenchPass>())
        {
            var result = ThroughputBench.Run(BenchShape.PaddedWide, pass, 4 * Gib, TimeSpan.FromHours(6));
            output.WriteLine(ThroughputBench.Describe(result));
        }
    }

    // The small runs check the machinery, not the memory: the peak working set depends on the GC mode (server or
    // non-concurrent GC adds slack that the default workstation concurrent GC does not), so the ceiling is asserted
    // only by the 1 GiB Large tests.
    [Fact]
    public void Bench_PaddedWide_SmallStream_RunsAndReportsItsPeak()
    {
        RunSmall(BenchShape.PaddedWide);
    }

    [Fact]
    public void Bench_Plain_SmallStream_RunsAndReportsItsPeak()
    {
        RunSmall(BenchShape.Plain);
    }

    [Theory]
    [InlineData(BenchShape.PaddedWide)]
    [InlineData(BenchShape.Plain)]
    public void Bench_CeilingTooLow_FailsTheAssertion(BenchShape shape)
    {
        // The assertion is live: the growth of a full profile over its baseline is more than a ceiling of one byte, and
        // more than the ceiling of the same run minus the whole of its growth.
        var result = ThroughputBench.Run(shape, BenchPass.Full, SmallBytes, TimeSpan.FromMinutes(5));
        ThroughputBench.AssertRan(result);
        var growth = result.PeakWorkingSet - result.Baseline;
        Assert.True(growth > 0, "the peak working set did not grow over the baseline, so the probe measures nothing");
        Assert.ThrowsAny<XunitException>(() => ThroughputBench.AssertWithinCeiling(result, ceilingBytes: 1));
        Assert.ThrowsAny<XunitException>(() => ThroughputBench.AssertWithinCeiling(result, ceilingBytes: growth - 1));
        ThroughputBench.AssertWithinCeiling(result, ceilingBytes: growth);
    }

    [Fact]
    public void Bench_ProfilesTheWholeTable()
    {
        // A benchmark that profiled a different table than it claims to time would be meaningless: the full profile sees
        // every row and column, and the padded wide table reports a constant line length.
        var wide = ThroughputBench.Run(BenchShape.PaddedWide, BenchPass.Full, SmallBytes, TimeSpan.FromMinutes(5));
        Assert.Equal(wide.Rows, wide.TableRows);
        Assert.Equal(ThroughputBench.WideColumns, wide.Fields);
        Assert.True(wide.ConstantLineLength);
        Assert.Equal(wide.Rows * ThroughputBench.WideColumns, wide.Cells);

        var plain = ThroughputBench.Run(BenchShape.Plain, BenchPass.Full, SmallBytes, TimeSpan.FromMinutes(5));
        Assert.Equal(plain.Rows, plain.TableRows);
        Assert.Equal(ThroughputBench.PlainColumns, plain.Fields);
    }

    private void RunSmall(BenchShape shape)
    {
        foreach (var pass in Enum.GetValues<BenchPass>())
        {
            var result = ThroughputBench.Run(shape, pass, SmallBytes, TimeSpan.FromMinutes(5));
            output.WriteLine(ThroughputBench.Describe(result));
            ThroughputBench.AssertRan(result);
        }
    }

    // The reading of the source alone is the reference for the generator's own cost; the byte pass and the full profile are
    // asserted against their ceilings (the full profile's from the accumulators' peak estimate, the byte pass's the bare
    // allowance, as it keeps no accumulators).
    private void RunAsserted(BenchShape shape, long bytes, TimeSpan timeout)
    {
        foreach (var pass in Enum.GetValues<BenchPass>())
        {
            var result = ThroughputBench.Run(shape, pass, bytes, timeout);
            output.WriteLine(ThroughputBench.Describe(result));
            if (pass != BenchPass.Drain)
            {
                ThroughputBench.AssertWithinCeiling(result);
            }
        }
    }
}
