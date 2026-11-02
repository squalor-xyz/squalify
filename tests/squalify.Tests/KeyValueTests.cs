// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>sq-09: whole-file key=value and INI documents, and the files that must not become one.</summary>
public sealed class KeyValueTests
{
    private static readonly string[] Fixtures =
    [
        "keyvalue/instrument.ini",
        "keyvalue/station.conf",
    ];

    private static ProfileOptions Options(bool includeValues = false, bool layoutOnly = false, params string[] allow) => ProfileOptions.Create(includeValues, allow, layoutOnly: layoutOnly);

    private static Descriptor Profile(string text, string name = "t.ini", bool includeValues = false, params string[] allow)
    {
        return Profiler.Profile(new BytesSource(name, Encoding.UTF8.GetBytes(text)), Options(includeValues, false, allow));
    }

    private static Descriptor ProfileFixture(string fixture, bool includeValues = false, params string[] allow)
    {
        return Profiler.Profile(TestBytes.Fixture(fixture), Options(includeValues, false, allow));
    }

    private static KeyValueDialect Dialect(Descriptor d)
    {
        Assert.Equal("keyValue", d.Format.Family);
        return Assert.IsType<KeyValueDialect>(d.Format.Dialect);
    }

    [Fact]
    public void KeyValue_IniSections_KeysAlwaysValuesOptIn()
    {
        var d = ProfileFixture("keyvalue/instrument.ini");
        var dialect = Dialect(d);
        Assert.Equal("=", dialect.Separator);
        Assert.True(dialect.HasSections);
        Assert.Equal(0.8, d.Format.Score);

        // Section names, their lines and their keys are always written, in file order; the values are not.
        Assert.Equal(2, d.Sections!.Count);
        Assert.Equal(("Instrument", 2L), (d.Sections[0].Name, d.Sections[0].Line));
        Assert.Equal(["Model", "Serial", "Enabled", "Range"], d.Sections[0].Keys);
        Assert.Equal(("Limits", 8L), (d.Sections[1].Name, d.Sections[1].Line));
        Assert.Equal(["Vmax", "Vmin", "Imax", "Timeout"], d.Sections[1].Keys);
        Assert.All(d.Sections, static s => Assert.Null(s.Values));
        Assert.DoesNotContain("SYN-1000", Encoding.UTF8.GetString(DescriptorWriter.ToBytes(d)), StringComparison.Ordinal);

        // --include-values opens every value, aligned with the keys.
        var all = ProfileFixture("keyvalue/instrument.ini", includeValues: true);
        Assert.Equal(["SYN-1000", "A12345", "true", "10"], all.Sections![0].Values);
        Assert.Equal(["3.3", "1.8", "0.250", "30"], all.Sections[1].Values);

        // --include-values-for opens the values of the named keys only; the rest of the aligned list is null.
        var named = ProfileFixture("keyvalue/instrument.ini", false, "serial", "Timeout");
        Assert.Equal(new string?[] { null, "A12345", null, null }, named.Sections![0].Values);
        Assert.Equal(new string?[] { null, null, null, "30" }, named.Sections[1].Values);
        Assert.DoesNotContain(named.Issues, static i => i.Code == "includeValuesForUnmatched");
        Assert.Contains(ProfileFixture("keyvalue/instrument.ini", false, "nothing").Issues, static i => i.Code == "includeValuesForUnmatched");
    }

    [Fact]
    public void KeyValue_OneFieldPerKey_VotesThroughTheValueClassifier()
    {
        var d = ProfileFixture("keyvalue/instrument.ini");
        Assert.Equal(["Model", "Serial", "Enabled", "Range", "Vmax", "Vmin", "Imax", "Timeout"], d.Table!.Fields.Select(static f => f.Name!).ToArray());
        Assert.Equal(["string", "string", "bool", "int", "float", "float", "float", "int"], d.Table.Fields.Select(static f => f.Type!).ToArray());
        Assert.All(d.Table.Fields, static f => Assert.Equal(1, f.Count));

        // The table is the sections: one row each, with the number of keys as the field count.
        Assert.Equal(2, d.Table.Rows);
        Assert.Equal(new MinMax(4, 4), d.Table.FieldCount);
        Assert.Null(d.Table.RowClasses);
    }

    [Fact]
    public void KeyValue_KeyThatRepeatsAcrossSections_IsOneFieldWithACountOfN()
    {
        var d = Profile("[a]\nName = x\nSize = 3\n[b]\nName = y\nSize = 4\n[c]\nName = z\nSize = 5\n");
        Assert.Equal(["Name", "Size"], d.Table!.Fields.Select(static f => f.Name!).ToArray());
        Assert.All(d.Table.Fields, static f => Assert.Equal(3, f.Count));
        Assert.Equal(3, d.Table.Rows);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "duplicateKey");
    }

    [Fact]
    public void KeyValue_DuplicateKeyInOneSection_Issue()
    {
        var d = Profile("[a]\nName = x\nSize = 3\nName = y\nSize = 4\nOther = 1\n[b]\nName = z\n");
        var issue = Assert.Single(d.Issues, static i => i.Code == "duplicateKey");
        Assert.Equal(2, issue.Count);
        Assert.Equal([4L, 5L], issue.FirstLines);

        // Every occurrence is listed and counted.
        Assert.Equal(["Name", "Size", "Name", "Size", "Other"], d.Sections![0].Keys);
        Assert.Equal(3, d.Table!.Fields.Single(static f => f.Name == "Name").Count);
    }

    [Fact]
    public void KeyValue_NoSections_OneUnnamedSection_AndTheSeparatorIsTheCommonOne()
    {
        var d = ProfileFixture("keyvalue/station.conf");
        var dialect = Dialect(d);
        Assert.Equal(":", dialect.Separator);
        Assert.False(dialect.HasSections);
        var section = Assert.Single(d.Sections!);
        Assert.Equal(string.Empty, section.Name);
        Assert.Equal(2, section.Line);
        Assert.Equal(["Station", "Operator", "Temperature", "Humidity", "Calibrated", "Open"], section.Keys);
        Assert.Equal(1, d.Table!.Rows);

        // A tie between the two separators goes to =.
        Assert.Equal("=", Dialect(Profile("a = 1\nb: 2\nc = 3\nd: 4\n", "t.cfg")).Separator);
        Assert.Equal(":", Dialect(Profile("a = 1\nb: 2\nc: 3\nd: 4\n", "t.cfg")).Separator);
    }

    [Fact]
    public void KeyValue_Comments_BlankLines_AndSectionHeaders_AreNotKeys()
    {
        var d = Profile("; one\n# two\n\n[main]\nA=1\n! three\n// four\nB=2\n\n[other]\nC=3\n");
        Assert.Equal(["A", "B"], d.Sections![0].Keys);
        Assert.Equal(["C"], d.Sections[1].Keys);
        var region = Assert.Single(d.Layout.Regions);
        Assert.Equal("data", region.Kind);
        var kinds = region.LineKinds!.ToDictionary(static e => e.Key, static e => e.Value);
        Assert.Equal(new[] { 2L, 4L, 3L, 2L }, new[] { kinds["blank"], kinds["comment"], kinds["keyValue"], kinds["text"] });
    }

    [Fact]
    public void WhitespaceKeyValueDoc_ChoosesKeyValue_NotDelimited()
    {
        // sq-04 architect A11: lines of "Lot = A12" give the whitespace delimiter k=3 on every line, so the delimited profiler scores
        // 0.7. The key=value profiler scores 0.8 and wins; the delimited score is not touched.
        var text = string.Concat(Enumerable.Range(0, 12).Select(static i => "Item" + i.ToString(CultureInfo.InvariantCulture) + " = A" + (i + 12).ToString(CultureInfo.InvariantCulture) + "\n"));
        var d = Profile(text, "t.txt");
        Assert.Equal("keyValue", d.Format.Family);
        Assert.Equal(0.8, d.Format.Score);
        var family = Assert.Single(d.Evidence, static e => e.Stage == "format" && e.Topic == "family");
        Assert.Equal(0.7, family.Candidates.Single(static c => c.Value == "delimited").Score);
        Assert.Equal(0.8, family.Candidates.Single(static c => c.Value == "keyValue").Score);
    }

    [Fact]
    public void KeyValue_NotChosen_ForPreambleCsv()
    {
        // A regression guard for sq-04: a key=value preamble above a table stays the preamble of a delimited file.
        var preamble = string.Concat(Enumerable.Range(0, 8).Select(static i => "Key" + i.ToString(CultureInfo.InvariantCulture) + "=Value" + i.ToString(CultureInfo.InvariantCulture) + "\n"));
        var rows = string.Concat(Enumerable.Range(1, 12).Select(static i => i.ToString(CultureInfo.InvariantCulture) + "," + (700 + (i * 100)).ToString(CultureInfo.InvariantCulture) + ",15." + (i % 10).ToString(CultureInfo.InvariantCulture) + "\n"));
        var d = Profile(preamble + "Step,Freq(MHz),Gain(dB)\n" + rows, "t.csv");
        Assert.Equal("delimited", d.Format.Family);
        Assert.Equal(8, d.Layout.Regions.Single(static r => r.Kind == "preamble").Keys!.Count);
        var family = Assert.Single(d.Evidence, static e => e.Stage == "format" && e.Topic == "family");
        Assert.Equal(0.0, family.Candidates.Single(static c => c.Value == "keyValue").Score);

        // Where the preamble is so long that the keys are 80 percent of the lines, the table is still a table-like run: the file is
        // not a key=value document (the delimited profiler declines it too, as before sq-09, and the text profile describes it).
        var longPreamble = string.Concat(Enumerable.Range(0, 30).Select(static i => "Key" + i.ToString(CultureInfo.InvariantCulture) + "=Value" + i.ToString(CultureInfo.InvariantCulture) + "\n"));
        var longer = Profile(longPreamble + "Step,Freq(MHz),Gain(dB)\n1,700,15.0\n2,800,15.2\n3,900,15.5\n", "t.csv");
        Assert.NotEqual("keyValue", longer.Format.Family);
        var longerFamily = Assert.Single(longer.Evidence, static e => e.Stage == "format" && e.Topic == "family");
        Assert.Equal(0.0, longerFamily.Candidates.Single(static c => c.Value == "keyValue").Score);
        Assert.Contains("table-like", longerFamily.Candidates.Single(static c => c.Value == "keyValue").Reason, StringComparison.Ordinal);

        // The committed fixtures with a preamble, too.
        foreach (var fixture in new[] { "lab/sweep-preamble.csv", "delimited/semicolon-preamble.csv", "canary/canary-preamble.csv" })
        {
            Assert.Equal("delimited", ProfileFixture(fixture).Format.Family);
        }
    }

    [Fact]
    public void KeyValue_NotChosen_ForACsvWhoseFirstCellsHoldAColon()
    {
        // Found by the reclassification guard: a record such as "Result: ok,1,2,3" reads as a pair, but a table that the comma gives is
        // the delimited profiler's, whatever the first cell looks like.
        var rows = string.Concat(Enumerable.Range(1, 40).Select(static i => "Row" + i.ToString(CultureInfo.InvariantCulture) + ": ok," + i.ToString(CultureInfo.InvariantCulture) + ",2.5,x\n"));
        var d = Profile(rows, "t.csv");
        Assert.Equal("delimited", d.Format.Family);
        var family = Assert.Single(d.Evidence, static e => e.Stage == "format" && e.Topic == "family");
        Assert.Equal(0.0, family.Candidates.Single(static c => c.Value == "keyValue").Score);
        Assert.Contains("table-like", family.Candidates.Single(static c => c.Value == "keyValue").Reason, StringComparison.Ordinal);

        // A header and nothing else after a long key=value preamble is a table without rows (the delimited profiler's degenerate shape).
        var preamble = string.Concat(Enumerable.Range(0, 40).Select(static i => "Key" + i.ToString(CultureInfo.InvariantCulture) + "=1\n"));
        Assert.Equal("delimited", Profile(preamble + "a,b,c\n", "t.csv").Format.Family);
    }

    [Fact]
    public void KeyValue_NotChosen_WhenMostKeysRepeatInsideTheirSection()
    {
        // A log or a list of records is not a document: its keys do not identify its values.
        var log = string.Concat(Enumerable.Range(0, 30).Select(static i => (i % 2 == 0 ? "INFO" : "WARN") + ": event " + i.ToString(CultureInfo.InvariantCulture) + "\n"));
        var d = Profile(log, "t.log");
        Assert.NotEqual("keyValue", d.Format.Family);
        var family = Assert.Single(d.Evidence, static e => e.Stage == "format" && e.Topic == "family");
        Assert.Contains("repeat", family.Candidates.Single(static c => c.Value == "keyValue").Reason, StringComparison.Ordinal);

        // The same key in every section is how a document repeats a structure, and is fine.
        Assert.Equal("keyValue", Profile("[a]\nName = 1\nSize = 2\n[b]\nName = 3\nSize = 4\n").Format.Family);
    }

    [Theory]
    [InlineData("a=1\n")]
    [InlineData("hello: world\n")]
    [InlineData("The quick brown fox: jumps over the lazy dog. Another line follows here.\nMore text that is plain prose with no keys at all.\nAnd a third line of prose.\n")]
    [InlineData("a=1\nb=2\nplain line one\nplain line two\nplain line three\nplain line four\n")]
    [InlineData("12:30:00,5\n12:31:00,6\n12:32:00,7\n")]
    public void KeyValue_NotChosen_ForOtherFiles(string text)
    {
        Assert.NotEqual("keyValue", Profile(text, "t.txt").Format.Family);
    }

    [Fact]
    public void KeyValue_LayoutOnly_KeepsTheSections()
    {
        var full = ProfileFixture("keyvalue/instrument.ini");
        var bare = Profiler.Profile(TestBytes.Fixture("keyvalue/instrument.ini"), Options(layoutOnly: true));
        Assert.Equal(full.Sections, bare.Sections);
        Assert.Equal(full.Layout, bare.Layout);
        Assert.Equal(full.Format, bare.Format);
        Assert.Equal(full.Table!.Fields.Select(static f => f.Name).ToArray(), bare.Table!.Fields.Select(static f => f.Name).ToArray());
        Assert.All(bare.Table.Fields, static f => Assert.Null(f.Type));
        Assert.Null(bare.Hints);
        Assert.Equal(full.Fingerprint.Layout, bare.Fingerprint.Layout);
        Assert.Equal(full.Fingerprint.Names, bare.Fingerprint.Names);
    }

    [Fact]
    public void KeyValue_Fingerprint_FollowsTheSectionNamesAndKeys_NotTheValues()
    {
        var a = Profile("[s]\nA = 1\nB = 2\n[t]\nC = 3\n");
        var b = Profile("[s]\nA = 9\nB = 8\n[t]\nC = 7\n");
        Assert.Equal(a.Fingerprint.Layout, b.Fingerprint.Layout);
        Assert.Equal(a.Fingerprint.Structure, b.Fingerprint.Structure);
        Assert.Equal(a.Fingerprint.Names, b.Fingerprint.Names);

        // Another key or section name changes it, case folded to ASCII.
        Assert.NotEqual(a.Fingerprint.Layout, Profile("[s]\nA = 1\nB2 = 2\n[t]\nC = 3\n").Fingerprint.Layout);
        Assert.NotEqual(a.Fingerprint.Layout, Profile("[s]\nA = 1\nB = 2\n[u]\nC = 3\n").Fingerprint.Layout);
        Assert.Equal(a.Fingerprint.Layout, Profile("[S]\na = 1\nb = 2\n[T]\nc = 3\n").Fingerprint.Layout);
    }

    [Fact]
    public void KeyValue_Hints_OfferTheKeyLines_AsStableLines()
    {
        var d = ProfileFixture("keyvalue/instrument.ini");
        Assert.Contains(d.Hints!.StableLines, static s => s.Regex == "^\\s*Model\\s*[=:]" && s.Line == 2);
        Assert.Null(d.Hints.HeaderFields);
        Assert.Equal(new MinMax(4, 4), d.Hints.FieldCount);
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Profile_Golden(string fixture)
    {
        Golden.Verify(fixture, ProfileFixture(fixture));
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Profile_Fixture_SameTwiceAndUnderOtherCultures(string fixture)
    {
        var baseline = DescriptorWriter.ToBytes(ProfileFixture(fixture));
        var saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(baseline, DescriptorWriter.ToBytes(ProfileFixture(fixture)));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void KeyValue_Json_WritesSectionsAfterTheTable()
    {
        using var json = JsonDocument.Parse(DescriptorWriter.ToBytes(ProfileFixture("keyvalue/instrument.ini")));
        var names = json.RootElement.EnumerateObject().Select(static p => p.Name).ToArray();
        Assert.True(Array.IndexOf(names, "table") < Array.IndexOf(names, "sections"));
        Assert.True(Array.IndexOf(names, "sections") < Array.IndexOf(names, "fingerprint"));
        Assert.Equal("1.1", json.RootElement.GetProperty("schemaVersion").GetString());
    }

    public static TheoryData<string> FixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in Fixtures)
        {
            data.Add(fixture);
        }

        return data;
    }
}
