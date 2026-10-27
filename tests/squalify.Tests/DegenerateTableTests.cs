// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>sq-34: the degenerate delimited tables (header only, one column, ragged start) and the run-start window rules.</summary>
public sealed class DegenerateTableTests
{
    private static Descriptor ProfileText(string text, string name = "t.csv")
    {
        return Profiler.Profile(new BytesSource(name, Encoding.UTF8.GetBytes(text)), ProfileOptions.Create(false, Array.Empty<string>()));
    }

    private static DelimitedDialect Dialect(Descriptor d)
    {
        Assert.Equal("delimited", d.Format.Family);
        return Assert.IsType<DelimitedDialect>(d.Format.Dialect);
    }

    private static string[] Names(Descriptor d)
    {
        return d.Table!.Fields.Select(static f => f.Name!).ToArray();
    }

    private static (string Kind, long First, long Last)[] Regions(Descriptor d)
    {
        return d.Layout.Regions.Select(static r => (r.Kind, r.FirstLine, r.LastLine)).ToArray();
    }

    // A header and `rows` data records of three fields; the data records in `ragged` (1-based) have two fields.
    private static string RaggedTable(int rows, params int[] ragged)
    {
        var builder = new StringBuilder("A(id),B,C\n");
        for (var i = 1; i <= rows; i++)
        {
            var id = i.ToString(CultureInfo.InvariantCulture);
            builder.Append(Array.IndexOf(ragged, i) >= 0 ? id + ",x\n" : id + ",x,y\n");
        }

        return builder.ToString();
    }

    private static string Column(string header, int rows)
    {
        var builder = new StringBuilder(header).Append('\n');
        for (var i = 1; i <= rows; i++)
        {
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return builder.ToString();
    }

    // Header only.

    [Fact]
    public void HeaderOnly_IsClaimed_WithZeroDataRows()
    {
        var d = ProfileText("Freq(MHz),Pout(dBm),Gain(dB)\n");
        var dialect = Dialect(d);
        Assert.Equal(",", dialect.Delimiter);
        Assert.True(dialect.HasHeader);
        Assert.Equal(new[] { "Freq(MHz)", "Pout(dBm)", "Gain(dB)" }, Names(d));
        Assert.Equal(0, d.Table!.Rows);
        Assert.Equal(new MinMax(3, 3), d.Table.FieldCount);
        Assert.All(d.Table.Fields, static f => Assert.Equal("empty", f.Type));
        Assert.Equal(new[] { ("header", 1L, 1L) }, Regions(d));
        var issue = Assert.Single(d.Issues, static i => i.Code == "noDataRows");
        Assert.Equal(1, issue.Count);
        Assert.Equal(new long[] { 1 }, issue.FirstLines);
    }

    [Theory]
    [InlineData("a\tb\tc\n", "\t")]
    [InlineData("a;b;c\r\n", ";")]
    [InlineData("a|b|c", "|")]
    public void HeaderOnly_EachDelimiter_AndWithoutAFinalLineBreak(string text, string delimiter)
    {
        var d = ProfileText(text);
        Assert.Equal(delimiter, Dialect(d).Delimiter);
        Assert.Equal(new[] { "a", "b", "c" }, Names(d));
        Assert.Equal(0, d.Table!.Rows);
        Assert.Contains(d.Issues, static i => i.Code == "noDataRows");
    }

    [Fact]
    public void HeaderOnly_AfterAPreamble_KeepsThePreamble()
    {
        var d = ProfileText("# sweep aborted\nLot=A12\nFreq(MHz),Pout(dBm),Gain(dB)\n\n");
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(0, d.Table!.Rows);
        Assert.Equal(new[] { ("preamble", 1L, 2L), ("header", 3L, 4L) }, Regions(d));
        Assert.Contains(d.Issues, static i => i.Code == "noDataRows");
    }

    [Theory]
    [InlineData("1,2,3\n")]
    [InlineData("a,a,b\n")]
    [InlineData("a,,c\n")]
    [InlineData("a,b\njust some text\n")]
    [InlineData("x y z\n")]
    [InlineData("Hello, world\n")]
    [InlineData("A title line\na,b,c\n")]
    [InlineData("Test Name,Result\n")]
    [InlineData("# Title\n\nHello, world.\n")]
    [InlineData("#!/bin/sh\n# comment\necho a, b\n")]
    [InlineData("ERROR: failed, retrying\n")]
    [InlineData("host=foo, port=80\n")]
    [InlineData("squalify profiles one data file.\nIt writes a descriptor, and nothing else\n")]
    [InlineData("Dear Sir, I am writing to you about the matter that we discussed at length on the telephone last week\n")]
    public void HeaderOnly_NotAHeader_StaysText(string text)
    {
        var d = ProfileText(text);
        Assert.Equal("text", d.Format.Family);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "noDataRows");
    }

    [Fact]
    public void NoDataRowsIssue_IsAbsent_WhenTheTableHasRows()
    {
        var d = ProfileText("a,b\n1,2\n3,4\n");
        Assert.DoesNotContain(d.Issues, static i => i.Code == "noDataRows");
    }

    // One column.

    [Fact]
    public void OneColumn_WithAClearHeader_IsClaimedWithK1()
    {
        var d = ProfileText(Column("value", 6));
        var dialect = Dialect(d);
        Assert.Equal("none", dialect.Delimiter);
        Assert.True(dialect.HasHeader);
        Assert.Equal(new[] { "value" }, Names(d));
        Assert.Equal(6, d.Table!.Rows);
        Assert.Equal(new MinMax(1, 1), d.Table.FieldCount);
        Assert.Equal("int", Assert.Single(d.Table.Fields).Type);
        Assert.Equal(new[] { ("header", 1L, 1L), ("data", 2L, 7L) }, Regions(d));
        Assert.DoesNotContain(d.Issues, static i => i.Code == "noDataRows");
    }

    [Fact]
    public void OneColumn_HeaderAndTwoValues_IsTheSmallestClaim()
    {
        var d = ProfileText("n\n1\n2\n");
        Assert.Equal("none", Dialect(d).Delimiter);
        Assert.Equal(2, d.Table!.Rows);
    }

    [Fact]
    public void OneColumn_WithAClearHeader_WritesTheWordNone_InDialectHintsAndEvidence()
    {
        using var json = System.Text.Json.JsonDocument.Parse(DescriptorWriter.ToBytes(ProfileText(Column("value", 6))));
        var dialect = json.RootElement.GetProperty("format").GetProperty("dialect");
        Assert.Equal("none", dialect.GetProperty("delimiter").GetString());
        Assert.True(dialect.GetProperty("hasHeader").GetBoolean());
        Assert.Equal("none", json.RootElement.GetProperty("hints").GetProperty("delimiter").GetString());
        var evidence = json.RootElement.GetProperty("evidence").EnumerateArray().Single(static e => e.GetProperty("stage").GetString() == "dialect" && e.GetProperty("topic").GetString() == "delimiter");
        Assert.Equal("none", evidence.GetProperty("chosen").GetString());
        var decimalEvidence = json.RootElement.GetProperty("evidence").EnumerateArray().Single(static e => e.GetProperty("topic").GetString() == "decimalSeparator");
        Assert.DoesNotContain("the delimiter is a comma", decimalEvidence.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OneColumn_QuotedCellsHoldingDelimiters_StayOneColumn()
    {
        var d = ProfileText("name\n\"a,b\"\n\"c;d\"\n\"e|f\"\n");
        Assert.Equal("text", d.Format.Family);
        var numbered = ProfileText("n\n1\n2\n3\n");
        Assert.Equal("none", Dialect(numbered).Delimiter);
    }

    [Theory]
    [InlineData("The quick brown fox\njumps over\nthe lazy dog\nagain and again\nuntil dawn\n")]
    [InlineData("INFO started\nINFO connected\nWARN slow\nINFO done\nINFO stopped\n")]
    [InlineData("alpha\nbeta\ngamma\ndelta\nepsilon\n")]
    [InlineData("1\n2\n3\n4\n5\n6\n")]
    [InlineData("value\n")]
    [InlineData("value\n1\n")]
    public void OneColumn_ProseLogOrNoClearHeader_StaysText(string text)
    {
        var d = ProfileText(text, "t.txt");
        Assert.Equal("text", d.Format.Family);
    }

    // The run start.

    [Theory]
    [InlineData(1, 2L)]
    [InlineData(2, 3L)]
    public void RaggedAmongTheFirstTwoDataRecords_IsReported_AndTheHeaderIsKept(int raggedRecord, long raggedLine)
    {
        var d = ProfileText(RaggedTable(20, raggedRecord));
        var dialect = Dialect(d);
        Assert.True(dialect.HasHeader);
        Assert.Equal(new[] { "A(id)", "B", "C" }, Names(d));
        Assert.Equal(20, d.Table!.Rows);
        var ragged = Assert.Single(d.Issues, static i => i.Code == "raggedRows");
        Assert.Equal(1, ragged.Count);
        Assert.Equal(new[] { raggedLine }, ragged.FirstLines);
        Assert.Equal(new[] { ("header", 1L, 1L), ("data", 2L, 21L) }, Regions(d));
    }

    [Fact]
    public void RaggedAmongTheFirstDataRecords_ASmallTable_IsStillDelimitedWithItsHeader()
    {
        var d = ProfileText("A(id),B,C\n1,x,y\n2,x\n3,x,y\n4,x,y\n");
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(4, d.Table!.Rows);
        Assert.Equal(new long[] { 3 }, Assert.Single(d.Issues, static i => i.Code == "raggedRows").FirstLines);
    }

    [Fact]
    public void RunStartWindow_SkipsOnlyMultiFieldRaggedRecords_OneFieldLinesStayPreamble()
    {
        // Three k-field lines and then a one-field key=value line. Without the lead rule only the window rule keeps
        // them preamble: a one-field line is not a ragged row, it ends the window.
        var d = ProfileText("Setup,PA-7,bench 3\nCalibrated,2026-01-02,ok\nChecked,2026-01-03,ok\nOperator=jr\n" + RaggedTable(20));
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(new[] { "A(id)", "B", "C" }, Names(d));
        Assert.Equal(new[] { ("preamble", 1L, 4L), ("header", 5L, 5L), ("data", 6L, 25L) }, Regions(d));
        Assert.DoesNotContain(d.Issues, static i => i.Code == "raggedRows");
    }

    [Fact]
    public void RunStartWindow_SkipsAMultiFieldRaggedRecord_AfterAOneRecordLead()
    {
        // A k-field title line, then a two-field line: by field counts the title is a header and the second line a
        // ragged data record, so the run starts at the title. The header detector then finds the real header.
        var d = ProfileText("Report,Q1,done\nTotal,12\n" + RaggedTable(20));
        Assert.Equal("delimited", d.Format.Family);
        Assert.Equal(new[] { "A(id)", "B", "C" }, Names(d));
        Assert.Equal(new[] { ("annotation", 1L, 2L), ("header", 3L, 3L), ("data", 4L, 23L) }, Regions(d));
    }

    [Fact]
    public void RunStartWindow_SkipsACommentLine_InsideTheWindow()
    {
        var d = ProfileText("A(id),B,C\n1,x,y\n2,x,y\n3,x,y\n# note\n4,x,y\n5,x,y\n6,x,y\n7,x,y\n");
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(7, d.Table!.Rows);
        Assert.Equal(1, Assert.Single(d.Issues, static i => i.Code == "commentInData").Count);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "raggedRows");
    }

    [Fact]
    public void RunStartWindow_SkipsATrailingOneFieldFooter()
    {
        var d = ProfileText("A(id),B,C\n1,x,y\n2,x,y\n3,x,y\n4,x,y\nTotal=4\n");
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(new[] { ("header", 1L, 1L), ("data", 2L, 5L), ("footer", 6L, 6L) }, Regions(d));
    }

    [Theory]
    [InlineData("Time,Vcc(V),Icc\n")]
    [InlineData("\"Test Name\",Result\n")]
    public void HeaderOnly_SingleWordNamesOrQuotedCells_AreAHeader(string text)
    {
        var d = ProfileText(text);
        Assert.Equal("delimited", d.Format.Family);
        Assert.Equal(0, d.Table!.Rows);
    }

    [Fact]
    public void HeaderOnly_WritesNoConstantLineLength_AndAnEvidenceEntryForBothShapes()
    {
        var d = ProfileText("Freq(MHz),Pout(dBm)\n");
        var padding = Dialect(d).Padding;
        Assert.False(padding.ConstantLineLength);
        Assert.Null(padding.LineLength);
        var shape = Assert.Single(d.Evidence, static e => e.Stage == "dialect" && e.Topic == "shape");
        Assert.Equal("headerOnly", shape.Chosen);
        Assert.Equal(new[] { ("headerOnly", 1.0), ("singleColumn", 0.0) }, shape.Candidates.Select(static c => (c.Value, c.Score)).ToArray());
        Assert.StartsWith("declined: ", shape.Candidates[1].Reason, StringComparison.Ordinal);

        var column = ProfileText(Column("value", 6));
        var columnShape = Assert.Single(column.Evidence, static e => e.Stage == "dialect" && e.Topic == "shape");
        Assert.Equal("singleColumn", columnShape.Chosen);
        Assert.Equal(new[] { ("headerOnly", 0.0), ("singleColumn", 1.0) }, columnShape.Candidates.Select(static c => (c.Value, c.Score)).ToArray());
        Assert.StartsWith("declined: ", columnShape.Candidates[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderOnly_HeaderBeyondTheHeaderSample_StaysText()
    {
        var limit = (int)Limits.Default.HeaderSample;
        string File(int keys) => string.Concat(Enumerable.Range(0, keys).Select(static i => "Key" + i.ToString(CultureInfo.InvariantCulture) + "=1\n")) + "a,b,c\n";

        // The header on line `limit` is within the sample; one line later it is not, and the file is not read for it.
        Assert.Equal("delimited", ProfileText(File(limit - 1)).Format.Family);
        Assert.Equal("text", ProfileText(File(limit)).Format.Family);
    }

    // Footer lines after the last full-width row never count against the window. The table still needs half of the
    // records to be full-width rows (the dialect score), so five footer lines after five rows is the limit.

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void RunStartWindow_SeveralFooterLines_AreATable(int footerLines)
    {
        var footer = string.Concat(Enumerable.Range(1, footerLines).Select(static i => "note " + i.ToString(CultureInfo.InvariantCulture) + "\n"));
        var d = ProfileText("A(id),B,C\n1,x,y\n2,x,y\n3,x,y\n4,x,y\n" + footer);
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(4, d.Table!.Rows);
        Assert.Equal(new[] { ("header", 1L, 1L), ("data", 2L, 5L), ("footer", 6L, 5L + footerLines) }, Regions(d));
        Assert.DoesNotContain(d.Issues, static i => i.Code == "raggedRows");
    }

    // A one-field line inside a table (after the header and three rows) is a stray row, reported as ragged.

    [Fact]
    public void RunStartWindow_StrayOneFieldLine_InsideTheTable_IsRaggedAndKeepsTheHeader()
    {
        var d = ProfileText("A,B,C\n1,x,y\n2,x,y\n3,x,y\nfoo bar\n4,x,y\n5,x,y\n6,x,y\n");
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(new[] { "A", "B", "C" }, Names(d));
        Assert.Equal(new long[] { 5 }, Assert.Single(d.Issues, static i => i.Code == "raggedRows").FirstLines);
        Assert.Equal(new[] { ("header", 1L, 1L), ("data", 2L, 8L) }, Regions(d));
    }

    [Fact]
    public void RunStartWindow_OneFieldLine_BeforeTheTable_StillEndsTheWindow()
    {
        // Two k-field lines and a stray one-field line: fewer than four k-field records came first, so it is not skipped.
        var d = ProfileText("Setup,PA-7,bench 3\nCalibrated,2026-01-02,ok\nOperator=jr\n" + RaggedTable(20));
        Assert.Equal(new[] { ("preamble", 1L, 3L), ("header", 4L, 4L), ("data", 5L, 24L) }, Regions(d));
    }

    // The comment probe of pass 1 must agree with LineClassifier.IsComment, which pass 2 and the header detector use.

    [Theory]
    [InlineData("Comma")]
    [InlineData("Tab")]
    [InlineData("Whitespace")]
    public void CommentProbe_AgreesWithLineClassifier(string kindName)
    {
        var kind = Enum.Parse<DelimiterKind>(kindName);
        string[] lines =
        [
            "# note", "#", "!x", "! x", "// x", "//", "/ /", "/", "/x", "x#", "x//", "  # lead", "\t# tab", " \t //x", "\u00A0# nbsp",
            "\"# quoted\"", "\"a\",# b", "a,#", "1,2,3", "!", "#!/bin/sh",
        ];
        foreach (var line in lines)
        {
            var sink = new CommentSink();
            var tokenizer = new RecordTokenizer(kind, sink, 65_536, collectCells: true, rawCap: static _ => int.MaxValue);
            long offset = 0;
            foreach (var c in line)
            {
                tokenizer.Char(c, offset++);
            }

            tokenizer.Break(offset + 1, offset);
            tokenizer.Flush(offset + 1);
            var record = Assert.Single(sink.Records);
            Assert.True(LineClassifier.IsComment(record.Raw) == record.IsComment, "line: " + line);
        }
    }

    private sealed class CommentSink : IRecordSink
    {
        public List<(string Raw, bool IsComment)> Records { get; } = [];

        public void Record(RecordInfo record) => Records.Add((record.Raw ?? string.Empty, record.IsComment));

        public void Blank(long line)
        {
        }
    }
}
