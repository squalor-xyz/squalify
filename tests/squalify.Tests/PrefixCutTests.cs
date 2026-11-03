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

            AssertObservable(result, bytes, where);
        }
    }

    // The contract in terms a consumer can observe: when the descriptor says text, the prefix is whole units of the reported
    // charset (after its byte order mark) and ends right after a terminator unit or at the end of the file, and a CR that ends it is
    // not the first half of a CRLF. It is decoded strictly, except for ascii or utf-8 with tolerated invalid bytes, which are a
    // property of the data and not of the cut.
    private static void AssertObservable(ProfiledPrefix result, byte[] whole, string where)
    {
        var encoding = result.Descriptor.Encoding;
        if (encoding.Kind != "text")
        {
            return;
        }

        var (width, bigEndian) = encoding.Charset switch
        {
            "utf-16le" => (2, false),
            "utf-16be" => (2, true),
            "utf-32le" => (4, false),
            "utf-32be" => (4, true),
            _ => (1, false),
        };
        var bom = encoding.Bom switch
        {
            "utf-8" => 3,
            "utf-16le" or "utf-16be" => 2,
            "utf-32le" or "utf-32be" => 4,
            _ => 0,
        };
        var prefix = result.Prefix.Span;

        // The byte order mark agrees with the charset (a TextDecoder skips it and decodes in the charset), and the prefix starts with it.
        if (encoding.Bom is { } bomName)
        {
            Assert.True(bomName == "utf-8" ? width == 1 : bomName == encoding.Charset, $"{where}: the bom {bomName} does not agree with the charset {encoding.Charset}");
            var mark = bomName switch
            {
                "utf-8" => Utf8Bom,
                "utf-16le" => Utf16LeBom,
                "utf-16be" => Utf16BeBom,
                "utf-32le" => Utf32LeBom,
                _ => Utf32BeBom,
            };
            Assert.True(prefix.StartsWith(mark), $"{where}: the prefix does not start with the {bomName} mark");
        }

        Assert.True((prefix.Length - bom) % width == 0, $"{where}: {prefix.Length} bytes after a {bom}-byte mark are not whole {width}-byte units of {encoding.Charset}");

        var atEnd = prefix.Length == whole.Length;
        if (!atEnd)
        {
            var last = prefix[^width..];
            var low = bigEndian ? last[^1] : last[0];
            var others = last.ToArray().Where((_, i) => i != (bigEndian ? width - 1 : 0)).All(b => b == 0);
            Assert.True(others && (low == 0x0A || low == 0x0D), $"{where}: the prefix of {encoding.Charset} does not end at a terminator");
            if (low == 0x0D)
            {
                var next = whole.AsSpan(prefix.Length, width);
                var nextLow = bigEndian ? next[^1] : next[0];
                var nextOthers = next.ToArray().Where((_, i) => i != (bigEndian ? width - 1 : 0)).All(b => b == 0);
                Assert.False(nextOthers && nextLow == 0x0A, $"{where}: the prefix ends between the CR and the LF of a CRLF");
            }
        }

        if (!(encoding.Charset is "ascii" or "utf-8" && encoding.InvalidBytesTolerated))
        {
            Decoder(encoding.Charset).GetString(prefix[bom..]);
        }
    }

    private static Encoding Decoder(string? charset) => charset switch
    {
        "utf-16le" => new UnicodeEncoding(false, false, true),
        "utf-16be" => new UnicodeEncoding(true, false, true),
        "utf-32le" => new UTF32Encoding(false, false, true),
        "utf-32be" => new UTF32Encoding(true, false, true),
        "utf-8" => new UTF8Encoding(false, true),
        "ascii" => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => Encoding.Latin1,
    };

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

    // A first record longer than the limit is read to its end: the cut then lies past the window and is made in the window's code units.
    [Theory]
    [MemberData(nameof(CharsetNames))]
    public void A_first_record_longer_than_the_limit_reports_a_charset_the_prefix_decodes_in(string name)
    {
        var charset = Named(name);
        foreach (var terminators in new[] { new[] { "\r\n" }, ["\r"], ["\n"] })
        {
            var text = "id," + new string('x', 150) + terminators[0] + Text(charset, terminators);
            var bytes = Bytes(charset, text);
            for (var limit = 40L; limit <= 90; limit += 7)
            {
                var result = Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Limit(limit));
                Assert.True(result.Prefix.Length > limit, $"{charset.Name}, limit {limit}: the first record is read to its end");
                AssertObservable(result, bytes, $"{charset.Name}, limit {limit}");
                if (charset.Width > 1 && charset.Sniffed is { } sniffed)
                {
                    Assert.Equal(sniffed, result.Descriptor.Encoding.Charset);
                }
            }
        }
    }

    [Fact]
    public void An_overshoot_that_reads_as_UTF16_keeps_the_ascii_sniff_of_the_window()
    {
        // 100 bytes of ascii inside the limit, no terminator, then the first line goes on with 150 pairs (x, NUL) and ends in an LF.
        // Alone, the 401 bytes of the first line sniff as utf-16le (rule 4: 150 of 351 bytes are NUL, all at odd offsets). The cut
        // was made one byte per unit, so the window's layout is kept: ascii, text.
        var bytes = new List<byte>();
        bytes.AddRange(Enumerable.Repeat((byte)'a', 100));
        for (var i = 0; i < 150; i++)
        {
            bytes.Add((byte)'x');
            bytes.Add(0);
        }

        bytes.Add((byte)'\n');
        bytes.AddRange(Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("bbbbbbbbbb\n", 20))));
        var data = bytes.ToArray();

        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", data), Limit(100));
        Assert.Equal(401, result.Prefix.Length);
        Assert.Equal("text", result.Descriptor.Encoding.Kind);
        Assert.Equal("ascii", result.Descriptor.Encoding.Charset);
        Assert.Equal(401, result.Scope.BytesRead);
        Assert.DoesNotContain(result.Descriptor.Issues, static i => i.Code is "invalidUtf8" or "invalidUnicode");
        AssertObservable(result, data, "ascii window, utf-16le overshoot");

        var profile = Profiler.Profile(new BytesSource("t.csv", data), Limit(100));
        Assert.Equal(profile, result.Descriptor);

        // The cut prefix on its own is a different file: it sniffs as utf-16le. That is where the sniff of a prefix differs.
        var alone = Profiler.Profile(new BytesSource("t.csv", result.Prefix.ToArray()), ProfileOptions.Create(false, Array.Empty<string>()));
        Assert.Equal("utf-16le", alone.Encoding.Charset);
    }

    [Fact]
    public void An_overshoot_that_reads_as_UTF16BE_keeps_the_UTF16LE_sniff_of_the_window()
    {
        // 100 bytes of utf-16le without a byte order mark (50 units (a, NUL)): rule 4, NUL at odd offsets only. The first line goes
        // on with 600 units (NUL, b) and ends in an LF unit. The 1,302 bytes alone have 450+ NULs at even offsets, so they sniff as utf-16be.
        var bytes = new List<byte>();
        for (var i = 0; i < 50; i++)
        {
            bytes.Add((byte)'a');
            bytes.Add(0);
        }

        for (var i = 0; i < 600; i++)
        {
            bytes.Add(0);
            bytes.Add((byte)'b');
        }

        bytes.AddRange([(byte)'\n', 0]);
        for (var i = 0; i < 20; i++)
        {
            bytes.AddRange(Encoding.Unicode.GetBytes("cccccccccc\n"));
        }

        var data = bytes.ToArray();
        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", data), Limit(100));
        Assert.Equal(1302, result.Prefix.Length);
        Assert.Equal("text", result.Descriptor.Encoding.Kind);
        Assert.Equal("utf-16le", result.Descriptor.Encoding.Charset);
        AssertObservable(result, data, "utf-16le window, utf-16be overshoot");

        var profile = Profiler.Profile(new BytesSource("t.csv", data), Limit(100));
        Assert.Equal(profile, result.Descriptor);

        var alone = Profiler.Profile(new BytesSource("t.csv", result.Prefix.ToArray()), ProfileOptions.Create(false, Array.Empty<string>()));
        Assert.Equal("utf-16be", alone.Encoding.Charset);
    }

    [Fact]
    public void A_cut_inside_the_limit_that_reads_as_UTF16BE_keeps_the_UTF16LE_sniff_of_the_window()
    {
        // The cut falls inside the limit, not past it. 50 units (NUL, A), an LF unit (0A, NUL), then 1,000 units (A, NUL) and more
        // lines. The window of 2,102 bytes has its NULs at odd offsets (1,001 of 1,051): utf-16le, so the cut is after the LF unit,
        // at 102. Those 102 bytes alone have 50 NULs at even offsets and 1 at an odd one: utf-16be.
        var bytes = new List<byte>();
        for (var i = 0; i < 50; i++)
        {
            bytes.Add(0);
            bytes.Add((byte)'A');
        }

        bytes.AddRange([(byte)'\n', 0]);
        for (var i = 0; i < 1000; i++)
        {
            bytes.Add((byte)'A');
            bytes.Add(0);
        }

        for (var i = 0; i < 5; i++)
        {
            bytes.AddRange([(byte)'\n', 0]);
            for (var j = 0; j < 10; j++)
            {
                bytes.AddRange([(byte)'B', 0]);
            }
        }

        var data = bytes.ToArray();
        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", data), Limit(2102));
        Assert.Equal(102, result.Prefix.Length);
        Assert.Equal(102, result.Scope.BytesRead);
        Assert.Equal("text", result.Descriptor.Encoding.Kind);
        Assert.Equal("utf-16le", result.Descriptor.Encoding.Charset);
        AssertObservable(result, data, "utf-16le window, cut inside the limit, utf-16be prefix");

        var profile = Profiler.Profile(new BytesSource("t.csv", data), Limit(2102));
        Assert.Equal(profile, result.Descriptor);

        var alone = Profiler.Profile(new BytesSource("t.csv", result.Prefix.ToArray()), ProfileOptions.Create(false, Array.Empty<string>()));
        Assert.Equal("utf-16be", alone.Encoding.Charset);
    }

    // sq-73 (architect A1): a short window sees no byte order mark, or a shorter one, than the cut prefix. The mark follows the kept charset.
    [Theory]
    [InlineData(1, "latin-1", null)]
    [InlineData(2, "utf-16le", "utf-16le")]
    [InlineData(3, "utf-16le", "utf-16le")]
    public void A_byte_order_mark_follows_the_kept_charset(int limit, string charset, string? bom)
    {
        // c1: UTF-16LE with a mark; a 1-byte window sees a single high byte. c2: UTF-32LE with a mark; a 2- or 3-byte window sees the UTF-16LE mark.
        var utf16 = new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0A, 0x00, 0x42, 0x00, 0x0A, 0x00 };
        var utf32 = new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x42, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00 };
        var data = limit == 1 ? utf16 : utf32;

        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", data), Limit(limit));
        Assert.Equal("text", result.Descriptor.Encoding.Kind);
        Assert.Equal(charset, result.Descriptor.Encoding.Charset);
        Assert.Equal(bom, result.Descriptor.Encoding.Bom);
        AssertObservable(result, data, $"limit {limit}");

        var profile = Profiler.Profile(new BytesSource("t.csv", data), Limit(limit));
        Assert.Equal(profile, result.Descriptor);
    }

    [Fact]
    public void A_one_byte_window_keeps_one_byte_per_unit_and_no_mark_for_a_UTF16_file()
    {
        // The evidence of the kept layout says what the prefix alone would sniff as.
        var data = new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0A, 0x00, 0x42, 0x00, 0x0A, 0x00 };
        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", data), Limit(1));
        Assert.Equal(5, result.Prefix.Length);
        var reasons = result.Descriptor.Evidence.Where(static e => e.Stage == "encoding").SelectMany(static e => e.Candidates).Select(static c => c.Reason);
        Assert.Contains(reasons, static r => r.Contains("would sniff as utf-16le", StringComparison.Ordinal));
    }
}
