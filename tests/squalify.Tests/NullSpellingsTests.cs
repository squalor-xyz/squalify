// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// sq-36: <c>table.fields[].nullSpellings</c> counts the null values by the exact spelling in the file (the field text
/// after dialect unquoting, never trimmed), over all data records, next to <c>nullTokens</c>, which is unchanged; <c>table.rowClasses.allNullRowSpellings</c> counts the all-null records by their uniform spelling.
/// </summary>
public sealed class NullSpellingsTests
{
    private static readonly FieldCaps Caps = new(10_000, 10_000, 64, 100_000);

    private static Descriptor ProfileText(string text, bool includeValues = false)
    {
        var source = new BytesSource("t.csv", Encoding.UTF8.GetBytes(text));
        return Profiler.Profile(source, ProfileOptions.Create(includeValues, Array.Empty<string>()));
    }

    private static FieldProfile Field(Descriptor d, string name)
    {
        return d.Table!.Fields.Single(f => string.Equals(f.Name, name, StringComparison.Ordinal));
    }

    private static (string Key, long Value)[] Pairs(CountMap? map)
    {
        return map!.Select(static e => (e.Key, e.Value)).ToArray();
    }

    private static JsonElement WrittenField(Descriptor d, string name)
    {
        using var document = JsonDocument.Parse(DescriptorWriter.ToBytes(d));
        return document.RootElement.GetProperty("table").GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == name).Clone();
    }

    // ---- The spec's scenarios -------------------------------------------------------------------------------------------

    [Fact]
    public void ExactSpellings_EachHaveTheirOwnCount_AndNullTokensIsUnchanged()
    {
        // NULL three times (one of them quoted: the dialect removes the quotes), Null, null and " NULL " once each.
        var d = ProfileText("A(id),V\n1,NULL\n2,Null\n3,null\n4, NULL \n5,NULL\n6,\"NULL\"\n7,x\n");
        var v = Field(d, "V");

        Assert.Equal(new[] { (" NULL ", 1L), ("NULL", 3L), ("Null", 1L), ("null", 1L) }, Pairs(v.NullSpellings));
        Assert.Equal(new[] { ("null", 6L) }, Pairs(v.NullTokens));
        Assert.Equal(6, v.Nulls);
        Assert.Equal(0, v.NullSpellingsOther);
    }

    [Fact]
    public void SeparatorRows_AreCountedInTheSpellings_AndTheCountsAddUpToNulls()
    {
        var text = new StringBuilder("A(id),B,C\n");
        text.Append("1,NULL,y\n2,null,NULL\nNULL,NULL,NULL\n3,x,NULL\nnull,NULL,Null\n4,NA,NULL\n");
        var d = ProfileText(text.ToString());
        Assert.Equal(2, d.Table!.RowClasses!.AllNullRows);

        foreach (var f in d.Table.Fields)
        {
            Assert.Equal(f.Nulls, f.NullSpellings!.Sum(static e => e.Value) + f.NullSpellingsOther);
        }

        // The all-null rows (NULL,NULL,NULL and null,NULL,Null) are in the spellings.
        Assert.Equal(new[] { ("NULL", 1L), ("null", 1L) }, Pairs(Field(d, "A(id)").NullSpellings));
        Assert.Equal(new[] { ("NA", 1L), ("NULL", 3L), ("null", 1L) }, Pairs(Field(d, "B").NullSpellings));
        Assert.Equal(new[] { ("NULL", 4L), ("Null", 1L) }, Pairs(Field(d, "C").NullSpellings));
        // nullTokens is unchanged: it counted the separator rows already.
        Assert.Equal(new[] { ("NA", 1L), ("null", 4L) }, Pairs(Field(d, "B").NullTokens));
    }

    [Fact]
    public void LabSweepFixture_SpellingsMatchNullTokens_AndTheSeparatorsAreTheirOwnMap()
    {
        var d = Profiler.Profile(TestBytes.Fixture("consumer/lab-sweep.csv"), ProfileOptions.Create(false, Array.Empty<string>()));
        var rc = d.Table!.RowClasses!;
        Assert.Equal(3, rc.AllNullRows);

        foreach (var f in d.Table.Fields)
        {
            Assert.Equal(f.Nulls, f.NullSpellings!.Sum(static e => e.Value) + f.NullSpellingsOther);
            Assert.Equal(0, f.NullSpellingsOther);
            // Every null of this file is spelled NULL, so the spelling count is the token count.
            Assert.Equal(f.NullTokens!.SingleOrDefault(static e => e.Key == "null").Value, f.NullSpellings!.SingleOrDefault(static e => e.Key == "NULL").Value);
        }

        Assert.Equal(new[] { ("NULL", 3L) }, Pairs(rc.AllNullRowSpellings));
        Assert.Equal(0, rc.AllNullRowSpellingsMixed);
        Assert.Equal(0, rc.AllNullRowSpellingsOther);
        Assert.Equal(3, Field(d, "RowID(id)").NullSpellings!.Single().Value);
        Assert.Equal(10, Field(d, "Err").NullSpellings!.Single().Value);
    }

    [Fact]
    public void AllNullRowSpellings_CountTheUniformSpellingOfEachSeparatorRow_AndTheMixedOnes()
    {
        // Separators: NULL x3 (one quoted), NA x2, the empty row (,,) x1, " NULL " x1, and two mixed rows.
        var d = ProfileText(
            "A(id),B,C\n1,x,y\nNULL,NULL,NULL\n2,x,y\nNA,NA,NA\n3,x,y\n,,\n4,x,y\n\"NULL\",NULL,\"NULL\"\nNA,NA,NA\nNULL, NULL , NULL \n NULL , NULL , NULL \nNULL,NA,NULL\nnull,null,Null\n5,x,y\nNULL,NULL,NULL\n");
        var rc = d.Table!.RowClasses!;
        Assert.Equal(10, rc.AllNullRows);
        Assert.Equal(new[] { ("", 1L), (" NULL ", 1L), ("NA", 2L), ("NULL", 3L) }, Pairs(rc.AllNullRowSpellings));
        Assert.Equal(3, rc.AllNullRowSpellingsMixed);
        Assert.Equal(0, rc.AllNullRowSpellingsOther);
        Assert.Equal(rc.AllNullRows, rc.AllNullRowSpellings!.Sum(static e => e.Value) + rc.AllNullRowSpellingsMixed + rc.AllNullRowSpellingsOther);

        // Written always, also with no separator row.
        var none = ProfileText("A(id),B\n1,x\n2,y\n");
        using var doc = JsonDocument.Parse(DescriptorWriter.ToBytes(none));
        var written = doc.RootElement.GetProperty("table").GetProperty("rowClasses");
        Assert.Equal("{}", written.GetProperty("allNullRowSpellings").GetRawText());
        Assert.Equal(0, written.GetProperty("allNullRowSpellingsMixed").GetInt64());
        Assert.Equal(0, written.GetProperty("allNullRowSpellingsOther").GetInt64());
        var names = written.EnumerateObject().Select(static p => p.Name).ToArray();
        Assert.Equal(new[] { "count", "transitions", "allNullRows", "allNullRowSpellings", "allNullRowSpellingsMixed", "allNullRowSpellingsOther", "allNullRowSpellingsOtherDistinct", "truncated", "classes" }, names);
    }

    [Fact]
    public void AllNullRowSpellings_KeepTheFirst64_AndLongSpellingsGoToOther()
    {
        var accumulator = new RowClassAccumulator(2, 64);
        for (var i = 0; i < 70; i++)
        {
            accumulator.AddAllNullSpelling(new string(' ', i) + "NA");
        }

        accumulator.AddAllNullSpelling(new string(' ', 300) + "NA");
        accumulator.AddAllNullSpelling("NA");
        accumulator.AddAllNullMixed();
        var info = accumulator.Build();
        Assert.Equal(64, info.AllNullRowSpellings!.Count);
        Assert.Equal(7, info.AllNullRowSpellingsOther);
        Assert.Equal(1, info.AllNullRowSpellingsMixed);
        Assert.Equal(2, info.AllNullRowSpellings.Single(static e => e.Key == "NA").Value);
    }

    [Fact]
    public void RowClassAccumulator_EstimatedBytes_CountsTheSpellingsHeld()
    {
        var accumulator = new RowClassAccumulator(2, 64);
        var empty = accumulator.EstimatedBytes;
        for (var i = 0; i < 64; i++)
        {
            accumulator.AddAllNullSpelling(new string(' ', i) + "-");
        }

        var held = accumulator.EstimatedBytes;
        Assert.True(held >= empty + (Enumerable.Range(0, 64).Sum(static i => i + 1) * 2L) + (64 * 40L), $"{held} against {empty}");

        // Past the cap only the remembered hashes of the dropped spellings grow (sq-43), up to their own cap of 64.
        for (var i = 0; i < 100; i++)
        {
            accumulator.AddAllNullSpelling("x" + i.ToString(CultureInfo.InvariantCulture));
        }

        var withHashes = accumulator.EstimatedBytes;
        Assert.True(withHashes > held);
        for (var i = 100; i < 1_100; i++)
        {
            accumulator.AddAllNullSpelling("x" + i.ToString(CultureInfo.InvariantCulture));
        }

        Assert.Equal(withHashes, accumulator.EstimatedBytes);
    }

    [Fact]
    public void ValuesThatDifferOnlyInSurroundingWhiteSpace_AreDistinctSpellings()
    {
        // Quoted so that the dialect keeps every character: "", " ", "  ", "NA", " NA", "NA ", a tab and a no-break space around NA.
        var d = ProfileText("A(id),V\n1,\"\"\n2,\" \"\n3,\"  \"\n4,\"NA\"\n5,\" NA\"\n6,\"NA \"\n7,\"\tNA\"\n8,\"\u00A0NA\"\n9,\"NA\"\n10,x\n");
        var v = Field(d, "V");

        Assert.Equal(
            new[] { ("", 1L), ("  ", 1L), (" ", 1L), (" NA", 1L), ("NA", 2L), ("NA ", 1L), ("\tNA", 1L), ("\u00A0NA", 1L) }.OrderBy(static p => p.Item1, StringComparer.Ordinal).ToArray(),
            Pairs(v.NullSpellings));

        // Classification trims, so nullTokens still has one key per vocabulary entry.
        Assert.Equal(new[] { ("", 3L), ("NA", 6L) }, Pairs(v.NullTokens));
    }

    [Fact]
    public void TheEmptyField_IsASpellingWithTheEmptyKey()
    {
        var d = ProfileText("A(id),V\n1,\n2,x\n3,\n4,y\n");
        Assert.Equal(new[] { ("", 2L) }, Pairs(Field(d, "V").NullSpellings));
    }

    [Fact]
    public void KeysAreOrdinal()
    {
        var d = ProfileText("A(id),V\n1,null\n2,NULL\n3,Null\n4,NA\n5,na\n6,N/A\n7,-\n8,NaN\n9,x\n");
        var keys = Field(d, "V").NullSpellings!.Select(static e => e.Key).ToArray();
        Assert.Equal(keys.OrderBy(static k => k, StringComparer.Ordinal).ToArray(), keys);
        Assert.Equal(new[] { "-", "N/A", "NA", "NULL", "NaN", "Null", "na", "null" }, keys);
    }

    // ---- No change when there are no nulls ------------------------------------------------------------------------------

    [Fact]
    public void NoNulls_LeavesBothMembersOut_AndTheModelHoldsAnEmptyMap()
    {
        var d = ProfileText("A(id),V\n1,x\n2,y\n");
        var v = Field(d, "V");
        Assert.Empty(v.NullSpellings!);
        Assert.Equal(0, v.NullSpellingsOther);

        // Absent means none: no "nullSpellings": {} and no "nullSpellingsOther": 0 in the bytes.
        var written = WrittenField(d, "V");
        Assert.False(written.TryGetProperty("nullSpellings", out _));
        Assert.False(written.TryGetProperty("nullSpellingsOther", out _));
        Assert.Equal(JsonValueKind.Object, written.GetProperty("nullTokens").ValueKind);
    }

    [Fact]
    public void ANullOnlyInSeparatorRows_HasItsSpelling_AndTheTableMapHasTheRow()
    {
        var d = ProfileText("A(id),B\n1,x\nNULL,NULL\n2,y\n");
        Assert.Equal(new[] { ("NULL", 1L) }, Pairs(Field(d, "A(id)").NullSpellings));
        Assert.Equal(1, Field(d, "A(id)").Nulls);
        Assert.Equal(new[] { ("NULL", 1L) }, Pairs(d.Table!.RowClasses!.AllNullRowSpellings));
    }

    [Fact]
    public void AddText_CountsTheSpellingOfANull_AndAnInvariantHoldsForTextInput()
    {
        var accumulator = new FieldAccumulator(Caps);
        foreach (var text in new[] { "NULL", " NULL ", "x", "", "null", "NULL" })
        {
            accumulator.AddText(text);
        }

        var profile = accumulator.Build(0, "V", null, 0);
        Assert.Equal(new[] { ("", 1L), (" NULL ", 1L), ("NULL", 2L), ("null", 1L) }, Pairs(profile.NullSpellings));
        Assert.Equal(profile.Nulls, profile.NullSpellings!.Sum(static e => e.Value) + profile.NullSpellingsOther);
    }

    [Fact]
    public void Written_NullSpellingsFollowsNullTokens_AndNullTokensIsUnchanged()
    {
        var d = ProfileText("A(id),V\n1,NULL\n2,Null\n3,x\n");
        var names = WrittenField(d, "V").EnumerateObject().Select(static p => p.Name).ToList();
        Assert.Equal(names.IndexOf("nullTokens") + 1, names.IndexOf("nullSpellings"));
        Assert.Equal("{ \"null\": 2 }", WrittenField(d, "V").GetProperty("nullTokens").GetRawText().Replace("\n", " ", StringComparison.Ordinal).Replace("  ", " ", StringComparison.Ordinal));
        Assert.Equal(2, WrittenField(d, "V").GetProperty("nullSpellings").EnumerateObject().Count());
    }

    [Fact]
    public void Spellings_AreWrittenWithoutTheValuesTier_LikeNullTokens()
    {
        var plain = ProfileText("A(id),V\n1,Null\n2,x\n");
        var withValues = ProfileText("A(id),V\n1,Null\n2,x\n", includeValues: true);
        Assert.Equal(1, WrittenField(plain, "V").GetProperty("nullSpellings").GetProperty("Null").GetInt64());
        Assert.Equal(1, WrittenField(withValues, "V").GetProperty("nullSpellings").GetProperty("Null").GetInt64());
    }

    [Fact]
    public void Fingerprints_DoNotDependOnTheSpellings()
    {
        var upper = ProfileText("A(id),V\n1,NULL\n2,NULL\n3,x\n");
        var mixed = ProfileText("A(id),V\n1,NULL\n2,null\n3,x\n");
        Assert.Equal(upper.Fingerprint, mixed.Fingerprint);
    }

    // ---- The bound --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Accumulator_KeepsTheFirstSpellingsUpToTheCap_AndCountsTheRestInOther()
    {
        var accumulator = new FieldAccumulator(Caps);
        var total = 0;
        for (var i = 0; i < 50; i++)
        {
            foreach (var token in new[] { "NULL", "NA" })
            {
                accumulator.AddNullSpelling(new string(' ', i) + token);
                total++;
            }
        }

        // One spelling again after the cap: still counted under its key.
        accumulator.AddNullSpelling("NULL");
        accumulator.AddNullSpelling("NULL");
        total += 2;

        var profile = accumulator.Build(0, "V", null, total);
        Assert.Equal(FieldAccumulator.MaxNullSpellings, profile.NullSpellings!.Count);
        Assert.Equal(100 - FieldAccumulator.MaxNullSpellings, profile.NullSpellingsOther);
        Assert.Equal(total, profile.NullSpellings.Sum(static e => e.Value) + profile.NullSpellingsOther);
        Assert.Equal(3, profile.NullSpellings.Single(static e => e.Key == "NULL").Value);
    }

    [Fact]
    public void Accumulator_CountsASpellingLongerThanTheLimitInOther()
    {
        var accumulator = new FieldAccumulator(Caps);
        accumulator.AddNullSpelling(new string(' ', FieldAccumulator.MaxNullSpellingLength - 4) + "NULL");
        accumulator.AddNullSpelling(new string(' ', FieldAccumulator.MaxNullSpellingLength - 3) + "NULL");
        accumulator.AddNullSpelling(new string(' ', 5000) + "NULL");
        accumulator.AddNullSpelling(new string(' ', 5000) + "NULL");

        var profile = accumulator.Build(0, "V", null, 4);
        var kept = Assert.Single(profile.NullSpellings!);
        Assert.Equal(FieldAccumulator.MaxNullSpellingLength, kept.Key.Length);
        Assert.Equal(3, profile.NullSpellingsOther);
    }

    [Fact]
    public void Profile_PastTheCap_StillAddsUpToNulls()
    {
        var text = new StringBuilder("A(id),V\n");
        for (var i = 0; i < 100; i++)
        {
            text.Append(i.ToString(CultureInfo.InvariantCulture)).Append(",\"").Append(new string(' ', i)).Append("NA\"\n");
        }

        text.Append("NA,NA\n");
        var d = ProfileText(text.ToString());
        var v = Field(d, "V");

        // All 100 lengths are within the length bound, so the count bound decides: the first 64 spellings are kept.
        Assert.Equal(FieldAccumulator.MaxNullSpellings, v.NullSpellings!.Count);
        Assert.Equal(100 - FieldAccumulator.MaxNullSpellings, v.NullSpellingsOther);
        Assert.Equal(101, v.Nulls);
        Assert.Equal(v.Nulls, v.NullSpellings.Sum(static e => e.Value) + v.NullSpellingsOther);
        Assert.Equal(100 - FieldAccumulator.MaxNullSpellings, WrittenField(d, "V").GetProperty("nullSpellingsOther").GetInt64());
    }

    [Fact]
    public void Blocks_BothInvariantsHoldOverTheProfiledBlocksOnly()
    {
        const string Head = "Freq(MHz),Pout(dBm),Gain(dB)\n";
        const string FirstRows = "700,10.0,15.0\nNULL,NULL,NULL\n800,null,15.2\n900,NA,15.3\nNA,NA,NA\n1000,10.5, NULL \n";
        // A repeat of the header with three rows under it is not a block (a short run): it stays data of the block before it.
        const string NotABlock = Head + "NULL,NULL,NULL\n1,2,3\n4,5,6\n";
        const string SecondRows = "700,10.0,15.0\n,,\n800,Null,15.2\nNULL,NA,NULL\n900,NA,15.3\n1000,10.5,15.4\n";
        // A block with another header is found but not profiled: its nulls count nowhere.
        const string Third = "Site=C\nTemp(C)=45\nVgs(V),Id(A),Gm(S)\nNULL,NULL,NULL\nNULL,NULL,NULL\n1,2,3\n4,5,6\n7,8,9\n,,\n";
        var d = ProfileText("# export\nSite=A\nTemp(C)=25\n" + Head + FirstRows + "Site=B\nTemp(C)=35\n" + Head + SecondRows + NotABlock + Third);

        Assert.Equal(3, ((DelimitedDialect)d.Format.Dialect!).Blocks);
        Assert.Contains(d.Issues, static i => i.Code == "heterogeneousBlocks");
        var rc = d.Table!.RowClasses!;

        // Profiled: block A (6 records) and block B (6, and the 4 of the short run); the third block is not.
        Assert.Equal(16, d.Table.Rows);
        Assert.Equal(5, rc.AllNullRows);
        Assert.Equal(new[] { ("", 1L), ("NA", 1L), ("NULL", 2L) }, Pairs(rc.AllNullRowSpellings));
        Assert.Equal(1, rc.AllNullRowSpellingsMixed);
        Assert.Equal(0, rc.AllNullRowSpellingsOther);
        Assert.Equal(rc.AllNullRows, rc.AllNullRowSpellings!.Sum(static e => e.Value) + rc.AllNullRowSpellingsMixed + rc.AllNullRowSpellingsOther);
        Assert.Equal(5, Field(d, "Freq(MHz)").Nulls);
        Assert.All(d.Table.Fields, static f => Assert.Equal(f.Nulls, f.NullSpellings!.Sum(static e => e.Value) + f.NullSpellingsOther));
    }

    [Fact]
    public void EstimatedBytes_CountsTheSpellingsHeld()
    {
        var accumulator = new FieldAccumulator(Caps);
        var empty = accumulator.EstimatedBytes;
        for (var i = 0; i < FieldAccumulator.MaxNullSpellings; i++)
        {
            accumulator.AddNullSpelling(new string(' ', i) + "-");
        }

        var held = accumulator.EstimatedBytes;
        var chars = Enumerable.Range(0, FieldAccumulator.MaxNullSpellings).Sum(static i => i + 1);
        Assert.True(held >= empty + (chars * 2L) + (FieldAccumulator.MaxNullSpellings * 40L), $"{held} bytes against {empty} when empty");

        // Past the cap only the remembered hashes of the dropped spellings grow (sq-43), up to their own cap of 64.
        for (var i = 0; i < 100; i++)
        {
            accumulator.AddNullSpelling("x" + i.ToString(CultureInfo.InvariantCulture));
        }

        var withHashes = accumulator.EstimatedBytes;
        Assert.True(withHashes > held);
        for (var i = 100; i < 1_100; i++)
        {
            accumulator.AddNullSpelling("x" + i.ToString(CultureInfo.InvariantCulture));
        }

        Assert.Equal(withHashes, accumulator.EstimatedBytes);
    }
}
