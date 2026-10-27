// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class MemoryAccountingTests
{
    private static readonly FieldCaps Caps = new(50_000, 50_000, 64, 1_000_000);

    [Fact]
    public void CeilingBytes_IsTheEstimatePlusTheAllowance()
    {
        Assert.Equal(64L * 1024 * 1024, MemoryBudget.AllowanceBytes);
        Assert.Equal(MemoryBudget.AllowanceBytes, MemoryBudget.CeilingBytes(0));
        Assert.Equal(12_345 + MemoryBudget.AllowanceBytes, MemoryBudget.CeilingBytes(12_345));
    }

    [Fact]
    public void EstimatedBytes_GrowsWithBufferedValues_AndDropsWhenTheBufferIsFreed()
    {
        var accumulator = new FieldAccumulator(Caps);
        var empty = accumulator.EstimatedBytes;
        for (var i = 0; i < 1_000; i++)
        {
            accumulator.AddText((100_000 + i).ToString(CultureInfo.InvariantCulture));
        }

        var thousand = accumulator.EstimatedBytes;
        for (var i = 1_000; i < 40_000; i++)
        {
            accumulator.AddText((100_000 + i).ToString(CultureInfo.InvariantCulture));
        }

        var atFortyThousand = accumulator.EstimatedBytes;

        // 40,000 distinct integers held exactly: 8 bytes each in the quantile buffer, and the distinct set on top.
        Assert.True(thousand > empty);
        Assert.True(atFortyThousand > thousand + (40_000 * 8), $"{atFortyThousand} bytes");

        for (var i = 40_000; i < 50_001; i++)
        {
            accumulator.AddText((100_000 + i).ToString(CultureInfo.InvariantCulture));
        }

        // Past both caps the buffers are freed: a 16 KiB sketch and a summary of a few thousand tuples remain.
        var past = accumulator.EstimatedBytes;
        Assert.True(past < atFortyThousand / 10, $"{past} bytes after the caps, {atFortyThousand} before");
    }

    [Fact]
    public void EstimatedBytes_CountsShapeMasks_AndTopKValueText()
    {
        var plain = new FieldAccumulator(Caps);
        var withTop = new FieldAccumulator(Caps, '.', FieldValueAccess.All);
        for (var i = 0; i < 500; i++)
        {
            var value = "value-" + new string('x', 200) + i.ToString(CultureInfo.InvariantCulture);
            plain.AddText(value);
            withTop.AddText(value);
        }

        // The top-k counters keep the text of each value (2 bytes a character); the plain accumulator does not.
        Assert.True(withTop.EstimatedBytes > plain.EstimatedBytes + (500 * 2 * 200), $"{withTop.EstimatedBytes} against {plain.EstimatedBytes}");
    }

    [Fact]
    public void TopK_PastTheCap_StillHoldsUpTo1000CountersAndCountsTheirText()
    {
        // The exact cap is 5, but Space-Saving keeps up to 1,000 counters: the estimate must follow them.
        var top = new TopK(5);
        var small = top.EstimatedBytes;
        for (var i = 0; i < 3_000; i++)
        {
            var text = "v" + i.ToString(CultureInfo.InvariantCulture);
            top.Add(FieldAccumulator.Hash(text), text);
        }

        var held = top.EstimatedBytes;
        Assert.True(held > small + (900 * 200), $"{held} bytes");

        // More values do not grow it: the counters are replaced, and each replacement swaps one text for another.
        for (var i = 3_000; i < 6_000; i++)
        {
            var text = "v" + i.ToString(CultureInfo.InvariantCulture);
            top.Add(FieldAccumulator.Hash(text), text);
        }

        Assert.True(top.EstimatedBytes <= held + 1_000 * 2 * 2, $"{top.EstimatedBytes} bytes");
    }

    [Fact]
    public void ShapeMasks_AreBoundedByAPerFieldCharacterBudget()
    {
        // The budget is divided by the field count like the distinct and quantile budgets.
        var nominal = Limits.Default;
        Assert.Equal(16_000_000 / 2000, MemoryBudget.Effective(nominal, 2000).ShapeChars);
        Assert.Equal(53_312, MemoryBudget.Effective(nominal, 1).ShapeChars);
        Assert.Equal(53_312, MemoryBudget.Effective(nominal, 300).ShapeChars);
        Assert.Equal(53_156, MemoryBudget.Effective(nominal, 301).ShapeChars);
        Assert.Equal(8_000, MemoryBudget.Record(nominal, MemoryBudget.Effective(nominal, 2000)).ShapeCharsPerField);

        // Masks "9", "a", "A" use 3 characters; "9{2}" (4 characters) would reach 7, over a budget of 6.
        var accumulator = new FieldAccumulator(new FieldCaps(1_000, 1_000, 64, 6));
        foreach (var value in new[] { "1", "a", "A", "11", "1", "22", "b" })
        {
            accumulator.AddText(value);
        }

        var shapes = accumulator.Build(0, "f", null, 0).Shapes!;
        Assert.Equal(["9:2", "a:2", "A:1", "other:2"], shapes.Select(static s => s.Mask + ":" + s.Count.ToString(CultureInfo.InvariantCulture)).ToArray());

        // A wide table of long, varied masks stays inside its budget however many values arrive.
        var wide = new FieldAccumulator(new FieldCaps(1_000, 1_000, 64, 200));
        for (var i = 0; i < 5_000; i++)
        {
            wide.AddText(new string('a', 1 + (i % 40)) + "-" + new string('1', 1 + (i % 7)));
        }

        var total = wide.Build(0, "f", null, 0).Shapes!.Where(static s => s.Mask != "other").Sum(static s => s.Mask.Length);
        Assert.True(total <= 200, $"{total} characters of masks");
    }

    [Fact]
    public void Profile_RecordsThePeakOfTheEstimatedBytes()
    {
        var trace = new MemoryTrace();
        var builder = new StringBuilder("id,v\n");
        for (var i = 0; i < 5_000; i++)
        {
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',').Append((i * 7 % 1000).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        Profiler.Profile(new BytesSource("t.csv", Encoding.UTF8.GetBytes(builder.ToString())), ProfileOptions.Create(false, []), ProfileLimits.Default, trace);
        Assert.True(trace.PeakEstimatedBytes > 5_000 * 8, $"{trace.PeakEstimatedBytes} bytes");
        Assert.Equal(trace.PeakEstimatedBytes + MemoryBudget.AllowanceBytes, trace.CeilingBytes);
    }

    [Fact]
    public void Distinct_PastTheCap_IsTheSketchOfTheExactSet()
    {
        var caps = new FieldCaps(100, 1_000_000, 64, 1_000_000);
        var accumulator = new FieldAccumulator(caps);
        var sketch = new HyperLogLog();
        for (var i = 0; i < 5_000; i++)
        {
            var text = "id-" + (i % 3_000).ToString(CultureInfo.InvariantCulture);
            accumulator.AddText(text);
            sketch.Add(FieldAccumulator.Hash(text));
        }

        // The accumulator moved its exact set into the sketch at the cap and went on adding to it.
        Assert.Equal(sketch.Estimate(), accumulator.Build(0, "f", null, 0).Distinct!.Value);
    }

    [Fact]
    public void ChunkedBuffer_KeepsOrderAcrossChunks_AndCountsItsChunks()
    {
        var buffer = new ChunkedBuffer<long>();
        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.ToArray());
        var empty = buffer.EstimatedBytes;
        for (long i = 0; i < 10_000; i++)
        {
            buffer.Add(i * 3);
        }

        Assert.Equal(10_000, buffer.Count);
        Assert.Equal(Enumerable.Range(0, 10_000).Select(static i => i * 3L).ToArray(), buffer.ToArray());
        Assert.Equal(buffer.ToArray(), buffer.InOrder().ToArray());

        // Three chunks of 4,096 eight-byte values, each under the large object heap limit.
        Assert.InRange(buffer.EstimatedBytes - empty, 3 * 4_096 * 8, (3 * 4_096 * 8) + 1_000);
    }

    [Fact]
    public void Gk_KeysOfDoubles_OrderLikeDoubles()
    {
        var values = new[] { double.NegativeInfinity, -1e300, -2.5, -1.0, -1e-300, -0.0, 0.0, 1e-300, 1.0, 2.5, 1e300, double.PositiveInfinity };
        var keys = values.Select(GreenwaldKhanna.KeyOfDouble).ToArray();
        for (var i = 1; i < keys.Length; i++)
        {
            Assert.True(keys[i - 1] < keys[i], $"{values[i - 1]} and {values[i]}");
        }

        foreach (var value in values)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(GreenwaldKhanna.DoubleOfKey(GreenwaldKhanna.KeyOfDouble(value))));
        }
    }

    [Fact]
    public void Gk_FewKeys_AreExact_AndMapKeysKeepsTheOrder()
    {
        var sketch = new GreenwaldKhanna();
        foreach (var key in new long[] { 50, 10, 40, 20, 60, 30, 50, 70 })
        {
            sketch.Add(key);
        }

        // Fewer than 500 keys are never compressed (the merge threshold is below 1): every rank is exact.
        Assert.Equal(8, sketch.Count);
        Assert.Equal(10, sketch.Query(1));
        Assert.Equal(20, sketch.Query(2));
        Assert.Equal(50, sketch.Query(5));
        Assert.Equal(50, sketch.Query(6));
        Assert.Equal(70, sketch.Query(8));

        sketch.MapKeys(static key => key * 2);
        Assert.Equal(20, sketch.Query(1));
        Assert.Equal(100, sketch.Query(6));
        Assert.Equal(140, sketch.Query(8));
        Assert.Throws<InvalidOperationException>(() => new GreenwaldKhanna().Query(1));
    }

    [Fact]
    public void Gk_SameKeysInTheSameOrder_GiveTheSameSummary()
    {
        static GreenwaldKhanna Build()
        {
            var sketch = new GreenwaldKhanna();
            var state = 12345UL;
            for (var i = 0; i < 200_000; i++)
            {
                state = (state * 6364136223846793005UL) + 1442695040888963407UL;
                sketch.Add((long)(state >> 40));
            }

            return sketch;
        }

        var a = Build();
        var b = Build();
        Assert.Equal(a.TupleCount, b.TupleCount);
        for (var rank = 1L; rank <= 200_000; rank += 7_919)
        {
            Assert.Equal(a.Query(rank), b.Query(rank));
        }
    }

    [Fact]
    public void Gk_SummaryHoldsAboutThirtyKilobytes()
    {
        // The merge works in a rented array and the summary trims its own after each merge: it held 124 KB before.
        var sketch = new GreenwaldKhanna();
        var state = 7UL;
        for (var i = 0; i < 1_000_000; i++)
        {
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            sketch.Add((long)(state >> 30));
        }

        Assert.True(sketch.EstimatedBytes < 40_000, $"{sketch.EstimatedBytes} bytes");
    }
}
