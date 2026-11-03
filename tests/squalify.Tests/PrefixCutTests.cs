// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// sq-73: the prefix cut contract. A byte-limited profile (and <see cref="Profiler.ProfilePrefix"/>) never cuts inside a
/// character or inside a line terminator of the charset it sniffed, so a consumer's character and line counts stay right.
/// </summary>
public sealed class PrefixCutTests
{
    /// <summary>One charset of the matrix: its strict decoder, its byte order mark (or none), its code unit and the charset the sniff reports.</summary>
    private sealed record Charset(string Name, Encoding Strict, byte[] Bom, int Width, string? Sniffed, Repertoire Repertoire = Repertoire.Wide)
    {
        public override string ToString() => Name;
    }

    /// <summary>The characters a test file uses: all of Unicode, the Latin-1 range (0xE9 bytes, invalid as UTF-8), or ASCII only.</summary>
    private enum Repertoire
    {
        Wide,
        Latin1,
        Ascii,
    }

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];
    private static readonly byte[] Utf32LeBom = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BeBom = [0x00, 0x00, 0xFE, 0xFF];

    private static readonly Charset[] All =
    [
        new("utf-8", new UTF8Encoding(false, true), [], 1, null),
        new("utf-8+bom", new UTF8Encoding(false, true), Utf8Bom, 1, "utf-8"),
        new("utf-16le", new UnicodeEncoding(false, false, true), [], 2, "utf-16le"),
        new("utf-16le+bom", new UnicodeEncoding(false, false, true), Utf16LeBom, 2, "utf-16le"),
        new("utf-16be", new UnicodeEncoding(true, false, true), [], 2, "utf-16be"),
        new("utf-16be+bom", new UnicodeEncoding(true, false, true), Utf16BeBom, 2, "utf-16be"),
        new("utf-32le+bom", new UTF32Encoding(false, false, true), Utf32LeBom, 4, "utf-32le"),
        new("utf-32be+bom", new UTF32Encoding(true, false, true), Utf32BeBom, 4, "utf-32be"),
        new("ascii", Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), [], 1, "ascii", Repertoire.Ascii),
        new("latin-1", Encoding.Latin1, [], 1, "latin-1", Repertoire.Latin1),
    ];

    // The sniff recognises UTF-32 only by its byte order mark: without one the NUL pattern is not UTF-16's, so the file is binary.
    private static readonly Charset[] Utf32WithoutBom =
    [
        new("utf-32le", new UTF32Encoding(false, false, true), [], 4, null),
        new("utf-32be", new UTF32Encoding(true, false, true), [], 4, null),
    ];

    public static TheoryData<string> CharsetNames()
    {
        var data = new TheoryData<string>();
        foreach (var charset in All)
        {
            data.Add(charset.Name);
        }

        return data;
    }

    // The first limit of every sweep: past the byte order mark and the header, so the window holds data.
    private const long From = 120;

    private static Charset Named(string name) => All.Single(c => c.Name == name);

    // The tail of a Wide line: units whose low byte is 0x0A or 0x0D (or, in UTF-32, a low byte of that value) but that are not
    // terminators, because another byte of the unit is not zero: U+010A, U+0D0A, U+0A0D and U+10A0D (a surrogate pair in UTF-16).
    private const string NotTerminators = "\u010A\u0D0A\u0A0D\U00010A0D";

    // A header, then lines. A Wide line holds a 2-byte (e acute), a 3-byte (euro sign) and a 4-byte (grinning face, a surrogate
    // pair in UTF-16) UTF-8 sequence, then the NotTerminators. A Latin1 line holds four 0xE9 and two other high bytes (invalid
    // as UTF-8), and an Ascii line none. Non-ASCII characters are escapes: source files are ASCII.
    private static string Text(Charset charset, IReadOnlyList<string> terminators, int lines = 40)
    {
        var sb = new StringBuilder("id,name,price,tag").Append(terminators[0]);
        for (var i = 1; i <= lines; i++)
        {
            var n = i.ToString(CultureInfo.InvariantCulture);
            var m = (i * 7).ToString(CultureInfo.InvariantCulture);
            sb.Append(n);
            sb.Append(charset.Repertoire switch
            {
                Repertoire.Wide => ",caf\u00E9" + n + ",\u20AC" + m + ",\U0001F600x" + NotTerminators,
                Repertoire.Latin1 => ",caf\u00E9" + n + ",\u00E9" + m + ",\u00C4\u00CAx,\u00E9y,\u00E9z",
                _ => ",cafe" + n + ",EUR" + m + ",x",
            });
            sb.Append(terminators[i % terminators.Count]);
        }

        return sb.ToString();
    }

    private static byte[] Bytes(Charset charset, string text) => charset.Bom.Concat(charset.Strict.GetBytes(text)).ToArray();

    // The lines of a text, each with its terminator; a CR followed by an LF is one terminator.
    private static List<string> Lines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
            {
                lines.Add(text[start..(i + 1)]);
                start = i + 1;
            }
            else if (text[i] == '\r')
            {
                lines.Add(text[start..(i + 2)]);
                i++;
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    // The contract, stated on whole lines and not on bytes: the byte offset after the last line whose terminator unit lies inside
    // the limit (counted in whole code units; a CR that ends the limit takes its LF); with none, after the first line.
    private static long ExpectedCut(Charset charset, List<string> lines, long total, long limit)
    {
        if (total <= limit)
        {
            return total;
        }

        var windowEnd = limit - (limit % charset.Width);
        long end = charset.Bom.Length;
        long cut = -1;
        long first = -1;
        foreach (var line in lines)
        {
            end += charset.Strict.GetByteCount(line);
            if (first < 0)
            {
                first = end;
            }

            var crlf = line.EndsWith("\r\n", StringComparison.Ordinal);
            var terminatorStart = end - (crlf ? charset.Width : 0);
            if (terminatorStart <= windowEnd)
            {
                cut = end;
            }
        }

        return cut >= 0 ? cut : first;
    }

    private static string Decode(Charset charset, ReadOnlySpan<byte> bytes) => charset.Strict.GetString(bytes[charset.Bom.Length..]);

    private static int Characters(string text) => text.EnumerateRunes().Count();

    private static ProfileOptions Limit(long maxBytes) => ProfileOptions.Create(false, Array.Empty<string>(), maxBytes);

    // Cuts at every limit of a range and checks the contract at each.
    private static void AssertEveryCut(Charset charset, string text, long from, long to)
    {
        var lines = Lines(text);
        var bytes = Bytes(charset, text);
        var whole = Decode(charset, bytes);
        Assert.Equal(text, whole);

        for (var limit = from; limit <= to; limit++)
        {
            var result = Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Limit(limit));
            var where = $"{charset.Name}, limit {limit}";

            var expected = ExpectedCut(charset, lines, bytes.Length, limit);
            Assert.True(expected == result.Prefix.Length, $"{where}: cut at {result.Prefix.Length}, expected {expected}");
            Assert.True(result.Prefix.Span.SequenceEqual(bytes.AsSpan(0, (int)expected)), $"{where}: the prefix is the first bytes of the file");

            // Strict decoding throws on a split character; the replacement character must not be there either.
            var prefix = Decode(charset, result.Prefix.Span);
            Assert.DoesNotContain('\uFFFD', prefix);

            // It ends at a terminator, and not between the CR and the LF of a CRLF.
            Assert.True(prefix.EndsWith('\n') || prefix.EndsWith('\r'), $"{where}: the prefix ends at a terminator");
            if (prefix.EndsWith('\r'))
            {
                Assert.True(!whole.AsSpan(prefix.Length).StartsWith("\n"), $"{where}: the cut splits a CRLF");
            }

            // Its lines and characters are those of the same lines in the whole file.
            var prefixLines = Lines(prefix);
            Assert.True(lines.Take(prefixLines.Count).SequenceEqual(prefixLines), $"{where}: the prefix is whole lines of the file");
            Assert.True(whole.StartsWith(prefix, StringComparison.Ordinal), $"{where}: the prefix text is the start of the file text");
            Assert.Equal(Characters(string.Concat(lines.Take(prefixLines.Count))), Characters(prefix));
            Assert.Equal(prefixLines.Count, Lines(string.Concat(lines.Take(prefixLines.Count))).Count);

            if (charset.Sniffed is { } sniffed)
            {
                Assert.Equal("text", result.Descriptor.Encoding.Kind);
                Assert.Equal(sniffed, result.Descriptor.Encoding.Charset);
            }
        }
    }

    private static long LineBytes(Charset charset) => charset.Strict.GetByteCount(Lines(Text(charset, ["\n"]))[1]);

    [Theory]
    [MemberData(nameof(CharsetNames))]
    public void The_cut_never_splits_a_character(string name)
    {
        var charset = Named(name);

        // Every limit across two lines, so each byte of a 2-, 3- and 4-byte sequence (and of a surrogate pair, and of a UTF-16 or
        // UTF-32 unit) is the last byte of the window in turn.
        AssertEveryCut(charset, Text(charset, ["\r\n"]), From, From + (2 * LineBytes(charset)));
        AssertEveryCut(charset, Text(charset, ["\n"]), From, From + (2 * LineBytes(charset)));
    }

    [Theory]
    [MemberData(nameof(CharsetNames))]
    public void CR_only_and_mixed_terminators_cut_at_a_whole_line(string name)
    {
        var charset = Named(name);
        var range = 2 * LineBytes(charset);
        AssertEveryCut(charset, Text(charset, ["\r"]), From, From + range);
        AssertEveryCut(charset, Text(charset, ["\r", "\n", "\r\n"]), From, From + (2 * range));
    }

    [Theory]
    [MemberData(nameof(CharsetNames))]
    public void A_CR_that_ends_the_limit_takes_its_LF(string name)
    {
        var charset = Named(name);
        var bytes = Bytes(charset, Text(charset, ["\r\n"]));
        var width = charset.Width;

        // The limit that ends on the CR of the third line's CRLF (its first unit, or the last byte of it for a wide unit).
        var text = Decode(charset, bytes);
        var third = Lines(text).Take(3).Sum(l => (long)charset.Strict.GetByteCount(l));
        var crEnd = charset.Bom.Length + third - width;
        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Limit(crEnd));
        Assert.Equal(charset.Bom.Length + third, result.Prefix.Length);
        Assert.EndsWith("\r\n", Decode(charset, result.Prefix.Span), StringComparison.Ordinal);

        // A limit one unit short of the CR keeps the second line as the cut.
        var shorter = Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Limit(crEnd - width));
        Assert.Equal(charset.Bom.Length + Lines(text).Take(2).Sum(l => (long)charset.Strict.GetByteCount(l)), shorter.Prefix.Length);

        // A lone CR that ends the limit, with no LF after it, is the cut.
        var lone = Bytes(charset, Text(charset, ["\r"]));
        var loneText = Decode(charset, lone);
        var loneThird = charset.Bom.Length + Lines(loneText).Take(3).Sum(l => (long)charset.Strict.GetByteCount(l));
        var loneResult = Profiler.ProfilePrefix(new BytesSource("t.csv", lone), Limit(loneThird));
        Assert.Equal(loneThird, loneResult.Prefix.Length);
    }

    [Theory]
    [InlineData("utf-32le")]
    [InlineData("utf-32be")]
    public void UTF32_without_a_byte_order_mark_is_not_recognised_and_is_cut_as_binary(string name)
    {
        // Documented sniff behavior (not new in this slice): the contract covers the charset the sniff reports, and the sniff
        // reports UTF-32 only from a byte order mark. Without one the file is binary and is cut at the limit.
        var charset = Utf32WithoutBom.Single(c => c.Name == name);
        var bytes = Bytes(charset, Text(charset, ["\n"]));
        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Limit(103));
        Assert.Equal("binary", result.Descriptor.Encoding.Kind);
        Assert.Equal(103, result.Prefix.Length);
    }

    [Theory]
    [MemberData(nameof(CharsetNames))]
    public void ProfilePrefix_and_Profile_agree(string name)
    {
        var charset = Named(name);
        foreach (var terminators in new[] { new[] { "\r\n" }, ["\r"], ["\r", "\n", "\r\n"] })
        {
            var bytes = Bytes(charset, Text(charset, terminators));
            var lineBytes = LineBytes(charset);

            // The first limits are the ones that straddle a character or a CRLF; a stride keeps the run short.
            for (var limit = From; limit <= From + (2 * lineBytes); limit += 3)
            {
                var profile = Profiler.Profile(new BytesSource("t.csv", bytes), Limit(limit));
                var prefix = Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Limit(limit));
                Assert.Equal(profile, prefix.Descriptor);
                Assert.Equal(profile.Scope!.BytesRead, prefix.Prefix.Length);
                Assert.Equal(
                    Encoding.UTF8.GetString(DescriptorWriter.ToBytes(profile)),
                    Encoding.UTF8.GetString(DescriptorWriter.ToBytes(prefix.Descriptor)));
            }
        }
    }

    private static ProfileOptions Ceiling(long? maxBytes, long? ceiling) => ProfileOptions.Create(false, Array.Empty<string>(), maxBytes, ceiling);

    [Theory]
    [MemberData(nameof(CharsetNames))]
    public void A_first_record_over_the_ceiling_has_its_error_code(string name)
    {
        var charset = Named(name);
        var text = Text(charset, ["\r\n"]);
        var bytes = Bytes(charset, text);
        var firstRecord = charset.Bom.Length + charset.Strict.GetByteCount(Lines(text)[0]);

        // One byte short of the first record, its terminator and any byte order mark included: refused, with the code.
        foreach (var ceiling in new[] { firstRecord - 1L, firstRecord - charset.Width })
        {
            var viaPrefix = Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Ceiling(10_000, ceiling)));
            Assert.Equal(SqualifyErrorCodes.FirstRecordTooLong, viaPrefix.Code);

            var viaProfile = Assert.Throws<SqualifyException>(() => Profiler.Profile(new BytesSource("t.csv", bytes), Ceiling(10_000, ceiling)));
            Assert.Equal(SqualifyErrorCodes.FirstRecordTooLong, viaProfile.Code);
            Assert.Equal(viaPrefix.Message, viaProfile.Message);
        }

        // A record of exactly the ceiling is accepted.
        Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Ceiling(10_000, firstRecord));
    }

    [Fact]
    public void A_first_record_over_the_ceiling_with_a_limit_below_the_file_has_its_error_code()
    {
        var charset = Named("utf-16le+bom");
        var bytes = Bytes(charset, Text(charset, ["\n"]));
        var error = Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Ceiling(200, 20)));
        Assert.Equal("firstRecordTooLong", error.Code);
        Assert.Equal(SqualifyErrorCodes.FirstRecordTooLong, error.Code);
    }

    [Fact]
    public void Another_failure_has_no_error_code()
    {
        var bytes = Bytes(Named("utf-8"), Text(Named("utf-8"), ["\n"]));
        var zero = Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Ceiling(0, null)));
        Assert.Null(zero.Code);

        var noLimit = Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Ceiling(null, null)));
        Assert.Null(noLimit.Code);

        var badCeiling = Assert.Throws<SqualifyException>(() => Profiler.Profile(new BytesSource("t.csv", bytes), Ceiling(100, 0)));
        Assert.Null(badCeiling.Code);

        var badName = Assert.Throws<SqualifyException>(() => Profiler.Profile(new BytesSource("a/b.csv", bytes), Ceiling(100, null)));
        Assert.Null(badName.Code);

        var missing = Assert.Throws<SqualifyException>(() => Profiler.Profile(Path.Combine(Path.GetTempPath(), "squalify-sq73-no-such-file.csv"), Ceiling(100, null)));
        Assert.Null(missing.Code);
    }

    [Fact]
    public void SqualifyException_Code_is_null_unless_set_and_the_code_constructors_keep_the_rest()
    {
        Assert.Null(new SqualifyException("m").Code);
        var inner = new InvalidOperationException("inner");
        var wrapped = new SqualifyException("m", inner);
        Assert.Null(wrapped.Code);
        Assert.Same(inner, wrapped.InnerException);

        var coded = new SqualifyException("m", "someCode");
        Assert.Equal("someCode", coded.Code);
        Assert.Equal("m", coded.Message);
        Assert.Null(coded.InnerException);

        var codedWrapped = new SqualifyException("m", "someCode", inner);
        Assert.Equal("someCode", codedWrapped.Code);
        Assert.Same(inner, codedWrapped.InnerException);

        Assert.Equal("firstRecordTooLong", SqualifyErrorCodes.FirstRecordTooLong);
    }
}
