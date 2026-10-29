// SPDX-License-Identifier: Apache-2.0
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class SyntheticCsvSourceTests
{
    [Fact]
    public void SyntheticCsvSource_PaddedWide_ConstantLineLength()
    {
        const int Rows = 300;
        const int Columns = 40;
        const int CellWidth = 24;
        var source = SyntheticCsvSource.PaddedWide(Rows, Columns, CellWidth);
        var bytes = ReadAll(source);
        Assert.Equal(source.Length, bytes.Length);

        // The preamble lines are key lines of their own length; every line of the table (annotation rows, header and data)
        // has the same length, and every cell of a data row is its value, right-padded with spaces to the cell width.
        var text = Encoding.ASCII.GetString(bytes);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        var lines = text[..^1].Split('\n');
        Assert.True(source.PreambleLineCount > 0);
        var table = lines.Skip(source.PreambleLineCount).ToArray();
        var expected = (Columns * CellWidth) + (Columns - 1);
        Assert.Equal(source.AnnotationLineCount + 1 + Rows, table.Length);
        Assert.All(table, line => Assert.Equal(expected, line.Length));
        foreach (var line in table.Skip(source.AnnotationLineCount + 1))
        {
            foreach (var cell in line.Split(','))
            {
                Assert.Equal(CellWidth, cell.Length);
                Assert.True(cell.Length > cell.TrimEnd().Length, "a cell is not right-padded");
                Assert.NotEmpty(cell.Trim());
                Assert.Equal(cell.TrimStart(), cell);
            }
        }

        // The profiler sees one wide padded table with a constant line length.
        var d = Profiler.Profile(source, ProfileOptions.Create(false, []));
        var dialect = Assert.IsType<DelimitedDialect>(d.Format.Dialect);
        Assert.Equal("delimited", d.Format.Family);
        Assert.True(dialect.Padding.ConstantLineLength);
        Assert.Equal(expected, dialect.Padding.LineLength);
        Assert.True(dialect.Padding.Trim);
        Assert.True(dialect.HasHeader);
        Assert.Equal(Rows, d.Table!.Rows);
        Assert.Equal(Columns, d.Table.Fields.Count);
        Assert.Contains(d.Layout.Regions, static r => r.Kind == "preamble");
        Assert.Contains(d.Layout.Regions, static r => r.Kind == "annotation");
    }

    [Fact]
    public void SyntheticCsvSource_PaddedWide_IsDeterministic()
    {
        var a = ReadAll(SyntheticCsvSource.PaddedWide(50, 20, 16));
        var b = ReadAll(SyntheticCsvSource.PaddedWide(50, 20, 16));
        var other = ReadAll(SyntheticCsvSource.PaddedWide(50, 20, 16, seed: 7));
        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(1)]
    public void SyntheticCsvSource_PaddedWide_CellNarrowerThanTheLabels_IsRejected(int cellWidth)
    {
        // "Upper_limit" and "Lower_limit" are 11 characters: a narrower cell would make the head lines longer than the data rows.
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticCsvSource.PaddedWide(5, 4, cellWidth));
    }

    [Fact]
    public void SyntheticCsvSource_PaddedWide_NarrowestCell_KeepsEveryTableLineTheSameLength()
    {
        var source = SyntheticCsvSource.PaddedWide(5, 9, 11);
        var lines = Encoding.ASCII.GetString(ReadAll(source))[..^1].Split('\n').Skip(source.PreambleLineCount).ToArray();
        Assert.Single(lines.Select(static l => l.Length).Distinct());
    }

    private static byte[] ReadAll(IProfileSource source)
    {
        using var stream = source.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
