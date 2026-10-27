// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class RowClassTests
{
    private const string Header = "RowID(id),Mode,Freq(MHz),Pout(dBm),Err\n";
    private const string Setup = "1,CW,NULL,NULL,NULL\n";
    private const string Measure = "2,CW,2400,1.5,NULL\n";
    private const string Separator = "NULL,NULL,NULL,NULL,NULL\n";
    private const string Error = "3,NULL,NULL,NULL,E01\n";

    // Line numbers are those of the file: the header is line 1. The rows are
    //  2 S, 3 M, 4 M, 5 Z, 6 S, 7 M, 8 M, 9 M, 10 Z, 11 S, 12 M, 13 E, 14 M, 15 Z
    private static readonly string[] Interleaved =
    [
        Setup, Measure, Measure, Separator,
        Setup, Measure, Measure, Measure, Separator,
        Setup, Measure, Error, Measure, Separator,
    ];

    private static ProfileOptions Options() => ProfileOptions.Create(false, Array.Empty<string>());

    private static Descriptor ProfileText(string text, ProfileLimits? limits = null)
    {
        var source = new BytesSource("t.csv", Encoding.UTF8.GetBytes(text));
        return limits is null ? Profiler.Profile(source, Options()) : Profiler.Profile(source, Options(), limits);
    }

    private static string Id(params int[] nonNull)
    {
        var text = string.Join(",", nonNull.Select(static i => i.ToString(CultureInfo.InvariantCulture)));
        return "rc:" + XxHash64.Hash(Encoding.UTF8.GetBytes(text)).ToString("x16", CultureInfo.InvariantCulture);
    }

    private static RowClassesInfo ClassesOf(Descriptor d)
    {
        return Assert.IsType<RowClassesInfo>(d.Table!.RowClasses);
    }

    private static long[][] Ranges(RowClass c)
    {
        return c.Fields.Select(static r => new[] { r.First, r.Last }).ToArray();
    }

    [Fact]
    public void RowClasses_Interleaved()
    {
        var d = ProfileText(Header + string.Concat(Interleaved));
        var rc = ClassesOf(d);
        Assert.Equal(14, d.Table!.Rows);
        Assert.Equal(4, rc.Count);
        Assert.Equal(10, rc.Transitions);
        Assert.Equal(3, rc.AllNullRows);
        Assert.Equal(0, rc.Truncated);

        var measure = rc.Classes.Single(static c => c.Id == Id(0, 1, 2, 3));
        Assert.Equal(7, measure.Count);
        Assert.Equal(new[] { new long[] { 0, 3 } }, Ranges(measure));
        Assert.Equal((3, 14), (measure.FirstLine, measure.LastLine));

        var setup = rc.Classes.Single(static c => c.Id == Id(0, 1));
        Assert.Equal(3, setup.Count);
        Assert.Equal(new[] { new long[] { 0, 1 } }, Ranges(setup));
        Assert.Equal((2, 11), (setup.FirstLine, setup.LastLine));

        var separator = rc.Classes.Single(static c => c.Id == Id());
        Assert.Equal(3, separator.Count);
        Assert.Empty(separator.Fields);
        Assert.Equal((5, 15), (separator.FirstLine, separator.LastLine));

        var error = rc.Classes.Single(static c => c.Id == Id(0, 4));
        Assert.Equal(1, error.Count);
        Assert.Equal(new[] { new long[] { 0, 0 }, new long[] { 4, 4 } }, Ranges(error));
        Assert.Equal((13, 13), (error.FirstLine, error.LastLine));

        // Sorted by count descending, then id ordinally (the setup and separator classes tie at 3).
        var tied = new[] { Id(0, 1), Id() }.OrderBy(static s => s, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { Id(0, 1, 2, 3), tied[0], tied[1], Id(0, 4) }, rc.Classes.Select(static c => c.Id).ToArray());
        Assert.Equal(d.Table.Rows, rc.Classes.Sum(static c => c.Count) + rc.Truncated);
    }

    [Fact]
    public void RowClasses_AllNullRows_CountedAndExcludedFromFieldNulls()
    {
        var d = ProfileText(Header + string.Concat(Interleaved));
        var fields = d.Table!.Fields;
        Assert.Equal(new long[] { 3, 4, 7, 7, 13 }, fields.Select(static f => f.Nulls).ToArray());
        Assert.Equal(new long[] { 0, 1, 4, 4, 10 }, fields.Select(static f => f.NullsExcludingAllNullRows).ToArray());
        Assert.Equal(3, ClassesOf(d).AllNullRows);
        Assert.All(fields, static f => Assert.Equal(3, f.Nulls - f.NullsExcludingAllNullRows));

        // No separator rows: nothing to exclude.
        var plain = ProfileText(Header + Setup + Measure + Error + Measure);
        Assert.Equal(0, ClassesOf(plain).AllNullRows);
        Assert.All(plain.Table!.Fields, static f => Assert.Equal(f.Nulls, f.NullsExcludingAllNullRows));
    }

    [Fact]
    public void RowClasses_Cap_TruncatedCounted()
    {
        // Classes by first appearance: A (id), B (id, mode), then C (id, freq) and D (id, err) are past a cap of 2.
        const string A = "1,NULL,NULL,NULL,NULL\n";
        const string B = "1,CW,NULL,NULL,NULL\n";
        const string C = "1,NULL,2400,NULL,NULL\n";
        const string D = "1,NULL,NULL,NULL,E01\n";
        var rows = new[] { A, A, B, C, C, A, D, C, B, A };
        var limits = ProfileLimits.Default with { RowClasses = 2 };
        var d = ProfileText(Header + string.Concat(rows), limits);
        var rc = ClassesOf(d);
        Assert.Equal(2, d.Limits.RowClasses);
        Assert.Equal(2, rc.Count);
        Assert.Equal(4, rc.Truncated);
        Assert.Equal(10, d.Table!.Rows);

        // The first classes seen are kept, with exact counts, and a record of a kept class after a truncated one still counts.
        var a = rc.Classes.Single(static c => c.Id == Id(0));
        var b = rc.Classes.Single(static c => c.Id == Id(0, 1));
        Assert.Equal(4, a.Count);
        Assert.Equal((2, 11), (a.FirstLine, a.LastLine));
        Assert.Equal(2, b.Count);
        Assert.Equal((4, 10), (b.FirstLine, b.LastLine));
        Assert.Equal(new[] { a.Id, b.Id }, rc.Classes.Select(static c => c.Id).ToArray());

        // A to B, B to C, C to A, A to D, D to C, C to B, B to A are transitions; A to A and C to C are not.
        Assert.Equal(7, rc.Transitions);
        Assert.Equal(10, rc.Classes.Sum(static c => c.Count) + rc.Truncated);

        // At the default cap the same file keeps all four.
        var all = ClassesOf(ProfileText(Header + string.Concat(rows)));
        Assert.Equal(4, all.Count);
        Assert.Equal(0, all.Truncated);
        Assert.Equal(7, all.Transitions);
    }

    [Fact]
    public void RowClasses_IdIndependentOfRowOrder()
    {
        var forward = ClassesOf(ProfileText(Header + string.Concat(Interleaved)));
        var reversed = ClassesOf(ProfileText(Header + string.Concat(Interleaved.Reverse())));
        var rotated = ClassesOf(ProfileText(Header + string.Concat(Interleaved.Skip(5).Concat(Interleaved.Take(5)))));
        var expected = forward.Classes.Select(static c => (c.Id, c.Count)).OrderBy(static t => t.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal(4, expected.Length);
        foreach (var other in new[] { reversed, rotated })
        {
            Assert.Equal(expected, other.Classes.Select(static c => (c.Id, c.Count)).OrderBy(static t => t.Id, StringComparer.Ordinal).ToArray());
            Assert.Equal(forward.AllNullRows, other.AllNullRows);
        }
    }

    [Fact]
    public void RowClasses_PlainTable_OneClass()
    {
        var builder = new StringBuilder("a,b,c\n");
        for (var i = 0; i < 20; i++)
        {
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(",x").Append(i % 3).Append(',').Append(i).Append(".5\n");
        }

        var d = ProfileText(builder.ToString());
        var rc = ClassesOf(d);
        Assert.Equal(1, rc.Count);
        Assert.Equal(0, rc.Transitions);
        Assert.Equal(0, rc.AllNullRows);
        Assert.Equal(0, rc.Truncated);
        var only = Assert.Single(rc.Classes);
        Assert.Equal(Id(0, 1, 2), only.Id);
        Assert.Equal(20, only.Count);
        Assert.Equal(new[] { new long[] { 0, 2 } }, Ranges(only));
        Assert.Equal((2, 21), (only.FirstLine, only.LastLine));
    }

    [Fact]
    public void RowClasses_AteShape_SeparatorsCountedApartFromDataRows()
    {
        // Interleaved all-NULL separator rows, as an ATE export writes them: prism maps DataRows to rows minus allNullRows.
        var builder = new StringBuilder("Pout(dBm),RowID(id),Mode,Note\r\n");
        builder.Append("NULL,NULL,NULL,NULL\r\n");
        for (var i = 1; i <= 8; i++)
        {
            builder.Append((10 + i).ToString(CultureInfo.InvariantCulture)).Append(".5,").Append(i.ToString(CultureInfo.InvariantCulture)).Append(",CW,").Append(i % 2 == 0 ? "\"a, b\"" : "ok").Append("\r\n");
            builder.Append("NULL,NULL,NULL,NULL\r\n");
        }

        var d = ProfileText(builder.ToString());
        var rc = ClassesOf(d);
        Assert.Equal(17, d.Table!.Rows);
        Assert.Equal(9, rc.AllNullRows);
        Assert.Equal(8, d.Table.Rows - rc.AllNullRows);
        Assert.Equal(2, rc.Count);
        Assert.Equal(16, rc.Transitions);
        var data = rc.Classes.Single(static c => c.Id == Id(0, 1, 2, 3));
        Assert.Equal(8, data.Count);
        Assert.Equal((3, 17), (data.FirstLine, data.LastLine));
        Assert.Equal(9, rc.Classes.Single(static c => c.Id == Id()).Count);
        Assert.Equal(new long[] { 0, 0, 0, 0 }, d.Table.Fields.Select(static f => f.NullsExcludingAllNullRows).ToArray());
    }

    [Fact]
    public void RowClasses_RaggedRecords_AreInNoClass()
    {
        var builder = new StringBuilder("a,b,c\n");
        for (var i = 1; i <= 12; i++)
        {
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(",x").Append(i % 2).Append(',').Append(i).Append('\n');
            if (i == 6)
            {
                builder.Append("6,x\n");
            }

            if (i == 9)
            {
                builder.Append("9,NULL,9\n");
            }
        }

        var d = ProfileText(builder.ToString());
        var rc = ClassesOf(d);
        Assert.Equal(14, d.Table!.Rows);
        Assert.Contains(d.Issues, static i => i.Code == "raggedRows");
        Assert.Equal(13, rc.Classes.Sum(static c => c.Count));
        Assert.Equal(0, rc.AllNullRows);
        // The ragged record is skipped, so it is not a transition either: the full class switches to {0,2} and back.
        Assert.Equal(2, rc.Transitions);
    }

    [Fact]
    public void RowClassAccumulator_IdIsXxHash64OfTheNonNullIndexes()
    {
        var accumulator = new RowClassAccumulator(12, 64);
        var mask = new bool[12];
        Array.Fill(mask, true);
        mask[1] = false;
        mask[10] = false;
        accumulator.Add(mask, 5);
        var info = accumulator.Build();
        var only = Assert.Single(info.Classes);
        Assert.Equal(Id(1, 10), only.Id);
        Assert.Equal(new[] { new long[] { 1, 1 }, new long[] { 10, 10 } }, Ranges(only));
        Assert.Equal("rc:" + XxHash64.Hash("1,10"u8).ToString("x16", CultureInfo.InvariantCulture), only.Id);

        // A pinned id, recorded from this implementation (the hash itself is pinned by XxHash64Tests): fields 0 to 3.
        var four = new RowClassAccumulator(4, 64);
        four.Add(new bool[4], 1);
        Assert.Equal("rc:f84a18ec7f5e8c9a", Assert.Single(four.Build().Classes).Id);
    }

    [Fact]
    public void RowClassAccumulator_WideRows_DoNotAllocatePerRow()
    {
        const int Width = 2_000;
        var accumulator = new RowClassAccumulator(Width, 64);
        var masks = new bool[3][];
        for (var m = 0; m < masks.Length; m++)
        {
            masks[m] = new bool[Width];
            for (var i = 0; i < Width; i++)
            {
                masks[m][i] = m == 2 || (i + m) % 5 == 0;
            }
        }

        // Warm up: every class is created, so the steady state has nothing left to allocate.
        for (var r = 0; r < 6; r++)
        {
            accumulator.Add(masks[r % 3], r + 2);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var r = 0; r < 30_000; r++)
        {
            accumulator.Add(masks[r % 3], r + 8);
        }

        // The bound is under one byte per row: any per-row allocation is at least 30,000 bytes, while JIT tier-up on
        // this thread during the loop can allocate a few KB once, which made a 1 KiB bound flaky.
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 30_000, "allocated " + allocated.ToString(CultureInfo.InvariantCulture) + " bytes for 30000 rows");
        var info = accumulator.Build();
        Assert.Equal(3, info.Count);
        Assert.Equal(10_002, info.AllNullRows);
        Assert.Equal(30_005, info.Transitions);
    }
}
