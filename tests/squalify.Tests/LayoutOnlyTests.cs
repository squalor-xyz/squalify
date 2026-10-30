// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// sq-47: the layout-only profile. <c>ProfileOptions.LayoutOnly</c> computes the encoding, the dialect, the layout regions, the
/// header names and the row classes, and leaves out the per-field sketches, statistics, types, hints and the evidence of the
/// type. The descriptor says so (<c>layoutOnly: true</c>, and null for the sections that were not computed).
/// </summary>
[Collection("MemoryProbe")]
public sealed class LayoutOnlyTests(ITestOutputHelper output)
{
    private const string Header = "Freq(MHz),Pout(dBm),Gain(dB)";

    private static ProfileOptions Full(long? maxBytes = null) => ProfileOptions.Create(false, [], maxBytes);

    private static ProfileOptions Layout(bool includeValues = false, long? maxBytes = null, long? maxFirstRecordBytes = null, params string[] allow)
        => ProfileOptions.Create(includeValues, allow, maxBytes, maxFirstRecordBytes, layoutOnly: true);

    private static JsonDocument Json(Descriptor d) => JsonDocument.Parse(DescriptorWriter.ToBytes(d));

    // Shapes

    private static string Row(int i, double shift = 0)
    {
        return string.Join(
            ",",
            (700 + (100 * i)).ToString(CultureInfo.InvariantCulture),
            (10 + (0.5 * i)).ToString("0.0", CultureInfo.InvariantCulture),
            (15 + (0.25 * i) + shift).ToString("0.00", CultureInfo.InvariantCulture));
    }

    private static string Block(string site, int temp, int rows, string header = Header, string? unitsRow = null)
    {
        var builder = new StringBuilder();
        builder.Append("Site=").Append(site).Append('\n');
        builder.Append("Temp(C)=").Append(temp.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append(header).Append('\n');
        if (unitsRow is not null)
        {
            builder.Append(unitsRow).Append('\n');
        }

        for (var i = 0; i < rows; i++)
        {
            builder.Append(Row(i)).Append('\n');
        }

        return builder.ToString();
    }

    private static string Blocks(int count, int rows, string? unitsRow = null)
    {
        var builder = new StringBuilder("# export\n");
        for (var b = 0; b < count; b++)
        {
            builder.Append(Block("S" + b.ToString(CultureInfo.InvariantCulture), 25 + (20 * b), rows, unitsRow: unitsRow));
        }

        builder.Append("Rows=").Append((count * rows).ToString(CultureInfo.InvariantCulture)).Append('\n');
        return builder.ToString();
    }

    private static string Wide(int columns, int rows)
    {
        var builder = new StringBuilder();
        builder.AppendJoin(',', Enumerable.Range(0, columns).Select(static c => "c" + c.ToString(CultureInfo.InvariantCulture))).Append('\n');
        for (var r = 0; r < rows; r++)
        {
            builder.AppendJoin(',', Enumerable.Range(0, columns).Select(c => ((r * 7) + c).ToString(CultureInfo.InvariantCulture))).Append('\n');
        }

        return builder.ToString();
    }

    private static IProfileSource Text(string name, string text) => new BytesSource(name, Encoding.UTF8.GetBytes(text));

    // The shapes the compared members are checked on: every fixture of the repository, and the shapes below that the fixtures do not cover.
    public static IEnumerable<(string Name, Func<IProfileSource> Source)> Synthetic()
    {
        yield return ("wide-padded", () => SyntheticCsvSource.PaddedWide(60, 256, 64));
        yield return ("blocks-with-units-rows", () => Text("t.csv", Blocks(4, 6, unitsRow: "MHz,dBm,dB")));
        yield return ("blocks-heterogeneous", () => Text("t.csv", "# export\n" + Block("A", 25, 6) + Block("B", 45, 6, header: "Freq(MHz),Pout(dBm),Other") + Block("C", 65, 6)));
        yield return ("blocks-truncated", () => Text("t.csv", Blocks(1003, 5)));
        yield return ("ragged-comments-duplicate-and-empty-names", () => Text(
            "t.csv",
            "# lab\nSite=1\nVth(V),vth(v),Id,\n0.1,0.2,3,x\n# a comment in the data\n0.2,0.3,4,y\n5,6\n0.3,0.4,5,z\n0.4,0.5,6,w\n0.5,0.6,7,v\n0.6,0.7,8,u\n"));
        yield return ("all-null-separators", () => Text(
            "t.csv",
            "Site,V(V),I(A)\n1,0.5,2\nNULL,NULL,NULL\n2,0.6,3\nNA,NA,NA\n,,\n3,0.7,4\nNULL,NA,NULL\n NULL , NULL , NULL \n4,,5\n- ,-,-\n5,0.9,7\n"));
        yield return ("date-order-conflict", () => Text("t.csv", "id,when\n1,3/17/2026\n2,17/3/2026\n3,4/5/2026\n4,5/4/2026\n5,6/7/2026\n"));
        // A file shorter than 64 KiB tolerates one invalid UTF-8 sequence and keeps its charset, so each shape holds one.
        yield return ("invalid-utf8-in-preamble", () => new BytesSource(
            "t.csv",
            [.. Encoding.UTF8.GetBytes("# note\nLot=A"), 0xFF, .. Encoding.UTF8.GetBytes("\nSite,Val\n1,2\n2,3\n3,4\n4,5\n")]));
        yield return ("invalid-utf8-in-data", () => new BytesSource(
            "t.csv",
            [.. Encoding.UTF8.GetBytes("# note\nLot=A\nSite,Val\n1,2\n2,"), 0xC3, 0x28, .. Encoding.UTF8.GetBytes("\n3,4\n4,5\n")]));
        yield return ("mixed-line-endings", () => Text("t.csv", "a,b\r\n1,2\n3,4\r5,6\n7,8\r\n9,10\n11,12\n"));
        yield return ("excluded-names", () => Text("t.csv", "Lot\u0001,ok,bad\uFFFD\n1,2,3\n4,5,6\n7,8,9\n"));
        yield return ("wide-2000-columns", () => Text("t.csv", Wide(2000, 12)));
        yield return ("empty", () => new BytesSource("t.csv", []));
        yield return ("utf16-with-stray-byte", () => new BytesSource("t.txt", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("a,b\n1,2\n"), 0x41]));
        yield return ("plain-text", () => Text("t.txt", "hello world\nthis is not a table\n"));
    }

    private static IEnumerable<(string Name, Func<IProfileSource> Source)> Fixtures()
    {
        var root = Golden.SourceFixturesRoot();
        foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(static p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".squalify.json", StringComparison.Ordinal) || name.EndsWith(".golden.txt", StringComparison.Ordinal) || name == ".gitkeep")
            {
                continue;
            }

            yield return (Path.GetRelativePath(root, path).Replace('\\', '/'), () => new FileSource(path));
        }
    }

    public static IEnumerable<(string Name, Func<IProfileSource> Source)> AllShapes() => Fixtures().Concat(Synthetic());

    // The members that a layout-only descriptor must have equal to the full descriptor's, as compact JSON text; "<absent>" for a missing member.
    private static readonly string[] CompareTop =
        ["schemaVersion", "tool", "limits", "file", "scope", "encoding", "format", "kind", "layout", "binary", "sections", "stdf", "issues"];

    private static readonly string[] CompareFingerprint = ["version", "structure", "layout", "names"];

    private static readonly string[] CompareField = ["index", "name", "rawName", "nameHex", "nameParts"];

    private static readonly string[] CompareTable = ["rows", "fieldCount", "rowClasses"];

    private static string Compact(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Member(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) ? Compact(value) : "<absent>";

    public static List<(string Key, string Value)> Compared(Descriptor d)
    {
        using var doc = Json(d);
        var root = doc.RootElement;
        var list = new List<(string, string)>();
        foreach (var key in CompareTop)
        {
            list.Add((key, Member(root, key)));
        }

        var fingerprint = root.GetProperty("fingerprint");
        foreach (var key in CompareFingerprint)
        {
            list.Add(("fingerprint." + key, Member(fingerprint, key)));
        }

        // The evidence of the stage "type" is the one entry that needs the types.
        list.Add(("evidence", Compact(JsonSerializer.SerializeToElement(root.GetProperty("evidence").EnumerateArray().Where(static e => e.GetProperty("stage").GetString() != "type").Select(static e => Compact(e)).ToArray()))));
        if (root.TryGetProperty("table", out var table))
        {
            foreach (var key in CompareTable)
            {
                list.Add(("table." + key, Member(table, key)));
            }

            var fields = table.GetProperty("fields").EnumerateArray().ToArray();
            list.Add(("table.fields.length", fields.Length.ToString(CultureInfo.InvariantCulture)));
            for (var i = 0; i < fields.Length; i++)
            {
                foreach (var key in CompareField)
                {
                    list.Add(("table.fields[" + i.ToString(CultureInfo.InvariantCulture) + "]." + key, Member(fields[i], key)));
                }
            }
        }
        else
        {
            list.Add(("table", "<absent>"));
        }

        return list;
    }

    // Same layout, less work

    [Fact]
    public void SameLayout_EveryFixtureAndShape_ComparedMembersEqualTheFullDescriptors()
    {
        var shapes = AllShapes().ToArray();
        Assert.True(shapes.Length >= 30, "the fixtures and the synthetic shapes: " + shapes.Length.ToString(CultureInfo.InvariantCulture));
        var tables = 0;
        var issueCodes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (name, source) in shapes)
        {
            var full = Profiler.Profile(source(), Full());
            var layout = Profiler.Profile(source(), Layout());
            Assert.True(layout.LayoutOnly, name);
            Assert.False(full.LayoutOnly, name);

            var expected = Compared(full);
            var actual = Compared(layout);
            Assert.Equal(expected.Select(static e => e.Key), actual.Select(static e => e.Key));
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.True(expected[i].Value == actual[i].Value, $"{name}: {expected[i].Key} differs:\nfull:   {Clip(expected[i].Value)}\nlayout: {Clip(actual[i].Value)}");
            }

            if (full.Table is not null)
            {
                tables++;
            }

            foreach (var issue in full.Issues)
            {
                issueCodes.Add(issue.Code);
            }

            // The model agrees with the JSON: the row classes and the layout are the very same records.
            Assert.Equal(full.Table?.RowClasses, layout.Table?.RowClasses);
            Assert.Equal(full.Layout, layout.Layout);
            Assert.Equal(full.Encoding, layout.Encoding);
            Assert.Equal(full.Format, layout.Format);
            Assert.Equal(full.Issues, layout.Issues);
            Assert.Equal(full.Fingerprint with { Typed = null }, layout.Fingerprint);
        }

        Assert.True(tables >= 20, "tables: " + tables.ToString(CultureInfo.InvariantCulture));

        // The shapes exercise the structural issues (every issue code that the profiler writes is structural: none comes from field analysis).
        string[] expectedCodes =
        [
            "blocksTruncated", "commentInData", "duplicateHeader", "emptyHeaderName", "heterogeneousBlocks", "invalidUtf8", "invalidUnicode",
            "mixedLineEndings", "nameExcludedFromFingerprint", "noDataRows", "raggedRows",
        ];
        Assert.True(expectedCodes.All(issueCodes.Contains), "issue codes seen: " + string.Join(",", issueCodes));
    }

    private static string Clip(string text) => text.Length <= 400 ? text : text[..400] + "...";

    [Fact]
    public void SameLayout_RowClasses_IncludeEveryKindOfAllNullRecord()
    {
        // The layout needs the per-record null decision, so the row classes, the all-null spellings and the transitions must be the full profile's.
        var source = Synthetic().Single(static s => s.Name == "all-null-separators").Source;
        var full = Profiler.Profile(source(), Full()).Table!.RowClasses!;
        var layout = Profiler.Profile(source(), Layout()).Table!.RowClasses!;
        Assert.Equal(full, layout);
        Assert.True(full.AllNullRows >= 6, "all-null rows: " + full.AllNullRows.ToString(CultureInfo.InvariantCulture));
        Assert.True(full.AllNullRowSpellingsMixed >= 2);
        Assert.True(full.Transitions > 5);
        Assert.NotEmpty(layout.AllNullRowSpellings!);
    }

    [Fact]
    public void SameLayout_DegenerateTablesAndTheHeaderlessTable_KeepTheirNames()
    {
        foreach (var name in new[] { "delimited/header-only.csv", "delimited/one-column.csv", "delimited/no-header.tsv" })
        {
            var source = Fixtures().Single(f => f.Name == name).Source;
            var full = Profiler.Profile(source(), Full());
            var layout = Profiler.Profile(source(), Layout());
            Assert.Equal(full.Table!.Fields.Select(static f => (f.Index, f.Name, f.RawName, f.NameParts)), layout.Table!.Fields.Select(static f => (f.Index, f.Name, f.RawName, f.NameParts)));
            Assert.Equal(full.Table.RowClasses, layout.Table.RowClasses);
        }

        // With no header, name, rawName and nameParts are written as null, as in the full descriptor.
        using var doc = Json(Profiler.Profile(Fixtures().Single(static f => f.Name == "delimited/no-header.tsv").Source(), Layout()));
        var field = doc.RootElement.GetProperty("table").GetProperty("fields")[0];
        Assert.Equal(new[] { "index", "name", "rawName", "nameParts" }, field.EnumerateObject().Select(static p => p.Name).ToArray());
        Assert.Equal(JsonValueKind.Null, field.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, field.GetProperty("rawName").ValueKind);
        Assert.Equal(JsonValueKind.Null, field.GetProperty("nameParts").ValueKind);
    }

    // Marked, not empty

    [Fact]
    public void Marked_LayoutOnlyIsWrittenOnlyWithTheOption_AfterOptions()
    {
        var source = Fixtures().Single(static f => f.Name == "lab/sweep-preamble.csv").Source;
        using var full = Json(Profiler.Profile(source(), Full()));
        Assert.False(full.RootElement.TryGetProperty("layoutOnly", out _));
        Assert.Equal(JsonValueKind.Object, full.RootElement.GetProperty("hints").ValueKind);
        Assert.Equal(JsonValueKind.String, full.RootElement.GetProperty("fingerprint").GetProperty("typed").ValueKind);

        using var layout = Json(Profiler.Profile(source(), Layout()));
        Assert.True(layout.RootElement.GetProperty("layoutOnly").GetBoolean());
        var keys = layout.RootElement.EnumerateObject().Select(static p => p.Name).ToList();
        Assert.Equal(keys.IndexOf("options") + 1, keys.IndexOf("layoutOnly"));
        Assert.Equal(full.RootElement.EnumerateObject().Select(static p => p.Name).Where(static k => k != "options"), keys.Where(static k => k != "layoutOnly" && k != "options"));

        // The options object does not change: layoutOnly is a member of the descriptor, not of options.
        Assert.Equal(Compact(full.RootElement.GetProperty("options")), Compact(layout.RootElement.GetProperty("options")));
        Assert.Equal(JsonValueKind.Null, layout.RootElement.GetProperty("hints").ValueKind);
        var fingerprint = layout.RootElement.GetProperty("fingerprint");
        Assert.Equal(new[] { "version", "structure", "layout", "typed", "names" }, fingerprint.EnumerateObject().Select(static p => p.Name).ToArray());
        Assert.Equal(JsonValueKind.Null, fingerprint.GetProperty("typed").ValueKind);
        Assert.Equal(2, fingerprint.GetProperty("version").GetInt32());
        Assert.Equal(full.RootElement.GetProperty("fingerprint").GetProperty("version").GetInt32(), fingerprint.GetProperty("version").GetInt32());
    }

    [Fact]
    public void Marked_FieldsCarryOnlyNames_AndNoTypesStatisticsSketchesTopTextOrUnit()
    {
        var source = Fixtures().Single(static f => f.Name == "lab/sweep-preamble.csv").Source;
        var d = Profiler.Profile(source(), Layout(includeValues: true));
        using var doc = Json(d);
        var fields = doc.RootElement.GetProperty("table").GetProperty("fields").EnumerateArray().ToArray();
        Assert.True(fields.Length >= 3);
        foreach (var field in fields)
        {
            Assert.Equal(new[] { "index", "name", "rawName", "nameParts" }, field.EnumerateObject().Select(static p => p.Name).ToArray());
        }

        foreach (var field in d.Table!.Fields)
        {
            Assert.NotNull(field.Name);
            Assert.NotNull(field.RawName);
            Assert.NotNull(field.NameParts);
            Assert.Null(field.Type);
            Assert.Null(field.Dominant);
            Assert.Null(field.TypeVotes);
            Assert.Null(field.Count);
            Assert.Null(field.Nulls);
            Assert.Null(field.NullsExcludingAllNullRows);
            Assert.Null(field.NullTokens);
            Assert.Null(field.Flags);
            Assert.Null(field.Distinct);
            Assert.Null(field.Length);
            Assert.Null(field.Shapes);
            Assert.Null(field.Monotonic);
            Assert.Null(field.Constant);
            Assert.Null(field.Unique);
            Assert.Null(field.Patterns);
            Assert.Null(field.Numeric);
            Assert.Null(field.Temporal);
            Assert.Null(field.StatsWithheld);
            Assert.Null(field.Unit);
            Assert.Null(field.Top);
            Assert.Null(field.Text);
            Assert.Null(field.NullSpellings);
            Assert.Equal(0, field.NullSpellingsOther);
            Assert.Equal(0, field.NullSpellingsOtherDistinct);
            Assert.False(field.NullSpellingsOtherDistinctCapped);
        }

        // The header names that carry a unit keep their parts; the row classes and the layout are written.
        Assert.Contains(d.Table.Fields, static f => f.NameParts!.Unit is not null);
        Assert.NotNull(d.Table.RowClasses);
        Assert.Null(d.Hints);
        Assert.Null(d.Fingerprint.Typed);
    }

    [Fact]
    public void Marked_TheEvidenceOfTheTypeIsLeftOut_AndNothingElseIs()
    {
        var source = Synthetic().Single(static s => s.Name == "date-order-conflict").Source;
        var full = Profiler.Profile(source(), Full());
        var layout = Profiler.Profile(source(), Layout());
        Assert.Contains(full.Evidence, static e => e.Stage == "type" && e.Topic == "dateOrderConflict");
        Assert.DoesNotContain(layout.Evidence, static e => e.Stage == "type");
        Assert.Equal(full.Evidence.Where(static e => e.Stage != "type"), layout.Evidence);
        Assert.Contains(layout.Evidence, static e => e is { Stage: "header", Topic: "candidates" });
        Assert.Contains(layout.Evidence, static e => e is { Stage: "unit", Topic: "headerConvention" });
        Assert.Contains(layout.Evidence, static e => e is { Stage: "dialect", Topic: "delimiter" });
    }

    [Fact]
    public void Marked_NonTables_AreMarkedToo_AndKeepTheirOtherSections()
    {
        foreach (var name in new[] { "text/plain.txt", "binary/random.bin", "binary/sample.gz" })
        {
            var source = Fixtures().Single(f => f.Name == name).Source;
            var d = Profiler.Profile(source(), Layout());
            using var doc = Json(d);
            Assert.True(doc.RootElement.GetProperty("layoutOnly").GetBoolean(), name);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("hints").ValueKind);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("fingerprint").GetProperty("typed").ValueKind);
            Assert.False(doc.RootElement.TryGetProperty("table", out _), name);
            Assert.Equal(name.StartsWith("binary", StringComparison.Ordinal), doc.RootElement.TryGetProperty("binary", out _));
        }
    }

    [Fact]
    public void Marked_TheWriterRefusesADescriptorWhoseShapeContradictsTheFlag()
    {
        var source = Fixtures().Single(static f => f.Name == "lab/sweep-preamble.csv").Source;
        var full = Profiler.Profile(source(), Full());
        var layout = Profiler.Profile(source(), Layout());

        // A layout-only descriptor that holds hints, a typed fingerprint, a type evidence entry or a field statistic.
        Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(layout with { Hints = full.Hints }));
        Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(layout with { Fingerprint = full.Fingerprint }));
        Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(layout with { Evidence = [.. layout.Evidence, new Evidence("type", "dateOrderConflict", "string", [])] }));
        var withType = layout with { Table = layout.Table! with { Fields = [layout.Table.Fields[0] with { Type = "int" }, .. layout.Table.Fields.Skip(1)] } };
        Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(withType));
        var withCount = layout with { Table = layout.Table! with { Fields = [layout.Table.Fields[0] with { Count = 0 }, .. layout.Table.Fields.Skip(1)] } };
        Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(withCount));

        // A full descriptor that lacks hints, the typed fingerprint or the type of a field.
        Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(full with { Hints = null }));
        Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(full with { Fingerprint = full.Fingerprint with { Typed = null } }));
        Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(full with { Table = full.Table! with { Fields = [full.Table.Fields[0] with { Type = null }, .. full.Table.Fields.Skip(1)] } }));
        Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(full with { Table = full.Table! with { Fields = [full.Table.Fields[0] with { Count = null }, .. full.Table.Fields.Skip(1)] } }));
    }

    [Fact]
    public void Marked_Equality_ALayoutOnlyDescriptorIsNeverEqualToAFullOne()
    {
        var source = Fixtures().Single(static f => f.Name == "lab/sweep-preamble.csv").Source;
        var full = Profiler.Profile(source(), Full());
        var layout = Profiler.Profile(source(), Layout());
        var again = Profiler.Profile(source(), Layout());
        Assert.NotEqual(full, layout);
        Assert.False(full.Equals(layout));
        Assert.Equal(layout, again);
        Assert.Equal(layout.GetHashCode(), again.GetHashCode());
        Assert.True(layout.LayoutOnly);
        Assert.Equal(layout.Options.LayoutOnly, layout.LayoutOnly);

        // The flag is in the options, so two descriptors that differ in nothing else differ by it.
        Assert.NotEqual(layout with { Options = Full() }, layout);
        Assert.False((layout with { Options = Full() }).LayoutOnly);

        // A table of layout-only fields differs from one of full fields even when the names agree.
        Assert.NotEqual(full.Table, layout.Table);
        Assert.NotEqual(full.Table!.Fields[0], layout.Table!.Fields[0]);
        Assert.Equal(layout.Table.Fields[0], again.Table!.Fields[0]);
    }

    [Fact]
    public void Options_LayoutOnly_IsATrailingOptionalMember_DefaultFalse_AndPartOfEquality()
    {
        Assert.False(new ProfileOptions(false, []).LayoutOnly);
        Assert.False(ProfileOptions.Create(false, []).LayoutOnly);
        Assert.False(ProfileOptions.Create(true, ["a"], 5, 6).LayoutOnly);
        Assert.True(ProfileOptions.Create(false, [], layoutOnly: true).LayoutOnly);
        Assert.True(ProfileOptions.Create(false, [], 5, 6, true).LayoutOnly);
        Assert.NotEqual(ProfileOptions.Create(false, []), ProfileOptions.Create(false, [], layoutOnly: true));
        Assert.NotEqual(ProfileOptions.Create(false, []).GetHashCode(), ProfileOptions.Create(false, [], layoutOnly: true).GetHashCode());
        Assert.Equal(ProfileOptions.Create(false, [], layoutOnly: true), ProfileOptions.Create(false, [], layoutOnly: true));
        var (includeValues, includeValuesFor, maxBytes, maxFirstRecordBytes, layoutOnly) = ProfileOptions.Create(true, ["a"], 5, 6, true);
        Assert.True(includeValues);
        Assert.Equal(["a"], includeValuesFor);
        Assert.Equal(5, maxBytes);
        Assert.Equal(6, maxFirstRecordBytes);
        Assert.True(layoutOnly);
    }

    // The fingerprint

    [Fact]
    public void Fingerprint_StructureLayoutAndNamesAreComputed_TheTypedProjectionIsNot_AndTheVersionIsUnchanged()
    {
        foreach (var name in new[] { "lab/sweep-preamble.csv", "delimited/per-site-blocks.csv", "delimited/no-header.tsv", "delimited/padded-annotated.csv" })
        {
            var source = Fixtures().Single(f => f.Name == name).Source;
            var full = Profiler.Profile(source(), Full());
            var layout = Profiler.Profile(source(), Layout());
            Assert.Equal(Projection.Version, layout.Fingerprint.Version);
            Assert.Equal(full.Fingerprint.Version, layout.Fingerprint.Version);
            Assert.Equal(full.Fingerprint.Structure, layout.Fingerprint.Structure);
            Assert.Equal(full.Fingerprint.Layout, layout.Fingerprint.Layout);
            Assert.Equal(full.Fingerprint.Names, layout.Fingerprint.Names);
            Assert.NotNull(full.Fingerprint.Typed);
            Assert.Null(layout.Fingerprint.Typed);

            // The projection bytes that a layout-only input can give are those of the full profile; the typed one is refused.
            var fullInput = ProjectionInput.From(full);
            var layoutInput = ProjectionInput.From(layout);
            Assert.True(layoutInput.LayoutOnly);
            Assert.Equal(Projection.Structure(fullInput), Projection.Structure(layoutInput));
            Assert.Equal(Projection.Layout(fullInput), Projection.Layout(layoutInput));
            Assert.Equal(Projection.Names(fullInput), Projection.Names(layoutInput));
            Assert.Throws<InvalidOperationException>(() => Projection.Typed(layoutInput));
        }
    }

    // The null decision

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NA")]
    [InlineData("na")]
    [InlineData("Na.")]
    [InlineData("N/A")]
    [InlineData("n/a")]
    [InlineData("null")]
    [InlineData("NULL")]
    [InlineData("Null")]
    [InlineData(" NULL ")]
    [InlineData("\u00A0NaN\t")]
    [InlineData("nan")]
    [InlineData("-")]
    [InlineData(" - ")]
    [InlineData("--")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("NAN.")]
    [InlineData("None")]
    [InlineData("nul")]
    [InlineData("nulls")]
    [InlineData("N/A ?")]
    [InlineData("\u2028NA")]
    [InlineData("1,5")]
    [InlineData("true")]
    [InlineData("x")]
    public void IsNull_IsExactlyTheNullDecisionOfTheClassifier(string text)
    {
        var expected = ValueClassifier.Classify(text, '.').Vote == ValueVote.Null;
        Assert.Equal(expected, ValueClassifier.IsNull(text));
        Assert.Equal(ValueClassifier.Classify(text, ',').Vote == ValueVote.Null, ValueClassifier.IsNull(text));
    }

    [Fact]
    public void IsNull_AgreesWithTheClassifierOnEveryShortAsciiString()
    {
        // Every string of up to 3 characters from an alphabet that holds the null vocabulary's letters and the trim set.
        const string alphabet = "NnAa/.-L u\t\u00A0\u2028x1";
        var count = 0;
        var chars = new char[3];
        for (var length = 0; length <= 3; length++)
        {
            var total = (int)Math.Pow(alphabet.Length, length);
            for (var n = 0; n < total; n++)
            {
                var rest = n;
                for (var i = 0; i < length; i++)
                {
                    chars[i] = alphabet[rest % alphabet.Length];
                    rest /= alphabet.Length;
                }

                var text = new string(chars, 0, length);
                Assert.Equal(ValueClassifier.Classify(text, '.').Vote == ValueVote.Null, ValueClassifier.IsNull(text));
                count++;
            }
        }

        Assert.True(count > 3_000);
    }

    // With the other options

    [Fact]
    public void WithMaxBytes_ScopeIsWrittenAsUsual_AndTheComparedMembersStillEqual()
    {
        var source = Fixtures().Single(static f => f.Name == "delimited/semiconductor-sweep.csv").Source;
        foreach (var limit in new long[] { 300, 700, 100_000 })
        {
            var full = Profiler.Profile(source(), Full(limit));
            var layout = Profiler.Profile(source(), Layout(maxBytes: limit));
            Assert.NotNull(layout.Scope);
            Assert.Equal(full.Scope, layout.Scope);
            Assert.Equal(limit, layout.Scope!.MaxBytes);
            Assert.Equal(Compared(full), Compared(layout));
            using var doc = Json(layout);
            Assert.True(doc.RootElement.TryGetProperty("scope", out _));
            Assert.True(doc.RootElement.GetProperty("layoutOnly").GetBoolean());
            var keys = doc.RootElement.EnumerateObject().Select(static p => p.Name).ToList();
            Assert.True(keys.IndexOf("scope") > keys.IndexOf("layoutOnly"));
        }
    }

    [Fact]
    public void WithProfilePrefix_TheDescriptorEqualsProfile_AndThePrefixIsTheFullProfilesPrefix()
    {
        var source = Fixtures().Single(static f => f.Name == "lab/sweep-preamble.csv").Source;
        var options = Layout(maxBytes: 250);
        var prefix = Profiler.ProfilePrefix(source(), options);
        Assert.Equal(Profiler.Profile(source(), options), prefix.Descriptor);
        Assert.True(prefix.Descriptor.LayoutOnly);
        Assert.Equal(DescriptorWriter.ToBytes(Profiler.Profile(source(), options)), DescriptorWriter.ToBytes(prefix.Descriptor));
        var fullPrefix = Profiler.ProfilePrefix(source(), Full(250));
        Assert.Equal(fullPrefix.Prefix.ToArray(), prefix.Prefix.ToArray());
        Assert.Equal(fullPrefix.Scope, prefix.Scope);
        Assert.Equal(Compared(fullPrefix.Descriptor), Compared(prefix.Descriptor));
    }

    [Fact]
    public void WithMaxFirstRecordBytes_ARefusedFileIsStillRefused_AndAnAcceptedOneIsUnchanged()
    {
        var text = "id,value,label\n" + string.Concat(Enumerable.Range(1, 50).Select(static i => i.ToString(CultureInfo.InvariantCulture) + ",2,x\n"));
        var source = Text("t.csv", text);
        var refused = Assert.Throws<SqualifyException>(() => Profiler.Profile(source, Layout(maxBytes: 100, maxFirstRecordBytes: 10)));
        var refusedFull = Assert.Throws<SqualifyException>(() => Profiler.Profile(source, ProfileOptions.Create(false, [], 100, 10)));
        Assert.Equal(refusedFull.Message, refused.Message);

        var accepted = Profiler.Profile(source, Layout(maxBytes: 100, maxFirstRecordBytes: 100));
        // The ceiling is not written to the descriptor, so the bytes are those of a profile without it.
        Assert.Equal(DescriptorWriter.ToBytes(Profiler.Profile(source, Layout(maxBytes: 100))), DescriptorWriter.ToBytes(accepted));
        Assert.Equal(Compared(Profiler.Profile(source, Full(100))), Compared(accepted));
    }

    [Fact]
    public void WithIncludeValues_NoFieldValueIsComputed_OnlyTheLayoutValuesOpenedByTheOptionAreWritten()
    {
        // IncludeValues has no effect on the sections that are left out: no top, text, unit or constant statistic is computed, so none can be written.
        var source = Fixtures().Single(static f => f.Name == "lab/sweep-preamble.csv").Source;
        var plain = Profiler.Profile(source(), Layout());
        var all = Profiler.Profile(source(), Layout(includeValues: true));
        var named = Profiler.Profile(source(), Layout(includeValues: false, maxBytes: null, maxFirstRecordBytes: null, "Lot"));
        Assert.Equal(plain.Table, all.Table);
        Assert.Equal(plain.Table, named.Table);
        Assert.Equal(plain.Evidence, all.Evidence);
        Assert.All(all.Table!.Fields, static f => Assert.True(f.Top is null && f.Text is null && f.Unit is null && f.Numeric is null && f.Temporal is null));

        // What the option does open is the preamble and annotation values, which are part of the layout: the same as in the full profile.
        var fullAll = Profiler.Profile(source(), ProfileOptions.Create(true, []));
        Assert.Equal(fullAll.Layout, all.Layout);
        Assert.NotEqual(plain.Layout, all.Layout);
        Assert.Contains(all.Layout.Regions, static r => r.Values is not null);
        Assert.DoesNotContain(plain.Layout.Regions, static r => r.Values is not null && r.Values.Any(static v => v is not null));
        Assert.Equal(Profiler.Profile(source(), ProfileOptions.Create(false, ["Lot"])).Layout, named.Layout);
        Assert.Null(all.Hints);

        // The option itself is echoed in options, as for any profile.
        using var doc = Json(all);
        Assert.True(doc.RootElement.GetProperty("options").GetProperty("includeValues").GetBoolean());
    }

    [Fact]
    public void WithIncludeValuesFor_AnUnmatchedNameIsReportedAsInTheFullProfile_AFieldNameMatches()
    {
        var source = Fixtures().Single(static f => f.Name == "lab/sweep-preamble.csv").Source;
        var full = Profiler.Profile(source(), ProfileOptions.Create(false, ["nothing-matches-this", "Lot"]));
        var layout = Profiler.Profile(source(), Layout(includeValues: false, maxBytes: null, maxFirstRecordBytes: null, "nothing-matches-this", "Lot"));
        Assert.Equal(full.Issues, layout.Issues);
        Assert.Contains(layout.Issues, static i => i.Code == "includeValuesForUnmatched");

        // A field name is a name that a layout-only profile still has.
        var fieldName = layout.Table!.Fields.First(static f => f.Name is { Length: > 0 }).Name!;
        var byField = Profiler.Profile(source(), Layout(includeValues: false, maxBytes: null, maxFirstRecordBytes: null, fieldName));
        Assert.DoesNotContain(byField.Issues, static i => i.Code == "includeValuesForUnmatched");
    }

    // Determinism

    [Fact]
    public void Determinism_TheSameBytesTwice_AndUnderAnotherCulture_GiveTheSameDescriptor()
    {
        var sources = new[] { "lab/sweep-preamble.csv", "delimited/per-site-blocks.csv", "delimited/semicolon-decimal-comma.csv", "delimited/padded-annotated.csv" }
            .Select(name => Fixtures().Single(f => f.Name == name).Source)
            .Append(Synthetic().Single(static s => s.Name == "all-null-separators").Source)
            .ToArray();
        var baseline = sources.Select(source => DescriptorWriter.ToBytes(Profiler.Profile(source(), Layout()))).ToArray();
        for (var i = 0; i < sources.Length; i++)
        {
            Assert.Equal(baseline[i], DescriptorWriter.ToBytes(Profiler.Profile(sources[i](), Layout())));
        }

        var saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "tr-TR", "fr-FR" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                for (var i = 0; i < sources.Length; i++)
                {
                    Assert.Equal(baseline[i], DescriptorWriter.ToBytes(Profiler.Profile(sources[i](), Layout())));
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // Memory

    [Fact]
    public void Limits_AreWrittenAsForAFullProfile_AndTheSketchPrecisionOfAWideTableIsStillThere()
    {
        // limits is a function of the field count and the tool version; layoutOnly says that no per-field cap or sketch is in use.
        var source = Synthetic().Single(static s => s.Name == "wide-2000-columns").Source;
        var full = Profiler.Profile(source(), Full());
        var layout = Profiler.Profile(source(), Layout());
        Assert.Equal(full.Limits, layout.Limits);
        Assert.Equal(12, layout.Limits.Sketches!.HllPrecision);
        Assert.Equal(1_000, layout.Limits.DistinctPerField);
        Assert.Equal(8_000, layout.Limits.ShapeCharsPerField);
    }

    private static (MemoryTrace Trace, double Seconds, Descriptor Descriptor) Timed(IProfileSource source, ProfileOptions options)
    {
        var trace = new MemoryTrace();
        var clock = Stopwatch.StartNew();
        var descriptor = Profiler.Profile(source, options, ProfileLimits.Default, trace);
        return (trace, clock.Elapsed.TotalSeconds, descriptor);
    }

    private void Print(string what, (MemoryTrace Trace, double Seconds, Descriptor Descriptor) full, (MemoryTrace Trace, double Seconds, Descriptor Descriptor) layout)
    {
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{what}: full {full.Seconds:0.00} s, estimated {full.Trace.PeakEstimatedBytes} bytes; layout-only {layout.Seconds:0.00} s, estimated {layout.Trace.PeakEstimatedBytes} bytes ({100.0 * layout.Trace.PeakEstimatedBytes / full.Trace.PeakEstimatedBytes:0.0} percent of the full profile; speed-up {full.Seconds / layout.Seconds:0.0}x)"));
    }

    [Fact]
    public void Cheaper_WidePaddedShape_64MiB_HoldsNoPerFieldState_ButTheHeaderSampleStaysInBoth()
    {
        // The figures are printed; the assertions are on the estimate, never on the timing.
        const long bytes = 64L * 1024 * 1024;
        var rows = SyntheticCsvSource.PaddedRowsFor(bytes, ThroughputBench.WideColumns, ThroughputBench.WideCellWidth);
        IProfileSource Source() => SyntheticCsvSource.PaddedWide(rows, ThroughputBench.WideColumns, ThroughputBench.WideCellWidth);

        var layout = Timed(Source(), Layout());
        var full = Timed(Source(), Full());
        Print($"wide padded {bytes} bytes, {rows} rows x {ThroughputBench.WideColumns} columns", full, layout);

        Assert.Equal(rows, layout.Descriptor.Table!.Rows);
        Assert.Equal(ThroughputBench.WideColumns, layout.Descriptor.Table.Fields.Count);
        Assert.Equal(Compared(full.Descriptor), Compared(layout.Descriptor));

        // The full profile holds an accumulator for each of the 256 fields (their exact buffers, shape masks and null spellings:
        // about 5 MB here); the layout-only profile holds none. What both hold is the header sample, the first 1,000 records
        // of 256 cells, which the header decision needs: 90 percent of either estimate for this shape, so the estimate is not
        // "a small fraction" of the full profile's here.
        Assert.True(full.Trace.PeakEstimatedBytes - layout.Trace.PeakEstimatedBytes > 2L * 1024 * 1024, $"layout-only {layout.Trace.PeakEstimatedBytes} against full {full.Trace.PeakEstimatedBytes}");
        Assert.True(layout.Trace.PeakEstimatedBytes < full.Trace.PeakEstimatedBytes, $"layout-only {layout.Trace.PeakEstimatedBytes} against full {full.Trace.PeakEstimatedBytes}");
    }

    [Fact]
    public void Cheaper_WidePaddedPrefix_OfTheSizeAConsumerReads_IsAFractionOfTheFullProfile()
    {
        // The consumer's case: 1 MiB of a wide padded file holds about 64 rows of 256 cells.
        var rows = SyntheticCsvSource.PaddedRowsFor(64L * 1024 * 1024, ThroughputBench.WideColumns, ThroughputBench.WideCellWidth);
        IProfileSource Source() => SyntheticCsvSource.PaddedWide(rows, ThroughputBench.WideColumns, ThroughputBench.WideCellWidth);
        var layout = Timed(Source(), Layout(maxBytes: 1024 * 1024));
        var full = Timed(Source(), Full(1024 * 1024));
        Print("wide padded, a 1 MiB prefix", full, layout);
        Assert.Equal(Compared(full.Descriptor), Compared(layout.Descriptor));
        Assert.True(layout.Descriptor.Table!.Rows is > 30 and < 100, "rows: " + layout.Descriptor.Table.Rows.ToString(CultureInfo.InvariantCulture));
        Assert.True(layout.Trace.PeakEstimatedBytes < full.Trace.PeakEstimatedBytes / 2, $"layout-only {layout.Trace.PeakEstimatedBytes} against full {full.Trace.PeakEstimatedBytes}");
    }

    private sealed class CountingSource(IProfileSource inner) : IProfileSource
    {
        public int Opens { get; private set; }

        public string Name => inner.Name;

        public long Length => inner.Length;

        public Stream Open()
        {
            Opens++;
            return inner.Open();
        }
    }

    [Fact]
    public void Cheaper_ThereIsNoStableLineRead_OneReadOfTheSourceLessThanAFullProfile()
    {
        // A full profile re-reads the start of the file for the text of the header and key lines (the hints); a layout-only one has no hints.
        var fixture = Fixtures().Single(static f => f.Name == "lab/sweep-preamble.csv").Source;
        var fullSource = new CountingSource(fixture());
        var layoutSource = new CountingSource(fixture());
        Profiler.Profile(fullSource, Full());
        Profiler.Profile(layoutSource, Layout());
        Assert.True(layoutSource.Opens >= 3, "the byte pass, the scoring pass and the profile pass: " + layoutSource.Opens.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(fullSource.Opens - 1, layoutSource.Opens);
    }

    [Fact]
    public void Cheaper_NoAccumulatorIsMade_EvenForAWideTableOfFewRows()
    {
        // Each field accumulator has a fixed share (its object and small arrays: about 1.5 KB). 2,000 fields x 3 rows have almost
        // no values to hold, so the estimate shows whether an accumulator exists at all.
        var source = Text("t.csv", Wide(2000, 3));
        var layout = Timed(source, Layout());
        var full = Timed(source, Full());
        Print("wide, 2,000 columns x 3 rows", full, layout);
        Assert.Equal(2000, layout.Descriptor.Table!.Fields.Count);
        Assert.True(full.Trace.PeakEstimatedBytes - layout.Trace.PeakEstimatedBytes > 2_000_000, $"layout-only {layout.Trace.PeakEstimatedBytes} against full {full.Trace.PeakEstimatedBytes}");
        Assert.True(layout.Trace.PeakEstimatedBytes < 1_500_000, "layout-only estimate: " + layout.Trace.PeakEstimatedBytes.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Cheaper_ATableWithManyRows_HoldsNoSketches_AndTheEstimateIsASmallFractionOfTheFullProfiles()
    {
        // Past the exact caps the full profile holds a sketch for each field; the layout-only estimate does not grow with the rows.
        var source = new SyntheticCsvSource(150_000, 12);
        var layout = Timed(source, Layout());
        var full = Timed(source, Full());
        Print("plain, 150,000 rows x 12 columns", full, layout);
        Assert.Equal(150_000, layout.Descriptor.Table!.Rows);
        Assert.True(layout.Trace.PeakEstimatedBytes < full.Trace.PeakEstimatedBytes / 5, $"layout-only {layout.Trace.PeakEstimatedBytes} against full {full.Trace.PeakEstimatedBytes}");
        Assert.True(layout.Trace.PeakEstimatedBytes < 2L * 1024 * 1024, "layout-only estimate: " + layout.Trace.PeakEstimatedBytes.ToString(CultureInfo.InvariantCulture));
    }
}
