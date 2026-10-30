// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>Repeated header and data blocks (sq-13): detection, aggregation, regions, the fingerprint and the caps.</summary>
public sealed class BlockTests
{
    private const string Header = "Freq(MHz),Pout(dBm),Gain(dB)";

    private static ProfileOptions Options(bool includeValues = false, params string[] allow) => ProfileOptions.Create(includeValues, allow);

    private static Descriptor ProfileText(string text, bool includeValues = false, params string[] allow)
    {
        return Profiler.Profile(new BytesSource("t.csv", Encoding.UTF8.GetBytes(text)), Options(includeValues, allow));
    }

    private static Descriptor ProfileFixture(string fixture)
    {
        return Profiler.Profile(TestBytes.Fixture(fixture), Options());
    }

    private static DelimitedDialect Dialect(Descriptor d)
    {
        return Assert.IsType<DelimitedDialect>(d.Format.Dialect);
    }

    private static string Row(int i, double shift = 0)
    {
        return string.Join(
            ",",
            (700 + (100 * i)).ToString(CultureInfo.InvariantCulture),
            (10 + (0.5 * i)).ToString("0.0", CultureInfo.InvariantCulture),
            (15 + (0.25 * i) + shift).ToString("0.00", CultureInfo.InvariantCulture));
    }

    // One block: its own preamble (a site and a temperature), the header and the rows.
    private static string Block(string site, int temp, int rows, string header = Header, double shift = 0, string? unitsRow = null)
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
            builder.Append(Row(i, shift)).Append('\n');
        }

        return builder.ToString();
    }

    private static string Blocks(int count, int rows = 6)
    {
        var builder = new StringBuilder("# export\n");
        for (var b = 0; b < count; b++)
        {
            builder.Append(Block("S" + b.ToString(CultureInfo.InvariantCulture), 25 + (20 * b), rows, shift: 0.2 * b));
        }

        builder.Append("Rows=").Append((count * rows).ToString(CultureInfo.InvariantCulture)).Append('\n');
        return builder.ToString();
    }

    private static string[] Kinds(IEnumerable<Region> regions) => regions.Select(static r => r.Kind).ToArray();

    [Fact]
    public void Blocks_IdenticalHeaders_FieldsAggregated()
    {
        var d = ProfileText(Blocks(3));
        var dialect = Dialect(d);
        Assert.Equal(3, dialect.Blocks);
        Assert.True(dialect.HasHeader);
        Assert.Equal(new[] { "Freq(MHz)", "Pout(dBm)", "Gain(dB)" }, d.Table!.Fields.Select(static f => f.Name!).ToArray());

        // The fields are profiled across all three blocks, and a later block's preamble is not a ragged row.
        Assert.Equal(18, d.Table.Rows);
        Assert.Equal(new MinMax(3, 3), d.Table.FieldCount);
        Assert.All(d.Table.Fields, static f => Assert.Equal(18, f.Count));
        Assert.Equal(18, d.Table.Fields[0].TypeVotes!.Single(static e => e.Key == "int").Value);
        Assert.Equal("int", d.Table.Fields[0].Type);
        Assert.Equal("float", d.Table.Fields[2].Type);
        Assert.Equal(18, d.Table.Fields[2].Distinct!.Value);
        Assert.DoesNotContain(d.Issues, static i => i.Code is "raggedRows" or "heterogeneousBlocks");

        // Row classes are per file: one class, and no transition at a block boundary.
        Assert.Equal(1, d.Table.RowClasses!.Count);
        Assert.Equal(0, d.Table.RowClasses.Transitions);
        Assert.Equal(18, d.Table.RowClasses.Classes[0].Count);

        // The layout: a block region per block (index only), the footer after the last one.
        Assert.Equal(new[] { "block", "block", "block", "footer" }, Kinds(d.Layout.Regions));
        var blocks = d.Layout.Regions.Where(static r => r.Kind == "block").ToArray();
        Assert.Equal(new long?[] { 0, 1, 2 }, blocks.Select(static b => b.Index).ToArray());
        Assert.Equal(new[] { (1L, 10L), (11L, 19L), (20L, 28L) }, blocks.Select(static b => (b.FirstLine, b.LastLine)).ToArray());
        Assert.Equal(new[] { "preamble", "header", "data" }, Kinds(blocks[0].Regions!));
        Assert.Equal(new[] { (1L, 3L), (4L, 4L), (5L, 10L) }, blocks[0].Regions!.Select(static r => (r.FirstLine, r.LastLine)).ToArray());
        Assert.Equal(new[] { "preamble", "header", "data" }, Kinds(blocks[1].Regions!));
        Assert.Equal(new[] { (11L, 12L), (13L, 13L), (14L, 19L) }, blocks[1].Regions!.Select(static r => (r.FirstLine, r.LastLine)).ToArray());
        Assert.Equal(new[] { "Site", "Temp(C)" }, blocks[1].Regions![0].Keys);
        Assert.Equal((29L, 29L), (d.Layout.Regions[3].FirstLine, d.Layout.Regions[3].LastLine));
        Assert.Equal(5, d.Hints!.DataStartLine);

        // The blocks tile the file: each starts where the previous ended.
        Assert.Equal(0, blocks[0].StartByte);
        Assert.Equal(blocks[0].EndByte, blocks[1].StartByte);
        Assert.Equal(blocks[1].EndByte, blocks[2].StartByte);
        Assert.Equal(blocks[2].EndByte, d.Layout.Regions[3].StartByte);
        Assert.Equal(d.File.Size, d.Layout.Regions[3].EndByte);

        // The evidence lists the blocks, then one summary.
        var evidence = Assert.Single(d.Evidence, static e => e is { Stage: "dialect", Topic: "blocks" });
        Assert.Equal("3", evidence.Chosen);
        Assert.Equal(new[] { "0", "1", "2", "summary" }, evidence.Candidates.Select(static c => c.Value).ToArray());

        // A fourth group of only four records (its header and three rows) is too short to be a block: the file still has
        // three, and the short group's records are rows of the third (its two preamble lines are ragged rows, and the repeated header is a text vote in a number column).
        var shortTail = ProfileText(Blocks(3).Replace("Rows=18\n", string.Empty, StringComparison.Ordinal) + Block("S3", 85, 3));
        Assert.Equal(3, Dialect(shortTail).Blocks);
        Assert.Equal(18 + 2 + 4, shortTail.Table!.Rows);
        Assert.Equal(2, Assert.Single(shortTail.Issues, static i => i.Code == "raggedRows").Count);
        Assert.All(shortTail.Table.Fields, static f => Assert.Equal(22, f.Count));
        Assert.Equal(1, shortTail.Table.Fields[0].TypeVotes!.Single(static e => e.Key == "string").Value);
    }

    [Fact]
    public void Blocks_Heterogeneous_FirstProfiled_Issue()
    {
        var text = "# export\n" + Block("A", 25, 6) + Block("B", 45, 7, header: "Vgs(V),Id(A),Gm(S)") + "Rows=13\n";
        var d = ProfileText(text);
        Assert.Equal(2, Dialect(d).Blocks);

        // Only the first block's fields are profiled.
        Assert.Equal(new[] { "Freq(MHz)", "Pout(dBm)", "Gain(dB)" }, d.Table!.Fields.Select(static f => f.Name!).ToArray());
        Assert.Equal(6, d.Table.Rows);
        Assert.All(d.Table.Fields, static f => Assert.Equal(6, f.Count));
        Assert.Equal(6, d.Table.RowClasses!.Classes.Sum(static c => c.Count));

        var issue = Assert.Single(d.Issues, static i => i.Code == "heterogeneousBlocks");
        Assert.Equal(1, issue.Count);
        Assert.Equal(new long[] { 13 }, issue.FirstLines);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "raggedRows");

        // The other block is still in the layout, with its own regions.
        var second = d.Layout.Regions.Single(static r => r is { Kind: "block", Index: 1 });
        Assert.Equal(new[] { "preamble", "header", "data" }, Kinds(second.Regions!));
        Assert.Equal((13L, 13L), (second.Regions![1].FirstLine, second.Regions[1].LastLine));
        var evidence = Assert.Single(d.Evidence, static e => e is { Stage: "dialect", Topic: "blocks" });
        Assert.Equal(new[] { 1.0, 0.0, 0.0 }, evidence.Candidates.Select(static c => c.Score).ToArray());
    }

    [Fact]
    public void Blocks_SingleTable_GoldensUnchanged()
    {
        // Every single-table fixture keeps its flat regions and one block; its golden is byte-identical.
        var root = Path.Combine(Golden.SourceFixturesRoot());
        var fixtures = new[] { "delimited", "lab" }
            .SelectMany(dir => Directory.GetFiles(Path.Combine(root, dir)).Select(path => dir + "/" + Path.GetFileName(path)))
            .Where(static f => !f.EndsWith(".json", StringComparison.Ordinal) && !f.Contains("per-site-blocks", StringComparison.Ordinal))
            .OrderBy(static f => f, StringComparer.Ordinal)
            .ToArray();
        Assert.True(fixtures.Length >= 8);
        foreach (var fixture in fixtures)
        {
            var d = ProfileFixture(fixture);
            Assert.Equal(1, Dialect(d).Blocks);
            Assert.DoesNotContain(d.Layout.Regions, static r => r.Kind == "block");
            Assert.DoesNotContain(d.Evidence, static e => e.Topic == "blocks");
            Assert.DoesNotContain(d.Issues, static i => i.Code == "heterogeneousBlocks");
            Golden.Verify(fixture, d);
        }

        // A table with a ragged line and then a short run that opens with a header-like row stays one table.
        var text = "a,b,c\n1,2,3\n4,5,6\n7,8,9\n10,11,12\n13,14,15\nnote\nx,y,z\n1,2,3\n4,5,6\n7,8,9\n";
        var shortRun = ProfileText(text);
        Assert.Equal(1, Dialect(shortRun).Blocks);
        Assert.DoesNotContain(shortRun.Layout.Regions, static r => r.Kind == "block");
        Assert.DoesNotContain(shortRun.Issues, static i => i.Code == "heterogeneousBlocks");
    }

    [Fact]
    public void Blocks_Fingerprint_BlockCountExcluded()
    {
        var three = ProfileText(Blocks(3)).Fingerprint;
        var five = ProfileText(Blocks(5)).Fingerprint;
        Assert.Equal(three, five);

        // More than one block is part of the layout and structure; one block is not.
        var one = ProfileText(Blocks(1)).Fingerprint;
        Assert.NotEqual(one.Layout, three.Layout);
        Assert.NotEqual(one.Typed, three.Typed);
        Assert.NotEqual(one.Structure, three.Structure);
        Assert.Equal(one.Names, three.Names);

        // The projection says so: `blocks: true`, and no count.
        var layout = ProjectionText(ProfileText(Blocks(3)), typed: false);
        Assert.Contains("\"blocks\":true", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("\"blocks\":3", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("blocks", ProjectionText(ProfileText(Blocks(1)), typed: false), StringComparison.Ordinal);

        // A region list that does not depend on the block count: the first block's regions, then the footer.
        Assert.Contains("\"regions\":[\"preamble\",\"header\",\"data\",\"footer\"]", layout, StringComparison.Ordinal);
        Assert.Contains("\"preambleKeys\":[\"site\",\"temp(c)\"]", layout, StringComparison.Ordinal);
    }

    private static string ProjectionText(Descriptor d, bool typed)
    {
        var input = ProjectionInput.From(d);
        return Encoding.UTF8.GetString(typed ? Projection.Typed(input) : Projection.Layout(input));
    }

    [Fact]
    public void Blocks_PerSiteFixture_Golden()
    {
        var d = ProfileFixture("delimited/per-site-blocks.csv");
        Assert.Equal(3, Dialect(d).Blocks);
        Assert.Equal(18, d.Table!.Rows);
        Golden.Verify("delimited/per-site-blocks.csv", d);
    }

    [Fact]
    public void Blocks_RaggedRowBetweenRuns_DoesNotStartABlock()
    {
        var builder = new StringBuilder(Header + "\n");
        for (var i = 0; i < 8; i++)
        {
            builder.Append(Row(i)).Append('\n');
        }

        builder.Append("bad line\n");
        for (var i = 8; i < 16; i++)
        {
            builder.Append(Row(i)).Append('\n');
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(1, Dialect(d).Blocks);
        Assert.DoesNotContain(d.Layout.Regions, static r => r.Kind == "block");
        Assert.Equal(17, d.Table!.Rows);
        Assert.Equal(16, d.Table.Fields[0].Count);
        var ragged = Assert.Single(d.Issues, static i => i.Code == "raggedRows");
        Assert.Equal(1, ragged.Count);
        Assert.Equal(new long[] { 10 }, ragged.FirstLines);
    }

    [Fact]
    public void Blocks_ShortRun_IsNotABlock()
    {
        // A second header with only three rows under it (a run of four records) does not start a block.
        var d = ProfileText("# export\n" + Block("A", 25, 6) + Block("B", 45, 3));
        Assert.Equal(1, Dialect(d).Blocks);
        Assert.DoesNotContain(d.Layout.Regions, static r => r.Kind == "block");
        Assert.DoesNotContain(d.Issues, static i => i.Code == "heterogeneousBlocks");
    }

    [Fact]
    public void Blocks_RowClasses_BreakAtTheBlockBoundary()
    {
        // The first record of the second block is of another class than the last record of the first. That is not a
        // transition: adjacency does not cross a block boundary.
        var builder = new StringBuilder("# export\nSite=A\n" + Header + "\n");
        for (var i = 0; i < 6; i++)
        {
            builder.Append(Row(i)).Append('\n');
        }

        builder.Append("Site=B\n" + Header + "\n700,,15.00\n");
        for (var i = 1; i < 6; i++)
        {
            builder.Append(Row(i)).Append('\n');
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(2, Dialect(d).Blocks);
        Assert.Equal(12, d.Table!.Rows);
        Assert.Equal(2, d.Table.RowClasses!.Count);
        Assert.Equal(1, d.Table.RowClasses.Transitions);
    }

    [Fact]
    public void Blocks_UnitsRow_IsPerBlock_AndNotTyped()
    {
        const string Units = "MHz,dBm,dB";
        var d = ProfileText("# export\n" + Block("A", 25, 6, unitsRow: Units) + Block("B", 45, 6, unitsRow: Units));
        Assert.Equal(2, Dialect(d).Blocks);
        Assert.True(Dialect(d).UnitsRow);
        Assert.Equal(12, d.Table!.Rows);
        Assert.All(d.Table.Fields, static f => Assert.Equal(12, f.Count));
        Assert.Equal("int", d.Table.Fields[0].Type);
        var blocks = d.Layout.Regions.Where(static r => r.Kind == "block").ToArray();
        Assert.Equal(new[] { "preamble", "header", "units", "data" }, Kinds(blocks[1].Regions!));
    }

    [Fact]
    public void Blocks_PreambleValues_AreValuesTier()
    {
        var text = Blocks(3);
        var withheld = DescriptorWriter.ToBytes(ProfileText(text));
        var json = Encoding.UTF8.GetString(withheld);
        Assert.DoesNotContain("\"S1\"", json, StringComparison.Ordinal);

        // The keys of a later block are always there; its values are only with the opt-in, and then aligned with the keys.
        var opened = ProfileText(text, includeValues: true);
        var block = opened.Layout.Regions.Single(static r => r is { Kind: "block", Index: 1 });
        var preamble = block.Regions![0];
        Assert.Equal(new[] { "Site", "Temp(C)" }, preamble.Keys);
        Assert.Equal(new[] { "S1", "45" }, preamble.Values);

        // An allow-list name opens the values of that key in every block, and only those.
        var some = ProfileText(text, includeValues: false, "site");
        var partial = some.Layout.Regions.Single(static r => r is { Kind: "block", Index: 2 }).Regions![0];
        Assert.Equal(new[] { "S2", null }, partial.Values);
        Assert.DoesNotContain(some.Issues, static i => i.Code == "includeValuesForUnmatched");

        // A name that only a later block has is not unmatched.
        var late = ProfileText(text.Replace("Temp(C)=65", "Extra=65", StringComparison.Ordinal), includeValues: false, "extra");
        Assert.DoesNotContain(late.Issues, static i => i.Code == "includeValuesForUnmatched");
    }

    [Fact]
    public void Blocks_AreCappedAtTheRegionLimit()
    {
        var limit = (int)Limits.Default.Regions;
        var d = ProfileText(Blocks(limit + 5, rows: 5));
        Assert.Equal(limit, Dialect(d).Blocks);
        Assert.Equal(limit, d.Layout.Regions.Count(static r => r.Kind == "block"));

        // Past the cap nothing more is recognised or profiled, and the issue counts the candidates left: no later header
        // becomes a row of the last block.
        Assert.Equal(limit * 5, d.Table!.Fields[0].Count);
        Assert.Equal("int", d.Table.Fields[0].Type);
        var issue = Assert.Single(d.Issues, static i => i.Code == "blocksTruncated");
        Assert.Equal(5, issue.Count);
        Assert.Equal(5, issue.FirstLines!.Count);
        Assert.DoesNotContain(d.Issues, static i => i.Code is "raggedRows" or "heterogeneousBlocks");

        // The evidence lists the first 10 blocks, then the summary that counts them all.
        var evidence = Assert.Single(d.Evidence, static e => e is { Stage: "dialect", Topic: "blocks" });
        Assert.Equal(11, evidence.Candidates.Count);
        Assert.Equal("candidates=" + (limit + 4).ToString(CultureInfo.InvariantCulture) + "; blocks=" + limit.ToString(CultureInfo.InvariantCulture) + "; profiled=" + limit.ToString(CultureInfo.InvariantCulture), evidence.Candidates[^1].Reason);
    }

    [Fact]
    public void Blocks_ErrorRowAfterRaggedRow_DoesNotStartABlock()
    {
        // A short ragged row, then an error row of text where the numbers are, then the table again: one table (the
        // run after the ragged row is long, and its first row is all strings). Same as on main.
        var builder = new StringBuilder("Site,Freq,Pout,Gain\n");
        for (var i = 0; i < 20; i++)
        {
            builder.Append(string.Format(CultureInfo.InvariantCulture, "S{0},{1},{2:0.0},20.1\n", i, 1000 + i, 10.5 + i));
        }

        builder.Append("S99,1100\n").Append("S20,ERR,11.0,20.5\n");
        for (var i = 21; i < 40; i++)
        {
            builder.Append(string.Format(CultureInfo.InvariantCulture, "S{0},{1},{2:0.0},20.1\n", i, 1000 + i, 10.5 + i));
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(1, Dialect(d).Blocks);
        Assert.Equal(new[] { "Site", "Freq", "Pout", "Gain" }, d.Table!.Fields.Select(static f => f.Name!).ToArray());
        Assert.Equal(41, d.Table.Rows);
        Assert.Equal(1, Assert.Single(d.Issues, static i => i.Code == "raggedRows").Count);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "heterogeneousBlocks");
    }

    [Fact]
    public void Blocks_AGapOfOnlyDataLikeRows_IsNoGap()
    {
        // `7,8` has two fields and no name: a short data row. The run after it opens with an all-text row (every typed
        // column fails), but with no comment, key=value or one-field line before it, it is no block.
        var builder = new StringBuilder("a,b,c\n");
        for (var i = 0; i < 20; i++)
        {
            builder.Append(string.Format(CultureInfo.InvariantCulture, "{0},{0},{0}\n", i));
        }

        builder.Append("7,8\nERR,timeout,retry\n");
        for (var i = 21; i < 40; i++)
        {
            builder.Append(string.Format(CultureInfo.InvariantCulture, "{0},{0},{0}\n", i));
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(1, Dialect(d).Blocks);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "heterogeneousBlocks");
    }

    [Fact]
    public void Blocks_AnErrorRowAfterANote_DoesNotStartABlock()
    {
        // The gap has a comment (a real gap), but the row fails only one of three typed columns: not a header.
        var builder = new StringBuilder("Site,Freq,Pout,Gain\n");
        for (var i = 0; i < 20; i++)
        {
            builder.Append(string.Format(CultureInfo.InvariantCulture, "S{0},{1},{2:0.0},20.1\n", i, 1000 + i, 10.5 + i));
        }

        builder.Append("# retry\nS20,ERR,11.0,20.5\n");
        for (var i = 21; i < 40; i++)
        {
            builder.Append(string.Format(CultureInfo.InvariantCulture, "S{0},{1},{2:0.0},20.1\n", i, 1000 + i, 10.5 + i));
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(1, Dialect(d).Blocks);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "heterogeneousBlocks");
    }

    [Fact]
    public void Blocks_ExactRepeat_AfterAKeyValueLineOfTheTablesWidth_IsTheHeader()
    {
        // k = 2: the `Site,B` line has the table's width, so it is part of the run; the repeat of the header one line later
        // is the header of the new block, and `Site,B` belongs to the block's annotation, not to its header.
        var d = ProfileText("f,v\n1,1.5\n2,2.5\n3,3.5\n4,4.5\n5,5.5\n6,6.5\n# block two\nSite,B\nf,v\n1,1.5\n2,2.5\n3,3.5\n4,4.5\n5,5.5\n6,6.5\n");
        Assert.Equal(2, Dialect(d).Blocks);
        Assert.Equal(12, d.Table!.Rows);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "heterogeneousBlocks");
        var second = d.Layout.Regions.Single(static r => r is { Kind: "block", Index: 1 });
        Assert.Equal(new[] { "preamble", "annotation", "header", "data" }, Kinds(second.Regions!));
        Assert.Equal(new[] { (8L, 8L), (9L, 9L), (10L, 10L), (11L, 16L) }, second.Regions!.Select(static r => (r.FirstLine, r.LastLine)).ToArray());
    }

    [Fact]
    public void Blocks_ExactRepeat_AfterAnAnnotationRow_IsTheHeader()
    {
        // A limits row (same width as the table) sits between the preamble and the repeated header.
        var text = "Site=A\na,b,c\n1,1,1\n2,2,2\n3,3,3\n4,4,4\n5,5,5\n6,6,6\nSite=B\nRange,10,20\na,b,c\n1,1,1\n2,2,2\n3,3,3\n4,4,4\n5,5,5\n6,6,6\n";
        var d = ProfileText(text);
        Assert.Equal(2, Dialect(d).Blocks);
        Assert.Equal(12, d.Table!.Rows);
        var second = d.Layout.Regions.Single(static r => r is { Kind: "block", Index: 1 });
        Assert.Equal(new[] { "preamble", "annotation", "header", "data" }, Kinds(second.Regions!));
        var row = Assert.Single(second.Regions![1].Rows!);
        Assert.Equal("Range", row.Label);
    }

    [Fact]
    public void Blocks_ExactRepeat_AllStringTables_AreSplitToo()
    {
        // No column has a type, so no header can fail one; only the exact repeat finds the blocks.
        var builder = new StringBuilder();
        for (var b = 0; b < 3; b++)
        {
            builder.Append("Site=").Append(b.ToString(CultureInfo.InvariantCulture)).Append("\nName,Color,Shape\n");
            for (var i = 0; i < 6; i++)
            {
                builder.Append("n").Append(i.ToString(CultureInfo.InvariantCulture)).Append(",c").Append(i.ToString(CultureInfo.InvariantCulture)).Append(",x\n");
            }
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(3, Dialect(d).Blocks);
        Assert.Equal(18, d.Table!.Rows);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "heterogeneousBlocks");
    }

    [Fact]
    public void Blocks_ExactRepeat_NeedsNoRealGap_ButAnotherHeaderDoes()
    {
        // The same header after a gap of one data-like row is a block (an exact repeat); another header there is not.
        var repeat = ProfileText(Block("A", 25, 6).Replace("Site=A\n", string.Empty, StringComparison.Ordinal) + "7,8\n" + Header + "\n" + Row(0) + "\n" + Row(1) + "\n" + Row(2) + "\n" + Row(3) + "\n" + Row(4) + "\n");
        Assert.Equal(2, Dialect(repeat).Blocks);
        var other = ProfileText(Block("A", 25, 6).Replace("Site=A\n", string.Empty, StringComparison.Ordinal) + "7,8\nVgs(V),Id(A),Gm(S)\n" + Row(0) + "\n" + Row(1) + "\n" + Row(2) + "\n" + Row(3) + "\n" + Row(4) + "\n");
        Assert.Equal(1, Dialect(other).Blocks);
    }

    [Fact]
    public void Blocks_FirstBlockWithoutAHeader_HasNoLaterBlocks()
    {
        // Six rows of numbers, a key line, then a table with a header: the table is the one table, as on main (the numbers
        // above its header are annotation rows), not a headerless block 0 and a block that cannot be compared with it.
        var d = ProfileText("1,1,1\n2,2,2\n3,3,3\n4,4,4\n5,5,5\n6,6,6\nSite=B\na,b,c\n1,1,1\n2,2,2\n3,3,3\n4,4,4\n5,5,5\n6,6,6\n");
        Assert.Equal(1, Dialect(d).Blocks);
        Assert.True(Dialect(d).HasHeader);
        Assert.Equal(new[] { "a", "b", "c" }, d.Table!.Fields.Select(static f => f.Name!).ToArray());
        Assert.Equal(6, d.Table.Rows);
    }

    [Fact]
    public void Blocks_AFirstBlockOfFewerThanFiveRecords_IsNoBlock()
    {
        // Header and three rows, a skippable ragged line and a one-field line, then a longer table with its own header:
        // the first run is too short to be a block, so there is one table (and no heterogeneousBlocks that would hide the
        // longer one).
        var d = ProfileText("a,b,c\n1,2,3\n4,5,6\n7,8,9\nx,y\nSite=B\nd,e,f\n1,1,1\n2,2,2\n3,3,3\n4,4,4\n5,5,5\n6,6,6\n");
        Assert.Equal(1, Dialect(d).Blocks);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "heterogeneousBlocks");
    }

    [Fact]
    public void Blocks_HeaderOnlyFirstLines_AreNoBlockStart()
    {
        // A header, a key line and another table: no block is recognised from a first block with no data row. What the
        // file is, is decided by the run start (sq-34), not by block detection.
        var d = ProfileText("a,b,c\nSite=B\nd,e,f\n1,1,1\n2,2,2\n3,3,3\n4,4,4\n5,5,5\n6,6,6\n");
        Assert.Equal(1, Dialect(d).Blocks);
        Assert.DoesNotContain(d.Issues, static i => i.Code == "heterogeneousBlocks");
    }

    [Fact]
    public void Blocks_Heterogeneous_ProfilesThePrefixOfMatchingBlocks()
    {
        // Blocks are read in one pass, so the fields are profiled across the blocks that have the first block's header,
        // up to the first that has another one; later blocks are listed, even one with the first header again.
        const string Other = "Vgs(V),Id(A),Gm(S)";
        var d = ProfileText("# export\n" + Block("A", 25, 6) + Block("B", 45, 6) + Block("C", 65, 6, header: Other) + Block("D", 85, 6));
        Assert.Equal(4, Dialect(d).Blocks);
        Assert.Equal(12, d.Table!.Rows);
        Assert.All(d.Table.Fields, static f => Assert.Equal(12, f.Count));
        var issue = Assert.Single(d.Issues, static i => i.Code == "heterogeneousBlocks");
        Assert.Equal(2, issue.Count);
        Assert.Equal(new long[] { 22, 31 }, issue.FirstLines);
        var evidence = Assert.Single(d.Evidence, static e => e is { Stage: "dialect", Topic: "blocks" });
        Assert.Equal("candidates=3; blocks=4; profiled=2", evidence.Candidates[^1].Reason);
        Assert.Equal(new[] { 1.0, 1.0, 0.0, 0.0 }, evidence.Candidates.Take(4).Select(static c => c.Score).ToArray());
    }

    [Fact]
    public void Blocks_AfterTheHeaderSample_AreFoundWhileStreaming()
    {
        // The first block is longer than the header sample, so the others are met after the first block was decided.
        // A run after a ragged line that does not open with a header is still no block.
        var rows = (int)Limits.Default.HeaderSample + 200;
        var builder = new StringBuilder("# export\n");
        builder.Append(Block("A", 25, rows)).Append(Block("B", 45, 6)).Append("bad line\n");
        for (var i = 0; i < 8; i++)
        {
            builder.Append(Row(i)).Append('\n');
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(2, Dialect(d).Blocks);
        Assert.Equal(rows + 6 + 1 + 8, d.Table!.Rows);
        Assert.Equal(rows + 6 + 8, d.Table.Fields[0].Count);
        Assert.Equal(1, Assert.Single(d.Issues, static i => i.Code == "raggedRows").Count);
        var second = d.Layout.Regions.Single(static r => r is { Kind: "block", Index: 1 });
        Assert.Equal(new[] { "preamble", "header", "data" }, Kinds(second.Regions!));
        Assert.Equal(rows + 5, second.FirstLine);
        Assert.Equal(d.File.Size, second.EndByte);
    }

    [Fact]
    public void Blocks_RejectedCandidateInTheSample_LeavesTheStreamAlone()
    {
        // A run after a ragged line that is no block, inside the header sample; then a header-like row at the first record
        // after the sample. The rejected candidate must not be looked at again: a row with no ragged line before it is no block.
        var builder = new StringBuilder(Header + "\n");
        for (var i = 0; i < 8; i++)
        {
            builder.Append(Row(i)).Append('\n');
        }

        builder.Append("bad line\n");
        for (var i = 0; i < 990; i++)
        {
            builder.Append(Row(i)).Append('\n');
        }

        builder.Append("x,y,z\n");
        for (var i = 0; i < 20; i++)
        {
            builder.Append(Row(i)).Append('\n');
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(1, Dialect(d).Blocks);
        Assert.Equal(8 + 1 + 990 + 1 + 20, d.Table!.Rows);
        Assert.Equal(1, Assert.Single(d.Issues, static i => i.Code == "raggedRows").Count);
        Assert.Equal(8 + 990 + 1 + 20, d.Table.Fields[0].Count);
    }

    [Theory]
    [InlineData(995)]
    [InlineData(996)]
    [InlineData(997)]
    public void Blocks_StartingAtTheEdgeOfTheHeaderSample_AreFound(int rows)
    {
        // The second block's gap, its header, or its first rows are cut by the 1,000-record header sample.
        var d = ProfileText("# export\n" + Block("A", 25, rows) + Block("B", 45, 6));
        Assert.Equal(2, Dialect(d).Blocks);
        Assert.Equal(rows + 6, d.Table!.Rows);
        Assert.Equal(rows + 6, d.Table.Fields[0].Count);
        Assert.DoesNotContain(d.Issues, static i => i.Code is "raggedRows" or "heterogeneousBlocks");
        Assert.Equal(new[] { "preamble", "header", "data" }, Kinds(d.Layout.Regions.Single(static r => r is { Kind: "block", Index: 1 }).Regions!));
    }

    [Fact]
    public void Blocks_LaterPreamble_KeepsWholeLinesAndBlankLines()
    {
        // A later block's preamble lines are not cut at the 16 characters kept for lines inside the run, and its blank
        // lines are counted in it.
        var text = "# export\n" + Block("A", 25, 6) + "\nNote=a rather long note about the second site\n\n" + Block("B", 45, 6);
        var d = ProfileText(text, includeValues: true);
        var preamble = d.Layout.Regions.Single(static r => r is { Kind: "block", Index: 1 }).Regions![0];
        Assert.Equal(new[] { "Note", "Site", "Temp(C)" }, preamble.Keys);
        Assert.Equal("a rather long note about the second site", preamble.Values![0]);
        Assert.Equal(1, preamble.LineKinds!.Single(static e => e.Key == "blank").Value);
        Assert.Equal(3, preamble.LineKinds!.Single(static e => e.Key == "keyValue").Value);
    }

    [Fact]
    public void Blocks_SeparatedByBlankLinesAndAKeyLine_AreFound_AndTheBlankLinesBelongToTheBlockBefore()
    {
        // Blank lines before a block's first record belong to the region before it; a blank line after that record is its.
        var text = "# export\n" + Block("A", 25, 6) + "\n\n" + Block("B", 45, 6) + "\n" + Block("C", 65, 6);
        var d = ProfileText(text);
        Assert.Equal(3, Dialect(d).Blocks);
        Assert.Equal(18, d.Table!.Rows);
        var blocks = d.Layout.Regions.Where(static r => r.Kind == "block").ToArray();
        Assert.Equal(new[] { (1L, 12L), (13L, 22L), (23L, 31L) }, blocks.Select(static b => (b.FirstLine, b.LastLine)).ToArray());
        Assert.Equal(blocks[0].EndByte, blocks[1].StartByte);
    }

    [Fact]
    public void Blocks_ValuesGuard_ChecksTheChildRegions()
    {
        var model = TestModels.Minimal() with
        {
            Layout = new LayoutSection([new Region("block", 1, 2, 0, 20, Index: 0, Regions: [new Region("preamble", 1, 2, 0, 20, Keys: ["Lot"], Values: ["A12"])])]),
        };
        var error = Assert.Throws<InvalidOperationException>(() => DescriptorWriter.ToBytes(model));
        Assert.Contains("layout.regions[0].regions[0].values", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("A12", error.Message, StringComparison.Ordinal);
        Assert.NotEmpty(DescriptorWriter.ToBytes(model with { Options = ProfileOptions.Create(false, ["Lot"]) }));
    }

    [Fact]
    public void Blocks_Json_BlockRegionHasOnlyIndexLinesBytesAndChildren()
    {
        using var doc = JsonDocument.Parse(DescriptorWriter.ToBytes(ProfileText(Blocks(2))));
        var block = doc.RootElement.GetProperty("layout").GetProperty("regions")[0];
        Assert.Equal(new[] { "kind", "index", "lines", "bytes", "regions" }, block.EnumerateObject().Select(static p => p.Name).ToArray());
        Assert.Equal(0, block.GetProperty("index").GetInt32());
        Assert.Equal("preamble", block.GetProperty("regions")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public void Blocks_SameTwice_AndUnderOtherCultures()
    {
        var baseline = DescriptorWriter.ToBytes(ProfileFixture("delimited/per-site-blocks.csv"));
        Assert.Equal(baseline, DescriptorWriter.ToBytes(ProfileFixture("delimited/per-site-blocks.csv")));
        var saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "tr-TR" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                Assert.Equal(baseline, DescriptorWriter.ToBytes(ProfileFixture("delimited/per-site-blocks.csv")));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
