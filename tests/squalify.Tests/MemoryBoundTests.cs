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
        Assert.True(result.PeakWorkingSet > 0, "the probe could not read its peak working set");

        // Several columns have more than 10,000 distinct values, so the sketches were exercised.
        Assert.True(result.Approximate > 0, "no field crossed the distinct cap");
        Assert.True(result.EstimatedBytes > 0);
        Assert.Equal(MemoryBudget.CeilingBytes(result.EstimatedBytes), result.CeilingBytes);
        Assert.True(
            result.PeakWorkingSet <= result.CeilingBytes,
            $"peak working set {result.PeakWorkingSet} bytes is above the ceiling {result.CeilingBytes} (estimated {result.EstimatedBytes} + allowance {MemoryBudget.AllowanceBytes}); {result.Seconds}s");
    }

    [Fact]
    [Trait("Category", "Large")]
    public void Memory_4GbStream_PeakWorkingSetBounded()
    {
        var result = MemoryProbe.RunChild(4L * 1024 * 1024 * 1024, Columns, TimeSpan.FromHours(2));

        Report(result);
        Assert.True(result.PeakWorkingSet > 0, "the probe could not read its peak working set");
        Assert.True(result.Approximate > 0);
        Assert.True(
            result.PeakWorkingSet <= result.CeilingBytes,
            $"peak working set {result.PeakWorkingSet} bytes is above the ceiling {result.CeilingBytes} (estimated {result.EstimatedBytes} + allowance {MemoryBudget.AllowanceBytes}); {result.Seconds}s");
    }

    private void Report(MemoryProbe.Result result)
    {
        output.WriteLine(
            $"peak working set {result.PeakWorkingSet / 1e6:F1} MB, estimated {result.EstimatedBytes / 1e6:F1} MB, ceiling {result.CeilingBytes / 1e6:F1} MB, {result.Seconds} s, {result.Approximate} of {result.Fields} fields approximate");
    }
}
