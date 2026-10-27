// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class TopKTests
{
    private static void Add(TopK top, string value, int times = 1)
    {
        var hash = FieldAccumulator.Hash(value);
        for (var i = 0; i < times; i++)
        {
            top.Add(hash, value);
        }
    }

    [Fact]
    public void TopK_ExactOrderingAndTies()
    {
        // Counts: c 7; A, a and b 5 each (a tie, ordered by ordinal value: "A" < "a" < "b"); then the rest by count.
        var counts = new (string Value, int Count)[] { ("b", 5), ("a", 5), ("c", 7), ("z", 1), ("A", 5), ("m", 2) };
        var top = new TopK(10_000);
        foreach (var (value, count) in counts)
        {
            Add(top, value, count);
        }

        var result = top.Build();
        Assert.False(result.Approximate);
        Assert.Null(result.MaxError);
        Assert.Equal(
            new[] { ("c", 7L), ("A", 5L), ("a", 5L), ("b", 5L), ("m", 2L), ("z", 1L) },
            result.Items.Select(static i => (i.Value, i.Count)).ToArray());

        // The order of arrival does not matter.
        var reversed = new TopK(10_000);
        foreach (var (value, count) in counts.Reverse())
        {
            Add(reversed, value, count);
        }

        Assert.Equal(result, reversed.Build(), TopValuesComparer.Instance);

        // Only the first K items are kept; ties at the cut-off are cut by ordinal value.
        var wide = new TopK(10_000);
        for (var i = 0; i < 12; i++)
        {
            Add(wide, "v" + i.ToString("00", CultureInfo.InvariantCulture), 3);
        }

        var ten = wide.Build().Items;
        Assert.Equal(TopK.K, ten.Count);
        Assert.Equal("v00", ten[0].Value);
        Assert.Equal("v09", ten[^1].Value);
    }

    [Fact]
    public void TopK_ExactUntilTheCapIsExceeded()
    {
        var atCap = new TopK(5, counters: 4);
        for (var i = 0; i < 5; i++)
        {
            Add(atCap, "k" + i, i + 1);
        }

        Assert.False(atCap.Build().Approximate);

        var past = new TopK(5, counters: 4);
        for (var i = 0; i < 6; i++)
        {
            Add(past, "k" + i, i + 1);
        }

        var result = past.Build();
        Assert.True(result.Approximate);
        Assert.NotNull(result.MaxError);
    }

    [Fact]
    public void TopK_SpaceSaving_ErrorBoundRespected()
    {
        // 100 distinct values with a skewed, deterministic stream; 16 counters; the exact phase ends at 20 values.
        const int counters = 16;
        var top = new TopK(20, counters);
        var truth = new Dictionary<string, long>(StringComparer.Ordinal);
        ulong state = 12345;
        long total = 0;
        for (var n = 0; n < 20_000; n++)
        {
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            var r = (int)((state >> 33) % 1000);
            var key = r < 400 ? r % 3 : r % 100;
            var value = "v" + key.ToString("000", CultureInfo.InvariantCulture);
            truth[value] = truth.GetValueOrDefault(value) + 1;
            total++;
            Add(top, value);
        }

        var result = top.Build();
        Assert.True(result.Approximate);
        var maxError = Assert.IsType<long>(result.MaxError);
        Assert.True(maxError <= total / counters, "maxError " + maxError + " exceeds N/counters " + (total / counters));
        Assert.Equal(TopK.K, result.Items.Count);
        foreach (var item in result.Items)
        {
            var actual = truth[item.Value];
            Assert.True(item.Count >= actual, item.Value + ": the estimate may not be below the truth");
            Assert.True(item.Count - actual <= maxError, item.Value + ": the estimate is off by more than maxError");
        }

        // The three heaviest values are always found, and listed in count order, then ordinal value.
        var heaviest = truth.OrderByDescending(static e => e.Value).ThenBy(static e => e.Key, StringComparer.Ordinal).Take(3).Select(static e => e.Key).ToArray();
        Assert.All(heaviest, h => Assert.Contains(result.Items, i => i.Value == h));
        for (var i = 1; i < result.Items.Count; i++)
        {
            var (a, b) = (result.Items[i - 1], result.Items[i]);
            Assert.True(a.Count > b.Count || (a.Count == b.Count && string.CompareOrdinal(a.Value, b.Value) < 0));
        }
    }

    [Fact]
    public void TopK_SpaceSaving_IsDeterministicWhateverTheTies()
    {
        // A stream of all-distinct values makes every eviction a tie; two runs must agree exactly.
        TopValues Run()
        {
            var top = new TopK(10, 12);
            for (var i = 0; i < 500; i++)
            {
                Add(top, "u" + (i * 7919 % 500).ToString("000", CultureInfo.InvariantCulture));
            }

            return top.Build();
        }

        var result = Run();
        Assert.Equal(result, Run(), TopValuesComparer.Instance);

        // Pinned: every eviction was a tie, settled by lowest count and then lowest ordinal value.
        Assert.True(result.Approximate);
        Assert.Equal(41L, result.MaxError);
        Assert.Equal(
            new[]
            {
                ("u067", 42L), ("u081", 42L), ("u148", 42L), ("u162", 42L), ("u243", 42L), ("u324", 42L), ("u405", 42L), ("u486", 42L),
                ("u377", 41L), ("u391", 41L),
            },
            result.Items.Select(static i => (i.Value, i.Count)).ToArray());
    }

    private sealed class TopValuesComparer : IEqualityComparer<TopValues>
    {
        public static readonly TopValuesComparer Instance = new();

        public bool Equals(TopValues? x, TopValues? y)
        {
            return x is not null && y is not null
                && x.Approximate == y.Approximate
                && x.MaxError == y.MaxError
                && x.Items.SequenceEqual(y.Items);
        }

        public int GetHashCode(TopValues obj) => obj.Items.Count;
    }
}
