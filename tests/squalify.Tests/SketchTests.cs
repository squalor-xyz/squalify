// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class SketchTests
{
    private const double ThreeSigma = 3 * 1.04 / 128;

    // A deterministic stream of 64-bit values: xorshift64* from a fixed seed.
    private static IEnumerable<ulong> Stream(ulong seed, long count)
    {
        var state = seed;
        for (long i = 0; i < count; i++)
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            yield return state * 0x2545F4914F6CDD1DUL;
        }
    }

    private static ulong HashOf(long i)
    {
        return XxHash64.HashString("value-" + i.ToString(CultureInfo.InvariantCulture));
    }

    private static HyperLogLog Filled(IEnumerable<long> ids)
    {
        var hll = new HyperLogLog();
        foreach (var id in ids)
        {
            hll.Add(HashOf(id));
        }

        return hll;
    }

    [Fact]
    public void Hll_ErrorWithin3Sigma_On1MDistinct()
    {
        foreach (var n in new long[] { 100_000, 1_000_000 })
        {
            var estimate = Filled(LongRange(0, n)).Estimate();
            var error = Math.Abs(estimate - n) / (double)n;
            Assert.True(error <= ThreeSigma, $"n={n} estimate={estimate} relative error {error:G6} exceeds {ThreeSigma:G6}");
        }

        // The error bound the descriptor reports is one standard error, 1.04 / sqrt(m) with m = 2^14.
        Assert.Equal(1.04 / 128.0, HyperLogLog.ErrorBound);
    }

    [Fact]
    public void Hll_SameSetDifferentOrder_SameEstimate()
    {
        const long N = 200_000;
        var forward = Filled(LongRange(0, N));
        var backward = Filled(LongRange(0, N).Reverse());
        var shuffled = Filled(LongRange(0, N).OrderBy(static i => HashOf(i + 7)));
        var repeated = Filled(LongRange(0, N).Concat(LongRange(0, N / 2)).Concat(LongRange(0, N)));
        Assert.Equal(forward.Estimate(), backward.Estimate());
        Assert.Equal(forward.Estimate(), shuffled.Estimate());
        Assert.Equal(forward.Estimate(), repeated.Estimate());
    }

    [Fact]
    public void Hll_SmallRange_LinearCounting()
    {
        Assert.Equal(0, new HyperLogLog().Estimate());
        const double M = HyperLogLog.RegisterCount;
        foreach (var n in new long[] { 1, 2, 10, 100, 1_000, 5_000, 10_000 })
        {
            var hll = Filled(LongRange(0, n));
            var estimate = hll.Estimate();

            // Below 2.5 m the estimate is m * ln(m / V), V being the empty registers, rounded half to even.
            var expected = (long)Math.Round(M * DetMath.Ln(M / hll.EmptyRegisters), MidpointRounding.ToEven);
            Assert.Equal(expected, estimate);
            Assert.True(Math.Abs(estimate - n) <= Math.Max(1, n * 0.01), $"n={n} estimate={estimate}");
        }

        // One value is counted as one, however often it repeats.
        var one = new HyperLogLog();
        for (var i = 0; i < 50; i++)
        {
            one.Add(HashOf(3));
        }

        Assert.Equal(1, one.Estimate());
    }

    [Fact]
    public void Gk_RankErrorWithinEpsilonN()
    {
        const long N = 10_000_000;
        var truth = new long[N];
        var sketch = new GreenwaldKhanna();
        var at = 0;
        foreach (var x in Stream(88172645463325252UL, N))
        {
            var key = (long)(x >> 2);
            truth[at++] = key;
            sketch.Add(key);
        }

        Array.Sort(truth);
        Assert.Equal(N, sketch.Count);
        var allowed = N / GreenwaldKhanna.InverseEpsilon;
        foreach (var percent in new long[] { 1, 5, 25, 50, 75, 95, 99 })
        {
            var rank = Math.Max(1, ((percent * N) + 99) / 100);
            var key = sketch.Query(rank);
            var first = LowerBound(truth, key) + 1;
            var last = LowerBound(truth, key + 1);
            Assert.True(first - allowed <= rank && rank <= last + allowed, $"p{percent}: rank {rank}, the value spans {first}..{last}, allowed error {allowed}");
        }

        // Space stays far below the stream: the summary is a few thousand tuples, not millions.
        Assert.True(sketch.EstimatedBytes < 4 * 1024 * 1024, $"{sketch.EstimatedBytes} bytes");
    }

    [Fact]
    public void Sketch_Goldens_SameBytesOnAllOs()
    {
        var text = new StringBuilder();
        foreach (var n in new long[] { 1, 7, 100, 3_000, 40_000, 90_000, 400_000 })
        {
            text.Append(string.Create(CultureInfo.InvariantCulture, $"hll n={n} estimate={Filled(LongRange(0, n)).Estimate()}\n"));
        }

        foreach (var n in new long[] { 10, 5_000, 120_000, 1_500_000 })
        {
            var sketch = new GreenwaldKhanna();
            foreach (var x in Stream(0x9E3779B97F4A7C15UL, n))
            {
                sketch.Add((long)(x >> 20));
            }

            text.Append(string.Create(CultureInfo.InvariantCulture, $"gk n={n}"));
            foreach (var percent in new long[] { 1, 5, 25, 50, 75, 95, 99 })
            {
                var rank = Math.Max(1, ((percent * n) + 99) / 100);
                text.Append(string.Create(CultureInfo.InvariantCulture, $" p{percent}={sketch.Query(rank)}"));
            }

            text.Append('\n');
        }

        Golden.VerifyText("sketches/hll-gk.golden.txt", text.ToString());

        // The descriptor of a generated table past every cap: the same bytes on every OS, in the golden.
        var descriptor = Profiler.Profile(new SyntheticCsvSource(130_000, 8), ProfileOptions.Create(false, []));
        Golden.Verify("sketches/synthetic-130k-8.csv", descriptor);
    }

    private static IEnumerable<long> LongRange(long start, long count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return start + i;
        }
    }

    // The number of values in the sorted array that are below the key.
    private static long LowerBound(long[] sorted, long key)
    {
        long low = 0;
        long high = sorted.Length;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (sorted[mid] < key)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
