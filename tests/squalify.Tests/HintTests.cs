// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.RegularExpressions;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class HintTests
{
    private static ProfileOptions Options(bool includeValues = false) => ProfileOptions.Create(includeValues, Array.Empty<string>());

    private static Descriptor Profile(string text, bool includeValues = false, string name = "t.csv")
    {
        return Profiler.Profile(new BytesSource(name, Encoding.UTF8.GetBytes(text)), Options(includeValues));
    }

    private static Descriptor ProfileBytes(byte[] bytes)
    {
        return Profiler.Profile(new BytesSource("t.csv", bytes), Options());
    }

    private static bool DotNet(string regex, string line) => new Regex(regex).IsMatch(line);

    private static bool Ecma(string regex, string line) => new Regex(regex, RegexOptions.ECMAScript).IsMatch(line);

    private static string[] Lines(string text) => text.Split('\n');

    private static void AssertEveryRegexMatchesItsLine(Descriptor d, string text)
    {
        var lines = Lines(text);
        Assert.NotEmpty(d.Hints.StableLines);
        foreach (var stable in d.Hints.StableLines)
        {
            var line = lines[(int)stable.Line];
            Assert.True(DotNet(stable.Regex, line), ".NET: " + stable.Regex + " vs " + line);
            Assert.True(Ecma(stable.Regex, line), "ECMAScript: " + stable.Regex + " vs " + line);
        }
    }

    // 8
    [Fact]
    public void Hints_StableLines_RegexMatchesSourceLine_DotNetAndEcmaScript()
    {
        var text = File.ReadAllText(TestBytes.Fixture("lab/sweep-preamble.csv"));
        var d = Profile(text);
        AssertEveryRegexMatchesItsLine(d, text);
        var regexes = d.Hints.StableLines.Select(static s => s.Regex).ToArray();
        Assert.Contains("^Freq\\(MHz\\),Pout\\(dBm\\),Gain\\(dB\\)$", regexes);
        Assert.Contains("^\\s*Lot\\s*[=:]", regexes);
        Assert.Contains("^\\s*Wafer\\s*[=:]", regexes);
        Assert.Contains("^\\s*Temp\\s*[=:]", regexes);
        Assert.Contains("^\\s*Operator\\s*[=:]", regexes);
        Assert.Contains("^ *\"?Freq\\(MHz\\)\"? *, *\"?Pout\\(dBm\\)\"? *, *\"?Gain\\(dB\\)\"? *", regexes);
        Assert.Equal(1 + 4 + 1, regexes.Length);
    }

    [Fact]
    public void Hints_StableLines_SpecialCharacters_AreEscaped()
    {
        var text = "Freq.a,x+y,(p|q),[r],{s},t*u,v?w,x^y,$z,back\\slash\n1,2,3,4,5,6,7,8,9,10\n2,3,4,5,6,7,8,9,10,11\n";
        var d = Profile(text);
        AssertEveryRegexMatchesItsLine(d, text);
        var full = d.Hints.StableLines.Single(static s => s.Regex.EndsWith('$'));
        Assert.Equal("^Freq\\.a,x\\+y,\\(p\\|q\\),\\[r\\],\\{s\\},t\\*u,v\\?w,x\\^y,\\$z,back\\\\slash$", full.Regex);
    }

    [Fact]
    public void Hints_StableLines_AreOrderedByLineThenRegex()
    {
        var text = "Wafer=2\n# note\nLot=1\nSite,Vth,Idsat\n1,0.5,3\n2,0.6,4\n3,0.7,5\n";
        var d = Profile(text);
        var lines = d.Hints.StableLines;
        for (var i = 1; i < lines.Count; i++)
        {
            var c = lines[i - 1].Line.CompareTo(lines[i].Line);
            Assert.True(c < 0 || (c == 0 && string.CompareOrdinal(lines[i - 1].Regex, lines[i].Regex) < 0));
        }

        Assert.Equal([0L, 2L, 3L, 3L], lines.Select(static s => s.Line).ToArray());
    }

    [Fact]
    public void Hints_StableLines_PaddedKeysAndHeader_Match()
    {
        var text = File.ReadAllText(TestBytes.Fixture("delimited/padded-annotated.csv"));
        var d = Profile(text);
        AssertEveryRegexMatchesItsLine(d, text);
        Assert.Equal(5 + 2, d.Hints.StableLines.Count);
    }

    [Fact]
    public void Hints_StableLines_QuotedHeader_PrefixAllowsQuotes()
    {
        var text = "\"Site\",\"Vth\",\"Idsat\"\n1,0.5,3\n2,0.6,4\n";
        var d = Profile(text);
        Assert.Equal(["Site", "Vth", "Idsat"], d.Hints.HeaderFields!.ToArray());
        var prefix = Assert.Single(d.Hints.StableLines, static s => !s.Regex.EndsWith('$'));
        Assert.True(DotNet(prefix.Regex, Lines(text)[0]));
        Assert.True(Ecma(prefix.Regex, Lines(text)[0]));
        Assert.True(DotNet(prefix.Regex, "Site,Vth,Idsat"));
    }

    [Fact]
    public void Hints_StableLines_UnmatchableLine_IsLeftOut()
    {
        // The key is trimmed of a no-break space, which \s matches under the .NET default but not under .NET's RegexOptions.ECMAScript (ASCII-only \s), so the line is not offered.
        var text = "\u00A0Lot=A12\nWafer=2\nSite,Vth\n1,0.5\n2,0.6\n3,0.7\n";
        var d = Profile(text);
        AssertEveryRegexMatchesItsLine(d, text);
        Assert.DoesNotContain(d.Hints.StableLines, static s => s.Line == 0);
    }

    [Fact]
    public void Hints_StableLines_ValuesTier_CommentAndTextOnlyWithIncludeValues()
    {
        var text = "# CANARYCOMMENT here\nLot=CANARYVALUE\nCANARYTEXT line\nSite,Vth\n1,0.5\n2,0.6\n3,0.7\n";
        var without = Profile(text);
        Assert.DoesNotContain("CANARY", string.Join("|", without.Hints.StableLines.Select(static s => s.Regex)), StringComparison.Ordinal);
        Assert.Equal(
            ["^\\s*Lot\\s*[=:]", "^ *\"?Site\"? *, *\"?Vth\"? *", "^Site,Vth$"],
            without.Hints.StableLines.Select(static l => l.Regex).ToArray());

        var with = Profile(text, includeValues: true);
        AssertEveryRegexMatchesItsLine(with, text);
        var regexes = with.Hints.StableLines.Select(static s => s.Regex).ToArray();
        Assert.Contains("^# CANARYCOMMENT here$", regexes);
        Assert.Contains("^CANARYTEXT line$", regexes);
        Assert.Contains("^\\s*Lot\\s*[=:]", regexes);
        Assert.DoesNotContain(regexes, static r => r.Contains("CANARYVALUE", StringComparison.Ordinal));
        Assert.Equal(5, regexes.Length);
    }

    // 9
    [Fact]
    public void Hints_NonNullFields_NeverNull()
    {
        var d = Profile("a,b,c,d\n1,,x,NA\n2,5,,7\n3,6,z,8\n");
        Assert.Equal([0L], d.Hints.NonNullFields!.ToArray());
        foreach (var index in d.Hints.NonNullFields!)
        {
            Assert.Equal(0, d.Table!.Fields[(int)index].Nulls);
        }

        var all = Profile("a,b\n1,x\n2,y\n");
        Assert.Equal([0L, 1L], all.Hints.NonNullFields!.ToArray());
    }

    [Fact]
    public void Hints_NonNullFields_NullOnlyInSeparatorRows_IsListed()
    {
        // The blank-field row is a separator: it is all null, so it does not count against a column.
        var d = Profile("a,b,c\n1,x,\n,,\n2,y,\n3,z,w\n,,\n4,q,\n");
        Assert.Equal(2, d.Table!.RowClasses!.AllNullRows);
        Assert.Equal([0L, 1L], d.Hints.NonNullFields!.ToArray());
        Assert.Equal(2, d.Table.Fields[0].Nulls);
        Assert.Equal(0, d.Table.Fields[0].NullsExcludingAllNullRows);
    }

    [Fact]
    public void Hints_NonNullFields_NullInADataRow_IsNotListed()
    {
        var d = Profile("a,b,c\n1,x,1\n,,\n2,,3\n3,z,4\n");
        Assert.Equal([0L, 2L], d.Hints.NonNullFields!.ToArray());
    }

    [Fact]
    public void Hints_NonNullFields_EveryRowASeparator_ListsNothing()
    {
        var d = Profile("a,b\n,\n,\n,\n");
        Assert.NotNull(d.Table);
        Assert.Equal(d.Table!.Rows, d.Table.RowClasses!.AllNullRows);
        Assert.NotNull(d.Hints.NonNullFields);
        Assert.Empty(d.Hints.NonNullFields!);
    }

    [Fact]
    public void Hints_StableLines_ReplacementOrControlCharacter_LeavesTheRegexOut()
    {
        byte[] File(params byte[] stray)
        {
            return TestBytes.Concat(
                TestBytes.Ascii("Lot=A12\nLo"),
                stray,
                TestBytes.Ascii("t=5\nWafer=2\nSite,Vth\n1,0.5\n2,0.6\n3,0.7\n"));
        }

        foreach (var stray in new[] { new byte[] { 0xFF }, new byte[] { 0x01 }, new byte[] { 0x85 } })
        {
            var d = ProfileBytes(File(stray));
            var regexes = d.Hints.StableLines.Select(static s => s.Regex).ToArray();
            Assert.Contains("^\\s*Lot\\s*[=:]", regexes);
            Assert.Contains("^\\s*Wafer\\s*[=:]", regexes);
            Assert.DoesNotContain(d.Hints.StableLines, static s => s.Line == 1);
            Assert.Equal(1 + 1 + 2, regexes.Length);
            Assert.DoesNotContain(regexes, static r => r.Any(static c => c < ' ' || c == '\uFFFD' || (c >= '\u007F' && c <= '\u009F')));
        }
    }

    [Fact]
    public void Hints_StableLines_HeaderWithAStrayByte_KeepsHeaderFieldsButNoHeaderRegex()
    {
        var bytes = TestBytes.Concat(TestBytes.Ascii("Lot=A12\nSite,V"), [0xFF], TestBytes.Ascii("th\n1,0.5\n2,0.6\n3,0.7\n"));
        var d = ProfileBytes(bytes);
        Assert.Equal(["Site", "V\uFFFDth"], d.Hints.HeaderFields!.ToArray());
        Assert.Equal(["^\\s*Lot\\s*[=:]"], d.Hints.StableLines.Select(static s => s.Regex).ToArray());
    }

    [Fact]
    public void Hints_StableLines_TabIsNotAControlForThisRule()
    {
        var text = "Lot=A12\nSite\tVth\tIdsat\n1\t0.5\t3\n2\t0.6\t4\n3\t0.7\t5\n";
        var d = Profile(text, name: "t.tsv");
        Assert.Equal("\t", d.Hints.Delimiter);
        Assert.Equal(3, d.Hints.StableLines.Count);
        AssertEveryRegexMatchesItsLine(d, text);
    }

    // 10
    [Fact]
    public void Hints_HeaderPrefix_And_FileNameShape()
    {
        var seven = Profile("c1,c2,c3,c4,c5,c6,c7\n1,2,3,4,5,6,7\n2,3,4,5,6,7,8\n", name: "Run_042.csv");
        Assert.Equal(["c1", "c2", "c3", "c4", "c5"], seven.Hints.HeaderPrefix!.ToArray());
        Assert.Equal(["c1", "c2", "c3", "c4", "c5", "c6", "c7"], seven.Hints.HeaderFields!.ToArray());
        Assert.Equal("Aa{2}_9{3}.a{3}", seven.Hints.FileNameShape);
        var prefix = Assert.Single(seven.Hints.StableLines, static s => !s.Regex.EndsWith('$'));
        Assert.Equal(" *\"?c1\"? *, *\"?c2\"? *, *\"?c3\"? *, *\"?c4\"? *, *\"?c5\"? *", prefix.Regex[1..]);

        var three = Profile("x,y,z\n1,2,3\n2,3,4\n", name: "{a}b.CSV");
        Assert.Equal(["x", "y", "z"], three.Hints.HeaderPrefix!.ToArray());
        Assert.Equal("?a?a.A{3}", three.Hints.FileNameShape);
        Assert.Equal(".csv", three.Hints.Extension);

        var headerless = Profiler.Profile(TestBytes.Fixture("delimited/no-header.tsv"), Options());
        Assert.Null(headerless.Hints.HeaderFields);
        Assert.Null(headerless.Hints.HeaderPrefix);
    }

    [Fact]
    public void Hints_Members_FromTheTable()
    {
        var d = Profile("# c\nSite,Vth(V),Idsat\n1,0.5,3\n2,0.6,4\n");
        var h = d.Hints;
        Assert.Equal(".csv", h.Extension);
        Assert.Null(h.Magic);
        Assert.Equal(new MinMax(3, 3), h.FieldCount);
        Assert.Equal(3, h.DataStartLine);
        Assert.Equal(",", h.Delimiter);
        Assert.Equal("none", h.HeaderConvention);
        Assert.Null(h.Stdf);
    }

    [Fact]
    public void Hints_ExtensionIsAsciiLowerCase()
    {
        var turkish = Profile("a,b\n1,2\n3,4\n", name: "DATA.CSVİ");
        Assert.Equal(".csvİ", turkish.Hints.Extension);
        var plain = Profile("a,b\n1,2\n3,4\n", name: "x.TXT");
        Assert.Equal(".txt", plain.Hints.Extension);
    }

    [Fact]
    public void Hints_NonTabular_HasNoTableHints()
    {
        var d = Profile("hello world\n", name: "a.txt");
        Assert.Empty(d.Hints.StableLines);
        Assert.Null(d.Hints.HeaderFields);
        Assert.Null(d.Hints.NonNullFields);
        Assert.Null(d.Hints.FieldCount);
        Assert.Null(d.Hints.DataStartLine);
        Assert.Null(d.Hints.Delimiter);
        Assert.Null(d.Hints.HeaderConvention);
    }

    [Fact]
    public void Hints_Magic_IsStructured()
    {
        var gz = Profiler.Profile(new BytesSource("a.gz", TestBytes.Hex("1F 8B 08 00 00 00 00 00 02 FF")), Options());
        Assert.Equal(new MagicHint("gzip", 0, "1f8b"), gz.Hints.Magic);
        var tar = new byte[600];
        "ustar"u8.CopyTo(tar.AsSpan(257));
        tar[0] = 1;
        var t = Profiler.Profile(new BytesSource("a.tar", tar), Options());
        Assert.Equal(new MagicHint("tar", 257, "7573746172"), t.Hints.Magic);
        var parquet = Profiler.Profile(new BytesSource("a.parquet", TestBytes.Hex("50 41 52 31 00 00 50 41 52 31")), Options());
        Assert.Equal(new MagicHint("parquet", 0, "50415231"), parquet.Hints.Magic);
        var json = Encoding.UTF8.GetString(DescriptorWriter.ToBytes(gz));
        Assert.Contains("\"magic\": { \"name\": \"gzip\", \"offset\": 0, \"hex\": \"1f8b\" }", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Hints_HeaderConvention_IsSetFromTheLayout()
    {
        var d = Profile("a(V),b(A)\n1,2\n3,4\n");
        Assert.Equal(d.Layout.HeaderConvention!.Dominant, d.Hints.HeaderConvention);
    }

    // 11
    [Fact]
    public void Hints_LongHeader_FullRegexEmitted()
    {
        const int columns = 3000;
        var names = Enumerable.Range(0, columns).Select(static i => "col" + i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var header = string.Join(",", names);
        Assert.Equal(30_000 - 1, header.Length);
        header += "x";
        Assert.Equal(30_000, header.Length);
        var row = string.Join(",", Enumerable.Range(0, columns).Select(static i => (i % 7).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var text = header + "\n" + row + "\n" + row + "\n" + row + "\n";
        var d = Profile(text);
        Assert.True(((DelimitedDialect)d.Format.Dialect!).HasHeader);
        var full = Assert.Single(d.Hints.StableLines, static s => s.Regex.EndsWith('$'));
        Assert.Equal(0, full.Line);
        Assert.Equal(30_000 + 2, full.Regex.Length);
        Assert.StartsWith("^col000000,col000001,", full.Regex, StringComparison.Ordinal);
        var line = Lines(text)[0];
        Assert.True(DotNet(full.Regex, line));
        Assert.True(Ecma(full.Regex, line));
        Assert.Equal(columns, d.Hints.HeaderFields!.Count);
        Assert.Equal(5, d.Hints.HeaderPrefix!.Count);
        AssertEveryRegexMatchesItsLine(d, text);
    }

    [Fact]
    public void Hints_WhitespaceDelimited_PrefixUsesWhitespaceRuns()
    {
        var text = "Lot=A12\nFreq Pout Gain\n" + string.Concat(Enumerable.Range(1, 12).Select(static i => $"{i * 100} {i}.5 {i}.25\n"));
        var d = Profile(text, name: "t.dat");
        Assert.Equal("whitespace", d.Hints.Delimiter);
        AssertEveryRegexMatchesItsLine(d, text);
        var prefix = Assert.Single(d.Hints.StableLines, static s => s.Regex.StartsWith("^\\s*\"?", StringComparison.Ordinal));
        Assert.Equal("^\\s*\"?Freq\"?\\s+\"?Pout\"?\\s+\"?Gain\"?\\s*", prefix.Regex);
    }
}

public sealed class PortableRegexTests
{
    [Fact]
    public void Escape_EscapesExactlyTheMetaCharacters()
    {
        Assert.Equal("\\\\\\.\\^\\$\\|\\?\\*\\+\\(\\)\\[\\]\\{\\}", PortableRegex.Escape("\\.^$|?*+()[]{}"));
        Assert.Equal("a-b/c d#e,f:g=h\téΩ", PortableRegex.Escape("a-b/c d#e,f:g=h\téΩ"));
        Assert.Equal(string.Empty, PortableRegex.Escape(string.Empty));
    }

    [Fact]
    public void Escape_EveryAsciiCharacter_CompilesAndMatchesItselfUnderBothEngines()
    {
        for (var c = 0; c < 128; c++)
        {
            var text = "a" + (char)c + "b";
            var regex = "^" + PortableRegex.Escape(text) + "$";
            Assert.True(new Regex(regex).IsMatch(text), "dotnet " + c);
            Assert.True(new Regex(regex, RegexOptions.ECMAScript).IsMatch(text), "ecma " + c);
            Assert.True(PortableRegex.MatchesBoth(regex, text), "both " + c);
        }
    }

    [Fact]
    public void Line_Key_AndPrefix_HaveTheDocumentedShape()
    {
        Assert.Equal("^a\\.b$", PortableRegex.Line("a.b"));
        Assert.Equal("^\\s*a\\.b\\s*[=:]", PortableRegex.Key("a.b"));
        Assert.Equal("^ *\"?a\"? *; *\"?b\"? *", PortableRegex.Prefix(["a", "b"], ";", quoted: true));
        Assert.Equal("^ *a *\\| *b *", PortableRegex.Prefix(["a", "b"], "|", quoted: false));
        Assert.Equal("^\\s*\"?a\"?\\s+\"?b\"?\\s*", PortableRegex.Prefix(["a", "b"], "whitespace", quoted: true));
    }

    [Fact]
    public void MatchesBoth_RequiresBothEngines()
    {
        Assert.True(PortableRegex.MatchesBoth("^a$", "a"));
        Assert.False(PortableRegex.MatchesBoth("^a$", "b"));
        Assert.False(PortableRegex.MatchesBoth("(", "("));

        // \s matches a no-break space under the .NET default but not under RegexOptions.ECMAScript, where it is ASCII-only.
        Assert.False(PortableRegex.MatchesBoth("^\\s*Lot\\s*[=:]", " Lot=1"));
        Assert.True(PortableRegex.MatchesBoth("^\\s*Lot\\s*[=:]", "  Lot = 1"));
    }
}

public sealed class LineReaderTests
{
    private static Dictionary<long, string> Read(byte[] bytes, params long[] lines)
    {
        var source = new BytesSource("t.txt", bytes);
        var sniff = ByteSniffer.Sniff(source, Limits.Default.CharsetTolerance);
        return LineReader.Read(source, sniff, lines);
    }

    [Fact]
    public void Read_FindsLinesByNumber_AcrossLineEndings()
    {
        var got = Read(TestBytes.Ascii("one\r\ntwo\rthree\nfour"), 1, 2, 3, 4);
        Assert.Equal(["one", "two", "three", "four"], new[] { got[1], got[2], got[3], got[4] });
    }

    [Fact]
    public void Read_OnlyTheWantedLines_AndBlankLines()
    {
        var got = Read(TestBytes.Ascii("a\n\nc\n"), 2, 3);
        Assert.Equal(2, got.Count);
        Assert.Equal(string.Empty, got[2]);
        Assert.Equal("c", got[3]);
        Assert.Empty(Read(TestBytes.Ascii("a\n"), 5));
    }

    [Fact]
    public void Read_SkipsTheBom_AndDecodesTheCharset()
    {
        var bytes = TestBytes.Concat([0xEF, 0xBB, 0xBF], Encoding.UTF8.GetBytes("Éa\nb\n"));
        var got = Read(bytes, 1, 2);
        Assert.Equal("Éa", got[1]);
        Assert.Equal("b", got[2]);
    }
}
