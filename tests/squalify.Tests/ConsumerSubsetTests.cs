// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// Pins the "Consumer subset" of <c>docs/descriptor.md</c>: the members a downstream classifier relies on, and the formulas
/// by which a consumer derives its own view from them. Fixtures are synthetic.
/// </summary>
public sealed class ConsumerSubsetTests
{
    // Every member path of the subset, as written in docs/descriptor.md. "[]" iterates an array.
    private static readonly string[] SubsetPaths =
    [
        "schemaVersion",
        "limits.nullTokens",
        "limits.unitTable",
        "file.name",
        "format.family",
        "format.dialect.delimiter",
        "format.dialect.quote",
        "format.dialect.hasHeader",
        "format.dialect.blocks",
        "encoding.bom",
        "encoding.lineEndings.crlf",
        "encoding.lineEndings.lf",
        "encoding.lineEndings.cr",
        "table.rows",
        "table.rowClasses.allNullRows",
        "table.rowClasses.truncated",
        "table.rowClasses.classes[].count",
        "table.fields[].index",
        "table.fields[].name",
        "table.fields[].rawName",
        "table.fields[].nameParts.base",
        "table.fields[].nameParts.unit",
        "table.fields[].nameParts.convention",
        "table.fields[].nameParts.unknownUnit",
        "table.fields[].type",
        "table.fields[].typeVotes",
        "table.fields[].count",
        "table.fields[].nulls",
        "table.fields[].nullsExcludingAllNullRows",
        "table.fields[].nullTokens",
        "table.fields[].constant",
        "table.fields[].statsWithheld",
        "issues[].code",
        "issues[].count",
    ];

    private static ProfileOptions Options(bool includeValues = false) => ProfileOptions.Create(includeValues, Array.Empty<string>());

    private static Descriptor ProfileText(string text, bool includeValues = false, ProfileLimits? limits = null)
    {
        var source = new BytesSource("t.csv", Encoding.UTF8.GetBytes(text));
        return limits is null
            ? Profiler.Profile(source, Options(includeValues))
            : Profiler.Profile(source, Options(includeValues), limits);
    }

    private static Descriptor ProfileFixture(string name, bool includeValues = false)
    {
        return Profiler.Profile(TestBytes.Fixture("consumer/" + name), Options(includeValues));
    }

    private static FieldProfile Field(Descriptor d, string name)
    {
        return d.Table!.Fields.Single(f => string.Equals(f.Name, name, StringComparison.Ordinal));
    }

    private static string Id(params int[] nonNull)
    {
        var text = string.Join(",", nonNull.Select(static i => i.ToString(CultureInfo.InvariantCulture)));
        return "rc:" + XxHash64.Hash(Encoding.UTF8.GetBytes(text)).ToString("x16", CultureInfo.InvariantCulture);
    }

    private static (string Key, long Value)[] Pairs(CountMap map)
    {
        return map.Select(static e => (e.Key, e.Value)).ToArray();
    }

    private static RowClassesInfo Classes(Descriptor d) => Assert.IsType<RowClassesInfo>(d.Table!.RowClasses);

    private static long Ragged(Descriptor d) => d.Issues.SingleOrDefault(static i => i.Code == "raggedRows")?.Count ?? 0;

    // 20 records under "A(id),B,C": two ragged (records 9 and 16), five classes counting the all-null separator.
    private static string RaggedTable()
    {
        var builder = new StringBuilder("A(id),B,C\n");
        for (var i = 1; i <= 20; i++)
        {
            var line = i switch
            {
                3 => "3,NULL,y",
                6 => "6,x,NULL",
                8 => "8,NULL,NULL",
                9 => "9,x",
                11 => "NULL,NULL,NULL",
                16 => "16,x,y,z",
                _ => i.ToString(CultureInfo.InvariantCulture) + ",x,y",
            };
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    // The consumer formulas of docs/descriptor.md, written once here.
    private static long DataRows(Descriptor d) => d.Table!.Rows - Classes(d).AllNullRows;

    private static bool DerivedConstant(Descriptor d, FieldProfile f)
    {
        var excl = f.NullsExcludingAllNullRows;
        return f.Constant == true && (excl == 0 || excl == f.Count - Classes(d).AllNullRows);
    }

    // True when every null of every field is spelled with the null-vocabulary entry "null" (NULL, Null or null).
    private static bool NullOnly(Descriptor d)
    {
        return d.Table!.Fields.All(static f => f.NullTokens!.All(static e => e.Key == "null"));
    }

    private static string Mapped(FieldProfile f)
    {
        var parts = f.NameParts!;
        if (!string.Equals(parts.Convention, "nameParenUnit", StringComparison.Ordinal))
        {
            return "None";
        }

        return string.Equals(parts.Unit, "id", StringComparison.Ordinal) ? "Id" : "Unit";
    }

    private static IEnumerable<(string Path, string Json)> Resolve(JsonElement element, string[] segments, int index, string prefix)
    {
        if (index == segments.Length)
        {
            yield return (prefix, element.GetRawText());
            yield break;
        }

        var segment = segments[index];
        var isArray = segment.EndsWith("[]", StringComparison.Ordinal);
        var name = isArray ? segment[..^2] : segment;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var child))
        {
            yield return (prefix + "." + name, "<missing>");
            yield break;
        }

        if (!isArray)
        {
            foreach (var r in Resolve(child, segments, index + 1, prefix + "." + name))
            {
                yield return r;
            }

            yield break;
        }

        var k = 0;
        foreach (var item in child.EnumerateArray())
        {
            foreach (var r in Resolve(item, segments, index + 1, prefix + "." + name + "[" + k.ToString(CultureInfo.InvariantCulture) + "]"))
            {
                yield return r;
            }

            k++;
        }
    }

    private static string SubsetProjection(Descriptor d)
    {
        using var doc = JsonDocument.Parse(DescriptorWriter.ToBytes(d));
        var builder = new StringBuilder();
        foreach (var path in SubsetPaths)
        {
            foreach (var (p, json) in Resolve(doc.RootElement, path.Split('.'), 0, string.Empty))
            {
                builder.Append(p).Append('=').Append(json).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string DocumentedSection()
    {
        var root = Path.GetFullPath(Path.Combine(Golden.SourceFixturesRoot(), "..", "..", "..", "docs", "descriptor.md"));
        Assert.True(File.Exists(root), "docs/descriptor.md not found at " + root);
        var text = File.ReadAllText(root);
        var start = text.IndexOf("\n## Consumer subset\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/descriptor.md has no '## Consumer subset' section");
        var next = text.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
        return next < 0 ? text[start..] : text[start..next];
    }

    // ---- The spec's four scenarios ------------------------------------------------------------------------------------

    [Fact]
    public void LabSweep_GivesDialectHeaderConventionsRowClassesAndFieldFacts()
    {
        var d = ProfileFixture("lab-sweep.csv");

        Assert.Equal("delimited", d.Format.Family);
        var dialect = Assert.IsType<DelimitedDialect>(d.Format.Dialect);
        Assert.Equal(",", dialect.Delimiter);
        Assert.Equal("\"", dialect.Quote);
        Assert.True(dialect.HasHeader);
        Assert.False(dialect.UnitsRow);

        string[] names = ["RowID(id)", "SweepID(id)", "Mode", "Vcc(V)", "Freq(MHz)", "Pout(dBm)", "EVM(dB)", "Err"];
        Assert.Equal(names, d.Table!.Fields.Select(static f => f.Name).ToArray());
        Assert.Equal(Enumerable.Range(0, 8).Select(static i => (long)i).ToArray(), d.Table.Fields.Select(static f => f.Index).ToArray());

        // base, unit, convention, unknownUnit; "id" is not in the unit table, so RowID(id) is an unknown unit.
        (string, string?, string, bool)[] parts =
        [
            ("RowID", "id", "nameParenUnit", true),
            ("SweepID", "id", "nameParenUnit", true),
            ("Mode", null, "none", false),
            ("Vcc", "V", "nameParenUnit", false),
            ("Freq", "MHz", "nameParenUnit", false),
            ("Pout", "dBm", "nameParenUnit", false),
            ("EVM", "dB", "nameParenUnit", false),
            ("Err", null, "none", false),
        ];
        Assert.Equal(parts, d.Table.Fields.Select(static f => (f.NameParts!.Base, f.NameParts.Unit, f.NameParts.Convention, f.NameParts.UnknownUnit)).ToArray());
        Assert.Equal(new[] { "Id", "Id", "None", "Unit", "Unit", "Unit", "Unit", "None" }, d.Table.Fields.Select(Mapped).ToArray());

        // Eight data rows and three all-null separators; the failed row 6 has fields 0-4 and 7 filled.
        var rc = Classes(d);
        Assert.Equal(11, d.Table.Rows);
        Assert.Equal(3, rc.AllNullRows);
        Assert.Equal(0, rc.Truncated);
        Assert.Equal(3, rc.Count);
        Assert.Equal(7, rc.Classes.Single(static c => c.Id == Id(0, 1, 2, 3, 4, 5, 6)).Count);
        Assert.Equal(1, rc.Classes.Single(static c => c.Id == Id(0, 1, 2, 3, 4, 7)).Count);
        var allNull = rc.Classes.Single(static c => c.Id == Id());
        Assert.Equal(3, allNull.Count);
        Assert.Empty(allNull.Fields);

        // type, count, nulls, nullsExcludingAllNullRows, constant
        (string Name, string Type, long Nulls, long NullsExcl, bool Constant)[] expected =
        [
            ("RowID(id)", "int", 3, 0, false),
            ("SweepID(id)", "int", 3, 0, false),
            ("Mode", "string", 3, 0, false),
            ("Vcc(V)", "float", 3, 0, true),
            ("Freq(MHz)", "int", 3, 0, false),
            ("Pout(dBm)", "float", 4, 1, false),
            ("EVM(dB)", "float", 4, 1, false),
            ("Err", "string", 10, 7, true),
        ];
        foreach (var e in expected)
        {
            var f = Field(d, e.Name);
            Assert.Equal(e.Type, f.Type);
            Assert.Equal(11, f.Count);
            Assert.Equal(e.Nulls, f.Nulls);
            Assert.Equal(e.NullsExcl, f.NullsExcludingAllNullRows);
            Assert.Equal(e.Constant, f.Constant);
            Assert.Equal(3, f.Nulls - f.NullsExcludingAllNullRows);
        }

        // Every null of a separator is spelled NULL in this file, so the null-vocabulary key is "null".
        Assert.Equal(new[] { ("null", 3L) }, Pairs(Field(d, "RowID(id)").NullTokens!));
        Assert.Equal(new[] { ("null", 4L) }, Pairs(Field(d, "Pout(dBm)").NullTokens!));
        Assert.Equal(new[] { ("null", 10L) }, Pairs(Field(d, "Err").NullTokens!));
        Assert.Empty(d.Issues);
    }

    [Fact]
    public void OneConditionCapture_ConstantTrueForConstantFields_FalseForVaryingOnes()
    {
        var d = ProfileFixture("one-condition-capture.csv");
        Assert.Equal(6, d.Table!.Rows);
        Assert.Equal(0, Classes(d).AllNullRows);
        Assert.Equal(1, Classes(d).Count);

        (string Name, string Type, bool Constant)[] expected =
        [
            ("Sample(s)", "float", false),
            ("Pout(dBm)", "float", false),
            ("Vcc(V)", "float", true),
            ("Temp(C)", "int", true),
            ("Mode", "string", true),
            ("Site", "string", true),
        ];
        foreach (var e in expected)
        {
            var f = Field(d, e.Name);
            Assert.Equal(e.Type, f.Type);
            Assert.Equal(e.Constant, f.Constant);
            Assert.Equal(0, f.Nulls);
            Assert.All(d.Table.Fields, x => Assert.Equal(6, x.Count));
        }
    }

    [Fact]
    public void ValuesOff_ConstantFieldIsReportedConstantWithoutItsValue()
    {
        var off = ProfileFixture("one-condition-capture.csv");
        var vcc = Field(off, "Vcc(V)");
        Assert.True(vcc.Constant);
        Assert.Equal("constant", vcc.StatsWithheld);
        Assert.Null(vcc.Numeric);

        // A constant text field has no statistics to withhold: statsWithheld stays null, and top and text are values tier.
        var mode = Field(off, "Mode");
        Assert.True(mode.Constant);
        Assert.Null(mode.StatsWithheld);
        Assert.Null(mode.Top);
        Assert.Null(mode.Text);

        // The varying float field keeps its statistics.
        Assert.NotNull(Field(off, "Pout(dBm)").Numeric);
        Assert.Null(Field(off, "Pout(dBm)").StatsWithheld);

        var json = Encoding.UTF8.GetString(DescriptorWriter.ToBytes(off));
        Assert.DoesNotContain("3.375", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ZXQ7", json, StringComparison.Ordinal);

        var on = ProfileFixture("one-condition-capture.csv", includeValues: true);
        Assert.True(Field(on, "Vcc(V)").Constant);
        Assert.Null(Field(on, "Vcc(V)").StatsWithheld);
        Assert.NotNull(Field(on, "Vcc(V)").Numeric);
        // top and text are values tier: with the flag a constant text field lists its one value.
        var onMode = Field(on, "Mode");
        Assert.Equal("ZXQ7", Assert.Single(onMode.Top!.Items).Value);
        Assert.Equal(new TextStats("ZXQ7", "ZXQ7"), onMode.Text);
    }

    [Fact]
    public void ConsumerSubset_IsByteIdenticalAcrossRuns()
    {
        foreach (var fixture in new[] { "lab-sweep.csv", "one-condition-capture.csv" })
        {
            var first = ProfileFixture(fixture);
            var second = ProfileFixture(fixture);
            Assert.Equal(SubsetProjection(first), SubsetProjection(second));
            Assert.Equal(DescriptorWriter.ToBytes(first), DescriptorWriter.ToBytes(second));
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void ConsumerSubset_IsByteIdenticalUnderAnotherCulture(string cultureName)
    {
        var expected = new[] { "lab-sweep.csv", "one-condition-capture.csv" }.Select(static f => SubsetProjection(ProfileFixture(f))).ToArray();
        var expectedBytes = new[] { "lab-sweep.csv", "one-condition-capture.csv" }.Select(static f => DescriptorWriter.ToBytes(ProfileFixture(f))).ToArray();
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            var culture = new CultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            var fixtures = new[] { "lab-sweep.csv", "one-condition-capture.csv" };
            for (var i = 0; i < fixtures.Length; i++)
            {
                var d = ProfileFixture(fixtures[i]);
                Assert.Equal(expected[i], SubsetProjection(d));
                Assert.Equal(expectedBytes[i], DescriptorWriter.ToBytes(d));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    // ---- The documented members exist, and the document lists them ----------------------------------------------------

    [Fact]
    public void EveryDocumentedMember_IsWrittenForADelimitedTable()
    {
        // A table with ragged records, so that issues[] is not empty.
        var d = ProfileText(RaggedTable());
        using var doc = JsonDocument.Parse(DescriptorWriter.ToBytes(d));
        foreach (var path in SubsetPaths)
        {
            var found = Resolve(doc.RootElement, path.Split('.'), 0, string.Empty).ToArray();
            Assert.NotEmpty(found);
            Assert.DoesNotContain(found, static f => f.Json == "<missing>");
        }

        // The members that hold null for a table with a header are still written.
        var lab = ProfileFixture("lab-sweep.csv");
        using var labDoc = JsonDocument.Parse(DescriptorWriter.ToBytes(lab));
        Assert.Equal(
            8,
            Resolve(labDoc.RootElement, "table.fields[].constant".Split('.'), 0, string.Empty).Count(static f => f.Json is "true" or "false"));
        Assert.Equal(
            8,
            Resolve(labDoc.RootElement, "table.fields[].nameParts.unit".Split('.'), 0, string.Empty).Count());
    }

    [Fact]
    public void Blocks_AreCounted_AndRowsAndRaggedRowsCoverOnlyTheProfiledBlocks()
    {
        static string Block(string site, string header, int rows)
        {
            var builder = new StringBuilder("Site=" + site + "\n" + header + "\n");
            for (var i = 0; i < rows; i++)
            {
                builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',').Append((i * 2).ToString(CultureInfo.InvariantCulture)).Append(",x\n");
            }

            return builder.ToString();
        }

        // Same header: one table, the rows of every block, the preambles of the later blocks are not ragged rows.
        var same = ProfileText(Block("A", "a,b,c", 6) + Block("B", "a,b,c", 6));
        Assert.Equal(2, ((DelimitedDialect)same.Format.Dialect!).Blocks);
        Assert.Equal(12, same.Table!.Rows);
        Assert.Equal(0, Ragged(same));
        Assert.Equal(same.Table.Rows, Classes(same).Classes.Sum(static c => c.Count) + Classes(same).Truncated + Ragged(same));

        // Another header in block 1: its records are in no member of the subset; the issue says so.
        var other = ProfileText(Block("A", "a,b,c", 6) + Block("B", "d,e,f", 6));
        Assert.Equal(2, ((DelimitedDialect)other.Format.Dialect!).Blocks);
        Assert.Equal(6, other.Table!.Rows);
        Assert.All(other.Table.Fields, static f => Assert.Equal(6, f.Count));
        Assert.Contains(other.Issues, static i => i.Code == "heterogeneousBlocks");
        Assert.Equal(1, ((DelimitedDialect)ProfileText(RaggedTable()).Format.Dialect!).Blocks);
    }

    [Fact]
    public void Document_ListsEveryMemberOfTheSubset()
    {
        var section = DocumentedSection();
        foreach (var path in SubsetPaths)
        {
            Assert.Contains("`" + path + "`", section, StringComparison.Ordinal);
        }
    }

    // ---- The derivation formulas --------------------------------------------------------------------------------------

    [Fact]
    public void DataRows_IsRowsMinusAllNullRows_AndMatchesTheNonSeparatorLinesOfTheFile()
    {
        var d = ProfileFixture("lab-sweep.csv");
        var lines = File.ReadAllLines(TestBytes.Fixture("consumer/lab-sweep.csv")).Skip(1).ToArray();
        var separators = lines.Count(static l => l == "NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL");
        Assert.Equal(lines.Length - separators, DataRows(d));
        Assert.Equal(8, DataRows(d));

        // With no ragged record, every field's count is rows, so count - allNullRows is the same number.
        Assert.All(d.Table!.Fields, f => Assert.Equal(DataRows(d), f.Count - Classes(d).AllNullRows));
        // nulls - nullsExcludingAllNullRows is allNullRows for every field.
        Assert.All(d.Table.Fields, f => Assert.Equal(Classes(d).AllNullRows, f.Nulls - f.NullsExcludingAllNullRows));
    }

    [Fact]
    public void RowInvariant_RowsEqualClassCountsPlusTruncatedPlusRagged()
    {
        var lab = ProfileFixture("lab-sweep.csv");
        Assert.Equal(lab.Table!.Rows, Classes(lab).Classes.Sum(static c => c.Count) + Classes(lab).Truncated + Ragged(lab));

        // Five classes and two ragged records; a cap of 2 keeps the first two classes seen and truncates the rest.
        var text = RaggedTable();
        var plain = ProfileText(text);
        var all = Classes(plain);
        Assert.Equal(20, plain.Table!.Rows);
        Assert.Equal(2, Ragged(plain));
        Assert.Equal(5, all.Count);
        Assert.Equal(0, all.Truncated);
        Assert.Equal(1, all.AllNullRows);
        Assert.Equal(plain.Table.Rows, all.Classes.Sum(static c => c.Count) + all.Truncated + Ragged(plain));

        var capped = ProfileText(text, limits: ProfileLimits.Default with { RowClasses = 2 });
        var rc = Classes(capped);
        Assert.Equal(2, rc.Classes.Count);
        Assert.Equal(3, rc.Truncated);
        Assert.Equal(1, rc.AllNullRows);
        Assert.Equal(capped.Table!.Rows, rc.Classes.Sum(static c => c.Count) + rc.Truncated + Ragged(capped));

        // Ragged records are data rows to DataRows: rows - allNullRows includes them.
        Assert.Equal(19, DataRows(plain));
        Assert.Equal(18, plain.Table.Fields[0].Count);
    }

    [Fact]
    public void RaggedRecords_AreCountedInRows_AndInNoFieldCountOrNulls()
    {
        var d = ProfileText(RaggedTable());
        Assert.Equal(20, d.Table!.Rows);
        Assert.Equal(2, Ragged(d));
        Assert.Equal(1, Classes(d).AllNullRows);
        Assert.Equal(19, DataRows(d));
        Assert.All(d.Table.Fields, f => Assert.Equal(18, f.Count));
        Assert.All(d.Table.Fields, f => Assert.Equal(f.Nulls, f.NullTokens!.Sum(static e => e.Value)));
        // The ragged "9,x" has no value for B or C that could be null: A has only the separator's null.
        Assert.Equal(1, d.Table.Fields[0].Nulls);
        Assert.Equal(d.Table.Rows - Ragged(d), Classes(d).Classes.Sum(static c => c.Count));
    }

    [Fact]
    public void D11Constant_IsConstantAndNoNullsOrOnlyNulls_OverTheDataRows()
    {
        var d = ProfileFixture("lab-sweep.csv");
        // Vcc is 3.3 in every data row; the separators are nulls but are not data rows.
        Assert.True(DerivedConstant(d, Field(d, "Vcc(V)")));
        // Err is constant (one value, E01) but NULL in seven of eight data rows: not constant when NULL is a value.
        Assert.True(Field(d, "Err").Constant);
        Assert.False(DerivedConstant(d, Field(d, "Err")));
        Assert.False(DerivedConstant(d, Field(d, "Freq(MHz)")));
        Assert.False(DerivedConstant(d, Field(d, "Pout(dBm)")));

        // The shorter formula on nulls and count is wrong when the table has separator rows.
        var vcc = Field(d, "Vcc(V)");
        Assert.False(vcc.Constant == true && (vcc.Nulls == 0 || vcc.Nulls == vcc.Count));

        // No separators: an all-null column is constant, a column of one value and some nulls is not, and a column
        // that is NULL in every data row is constant again.
        var plain = ProfileText("RowID(id),Mode,Spare,Note,Setting\n1,CW,NULL,x,5\n2,CW,NULL,NULL,5\n3,CW,NULL,y,NULL\n");
        Assert.True(Field(plain, "Spare").Constant);
        Assert.True(DerivedConstant(plain, Field(plain, "Spare")));
        Assert.True(DerivedConstant(plain, Field(plain, "Mode")));
        Assert.True(Field(plain, "Setting").Constant);
        Assert.False(DerivedConstant(plain, Field(plain, "Setting")));
        Assert.False(DerivedConstant(plain, Field(plain, "Note")));
        Assert.False(DerivedConstant(plain, Field(plain, "RowID(id)")));

        // A column that is NULL in every data row of a table with separators is constant under D11 too.
        var sep = ProfileText("RowID(id),Mode,Spare\n1,CW,NULL\n2,CW,NULL\nNULL,NULL,NULL\n3,CW,NULL\n");
        Assert.Equal(1, Classes(sep).AllNullRows);
        Assert.True(Field(sep, "Spare").Constant);
        Assert.True(DerivedConstant(sep, Field(sep, "Spare")));
        Assert.True(DerivedConstant(sep, Field(sep, "Mode")));
    }

    [Fact]
    public void Constant_IgnoresNulls_AndSeparatorRowsDoNotBreakIt()
    {
        var d = ProfileText("A(id),Setting,Gone\n1,5,NULL\n2,NULL,NULL\nNULL,NULL,NULL\n3,5,NULL\n");
        Assert.True(Field(d, "Setting").Constant);
        Assert.True(Field(d, "Gone").Constant);
        Assert.False(Field(d, "A(id)").Constant);
    }

    [Fact]
    public void Constant_ComparesParsedValues_ForNumbers_AndTextForOtherTypes()
    {
        // 1, 1.0 and 01 parse to the same number: constant, although distinct and unique see three texts.
        var d = ProfileText("A(id),V(x)\n1,1\n2,1.0\n3,01\n");
        var v = Field(d, "V(x)");
        Assert.Equal("float", v.Type);
        Assert.True(v.Constant);
        Assert.Equal(3, v.Distinct!.Value);
        Assert.True(v.Unique);

        // A text field is compared as trimmed text, case-sensitively.
        var text = ProfileText("A(id),T\n1,Alpha\n2,alpha\n3,Alpha\n");
        Assert.Equal("string", Field(text, "T").Type);
        Assert.False(Field(text, "T").Constant);
    }

    // ---- Differences from the consumer ---------------------------------------------------------------------------------

    [Fact]
    public void NullTokens_KeysAreVocabularyEntries_NotSpellings()
    {
        var d = ProfileText("A(id),V\n1,NULL\n2,Null\n3,null\n4,NaN\n5,\n6,  \n7,NA\n8,na\n9,N/A\n10,NA.\n11,-\n12,x\n");
        var v = Field(d, "V");
        Assert.Equal(
            new[] { ("", 2L), ("-", 1L), ("N/A", 1L), ("NA", 2L), ("NA.", 1L), ("NaN", 1L), ("null", 3L) },
            Pairs(v.NullTokens!));
        Assert.Equal(11, v.Nulls);
        Assert.Equal(v.Nulls, v.NullTokens!.Sum(static e => e.Value));
        Assert.Equal("string", v.Type);
        Assert.Equal(1, v.TypeVotes.Single(static e => e.Key == "string").Value);
    }

    [Fact]
    public void NaN_IsNull_AndTheOtherNonFiniteSpellingsAreStrings()
    {
        var nan = Field(ProfileText("A(id),V(x)\n1,1.5\n2,NaN\n3,2.5\n"), "V(x)");
        Assert.Equal("float", nan.Type);
        Assert.Equal(1, nan.Nulls);
        Assert.Equal(new[] { ("NaN", 1L) }, Pairs(nan.NullTokens!));

        foreach (var spelling in new[] { "-NaN", "+NaN", "nan", "Infinity", "-Infinity", "+Infinity", "inf" })
        {
            var f = Field(ProfileText("A(id),V(x)\n1,1.5\n2," + spelling + "\n3,2.5\n"), "V(x)");
            var expectNull = spelling == "nan";
            Assert.Equal(expectNull ? "float" : "string", f.Type);
            Assert.Equal(expectNull ? 1 : 0, f.Nulls);
            Assert.Equal(expectNull ? 0 : 1, f.TypeVotes.SingleOrDefault(static e => e.Key == "string").Value);
        }
    }

    [Fact]
    public void BigIntegers_AreIntUpToInt64_FloatUpTo309Digits_ThenString()
    {
        static string Type(string value) => Field(ProfileText("A(id),V\n1," + value + "\n2,1\n3,2\n"), "V").Type;

        Assert.Equal("int", Type("9223372036854775807"));
        Assert.Equal("float", Type("9223372036854775808"));
        Assert.Equal("float", Type("1" + new string('0', 19)));
        Assert.Equal("float", Type("1" + new string('0', 308)));
        Assert.Equal("string", Type("1" + new string('0', 309)));
    }

    [Fact]
    public void AllNullRows_UseTheNullVocabulary_NotOnlyTheNullToken()
    {
        var d = ProfileText("RowID(id),Mode,Freq(MHz),Err\n1,CW,2400,NULL\nNULL,NULL,NULL,NULL\nNA,N/A,,-\nnull,NaN,NULL,\n2,CW,2410,NULL\n");
        var rc = Classes(d);
        Assert.Equal(5, d.Table!.Rows);
        Assert.Equal(3, rc.AllNullRows);
        Assert.Equal(2, DataRows(d));
        Assert.Equal(0, Field(d, "RowID(id)").NullsExcludingAllNullRows);
    }

    [Fact]
    public void NullOnlyFile_GivesTheConsumerItsOwnNullView()
    {
        // Every null of the lab sweep is spelled NULL, so the descriptor's null view and a NULL-only reader agree.
        var d = ProfileFixture("lab-sweep.csv");
        Assert.True(NullOnly(d));
        var all = Classes(d).AllNullRows;
        var expectedNullOnly = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["RowID(id)"] = 0,
            ["Mode"] = 0,
            ["Pout(dBm)"] = 1,
            ["Err"] = 7,
        };
        foreach (var (name, expected) in expectedNullOnly)
        {
            var f = Field(d, name);
            // Nulls over the data rows: the same number, and the NULL-key count less the separators.
            Assert.Equal(expected, f.NullsExcludingAllNullRows);
            Assert.Equal(expected, f.NullTokens!.Single(static e => e.Key == "null").Value - all);
        }

        // Another spelling anywhere in the file: the squalify view is wider than a NULL-only reader's.
        var mixed = ProfileText("RowID(id),Mode,Freq(MHz),Err\n1,CW,2400,NULL\nNULL,NULL,NULL,NULL\nNA,N/A,,-\n2,CW,2410,NA\n3,CW,2420,NULL\n4,CW,2430,NULL\n");
        Assert.False(NullOnly(mixed));
        Assert.Equal(2, Classes(mixed).AllNullRows);
        Assert.Contains(Field(mixed, "Err").NullTokens!, static e => e.Key == "-");
    }

    [Fact]
    public void ConstantFalse_ImpliesNotTextConstant_ButConstantTrueDoesNot()
    {
        foreach (var fixture in new[] { "lab-sweep.csv", "one-condition-capture.csv" })
        {
            var d = ProfileFixture(fixture);
            var rows = File.ReadAllLines(TestBytes.Fixture("consumer/" + fixture)).Skip(1).Select(static l => l.Split(',')).ToArray();
            foreach (var f in d.Table!.Fields)
            {
                var texts = rows.Select(r => r[f.Index]).Where(static t => t != "NULL").Distinct(StringComparer.Ordinal).ToArray();
                var textConstant = texts.Length <= 1;
                if (f.Constant == false)
                {
                    Assert.False(textConstant);
                }
                else
                {
                    Assert.True(textConstant);
                }
            }
        }

        // 1, 1.0 and 01: constant, yet three texts. constant true does not imply the same text.
        var spelled = Field(ProfileText("A(id),V(x)\n1,1\n2,1.0\n3,01\n"), "V(x)");
        Assert.True(spelled.Constant);
    }

    [Fact]
    public void NonNumericVotes_BoundTheMisfitsOfAUnitColumn_OnlyInANullOnlyFileWithoutPadding()
    {
        static long NonNumeric(FieldProfile f) => f.TypeVotes.Sum(static e => e.Value) - f.TypeVotes.Where(static e => e.Key is "int" or "float").Sum(static e => e.Value);

        // A NULL-only file with no padded value: the votes count the values a number-only reader cannot read, except
        // that Infinity and -Infinity are readable to a consumer that accepts them.
        var d = ProfileText("A(id),V(x),W(y),Z(z)\n1,1.5,1,abc\n2,Infinity,2,3\n3,NULL,3,4\n4,2,4,5\n");
        Assert.True(NullOnly(d));
        Assert.All(d.Table!.Fields, static f => Assert.Equal(0, f.Flags!.Padded));
        Assert.Equal(1, NonNumeric(Field(d, "V(x)")));
        Assert.Equal(0, NonNumeric(Field(d, "W(y)")));
        Assert.Equal(1, NonNumeric(Field(d, "Z(z)")));

        // Counterexample 1: other null spellings are nulls, not votes, but a consumer that reads only NULL as null
        // counts each as a misfit. NonNumeric is 0 here although four values are misfits to it. The file is not NULL-only.
        var spellings = ProfileText("A(id),V(x)\n1,1.5\n2,NA\n3,\n4,-\n5,nan\n6,2\n");
        Assert.Equal(0, NonNumeric(Field(spellings, "V(x)")));
        Assert.Equal(4, Field(spellings, "V(x)").Nulls);
        Assert.False(NullOnly(spellings));

        // Counterexample 2: a padded value is a number vote, but a consumer that reads the text as written fails it.
        var padded = ProfileText("A(id),V(x)\n1,1.5\n2, 2.5 \n3,3\n");
        Assert.Equal(0, NonNumeric(Field(padded, "V(x)")));
        Assert.Equal(1, Field(padded, "V(x)").Flags!.Padded);

        // Counterexample 3: sign and comma-decimal syntax. A signed value is a number vote even in a NULL-only file
        // without padding, and a comma decimal in a semicolon file is a number vote too.
        var signed = ProfileText("A(id),V(x)\n1,1.5\n2,+3\n3,-4\n");
        Assert.True(NullOnly(signed));
        Assert.Equal(0, NonNumeric(Field(signed, "V(x)")));
        var comma = ProfileText("A(id);V(x)\n1;1,5\n2;2,5\n3;3,5\n4;4,5\n");
        Assert.Equal(";", ((DelimitedDialect)comma.Format.Dialect!).Delimiter);
        Assert.Equal(0, NonNumeric(Field(comma, "V(x)")));
        Assert.Equal(4, Field(comma, "V(x)").Flags!.CommaDecimalLike);

        // An all-NA column is empty to squalify; the consumer sees every value as a misfit.
        var allNa = ProfileText("A(id),V(x),W\n1,NA,x\n2,NA,y\n3,NA,z\n");
        Assert.Equal("empty", Field(allNa, "V(x)").Type);
        Assert.Equal(3, Field(allNa, "V(x)").Nulls);
        Assert.False(NullOnly(allNa));
    }

    [Fact]
    public void ConstantWithNullAsValue_FalseImpliesConsumerFalse_TrueIsExactOnlyInANullOnlyFileWithSameTexts()
    {
        // The consumer's rule, from the raw text: the same text in every data row, NULL included.
        // A data row is any record that is not all NULL.
        static bool ConsumerConstant(string[][] rows, int index)
        {
            var data = rows.Where(static r => !r.All(static c => c == "NULL")).ToArray();
            return data.Length > 0 && data.Select(r => r[index]).Distinct(StringComparer.Ordinal).Count() == 1;
        }

        foreach (var fixture in new[] { "lab-sweep.csv", "one-condition-capture.csv" })
        {
            var d = ProfileFixture(fixture);
            var rows = File.ReadAllLines(TestBytes.Fixture("consumer/" + fixture)).Skip(1).Select(static l => l.Split(',')).ToArray();
            Assert.True(NullOnly(d));
            foreach (var f in d.Table!.Fields)
            {
                // NULL-only, identical texts: the formula and the consumer's rule agree exactly.
                Assert.Equal(ConsumerConstant(rows, (int)f.Index), DerivedConstant(d, f));
            }
        }

        // Counterexample, not NULL-only: the row of NA is a data row to the consumer, with the text NA, so its rule is
        // false; for squalify that row is all null, so Mode is constant with no null outside all-null rows.
        var na = ProfileText("RowID(id),Mode\n1,x\n2,x\nNA,NA\n3,x\n4,x\n5,x\n");
        Assert.Equal(1, Classes(na).AllNullRows);
        Assert.False(NullOnly(na));
        Assert.True(DerivedConstant(na, Field(na, "Mode")));

        // Counterexample, NULL-only but different texts: 1, 1.0 and 01 are one parsed value.
        var spelled = ProfileText("RowID(id),V(x)\n1,1\n2,1.0\n3,01\n");
        Assert.True(NullOnly(spelled));
        Assert.True(DerivedConstant(spelled, Field(spelled, "V(x)")));

        // Formula false: the consumer's rule is false (null in some data rows only).
        var sparse = ProfileText("RowID(id),Setting\n1,5\n2,NULL\n3,5\n");
        Assert.False(DerivedConstant(sparse, Field(sparse, "Setting")));
    }

    [Fact]
    public void ZeroDataRows_ConstantCanBeTrueWhileTheConsumerSetsItFalse()
    {
        var d = ProfileText("RowID(id),Mode,Freq(MHz)\nNULL,NULL,NULL\nNULL,NULL,NULL\nNULL,NULL,NULL\nNULL,NULL,NULL\n");
        Assert.Equal("delimited", d.Format.Family);
        Assert.Equal(0, DataRows(d));
        Assert.Equal(4, Classes(d).AllNullRows);
        Assert.All(d.Table!.Fields, static f => Assert.True(f.Constant));
    }

    [Fact]
    public void NaN_IsCountedInAnyAsciiCase_AllNullRecordsIncluded()
    {
        var d = ProfileText("A(id),V(x)\n1,NaN\n2,nan\n3,NAN\n4,1.5\nNaN,NaN\n5,2.5\n");
        var v = Field(d, "V(x)");
        Assert.Equal(new[] { ("NaN", 4L) }, Pairs(v.NullTokens!));
        Assert.Equal(1, Classes(d).AllNullRows);
        Assert.Equal("float", v.Type);
    }

    [Fact]
    public void NullsZero_IsASufficientNeverNullTest_ButNullsExcludingAllNullRowsIsNot()
    {
        // nulls == 0: no null of any spelling anywhere.
        var clean = ProfileText("RowID(id),Mode\n1,x\n2,y\n3,z\n");
        Assert.All(clean.Table!.Fields, static f => Assert.Equal(0, f.Nulls));

        // A record of NA is all null to squalify: nullsExcludingAllNullRows is 0 for Mode although the consumer, which
        // reads that record as a data row, has an NA token there.
        var na = ProfileText("RowID(id),Mode\n1,x\n2,y\nNA,NA\n3,z\n4,w\n5,v\n");
        var mode = Field(na, "Mode");
        Assert.Equal(0, mode.NullsExcludingAllNullRows);
        Assert.Equal(1, mode.Nulls);
        Assert.Equal(new[] { ("NA", 1L) }, Pairs(mode.NullTokens!));
    }

    [Fact]
    public void RaggedRecordAtTheStartOfTheRun_KeepsTheHeader()
    {
        // sq-27: a ragged record inside the run-start window no longer moves the start past the header.
        var builder = new StringBuilder("A(id),B,C\n");
        for (var i = 1; i <= 20; i++)
        {
            builder.Append(i == 3 ? "3,x\n" : i.ToString(CultureInfo.InvariantCulture) + ",x,y\n");
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal("delimited", d.Format.Family);
        Assert.True(((DelimitedDialect)d.Format.Dialect!).HasHeader);
        Assert.Equal(new[] { "A(id)", "B", "C" }, d.Table!.Fields.Select(static f => f.Name).ToArray());
        Assert.Equal(1, Assert.Single(d.Issues, static i => i.Code == "raggedRows").Count);

        // The same table with the ragged record far from the top keeps its header.
        var later = new StringBuilder("A(id),B,C\n");
        for (var i = 1; i <= 20; i++)
        {
            later.Append(i == 16 ? "16,x\n" : i.ToString(CultureInfo.InvariantCulture) + ",x,y\n");
        }

        Assert.True(((DelimitedDialect)ProfileText(later.ToString()).Format.Dialect!).HasHeader);
    }

    [Fact]
    public void RaggedRecordAmongTheFirstTwoDataRecords_KeepsTheHeader()
    {
        // sq-34 replaced the lead rule: the window skips a ragged record whatever the start's own k-run.
        var builder = new StringBuilder("A(id),B,C\n");
        for (var i = 1; i <= 20; i++)
        {
            builder.Append(i == 2 ? "2,x\n" : i.ToString(CultureInfo.InvariantCulture) + ",x,y\n");
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal("delimited", d.Format.Family);
        Assert.True(((DelimitedDialect)d.Format.Dialect!).HasHeader);
        Assert.Equal(new long[] { 3 }, Assert.Single(d.Issues, static i => i.Code == "raggedRows").FirstLines);
    }

    [Fact]
    public void Document_NamesNoInternalDecisionIds_AndSaysTableChangesDoNotBumpSchemaVersion()
    {
        var section = DocumentedSection();
        Assert.DoesNotMatch(@"\bD\d+\b", section);
        Assert.Contains("not `schemaVersion`", section, StringComparison.Ordinal);
        Assert.DoesNotContain("sq-27", section, StringComparison.Ordinal);
        Assert.Contains("pending sq-12", section, StringComparison.Ordinal);
    }

    [Fact]
    public void IdColumn_IntIsWiderThanDigitsOnly()
    {
        // Squalify's int accepts a sign, padding and leading zeros; a consumer that wants digits only keeps its own check.
        var d = ProfileText("A(id),V\n1,+1\n2,-1\n3, 007 \n4,12\n");
        var v = Field(d, "V");
        Assert.Equal("int", v.Type);
        Assert.Equal(1, v.Flags!.LeadingZero);
        Assert.Equal(1, v.Flags.Padded);
    }

    [Fact]
    public void HeaderNames_AreTrimmed_AndFollowTheDataBallGrammar()
    {
        var d = ProfileText("RowID( id ),Blank( ),Nest(b(c)),Padded(id) ,X(1),Pout(dBm)[1],Trace[0],Pout[dBm],VCC_V,Mode\n1,2,3,4,5,6,7,8,9,x\n2,3,4,5,6,7,8,9,10,y\n");
        var f = d.Table!.Fields;
        Assert.Equal("RowID( id )", f[0].Name);

        // RowID( id ): the unit is trimmed, so it is "id": an Id column to a consumer.
        Assert.Equal(("RowID", "id", "nameParenUnit"), (f[0].NameParts!.Base, f[0].NameParts!.Unit, f[0].NameParts!.Convention));
        Assert.Equal("Id", Mapped(f[0]));

        // Blank( ): empty unit text is not a unit, so the name is none with the whole name as base.
        Assert.Equal(("Blank( )", (string?)null, "none"), (f[1].NameParts!.Base, f[1].NameParts!.Unit, f[1].NameParts!.Convention));
        Assert.Equal("None", Mapped(f[1]));

        // Nest(b(c)): the last "(" starts the unit, so the unit is "c)".
        Assert.Equal(("Nest(b", "c)", "nameParenUnit"), (f[2].NameParts!.Base, f[2].NameParts!.Unit, f[2].NameParts!.Convention));

        // A trailing space on the header cell is trimmed.
        Assert.Equal("Padded(id)", f[3].Name);
        Assert.Equal("Id", Mapped(f[3]));

        // Digit-only paren text is still the paren convention (unit "1", unknown).
        Assert.Equal(("X", "1", "nameParenUnit", true), (f[4].NameParts!.Base, f[4].NameParts!.Unit, f[4].NameParts!.Convention, f[4].NameParts!.UnknownUnit));
        Assert.Equal("Unit", Mapped(f[4]));

        // An indexed trace column carries no unit: Pout(dBm)[1] and Trace[0] are none, with the whole name as base.
        Assert.Equal(("Pout(dBm)[1]", (string?)null, "none"), (f[5].NameParts!.Base, f[5].NameParts!.Unit, f[5].NameParts!.Convention));
        Assert.Equal(("Trace[0]", (string?)null, "none"), (f[6].NameParts!.Base, f[6].NameParts!.Unit, f[6].NameParts!.Convention));

        // Bracket and underscore units carry a unit in squalify but are None to a paren-only consumer.
        Assert.Equal(("Pout", "dBm", "nameBracketUnit"), (f[7].NameParts!.Base, f[7].NameParts!.Unit, f[7].NameParts!.Convention));
        Assert.Equal("None", Mapped(f[7]));
        Assert.Equal(("VCC", "V", "nameUnderscoreUnit"), (f[8].NameParts!.Base, f[8].NameParts!.Unit, f[8].NameParts!.Convention));
        Assert.Equal("None", Mapped(f[8]));
        Assert.Equal("None", Mapped(f[9]));
    }

    [Fact]
    public void IdUnit_IsMatchedOrdinally_SoNearMissesAreUnitColumns()
    {
        var d = ProfileText("RowID(ID),Idx(Id),Key(id),Mode\n1,2,3,x\n2,3,4,y\n");
        Assert.Equal(new[] { "Unit", "Unit", "Id", "None" }, d.Table!.Fields.Select(Mapped).ToArray());
        Assert.Equal("ID", d.Table.Fields[0].NameParts!.Unit);
        Assert.Equal("RowID(ID)", d.Table.Fields[0].Name);
    }

    [Fact]
    public void Types_AreFromValues_NotFromTheNameConvention()
    {
        // The descriptor votes from values; a consumer that types by name (id: integer, unit: number, otherwise
        // bool-or-string) reads `type` as a check. A unitless column of 1 and 2 is int, not string.
        var d = ProfileText("RowID(id),Freq(MHz),Mode,Flag,Level(V)\n1,2400,1,true,1.5\n2,2410.5,2,false,2\n3,2420,1,TRUE,NaN\n");
        Assert.Equal("int", Field(d, "RowID(id)").Type);
        Assert.Equal("float", Field(d, "Freq(MHz)").Type);
        Assert.Equal("int", Field(d, "Mode").Type);
        Assert.Equal("bool", Field(d, "Flag").Type);
        Assert.Equal("float", Field(d, "Level(V)").Type);
        Assert.Equal(1, Field(d, "Level(V)").Nulls);
    }

    [Fact]
    public void HeaderOnlyFile_IsADelimitedTableWithZeroRows()
    {
        var d = ProfileText("A(id),B,C\n");
        Assert.Equal("delimited", d.Format.Family);
        Assert.True(((DelimitedDialect)d.Format.Dialect!).HasHeader);
        Assert.Equal(0, d.Table!.Rows);
        Assert.Equal(1, Assert.Single(d.Issues, static i => i.Code == "noDataRows").Count);
    }

    [Fact]
    public void Bool_IsWiderThanTrueAndFalse()
    {
        var d = ProfileText("A(id),V\n1,yes\n2,No\n3,TRUE\n4,false\n");
        Assert.Equal("bool", Field(d, "V").Type);
    }

    [Fact]
    public void HeaderlessTable_HasNoNamesOrConventions_ButTheSameFieldFacts()
    {
        var d = ProfileText("1,x,3.5\n2,y,3.5\n3,z,3.5\n");
        Assert.False(((DelimitedDialect)d.Format.Dialect!).HasHeader);
        Assert.All(d.Table!.Fields, static f => Assert.Null(f.Name));
        Assert.All(d.Table.Fields, static f => Assert.Null(f.NameParts));
        Assert.Equal(3, d.Table.Rows);
        Assert.True(d.Table.Fields[2].Constant);
    }
}
