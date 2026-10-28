// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// sq-36: <c>table.fields[].nullSpellings</c> counts the null values by the exact spelling in the file (the field text
/// after dialect unquoting, never trimmed), outside all-null records, next to <c>nullTokens</c>, which is unchanged.
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
    public void SeparatorRows_AreLeftOutOfTheSpellings_AndTheCountsAddUpToNullsExcludingAllNullRows()
    {
        var text = new StringBuilder("A(id),B,C\n");
        text.Append("1,NULL,y\n2,null,NULL\nNULL,NULL,NULL\n3,x,NULL\nnull,NULL,Null\n4,NA,NULL\n");
        var d = ProfileText(text.ToString());
        Assert.Equal(2, d.Table!.RowClasses!.AllNullRows);

        foreach (var f in d.Table.Fields)
        {
            Assert.Equal(f.NullsExcludingAllNullRows, f.NullSpellings!.Sum(static e => e.Value) + f.NullSpellingsOther);
            Assert.Equal(f.Nulls - d.Table.RowClasses.AllNullRows, f.NullSpellings!.Sum(static e => e.Value));
        }

        // The two all-null rows (NULL,NULL,NULL and null,NULL,Null) are in no spelling count.
        Assert.Empty(Field(d, "A(id)").NullSpellings!);
        Assert.Equal(new[] { ("NA", 1L), ("NULL", 1L), ("null", 1L) }, Pairs(Field(d, "B").NullSpellings));
        Assert.Equal(new[] { ("NULL", 3L) }, Pairs(Field(d, "C").NullSpellings));
        // nullTokens is unchanged: it still counts the separator rows.
        Assert.Equal(new[] { ("NA", 1L), ("null", 4L) }, Pairs(Field(d, "B").NullTokens));
    }

    [Fact]
    public void SeparatorRows_OfTheLabSweepFixture_AreLeftOut_AndTheNullKeyLessSeparatorsIsTheSpellingCount()
    {
        var d = Profiler.Profile(TestBytes.Fixture("consumer/lab-sweep.csv"), ProfileOptions.Create(false, Array.Empty<string>()));
        var all = d.Table!.RowClasses!.AllNullRows;
        Assert.Equal(3, all);

        foreach (var f in d.Table.Fields)
        {
            Assert.Equal(f.NullsExcludingAllNullRows, f.NullSpellings!.Sum(static e => e.Value) + f.NullSpellingsOther);
            Assert.Equal(0, f.NullSpellingsOther);
            // Every null of this file is spelled NULL, so the spelling count is the token count less the separators.
            var token = f.NullTokens!.SingleOrDefault(static e => e.Key == "null").Value;
            var spelling = f.NullSpellings!.SingleOrDefault(static e => e.Key == "NULL").Value;
            Assert.Equal(token - all, spelling);
        }

        // A column that is null only in the separator rows has no spelling at all.
        Assert.Empty(Field(d, "RowID(id)").NullSpellings!);
        Assert.Equal(7, Field(d, "Err").NullSpellings!.Single().Value);
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
    public void ANullOnlyInSeparatorRows_HasNoSpelling()
    {
        var d = ProfileText("A(id),B\n1,x\nNULL,NULL\n2,y\n");
        Assert.Empty(Field(d, "A(id)").NullSpellings!);
        Assert.Equal(1, Field(d, "A(id)").Nulls);
        Assert.Equal(0, Field(d, "A(id)").NullsExcludingAllNullRows);
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
    public void Profile_PastTheCap_StillAddsUpToNullsExcludingAllNullRows()
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
        Assert.Equal(100, v.NullsExcludingAllNullRows);
        Assert.Equal(v.NullsExcludingAllNullRows, v.NullSpellings.Sum(static e => e.Value) + v.NullSpellingsOther);
        Assert.Equal(100 - FieldAccumulator.MaxNullSpellings, WrittenField(d, "V").GetProperty("nullSpellingsOther").GetInt64());
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

        // Past the cap nothing more is held.
        for (var i = 0; i < 1_000; i++)
        {
            accumulator.AddNullSpelling("x" + i.ToString(CultureInfo.InvariantCulture));
        }

        Assert.Equal(held, accumulator.EstimatedBytes);
    }
}
