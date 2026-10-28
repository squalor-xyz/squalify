// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>sq-37: <c>table.fields[].rawName</c>, the header text exactly as in the file (quotes removed per the dialect, nothing else changed).</summary>
public sealed class RawNameTests
{
    private const string Rows = "1,2.5,3.5,4,5\n2,3.5,4.5,5,6\n3,4.5,5.5,6,7\n4,5.5,6.5,7,8\n5,6.5,7.5,8,9\n6,7.5,8.5,9,10\n";

    private static Descriptor ProfileText(string text)
    {
        return Profiler.Profile(new BytesSource("t.csv", Encoding.UTF8.GetBytes(text)), ProfileOptions.Create(false, []));
    }

    private static string?[] Raw(Descriptor d) => d.Table!.Fields.Select(static f => f.RawName).ToArray();

    private static string?[] Names(Descriptor d) => d.Table!.Fields.Select(static f => f.Name).ToArray();

    [Fact]
    public void Profile_SpacesQuotesAndEmbeddedUnit_RawNameIsTheTextUnchanged()
    {
        var d = ProfileText("  Val(V) ,\"Quoted, name\",RowID( id ),\"  padded  \",\"say \"\"hi\"\"\"\n" + Rows);
        Assert.Equal(new string?[] { "  Val(V) ", "Quoted, name", "RowID( id )", "  padded  ", "say \"hi\"" }, Raw(d));
        // The trimmed name and its parts are as before.
        Assert.Equal(new string?[] { "Val(V)", "Quoted, name", "RowID( id )", "padded", "say \"hi\"" }, Names(d));
        Assert.Equal("id", d.Table!.Fields[2].NameParts!.Unit);
    }

    [Fact]
    public void Profile_TwoNamesThatTrimToTheSameName_AreTwoRawNames()
    {
        var d = ProfileText("Val(V),Val(V) ,b,c,d\n" + Rows);
        Assert.Equal("Val(V)", d.Table!.Fields[0].Name);
        Assert.Equal("Val(V)", d.Table.Fields[1].Name);
        Assert.Equal(new string?[] { "Val(V)", "Val(V) ", "b", "c", "d" }, Raw(d));
    }

    [Fact]
    public void Profile_NoHeader_HasNoRawNames()
    {
        var d = ProfileText(Rows);
        Assert.False(((DelimitedDialect)d.Format.Dialect!).HasHeader);
        Assert.All(d.Table!.Fields, static f => Assert.Null(f.RawName));
        using var doc = JsonDocument.Parse(DescriptorWriter.ToBytes(d));
        var field = doc.RootElement.GetProperty("table").GetProperty("fields")[0];
        Assert.Equal(JsonValueKind.Null, field.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, field.GetProperty("rawName").ValueKind);
    }

    [Fact]
    public void Profile_HeaderWithInvalidByte_RawNameHoldsTheDecodedTextWithFffd()
    {
        var bytes = TestBytes.Concat(TestBytes.Ascii("Freq, P"), TestBytes.Hex("FF"), TestBytes.Ascii("out ,c,d,e\n"), TestBytes.Ascii(Rows));
        var d = Profiler.Profile(new BytesSource("bad.csv", bytes), ProfileOptions.Create(false, []));
        var field = d.Table!.Fields[1];
        Assert.Equal("P\uFFFDout", field.Name);
        Assert.Equal(" P\uFFFDout ", field.RawName);
        Assert.NotNull(field.NameHex);
        Assert.Equal("Freq", d.Table.Fields[0].RawName);
    }

    [Fact]
    public void Profile_UnitsRow_RawNameIsTheHeaderLineOnly()
    {
        var d = ProfileText("Freq ,Pout,Idx,Gain(dB), x\nMHz,dBm,,dB,\n700,10.5,1,15.5,1\n800,11.5,2,16.5,2\n900,12.5,3,17.5,3\n1000,13.5,4,18.5,4\n1100,14.5,5,19.5,5\n1200,15.5,6,20.5,6\n");
        Assert.True(((DelimitedDialect)d.Format.Dialect!).UnitsRow);
        Assert.Equal(new string?[] { "Freq ", "Pout", "Idx", "Gain(dB)", " x" }, Raw(d));
    }

    [Fact]
    public void Profile_BlankAndMultiLineHeaderCells_KeepTheirText()
    {
        var d = ProfileText("Freq,\"Two\nlines\",, d,e\n" + Rows);
        Assert.Equal(new string?[] { "Freq", "Two\nlines", "", " d", "e" }, Raw(d));
    }

    [Fact]
    public void Profile_BomOnTheFirstHeaderCell_IsNotPartOfRawName()
    {
        var bytes = TestBytes.Concat(TestBytes.Hex("EFBBBF"), TestBytes.Ascii(" a ,b,c,d,e\n" + Rows));
        var d = Profiler.Profile(new BytesSource("bom.csv", bytes), ProfileOptions.Create(false, []));
        Assert.Equal("utf-8", d.Encoding.Bom);
        Assert.Equal(new string?[] { " a ", "b", "c", "d", "e" }, Raw(d));
    }

    [Fact]
    public void Profile_CrlfInsideAQuotedHeaderCell_IsWrittenAsLf()
    {
        var d = ProfileText("a,\"Two\r\nlines\",c,d,e\r\n" + Rows.Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Equal(new string?[] { "a", "Two\nlines", "c", "d", "e" }, Raw(d));
    }

    [Fact]
    public void Profile_SpacesAfterTheClosingQuote_AreNotPartOfRawName()
    {
        var d = ProfileText("a,\" b \"  ,c,d,e\n" + Rows);
        Assert.Equal(new string?[] { "a", " b ", "c", "d", "e" }, Raw(d));
    }

    [Fact]
    public void Profile_HeaderAfterAPreamble_RawNameIsTheHeaderLine()
    {
        var d = ProfileText("Site=A\nOperator=x\n x ,\"y \",z,w,v\n" + Rows);
        Assert.Equal(new string?[] { " x ", "y ", "z", "w", "v" }, Raw(d));
        Assert.Equal(new string?[] { "x", "y", "z", "w", "v" }, Names(d));
    }

    [Fact]
    public void Profile_PaddedHeaderInALaterBlock_RawNameIsTheFirstBlocksHeader()
    {
        static string Block(string site, string header) => "Site=" + site + "\n" + header + "\n" + Rows;
        var d = ProfileText(Block("A", "a,b,c,d,e") + Block("B", " a ,b ,c,d,e"));
        Assert.Equal(2, ((DelimitedDialect)d.Format.Dialect!).Blocks);
        Assert.Equal(new string?[] { "a", "b", "c", "d", "e" }, Raw(d));
    }

    [Fact]
    public void Descriptor_RawNameIsWrittenForEveryFieldAfterName()
    {
        var d = ProfileText("a,b ,c,d,e\n" + Rows);
        var text = Encoding.UTF8.GetString(DescriptorWriter.ToBytes(d));
        Assert.Contains("\"name\": \"b\",\n", text, StringComparison.Ordinal);
        Assert.Contains("\"rawName\": \"b \",\n", text, StringComparison.Ordinal);
        // Written even when equal to the name, so a consumer reads one member for every field.
        Assert.Contains("\"rawName\": \"a\",\n", text, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(DescriptorWriter.ToBytes(d));
        foreach (var field in doc.RootElement.GetProperty("table").GetProperty("fields").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.String, field.GetProperty("rawName").ValueKind);
            var keys = field.EnumerateObject().Select(static p => p.Name).ToList();
            Assert.Equal(keys.IndexOf("name") + 1, keys.IndexOf("rawName"));
        }
    }

    [Fact]
    public void Fingerprint_NamesProjection_IsUnchangedByRawNames()
    {
        // The names projection uses the trimmed, case-folded name: a header with padding gives the same hash as one without.
        var padded = ProfileText("  Val(V) ,b ,c,d,e\n" + Rows);
        var plain = ProfileText("Val(V),b,c,d,e\n" + Rows);
        Assert.Equal(plain.Fingerprint.Names, padded.Fingerprint.Names);
    }
}
