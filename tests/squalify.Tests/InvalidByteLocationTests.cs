// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>sq-46: an invalid-byte issue says where the bytes are (offsets, lines and layout regions), and the encoding says when the charset was kept despite them.</summary>
public sealed class InvalidByteLocationTests
{
    private static readonly ProfileOptions Options = ProfileOptions.Create(false, Array.Empty<string>());

    private static byte[] Fixture(string relative)
    {
        return File.ReadAllBytes(TestBytes.Fixture(relative));
    }

    // Replaces the byte at the offset with 0xFF, which is never valid UTF-8.
    private static byte[] Flip(byte[] bytes, params long[] offsets)
    {
        var copy = (byte[])bytes.Clone();
        foreach (var offset in offsets)
        {
            copy[offset] = 0xFF;
        }

        return copy;
    }

    private static Descriptor Profile(byte[] bytes)
    {
        return Profiler.Profile(new BytesSource("t.csv", bytes), Options);
    }

    private static Issue InvalidUtf8(Descriptor descriptor)
    {
        return Assert.Single(descriptor.Issues, static i => i.Code == "invalidUtf8");
    }

    private static string Json(Descriptor descriptor)
    {
        return Encoding.UTF8.GetString(DescriptorWriter.ToBytes(descriptor));
    }

    private static long LineOf(byte[] bytes, long offset)
    {
        // The line of a byte in a file whose line breaks are all LF.
        return 1 + bytes.Take((int)offset).Count(static b => b == (byte)'\n');
    }

    // The sweep-preamble fixture: lines 1-7 are the preamble (bytes 0-98), 8 the header (98-127), 9 the units row (127-138),
    // 10-29 the data (138-435) and 30-31 the footer (435-448).
    [Theory]
    [InlineData(19, "preamble")]
    [InlineData(99, "header")]
    [InlineData(129, "units")]
    [InlineData(336, "data")]
    [InlineData(443, "footer")]
    public void OneStrayByte_IsLocatedInItsLayoutRegion(int offset, string region)
    {
        var bytes = Flip(Fixture("lab/sweep-preamble.csv"), offset);
        var descriptor = Profile(bytes);

        // The layout still has the region the byte is in (a stray byte in a data cell can change how the first rows read).
        Assert.Contains(region, descriptor.Layout.Regions.Select(static r => r.Kind));
        var issue = InvalidUtf8(descriptor);
        Assert.Equal(1, issue.Count);
        Assert.Equal(new long[] { offset }, issue.Offsets);
        Assert.Equal(new[] { LineOf(bytes, offset) }, issue.FirstLines);
        Assert.Equal(new[] { region }, issue.Regions);
    }

    [Fact]
    public void OneStrayByte_InTheAnnotationRows_IsNamedAnnotation()
    {
        // padded-annotated: preamble 0-110, annotation 110-410 (lines 6-9), header 410-485, data from 485.
        var bytes = Flip(Fixture("delimited/padded-annotated.csv"), 200);
        var descriptor = Profile(bytes);
        Assert.Equal(new[] { "preamble", "annotation", "header", "data" }, descriptor.Layout.Regions.Select(static r => r.Kind).ToArray());
        var issue = InvalidUtf8(descriptor);
        Assert.Equal(new long[] { 200 }, issue.Offsets);
        Assert.Equal(new long[] { 7 }, issue.FirstLines);
        Assert.Equal(new[] { "annotation" }, issue.Regions);
    }

    // The tolerance is 1 per 64 KiB, so four bytes need a file of more than 192 KiB.
    private static byte[] BigSweep(out long preambleByte, out long headerByte, out long dataByte, out long footerByte)
    {
        var text = new StringBuilder("# Sweep export\nLot=A12\nWafer: 7\nTemp = 25\nOperator=jr\nFreq(MHz),Pout(dBm),Gain(dB)\nMHz,dBm,dB\n");
        for (var i = 0; i < 13000; i++)
        {
            text.Append(i.ToString("D5", CultureInfo.InvariantCulture)).Append(",10.0,15.0\n");
        }

        text.Append("Rows=13000\nDone\n");
        var bytes = Encoding.ASCII.GetBytes(text.ToString());
        Assert.True(bytes.Length > 3 * 65536);
        var s = text.ToString();
        preambleByte = s.IndexOf("Lot=", StringComparison.Ordinal) + 4;
        headerByte = s.IndexOf("Freq", StringComparison.Ordinal) + 1;
        dataByte = s.IndexOf("04000,", StringComparison.Ordinal) + 2;
        footerByte = s.IndexOf("Done", StringComparison.Ordinal) + 1;
        return bytes;
    }

    [Fact]
    public void SeveralStrayBytes_GiveOneRegionEach_InFileOrder()
    {
        var bytes = BigSweep(out var preamble, out var header, out var data, out var footer);
        var flipped = Flip(bytes, preamble, header, data, footer);
        var descriptor = Profile(flipped);

        var issue = InvalidUtf8(descriptor);
        Assert.Equal(4, issue.Count);
        Assert.Equal(new[] { preamble, header, data, footer }, issue.Offsets);
        Assert.Equal(new[] { 2L, 6L, LineOf(flipped, data), LineOf(flipped, footer) }, issue.FirstLines);
        Assert.Equal(new[] { "preamble", "header", "data", "footer" }, issue.Regions);
        Assert.True(descriptor.Encoding.InvalidBytesTolerated);
    }

    [Fact]
    public void TwoStrayBytes_OnOneLine_GiveTwoLinesEntriesAndTwoRegions()
    {
        // firstLines lines up with offsets: a line repeats when two offsets lie on it.
        var bytes = BigSweep(out var preamble, out _, out _, out _);
        var flipped = Flip(bytes, preamble, preamble + 1);
        var issue = InvalidUtf8(Profile(flipped));
        Assert.Equal(new[] { preamble, preamble + 1 }, issue.Offsets);
        Assert.Equal(new long[] { 2, 2 }, issue.FirstLines);
        Assert.Equal(new[] { "preamble", "preamble" }, issue.Regions);
    }

    [Fact]
    public void StrayBytes_AfterCrlfAndLoneCrLineBreaks_CountEachLineOnce()
    {
        // a CRLF is one line break, a lone CR is another, a lone LF a third: the byte is on line 4.
        var bytes = TestBytes.Concat(TestBytes.Ascii("a\r\nb\rc\nd"), TestBytes.Hex("FF"), TestBytes.Ascii("\n"));
        var issue = InvalidUtf8(Profile(bytes));
        Assert.Equal(new long[] { 8 }, issue.Offsets);
        Assert.Equal(new long[] { 4 }, issue.FirstLines);
    }

    [Fact]
    public void StrayBytes_InASecondBlock_AreNamedByTheBlocksOwnRegion()
    {
        var text = "Site=A\na,b,c\n1,1,1\n2,2,2\n3,3,3\n4,4,4\n5,5,5\n6,6,6\nSite=B\na,b,c\n1,1,1\n2,2,2\n3,3,3\n4,4,4\n5,5,5\n6,6,6\n";
        var bytes = Encoding.ASCII.GetBytes(text);
        var second = text.IndexOf("Site=B", StringComparison.Ordinal);
        var descriptor = Profile(Flip(bytes, second + 5));
        Assert.Contains(descriptor.Layout.Regions, static r => r.Kind == "block");
        var issue = InvalidUtf8(descriptor);
        Assert.Equal(new[] { "preamble" }, issue.Regions);
        Assert.Equal(new long[] { 9 }, issue.FirstLines);

        var inData = Profile(Flip(bytes, text.IndexOf("4,4,4", second, StringComparison.Ordinal)));
        Assert.Equal(new[] { "data" }, InvalidUtf8(inData).Regions);
        var inHeader = Profile(Flip(bytes, second + 7));
        Assert.Equal(new[] { "header" }, InvalidUtf8(inHeader).Regions);
    }

    [Fact]
    public void StrayByte_InAFileThatIsNotATable_IsNamedByTheOneUnknownRegion()
    {
        var bytes = TestBytes.Concat(TestBytes.Ascii("hello there\nthis is prose, with a comma\nand "), TestBytes.Hex("FF"), TestBytes.Ascii(" a stray byte\n"));
        var descriptor = Profile(bytes);
        var issue = InvalidUtf8(descriptor);
        Assert.Equal(new long[] { 44 }, issue.Offsets);
        Assert.Equal(new long[] { 3 }, issue.FirstLines);
        var covering = descriptor.Layout.Regions.Single(static r => r.StartByte <= 44 && 44 < r.EndByte);
        Assert.Equal(new[] { covering.Kind }, issue.Regions);
    }

    [Fact]
    public void Lines_AnOffsetPastTheLastCharacter_IsOnTheLineAfterTheLastCharacter()
    {
        // Not reachable from a real offset list (each offset starts a character), but the lookup still answers.
        var bytes = TestBytes.Ascii("ab\ncd\n");
        var source = new BytesSource("t.txt", bytes);
        var sniff = ByteSniffer.Sniff(source, new CharsetTolerance(8, 65536));
        Assert.Equal(new long[] { 1, 2, 3, 3 }, InvalidByteLocator.Lines(source, sniff, [0, 3, 6, 100]).ToArray());
    }

    // ---- The region lookup, on layouts that leave gaps -----------------------------------------------------------------

    [Fact]
    public void RegionNames_AByteOutsideEveryRegion_IsOther()
    {
        var layout = new LayoutSection(
        [
            new Region("preamble", 1, 2, 0, 10),
            new Region("data", 4, 5, 20, 30),
        ]);
        Assert.Equal(
            new[] { "preamble", "preamble", "other", "other", "data", "data", "other" },
            InvalidByteLocator.RegionNames(layout, [0, 9, 10, 19, 20, 29, 30]).ToArray());
        Assert.Equal(new[] { "other" }, InvalidByteLocator.RegionNames(new LayoutSection(Array.Empty<Region>()), [5]).ToArray());
    }

    [Fact]
    public void RegionNames_InABlock_AreTheChildRegions_AndTheGapsInABlockAreOther()
    {
        var layout = new LayoutSection(
        [
            new Region(
                "block",
                1,
                4,
                0,
                40,
                Index: 0,
                Regions: [new Region("preamble", 1, 1, 0, 10), new Region("header", 2, 2, 12, 20), new Region("data", 3, 4, 20, 40)]),
            new Region("block", 5, 6, 40, 60, Index: 1, Regions: null),
            new Region("footer", 7, 7, 60, 70),
        ]);
        Assert.Equal(
            new[] { "preamble", "other", "header", "data", "other", "footer" },
            InvalidByteLocator.RegionNames(layout, [3, 10, 12, 39, 45, 65]).ToArray());
    }

    // ---- encoding.invalidBytesTolerated --------------------------------------------------------------------------------

    [Fact]
    public void Tolerated_IsTrue_WhenTheCharsetIsKeptDespiteStrayBytes()
    {
        var descriptor = Profile(Flip(Fixture("lab/sweep-preamble.csv"), 336));
        Assert.Equal("ascii", descriptor.Encoding.Charset);
        Assert.True(descriptor.Encoding.InvalidBytesTolerated);
        Assert.Contains("\"invalidBytes\": 1, \"invalidBytesTolerated\": true, \"controlBytes\": 0", Compact(Json(descriptor)));

        // A UTF-8 file with a stray byte keeps utf-8.
        var utf8 = Profile(TestBytes.Concat(Encoding.UTF8.GetBytes("caf\u00E9,b\n1,2\n3,4\n"), TestBytes.Hex("FF"), TestBytes.Ascii(",5\n")));
        Assert.Equal("utf-8", utf8.Encoding.Charset);
        Assert.True(utf8.Encoding.InvalidBytesTolerated);
    }

    [Fact]
    public void Tolerated_IsAbsent_WithNoInvalidBytes_AndWhenTheToleranceIsExceeded()
    {
        var clean = Profile(Fixture("lab/sweep-preamble.csv"));
        Assert.False(clean.Encoding.InvalidBytesTolerated);
        Assert.DoesNotContain("invalidBytesTolerated", Json(clean), StringComparison.Ordinal);
        Assert.DoesNotContain(clean.Issues, static i => i.Code == "invalidUtf8");

        // Two stray bytes in a small file exceed the tolerance of one: the charset becomes latin-1, nothing is kept.
        var exceeded = Profile(Flip(Fixture("lab/sweep-preamble.csv"), 336, 330));
        Assert.Equal("latin-1", exceeded.Encoding.Charset);
        Assert.Equal(2, exceeded.Encoding.InvalidBytes);
        Assert.False(exceeded.Encoding.InvalidBytesTolerated);
        Assert.DoesNotContain("invalidBytesTolerated", Json(exceeded), StringComparison.Ordinal);
        Assert.DoesNotContain(exceeded.Issues, static i => i.Code == "invalidUtf8");
    }

    [Fact]
    public void Tolerated_IsAbsent_ForUtf16WithInvalidUnits()
    {
        // UTF-16 has no tolerance: invalidUnicode reports every unit and the charset was never at risk.
        var bytes = TestBytes.Concat(TestBytes.Hex("FF FE"), new UnicodeEncoding(false, false).GetBytes("ab\n"), TestBytes.Hex("00 D8"));
        var descriptor = Profile(bytes);
        Assert.Equal("utf-16le", descriptor.Encoding.Charset);
        Assert.False(descriptor.Encoding.InvalidBytesTolerated);
        Assert.DoesNotContain("invalidBytesTolerated", Json(descriptor), StringComparison.Ordinal);
    }

    // ---- Files without invalid bytes are unchanged; determinism --------------------------------------------------------

    [Fact]
    public void OtherIssues_KeepTheirShape_AndRegionsAreWrittenOnlyForInvalidBytes()
    {
        var ragged = Profile(Encoding.ASCII.GetBytes("a,b,c\n1,2,3\n4,5\n6,7,8\n9,10,11\n"));
        var issue = Assert.Single(ragged.Issues, static i => i.Code == "raggedRows");
        Assert.NotNull(issue.FirstLines);
        Assert.Null(issue.Offsets);
        Assert.Null(issue.Regions);
        using var doc = JsonDocument.Parse(Json(ragged));
        foreach (var written in doc.RootElement.GetProperty("issues").EnumerateArray())
        {
            Assert.False(written.TryGetProperty("regions", out _));
            Assert.False(written.TryGetProperty("offsets", out _));
        }
    }

    [Fact]
    public void TheSameBytesTwice_GiveTheSameIssue_WhateverTheReadSizes()
    {
        var bytes = Flip(Fixture("lab/sweep-preamble.csv"), 99);
        var first = Profile(bytes);
        var second = Profile(bytes);
        Assert.Equal(InvalidUtf8(first), InvalidUtf8(second));
        Assert.Equal(Json(first), Json(second));

        var small = Profiler.Profile(new OneBytePerReadSource("t.csv", bytes), Options);
        Assert.Equal(InvalidUtf8(first), InvalidUtf8(small));
        Assert.Equal(Json(first), Json(small));
    }

    [Fact]
    public void TheIssue_IsWrittenWithFirstLines_ThenOffsets_ThenRegions()
    {
        var descriptor = Profile(Flip(Fixture("lab/sweep-preamble.csv"), 99));
        using var doc = JsonDocument.Parse(Json(descriptor));
        var issue = doc.RootElement.GetProperty("issues").EnumerateArray().Single(static i => i.GetProperty("code").GetString() == "invalidUtf8");
        Assert.Equal(new[] { "code", "count", "firstLines", "offsets", "regions" }, issue.EnumerateObject().Select(static p => p.Name).ToArray());
        Assert.Equal("header", issue.GetProperty("regions")[0].GetString());
    }

    [Fact]
    public void ProfilePrefix_LocatesTheBytesInThePrefix()
    {
        var bytes = Flip(Fixture("lab/sweep-preamble.csv"), 336);
        var prefix = Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), ProfileOptions.Create(false, Array.Empty<string>(), 400));
        var issue = InvalidUtf8(prefix.Descriptor);
        Assert.Equal(new long[] { 336 }, issue.Offsets);
        Assert.Equal(new[] { "data" }, issue.Regions);
    }

    // ---- The writer ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Writer_RegionsWithoutOffsets_Throws()
    {
        var model = TestModels.Minimal() with { Issues = [new Issue("bad", 1, FirstLines: [1], Regions: ["data"])] };
        var error = Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(model));
        Assert.Contains("bad", error.Message);
    }

    [Fact]
    public void Writer_ListsOfDifferentLengths_Throw()
    {
        var shortRegions = TestModels.Minimal() with { Issues = [new Issue("bad", 2, FirstLines: [1, 2], Offsets: [5, 9], Regions: ["data"])] };
        Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(shortRegions));
        var shortLines = TestModels.Minimal() with { Issues = [new Issue("bad", 2, FirstLines: [1], Offsets: [5, 9], Regions: ["data", "data"])] };
        Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(shortLines));
    }

    [Fact]
    public void Writer_OffsetsAndRegionsWithoutLines_AreAllowed()
    {
        var model = TestModels.Minimal() with { Issues = [new Issue("ok", 1, Offsets: [5], Regions: ["data"])] };
        Assert.Contains("\"offsets\": [5], \"regions\": [\"data\"]", Json(model));
    }

    // Collapses the whitespace of the canonical form so that one object reads on one line.
    private static string Compact(string json)
    {
        return System.Text.RegularExpressions.Regex.Replace(json, @"\s+", " ");
    }

    private sealed class OneBytePerReadSource(string name, byte[] bytes) : IProfileSource
    {
        public string Name { get; } = name;

        public long Length => bytes.Length;

        public Stream Open()
        {
            return new OneByteStream(bytes);
        }
    }

    private sealed class OneByteStream(byte[] bytes) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => bytes.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= bytes.Length || count == 0)
            {
                return 0;
            }

            buffer[offset] = bytes[_position++];
            return 1;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
