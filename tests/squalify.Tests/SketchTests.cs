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
        Assert.Equal(1.04 / 128.0, HyperLogLog.RelativeStdError);
    }

    [Fact]
    public void Hll_ErrorNearTheSwitchToTheRawEstimate_StaysUnder1p5Percent()
    {
        // The raw estimate alone runs about 2.8 percent high at 41,000 values. Linear counting up to 3.5 m (57,344
        // values) keeps the whole range from 30,000 to 120,000 values inside 1.5 percent; the worst case measured is
        // 1.4 percent, near 56,500 values.
        var hll = new HyperLogLog();
        double worst = 0;
        for (long i = 1; i <= 120_000; i++)
        {
            hll.Add(HashOf(i));
            if (i >= 30_000 && i % 500 == 0)
            {
                worst = Math.Max(worst, Math.Abs(hll.Estimate() - i) / (double)i);
            }
        }

        Assert.True(worst <= 0.015, $"worst relative error {worst:G4}");

        // Pinned at 41,000 values, where the raw estimate alone was 2.8 percent off.
        var at41k = Filled(LongRange(1, 41_000));
        Assert.True(Math.Abs(at41k.Estimate() - 41_000) <= 41_000 * 0.01, $"estimate {at41k.Estimate()}");
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

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(13)]
    public void Hll_BelowTheDefaultPrecision_StaysWithin3Sigma_AndHoldsItsRegisters(int precision)
    {
        var hll = new HyperLogLog(precision);
        Assert.Equal(precision, hll.PrecisionBits);
        Assert.Equal((1L << precision) + 64, hll.EstimatedBytes);
        Assert.Equal(1.04 / Math.Sqrt(1 << precision), hll.StdError);
        Assert.Equal(hll.StdError, HyperLogLog.RelativeStdErrorOf(precision));

        // Over the range of linear counting and of the raw estimate, at every fifth power of the register count.
        var m = 1L << precision;
        double worst = 0;
        long added = 0;
        foreach (var n in new[] { m / 4, m, 3 * m, 10 * m, 100 * m, 2_000 * m })
        {
            if (n > 400_000)
            {
                break;
            }

            for (; added < n; added++)
            {
                hll.Add(HashOf(added));
            }

            worst = Math.Max(worst, Math.Abs(hll.Estimate() - n) / (double)n);
        }

        Assert.True(worst <= 3 * hll.StdError + 0.02, $"precision {precision}: worst relative error {worst:G4}, one sigma {hll.StdError:G4}");
    }

    [Fact]
    public void Hll_AtAScaledPrecision_SameSetDifferentOrder_SameEstimate_AndSmallRangeIsLinearCounting()
    {
        const int P = 10;
        var forward = new HyperLogLog(P);
        var backward = new HyperLogLog(P);
        foreach (var id in LongRange(0, 50_000))
        {
            forward.Add(HashOf(id));
        }

        foreach (var id in LongRange(0, 50_000).Reverse().Concat(LongRange(0, 10_000)))
        {
            backward.Add(HashOf(id));
        }

        Assert.Equal(forward.Estimate(), backward.Estimate());

        // Below 3.5 m the estimate is m * ln(m / V) for the empty registers V, with this sketch's own m.
        var small = new HyperLogLog(P);
        foreach (var id in LongRange(0, 300))
        {
            small.Add(HashOf(id));
        }

        var expected = (long)Math.Round(1024.0 * DetMath.Ln(1024.0 / small.EmptyRegisters), MidpointRounding.ToEven);
        Assert.Equal(expected, small.Estimate());
    }

    [Fact]
    public void Hll_PrecisionOutsideTheSupportedRange_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyperLogLog(HyperLogLog.MinPrecision - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HyperLogLog(HyperLogLog.Precision + 1));
        Assert.Equal(HyperLogLog.Precision, new HyperLogLog().PrecisionBits);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(500)]
    [InlineData(250)]
    [InlineData(125)]
    [InlineData(64)]
    [InlineData(32)]
    [InlineData(16)]
    public void Gk_ScaledEpsilon_KeepsItsRankError_AndItsNominalSize(int inverseEpsilon)
    {
        const int N = 200_000;
        var sketch = new GreenwaldKhanna(inverseEpsilon);
        var state = 99UL;
        var keys = new long[N];
        for (var i = 0; i < N; i++)
        {
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            keys[i] = (long)(state >> 30);
            sketch.Add(keys[i]);
        }

        var sorted = keys.OrderBy(static k => k).ToArray();
        var allowed = N / inverseEpsilon;
        foreach (var rank in new long[] { 1, N / 100, N / 4, N / 2, (3 * N) / 4, (99 * N) / 100, N })
        {
            var answer = sketch.Query(rank);
            var low = Array.BinarySearch(sorted, answer);
            while (low > 0 && sorted[low - 1] == answer)
            {
                low--;
            }

            var high = low;
            while (high + 1 < N && sorted[high + 1] == answer)
            {
                high++;
            }

            var distance = rank < low + 1 ? low + 1 - rank : rank > high + 1 ? rank - (high + 1) : 0;
            Assert.True(distance <= allowed, $"1/eps {inverseEpsilon}, rank {rank}: off by {distance}, allowed {allowed}");
        }

        Assert.Equal(1.0 / inverseEpsilon, GreenwaldKhanna.EpsilonOf(inverseEpsilon));
        Assert.True(sketch.EstimatedBytes <= GreenwaldKhanna.NominalBytes(inverseEpsilon), $"{sketch.EstimatedBytes} bytes, nominal {GreenwaldKhanna.NominalBytes(inverseEpsilon)}");
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(250)]
    [InlineData(64)]
    [InlineData(16)]
    public void Gk_NominalBytes_HoldsForSortedAndShuffledKeys_OverALongStream(int inverseEpsilon)
    {
        // Keys that are mostly distinct, in the three orders a column comes in. (Keys with few distinct values hold more;
        // see the remark on GreenwaldKhanna.NominalBytes.)
        foreach (var order in new[] { "ascending", "descending", "shuffled" })
        {
            var sketch = new GreenwaldKhanna(inverseEpsilon);
            var state = 5UL;
            long peak = 0;
            const long N = 1_000_000;
            for (long i = 0; i < N; i++)
            {
                state = (state * 6364136223846793005UL) + 1442695040888963407UL;
                sketch.Add(order == "ascending" ? i : order == "descending" ? N - i : (long)(state >> 30));
                if ((i & 8191) == 0)
                {
                    peak = Math.Max(peak, sketch.EstimatedBytes);
                }
            }

            peak = Math.Max(peak, sketch.EstimatedBytes);
            Assert.True(peak <= GreenwaldKhanna.NominalBytes(inverseEpsilon), $"{order}, 1/eps {inverseEpsilon}: {peak} bytes, nominal {GreenwaldKhanna.NominalBytes(inverseEpsilon)}");
        }
    }

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(14)]
    public void Hll_RawEstimate_UsesTheConstantOfItsOwnRegisterCount(int precision)
    {
        // One hash per register with rank 1 (the first bit after the index is set): no register is empty, so the raw
        // estimate alpha * m^2 / sum(2^-M) = alpha * m * 2 is used, with alpha = 0.7213 / (1 + 1.079 / m).
        var hll = new HyperLogLog(precision);
        var m = 1UL << precision;
        for (ulong index = 0; index < m; index++)
        {
            hll.Add((index << (64 - precision)) | (1UL << (63 - precision)));
        }

        Assert.Equal(0, hll.EmptyRegisters);
        var expected = (long)Math.Round(0.7213 / (1.0 + (1.079 / m)) * m * 2.0, MidpointRounding.ToEven);
        Assert.Equal(expected, hll.Estimate());
    }
}
