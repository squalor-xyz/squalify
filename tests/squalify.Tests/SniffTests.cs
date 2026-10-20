// SPDX-License-Identifier: Apache-2.0
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public class SniffTests
{
    private static readonly CharsetTolerance Tolerance = new(8, 65536);

    private static SniffResult Sniff(byte[] bytes)
    {
        return ByteSniffer.Sniff(new BytesSource("t.bin", bytes), Tolerance);
    }

    private static byte[] Utf16(string text, bool bigEndian, bool bom)
    {
        var encoding = new UnicodeEncoding(bigEndian, bom);
        return TestBytes.Concat(bom ? encoding.GetPreamble() : Array.Empty<byte>(), encoding.GetBytes(text));
    }

    public static TheoryData<string, byte[], string, string?, string?> CharsetRows() => new()
    {
        { "ascii", TestBytes.Ascii("hello\nworld\n"), "text", "ascii", null },
        { "utf-8", Encoding.UTF8.GetBytes("café µA\n"), "text", "utf-8", null },
        { "utf-8 with BOM", TestBytes.Concat(TestBytes.Hex("EF BB BF"), Encoding.UTF8.GetBytes("café\n")), "text", "utf-8", "utf-8" },
        { "utf-8 BOM and ascii", TestBytes.Concat(TestBytes.Hex("EF BB BF"), TestBytes.Ascii("hi\n")), "text", "utf-8", "utf-8" },
        { "utf-16le with BOM", Utf16("hello\n", bigEndian: false, bom: true), "text", "utf-16le", "utf-16le" },
        { "utf-16be with BOM", Utf16("hello\n", bigEndian: true, bom: true), "text", "utf-16be", "utf-16be" },
        { "utf-16le without BOM", Utf16("hello world\n", bigEndian: false, bom: false), "text", "utf-16le", null },
        { "utf-16be without BOM", Utf16("hello world\n", bigEndian: true, bom: false), "text", "utf-16be", null },
        { "utf-32le with BOM", TestBytes.Hex("FF FE 00 00 68 00 00 00 69 00 00 00"), "text", "utf-32le", "utf-32le" },
        { "utf-32be with BOM", TestBytes.Hex("00 00 FE FF 00 00 00 68 00 00 00 69"), "text", "utf-32be", "utf-32be" },
        { "latin-1", TestBytes.Concat(TestBytes.Ascii("caf"), TestBytes.Hex("E9"), TestBytes.Ascii(" "), TestBytes.Hex("93"), TestBytes.Ascii("quoted"), TestBytes.Hex("94 0A")), "text", "latin-1", null },
    };

    [Theory]
    [MemberData(nameof(CharsetRows))]
    public void Sniff_Charset(string label, byte[] bytes, string kind, string? charset, string? bom)
    {
        var result = Sniff(bytes);
        Assert.True(kind == result.Kind, label + ": kind");
        Assert.True(charset == result.Charset, label + ": charset " + result.Charset);
        Assert.True(bom == result.Bom, label + ": bom " + result.Bom);
    }

    [Fact]
    public void Sniff_Charset_Latin1_NotesWindows1252WhenC1BytesAppear()
    {
        var withC1 = Sniff(TestBytes.Concat(TestBytes.Ascii("a "), TestBytes.Hex("93"), TestBytes.Ascii(" b "), TestBytes.Hex("94 95 0A")));
        Assert.Equal("latin-1", withC1.Charset);
        Assert.Contains("possibly windows-1252", ChosenReason(withC1, "charset"));

        var withoutC1 = Sniff(TestBytes.Concat(TestBytes.Ascii("caf"), TestBytes.Hex("E9"), TestBytes.Ascii(" caf"), TestBytes.Hex("E9"), TestBytes.Ascii(" caf"), TestBytes.Hex("E9 0A")));
        Assert.Equal("latin-1", withoutC1.Charset);
        Assert.DoesNotContain("windows-1252", ChosenReason(withoutC1, "charset"));
    }

    private static string ChosenReason(SniffResult result, string topic)
    {
        var entry = Assert.Single(result.Evidence, e => e.Topic == topic);
        Assert.NotNull(entry.Chosen);
        return Assert.Single(entry.Candidates, c => c.Value == entry.Chosen).Reason;
    }

    public static TheoryData<string, byte[]> MagicRows()
    {
        var tar = new byte[262];
        Array.Copy(TestBytes.Ascii("ustar"), 0, tar, 257, 5);
        return new TheoryData<string, byte[]>
        {
            { "parquet", TestBytes.Concat(TestBytes.Ascii("PAR1"), TestBytes.Hex("00 01 02 03 04"), TestBytes.Ascii("PAR1")) },
            { "stdf-le", TestBytes.Hex("02 00 00 0A 04 01") },
            { "stdf-be", TestBytes.Hex("00 02 00 0A 01 04") },
            { "gzip", TestBytes.Hex("1F 8B 08 00 00 00 00 00") },
            { "zip", TestBytes.Hex("50 4B 03 04 14 00 00 00") },
            { "zip", TestBytes.Hex("50 4B 05 06 00 00 00 00") },
            { "xz", TestBytes.Hex("FD 37 7A 58 5A 00 00 04") },
            { "bzip2", TestBytes.Ascii("BZh91AY&SY") },
            { "zstd", TestBytes.Hex("28 B5 2F FD 00 58 00 00") },
            { "7z", TestBytes.Hex("37 7A BC AF 27 1C 00 04") },
            { "tar", tar },
            { "ole2", TestBytes.Hex("D0 CF 11 E0 A1 B1 1A E1 00 00") },
            { "pdf", TestBytes.Ascii("%PDF-1.7\n") },
            { "png", TestBytes.Hex("89 50 4E 47 0D 0A 1A 0A 00 00") },
            { "sqlite", TestBytes.Concat(TestBytes.Ascii("SQLite format 3"), TestBytes.Hex("00 10 00")) },
            { "hdf5", TestBytes.Hex("89 48 44 46 0D 0A 1A 0A 00 00") },
            { "duckdb", TestBytes.Concat(TestBytes.Hex("00 00 00 00 00 00 00 00"), TestBytes.Ascii("DUCK"), TestBytes.Hex("01 00 00 00")) },
            { "arrow", TestBytes.Concat(TestBytes.Ascii("ARROW1"), TestBytes.Hex("00 00")) },
            { "netcdf", TestBytes.Concat(TestBytes.Ascii("CDF"), TestBytes.Hex("01 00 00 00")) },
            { "netcdf", TestBytes.Concat(TestBytes.Ascii("CDF"), TestBytes.Hex("02 00 00 00")) },
        };
    }

    [Theory]
    [MemberData(nameof(MagicRows))]
    public void Sniff_Magic(string name, byte[] bytes)
    {
        var result = Sniff(bytes);
        Assert.Equal(name, result.Magic);
        Assert.Equal("binary", result.Kind);
        Assert.Null(result.Charset);
    }

    [Fact]
    public void Sniff_Magic_HasOneTestRowPerTableEntry()
    {
        var rows = MagicRows().Select(static row => (string)row[0]).ToArray();
        Assert.Equal(MagicTable.Entries.Select(static e => e.Name).ToArray(), rows);
    }

    [Fact]
    public void Sniff_Magic_ParquetNeedsBothEnds()
    {
        Assert.Null(Sniff(TestBytes.Ascii("PAR1 only at the head\n")).Magic);
        Assert.Null(Sniff(TestBytes.Ascii("PAR1")).Magic);
        Assert.Null(Sniff(TestBytes.Ascii("only at the tail PAR1")).Magic);
        Assert.Equal("parquet", Sniff(TestBytes.Ascii("PAR1PAR1")).Magic);
    }

    [Fact]
    public void Sniff_Magic_WrongOffset_NoMatch()
    {
        var duckLate = TestBytes.Concat(new byte[9], TestBytes.Ascii("DUCK"));
        Assert.Null(Sniff(duckLate).Magic);
        var tarEarly = new byte[262];
        Array.Copy(TestBytes.Ascii("ustar"), 0, tarEarly, 256, 5);
        Assert.Null(Sniff(tarEarly).Magic);
    }

    [Fact]
    public void Sniff_Magic_FirstEntryWins()
    {
        // A zip-looking file whose tail is also "PAR1" is still not parquet: it has no head match.
        var zipThenPar = TestBytes.Concat(TestBytes.Hex("50 4B 03 04"), TestBytes.Ascii("PAR1"));
        Assert.Equal("zip", Sniff(zipThenPar).Magic);
    }

    [Fact]
    public void Sniff_EmptyFile_FamilyEmpty()
    {
        var sniff = Sniff(Array.Empty<byte>());
        Assert.Equal("empty", sniff.Kind);
        Assert.Null(sniff.Charset);
        Assert.Equal(0, sniff.Length);

        var descriptor = Profiler.Profile(new BytesSource("none.dat", Array.Empty<byte>()), ProfileOptions.Create(false, Array.Empty<string>()));
        Assert.Equal("empty", descriptor.Format.Family);
        Assert.Equal("empty", descriptor.Encoding.Kind);
        Assert.Null(descriptor.Encoding.Charset);
        Assert.Equal(0, descriptor.File.Size);
        Assert.Equal(TestModels.Sha256Empty, descriptor.File.Sha256);
        Assert.Empty(descriptor.Layout.Regions);
        Assert.Null(descriptor.Binary);
    }

    [Theory]
    [InlineData(65534, false)]
    [InlineData(65535, false)]
    [InlineData(65533, true)]
    [InlineData(65534, true)]
    [InlineData(65535, true)]
    [InlineData(65532, false)]
    public void Sniff_Utf8SplitAcrossChunkBoundary_IsValid(int start, bool fourByte)
    {
        // The multi-byte character starts at `start`, so it straddles byte 65,536, the end of the first 64 KiB buffer.
        var character = fourByte ? TestBytes.Hex("F0 9F 98 80") : TestBytes.Hex("E2 82 AC");
        var bytes = TestBytes.Concat(Enumerable.Repeat((byte)'a', start).ToArray(), character, TestBytes.Ascii("tail\n"));
        var result = ByteSniffer.Sniff(new GeneratedSource("big.txt", bytes.Length, i => bytes[i]), Tolerance);
        Assert.Equal("utf-8", result.Charset);
        Assert.Equal(0, result.Facts.InvalidSequences);
        Assert.Equal(0, result.Facts.InvalidBytes);
        Assert.Equal(bytes.Length, result.Length);
    }

    [Fact]
    public void Sniff_Utf8TruncatedAtEndOfFile_IsOneInvalidSequence()
    {
        var bytes = TestBytes.Concat(Enumerable.Repeat((byte)'a', 70000).ToArray(), TestBytes.Hex("E2 82"));
        var result = ByteSniffer.Sniff(new GeneratedSource("t.txt", bytes.Length, i => bytes[i]), Tolerance);
        Assert.Equal(1, result.Facts.InvalidSequences);
        Assert.Equal(2, result.Facts.InvalidBytes);
        Assert.Equal(new long[] { 70000 }, result.Facts.InvalidOffsets);
    }

    [Fact]
    public void Sniff_StrayInvalidByte_KeepsAscii_WithIssue()
    {
        var bytes = TestBytes.Concat(TestBytes.Ascii("line one\n"), TestBytes.Hex("89"), TestBytes.Ascii("line two\n"), TestBytes.Hex("13"), TestBytes.Ascii("end\n"));
        var sniff = Sniff(bytes);
        Assert.Equal("text", sniff.Kind);
        Assert.Equal("ascii", sniff.Charset);
        Assert.Equal(1, sniff.Facts.ControlBytes);

        var descriptor = Profiler.Profile(new BytesSource("stray.txt", bytes), ProfileOptions.Create(false, Array.Empty<string>()));
        Assert.Equal("text", descriptor.Encoding.Kind);
        Assert.Equal("ascii", descriptor.Encoding.Charset);
        Assert.Equal(1, descriptor.Encoding.InvalidBytes);
        Assert.Equal(1, descriptor.Encoding.ControlBytes);
        var issue = Assert.Single(descriptor.Issues, i => i.Code == "invalidUtf8");
        Assert.Equal(1, issue.Count);
        Assert.Equal(new long[] { 9 }, issue.Offsets);
        Assert.Null(issue.FirstLines);
    }

    [Fact]
    public void Sniff_StrayInvalidByteInUtf8File_KeepsUtf8()
    {
        var bytes = TestBytes.Concat(Encoding.UTF8.GetBytes("café "), TestBytes.Hex("FF"), TestBytes.Ascii(" end\n"));
        var sniff = Sniff(bytes);
        Assert.Equal("utf-8", sniff.Charset);
        Assert.Equal(1, sniff.Facts.InvalidSequences);
    }

    [Fact]
    public void Sniff_ManyInvalidBytes_FallsBackToLatin1()
    {
        var bytes = TestBytes.Concat(TestBytes.Ascii("a"), TestBytes.Hex("89"), TestBytes.Ascii("b"), TestBytes.Hex("8A"), TestBytes.Ascii("c"), TestBytes.Hex("8B"), TestBytes.Ascii("d\n"));
        var sniff = Sniff(bytes);
        Assert.Equal("text", sniff.Kind);
        Assert.Equal("latin-1", sniff.Charset);
        Assert.Equal(3, sniff.Facts.InvalidSequences);
        var reason = ChosenReason(sniff, "charset");
        Assert.Contains("3 invalid UTF-8 sequences", reason);
        Assert.Contains("tolerance", reason);

        var descriptor = Profiler.Profile(new BytesSource("many.txt", bytes), ProfileOptions.Create(false, Array.Empty<string>()));
        Assert.Equal("latin-1", descriptor.Encoding.Charset);
        Assert.Equal(3, descriptor.Encoding.InvalidBytes);
    }

    [Theory]
    [InlineData(100, 1, "ascii")]
    [InlineData(100, 2, "latin-1")]
    [InlineData(65536, 1, "ascii")]
    [InlineData(65537, 2, "ascii")]
    [InlineData(65537, 3, "latin-1")]
    [InlineData(600000, 8, "ascii")]
    [InlineData(600000, 9, "latin-1")]
    [InlineData(10000000, 8, "ascii")]
    [InlineData(10000000, 9, "latin-1")]
    public void Sniff_InvalidSequenceTolerance_AtMostEightAndOnePer64KiB(int length, int invalid, string charset)
    {
        var spacing = length / (invalid + 1);
        var source = new GeneratedSource("t.txt", length, i => i % spacing == spacing - 1 && i / spacing < invalid ? (byte)0x89 : (byte)'a');
        var result = ByteSniffer.Sniff(source, Tolerance);
        Assert.Equal(invalid, result.Facts.InvalidSequences);
        Assert.Equal(charset, result.Charset);
    }

    [Theory]
    [InlineData("1,3,5", 10, "utf-16le")]
    [InlineData("1,3", 10, "binary")]
    [InlineData("0,2,4", 10, "utf-16be")]
    [InlineData("0,2", 10, "binary")]
    [InlineData("0,1,3,5,7,9,11,13,15,17", 20, "utf-16le")]
    [InlineData("0,2,1,3,5,7,9,11,13,15", 20, "binary")]
    [InlineData("1,3,5,7,9,11,13,15,17,18", 20, "utf-16le")]
    public void Sniff_NulParity_Thresholds(string nulPositions, int length, string expected)
    {
        var bytes = Enumerable.Repeat((byte)'a', length).ToArray();
        foreach (var position in nulPositions.Split(','))
        {
            bytes[int.Parse(position, System.Globalization.CultureInfo.InvariantCulture)] = 0;
        }

        var result = Sniff(bytes);
        Assert.Equal(expected == "binary" ? "binary" : "text", result.Kind);
        Assert.Equal(expected == "binary" ? null : expected, result.Charset);
        Assert.Null(result.Bom);
    }

    [Fact]
    public void Sniff_NulInOtherwiseText_IsBinary()
    {
        var bytes = TestBytes.Concat(TestBytes.Ascii("plain text with one NUL"), TestBytes.Hex("00"), TestBytes.Ascii(" inside\n"));
        Assert.Equal("binary", Sniff(bytes).Kind);
    }

    [Theory]
    [InlineData(5, "text")]
    [InlineData(6, "binary")]
    public void Sniff_ControlBytes_AboveFivePercentIsBinary(int controls, string kind)
    {
        var bytes = Enumerable.Repeat((byte)'a', 100).ToArray();
        for (var i = 0; i < controls; i++)
        {
            bytes[i * 10] = 0x01;
        }

        var sniff = Sniff(bytes);
        Assert.Equal(kind, sniff.Kind);
        Assert.Equal(controls, sniff.Facts.ControlBytes);
    }

    [Fact]
    public void Sniff_Facts_HashHistogramAndCounts()
    {
        var bytes = TestBytes.Hex("00 41 7F 80 9F A0 FF 0A 09 0E");
        var facts = Sniff(bytes).Facts;
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), facts.Sha256);
        Assert.Equal(10, facts.Length);
        Assert.Equal(256, facts.Histogram.Length);
        Assert.Equal(10, facts.Histogram.Sum());
        Assert.Equal(1, facts.Histogram[0x80]);
        Assert.Equal(3, facts.ControlBytes);
        Assert.Equal(4, facts.HighBytes);
        Assert.Equal(2, facts.C1Bytes);
        Assert.Equal(1, facts.NulEven);
        Assert.Equal(0, facts.NulOdd);
    }

    [Fact]
    public void Sniff_Evidence_ClassAndCharset()
    {
        var sniff = Sniff(TestBytes.Ascii("hello\n"));
        var topics = sniff.Evidence.Select(static e => e.Stage + "/" + e.Topic).ToArray();
        Assert.Equal(new[] { "encoding/charset", "encoding/class" }, topics);
        Assert.All(sniff.Evidence, static e => Assert.Equal(e.Candidates.Select(static c => c.Value).Order(StringComparer.Ordinal), e.Candidates.Select(static c => c.Value)));
        Assert.Equal("text", sniff.Evidence.Single(static e => e.Topic == "class").Chosen);
        Assert.Equal("ascii", sniff.Evidence.Single(static e => e.Topic == "charset").Chosen);
    }
}
