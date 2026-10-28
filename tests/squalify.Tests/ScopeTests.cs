// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>sq-35: byte-limited profiling and the <c>scope</c> section.</summary>
public sealed class ScopeTests
{
    private static ProfileOptions Limit(long? maxBytes) => ProfileOptions.Create(false, Array.Empty<string>(), maxBytes);

    private static string Json(Descriptor descriptor) => Encoding.UTF8.GetString(DescriptorWriter.ToBytes(descriptor));

    // "id,value,label\n" then rows "<id>,<id*3>,item<id>\n".
    private static byte[] Csv(int rows, string eol = "\n")
    {
        var sb = new StringBuilder("id,value,label").Append(eol);
        for (var i = 1; i <= rows; i++)
        {
            sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',').Append((i * 3).ToString(CultureInfo.InvariantCulture)).Append(",item")
                .Append(i.ToString(CultureInfo.InvariantCulture)).Append(eol);
        }

        return TestBytes.Ascii(sb.ToString());
    }

    private static long DataRows(Descriptor d) => d.Table!.Rows;

    [Fact]
    public void Limit_SmallerThanFile_ReadsAtMostLimitPlusRecordEnd_AndEndsOnWholeRecord()
    {
        var bytes = Csv(500);
        var source = new CountingSource("t.csv", bytes);
        var descriptor = Profiler.Profile(source, Limit(1000));

        var scope = Assert.IsType<ScopeSection>(descriptor.Scope);
        Assert.False(scope.Complete);
        Assert.True(scope.BytesRead <= 1000, "the cut is at or before the limit when a line end exists there");
        Assert.True(scope.BytesRead > 900, "the cut is the last line end, not some earlier one");
        Assert.Equal((byte)'\n', bytes[scope.BytesRead - 1]);
        Assert.True(source.MaxPosition <= 1000, "no pass reads past the limit when a line end lies inside it: " + source.MaxPosition);
        var expectedRows = bytes.AsSpan(0, (int)scope.BytesRead).Count((byte)'\n') - 1;
        Assert.Equal(expectedRows, DataRows(descriptor));
    }

    [Fact]
    public void Limit_ReportsScope_WholeSizeKept_Sha256Absent()
    {
        var bytes = Csv(500);
        var descriptor = Profiler.Profile(new BytesSource("t.csv", bytes), Limit(1000));

        var scope = Assert.IsType<ScopeSection>(descriptor.Scope);
        Assert.Equal(1000, scope.MaxBytes);
        Assert.False(scope.Complete);
        Assert.Equal(bytes.Length, descriptor.File.Size);
        Assert.Null(descriptor.File.Sha256);
        var text = Json(descriptor);
        Assert.DoesNotContain("\"sha256\":", text, StringComparison.Ordinal);
        Assert.Contains("\"scope\": {", text, StringComparison.Ordinal);
        Assert.Contains("\"maxBytes\": 1000,", text, StringComparison.Ordinal);
        Assert.Contains("\"complete\": false", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("\"file\":", StringComparison.Ordinal) < text.IndexOf("\"scope\":", StringComparison.Ordinal));
        Assert.True(text.IndexOf("\"scope\":", StringComparison.Ordinal) < text.IndexOf("\"encoding\":", StringComparison.Ordinal));
    }

    [Fact]
    public void Limit_LargerThanFile_SameDescriptorAsNoLimit_ApartFromScope()
    {
        var bytes = Csv(50);
        var unlimited = Profiler.Profile(new BytesSource("t.csv", bytes), Limit(null));
        var limited = Profiler.Profile(new BytesSource("t.csv", bytes), Limit(1_000_000));

        var scope = Assert.IsType<ScopeSection>(limited.Scope);
        Assert.True(scope.Complete);
        Assert.Equal(bytes.Length, scope.BytesRead);
        Assert.Equal(1_000_000, scope.MaxBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), limited.File.Sha256);
        Assert.Equal(Json(unlimited), Json(limited with { Scope = null }));
    }

    [Fact]
    public void Limit_EqualToFileSize_IsComplete()
    {
        var bytes = Csv(50);
        var descriptor = Profiler.Profile(new BytesSource("t.csv", bytes), Limit(bytes.Length));
        var scope = Assert.IsType<ScopeSection>(descriptor.Scope);
        Assert.True(scope.Complete);
        Assert.Equal(bytes.Length, scope.BytesRead);
        Assert.NotNull(descriptor.File.Sha256);
    }

    [Fact]
    public void NoLimit_WritesNoScope_AndKeepsSha256()
    {
        var descriptor = Profiler.Profile(new BytesSource("t.csv", Csv(5)), Limit(null));
        Assert.Null(descriptor.Scope);
        Assert.NotNull(descriptor.File.Sha256);
        var text = Json(descriptor);
        Assert.DoesNotContain("\"scope\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("maxBytes", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Limit_FirstRecordLongerThanLimit_ReadWholeNotEmpty()
    {
        var header = "id," + new string('x', 20000) + "\n";
        var rest = new StringBuilder();
        for (var i = 0; i < 20; i++)
        {
            rest.Append(i.ToString(CultureInfo.InvariantCulture)).Append(",5\n");
        }

        var bytes = TestBytes.Ascii(header + rest);
        var source = new CountingSource("long.csv", bytes);
        var descriptor = Profiler.Profile(source, Limit(100));

        var scope = Assert.IsType<ScopeSection>(descriptor.Scope);
        Assert.Equal(100, scope.MaxBytes);
        Assert.Equal(header.Length, scope.BytesRead);
        Assert.False(scope.Complete);
        Assert.Equal("text", descriptor.Encoding.Kind);
        Assert.True(descriptor.Layout.Regions.Count > 0);
        Assert.Equal(bytes.Length, descriptor.File.Size);
        Assert.True(source.MaxPosition <= header.Length + 4096, "the record end is found with small reads: " + source.MaxPosition);
    }

    [Fact]
    public void Limit_SingleLineWithoutTerminator_ReadsWholeFile_Complete()
    {
        var bytes = TestBytes.Ascii(new string('a', 3000));
        var descriptor = Profiler.Profile(new BytesSource("one.txt", bytes), Limit(100));
        var scope = Assert.IsType<ScopeSection>(descriptor.Scope);
        Assert.True(scope.Complete);
        Assert.Equal(3000, scope.BytesRead);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), descriptor.File.Sha256);
    }

    [Fact]
    public void Limit_CutsBetweenCrAndLf_KeepsTheCrLfPairWhole()
    {
        var bytes = Csv(200, "\r\n");
        var cr = Array.IndexOf(bytes, (byte)'\r', 300);
        Assert.Equal((byte)'\n', bytes[cr + 1]);
        var descriptor = Profiler.Profile(new BytesSource("t.csv", bytes), Limit(cr + 1));
        var scope = Assert.IsType<ScopeSection>(descriptor.Scope);
        Assert.Equal(cr + 2, scope.BytesRead);
        Assert.Equal(0, descriptor.Encoding.LineEndings.Cr);
        Assert.Equal(0, descriptor.Encoding.LineEndings.Lf);
    }

    [Fact]
    public void Limit_Binary_CutsAtTheByteLimit()
    {
        var bytes = new byte[5000];
        new Random(7).NextBytes(bytes);
        for (var i = 0; i < bytes.Length; i += 7)
        {
            bytes[i] = 0x0A;
        }

        bytes[0] = 0;
        var descriptor = Profiler.Profile(new BytesSource("blob.bin", bytes), Limit(1001));
        var scope = Assert.IsType<ScopeSection>(descriptor.Scope);
        Assert.Equal(1001, scope.BytesRead);
        Assert.False(scope.Complete);
        Assert.Equal("binary", descriptor.Encoding.Kind);
        Assert.Equal(bytes.Length, descriptor.File.Size);
        Assert.Null(descriptor.File.Sha256);
    }

    [Fact]
    public void Limit_Utf16Text_CutsOnACodeUnitBoundaryAfterALineEnd()
    {
        var text = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Encoding.ASCII.GetString(Csv(300)))).ToArray();
        var descriptor = Profiler.Profile(new BytesSource("u.csv", text), Limit(1001));
        var scope = Assert.IsType<ScopeSection>(descriptor.Scope);
        Assert.Equal("utf-16le", descriptor.Encoding.Charset);
        Assert.Equal(0, scope.BytesRead % 2);
        Assert.True(scope.BytesRead <= 1001);
        Assert.Equal(0x0A, text[(int)scope.BytesRead - 2]);
        Assert.Equal(0x00, text[(int)scope.BytesRead - 1]);
    }

    [Fact]
    public void Limit_FingerprintsEqualThoseOfTheExactPrefixFile_AndDoNotDependOnTheLimit()
    {
        var bytes = Csv(500);
        var a = Profiler.Profile(new BytesSource("t.csv", bytes), Limit(1000));
        var b = Profiler.Profile(new BytesSource("t.csv", bytes), Limit(1001));
        var cut = (int)a.Scope!.BytesRead;
        Assert.Equal(cut, b.Scope!.BytesRead);
        var prefixOnly = Profiler.Profile(new BytesSource("t.csv", bytes[..cut]), Limit(null));

        Assert.Equal(prefixOnly.Fingerprint, a.Fingerprint);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(prefixOnly.Table!.Fields.Count, a.Table!.Fields.Count);
        // The whole profile of the prefix is the prefix file's profile, except the file section and the scope.
        var expected = Json(prefixOnly with { File = a.File, Scope = a.Scope });
        Assert.Equal(expected, Json(a));
    }

    [Fact]
    public void Limit_FromAPath_UsesTheFileLengthAsTheWholeSize()
    {
        var path = Path.Combine(Path.GetTempPath(), "squalify-scope-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var bytes = Csv(400);
            File.WriteAllBytes(path, bytes);
            var descriptor = Profiler.Profile(path, Limit(2000));
            Assert.Equal(bytes.Length, descriptor.File.Size);
            Assert.False(descriptor.Scope!.Complete);
            Assert.True(descriptor.Scope.BytesRead <= 2000);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Limit_ZeroOrNegative_Throws(long limit)
    {
        var ex = Assert.Throws<SqualifyException>(() => Profiler.Profile(new BytesSource("t.csv", Csv(5)), Limit(limit)));
        Assert.Contains("--max-bytes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Limit_EmptyFile_IsCompleteWithZeroBytes()
    {
        var descriptor = Profiler.Profile(new BytesSource("e.txt", []), Limit(10));
        var scope = Assert.IsType<ScopeSection>(descriptor.Scope);
        Assert.True(scope.Complete);
        Assert.Equal(0, scope.BytesRead);
    }

    [Fact]
    public void Limit_SameInputTwice_SameBytes()
    {
        var bytes = Csv(500);
        var a = Json(Profiler.Profile(new BytesSource("t.csv", bytes), Limit(1000)));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(a, Json(Profiler.Profile(new BytesSource("t.csv", bytes), Limit(1000))));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>A source that remembers the furthest byte any stream read.</summary>
    private sealed class CountingSource(string name, byte[] bytes) : IProfileSource
    {
        public string Name { get; } = name;

        public long Length => bytes.Length;

        public long MaxPosition { get; private set; }

        public Stream Open() => new Counting(this, new MemoryStream(bytes, writable: false));

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
                // Hand out at most 4093 bytes at a time, so the reader's own buffering decides what it asks for.
                var n = inner.Read(buffer, offset, Math.Min(count, 4093));
                owner.MaxPosition = Math.Max(owner.MaxPosition, inner.Position);
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
