// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>sq-39: the opt-in first-record ceiling of byte-limited profiling.</summary>
public sealed class FirstRecordCeilingTests
{
    private static ProfileOptions Ceiling(long? maxBytes, long? maxFirstRecordBytes) =>
        ProfileOptions.Create(false, Array.Empty<string>(), maxBytes, maxFirstRecordBytes);

    private static string Json(Descriptor descriptor) => Encoding.UTF8.GetString(DescriptorWriter.ToBytes(descriptor));

    // "id,value,label" (14 characters) and the end of line, then 200 rows.
    private static byte[] Csv(string eol)
    {
        var sb = new StringBuilder("id,value,label").Append(eol);
        for (var i = 1; i <= 200; i++)
        {
            sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',').Append((i * 3).ToString(CultureInfo.InvariantCulture)).Append(",item")
                .Append(i.ToString(CultureInfo.InvariantCulture)).Append(eol);
        }

        return TestBytes.Ascii(sb.ToString());
    }

    private static byte[] Encoded(Encoding encoding, string text) => encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

    private static SqualifyException Refused(IProfileSource source, long? maxBytes, long? ceiling) =>
        Assert.Throws<SqualifyException>(() => Profiler.Profile(source, Ceiling(maxBytes, ceiling)));

    [Fact]
    public void Off_FirstLineLongerThanTheLimit_IsReadWhole_AndTheCeilingChangesNothingWhenItIsMet()
    {
        var header = "id," + new string('x', 20000) + "\n";
        var bytes = TestBytes.Ascii(header + string.Concat(Enumerable.Range(0, 20).Select(static i => i.ToString(CultureInfo.InvariantCulture) + ",5\n")));

        var off = Profiler.Profile(new BytesSource("long.csv", bytes), Ceiling(100, null));
        Assert.Equal(header.Length, off.Scope!.BytesRead);
        Assert.False(off.Scope.Complete);
        Assert.Equal(100, off.Scope.MaxBytes);

        var met = Profiler.Profile(new BytesSource("long.csv", bytes), Ceiling(100, header.Length));
        Assert.Equal(Json(off), Json(met));
        Assert.DoesNotContain("irstRecord", Json(met), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n", 8)]
    [InlineData("\n", 15)]
    [InlineData("\n", 40)]
    [InlineData("\n", 1_000_000)]
    [InlineData("\r\n", 8)]
    [InlineData("\r\n", 16)]
    [InlineData("\r\n", 1_000_000)]
    [InlineData("\r", 8)]
    [InlineData("\r", 15)]
    [InlineData("\r", 1_000_000)]
    public void Boundary_FirstRecordOfExactlyN_IsAccepted_AndNPlusOneIsRefused(string eol, long maxBytes)
    {
        var bytes = Csv(eol);
        var length = 14 + eol.Length;

        var descriptor = Profiler.Profile(new BytesSource("t.csv", bytes), Ceiling(maxBytes, length));
        Assert.True(descriptor.Scope!.BytesRead >= length);

        var ex = Refused(new BytesSource("t.csv", bytes), maxBytes, length - 1);
        Assert.Contains("longer", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_AFirstRecordWhoseTerminatorIsByteNPlusOne_IsRefused()
    {
        // "id,value,label" is 14 bytes: with N = 14 the LF is byte 15.
        Refused(new BytesSource("t.csv", Csv("\n")), 100_000, 14);
        Refused(new BytesSource("t.csv", Csv("\n")), 20, 14);
    }

    [Fact]
    public void Cuts_BareCrAsByteN_ReadsOneMoreUnit_NoLfAfterIt_RecordIsN()
    {
        var bytes = Csv("\r");
        var source = new TrackingSource(new BytesSource("t.csv", bytes));
        var descriptor = Profiler.Profile(source, Ceiling(15, 15));
        Assert.Equal(15, descriptor.Scope!.BytesRead);
        Assert.True(source.MaxPosition <= 16, "one lookahead byte only: " + source.MaxPosition);
    }

    [Fact]
    public void Cuts_CrAsByteN_FollowedByLf_IsCrlf_RecordIsNPlusOne_Refused()
    {
        var bytes = Csv("\r\n");
        var source = new TrackingSource(new BytesSource("t.csv", bytes));
        var ex = Refused(source, 1000, 15);
        Assert.Contains("longer", ex.Message, StringComparison.Ordinal);
        Assert.True(source.MaxPosition <= 16, "one lookahead byte only: " + source.MaxPosition);
    }

    [Theory]
    [InlineData("\n", "\n")]
    [InlineData("\r", "\r")]
    [InlineData("\n", "\r")]
    public void Cuts_ATerminatorAsByteN_IsTheEndOfTheRecord_WhateverFollowsIt(string first, string second)
    {
        // Only a CR is followed to see whether an LF comes next; an LF or a CR followed by another terminator is not a CRLF.
        var bytes = TestBytes.Ascii("id,v" + first + second + "1,2\n3,4\n");
        Profiler.Profile(new BytesSource("t.csv", bytes), Ceiling(1000, 5));
        Profiler.Profile(new BytesSource("t.csv", bytes), Ceiling(6, 5));
    }

    [Fact]
    public void Cuts_CrlfWithinN_IsAccepted_WhenTheLfIsByteN()
    {
        Profiler.Profile(new BytesSource("t.csv", Csv("\r\n")), Ceiling(1000, 16));

        // The LF that completes the CRLF is byte N; the unit after it is not looked at (here it is another LF).
        var blank = TestBytes.Ascii("id,v\r\n\n1,2\n3,4\n");
        Profiler.Profile(new BytesSource("t.csv", blank), Ceiling(1000, 6));
        Profiler.Profile(new BytesSource("t.csv", blank), Ceiling(7, 6));
    }

    [Theory]
    [InlineData("utf-16le", 2)]
    [InlineData("utf-16be", 2)]
    [InlineData("utf-32le", 4)]
    [InlineData("utf-32be", 4)]
    public void Cuts_WideCharsets_CountTerminatorAndUnitsInBytes_AndStopAtTheFirstWholeUnitPastN(string charset, int unit)
    {
        Encoding encoding = charset switch
        {
            "utf-16le" => new UnicodeEncoding(false, true),
            "utf-16be" => new UnicodeEncoding(true, true),
            "utf-32le" => new UTF32Encoding(false, true),
            _ => new UTF32Encoding(true, true),
        };
        var text = "id,value,label\n" + string.Concat(Enumerable.Range(1, 40).Select(static i => i.ToString(CultureInfo.InvariantCulture) + ",5,x\n"));
        var bytes = Encoded(encoding, text);
        var preamble = encoding.GetPreamble().Length;
        var length = preamble + (15 * unit);

        for (var n = length; n <= length + unit; n++)
        {
            var descriptor = Profiler.Profile(new BytesSource("u.csv", bytes), Ceiling(100_000, n));
            Assert.Equal(charset, descriptor.Encoding.Charset);
        }

        for (var n = length - unit + 1; n < length; n++)
        {
            var source = new TrackingSource(new BytesSource("u.csv", bytes));
            var ex = Refused(source, 100_000, n);
            Assert.Contains("longer", ex.Message, StringComparison.Ordinal);
            Assert.True(source.MaxPosition <= n + unit, $"N={n}: read to {source.MaxPosition}");
        }
    }

    [Theory]
    [InlineData("utf-16le", 2)]
    [InlineData("utf-32le", 4)]
    public void Cuts_WideCharsets_CrAsTheLastUnitWithinN_LfAfterIt_IsRefused_NoLf_IsAccepted(string charset, int unit)
    {
        Encoding encoding = charset == "utf-16le" ? new UnicodeEncoding(false, true) : new UTF32Encoding(false, true);
        var preamble = encoding.GetPreamble().Length;
        var crlf = Encoded(encoding, "id,value\r\n1,2\r\n3,4\r\n");
        var cr = Encoded(encoding, "id,value\r1,2\r3,4\r");

        // The CR is the 9th unit: its end is at preamble + 9 * unit.
        var end = preamble + (9 * unit);
        for (var n = end; n < end + unit; n++)
        {
            Refused(new BytesSource("u.csv", crlf), 100_000, n);
            Profiler.Profile(new BytesSource("u.csv", cr), Ceiling(100_000, n));
        }

        Profiler.Profile(new BytesSource("u.csv", crlf), Ceiling(100_000, end + unit));
    }

    [Fact]
    public void Binary_WithNoCrOrLf_IsNotSubject()
    {
        var bytes = new byte[10_000];
        new Random(11).NextBytes(bytes);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] is 0x0A or 0x0D)
            {
                bytes[i] = 0x41;
            }
        }

        bytes[0] = 0;
        var cut = Profiler.Profile(new BytesSource("blob.bin", bytes), Ceiling(1000, 100));
        Assert.Equal("binary", cut.Encoding.Kind);
        Assert.Equal(1000, cut.Scope!.BytesRead);

        var whole = Profiler.Profile(new BytesSource("blob.bin", bytes), Ceiling(1_000_000, 100));
        Assert.Equal("binary", whole.Encoding.Kind);
        Assert.True(whole.Scope!.Complete);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(5_000_000)]
    [InlineData(1L << 40)]
    public void ReadsAtMostNPlusOneBytes_OfALargeFileWithNoLineBreak(long maxBytes)
    {
        const long length = 64L * 1024 * 1024;
        const long n = 1000;
        var source = new TrackingSource(new GeneratedSource("big.txt", length, static _ => (byte)'a'));

        var ex = Refused(source, maxBytes, n);

        Assert.Contains("longer", ex.Message, StringComparison.Ordinal);
        Assert.True(source.MaxPosition <= n + 1, "read to " + source.MaxPosition);
    }

    [Fact]
    public void ReadsAtMostNPlusOneBytes_WhenTheFileIsSmallerThanMaxBytes_AndHasNoLineBreak()
    {
        var source = new TrackingSource(new GeneratedSource("mid.txt", 3_000_000, static _ => (byte)'a'));
        Refused(source, 10_000_000, 1000);
        Assert.True(source.MaxPosition <= 1001, "read to " + source.MaxPosition);
    }

    [Fact]
    public void OnlyTheFirstRecord_ALaterLongerRecordDoesNotTrigger()
    {
        var bytes = TestBytes.Ascii("id,v\n1," + new string('x', 5000) + "\n2,3\n");
        var met = Profiler.Profile(new BytesSource("t.csv", bytes), Ceiling(100_000, 5));
        var plain = Profiler.Profile(new BytesSource("t.csv", bytes), Ceiling(100_000, null));
        Assert.Equal(Json(plain), Json(met));

        var small = Profiler.Profile(new BytesSource("t.csv", bytes), Ceiling(20, 5));
        Assert.Equal(5, small.Scope!.BytesRead);
    }

    [Fact]
    public void FileShorterThanN_WithNoTerminator_IsAccepted_ButOneLongerThanNIsRefused()
    {
        var abc = TestBytes.Ascii("abcdef");
        Profiler.Profile(new BytesSource("one.txt", abc), Ceiling(100, 6));
        Profiler.Profile(new BytesSource("one.txt", abc), Ceiling(100, 50));
        Refused(new BytesSource("one.txt", abc), 100, 5);
        Refused(new BytesSource("one.txt", abc), 3, 5);
    }

    [Fact]
    public void Failure_NamesTheOption_N_AndThatTheFirstRecordIsLonger()
    {
        var ex = Refused(new BytesSource("wide.csv", Csv("\n")), 100, 10);
        Assert.Contains("--max-first-record-bytes", ex.Message, StringComparison.Ordinal);
        Assert.Contains("10", ex.Message, StringComparison.Ordinal);
        Assert.Contains("longer", ex.Message, StringComparison.Ordinal);
        Assert.Contains("first record", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validation_ZeroOrNegative_Throws(long ceiling)
    {
        var ex = Refused(new BytesSource("t.csv", Csv("\n")), 100, ceiling);
        Assert.Contains("--max-first-record-bytes", ex.Message, StringComparison.Ordinal);
        Assert.Contains("positive", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_WithoutMaxBytes_IsRejected()
    {
        var ex = Refused(new BytesSource("t.csv", Csv("\n")), null, 100);
        Assert.Contains("--max-first-record-bytes", ex.Message, StringComparison.Ordinal);
        Assert.Contains("--max-bytes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_StoresTheCeiling_AndTheOptionsSectionDoesNotWriteIt()
    {
        var options = ProfileOptions.Create(false, Array.Empty<string>(), 100, 50);
        Assert.Equal(50, options.MaxFirstRecordBytes);
        Assert.Null(ProfileOptions.Create(false, Array.Empty<string>()).MaxFirstRecordBytes);
        var descriptor = Profiler.Profile(new BytesSource("t.csv", Csv("\n")), Ceiling(100, 50));
        Assert.DoesNotContain("irstRecord", Json(descriptor), StringComparison.Ordinal);
    }

    [Fact]
    public void SameInputTwice_SameResult_AndSameFailure()
    {
        var bytes = Csv("\n");
        Assert.Equal(Json(Profiler.Profile(new BytesSource("t.csv", bytes), Ceiling(100, 50))), Json(Profiler.Profile(new BytesSource("t.csv", bytes), Ceiling(100, 50))));
        Assert.Equal(Refused(new BytesSource("t.csv", bytes), 100, 5).Message, Refused(new BytesSource("t.csv", bytes), 100, 5).Message);
    }

    /// <summary>A source that remembers the furthest byte any stream read.</summary>
    private sealed class TrackingSource(IProfileSource inner) : IProfileSource
    {
        public string Name => inner.Name;

        public long Length => inner.Length;

        public long MaxPosition { get; private set; }

        public Stream Open() => new Tracking(this, inner.Open());

        private sealed class Tracking(TrackingSource owner, Stream stream) : Stream
        {
            private long _position;

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => stream.Length;

            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var n = stream.Read(buffer, offset, count);
                _position += n;
                owner.MaxPosition = Math.Max(owner.MaxPosition, _position);
                return n;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    stream.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
