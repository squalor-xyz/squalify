// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// sq-43: <c>nullSpellingsOtherDistinct</c> and <c>allNullRowSpellingsOtherDistinct</c> count the distinct spellings among
/// the values that <c>nullSpellingsOther</c> and <c>allNullRowSpellingsOther</c> count: each distinct dropped spelling once
/// however often it recurs, exact text (not trimmed), exact up to <see cref="SpellingCounter.MaxDistinctOther"/> (64) further
/// spellings; one more sets the <c>Capped</c> marker. A field leaves the members out when its other count is 0; the table
/// always writes the count, and its marker only when the cap was hit.
/// </summary>
public sealed class NullSpellingsOtherDistinctTests
{
    private static readonly FieldCaps Caps = new(10_000, 10_000, 64, 100_000);

    private const int K = SpellingCounter.MaxDistinctOther;
    private const int Kept = SpellingCounter.MaxSpellings;
    private const int MaxChars = SpellingCounter.MaxLength;

    // A spelling that the vocabulary matches, distinct for each i, at most 256 characters up to i = 253.
    private static string Padded(int i) => new string(' ', i) + "NA";

    // A spelling past the character bound, distinct for each i (the last characters differ).
    private static string Long(int i) => new string(' ', MaxChars + 10) + "NA" + i.ToString(CultureInfo.InvariantCulture);

    private static FieldProfile BuildField(FieldAccumulator accumulator, long nulls)
    {
        return accumulator.Build(0, "V", null, nulls);
    }

    private static FieldProfile FieldOf(IEnumerable<string> spellings)
    {
        var accumulator = new FieldAccumulator(Caps);
        var count = 0L;
        foreach (var spelling in spellings)
        {
            accumulator.AddNullSpelling(spelling);
            count++;
        }

        return BuildField(accumulator, count);
    }

    private static RowClassesInfo RowsOf(IEnumerable<string> spellings)
    {
        var accumulator = new RowClassAccumulator(2, 64);
        foreach (var spelling in spellings)
        {
            accumulator.AddAllNullSpelling(spelling);
        }

        return accumulator.Build();
    }

    // The first Kept spellings fill the map, so everything after them is a dropped spelling.
    private static IEnumerable<string> Fill() => Enumerable.Range(0, Kept).Select(static i => Padded(i));

    private static Descriptor ProfileText(string text)
    {
        return Profiler.Profile(new BytesSource("t.csv", Encoding.UTF8.GetBytes(text)), ProfileOptions.Create(false, Array.Empty<string>()));
    }

    private static JsonElement WrittenField(Descriptor d, string name)
    {
        using var document = JsonDocument.Parse(DescriptorWriter.ToBytes(d));
        return document.RootElement.GetProperty("table").GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == name).Clone();
    }

    private static JsonElement WrittenRowClasses(Descriptor d)
    {
        using var document = JsonDocument.Parse(DescriptorWriter.ToBytes(d));
        return document.RootElement.GetProperty("table").GetProperty("rowClasses").Clone();
    }

    private static string DocsText(string file)
    {
        var path = Path.GetFullPath(Path.Combine(Golden.SourceFixturesRoot(), "..", "..", "..", "docs", file));
        Assert.True(File.Exists(path), "docs/" + file + " not found at " + path);
        return File.ReadAllText(path);
    }

    private static string Section(string text, string heading)
    {
        var start = text.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "no section '" + heading + "'");
        var level = heading.TakeWhile(static c => c == '#').Count();
        var next = Regex.Match(text[(start + heading.Length + 2)..], "\n#{1," + level.ToString(CultureInfo.InvariantCulture) + "} ");
        return next.Success ? text.Substring(start, heading.Length + 2 + next.Index) : text[start..];
    }

    // The accumulator tests feed spellings directly, so the null total is the number fed; a profile of a file reads it from the field.
    private static void AssertFieldInvariants(FieldProfile f, long? nulls = null)
    {
        Assert.Equal(nulls ?? f.Nulls, f.NullSpellings!.Sum(static e => e.Value) + f.NullSpellingsOther);
        Assert.True(f.NullSpellingsOtherDistinct <= f.NullSpellingsOther, "distinct is more than other");
        Assert.True(f.NullSpellingsOther == 0 || f.NullSpellingsOtherDistinct >= 1, "other is positive but distinct is 0");
        Assert.True(f.NullSpellingsOther > 0 || (f.NullSpellingsOtherDistinct == 0 && !f.NullSpellingsOtherDistinctCapped));
    }

    // ---- The constant ---------------------------------------------------------------------------------------------------

    [Fact]
    public void TheCap_IsSixtyFour()
    {
        Assert.Equal(64, K);
    }

    // ---- One long spelling versus many ----------------------------------------------------------------------------------

    [Fact]
    public void OneLongSpelling_ThousandNulls_IsOneDistinctSpelling()
    {
        var spelling = new string(' ', 300) + "NA";
        var f = FieldOf(Enumerable.Repeat(spelling, 1_000));

        Assert.Empty(f.NullSpellings!);
        Assert.Equal(1_000, f.NullSpellingsOther);
        Assert.Equal(1, f.NullSpellingsOtherDistinct);
        Assert.False(f.NullSpellingsOtherDistinctCapped);
        AssertFieldInvariants(f, 1_000);
    }

    [Fact]
    public void ManySpellings_200Distinct_64Kept_136Dropped_ReportsTheCap()
    {
        // 200 distinct spellings, each once; the first 64 are kept, the other 136 are dropped, and 136 is past K = 64.
        var f = FieldOf(Enumerable.Range(0, 200).Select(static i => Padded(i)));

        Assert.Equal(Kept, f.NullSpellings!.Count);
        Assert.Equal(136, f.NullSpellingsOther);
        Assert.Equal(K, f.NullSpellingsOtherDistinct);
        Assert.True(f.NullSpellingsOtherDistinctCapped);
        AssertFieldInvariants(f, 200);
    }

    [Fact]
    public void ManySpellings_100Distinct_36Dropped_IsExactAndNotCapped()
    {
        var f = FieldOf(Enumerable.Range(0, 100).Select(static i => Padded(i)));

        Assert.Equal(Kept, f.NullSpellings!.Count);
        Assert.Equal(36, f.NullSpellingsOther);
        Assert.Equal(36, f.NullSpellingsOtherDistinct);
        Assert.False(f.NullSpellingsOtherDistinctCapped);
        AssertFieldInvariants(f, 100);
    }

    [Fact]
    public void TheCapBoundary_ExactlyKDistinctIsExactNotCapped_OneMoreIsCapped()
    {
        var atCap = FieldOf(Fill().Concat(Enumerable.Range(Kept, K).Select(static i => Padded(i))));
        Assert.Equal(K, atCap.NullSpellingsOther);
        Assert.Equal(K, atCap.NullSpellingsOtherDistinct);
        Assert.False(atCap.NullSpellingsOtherDistinctCapped);

        var past = FieldOf(Fill().Concat(Enumerable.Range(Kept, K + 1).Select(static i => Padded(i))));
        Assert.Equal(K + 1, past.NullSpellingsOther);
        Assert.Equal(K, past.NullSpellingsOtherDistinct);
        Assert.True(past.NullSpellingsOtherDistinctCapped);

        var oneBelow = FieldOf(Fill().Concat(Enumerable.Range(Kept, K - 1).Select(static i => Padded(i))));
        Assert.Equal(K - 1, oneBelow.NullSpellingsOtherDistinct);
        Assert.False(oneBelow.NullSpellingsOtherDistinctCapped);
    }

    [Fact]
    public void AfterTheCap_AKnownDroppedSpellingDoesNotSetTheMarker_AndAFurtherNewOneDoes()
    {
        // K distinct dropped spellings, then only those again: still exact. Then a new one: capped.
        var dropped = Enumerable.Range(Kept, K).Select(static i => Padded(i)).ToArray();
        var again = FieldOf(Fill().Concat(dropped).Concat(dropped).Concat(dropped.Take(10)));
        Assert.Equal(2 * K + 10, again.NullSpellingsOther);
        Assert.Equal(K, again.NullSpellingsOtherDistinct);
        Assert.False(again.NullSpellingsOtherDistinctCapped);

        var more = FieldOf(Fill().Concat(dropped).Concat([Padded(Kept + K)]).Concat(dropped));
        Assert.Equal(K, more.NullSpellingsOtherDistinct);
        Assert.True(more.NullSpellingsOtherDistinctCapped);
    }

    // ---- Recurrence and causes ------------------------------------------------------------------------------------------

    [Fact]
    public void ARecurringDroppedSpelling_CountsOnce()
    {
        // One spelling past the 64 bound, five times, another three times: eight cells, two spellings. A kept one keeps counting under its key.
        var f = FieldOf(Fill().Concat(Enumerable.Repeat(Padded(100), 5)).Concat(Enumerable.Repeat(Padded(101), 3)).Concat([Padded(0), Padded(0)]));

        Assert.Equal(8, f.NullSpellingsOther);
        Assert.Equal(2, f.NullSpellingsOtherDistinct);
        Assert.False(f.NullSpellingsOtherDistinctCapped);
        Assert.Equal(3, f.NullSpellings!.Single(static e => e.Key == Padded(0)).Value);
        AssertFieldInvariants(f, Kept + 10);
    }

    [Fact]
    public void BothCauses_TheCountBoundAndTheLengthBound_EachCountOnce()
    {
        // The map is full: a 65th short spelling (dropped by the 64 bound) and two spellings over 256 characters
        // (dropped by the length bound), each repeated; three distinct spellings.
        var f = FieldOf(Fill().Concat([Padded(100), Padded(100), Long(1), Long(1), Long(1), Long(2)]));
        Assert.Equal(6, f.NullSpellingsOther);
        Assert.Equal(3, f.NullSpellingsOtherDistinct);
        Assert.False(f.NullSpellingsOtherDistinctCapped);
        AssertFieldInvariants(f, Kept + 6);
    }

    [Fact]
    public void ALongSpelling_IsCountedAlsoWhenTheMapHasRoom()
    {
        // Only the length bound drops these: the map holds one spelling, with 63 places free.
        var f = FieldOf(["NULL", Long(1), Long(1), Long(2), "NULL"]);
        Assert.Single(f.NullSpellings!);
        Assert.Equal(3, f.NullSpellingsOther);
        Assert.Equal(2, f.NullSpellingsOtherDistinct);
    }

    [Fact]
    public void Spellings_AreComparedAsExactText_NotTrimmed()
    {
        // " NULL", "NULL" and "NULL " are three spellings (they differ only in white space); all are dropped here by the length bound.
        var pad = new string(' ', MaxChars + 10);
        var f = FieldOf([pad + " NULL", pad + "NULL", pad + "NULL ", pad + "NULL"]);
        Assert.Equal(4, f.NullSpellingsOther);
        Assert.Equal(3, f.NullSpellingsOtherDistinct);

        // The same by the 64 bound.
        // The last spelling starts with U+00A0 (no-break space), not a plain space: it is a trim-set character the vocabulary accepts but another spelling, so it counts as a fifth.
        var byCount = FieldOf(Fill().Concat([" NULL", "NULL", "NULL ", "NULL", "\tNULL", "\u00A0NULL"]));
        Assert.Equal(6, byCount.NullSpellingsOther);
        Assert.Equal(5, byCount.NullSpellingsOtherDistinct);
    }

    [Fact]
    public void LongSpellings_ThatDifferOnlyLateOrInLength_AreDistinct()
    {
        var pad = new string('x', 2_000);
        var f = FieldOf([pad + "a", pad + "b", pad + "aa", pad, pad + "a"]);
        Assert.Equal(5, f.NullSpellingsOther);
        Assert.Equal(4, f.NullSpellingsOtherDistinct);
    }

    [Fact]
    public void Spellings_AreComparedAsUtf16Text_SoLoneSurrogatesDifferFromEachOtherAndFromTheReplacementCharacter()
    {
        // UTF-8 would turn each lone surrogate into U+FFFD; the exact text is what counts.
        var pad = new string(' ', MaxChars + 10);
        var f = FieldOf([pad + "\uD800", pad + "\uD801", pad + "\uFFFD", pad + "\uD800"]);
        Assert.Equal(4, f.NullSpellingsOther);
        Assert.Equal(3, f.NullSpellingsOtherDistinct);
    }

    // ---- The key: XxHash64 of the UTF-16 text, little-endian, with the length ---------------------------------------------

    [Fact]
    public void TheKey_IsTheXxHash64OfTheUtf16LittleEndianBytes_SeededWithTheLength()
    {
        Assert.Equal(XxHash64.Hash([97, 0, 98, 0, 99, 0], 3UL), SpellingCounter.KeyOf("abc".AsSpan()));
        Assert.Equal(XxHash64.Hash(ReadOnlySpan<byte>.Empty, 0UL), SpellingCounter.KeyOf(ReadOnlySpan<char>.Empty));

        // A character above 0xFF: the low byte comes first. A lone surrogate is itself, not U+FFFD.
        Assert.Equal(XxHash64.Hash([0x02, 0x01, 0x00, 0xD8], 2UL), SpellingCounter.KeyOf("\u0102\uD800".AsSpan()));
        Assert.NotEqual(SpellingCounter.KeyOf("\uD800".AsSpan()), SpellingCounter.KeyOf("\uFFFD".AsSpan()));

        // A long text (more than the stack buffer, several 32-byte blocks) goes through the same hash.
        var text = new string('q', 1_000);
        Assert.Equal(XxHash64.Hash(Encoding.Unicode.GetBytes(text), 1_000UL), SpellingCounter.KeyOf(text.AsSpan()));
        var edge = new string('q', 256);
        Assert.Equal(XxHash64.Hash(Encoding.Unicode.GetBytes(edge), 256UL), SpellingCounter.KeyOf(edge.AsSpan()));
        var past = new string('q', 257);
        Assert.Equal(XxHash64.Hash(Encoding.Unicode.GetBytes(past), 257UL), SpellingCounter.KeyOf(past.AsSpan()));

        // The length is mixed in: the same units after a different count differ.
        Assert.NotEqual(SpellingCounter.KeyOf("a".AsSpan()), XxHash64.Hash([97, 0], 0UL));
    }

    [Fact]
    public void TheKey_DoesNotDependOnTheCulture()
    {
        var saved = Thread.CurrentThread.CurrentCulture;
        try
        {
            var invariant = SpellingCounter.KeyOf("Null I i".AsSpan());
            Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal(invariant, SpellingCounter.KeyOf("Null I i".AsSpan()));
            var f = FieldOf(Fill().Concat(["I", "i", "\u0130", "\u0131"]));
            Assert.Equal(4, f.NullSpellingsOtherDistinct);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = saved;
        }
    }

    // ---- The all-null rows ----------------------------------------------------------------------------------------------

    [Fact]
    public void AllNullRows_OneLongSpelling_IsOneDistinct()
    {
        var info = RowsOf(Enumerable.Repeat(new string(' ', 300) + "NA", 1_000));
        Assert.Empty(info.AllNullRowSpellings!);
        Assert.Equal(1_000, info.AllNullRowSpellingsOther);
        Assert.Equal(1, info.AllNullRowSpellingsOtherDistinct);
        Assert.False(info.AllNullRowSpellingsOtherDistinctCapped);
    }

    [Fact]
    public void AllNullRows_200Distinct_IsCapped_AndHundredDistinctIsExact()
    {
        var many = RowsOf(Enumerable.Range(0, 200).Select(static i => Padded(i)));
        Assert.Equal(136, many.AllNullRowSpellingsOther);
        Assert.Equal(K, many.AllNullRowSpellingsOtherDistinct);
        Assert.True(many.AllNullRowSpellingsOtherDistinctCapped);

        var some = RowsOf(Enumerable.Range(0, 100).Select(static i => Padded(i)));
        Assert.Equal(36, some.AllNullRowSpellingsOther);
        Assert.Equal(36, some.AllNullRowSpellingsOtherDistinct);
        Assert.False(some.AllNullRowSpellingsOtherDistinctCapped);
    }

    [Fact]
    public void AllNullRows_TheCapBoundary_Recurrence_AndBothCauses()
    {
        var atCap = RowsOf(Fill().Concat(Enumerable.Range(Kept, K).Select(static i => Padded(i))));
        Assert.Equal(K, atCap.AllNullRowSpellingsOtherDistinct);
        Assert.False(atCap.AllNullRowSpellingsOtherDistinctCapped);

        var past = RowsOf(Fill().Concat(Enumerable.Range(Kept, K + 1).Select(static i => Padded(i))));
        Assert.Equal(K, past.AllNullRowSpellingsOtherDistinct);
        Assert.True(past.AllNullRowSpellingsOtherDistinctCapped);

        var recurring = RowsOf(Fill().Concat([Padded(100), Padded(100), Padded(100), Long(1), Long(1), Long(2), "NA ", "NA  ", "NA "]));
        Assert.Equal(9, recurring.AllNullRowSpellingsOther);
        Assert.Equal(5, recurring.AllNullRowSpellingsOtherDistinct);
        Assert.False(recurring.AllNullRowSpellingsOtherDistinctCapped);
    }

    [Fact]
    public void AllNullRows_MixedRowsAndKeptSpellings_AreNotCountedAsDistinctOther()
    {
        var accumulator = new RowClassAccumulator(2, 64);
        accumulator.AddAllNullSpelling("NA");
        accumulator.AddAllNullSpelling(Long(1));
        accumulator.AddAllNullMixed();
        accumulator.AddAllNullMixed();
        var info = accumulator.Build();
        Assert.Equal(2, info.AllNullRowSpellingsMixed);
        Assert.Equal(1, info.AllNullRowSpellingsOther);
        Assert.Equal(1, info.AllNullRowSpellingsOtherDistinct);
    }

    // ---- Through a profile of a file ------------------------------------------------------------------------------------

    private static string Spelled(int spaces) => "\"" + new string(' ', spaces) + "NA\"";

    [Fact]
    public void Profile_200DistinctSpellingsInAField_AndInTheSeparatorRows_WritesCountsAndMarkers()
    {
        // A field of 200 distinct padded blanks (64 kept, 136 dropped), and 200 all-null records that spell the same 200 ways
        // (every cell of a row is the same text).
        var text = new StringBuilder("A(id),B\n");
        for (var i = 0; i < 200; i++)
        {
            text.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Spelled(i)).Append('\n');
            text.Append(Spelled(i)).Append(',').Append(Spelled(i)).Append('\n');
        }

        var d = ProfileText(text.ToString());
        var b = d.Table!.Fields.Single(static f => f.Name == "B");
        Assert.Equal(Kept, b.NullSpellings!.Count);
        // Every spelling occurs twice (a row of the field, and a separator row), so 136 dropped spellings are 272 cells.
        Assert.Equal(400, b.Nulls);
        Assert.Equal(2 * (200 - Kept), b.NullSpellingsOther);
        Assert.Equal(K, b.NullSpellingsOtherDistinct);
        Assert.True(b.NullSpellingsOtherDistinctCapped);
        AssertFieldInvariants(b);

        var rc = d.Table.RowClasses!;
        Assert.Equal(200, rc.AllNullRows);
        Assert.Equal(200 - Kept, rc.AllNullRowSpellingsOther);
        Assert.Equal(K, rc.AllNullRowSpellingsOtherDistinct);
        Assert.True(rc.AllNullRowSpellingsOtherDistinctCapped);

        var writtenField = WrittenField(d, "B");
        Assert.Equal(K, writtenField.GetProperty("nullSpellingsOtherDistinct").GetInt64());
        Assert.True(writtenField.GetProperty("nullSpellingsOtherDistinctCapped").GetBoolean());
        var writtenRows = WrittenRowClasses(d);
        Assert.Equal(K, writtenRows.GetProperty("allNullRowSpellingsOtherDistinct").GetInt64());
        Assert.True(writtenRows.GetProperty("allNullRowSpellingsOtherDistinctCapped").GetBoolean());
    }

    [Fact]
    public void Profile_OneLongNullSpelling_ThroughAFile_IsOneDistinct_AndNoMarkerIsWritten()
    {
        var text = new StringBuilder("A(id),V\n");
        for (var i = 0; i < 50; i++)
        {
            text.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Spelled(300)).Append('\n');
        }

        text.Append("x,y\n");
        var d = ProfileText(text.ToString());
        var v = d.Table!.Fields.Single(static f => f.Name == "V");
        Assert.Equal(50, v.NullSpellingsOther);
        Assert.Equal(1, v.NullSpellingsOtherDistinct);

        var written = WrittenField(d, "V");
        Assert.Equal(1, written.GetProperty("nullSpellingsOtherDistinct").GetInt64());
        Assert.False(written.TryGetProperty("nullSpellingsOtherDistinctCapped", out _));
        Assert.Equal(["nullSpellingsOther", "nullSpellingsOtherDistinct"], written.EnumerateObject().Select(static p => p.Name).Where(static n => n.StartsWith("nullSpellingsOther", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void F9_ASingleSpelling_IsKeysPlusDistinctOtherEqualsOne()
    {
        // A field whose only spelling is 300 characters long: no key, one dropped spelling.
        var long1 = ProfileText("A(id),V\n1," + Spelled(300) + "\n2," + Spelled(300) + "\n3,x\n").Table!.Fields.Single(static f => f.Name == "V");
        Assert.Equal(1, long1.NullSpellings!.Count + long1.NullSpellingsOtherDistinct);

        // A field with two long spellings, and one with a short and a long one: not single-spelled.
        var long2 = ProfileText("A(id),V\n1," + Spelled(300) + "\n2," + Spelled(301) + "\n3,x\n").Table!.Fields.Single(static f => f.Name == "V");
        Assert.Equal(2, long2.NullSpellings!.Count + long2.NullSpellingsOtherDistinct);
        var mixed = ProfileText("A(id),V\n1,NULL\n2," + Spelled(300) + "\n3,x\n").Table!.Fields.Single(static f => f.Name == "V");
        Assert.Equal(2, mixed.NullSpellings!.Count + mixed.NullSpellingsOtherDistinct);

        // Only a spelling that is a key, with nothing dropped: one key, no distinct other.
        var kept = ProfileText("A(id),V\n1,NULL\n2,NULL\n3,x\n").Table!.Fields.Single(static f => f.Name == "V");
        Assert.Equal(1, kept.NullSpellings!.Count + kept.NullSpellingsOtherDistinct);
    }

    // ---- No spill, no member --------------------------------------------------------------------------------------------

    [Fact]
    public void NoSpill_AFieldWritesNeitherMember_AndTheTableWritesTheRowCountAsZero()
    {
        var d = ProfileText("A(id),V\n1,NULL\n2,Null\n3,x\nNULL,NULL\n");
        var v = d.Table!.Fields.Single(static f => f.Name == "V");
        Assert.Equal(0, v.NullSpellingsOther);
        Assert.Equal(0, v.NullSpellingsOtherDistinct);
        Assert.False(v.NullSpellingsOtherDistinctCapped);

        var written = WrittenField(d, "V");
        Assert.False(written.TryGetProperty("nullSpellingsOtherDistinct", out _));
        Assert.False(written.TryGetProperty("nullSpellingsOtherDistinctCapped", out _));

        var rows = WrittenRowClasses(d);
        Assert.Equal(0, rows.GetProperty("allNullRowSpellingsOtherDistinct").GetInt64());
        Assert.False(rows.TryGetProperty("allNullRowSpellingsOtherDistinctCapped", out _));
        Assert.Equal(
            ["count", "transitions", "allNullRows", "allNullRowSpellings", "allNullRowSpellingsMixed", "allNullRowSpellingsOther", "allNullRowSpellingsOtherDistinct", "truncated", "classes"],
            rows.EnumerateObject().Select(static p => p.Name).ToArray());

        // No data row at all: the member is still written.
        var none = WrittenRowClasses(ProfileText("A(id),B\n"));
        Assert.Equal(0, none.GetProperty("allNullRowSpellingsOtherDistinct").GetInt64());
    }

    [Fact]
    public void Writer_WritesTheMembersFromTheModel_InOrder_AndOmitsThemByTheirRules()
    {
        var plain = TestModels.FullDelimited();
        var field = plain.Table!.Fields[0];
        var rows = plain.Table.RowClasses!;

        JsonElement Field(FieldProfile f)
        {
            using var document = JsonDocument.Parse(DescriptorWriter.ToBytes(plain with { Table = plain.Table with { Fields = [f, .. plain.Table.Fields.Skip(1)] } }));
            return document.RootElement.GetProperty("table").GetProperty("fields")[0].Clone();
        }

        JsonElement Rows(RowClassesInfo r)
        {
            using var document = JsonDocument.Parse(DescriptorWriter.ToBytes(plain with { Table = plain.Table with { RowClasses = r } }));
            return document.RootElement.GetProperty("table").GetProperty("rowClasses").Clone();
        }

        string[] Names(JsonElement e) => e.EnumerateObject().Select(static p => p.Name).Where(static n => n.Contains("Other", StringComparison.Ordinal)).ToArray();

        // A field: the count is left out at 0 (even with the marker false), written when positive; the marker only when true.
        var zero = Field(field with { NullSpellingsOther = 0, NullSpellingsOtherDistinct = 0, NullSpellingsOtherDistinctCapped = false });
        Assert.Empty(Names(zero));
        var counted = Field(field with { NullSpellingsOther = 9, NullSpellingsOtherDistinct = 3 });
        Assert.Equal(["nullSpellingsOther", "nullSpellingsOtherDistinct"], Names(counted));
        Assert.Equal(3, counted.GetProperty("nullSpellingsOtherDistinct").GetInt64());
        var capped = Field(field with { NullSpellingsOther = 900, NullSpellingsOtherDistinct = 64, NullSpellingsOtherDistinctCapped = true });
        Assert.Equal(["nullSpellingsOther", "nullSpellingsOtherDistinct", "nullSpellingsOtherDistinctCapped"], Names(capped));
        Assert.True(capped.GetProperty("nullSpellingsOtherDistinctCapped").GetBoolean());
        var names = capped.EnumerateObject().Select(static p => p.Name).ToList();
        Assert.Equal(names.IndexOf("nullSpellingsOther") + 1, names.IndexOf("nullSpellingsOtherDistinct"));
        Assert.Equal(names.IndexOf("nullSpellingsOtherDistinct") + 1, names.IndexOf("nullSpellingsOtherDistinctCapped"));

        // The table: the count is always written, the marker only when true.
        var rowZero = Rows(rows with { AllNullRowSpellingsOther = 0, AllNullRowSpellingsOtherDistinct = 0, AllNullRowSpellingsOtherDistinctCapped = false });
        Assert.Equal(["allNullRowSpellingsOther", "allNullRowSpellingsOtherDistinct"], Names(rowZero));
        Assert.Equal(0, rowZero.GetProperty("allNullRowSpellingsOtherDistinct").GetInt64());
        var rowCounted = Rows(rows with { AllNullRowSpellingsOther = 9, AllNullRowSpellingsOtherDistinct = 4, AllNullRowSpellingsOtherDistinctCapped = false });
        Assert.Equal(4, rowCounted.GetProperty("allNullRowSpellingsOtherDistinct").GetInt64());
        Assert.False(rowCounted.TryGetProperty("allNullRowSpellingsOtherDistinctCapped", out _));
        var rowCapped = Rows(rows with { AllNullRowSpellingsOther = 900, AllNullRowSpellingsOtherDistinct = 64, AllNullRowSpellingsOtherDistinctCapped = true });
        Assert.Equal(["allNullRowSpellingsOther", "allNullRowSpellingsOtherDistinct", "allNullRowSpellingsOtherDistinctCapped"], Names(rowCapped));
        Assert.Equal(
            ["count", "transitions", "allNullRows", "allNullRowSpellings", "allNullRowSpellingsMixed", "allNullRowSpellingsOther", "allNullRowSpellingsOtherDistinct", "allNullRowSpellingsOtherDistinctCapped", "truncated", "classes"],
            rowCapped.EnumerateObject().Select(static p => p.Name).ToArray());
    }

    // ---- Records --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Records_BuiltWithoutTheNewMembers_Compile_AndReadAsNoSpill()
    {
        // The trailing members are optional, so the sq-36 forms of the constructors still compile.
        var rows = new RowClassesInfo(1, 0, 0, 0, [], null, 0, 5);
        Assert.Equal(0, rows.AllNullRowSpellingsOtherDistinct);
        Assert.False(rows.AllNullRowSpellingsOtherDistinctCapped);
        var field = TestModels.FullDelimited().Table!.Fields[0] with { NullSpellingsOtherDistinct = 0 };
        Assert.False(field.NullSpellingsOtherDistinctCapped);
    }

    [Fact]
    public void Records_CompareByValue_IncludingTheNewMembers()
    {
        var field = TestModels.FullDelimited().Table!.Fields[0];
        Assert.NotEqual(field, field with { NullSpellingsOtherDistinct = 7 });
        Assert.NotEqual(field, field with { NullSpellingsOtherDistinctCapped = true });
        Assert.Equal(field with { NullSpellingsOtherDistinct = 7 }, field with { NullSpellingsOtherDistinct = 7 });
        Assert.Equal((field with { NullSpellingsOtherDistinct = 7 }).GetHashCode(), (field with { NullSpellingsOtherDistinct = 7 }).GetHashCode());

        var rows = new RowClassesInfo(1, 0, 0, 0, []);
        Assert.NotEqual(rows, rows with { AllNullRowSpellingsOtherDistinct = 7 });
        Assert.NotEqual(rows, rows with { AllNullRowSpellingsOtherDistinctCapped = true });
        Assert.Equal(rows with { AllNullRowSpellingsOtherDistinct = 7 }, rows with { AllNullRowSpellingsOtherDistinct = 7 });
        Assert.Equal((rows with { AllNullRowSpellingsOtherDistinct = 7 }).GetHashCode(), (rows with { AllNullRowSpellingsOtherDistinct = 7 }).GetHashCode());
    }

    // ---- limits ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Limits_PublishTheCap_AsMaxDistinctOther_AlwaysWritten_AndItIsTheCountersConstant()
    {
        Descriptor[] descriptors =
        [
            ProfileText("A(id),B\n1,2\n"),
            Profiler.Profile(new BytesSource("t.txt", Encoding.UTF8.GetBytes("just some prose\n")), ProfileOptions.Create(false, Array.Empty<string>())),
            Profiler.Profile(new BytesSource("t.dat", []), ProfileOptions.Create(false, Array.Empty<string>())),
            TestModels.Minimal(),
            TestModels.FullDelimited(),
            TestModels.FullBinaryStdf(),
        ];
        foreach (var descriptor in descriptors)
        {
            using var document = JsonDocument.Parse(DescriptorWriter.ToBytes(descriptor));
            var bounds = document.RootElement.GetProperty("limits").GetProperty("nullSpellings");
            Assert.Equal(["max", "maxChars", "maxDistinctOther"], bounds.EnumerateObject().Select(static p => p.Name).ToArray());
            Assert.Equal(64, bounds.GetProperty("maxDistinctOther").GetInt64());
            Assert.Equal(SpellingCounter.MaxDistinctOther, bounds.GetProperty("maxDistinctOther").GetInt64());
        }

        Assert.Equal(SpellingCounter.MaxDistinctOther, Limits.Default.NullSpellings!.MaxDistinctOther);

        // The writer writes what the model holds, not a fixed number.
        var plain = TestModels.FullDelimited();
        var changed = plain with { Limits = plain.Limits with { NullSpellings = new NullSpellingLimits(7, 11, 5) } };
        using var changedDocument = JsonDocument.Parse(DescriptorWriter.ToBytes(changed));
        Assert.Equal(5, changedDocument.RootElement.GetProperty("limits").GetProperty("nullSpellings").GetProperty("maxDistinctOther").GetInt64());
    }

    [Fact]
    public void Limits_ARecordBuiltWithoutTheMember_ReadsTheDefaultCap()
    {
        var built = new Limits(10000, 100000, 64, 53312, 1000, 1000, 65536, 100000, 64, new CharsetTolerance(8, 65536), 10000, new Budget(10000000, 2000000, 16000000), 1, 1);
        using var document = JsonDocument.Parse(DescriptorWriter.ToBytes(TestModels.Minimal() with { Limits = built }));
        Assert.Equal(64, document.RootElement.GetProperty("limits").GetProperty("nullSpellings").GetProperty("maxDistinctOther").GetInt64());
    }

    // ---- Memory ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void EstimatedBytes_CoversTheRememberedHashes_AndStopsGrowingAtTheCap()
    {
        var accumulator = new FieldAccumulator(Caps);
        foreach (var spelling in Fill())
        {
            accumulator.AddNullSpelling(spelling);
        }

        var full = accumulator.EstimatedBytes;
        for (var i = 0; i < K; i++)
        {
            accumulator.AddNullSpelling(Padded(Kept + i));
        }

        var remembered = accumulator.EstimatedBytes;

        // 64 remembered keys of 8 bytes each at the least (2,000 columns of them are about 1 MB at this floor).
        Assert.True(remembered >= full + (K * 8L), $"{remembered} bytes against {full} before the hashes");

        // Nothing more is held past the cap (new spellings are neither counted nor remembered), nor for a recurrence.
        for (var i = 0; i < 1_000; i++)
        {
            accumulator.AddNullSpelling(Long(i));
            accumulator.AddNullSpelling(Padded(Kept + (i % K)));
        }

        Assert.Equal(remembered, accumulator.EstimatedBytes);
    }

    [Fact]
    public void RowClassAccumulator_EstimatedBytes_CoversTheRememberedHashes()
    {
        var accumulator = new RowClassAccumulator(2, 64);
        foreach (var spelling in Fill())
        {
            accumulator.AddAllNullSpelling(spelling);
        }

        var full = accumulator.EstimatedBytes;
        for (var i = 0; i < K + 100; i++)
        {
            accumulator.AddAllNullSpelling(Padded(Kept + i));
        }

        var remembered = accumulator.EstimatedBytes;
        Assert.True(remembered >= full + (K * 8L), $"{remembered} bytes against {full} before the hashes");
        accumulator.AddAllNullSpelling(Long(1));
        Assert.Equal(remembered, accumulator.EstimatedBytes);
    }

    [Fact]
    public void NoSpill_HoldsNoMoreThanBefore()
    {
        // A counter that never drops a spelling pays nothing for the distinct count.
        var accumulator = new FieldAccumulator(Caps);
        foreach (var spelling in Fill())
        {
            accumulator.AddNullSpelling(spelling);
        }

        var plain = new FieldAccumulator(Caps);
        foreach (var spelling in Fill())
        {
            plain.AddNullSpelling(spelling);
        }

        Assert.Equal(plain.EstimatedBytes, accumulator.EstimatedBytes);
        var empty = new FieldAccumulator(Caps).EstimatedBytes;
        Assert.True(empty < 4_096, $"an empty accumulator estimates {empty} bytes");
    }

    private const int WideFields = 2_000;

    // A table of 2,000 fields in which every cell is a null of a padded spelling, 200 distinct spellings, all rows uniform: every
    // field's counter and the table's all-null counter keep 64 spellings, drop 136 and remember 64 hashes.
    private sealed class SpillingSource : IProfileSource
    {
        private readonly byte[] _bytes;

        public SpillingSource()
        {
            var text = new StringBuilder();
            text.Append("F0(id)");
            for (var c = 1; c < WideFields; c++)
            {
                text.Append(",F").Append(c.ToString(CultureInfo.InvariantCulture));
            }

            text.Append('\n');
            for (var r = 0; r < 200; r++)
            {
                // 8 bits of r as space and tab, then NA: 200 distinct spellings of 10 characters.
                var spelling = new StringBuilder();
                for (var bit = 7; bit >= 0; bit--)
                {
                    spelling.Append(((r >> bit) & 1) == 1 ? '\t' : ' ');
                }

                spelling.Append("NA");
                var cell = "\"" + spelling + "\"";
                for (var c = 0; c < WideFields; c++)
                {
                    text.Append(c == 0 ? string.Empty : ",").Append(cell);
                }

                text.Append('\n');
            }

            _bytes = Encoding.ASCII.GetBytes(text.ToString());
        }

        public string Name => "wide-spilling.csv";

        public long Length => _bytes.Length;

        public Stream Open() => new MemoryStream(_bytes, writable: false);
    }

    [Fact]
    public void WideStream_EveryFieldSpills_StaysInsideTheAllowance_AndEveryFieldIsCappedAtExactlyK()
    {
        long last = 0;
        long peak = 0;
        var trace = new MemoryTrace { Observer = bytes => { last = bytes; peak = Math.Max(peak, bytes); } };
        var descriptor = Profiler.Profile(new SpillingSource(), ProfileOptions.Create(false, []), ProfileLimits.Default, trace);

        var fields = descriptor.Table!.Fields;
        Assert.Equal(WideFields, fields.Count);
        foreach (var field in fields)
        {
            Assert.Equal(Kept, field.NullSpellings!.Count);
            Assert.Equal(200 - Kept, field.NullSpellingsOther);
            Assert.Equal(K, field.NullSpellingsOtherDistinct);
            Assert.True(field.NullSpellingsOtherDistinctCapped);
        }

        Assert.Equal(K, descriptor.Table.RowClasses!.AllNullRowSpellingsOtherDistinct);

        // The summed estimate (hashes included) stays within the allowance of sq-38; the ceiling check of sq-11 is on the same figure.
        Assert.True(peak <= MemoryBudget.SketchBudgetBytes, $"the peak estimate is {peak} bytes, above {MemoryBudget.SketchBudgetBytes}");
        Assert.True(last >= WideFields * K * 8L, $"the estimate of {last} bytes does not cover the {WideFields * K} remembered hashes");
    }

    // ---- Documented -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Documented_DescriptorMd_NamesTheMembers_TheKeyOrder_TheSubset_TheF9Rule_AndThePolicy()
    {
        var descriptor = DocsText("descriptor.md");

        var limits = Section(descriptor, "## `limits`");
        Assert.Contains("\"nullSpellings\": { \"max\": 64, \"maxChars\": 256, \"maxDistinctOther\": 64 }", limits, StringComparison.Ordinal);
        Assert.Contains("`maxDistinctOther` (64)", limits, StringComparison.Ordinal);

        var keyOrder = Section(descriptor, "### Key order of nested objects");
        Assert.Contains("| `limits.nullSpellings` | `max`, `maxChars`, `maxDistinctOther` |", keyOrder, StringComparison.Ordinal);
        var rowRow = keyOrder.Split('\n').Single(static l => l.StartsWith("| `table.rowClasses` |", StringComparison.Ordinal));
        Assert.Matches("`allNullRowSpellingsOther`[^|]*`allNullRowSpellingsOtherDistinct`[^|]*`allNullRowSpellingsOtherDistinctCapped`[^|]*`truncated`", rowRow);
        var fieldRow = keyOrder.Split('\n').Single(static l => l.StartsWith("| `table.fields[]` |", StringComparison.Ordinal));
        Assert.Matches("`nullSpellingsOther`[^|]*`nullSpellingsOtherDistinct`[^|]*`nullSpellingsOtherDistinctCapped`[^|]*`flags`", fieldRow);

        var subset = Section(descriptor, "## Consumer subset");
        Assert.Contains("| `limits.nullSpellings.maxDistinctOther` | S |", subset, StringComparison.Ordinal);
        Assert.Contains("| `table.rowClasses.allNullRowSpellingsOtherDistinct` | S, T |", subset, StringComparison.Ordinal);
        Assert.Contains("| `table.rowClasses.allNullRowSpellingsOtherDistinctCapped` |", subset, StringComparison.Ordinal);
        Assert.Contains("| `table.fields[].nullSpellingsOtherDistinct` | S, T |", subset, StringComparison.Ordinal);
        Assert.Contains("| `table.fields[].nullSpellingsOtherDistinctCapped` |", subset, StringComparison.Ordinal);
        Assert.Contains("keys(nullSpellings) + nullSpellingsOtherDistinct == 1", subset, StringComparison.Ordinal);

        var fields = Section(descriptor, "## `table.fields[]`");
        Assert.Contains("**`nullSpellingsOtherDistinct`**", fields, StringComparison.Ordinal);
        Assert.Contains("collision", fields, StringComparison.Ordinal);

        var rowClasses = Section(descriptor, "## `table.rowClasses`");
        Assert.Contains("**`allNullRowSpellingsOtherDistinct`**", rowClasses, StringComparison.Ordinal);
        Assert.Contains("allNullRowSpellingsOtherDistinctCapped", rowClasses, StringComparison.Ordinal);

        Assert.DoesNotContain("reads \"at least 64\"", descriptor, StringComparison.Ordinal);
        Assert.Contains("more than 64 distinct", fields, StringComparison.Ordinal);
        Assert.Contains("more than 64 distinct", rowClasses, StringComparison.Ordinal);

        var policy = Section(descriptor, "## Versioning policy");
        Assert.Contains("sq-43 adds, again without a bump:", policy, StringComparison.Ordinal);

        var design = DocsText("design.md");
        Assert.Contains("`SpellingCounter.MaxDistinctOther`", design, StringComparison.Ordinal);
        Assert.Contains("collision", design, StringComparison.Ordinal);
    }
}
