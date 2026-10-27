// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class DelimitedTests
{
    private static readonly string[] Fixtures =
    [
        "delimited/semiconductor-sweep.csv",
        "lab/sweep-preamble.csv",
        "delimited/semicolon-preamble.csv",
        "delimited/no-header.tsv",
        "delimited/padded-annotated.csv",
        "delimited/padded-annotated-no-units.csv",
        "delimited/semicolon-decimal-comma.csv",
        "delimited/leading-separator-rows.csv",
    ];

    private static ProfileOptions Options(bool includeValues = false) => ProfileOptions.Create(includeValues, Array.Empty<string>());

    private static Descriptor ProfileFixture(string fixture, bool includeValues = false)
    {
        return Profiler.Profile(TestBytes.Fixture(fixture), Options(includeValues));
    }

    private static Descriptor ProfileText(string text, bool includeValues = false, string name = "t.csv")
    {
        return Profiler.Profile(new BytesSource(name, Encoding.UTF8.GetBytes(text)), Options(includeValues));
    }

    private static DelimitedDialect Dialect(Descriptor d)
    {
        Assert.Equal("delimited", d.Format.Family);
        return Assert.IsType<DelimitedDialect>(d.Format.Dialect);
    }

    private static Region RegionOf(Descriptor d, string kind)
    {
        return Assert.Single(d.Layout.Regions, r => r.Kind == kind);
    }

    private static string[] Names(Descriptor d)
    {
        return d.Table!.Fields.Select(static f => f.Name!).ToArray();
    }

    private static Evidence EvidenceOf(Descriptor d, string stage, string topic)
    {
        return Assert.Single(d.Evidence, e => e.Stage == stage && e.Topic == topic);
    }

    private static string Table(string delimiter, string[] preamble, int rows)
    {
        var builder = new StringBuilder();
        foreach (var line in preamble)
        {
            builder.Append(line).Append('\n');
        }

        builder.Append(string.Join(delimiter, "Freq(MHz)", "Pout(dBm)", "Gain(dB)")).Append('\n');
        for (var i = 0; i < rows; i++)
        {
            builder.Append(string.Join(delimiter, (700 + (100 * i)).ToString(CultureInfo.InvariantCulture), (10 + (i * 0.5)).ToString("0.0", CultureInfo.InvariantCulture), (15 + (i * 0.25)).ToString("0.00", CultureInfo.InvariantCulture))).Append('\n');
        }

        return builder.ToString();
    }

    private static readonly string[] FivePreambleLines = ["Lot=A12", "Wafer=7", "Temp=25", "# note", "Operator=jr"];

    [Theory]
    [InlineData(",", ",")]
    [InlineData("\t", "\t")]
    [InlineData(";", ";")]
    [InlineData("|", "|")]
    [InlineData(" ", "whitespace")]
    public void Dialect_PicksDelimiter(string used, string expected)
    {
        var d = ProfileText(Table(used, FivePreambleLines, 12));
        var dialect = Dialect(d);
        Assert.Equal(expected, dialect.Delimiter);
        Assert.True(dialect.HasHeader);
        Assert.Equal(new[] { "Freq(MHz)", "Pout(dBm)", "Gain(dB)" }, Names(d));
        var preamble = RegionOf(d, "preamble");
        Assert.Equal((1L, 5L), (preamble.FirstLine, preamble.LastLine));
        Assert.Equal(12, d.Table!.Rows);
        var evidence = EvidenceOf(d, "dialect", "delimiter");
        Assert.Equal(expected, evidence.Chosen);
        Assert.Equal(new[] { ",", "\t", ";", "|", "whitespace" }, evidence.Candidates.Select(static c => c.Value).ToArray());
    }

    [Fact]
    public void Dialect_QuotedDelimiterAndNewline_DoNotSplit()
    {
        var text = "id,note,val\n1,\"a,b\",1.5\n2,\"line1\nline2\",2.5\n3,x,3.5\n4,y,4.5\n5,z,5.5\n6,w,6.5\n7,v,7.5\n8,u,8.5\n";
        var d = ProfileText(text);
        var dialect = Dialect(d);
        Assert.Equal(",", dialect.Delimiter);
        Assert.Equal("\"", dialect.Quote);
        Assert.Equal(new[] { "id", "note", "val" }, Names(d));
        Assert.Equal(new MinMax(3, 3), d.Table!.FieldCount);
        Assert.Equal(8, d.Table.Rows);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "raggedRows");
        var data = RegionOf(d, "data");
        Assert.Equal((2L, 10L), (data.FirstLine, data.LastLine));
        Assert.Equal(8, d.Table.Fields[2].TypeVotes.Single(static e => e.Key == "float").Value);
    }

    [Fact]
    public void Dialect_RunawayQuote_DisqualifiesCandidate_WithEvidence()
    {
        var builder = new StringBuilder("Id;Note\n");
        for (var i = 1; i <= 3000; i++)
        {
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(';').Append(i == 2 ? "p,\"q" : "abcdefghijklmnopqrstuvwxyz").Append('\n');
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(";", Dialect(d).Delimiter);
        var evidence = EvidenceOf(d, "dialect", "delimiter");
        var comma = Assert.Single(evidence.Candidates, static c => c.Value == ",");
        Assert.Equal(0.0, comma.Score);
        Assert.Equal("disqualified: runaway quote opened at line 3", comma.Reason);
        Assert.Equal(";", evidence.Chosen);
    }

    [Fact]
    public void Regions_PreambleKeyValue_HeaderUnitsDataFooter()
    {
        var d = ProfileFixture("lab/sweep-preamble.csv");
        var dialect = Dialect(d);
        Assert.Equal(",", dialect.Delimiter);
        Assert.True(dialect.HasHeader);
        Assert.True(dialect.UnitsRow);
        Assert.Equal(
            new[]
            {
                ("preamble", 1L, 7L, 0L, 98L),
                ("header", 8L, 8L, 98L, 127L),
                ("units", 9L, 9L, 127L, 138L),
                ("data", 10L, 29L, 138L, 435L),
                ("footer", 30L, 31L, 435L, 448L),
            },
            d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine, r.StartByte, r.EndByte)).ToArray());
        var preamble = RegionOf(d, "preamble");
        Assert.Equal(new[] { "Lot", "Wafer", "Temp", "Operator" }, preamble.Keys);
        Assert.Equal(
            new[] { ("comment", 1L), ("keyValue", 4L), ("text", 2L) },
            preamble.LineKinds!.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Null(preamble.Values);
        Assert.Equal(
            new[] { ("keyValue", 1L), ("text", 1L) },
            RegionOf(d, "footer").LineKinds!.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Equal(new[] { "Freq(MHz)", "Pout(dBm)", "Gain(dB)" }, Names(d));
        Assert.Equal(20, d.Table!.Rows);
        Assert.Equal(new MinMax(3, 3), d.Table.FieldCount);
        Assert.Equal(28L, d.Layout.MaxLineLength);
        Assert.Equal(new LineEndings(0, 31, 0), d.Encoding.LineEndings);
    }

    [Fact]
    public void Regions_PreambleValues_OnlyWithIncludeValues()
    {
        var without = ProfileFixture("lab/sweep-preamble.csv");
        Assert.Null(RegionOf(without, "preamble").Values);
        var json = Encoding.UTF8.GetString(DescriptorWriter.ToBytes(without));
        Assert.DoesNotContain("A12", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PA-7", json, StringComparison.Ordinal);
        Assert.DoesNotContain("jr", json, StringComparison.Ordinal);

        var with = ProfileFixture("lab/sweep-preamble.csv", includeValues: true);
        Assert.Equal(new[] { "A12", "7", "25", "jr" }, RegionOf(with, "preamble").Values);
    }

    [Fact]
    public void UnitsRow_DetectedAlways_TextOnlyWithIncludeValues()
    {
        var without = ProfileFixture("lab/sweep-preamble.csv");
        Assert.True(Dialect(without).UnitsRow);
        Assert.All(without.Table!.Fields, static f => Assert.Null(f.Unit));
        // nameParts.unit carries the unit parsed from a header name such as Pout(dBm); only fields[].unit is the units row.
        using var written = System.Text.Json.JsonDocument.Parse(DescriptorWriter.ToBytes(without));
        foreach (var field in written.RootElement.GetProperty("table").GetProperty("fields").EnumerateArray())
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, field.GetProperty("unit").ValueKind);
        }

        var with = ProfileFixture("lab/sweep-preamble.csv", includeValues: true);
        Assert.Equal(new[] { "MHz", "dBm", "dB" }, with.Table!.Fields.Select(static f => f.Unit).ToArray());
    }

    [Fact]
    public void IncludeValuesFor_OpensOnlyTheNamedKeysLabelsAndFields()
    {
        var options = ProfileOptions.Create(false, ["LOT", "freq(mhz)", "unit"]);
        var d = Profiler.Profile(TestBytes.Fixture("lab/sweep-preamble.csv"), options);
        Assert.Equal(new string?[] { "A12", null, null, null }, RegionOf(d, "preamble").Values);
        Assert.Equal(new string?[] { "MHz", null, null }, d.Table!.Fields.Select(static f => f.Unit).ToArray());

        var annotated = Profiler.Profile(TestBytes.Fixture("delimited/padded-annotated.csv"), options);
        var rows = RegionOf(annotated, "annotation").Rows!;
        Assert.Equal(new[] { false, false, false, true }, rows.Select(static r => r.Values is not null).ToArray());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("a")]
    [InlineData("a,")]
    [InlineData(",")]
    [InlineData("\"")]
    [InlineData("\"a,b\n1,2")]
    [InlineData("a,b\r1,2\r3,4\r")]
    [InlineData("a,b\n1,2\n3,4\n\n\n\n")]
    [InlineData("a;b\n;\n;\n;\n;\n;\n;\n;\n")]
    [InlineData("   \n   \n   \n")]
    [InlineData("a\tb\n\t\n\t\n\t\n\t\n\t\n\t\n")]
    [InlineData("x y z\n 1 2 3 \n 4 5 6 \n 7 8 9 \n 1 2 3 \n 4 5 6 \n 7 8 9 \n")]
    public void Profile_TinyAndOddInputs_DoNotThrow_AndStayDeterministic(string text)
    {
        var first = TestBytes.ProfileText(new BytesSource("odd.csv", Encoding.UTF8.GetBytes(text)));
        var second = TestBytes.ProfileText(new BytesSource("odd.csv", Encoding.UTF8.GetBytes(text)));
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Comment_BeforeTheHeader_NeverBecomesALabelOrACellValue(bool includeValues)
    {
        var builder = new StringBuilder("# operator Alice, lot 99, x\nFreq,Pout,Gain\n");
        for (var i = 0; i < 8; i++)
        {
            builder.Append("1,2.5,3.5\n");
        }

        var d = ProfileText(builder.ToString(), includeValues);
        Assert.Equal(",", Dialect(d).Delimiter);
        Assert.Equal(new[] { "Freq", "Pout", "Gain" }, Names(d));
        Assert.DoesNotContain(d.Layout.Regions, static r => r.Kind == "annotation");
        var comment = Assert.Single(d.Issues, static i => i.Code == "commentInData");
        Assert.Equal(1, comment.Count);
        Assert.Equal(new long[] { 1 }, comment.FirstLines);
        var json = Encoding.UTF8.GetString(DescriptorWriter.ToBytes(d));
        Assert.DoesNotContain("Alice", json, StringComparison.Ordinal);
        Assert.DoesNotContain("operator", json, StringComparison.Ordinal);
        Assert.Equal(8, d.Table!.Rows);
    }

    [Fact]
    public void Comment_BetweenAnnotationRows_IsNotAnAnnotationRow()
    {
        var d = ProfileFixture("delimited/padded-annotated.csv");
        var lines = File.ReadAllLines(TestBytes.Fixture("delimited/padded-annotated.csv")).ToList();
        lines.Insert(6, "# note, from, the, operator, here");
        var withComment = Profiler.Profile(new BytesSource("c.csv", Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n")), Options(true));
        var rows = RegionOf(withComment, "annotation").Rows!;
        Assert.Equal(RegionOf(d, "annotation").Rows!.Select(static r => r.Label).ToArray(), rows.Select(static r => r.Label).ToArray());
        Assert.Equal(1, Assert.Single(withComment.Issues, static i => i.Code == "commentInData").Count);
        Assert.DoesNotContain("note", Encoding.UTF8.GetString(DescriptorWriter.ToBytes(withComment)), StringComparison.Ordinal);
    }

    private static List<string[]> Tokenize(string text, DelimiterKind kind)
    {
        var source = new BytesSource("t.csv", Encoding.UTF8.GetBytes(text));
        var sniff = ByteSniffer.Sniff(source, new CharsetTolerance(8, 65536));
        var records = new List<string[]>();
        var sink = new CollectingSink(records);
        var tokenizer = new RecordTokenizer(kind, sink, 65_536, collectCells: true);
        TextScanner.Scan(source, sniff, [tokenizer], new LineCounter(), null);
        return records;
    }

    private sealed class CollectingSink(List<string[]> records) : IRecordSink
    {
        public void Record(RecordInfo record) => records.Add(record.Cells.ToArray());

        public void Blank(long line)
        {
        }
    }

    [Theory]
    [InlineData("\"a,b\"  ,0\r\n", "a,b", "0")]
    [InlineData("\"a,b\"\t ,0\n", "a,b", "0")]
    [InlineData("\"a,b\",0\n", "a,b", "0")]
    [InlineData("\"a\"\"b\"  ,0\n", "a\"b", "0")]
    public void Tokenizer_ClosingQuoteThenSpacesThenDelimiter_ClosesTheField(string line, string first, string second)
    {
        var records = Tokenize(line, DelimiterKind.Comma);
        Assert.Equal(new[] { first, second }, Assert.Single(records));
    }

    [Fact]
    public void Tokenizer_ClosingQuoteThenSpacesThenEndOfLine_ClosesTheField()
    {
        var records = Tokenize("1,\"a\"  \r\n2,\"b\"\t\n", DelimiterKind.Comma);
        Assert.Equal(new[] { new[] { "1", "a" }, new[] { "2", "b" } }, records);
    }

    [Fact]
    public void Tokenizer_QuoteFollowedBySpacesThenOtherText_StaysLiteral()
    {
        // The quote after "a" is followed by a space and then x, not by the delimiter: it is literal text.
        var records = Tokenize("\"a\" x\",0\n", DelimiterKind.Comma);
        Assert.Equal(new[] { "a\" x", "0" }, Assert.Single(records));
        var lone = Tokenize("\"a\"x\",0\n", DelimiterKind.Comma);
        Assert.Equal(new[] { "a\"x", "0" }, Assert.Single(lone));
    }

    [Fact]
    public void Dialect_PaddedQuotedCells_AreNotSplitAndNotLiteral()
    {
        var builder = new StringBuilder("name   ,v\r\n");
        for (var i = 0; i < 8; i++)
        {
            builder.Append("\"a,b\"  ,").Append(i).Append("\r\n");
        }

        var d = ProfileText(builder.ToString());
        var dialect = Dialect(d);
        Assert.Equal(",", dialect.Delimiter);
        Assert.Equal("\"", dialect.Quote);
        Assert.True(dialect.HasHeader);
        Assert.Equal(new[] { "name", "v" }, Names(d));
        Assert.Equal(new MinMax(2, 2), d.Table!.FieldCount);
        Assert.Equal(8, d.Table.Rows);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "raggedRows");
        var comma = Assert.Single(EvidenceOf(d, "dialect", "delimiter").Candidates, static c => c.Value == ",");
        Assert.DoesNotContain("literal", comma.Reason, StringComparison.Ordinal);
        Assert.Equal(new[] { ("string", 8L) }, d.Table.Fields[0].TypeVotes.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Equal(new[] { ("int", 8L) }, d.Table.Fields[1].TypeVotes.Select(static e => (e.Key, e.Value)).ToArray());
    }

    [Fact]
    public void Fields_MembersOfLaterSlices_AreNull_AndTheMembersOfSq05AndSq14AreWritten()
    {
        var d = ProfileFixture("delimited/padded-annotated.csv", includeValues: true);
        using var json = System.Text.Json.JsonDocument.Parse(DescriptorWriter.ToBytes(d));
        foreach (var field in json.RootElement.GetProperty("table").GetProperty("fields").EnumerateArray())
        {
            // top is values tier and the flag is on, so it is there; text is only for string fields.
            Assert.Equal(System.Text.Json.JsonValueKind.Object, field.GetProperty("top").ValueKind);
            Assert.Equal(
                field.GetProperty("type").GetString() == "string" ? System.Text.Json.JsonValueKind.Object : System.Text.Json.JsonValueKind.Null,
                field.GetProperty("text").ValueKind);

            foreach (var member in new[] { "index", "name", "nameParts", "type", "dominant", "typeVotes", "count", "nulls", "nullsExcludingAllNullRows", "nullTokens", "flags", "distinct", "shapes", "constant", "unique" })
            {
                Assert.True(field.TryGetProperty(member, out var value), member);
                Assert.NotEqual(System.Text.Json.JsonValueKind.Null, value.ValueKind);
            }

            Assert.True(field.TryGetProperty("unit", out _));
        }

        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.RootElement.GetProperty("table").GetProperty("fields")[0].GetProperty("unit").ValueKind);
    }

    [Fact]
    public void Preamble_ValuesAreHeldOnlyWhenTheyMayBeWritten()
    {
        static int Held(ProfileOptions options)
        {
            var dialect = new CandidateResult { Kind = DelimiterKind.Comma, K = 3, RunFirstLine = 10, RunLastLine = 20, Reason = "x" };
            var pass = new DelimitedPass(dialect, new ProfileContext(options, Limits.Default));
            var line = 1L;
            foreach (var text in new[] { "Lot=A12", "Wafer=7", "Temp=25" })
            {
                var record = new RecordInfo { FieldCount = 1, StartLine = line, EndLine = line, Raw = text };
                record.Cells.Add(text);
                pass.Record(record);
                line++;
            }

            return pass.PreambleValuesHeld;
        }

        Assert.Equal(0, Held(Options()));
        Assert.Equal(1, Held(ProfileOptions.Create(false, ["LOT"])));
        Assert.Equal(3, Held(Options(true)));
    }

    [Fact]
    public void Header_Evidence_ListsEligibleCandidatesTheWinnerAndOneSummary()
    {
        var d = ProfileFixture("lab/sweep-preamble.csv");
        var evidence = EvidenceOf(d, "header", "candidates");
        Assert.Equal(new[] { "8", "9", "summary" }, evidence.Candidates.Select(static c => c.Value).ToArray());
        Assert.Equal(
            "sampled=22; eligible=2; ineligible=20 (comment line=0, field count=0, blank or repeated names=0, fits every typed column=20)",
            evidence.Candidates[^1].Reason);
        Assert.Equal(0.0, evidence.Candidates[^1].Score);

        // A winner that is not eligible (relaxed) is listed too, and the summary counts the ineligible by reason.
        var relaxed = ProfileText("Pout,pout,,Gain\n1.5,2.5,3.5,4.5\n2.5,3.5,4.5,5.5\n3.5,4.5,5.5,6.5\n4.5,5.5,6.5,7.5\n5.5,6.5,7.5,8.5\n6.5,7.5,8.5,9.5\n");
        var relaxedEvidence = EvidenceOf(relaxed, "header", "candidates");
        Assert.Equal(new[] { "1", "summary" }, relaxedEvidence.Candidates.Select(static c => c.Value).ToArray());
        Assert.Equal(
            "sampled=7; eligible=0; ineligible=7 (comment line=0, field count=0, blank or repeated names=1, fits every typed column=6)",
            relaxedEvidence.Candidates[^1].Reason);

        // No header: only the summary.
        var none = EvidenceOf(ProfileFixture("delimited/no-header.tsv"), "header", "candidates");
        Assert.Equal(new[] { "summary" }, none.Candidates.Select(static c => c.Value).ToArray());
        Assert.Null(none.Chosen);
        Assert.Contains("fits every typed column=6)", none.Candidates[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_Evidence_SummaryCountsCommentAndRaggedRecords()
    {
        var text = "a,b\n1,2\n2,3\n3,4\n4,5\n5,6\n6,7\n7,8\n# a comment\n8,9\n9,10\n10,11\n";
        var summary = EvidenceOf(ProfileText(text), "header", "candidates").Candidates[^1];
        Assert.Equal(HeaderDetector.SummaryValue, summary.Value);
        Assert.Contains("sampled=12;", summary.Reason, StringComparison.Ordinal);
        Assert.Contains("comment line=1,", summary.Reason, StringComparison.Ordinal);
        var ragged = EvidenceOf(ProfileText("a,b\n1,2\n2,3\n3,4\n4,5\n5,6\n6,7\n7,8\n1,2,3\n8,9\n9,10\n"), "header", "candidates").Candidates[^1];
        Assert.Contains("field count=1,", ragged.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_AllNumericFirstRow_IsData()
    {
        var d = ProfileFixture("delimited/no-header.tsv");
        var dialect = Dialect(d);
        Assert.Equal("\t", dialect.Delimiter);
        Assert.False(dialect.HasHeader);
        Assert.False(dialect.UnitsRow);
        Assert.Equal(6, d.Table!.Rows);
        Assert.All(d.Table.Fields, static f => Assert.Null(f.Name));
        Assert.DoesNotContain(d.Layout.Regions, static r => r.Kind is "header" or "annotation" or "units");
        Assert.Equal(new[] { "data" }, d.Layout.Regions.Select(static r => r.Kind).ToArray());
        Assert.Null(EvidenceOf(d, "header", "candidates").Chosen);
        Assert.Equal(new[] { ("int", 6L) }, d.Table.Fields[0].TypeVotes.Select(static e => (e.Key, e.Value)).ToArray());
    }

    [Fact]
    public void Header_AllStringTable_AssumedWithEvidence()
    {
        var text = "name,city,color\nann,paris,red\nbob,rome,blue\ncid,oslo,green\ndee,bern,pink\neve,kiev,gray\nfay,lima,gold\n";
        var d = ProfileText(text);
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(new[] { "name", "city", "color" }, Names(d));
        var evidence = EvidenceOf(d, "header", "candidates");
        Assert.Equal("1", evidence.Chosen);
        Assert.Contains("assumed: all-string table", Assert.Single(evidence.Candidates, static c => c.Value == "1").Reason, StringComparison.Ordinal);
        Assert.Equal(6, d.Table!.Rows);
    }

    [Fact]
    public void RaggedRows_ReportedNotRejected()
    {
        var text = "a,b,c\n1,2,3\n2,3,4\n3,4,5\n4,5,6\n5,6,7\n6,7,8\n7,8,9\n4,5\n10,11,12\n11,12,13\n12,13,14\n9,10,11,12\n13,14,15\n16,17,18\n19,20,21\n";
        var d = ProfileText(text);
        Assert.Equal(",", Dialect(d).Delimiter);
        var ragged = Assert.Single(d.Issues, static i => i.Code == "raggedRows");
        Assert.Equal(2, ragged.Count);
        Assert.Equal(new long[] { 9, 13 }, ragged.FirstLines);
        Assert.Equal(new MinMax(2, 4), d.Table!.FieldCount);
        Assert.Equal(15, d.Table.Rows);
        Assert.Equal(3, d.Table.Fields.Count);
    }

    [Fact]
    public void Irregular_FallsBackToText_WithIssue()
    {
        // Alternating 2-field and 3-field lines: 120,000 runs, over the limit of 100,000.
        var pattern = TestBytes.Ascii("a,b\na,b,c\n");
        var source = new GeneratedSource("irregular.csv", pattern.Length * 60_000L, position => pattern[(int)(position % pattern.Length)]);
        var d = Profiler.Profile(source, Options());
        Assert.Equal("text", d.Format.Family);
        Assert.Null(d.Format.Dialect);
        Assert.Null(d.Table);
        var issue = Assert.Single(d.Issues, static i => i.Code == "tooIrregular");
        Assert.True(issue.Count >= 1);
        var family = EvidenceOf(d, "format", "family");
        var delimited = Assert.Single(family.Candidates, static c => c.Value == "delimited");
        Assert.Equal(0.0, delimited.Score);
        Assert.Contains("too irregular", delimited.Reason, StringComparison.Ordinal);
        Assert.Contains("too irregular", Assert.Single(EvidenceOf(d, "dialect", "delimiter").Candidates, static c => c.Value == ",").Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_AnnotationRowsAboveHeader_ScoredAndRegioned()
    {
        var d = ProfileFixture("delimited/padded-annotated.csv");
        var dialect = Dialect(d);
        Assert.Equal(",", dialect.Delimiter);
        Assert.True(dialect.HasHeader);
        Assert.False(dialect.UnitsRow);
        Assert.Equal(new[] { "Site", "Pout(dBm)", "Gain(dB)", "Icc(mA)", "Vcc(V)" }, Names(d));

        Assert.Equal(
            new[] { ("preamble", 1L, 5L), ("annotation", 6L, 9L), ("header", 10L, 10L), ("data", 11L, 20L) },
            d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine)).ToArray());
        var preamble = RegionOf(d, "preamble");
        Assert.Equal(new[] { "Lot", "Wafer", "Tester", "Program", "Date" }, preamble.Keys);
        Assert.Equal(5L, preamble.LineKinds!.Single(static e => e.Key == "keyValue").Value);

        var rows = RegionOf(d, "annotation").Rows!;
        Assert.Equal(new[] { "Prog_line", "Upper_limit", "Lower_limit", "Unit" }, rows.Select(static r => r.Label).ToArray());
        Assert.Equal(new long[] { 6, 7, 8, 9 }, rows.Select(static r => r.Line).ToArray());
        Assert.Equal(new long[] { 1, 0, 0, 0 }, rows.Select(static r => r.BlankFields).ToArray());
        Assert.Equal(new[] { "int", "float", "float", "string" }, rows.Select(static r => r.Type).ToArray());
        Assert.Equal(new[] { ("int", 3L) }, rows[0].TypeVotes.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Equal(new[] { ("float", 4L) }, rows[1].TypeVotes.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Equal(new[] { ("string", 3L) }, rows[3].TypeVotes.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Equal(new[] { ("NA.", 1L) }, rows[3].NullTokens.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Empty(rows[0].NullTokens);

        // The measurement fields are numbers (the one malformed data row is ragged and is not typed).
        var fields = d.Table!.Fields;
        Assert.Equal(new[] { "int", "float", "float", "float", "float" }, fields.Select(static f => Assert.Single(f.TypeVotes).Key).ToArray());
        Assert.Equal(new long[] { 9, 9, 7, 9, 9 }, fields.Select(static f => f.TypeVotes.Single().Value).ToArray());

        // The unit annotation row is a header candidate too, because the header looks like a units row to it.
        // It scores below the real header, which it would tie without the contrast term.
        var candidates = EvidenceOf(d, "header", "candidates");
        Assert.Equal("10", candidates.Chosen);
        var unit = Assert.Single(candidates.Candidates, static c => c.Value == "9");
        var header = Assert.Single(candidates.Candidates, static c => c.Value == "10");
        Assert.True(header.Score > unit.Score, $"{header.Score} vs {unit.Score}");
        Assert.True(unit.Score > 0);
        Assert.StartsWith("eligible", unit.Reason, StringComparison.Ordinal);
        Assert.Equal(1.0, header.Score);
    }

    [Fact]
    public void Header_AnnotationRowRemoved_StillPicksHeader()
    {
        var d = ProfileFixture("delimited/padded-annotated-no-units.csv");
        var dialect = Dialect(d);
        Assert.True(dialect.HasHeader);
        Assert.False(dialect.UnitsRow);
        Assert.Equal(new[] { "Site", "Pout(dBm)", "Gain(dB)", "Icc(mA)", "Vcc(V)" }, Names(d));
        Assert.Equal(
            new[] { ("preamble", 1L, 5L), ("annotation", 6L, 8L), ("header", 9L, 9L), ("data", 10L, 19L) },
            d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine)).ToArray());
        var rows = RegionOf(d, "annotation").Rows!;
        Assert.Equal(new[] { "Prog_line", "Upper_limit", "Lower_limit" }, rows.Select(static r => r.Label).ToArray());
        Assert.Equal("9", EvidenceOf(d, "header", "candidates").Chosen);
        Assert.Equal(new[] { "int", "float", "float", "float", "float" }, d.Table!.Fields.Select(static f => Assert.Single(f.TypeVotes).Key).ToArray());
    }

    [Fact]
    public void Regions_AnnotationValues_OnlyWithIncludeValues()
    {
        var without = ProfileFixture("delimited/padded-annotated.csv");
        Assert.All(RegionOf(without, "annotation").Rows!, static r => Assert.Null(r.Values));
        var json = Encoding.UTF8.GetString(DescriptorWriter.ToBytes(without));
        Assert.DoesNotContain("3.600000000000", json, StringComparison.Ordinal);
        // The unit "mA" is also parsed from the header name Icc(mA), which is always included, so check the cells by member.
        using var written = System.Text.Json.JsonDocument.Parse(DescriptorWriter.ToBytes(without));
        foreach (var region in written.RootElement.GetProperty("layout").GetProperty("regions").EnumerateArray())
        {
            if (region.TryGetProperty("rows", out var annotationRows))
            {
                foreach (var row in annotationRows.EnumerateArray())
                {
                    Assert.False(row.TryGetProperty("values", out _));
                }
            }
        }

        var with = ProfileFixture("delimited/padded-annotated.csv", includeValues: true);
        var rows = RegionOf(with, "annotation").Rows!;
        Assert.Equal(new[] { "Unit", "dBm", "dB", "mA", "NA." }, rows[3].Values);
        Assert.Equal(new[] { "Prog_line", "112", "", "118", "121" }, rows[0].Values);
    }

    [Fact]
    public void Dialect_PaddedFixedWidth_CommaBeatsWhitespace()
    {
        var d = ProfileFixture("delimited/padded-annotated.csv");
        Assert.Equal(",", Dialect(d).Delimiter);
        var candidates = EvidenceOf(d, "dialect", "delimiter").Candidates;
        var comma = Assert.Single(candidates, static c => c.Value == ",");
        var whitespace = Assert.Single(candidates, static c => c.Value == "whitespace");
        // Whitespace runs split 15 of 20 records into 5 fields, the comma only 14 of 20 (one row lost its
        // delimiter), so whitespace is ahead before the clean share. Comma wins by cleanliness, not by order.
        Assert.Equal("k=5; 14/20 records; clean=1; run lines 6-20; 0 quoted fields", comma.Reason);
        Assert.StartsWith("k=5; 15/20 records; clean=0.", whitespace.Reason, StringComparison.Ordinal);
        Assert.Equal(0.7, comma.Score);
        Assert.True(whitespace.Score < 0.2, whitespace.Score.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Dialect_PaddingFacts()
    {
        var padded = Dialect(ProfileFixture("delimited/padded-annotated.csv")).Padding;
        Assert.True(padded.Trim);
        Assert.Equal(71.0 / 73.0, padded.PaddedShare);
        Assert.True(padded.ConstantLineLength);
        Assert.Equal(74L, padded.LineLength);

        var plain = Dialect(ProfileFixture("delimited/semicolon-preamble.csv")).Padding;
        Assert.False(plain.Trim);
        Assert.Equal(0.0, plain.PaddedShare);
        Assert.False(plain.ConstantLineLength);
        Assert.Null(plain.LineLength);
    }

    [Fact]
    public void Dialect_DecimalComma_Semicolon()
    {
        var d = ProfileFixture("delimited/semicolon-decimal-comma.csv");
        var dialect = Dialect(d);
        Assert.Equal(";", dialect.Delimiter);
        Assert.Equal(",", dialect.DecimalSeparator);
        Assert.Equal(new[] { "int", "float", "float" }, d.Table!.Fields.Select(static f => Assert.Single(f.TypeVotes).Key).ToArray());
        var evidence = EvidenceOf(d, "dialect", "decimalSeparator");
        Assert.Equal(",", evidence.Chosen);

        // With a dot decimal in a semicolon file the separator stays a dot.
        Assert.Equal(".", Dialect(ProfileFixture("delimited/semicolon-preamble.csv")).DecimalSeparator);

        // A comma delimiter never gives a decimal comma, even when quoted cells look like one.
        var quoted = ProfileText("a,b\n\"1,5\",x\n\"2,5\",y\n\"3,5\",z\n\"4,5\",w\n\"5,5\",v\n\"6,5\",u\n");
        Assert.Equal(".", Dialect(quoted).DecimalSeparator);
    }

    [Fact]
    public void Dialect_EqualDotAndCommaDecimalCounts_KeepTheDot()
    {
        var text = "a;b\n1,5;2.5\n2,5;3.5\n3,5;4.5\n4,5;5.5\n5,5;6.5\n6,5;7.5\n";
        var d = ProfileText(text);
        Assert.Equal(";", Dialect(d).Delimiter);
        Assert.Equal(".", Dialect(d).DecimalSeparator);
        var evidence = EvidenceOf(d, "dialect", "decimalSeparator");
        Assert.Equal("6 comma-decimal cells", Assert.Single(evidence.Candidates, static c => c.Value == ",").Reason);
        Assert.Equal("6 dot-decimal cells", Assert.Single(evidence.Candidates, static c => c.Value == ".").Reason);
    }

    [Fact]
    public void Dialect_ModalFieldCountTie_GoesToTheLargerK()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 7; i++)
        {
            builder.Append("a,b,c\n");
        }

        for (var i = 0; i < 7; i++)
        {
            builder.Append("a,b,c,d\n");
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(4, d.Table!.Fields.Count);
        Assert.Equal(new MinMax(4, 4), d.Table.FieldCount);
    }

    [Fact]
    public void Dialect_ScoreTie_GoesToTheLargerK_BeforeCandidateOrder()
    {
        // Space-aligned decimal-comma rows: comma gives 3 fields and whitespace runs give 4, and both are perfectly
        // clean (spaces never make a token unclean; a decimal comma is exempt). The larger k wins the tie, not the
        // earlier candidate. (This test used to tie tab with whitespace; owner decision 9 now gives that tie to tab.)
        var builder = new StringBuilder();
        for (var i = 1; i <= 8; i++)
        {
            builder.Append("Lot A ").Append(i).Append(",5 ").Append(i + 1).Append(",3\n");
        }

        var d = ProfileText(builder.ToString());
        var candidates = EvidenceOf(d, "dialect", "delimiter").Candidates;
        Assert.Equal(1.0, Assert.Single(candidates, static c => c.Value == ",").Score);
        Assert.Equal(1.0, Assert.Single(candidates, static c => c.Value == "whitespace").Score);
        Assert.Equal("whitespace", Dialect(d).Delimiter);
    }

    private static string Tsv(string header, Func<int, string> row)
    {
        var builder = new StringBuilder();
        if (header.Length > 0)
        {
            builder.Append(header).Append('\n');
        }

        for (var i = 1; i <= 8; i++)
        {
            builder.Append(row(i)).Append('\n');
        }

        return builder.ToString();
    }

    [Fact]
    public void Dialect_HeaderlessTsvWithMultiWordCells_PicksTab()
    {
        // Whitespace runs split every row into 5 fields (clean, a tie with tab's 3): tab must win the tie.
        var d = ProfileText(Tsv(string.Empty, static i => $"Lot A\tWafer {i}\t1.5"));
        Assert.Equal("\t", Dialect(d).Delimiter);
        Assert.Equal(3, d.Table!.Fields.Count);
        var candidates = EvidenceOf(d, "dialect", "delimiter").Candidates;
        Assert.Equal(1.0, Assert.Single(candidates, static c => c.Value == "whitespace").Score);
    }

    [Fact]
    public void Dialect_TsvWithMultiWordHeaderNames_PicksTab_AndNamesStayWhole()
    {
        var d = ProfileText(Tsv("Lot ID\tWafer No\tVdd", static i => $"Lot A\tWafer {i}\t1.5"));
        Assert.Equal("\t", Dialect(d).Delimiter);
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(new[] { "Lot ID", "Wafer No", "Vdd" }, Names(d));
    }

    [Fact]
    public void Dialect_TsvWithADatetimeColumn_PicksTab()
    {
        var d = ProfileText(Tsv(string.Empty, static i => $"A\t2024-01-0{i} 12:00:00\t1.5"));
        Assert.Equal("\t", Dialect(d).Delimiter);
        Assert.Equal(new[] { "string", "datetime", "float" }, d.Table!.Fields.Select(static f => f.Type).ToArray());
    }

    [Fact]
    public void Dialect_TwoColumnHeaderlessDecimalCommaTsv_PicksTab()
    {
        var d = ProfileText(Tsv(string.Empty, static i => $"Lot A\t{i},5"));
        var dialect = Dialect(d);
        Assert.Equal("\t", dialect.Delimiter);
        Assert.Equal(",", dialect.DecimalSeparator);
        Assert.Equal(new[] { "string", "float" }, d.Table!.Fields.Select(static f => f.Type).ToArray());
    }

    [Fact]
    public void Dialect_TabInsideACommaToken_IsUnclean_ButSpacesAreNot()
    {
        // Comma tokens of the two-column TSV: "Lot A<TAB>1" (a tab inside, unclean) and "5" (clean).
        var tsv = ProfileText(Tsv(string.Empty, static i => $"Lot A\t{i},5"));
        var comma = Assert.Single(EvidenceOf(tsv, "dialect", "delimiter").Candidates, static c => c.Value == ",");
        Assert.StartsWith("k=2; 8/8 records; clean=0.5;", comma.Reason, StringComparison.Ordinal);

        // The same cells with a space instead of the tab: nothing is unclean, so comma is clean.
        var spaced = ProfileText(Tsv(string.Empty, static i => $"Lot A {i},5"));
        var spacedComma = Assert.Single(EvidenceOf(spaced, "dialect", "delimiter").Candidates, static c => c.Value == ",");
        Assert.StartsWith("k=2; 8/8 records; clean=1;", spacedComma.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialect_Quote_IsAlwaysTheDoubleQuote_AndEvidenceCountsQuotedFields()
    {
        var plain = ProfileFixture("lab/sweep-preamble.csv");
        Assert.Equal("\"", Dialect(plain).Quote);
        var plainComma = Assert.Single(EvidenceOf(plain, "dialect", "delimiter").Candidates, static c => c.Value == ",");
        Assert.Equal("k=3; 22/31 records; clean=1; run lines 8-29; 0 quoted fields", plainComma.Reason);

        var quoted = ProfileText("a,b\n\"x,1\",1\n2,2\n\"3\",3\n4,4\n5,5\n6,6\n7,7\n");
        Assert.Equal("\"", Dialect(quoted).Quote);
        var quotedComma = Assert.Single(EvidenceOf(quoted, "dialect", "delimiter").Candidates, static c => c.Value == ",");
        Assert.EndsWith("; 2 quoted fields", quotedComma.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialect_DecimalCommaInsideSemicolonTokens_IsClean()
    {
        var builder = new StringBuilder("a;b,c;d\n");
        for (var i = 0; i < 8; i++)
        {
            builder.Append("1;2,3;4\n");
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(";", Dialect(d).Delimiter);
    }

    [Fact]
    public void Dialect_HeaderlessSemicolonDecimalComma_ChoosesSemicolon()
    {
        var builder = new StringBuilder();
        for (var i = 1; i <= 8; i++)
        {
            builder.Append(i).Append(",5;").Append(i + 1).Append(",3\n");
        }

        var d = ProfileText(builder.ToString());
        var dialect = Dialect(d);
        Assert.Equal(";", dialect.Delimiter);
        Assert.Equal(",", dialect.DecimalSeparator);
        Assert.False(dialect.HasHeader);
        Assert.Equal(new[] { "float", "float" }, d.Table!.Fields.Select(static f => f.Type).ToArray());
        var candidates = EvidenceOf(d, "dialect", "delimiter").Candidates;
        var semicolon = Assert.Single(candidates, static c => c.Value == ";");
        var comma = Assert.Single(candidates, static c => c.Value == ",");
        Assert.Equal("k=2; 8/8 records; clean=1; run lines 1-8; 0 quoted fields", semicolon.Reason);
        Assert.Equal(1.0, semicolon.Score);
        Assert.True(comma.Score < 1.0);
    }

    [Fact]
    public void Dialect_CleanShare_CountsAnotherDelimiterAnywhereInAToken()
    {
        // Semicolon tokens: 1, x,y, 3. The middle one holds a comma that is neither at its start nor at its end,
        // and is not a decimal-comma number, so it is unclean.
        var builder = new StringBuilder();
        for (var i = 0; i < 8; i++)
        {
            builder.Append("1;x,y;3\n");
        }

        var d = ProfileText(builder.ToString());
        var semicolon = Assert.Single(EvidenceOf(d, "dialect", "delimiter").Candidates, static c => c.Value == ";");
        Assert.Equal("k=3; 8/8 records; clean=0.6666666666666666; run lines 1-8; 0 quoted fields", semicolon.Reason);
        Assert.Equal(";", Dialect(d).Delimiter);
    }

    [Fact]
    public void Dialect_ShortTable_RunReachesTheEnd()
    {
        var d = ProfileText("a,b\n1,2\n3,4\n");
        Assert.Equal(",", Dialect(d).Delimiter);
        Assert.Equal(2, d.Table!.Rows);
    }

    // A header and `rows` data records of three fields; the data records in `ragged` (1-based) have two fields.
    private static string RaggedTable(string header, int rows, params int[] ragged)
    {
        var builder = new StringBuilder(header).Append('\n');
        for (var i = 1; i <= rows; i++)
        {
            var id = i.ToString(CultureInfo.InvariantCulture);
            builder.Append(Array.IndexOf(ragged, i) >= 0 ? id + ",x\n" : id + ",x,y\n");
        }

        return builder.ToString();
    }

    private static string CommaReason(Descriptor d)
    {
        return Assert.Single(EvidenceOf(d, "dialect", "delimiter").Candidates, static c => c.Value == ",").Reason;
    }

    [Fact]
    public void RunStart_RaggedRecordInsideTheWindow_KeepsTheHeader()
    {
        var d = ProfileText(RaggedTable("A(id),B,C", 20, 3));
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(new[] { "A(id)", "B", "C" }, Names(d));
        Assert.Equal(20, d.Table!.Rows);
        var ragged = Assert.Single(d.Issues, static i => i.Code == "raggedRows");
        Assert.Equal(1, ragged.Count);
        Assert.Equal(new long[] { 4 }, ragged.FirstLines);
        Assert.Equal("k=3; 20/21 records; clean=1; run lines 1-21; 1 non-k records skipped in the start window; 0 quoted fields", CommaReason(d));
    }

    [Fact]
    public void RunStart_TwoRaggedRecordsInsideTheWindow_KeepsTheHeader()
    {
        var d = ProfileText(RaggedTable("A(id),B,C", 20, 3, 5));
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(new[] { "A(id)", "B", "C" }, Names(d));
        Assert.Equal(new long[] { 4, 6 }, Assert.Single(d.Issues, static i => i.Code == "raggedRows").FirstLines);
        Assert.Contains("run lines 1-21; 2 non-k records skipped in the start window;", CommaReason(d), StringComparison.Ordinal);
    }

    [Fact]
    public void RunStart_ThreeNonKRecordsInsideTheWindow_FailsTheWindow()
    {
        // Records 3, 4 and 5 are ragged: the header cannot start the run, so the start moves to the first record after them.
        var d = ProfileText(RaggedTable("A(id),B,C", 20, 3, 4, 5));
        Assert.Equal("delimited", d.Format.Family);
        Assert.Contains("run lines 7-21; 0 quoted fields", CommaReason(d), StringComparison.Ordinal);
        Assert.DoesNotContain("skipped", CommaReason(d), StringComparison.Ordinal);
        Assert.False(Dialect(d).HasHeader);
    }

    [Fact]
    public void SmallRaggedTable_IsDelimited()
    {
        // A header and nine data records, with data record 5 (file record 6 counting the header) ragged: no k-run has six records, and the one that reaches the end is too short to score.
        var d = ProfileText(RaggedTable("A(id),B,C", 9, 5));
        Assert.Equal("delimited", d.Format.Family);
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(9, d.Table!.Rows);
        Assert.Equal(1, Assert.Single(d.Issues, static i => i.Code == "raggedRows").Count);
    }

    [Fact]
    public void Preamble_KeyValueLines_AreNotAbsorbedIntoTheRun()
    {
        var plain = ProfileText(RaggedTable("A(id),B,C", 20, 3));
        var d = ProfileText("Lot=A\nWafer=7\n" + RaggedTable("A(id),B,C", 20, 3));
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(Names(plain), Names(d));
        Assert.Equal(
            new[] { ("preamble", 1L, 2L), ("header", 3L, 3L), ("data", 4L, 23L) },
            d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine)).ToArray());
    }

    [Fact]
    public void Preamble_TitleLineWithTheSameFieldCount_IsNotAbsorbedIntoTheRun()
    {
        // "Report,," has k fields and two key=value lines follow it. A lone k-field record directly followed by other
        // records must not start the run, or the preamble would turn into annotation rows above the header.
        var d = ProfileText("Report,,\nLot=A\nWafer=7\n" + RaggedTable("A(id),B,C", 20));
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(new[] { "A(id)", "B", "C" }, Names(d));
        Assert.Equal(
            new[] { ("preamble", 1L, 3L), ("header", 4L, 4L), ("data", 5L, 24L) },
            d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine)).ToArray());
        Assert.DoesNotContain("skipped", CommaReason(d), StringComparison.Ordinal);
    }

    [Fact]
    public void Preamble_TwoKFieldLinesThenKeyValue_StayPreamble()
    {
        // Two lines with k fields and a key=value line look like a header, a data record and a ragged record by field
        // counts. The lead rule (three k-field records in a row) keeps them preamble.
        var d = ProfileText("Setup,PA-7,bench 3\nCalibrated,2026-01-02,ok\nOperator=jr\n" + RaggedTable("A(id),B,C", 20));
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(new[] { "A(id)", "B", "C" }, Names(d));
        Assert.Equal(
            new[] { ("preamble", 1L, 3L), ("header", 4L, 4L), ("data", 5L, 24L) },
            d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine)).ToArray());
        Assert.DoesNotContain("skipped", CommaReason(d), StringComparison.Ordinal);
    }

    [Fact]
    public void RunStart_TableWithATotalLineAfterTheRows_IsDelimited()
    {
        // A header, four rows and a one-field footer line: the window reaches the end after skipping the footer line, so
        // the file is delimited (it used to fall back to text because no k-run had six records).
        var d = ProfileText("A(id),B,C\n1,x,y\n2,x,y\n3,x,y\n4,x,y\nTotal=4\n");
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(4, d.Table!.Rows);
        Assert.Equal(
            new[] { ("header", 1L, 1L), ("data", 2L, 5L), ("footer", 6L, 6L) },
            d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine)).ToArray());
        Assert.Contains("run lines 1-5; 1 non-k records skipped in the start window;", CommaReason(d), StringComparison.Ordinal);
    }

    [Fact]
    public void RunStart_Tolerance_DoesNotLetProseBecomeWhitespaceDelimited()
    {
        // Seven lines of five words (one cut short) open the file, and shorter and longer lines follow. The start window
        // now skips the cut-short line and finds a run of six lines, but the run has fewer than half of the records with
        // k fields, so the file stays text.
        var lines = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            lines.Add("one two three four five");
        }

        lines[3] = "one two three";
        for (var i = 0; i < 10; i++)
        {
            lines.Add(i % 2 == 0 ? "short line here ok" : "a somewhat longer line of prose follows here");
        }

        var d = ProfileText(string.Join('\n', lines) + "\n", name: "t.txt");
        Assert.Equal("text", d.Format.Family);
    }

    [Fact]
    public void Dialect_QuotedFieldOfExactlyTheLimit_IsNotRunaway_OneMoreIs()
    {
        static string Table(int quoted)
        {
            var builder = new StringBuilder("a,b\n");
            for (var i = 0; i < 8; i++)
            {
                builder.Append(i).Append(",x\n");
            }

            builder.Append("9,\"").Append('q', quoted).Append("\"\n");
            return builder.ToString();
        }

        var limit = (int)Limits.Default.RunawayQuote;
        var ok = ProfileText(Table(limit));
        Assert.Equal(",", Dialect(ok).Delimiter);
        Assert.DoesNotContain("disqualified", Assert.Single(EvidenceOf(ok, "dialect", "delimiter").Candidates, static c => c.Value == ",").Reason, StringComparison.Ordinal);
        var runaway = ProfileText(Table(limit + 1));
        Assert.Contains("runaway quote opened at line 10", Assert.Single(EvidenceOf(runaway, "dialect", "delimiter").Candidates, static c => c.Value == ",").Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Regions_PreambleKeyDelimValue_And_ExcelStyleKeyValue_Lines()
    {
        var builder = new StringBuilder("Lot,A12\nWafer:,7\nSite:,3,,\n12:30,x\n");
        builder.Append("a,b,c\n");
        for (var i = 0; i < 8; i++)
        {
            builder.Append("1,2,3\n");
        }

        var d = ProfileText(builder.ToString(), includeValues: true);
        var preamble = RegionOf(d, "preamble");
        Assert.Equal(new[] { "Lot", "Wafer", "Site", "12:30" }, preamble.Keys);
        Assert.Equal(new[] { "A12", "7", "3", "x" }, preamble.Values);
        Assert.Equal(new[] { ("keyDelimValue", 2L), ("keyValue", 2L) }, preamble.LineKinds!.Select(static e => (e.Key, e.Value)).ToArray());
    }

    [Fact]
    public void Regions_PreambleKeyOf64Characters_IsKeyValue_And65IsText()
    {
        var builder = new StringBuilder();
        builder.Append('k', 64).Append("=1\n").Append('k', 65).Append("=2\n");
        builder.Append("a,b,c\n");
        for (var i = 0; i < 8; i++)
        {
            builder.Append("1,2,3\n");
        }

        var preamble = RegionOf(ProfileText(builder.ToString()), "preamble");
        Assert.Equal(new[] { new string('k', 64) }, preamble.Keys);
        Assert.Equal(new[] { ("keyValue", 1L), ("text", 1L) }, preamble.LineKinds!.Select(static e => (e.Key, e.Value)).ToArray());
    }

    [Fact]
    public void Header_LeadingNullHeavyRows_HeaderStillFound()
    {
        var d = ProfileFixture("delimited/leading-separator-rows.csv");
        var dialect = Dialect(d);
        Assert.True(dialect.HasHeader);
        Assert.False(dialect.UnitsRow);
        Assert.Equal(new[] { "Pout(dBm)", "Gain(dB)", "Status" }, Names(d));
        Assert.Equal(
            new[] { ("header", 1L, 1L), ("data", 2L, 141L) },
            d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine)).ToArray());
        Assert.Equal(140, d.Table!.Rows);
        var fields = d.Table.Fields;
        Assert.Equal(new[] { ("float", 20L) }, fields[0].TypeVotes.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Equal(new[] { ("string", 80L) }, fields[2].TypeVotes.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Equal(new LineEndings(141, 0, 0), d.Encoding.LineEndings);
    }

    [Fact]
    public void Layout_MaxLineLength_Reported()
    {
        var builder = new StringBuilder("a,b\n1,x\n2,y\n3,");
        builder.Append('z', 100_000).Append('\n');
        for (var i = 4; i <= 9; i++)
        {
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(",w\n");
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(",", Dialect(d).Delimiter);
        Assert.Equal(100_002L, d.Layout.MaxLineLength);
        Assert.Equal(new[] { ("string", 9L) }, d.Table!.Fields[1].TypeVotes.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Equal(9, d.Table.Rows);
    }

    [Fact]
    public void Dialect_LoneQuoteInQuotedField_IsLiteral()
    {
        // An ATE export's writer quotes a field that holds a comma but does not escape quotes inside it.
        var text = "Name,Note,Val\n"
            + "a,\"5\" pipe, 3\",1.5\n"
            + "b,\"say \"\"hi\"\", ok\",2.5\n"
            + "c,plain,3.5\n"
            + "d,\"6\" cable\",4.5\n"
            + "e,plain,5.5\n"
            + "f,plain,6.5\n"
            + "g,plain,7.5\n";
        var d = ProfileText(text, includeValues: true);
        Assert.Equal(",", Dialect(d).Delimiter);
        Assert.Equal(new MinMax(3, 3), d.Table!.FieldCount);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "raggedRows");
        Assert.Equal(7, d.Table.Rows);
        var comma = Assert.Single(EvidenceOf(d, "dialect", "delimiter").Candidates, static c => c.Value == ",");
        Assert.EndsWith("; 2 literal quotes", comma.Reason, StringComparison.Ordinal);
        Assert.Equal(new[] { ("float", 7L) }, d.Table.Fields[2].TypeVotes.Select(static e => (e.Key, e.Value)).ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("NA")]
    [InlineData("-")]
    [InlineData("null")]
    public void UnitsRow_BlankCellsForUnitlessColumns_StillUnits(string unitless)
    {
        var text = "Freq(MHz),Pout(dBm),Idx,Phase(deg)\nMHz,dBm," + unitless + ",deg\n"
            + "700,10.5,1,45.5\n800,11.5,2,46.5\n900,12.5,3,47.5\n1000,13.5,4,48.5\n1100,14.5,5,49.5\n1200,15.5,6,50.5\n";
        var d = ProfileText(text, includeValues: true);
        var dialect = Dialect(d);
        Assert.True(dialect.HasHeader);
        Assert.True(dialect.UnitsRow);
        var units = RegionOf(d, "units");
        Assert.Equal((2L, 2L), (units.FirstLine, units.LastLine));
        Assert.Equal(6, d.Table!.Rows);
        Assert.Equal(new string?[] { "MHz", "dBm", null, "deg" }, d.Table.Fields.Select(static f => f.Unit).ToArray());
    }

    [Fact]
    public void UnitsRow_AllNullFirstDataRow_IsNotUnits()
    {
        // An ATE main CSV: interleaved all-NULL separator rows. Nulls are neutral, but a units row needs one value.
        var text = "Pout(dBm),RowID(id),Gain(dB)\r\nNULL,NULL,NULL\r\n10.5,1,15.5\r\n11.5,2,16.5\r\nNULL,NULL,NULL\r\n12.5,3,17.5\r\n13.5,4,18.5\r\n14.5,5,19.5\r\n";
        var d = ProfileText(text);
        var dialect = Dialect(d);
        Assert.True(dialect.HasHeader);
        Assert.False(dialect.UnitsRow);
        Assert.Equal(new[] { "Pout(dBm)", "RowID(id)", "Gain(dB)" }, Names(d));
        Assert.Equal(7, d.Table!.Rows);
        Assert.Equal(new LineEndings(8, 0, 0), d.Encoding.LineEndings);
        Assert.Equal(new[] { ("header", 1L, 1L), ("data", 2L, 8L) }, d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine)).ToArray());
    }

    [Fact]
    public void Header_UnitsRowMustFailEveryNonNullColumnType()
    {
        // The row below the header holds a number in a numeric column, so it is data, not a units row.
        var d = ProfileText("Freq(MHz),Pout(dBm)\n700,dBm\n800,11.5\n900,12.5\n1000,13.5\n1100,14.5\n1200,15.5\n1300,16.5\n");
        Assert.False(Dialect(d).UnitsRow);
        Assert.Equal(7, d.Table!.Rows);
    }

    private static SampleRecord Units(string[] cells)
    {
        var record = new SampleRecord { Line = 2, StartByte = 0, Cells = cells, IsComment = false };
        HeaderDetector.Classify([record], cells.Length, '.');
        return record;
    }

    [Fact]
    public void IsUnitsRow_NullCellsAreNeutral_ButOneValueIsRequired()
    {
        var types = new[] { ValueVote.Int, ValueVote.Float, ValueVote.Float };
        Assert.True(HeaderDetector.IsUnitsRow(Units(["MHz", "", "NA"]), types, 3));
        Assert.False(HeaderDetector.IsUnitsRow(Units(["", "NA", "-"]), types, 3));
        Assert.False(HeaderDetector.IsUnitsRow(Units(["NULL", "NULL", "NULL"]), types, 3));
    }

    [Fact]
    public void IsUnitsRow_EveryNonNullFieldMustFailItsColumnType()
    {
        var types = new[] { ValueVote.Int, ValueVote.Float };
        Assert.True(HeaderDetector.IsUnitsRow(Units(["MHz", "dBm"]), types, 2));
        Assert.False(HeaderDetector.IsUnitsRow(Units(["700", "dBm"]), types, 2));
        Assert.False(HeaderDetector.IsUnitsRow(Units(["MHz", "11.5"]), types, 2));
    }

    [Fact]
    public void IsUnitsRow_NeedsNonStringColumnsAndShortText()
    {
        Assert.False(HeaderDetector.IsUnitsRow(Units(["kind", "MHz"]), [ValueVote.String, ValueVote.Int], 2));
        Assert.False(HeaderDetector.IsUnitsRow(Units(["kind", "MHz"]), [ValueVote.Null, ValueVote.Int], 2));
        Assert.True(HeaderDetector.IsUnitsRow(Units(["", "MHz"]), [ValueVote.String, ValueVote.Int], 2));
        Assert.True(HeaderDetector.IsUnitsRow(Units(["sixteen chars ok", "MHz"]), [ValueVote.Int, ValueVote.Int], 2));
        Assert.False(HeaderDetector.IsUnitsRow(Units(["seventeen chars ok", "MHz"]), [ValueVote.Int, ValueVote.Int], 2));
    }

    [Fact]
    public void Header_DuplicateAndEmptyNames_StillHeader_WithIssues()
    {
        var d = ProfileText("Pout,pout,,Gain\n1.5,2.5,3.5,4.5\n2.5,3.5,4.5,5.5\n3.5,4.5,5.5,6.5\n4.5,5.5,6.5,7.5\n5.5,6.5,7.5,8.5\n6.5,7.5,8.5,9.5\n");
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(new[] { "Pout", "pout", "", "Gain" }, Names(d));
        var duplicate = Assert.Single(d.Issues, static i => i.Code == "duplicateHeader");
        Assert.Equal(1, duplicate.Count);
        Assert.Equal(new long[] { 1 }, duplicate.FirstLines);
        var empty = Assert.Single(d.Issues, static i => i.Code == "emptyHeaderName");
        Assert.Equal(1, empty.Count);
        Assert.Equal(new long[] { 1 }, empty.FirstLines);
        var evidence = EvidenceOf(d, "header", "candidates");
        Assert.Equal("1", evidence.Chosen);
        Assert.StartsWith("relaxed", Assert.Single(evidence.Candidates, static c => c.Value == "1").Reason, StringComparison.Ordinal);
        Assert.Equal(6, d.Table!.Rows);
    }

    [Fact]
    public void Header_NamesThatDifferOnlyInAsciiCase_AreNotDistinct()
    {
        var d = ProfileText("Pout,POUT\n1.5,2.5\n2.5,3.5\n3.5,4.5\n4.5,5.5\n5.5,6.5\n6.5,7.5\n");
        var evidence = EvidenceOf(d, "header", "candidates");
        Assert.StartsWith("relaxed", Assert.Single(evidence.Candidates, static c => c.Value == "1").Reason, StringComparison.Ordinal);
        Assert.Contains("unique=0.5", Assert.Single(evidence.Candidates, static c => c.Value == "1").Reason, StringComparison.Ordinal);
        Assert.Contains(d.Issues, static i => i.Code == "duplicateHeader");
    }

    [Fact]
    public void CommentInData_IsCountedAndSkipped_AndFooterIsRegioned()
    {
        var text = "a,b\n1,2\n2,3\n3,4\n4,5\n5,6\n6,7\n7,8\n# a comment inside the table\n8,9\n9,10\n10,11\nend of file\n";
        var d = ProfileText(text);
        var comment = Assert.Single(d.Issues, static i => i.Code == "commentInData");
        Assert.Equal(1, comment.Count);
        Assert.Equal(new long[] { 9 }, comment.FirstLines);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "raggedRows");
        Assert.Equal(10, d.Table!.Rows);
        Assert.Equal(
            new[] { ("header", 1L, 1L), ("data", 2L, 12L), ("footer", 13L, 13L) },
            d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine)).ToArray());
    }

    [Fact]
    public void Regions_BlankLines_BelongToTheEarlierRegion()
    {
        var text = "\n\nLot=A\n\na,b\n1,2\n\n2,3\n3,4\n4,5\n5,6\n6,7\n7,8\n\nDone\n\n";
        var d = ProfileText(text);
        Assert.Equal(
            new[] { ("preamble", 1L, 4L), ("header", 5L, 5L), ("data", 6L, 14L), ("footer", 15L, 16L) },
            d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine)).ToArray());
        var preamble = RegionOf(d, "preamble");
        Assert.Equal(new[] { ("blank", 3L), ("keyValue", 1L) }, preamble.LineKinds!.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Equal(7, d.Table!.Rows);
        var last = d.Layout.Regions[^1];
        Assert.Equal(text.Length, last.EndByte);
    }

    [Fact]
    public void Regions_Utf16WithBom_ByteRangesAreBytes()
    {
        var text = "Lot=A\na,b\n1,2\n2,3\n3,4\n4,5\n5,6\n6,7\n";
        var bytes = TestBytes.Concat(Encoding.Unicode.GetPreamble(), Encoding.Unicode.GetBytes(text));
        var d = Profiler.Profile(new BytesSource("u16.csv", bytes), Options());
        Assert.Equal("utf-16le", d.Encoding.Charset);
        Assert.Equal(",", Dialect(d).Delimiter);
        var regions = d.Layout.Regions;
        Assert.Equal(new[] { "preamble", "header", "data" }, regions.Select(static r => r.Kind).ToArray());
        Assert.Equal(0L, regions[0].StartByte);
        Assert.Equal(2L + (2 * "Lot=A\n".Length), regions[0].EndByte);
        Assert.Equal(regions[0].EndByte, regions[1].StartByte);
        Assert.Equal(bytes.Length, regions[^1].EndByte);
    }

    [Fact]
    public void Dialect_WhitespaceRun_NeedsAFullWindow_SoShortProseStaysText()
    {
        Assert.Equal("text", ProfileText("café one\nline two\n").Format.Family);
        Assert.Equal("text", ProfileText("a b c\n1 2 3\n4 5 6\n7 8 9\n10 11 12\n").Format.Family);
        Assert.Equal("delimited", ProfileText("a b c\n1 2 3\n4 5 6\n7 8 9\n10 11 12\n13 14 15\n").Format.Family);
    }

    [Fact]
    public void AteMainCsv_HasHeader_NoUnitsRow_QuotedFieldsWithCommas()
    {
        var builder = new StringBuilder("Pout(dBm),RowID(id),Mode,Note\r\n");
        builder.Append("NULL,NULL,NULL,NULL\r\n");
        for (var i = 1; i <= 8; i++)
        {
            builder.Append((10 + i).ToString(CultureInfo.InvariantCulture)).Append(".5,").Append(i.ToString(CultureInfo.InvariantCulture)).Append(",CW,").Append(i % 2 == 0 ? "\"a, b\"" : "ok").Append("\r\n");
            builder.Append("NULL,NULL,NULL,NULL\r\n");
        }

        var d = ProfileText(builder.ToString());
        var dialect = Dialect(d);
        Assert.Equal(",", dialect.Delimiter);
        Assert.Equal("\"", dialect.Quote);
        Assert.True(dialect.HasHeader);
        Assert.False(dialect.UnitsRow);
        Assert.Equal(new[] { "Pout(dBm)", "RowID(id)", "Mode", "Note" }, Names(d));
        Assert.Equal(17, d.Table!.Rows);
        Assert.Equal(new LineEndings(18, 0, 0), d.Encoding.LineEndings);
        Assert.Equal(new[] { "float", "int", "string", "string" }, d.Table.Fields.Select(static f => f.Type).ToArray());
        Assert.Equal(new long[] { 9, 9, 9, 9 }, d.Table.Fields.Select(static f => f.Nulls).ToArray());
        Assert.Equal(new long[] { 0, 0, 0, 0 }, d.Table.Fields.Select(static f => f.NullsExcludingAllNullRows).ToArray());
    }

    [Fact]
    public void Profile_Evidence_StagesAreInPipelineOrder()
    {
        var d = ProfileFixture("lab/sweep-preamble.csv");
        Assert.Equal(
            new[] { "encoding/charset", "encoding/class", "format/family", "dialect/decimalSeparator", "dialect/delimiter", "header/candidates", "unit/headerConvention" },
            d.Evidence.Select(static e => e.Stage + "/" + e.Topic).ToArray());
    }

    [Fact]
    public void Dialect_RunLimit_IsExactlyTheLimitOfRuns()
    {
        // 100,000 runs are allowed; one more is "too irregular". Alternate 2 and 3 fields: each line is a run.
        static string Alternating(int lines)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < lines; i++)
            {
                builder.Append(i % 2 == 0 ? "a,b\n" : "a,b,c\n");
            }

            return builder.ToString();
        }

        var limit = (int)Limits.Default.FieldCountRuns;
        Assert.DoesNotContain(ProfileText(Alternating(limit)).Issues, static i => i.Code == "tooIrregular");
        Assert.Contains(ProfileText(Alternating(limit + 1)).Issues, static i => i.Code == "tooIrregular");
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
        Assert.Equal(baseline, DescriptorWriter.ToBytes(ProfileFixture(fixture)));

        var saved = CultureInfo.CurrentCulture;
        var savedUi = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var name in new[] { "de-DE", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(baseline, DescriptorWriter.ToBytes(ProfileFixture(fixture)));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUi;
        }
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

    [Fact]
    public void Profile_SemiconductorSweep_NamesAll13HeaderFieldsInOrder()
    {
        var d = ProfileFixture("delimited/semiconductor-sweep.csv");
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(
            new[]
            {
                "stimulusGrp(id)", "sweep(id)", "SN", "Date(date)", "Teststand", "Temp(degC)", "Vcc(V)",
                "Frequency(MHz)", "Pout(dBm)", "Pin(dBm)", "Gain(dB)", "I_Total(A)", "EVM(dB)",
            },
            Names(d));
        Assert.Equal(81, d.Table!.Rows);
    }

    [Fact]
    public void Profile_HeaderNameWithInvalidBytes_CarriesNameHex()
    {
        var bytes = TestBytes.Concat(
            TestBytes.Ascii("Freq,P"),
            TestBytes.Hex("FF"),
            TestBytes.Ascii("out,Gain\n"),
            TestBytes.Ascii("1,2.5,3.5\n2,3.5,4.5\n3,4.5,5.5\n4,5.5,6.5\n5,6.5,7.5\n6,7.5,8.5\n"));
        var d = Profiler.Profile(new BytesSource("bad.csv", bytes), Options());
        var field = d.Table!.Fields[1];
        Assert.Equal("P�out", field.Name);
        Assert.Equal("50ff6f7574", field.NameHex);
        Assert.Null(d.Table.Fields[0].NameHex);
    }
}
