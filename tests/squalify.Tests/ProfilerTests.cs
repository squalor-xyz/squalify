// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class ProfilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "squalify-profiler-" + Guid.NewGuid().ToString("N"));

    public ProfilerTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private static ProfileOptions Options() => ProfileOptions.Create(false, Array.Empty<string>());

    private static Descriptor Profile(string name, byte[] bytes) => Profiler.Profile(new BytesSource(name, bytes), Options());

    [Fact]
    public void Profile_SameBytesDifferentDirectory_IdenticalOutput()
    {
        var bytes = File.ReadAllBytes(TestBytes.Fixture("text/plain.txt"));
        var dirA = Directory.CreateDirectory(Path.Combine(_root, "one", "deep")).FullName;
        var dirB = Directory.CreateDirectory(Path.Combine(_root, "two")).FullName;
        File.WriteAllBytes(Path.Combine(dirA, "plain.txt"), bytes);
        File.WriteAllBytes(Path.Combine(dirB, "plain.txt"), bytes);

        var a = DescriptorWriter.ToBytes(Profiler.Profile(Path.Combine(dirA, "plain.txt"), Options()));
        var b = DescriptorWriter.ToBytes(Profiler.Profile(Path.Combine(dirB, "plain.txt"), Options()));
        Assert.Equal(a, b);
        Assert.DoesNotContain(_root, Encoding.UTF8.GetString(a), StringComparison.Ordinal);
        Assert.DoesNotContain("deep", Encoding.UTF8.GetString(a), StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_DifferentName_OnlyFileNameDiffers()
    {
        var bytes = TestBytes.Ascii("one\ntwo\n");
        var a = TestBytes.ProfileText(new BytesSource("alpha.txt", bytes)).Split('\n');
        var b = TestBytes.ProfileText(new BytesSource("bravo.txt", bytes)).Split('\n');
        Assert.Equal(a.Length, b.Length);
        var differing = a.Zip(b).Where(static pair => pair.First != pair.Second).ToArray();
        var pair = Assert.Single(differing);
        Assert.Equal("    \"name\": \"alpha.txt\",", pair.First);
        Assert.Equal("    \"name\": \"bravo.txt\",", pair.Second);
    }

    [Fact]
    public void Profile_DifferentExtension_ChangesOnlyNameDerivedMembers()
    {
        var bytes = TestBytes.Ascii("one\ntwo\n");
        var a = TestBytes.ProfileText(new BytesSource("alpha.txt", bytes)).Split('\n');
        var b = TestBytes.ProfileText(new BytesSource("Bravo99.LOG", bytes)).Split('\n');
        Assert.Equal(a.Length, b.Length);
        var differing = a.Zip(b).Where(static pair => pair.First != pair.Second).Select(static pair => pair.First.TrimStart().Split(':')[0]).ToArray();
        Assert.Equal(new[] { "\"name\"", "\"extension\"", "\"fileNameShape\"" }, differing);
    }

    [Fact]
    public void Profile_Directory_Throws()
    {
        var error = Assert.Throws<SqualifyException>(() => Profiler.Profile(_root, Options()));
        Assert.Contains("directory", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<SqualifyException>(() => new FileSource(_root));
    }

    [Theory]
    [InlineData("dir/file.txt")]
    [InlineData("dir\\file.txt")]
    [InlineData("/abs/file.txt")]
    [InlineData("C:\\data\\file.txt")]
    [InlineData("trailing/")]
    [InlineData("")]
    public void Profile_SourceNameThatIsNotABareFileName_Throws(string name)
    {
        var error = Assert.Throws<SqualifyException>(() => Profile(name, TestBytes.Ascii("x\n")));
        Assert.Contains("file name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Profile_SourceNameThatIsABareFileName_IsAccepted()
    {
        Assert.Equal("my file (1).txt", Profile("my file (1).txt", TestBytes.Ascii("x\n")).File.Name);
    }

    [Fact]
    public void Profile_MissingFile_Throws()
    {
        var error = Assert.Throws<SqualifyException>(() => Profiler.Profile(Path.Combine(_root, "nope.csv"), Options()));
        Assert.Contains("nope.csv", error.Message);
    }

    [Theory]
    [InlineData("text/plain.txt")]
    [InlineData("binary/random.bin")]
    [InlineData("binary/sample.gz")]
    public void Profile_Golden(string fixture)
    {
        Golden.Verify(fixture, Profiler.Profile(TestBytes.Fixture(fixture), Options()));
    }

    [Theory]
    [InlineData("text/plain.txt")]
    [InlineData("binary/random.bin")]
    [InlineData("binary/sample.gz")]
    public void Profile_Fixture_SameTwiceAndUnderOtherCultures(string fixture)
    {
        var path = TestBytes.Fixture(fixture);
        var baseline = DescriptorWriter.ToBytes(Profiler.Profile(path, Options()));
        Assert.Equal(baseline, DescriptorWriter.ToBytes(Profiler.Profile(path, Options())));

        var saved = CultureInfo.CurrentCulture;
        var savedUi = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var name in new[] { "de-DE", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(baseline, DescriptorWriter.ToBytes(Profiler.Profile(path, Options())));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }

    [Fact]
    public void Profile_Text_CountsLinesEndingsAndBlankLines()
    {
        var descriptor = Profile("a.txt", TestBytes.Ascii("a\r\nbb\r\n\r\nccc"));
        Assert.Equal("text", descriptor.Format.Family);
        Assert.Equal(0.1, descriptor.Format.Score);
        Assert.Null(descriptor.Format.Dialect);
        Assert.Equal(new LineEndings(3, 0, 0), descriptor.Encoding.LineEndings);
        Assert.Equal(3, descriptor.Layout.MaxLineLength);
        var region = Assert.Single(descriptor.Layout.Regions);
        Assert.Equal("unknown", region.Kind);
        Assert.Equal((1L, 4L), (region.FirstLine, region.LastLine));
        Assert.Equal((0L, 12L), (region.StartByte, region.EndByte));
        Assert.Equal(new[] { "blank", "text" }, region.LineKinds!.Select(static e => e.Key).ToArray());
        Assert.Equal(new long[] { 1, 3 }, region.LineKinds!.Select(static e => e.Value).ToArray());
        Assert.Empty(descriptor.Issues);
        Assert.Null(descriptor.Table);
        Assert.Null(descriptor.Binary);
    }

    [Fact]
    public void Profile_Text_WhitespaceOnlyLineIsBlank_AndNoTrailingNewlineStillCountsTheLastLine()
    {
        var descriptor = Profile("a.txt", TestBytes.Ascii("x\n \t \nlast"));
        var region = Assert.Single(descriptor.Layout.Regions);
        Assert.Equal(3L, region.LastLine);
        Assert.Equal(new long[] { 1, 2 }, region.LineKinds!.Select(static e => e.Value).ToArray());
        Assert.Equal(new LineEndings(0, 2, 0), descriptor.Encoding.LineEndings);
        Assert.Equal(4, descriptor.Layout.MaxLineLength);
    }

    [Fact]
    public void Profile_Text_TrailingNewlineDoesNotAddALine()
    {
        var region = Assert.Single(Profile("a.txt", TestBytes.Ascii("a\nb\n")).Layout.Regions);
        Assert.Equal(2L, region.LastLine);
    }

    [Fact]
    public void Profile_Text_MixedLineEndings_ReportsTheMinorityLines()
    {
        var descriptor = Profile("a.txt", TestBytes.Ascii("a\r\nb\nc\rd\n"));
        Assert.Equal(new LineEndings(1, 2, 1), descriptor.Encoding.LineEndings);
        var issue = Assert.Single(descriptor.Issues);
        Assert.Equal("mixedLineEndings", issue.Code);
        Assert.Equal(2, issue.Count);
        Assert.Equal(new long[] { 1, 3 }, issue.FirstLines);
        Assert.Null(issue.Offsets);
    }

    [Fact]
    public void Profile_Text_MixedLineEndings_TieGoesToCrlfThenLf()
    {
        // One CRLF and one LF: CRLF is the dominant kind, so the LF line (line 2) is the minority.
        var issue = Assert.Single(Profile("a.txt", TestBytes.Ascii("a\r\nb\n")).Issues);
        Assert.Equal(1, issue.Count);
        Assert.Equal(new long[] { 2 }, issue.FirstLines);

        // One LF and one CR: LF is dominant, so the CR line (line 2) is the minority.
        var lfCr = Assert.Single(Profile("a.txt", TestBytes.Ascii("a\nb\r")).Issues);
        Assert.Equal(1, lfCr.Count);
        Assert.Equal(new long[] { 2 }, lfCr.FirstLines);
    }

    [Fact]
    public void Profile_Text_MixedLineEndings_TwoMinorityKindsAreMergedInLineOrderAndCapped()
    {
        // 30 LF lines, then alternating CRLF and CR lines: 12 of each minority kind, interleaved from line 31.
        var text = string.Concat(Enumerable.Repeat("x\n", 30)) + string.Concat(Enumerable.Range(0, 24).Select(static i => i % 2 == 0 ? "y\r\n" : "z\r"));
        var issue = Assert.Single(Profile("a.txt", TestBytes.Ascii(text)).Issues);
        Assert.Equal(24, issue.Count);
        Assert.Equal(Enumerable.Range(31, 10).Select(static i => (long)i).ToArray(), issue.FirstLines);
    }

    [Fact]
    public void Profile_Evidence_SortEvidence_OrdersByStageThenTopic()
    {
        var shuffled = new[]
        {
            new Evidence("format", "family", null, Array.Empty<Candidate>()),
            new Evidence("zzz-later", "a", null, Array.Empty<Candidate>()),
            new Evidence("encoding", "class", null, Array.Empty<Candidate>()),
            new Evidence("encoding", "charset", null, Array.Empty<Candidate>()),
        };
        Assert.Equal(
            new[] { "encoding/charset", "encoding/class", "format/family", "zzz-later/a" },
            Profiler.SortEvidence(shuffled).Select(static e => e.Stage + "/" + e.Topic).ToArray());
    }

    [Fact]
    public void Profile_Text_LoneCarriageReturnAtEndOfFile_IsCounted()
    {
        var descriptor = Profile("a.txt", TestBytes.Ascii("a\rb\r"));
        Assert.Equal(new LineEndings(0, 0, 2), descriptor.Encoding.LineEndings);
        Assert.Equal(2L, Assert.Single(descriptor.Layout.Regions).LastLine);
        Assert.Empty(descriptor.Issues);
    }

    [Fact]
    public void Profile_Text_MixedLineEndings_ListsOnlyTheFirstTenLines()
    {
        var text = string.Concat(Enumerable.Range(0, 30).Select(static i => i % 2 == 0 ? "x\r\n" : "y\n")) + "z\n";
        var issue = Assert.Single(Profile("a.txt", TestBytes.Ascii(text)).Issues);
        Assert.Equal(10, issue.FirstLines!.Count);
        Assert.Equal(issue.FirstLines.Order().ToArray(), issue.FirstLines.ToArray());
    }

    [Fact]
    public void Profile_Text_Utf16WithBom_DecodesLines()
    {
        var bytes = TestBytes.Concat(TestBytes.Hex("FF FE"), new UnicodeEncoding(false, false).GetBytes("ab\r\ncde\r\n"));
        var descriptor = Profile("u.txt", bytes);
        Assert.Equal("utf-16le", descriptor.Encoding.Charset);
        Assert.Equal("utf-16le", descriptor.Encoding.Bom);
        Assert.Equal(new LineEndings(2, 0, 0), descriptor.Encoding.LineEndings);
        Assert.Equal(3, descriptor.Layout.MaxLineLength);
        var bomRegion = Assert.Single(descriptor.Layout.Regions);
        Assert.Equal(2L, bomRegion.LastLine);
        Assert.Equal((0L, descriptor.File.Size), (bomRegion.StartByte, bomRegion.EndByte));
        Assert.Equal(0, descriptor.Encoding.InvalidBytes);
        Assert.Empty(descriptor.Issues);
    }

    [Fact]
    public void Profile_Text_BomOnly_HasNoLines()
    {
        var descriptor = Profile("b.txt", TestBytes.Hex("EF BB BF"));
        Assert.Equal("text", descriptor.Encoding.Kind);
        Assert.Equal("utf-8", descriptor.Encoding.Bom);
        Assert.Empty(descriptor.Layout.Regions);
        Assert.Equal(0, descriptor.Layout.MaxLineLength);
    }

    [Fact]
    public void Profile_Text_Latin1_HasNoInvalidUtf8Issue_ButCountsInvalidBytes()
    {
        var descriptor = Profile("l.txt", TestBytes.Concat(TestBytes.Ascii("a"), TestBytes.Hex("E9"), TestBytes.Ascii("b"), TestBytes.Hex("E8"), TestBytes.Ascii("c\n")));
        Assert.Equal("latin-1", descriptor.Encoding.Charset);
        Assert.Equal(2, descriptor.Encoding.InvalidBytes);
        Assert.Empty(descriptor.Issues);
    }

    [Fact]
    public void Profile_Text_ControlByteInText_IsCountedAndStaysText()
    {
        var bytes = TestBytes.Concat(Enumerable.Repeat(TestBytes.Ascii("0123456789"), 30).SelectMany(static b => b).ToArray(), TestBytes.Hex("01"));
        var descriptor = Profile("c.txt", bytes);
        Assert.Equal("text", descriptor.Encoding.Kind);
        Assert.Equal(1, descriptor.Encoding.ControlBytes);
        Assert.Empty(descriptor.Issues);
    }

    [Fact]
    public void Profile_Binary_WritesByteStatistics_AndNoBlocksYet()
    {
        var bytes = TestBytes.Concat(TestBytes.Hex("00 00 01 41 42 7F 80 FF"), TestBytes.Ascii("hi"));
        var descriptor = Profile("x.bin", bytes);
        Assert.Equal("binary", descriptor.Format.Family);
        Assert.Equal("binary", descriptor.Encoding.Kind);
        Assert.Null(descriptor.Encoding.Charset);
        var binary = descriptor.Binary!;
        Assert.Equal(256, binary.Bytes.Histogram.Count);
        Assert.Equal(10, binary.Bytes.Histogram.Sum());
        Assert.Equal(2, binary.Bytes.Nul);
        Assert.Equal(4, binary.Bytes.PrintableAscii);
        Assert.Equal(2, binary.Bytes.High);
        Assert.InRange(binary.Bytes.Entropy, 2.0, 3.5);
        Assert.Null(binary.Blocks);
        Assert.Null(binary.Segments);
        Assert.Equal(new LineEndings(0, 0, 0), descriptor.Encoding.LineEndings);
        Assert.Empty(descriptor.Layout.Regions);
        Assert.Null(descriptor.Layout.MaxLineLength);
    }

    [Fact]
    public void Profile_Binary_NamedMagicReachesFormatScoreAndHints()
    {
        var gz = Profile("a.GZ", TestBytes.Hex("1F 8B 08 00 00 00 00 00 02 FF"));
        Assert.Equal(0.2, gz.Format.Score);
        Assert.Equal(new MagicHint("gzip", 0, "1f8b"), gz.Hints.Magic);
        Assert.Equal(".gz", gz.Hints.Extension);

        var plain = Profile("a", TestBytes.Hex("01 02 03 04 05 06 07 08"));
        Assert.Equal(0.1, plain.Format.Score);
        Assert.Null(plain.Hints.Magic);
        Assert.Null(plain.Hints.Extension);
    }

    [Fact]
    public void Profile_Descriptor_HasTheBasicSections()
    {
        var descriptor = Profile("plain.txt", TestBytes.Ascii("hello\n"));
        Assert.Equal(ToolInfo.Version, descriptor.Tool.Version);
        Assert.Equal("plain.txt", descriptor.File.Name);
        Assert.Equal(6, descriptor.File.Size);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(TestBytes.Ascii("hello\n"))), descriptor.File.Sha256);
        Assert.Equal(Limits.Default, descriptor.Limits);
        Assert.Equal("text", descriptor.Kind.Label);
        Assert.Equal("a{5}.a{3}", descriptor.Hints.FileNameShape);
        Assert.Equal(".txt", descriptor.Hints.Extension);
        Assert.Equal(1, descriptor.Fingerprint.Version);
    }

    [Fact]
    public void Profile_Options_AreCarriedIntoTheDescriptor()
    {
        var descriptor = Profiler.Profile(new BytesSource("a.txt", TestBytes.Ascii("x\n")), ProfileOptions.Create(true, ["b", "a"]));
        Assert.True(descriptor.Options.IncludeValues);
        Assert.Equal(new[] { "a", "b" }, descriptor.Options.IncludeValuesFor.ToArray());
    }

    [Fact]
    public void Profile_Evidence_IsOrderedByStageThenTopic_AndListsTheFormatScores()
    {
        var descriptor = Profile("a.txt", TestBytes.Ascii("x\n"));
        Assert.Equal(new[] { "encoding/charset", "encoding/class", "format/family" }, descriptor.Evidence.Select(static e => e.Stage + "/" + e.Topic).ToArray());
        var family = descriptor.Evidence.Single(static e => e.Topic == "family");
        Assert.Equal(new[] { "delimited", "text", "binary" }, family.Candidates.Select(static c => c.Value).ToArray());
    }

    [Fact]
    public void Profile_Issues_AreSortedByCode()
    {
        // A stray invalid byte (invalidUtf8) and mixed line endings (mixedLineEndings), in that code order.
        var descriptor = Profile("a.txt", TestBytes.Concat(TestBytes.Ascii("a\r\nb\nc\n"), TestBytes.Hex("89"), TestBytes.Ascii("d\n")));
        Assert.Equal(new[] { "invalidUtf8", "mixedLineEndings" }, descriptor.Issues.Select(static i => i.Code).ToArray());
    }

    [Fact]
    public void Profile_ThrowsSqualifyException_WhenTheSourceFails()
    {
        var error = Assert.Throws<SqualifyException>(() => Profiler.Profile(new FailingSource(), Options()));
        Assert.Contains("boom", error.Message);
    }

    private sealed class FailingSource : IProfileSource
    {
        public string Name => "f.txt";

        public long Length => 1;

        public Stream Open() => throw new IOException("boom");
    }

    [Fact]
    public void Profile_Descriptor_WritesCanonically()
    {
        var text = TestBytes.ProfileText(new BytesSource("plain.txt", TestBytes.Ascii("hello\n")));
        Assert.EndsWith("}\n", text);
        Assert.DoesNotContain('\r', text);
    }
}
