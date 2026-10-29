// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>sq-40: <see cref="Profiler.ProfilePrefix"/> returns the descriptor and the exact prefix bytes it describes, from one read.</summary>
public sealed class ProfilePrefixTests
{
    /// <summary>One input of the matrix: the bytes, the limit, the code unit of the charset and how far a read may overshoot.</summary>
    private sealed record Case(byte[] Bytes, long Max, int Width, bool BigEndian, bool Binary, int Slack);

    // "id,value,label" then rows "<id>,<id*3>,item<id>", with the given end of line.
    private static string CsvText(int rows, string eol)
    {
        var sb = new StringBuilder("id,value,label").Append(eol);
        for (var i = 1; i <= rows; i++)
        {
            sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',').Append((i * 3).ToString(CultureInfo.InvariantCulture)).Append(",item")
                .Append(i.ToString(CultureInfo.InvariantCulture)).Append(eol);
        }

        return sb.ToString();
    }

    private static byte[] Encoded(Encoding encoding, string text) => encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

    // The limit that puts a CR of a CRLF line end as the last byte of the window.
    private static long CrEndsWindow(byte[] bytes)
    {
        var from = 300;
        var index = Array.IndexOf(bytes, (byte)'\r', from);
        return index + 1;
    }

    private static readonly Dictionary<string, Case> Cases = BuildCases();

    private static Dictionary<string, Case> BuildCases()
    {
        var crlf = Encoding.ASCII.GetBytes(CsvText(500, "\r\n"));
        var binary = Enumerable.Range(0, 5000).Select(static i => (byte)((i * 37) % 256)).ToArray();
        var longFirst = Encoding.ASCII.GetBytes("id," + new string('x', 20000) + "\n" + string.Concat(Enumerable.Range(0, 20).Select(static i => i.ToString(CultureInfo.InvariantCulture) + ",5\n")));
        return new Dictionary<string, Case>
        {
            ["lf"] = new(Encoding.ASCII.GetBytes(CsvText(500, "\n")), 1000, 1, false, false, 4),
            ["crlf"] = new(crlf, 1000, 1, false, false, 4),
            ["crlf-cr-ends-window"] = new(crlf, CrEndsWindow(crlf), 1, false, false, 4),
            ["cr"] = new(Encoding.ASCII.GetBytes(CsvText(500, "\r")), 1000, 1, false, false, 4),
            ["utf16le"] = new(Encoded(Encoding.Unicode, CsvText(300, "\n")), 1001, 2, false, false, 4),
            ["utf16be"] = new(Encoded(Encoding.BigEndianUnicode, CsvText(300, "\r\n")), 1001, 2, true, false, 4),
            ["utf32le"] = new(Encoded(Encoding.UTF32, CsvText(300, "\n")), 1003, 4, false, false, 4),
            ["binary"] = new(binary, 1000, 1, false, true, 4),
            ["long-first-line"] = new(longFirst, 100, 1, false, false, 4096),
            ["no-terminator"] = new(Encoding.ASCII.GetBytes(new string('a', 3000)), 100, 1, false, false, 4096),
            ["smaller-than-limit"] = new(Encoding.ASCII.GetBytes(CsvText(10, "\n")), 1_000_000, 1, false, false, 0),
            ["equal-to-limit"] = new(Encoding.ASCII.GetBytes(CsvText(10, "\n")), Encoding.ASCII.GetBytes(CsvText(10, "\n")).Length, 1, false, false, 0),
        };
    }

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Cases.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    private static ProfileOptions Limit(long? maxBytes, long? ceiling = null) =>
        ProfileOptions.Create(false, Array.Empty<string>(), maxBytes, ceiling);

    private static string Json(Descriptor descriptor) => Encoding.UTF8.GetString(DescriptorWriter.ToBytes(descriptor));

    // An independent statement of the cut rule, written from the contract and not from ScopeCut.
    private static long ReferenceCut(Case c)
    {
        var b = c.Bytes;
        var w = c.Width;
        if (b.Length <= c.Max)
        {
            return b.Length;
        }

        if (c.Binary)
        {
            return c.Max;
        }

        char Kind(long at)
        {
            var low = c.BigEndian ? b[at + w - 1] : b[at];
            if (low != 0x0A && low != 0x0D)
            {
                return '\0';
            }

            for (var i = 0; i < w; i++)
            {
                var isLow = c.BigEndian ? i == w - 1 : i == 0;
                if (!isLow && b[at + i] != 0)
                {
                    return '\0';
                }
            }

            return low == 0x0A ? '\n' : '\r';
        }

        long Take(long end, char kind) => kind == '\r' && end + w <= b.Length && Kind(end) == '\n' ? end + w : end;

        var windowEnd = c.Max - (c.Max % w);
        long last = -1;
        char lastKind = '\0';
        for (long i = 0; i + w <= windowEnd; i += w)
        {
            var k = Kind(i);
            if (k != '\0')
            {
                last = i + w;
                lastKind = k;
            }
        }

        if (last >= 0)
        {
            return last == windowEnd ? Take(last, lastKind) : last;
        }

        for (var i = windowEnd; i + w <= b.Length; i += w)
        {
            var k = Kind(i);
            if (k != '\0')
            {
                return Take(i + w, k);
            }
        }

        return b.Length;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Prefix_IsTheReferenceCut_AndTheDescriptorDescribesExactlyThoseBytes(string name)
    {
        var c = Cases[name];
        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", c.Bytes), Limit(c.Max));

        var expected = ReferenceCut(c);
        Assert.Equal(expected, result.Prefix.Length);
        Assert.Equal(expected, result.Scope.BytesRead);
        Assert.True(result.Prefix.Span.SequenceEqual(c.Bytes.AsSpan(0, (int)expected)), "the prefix is the first bytes of the source");
        Assert.Equal(c.Binary, result.Descriptor.Encoding.Kind == "binary");

        // Profile the returned bytes alone, with no limit: the same descriptor, apart from the options, file and scope.
        var alone = Profiler.Profile(new BytesSource("t.csv", result.Prefix.ToArray()), Limit(null));
        var a = result.Descriptor with { Options = alone.Options, File = alone.File, Scope = null };
        Assert.Equal(Json(alone), Json(a));
        Assert.Equal(c.Bytes.Length, result.Descriptor.File.Size);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void SameCutAndSameDescriptorAsProfile(string name)
    {
        var c = Cases[name];
        var profile = Profiler.Profile(new BytesSource("t.csv", c.Bytes), Limit(c.Max));
        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", c.Bytes), Limit(c.Max));

        Assert.Equal(profile.Scope, result.Scope);
        Assert.Equal(profile.Scope, result.Descriptor.Scope);
        Assert.Equal(profile.Scope!.BytesRead, result.Prefix.Length);
        Assert.Equal(Json(profile), Json(result.Descriptor));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ReadOnce_OneOpen_AndNoMoreThanTheWindowPlusTheOvershoot(string name)
    {
        var c = Cases[name];
        var source = new CountingSource("t.csv", c.Bytes);
        var result = Profiler.ProfilePrefix(source, Limit(c.Max));

        Assert.Equal(1, source.Opens);
        Assert.True(source.BytesRead >= result.Prefix.Length);
        var bound = Math.Max(Math.Min(c.Bytes.Length, c.Max), result.Prefix.Length) + c.Slack;
        Assert.True(source.BytesRead <= bound, $"read {source.BytesRead} bytes, bound {bound}");
        Assert.True(source.BytesRead <= c.Bytes.Length);
    }

    [Fact]
    public void ReadOnce_WithTheCeiling_OneOpen()
    {
        var c = Cases["lf"];
        var source = new CountingSource("t.csv", c.Bytes);
        var result = Profiler.ProfilePrefix(source, Limit(c.Max, 100));
        Assert.Equal(1, source.Opens);
        Assert.Equal(ReferenceCut(c), result.Prefix.Length);
    }

    [Fact]
    public void FileFacts_PrefixOnly_WholeSizeAndNoSha256()
    {
        var c = Cases["lf"];
        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", c.Bytes), Limit(c.Max));
        Assert.False(result.Scope.Complete);
        Assert.Equal(c.Bytes.Length, result.Descriptor.File.Size);
        Assert.Null(result.Descriptor.File.Sha256);
        Assert.True(result.Prefix.Length < c.Bytes.Length);
    }

    [Fact]
    public void FileFacts_EndReached_CompleteAndSha256AsForAnUnlimitedRun()
    {
        var bytes = Cases["smaller-than-limit"].Bytes;
        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Limit(1_000_000));
        Assert.True(result.Scope.Complete);
        Assert.True(result.Prefix.Span.SequenceEqual(bytes));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), result.Descriptor.File.Sha256);
        var unlimited = Profiler.Profile(new BytesSource("t.csv", bytes), Limit(null));
        Assert.Equal(unlimited.File, result.Descriptor.File);
    }

    [Fact]
    public void FileFacts_NoTerminator_ReadsToTheEnd_Complete()
    {
        var bytes = Cases["no-terminator"].Bytes;
        var result = Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Limit(100));
        Assert.True(result.Scope.Complete);
        Assert.Equal(bytes.Length, result.Prefix.Length);
        Assert.NotNull(result.Descriptor.File.Sha256);
    }

    [Fact]
    public void Ceiling_Exceeded_RaisesTheSameException_AndTheBufferNeverHoldsMoreThanNPlusOne()
    {
        var bytes = Encoding.ASCII.GetBytes("id," + new string('x', 1_000_000) + "\n1,2\n");
        var viaProfile = Assert.Throws<SqualifyException>(() => Profiler.Profile(new BytesSource("t.csv", bytes), Limit(5_000_000, 200)));

        var source = new CountingSource("t.csv", bytes);
        var viaPrefix = Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(source, Limit(5_000_000, 200)));
        Assert.Equal(viaProfile.Message, viaPrefix.Message);
        Assert.Contains("--max-first-record-bytes", viaPrefix.Message, StringComparison.Ordinal);
        Assert.Equal(1, source.Opens);
        Assert.True(source.BytesRead <= 201, "read " + source.BytesRead);
    }

    [Fact]
    public void Ceiling_CrAtTheCeiling_ReadsOneUnitMore_AndNoMore()
    {
        var bytes = Encoding.ASCII.GetBytes("id,value,label\r\n" + new string('x', 50000));
        var source = new CountingSource("t.csv", bytes);
        Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(source, Limit(1_000_000, 15)));
        Assert.True(source.BytesRead <= 16, "read " + source.BytesRead);
    }

    [Fact]
    public void Ceiling_Met_GivesTheDescriptorWithoutIt()
    {
        var c = Cases["lf"];
        var with = Profiler.ProfilePrefix(new BytesSource("t.csv", c.Bytes), Limit(c.Max, 100));
        var without = Profiler.ProfilePrefix(new BytesSource("t.csv", c.Bytes), Limit(c.Max));
        Assert.Equal(Json(without.Descriptor), Json(with.Descriptor));
        Assert.True(with.Prefix.Span.SequenceEqual(without.Prefix.Span));
    }

    [Fact]
    public void MaxBytesNull_IsRejected_AndNothingIsRead()
    {
        var source = new CountingSource("t.csv", Cases["lf"].Bytes);
        var ex = Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(source, Limit(null)));
        Assert.Contains("byte limit", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.Opens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void MaxBytesZeroOrNegative_IsRejected(long limit)
    {
        Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(new BytesSource("t.csv", Cases["lf"].Bytes), Limit(limit)));
    }

    [Fact]
    public void CeilingWithoutMaxBytes_AndBadNames_AreRejectedAsForProfile()
    {
        var bytes = Cases["lf"].Bytes;
        Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), Limit(null, 10)));
        Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(new BytesSource("a/b.csv", bytes), Limit(100)));
        Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(new BytesSource(string.Empty, bytes), Limit(100)));
        Assert.Throws<ArgumentNullException>(() => Profiler.ProfilePrefix(null!, Limit(100)));
        Assert.Throws<ArgumentNullException>(() => Profiler.ProfilePrefix(new BytesSource("t.csv", bytes), null!));
    }

    [Fact]
    public void SourceWithNoKnownLength_IsRejected()
    {
        var source = new CountingSource("t.csv", Cases["lf"].Bytes, reportedLength: -1);
        var ex = Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(source, Limit(1000)));
        Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, source.Opens);
    }

    [Fact]
    public void SourceReportingMoreThanItHolds_GivesTheBytesItHad_AndTheDescriptorDescribesThem()
    {
        var bytes = Cases["lf"].Bytes;
        var source = new CountingSource("t.csv", bytes, reportedLength: bytes.Length + 5000);
        var result = Profiler.ProfilePrefix(source, Limit(1000));
        Assert.True(result.Prefix.Length <= bytes.Length);
        Assert.True(result.Prefix.Span.SequenceEqual(bytes.AsSpan(0, result.Prefix.Length)));
        Assert.Equal(result.Prefix.Length, result.Scope.BytesRead);
        Assert.Equal(bytes.Length + 5000, result.Descriptor.File.Size);
    }

    [Fact]
    public void SourceTooLargeToBuffer_IsRejected_NotAnOutOfMemory()
    {
        var source = new GeneratedSource("big.bin", 4_000_000_000L, static i => (byte)(i % 251));
        var ex = Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(source, Limit(3_000_000_000L)));
        Assert.Contains("buffer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ownership_MutatingTheReturnedMemory_DoesNotChangeTheDescriptor_OrAnotherCall()
    {
        var c = Cases["lf"];
        var first = Profiler.ProfilePrefix(new BytesSource("t.csv", c.Bytes), Limit(c.Max));
        var second = Profiler.ProfilePrefix(new BytesSource("t.csv", c.Bytes), Limit(c.Max));
        var before = Json(first.Descriptor);
        var original = first.Prefix.ToArray();

        Assert.True(MemoryMarshal.TryGetArray(first.Prefix, out var segment));
        Array.Fill(segment.Array!, (byte)0xFF, segment.Offset, segment.Count);

        Assert.Equal(before, Json(first.Descriptor));
        Assert.Equal(before, Json(second.Descriptor));
        Assert.True(second.Prefix.Span.SequenceEqual(original), "another result does not share the buffer");
        Assert.True(c.Bytes.AsSpan(0, original.Length).SequenceEqual(original), "the caller's source bytes are untouched");
        var third = Profiler.ProfilePrefix(new BytesSource("t.csv", c.Bytes), Limit(c.Max));
        Assert.Equal(before, Json(third.Descriptor));
        Assert.True(third.Prefix.Span.SequenceEqual(original));
    }

    [Fact]
    public void Determinism_SecondCallAndAnotherCulture_GiveTheSameDescriptorAndBytes()
    {
        var c = Cases["utf16le"];
        var a = Profiler.ProfilePrefix(new BytesSource("t.csv", c.Bytes), Limit(c.Max));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var b = Profiler.ProfilePrefix(new BytesSource("t.csv", c.Bytes), Limit(c.Max));
            Assert.Equal(Json(a.Descriptor), Json(b.Descriptor));
            Assert.True(a.Prefix.Span.SequenceEqual(b.Prefix.Span));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void PublicShape_ProfiledPrefixAndProfilePrefix_AndIProfileSourceIsUnchanged()
    {
        var type = typeof(ProfiledPrefix);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Equal(typeof(Descriptor), type.GetProperty("Descriptor")!.PropertyType);
        Assert.Equal(typeof(ReadOnlyMemory<byte>), type.GetProperty("Prefix")!.PropertyType);
        Assert.Equal(typeof(ScopeSection), type.GetProperty("Scope")!.PropertyType);
        Assert.NotNull(type.GetMethod("<Clone>$"));
        Assert.True(typeof(ScopeSection).IsPublic);

        var method = typeof(Profiler).GetMethod("ProfilePrefix", BindingFlags.Public | BindingFlags.Static, [typeof(IProfileSource), typeof(ProfileOptions)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(ProfiledPrefix), method!.ReturnType);

        var members = typeof(IProfileSource).GetMembers().Select(static m => m.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Length", "Name", "Open", "get_Length", "get_Name" }, members);
    }

    /// <summary>A source that counts its opens and the bytes handed out, and hands them out in short reads.</summary>
    private sealed class CountingSource(string name, byte[] bytes, long? reportedLength = null) : IProfileSource
    {
        public string Name { get; } = name;

        public long Length => reportedLength ?? bytes.Length;

        public int Opens { get; private set; }

        public long BytesRead { get; private set; }

        public Stream Open()
        {
            Opens++;
            return new Counting(this, new MemoryStream(bytes, writable: false));
        }

        private sealed class Counting(CountingSource owner, MemoryStream inner) : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => inner.Length;

            public override long Position
            {
                get => inner.Position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var n = inner.Read(buffer, offset, Math.Min(count, 4093));
                owner.BytesRead += n;
                return n;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
