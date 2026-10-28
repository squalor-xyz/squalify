// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public class InvalidUnicodeTests
{
    private static readonly ProfileOptions Options = ProfileOptions.Create(false, Array.Empty<string>());

    private static byte[] Utf16(bool bigEndian, bool bom, params object[] parts)
    {
        var bytes = new List<byte>();
        if (bom)
        {
            bytes.AddRange(bigEndian ? new byte[] { 0xFE, 0xFF } : new byte[] { 0xFF, 0xFE });
        }

        foreach (var part in parts)
        {
            switch (part)
            {
                case string text:
                    bytes.AddRange(new UnicodeEncoding(bigEndian, false).GetBytes(text));
                    break;
                case int unit:
                    var two = new byte[2];
                    if (bigEndian)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(two, (ushort)unit);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(two, (ushort)unit);
                    }

                    bytes.AddRange(two);
                    break;
                case byte raw:
                    bytes.Add(raw);
                    break;
                default:
                    throw new ArgumentException("Unsupported part.");
            }
        }

        return bytes.ToArray();
    }

    private static byte[] Utf32(bool bigEndian, params object[] parts)
    {
        var bytes = new List<byte>(bigEndian ? new byte[] { 0x00, 0x00, 0xFE, 0xFF } : new byte[] { 0xFF, 0xFE, 0x00, 0x00 });
        foreach (var part in parts)
        {
            switch (part)
            {
                case string text:
                    foreach (var rune in text.EnumerateRunes())
                    {
                        bytes.AddRange(Word((uint)rune.Value, bigEndian));
                    }

                    break;
                case uint value:
                    bytes.AddRange(Word(value, bigEndian));
                    break;
                case byte raw:
                    bytes.Add(raw);
                    break;
                default:
                    throw new ArgumentException("Unsupported part.");
            }
        }

        return bytes.ToArray();
    }

    private static byte[] Word(uint value, bool bigEndian)
    {
        var four = new byte[4];
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(four, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(four, value);
        }

        return four;
    }

    private static Descriptor Profile(byte[] bytes)
    {
        return Profiler.Profile(new BytesSource("t.txt", bytes), Options);
    }

    private static Issue? InvalidUnicode(Descriptor descriptor)
    {
        return descriptor.Issues.SingleOrDefault(static i => i.Code == "invalidUnicode");
    }

    [Fact]
    public void Utf16Le_StrayTrailingByte_CountsOneByteWithOffset()
    {
        var bytes = Utf16(false, true, "hello world\n", (byte)0x41);
        var descriptor = Profile(bytes);
        Assert.Equal("utf-16le", descriptor.Encoding.Charset);
        Assert.Equal(1, descriptor.Encoding.InvalidBytes);
        var issue = InvalidUnicode(descriptor);
        Assert.NotNull(issue);
        Assert.Equal(1, issue.Count);
        Assert.Equal(new long[] { 26 }, issue.Offsets);
        Assert.Null(issue.FirstLines);
    }

    [Theory]
    [InlineData(false, 0xD800)]
    [InlineData(false, 0xDBFF)]
    [InlineData(false, 0xDC00)]
    [InlineData(false, 0xDFFF)]
    [InlineData(true, 0xD800)]
    [InlineData(true, 0xDBFF)]
    [InlineData(true, 0xDC00)]
    [InlineData(true, 0xDFFF)]
    public void Utf16_LoneSurrogateInTheMiddle_CountsTwoBytes(bool bigEndian, int surrogate)
    {
        var bytes = Utf16(bigEndian, true, "ab", surrogate, "cd\n");
        var descriptor = Profile(bytes);
        Assert.Equal(bigEndian ? "utf-16be" : "utf-16le", descriptor.Encoding.Charset);
        Assert.Equal(2, descriptor.Encoding.InvalidBytes);
        var issue = InvalidUnicode(descriptor);
        Assert.NotNull(issue);
        Assert.Equal(1, issue.Count);
        Assert.Equal(new long[] { 6 }, issue.Offsets);
    }

    [Theory]
    [InlineData(false, 0x110000u)]
    [InlineData(false, 0xD800u)]
    [InlineData(false, 0xDFFFu)]
    [InlineData(false, 0xFFFFFFFFu)]
    [InlineData(true, 0x110000u)]
    [InlineData(true, 0xD800u)]
    [InlineData(true, 0xDFFFu)]
    [InlineData(true, 0xFFFFFFFFu)]
    public void Utf32_InvalidValue_CountsFourBytes(bool bigEndian, uint value)
    {
        var bytes = Utf32(bigEndian, "ab", value, "cd\n");
        var descriptor = Profile(bytes);
        Assert.Equal(bigEndian ? "utf-32be" : "utf-32le", descriptor.Encoding.Charset);
        Assert.Equal(4, descriptor.Encoding.InvalidBytes);
        var issue = InvalidUnicode(descriptor);
        Assert.NotNull(issue);
        Assert.Equal(1, issue.Count);
        Assert.Equal(new long[] { 12 }, issue.Offsets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf32_TruncatedTrailingUnit_CountsTheRemainingBytes(bool bigEndian)
    {
        var descriptor = Profile(Utf32(bigEndian, "ab\n", (byte)1, (byte)2, (byte)3));
        Assert.Equal(3, descriptor.Encoding.InvalidBytes);
        var issue = InvalidUnicode(descriptor);
        Assert.NotNull(issue);
        Assert.Equal(1, issue.Count);
        Assert.Equal(new long[] { 16 }, issue.Offsets);
    }

    [Fact]
    public void Utf16_HighSurrogateThenOddByteAtTheEnd_CountsBoth()
    {
        var descriptor = Profile(Utf16(false, true, "ab\n", 0xD800, (byte)0x41));
        Assert.Equal(3, descriptor.Encoding.InvalidBytes);
        var issue = InvalidUnicode(descriptor);
        Assert.NotNull(issue);
        Assert.Equal(2, issue.Count);
        Assert.Equal(new long[] { 8, 10 }, issue.Offsets);
    }

    [Fact]
    public void Utf16_WithoutBom_DetectedByNulParity_ReportsInvalidUnits()
    {
        var bytes = Utf16(true, false, "hello world\n", 0xDC00, "more text\n");
        var descriptor = Profile(bytes);
        Assert.Equal("utf-16be", descriptor.Encoding.Charset);
        Assert.Null(descriptor.Encoding.Bom);
        Assert.Equal(2, descriptor.Encoding.InvalidBytes);
        Assert.Equal(new long[] { 24 }, InvalidUnicode(descriptor)!.Offsets);
    }

    [Fact]
    public void Utf16And32_ValidTextIncludingSupplementaryPairsAndFffd_HasNoInvalidBytesAndNoIssue()
    {
        // U+FFFD itself is valid text and must not be mistaken for a replacement made by the decoder.
        var text = "caf\u00E9 \U0001F600 \uFFFD \U0010FFFF\n";
        var mostlyAscii = "hello world, some plain ascii text \U0001F600 \uFFFD \U0010FFFF\n";
        foreach (var bytes in new[]
        {
            Utf16(false, true, text),
            Utf16(true, true, text),
            Utf16(false, false, mostlyAscii),
            Utf16(true, false, mostlyAscii),
            Utf32(false, text),
            Utf32(true, text),
        })
        {
            var descriptor = Profile(bytes);
            Assert.StartsWith("utf-", descriptor.Encoding.Charset);
            Assert.NotEqual("utf-8", descriptor.Encoding.Charset);
            Assert.Equal(0, descriptor.Encoding.InvalidBytes);
            Assert.Null(InvalidUnicode(descriptor));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf16_OffsetsAreCappedAtTenAndAscending_CountsStayExact(bool bigEndian)
    {
        var parts = new List<object>();
        for (var i = 0; i < 15; i++)
        {
            parts.Add("ab");
            parts.Add(0xD800);
        }

        parts.Add("\n");
        var descriptor = Profile(Utf16(bigEndian, true, parts.ToArray()));
        Assert.Equal(30, descriptor.Encoding.InvalidBytes);
        var issue = InvalidUnicode(descriptor)!;
        Assert.Equal(15, issue.Count);
        Assert.Equal(Enumerable.Range(0, 10).Select(static i => 2L + (6 * i) + 4).ToArray(), issue.Offsets);
    }

    [Fact]
    public void Utf32_OffsetsAreCappedAtTen()
    {
        var parts = new List<object>();
        for (var i = 0; i < 12; i++)
        {
            parts.Add(0x110000u);
        }

        var descriptor = Profile(Utf32(true, parts.ToArray()));
        Assert.Equal(48, descriptor.Encoding.InvalidBytes);
        var issue = InvalidUnicode(descriptor)!;
        Assert.Equal(12, issue.Count);
        Assert.Equal(Enumerable.Range(0, 10).Select(static i => 4L + (4 * i)).ToArray(), issue.Offsets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf16_UnitsThatStraddleTheInternalBuffer_AreJudgedWhole(bool bigEndian)
    {
        // The pair starts at byte 65,534 and ends at 65,537: valid. The lone high surrogate at 65,538 is followed by a letter.
        var pad = new string('a', (65534 - 2) / 2);
        var bytes = Utf16(bigEndian, true, pad, "\U0001F600", 0xD800, "z\n");
        var descriptor = Profile(bytes);
        Assert.Equal(2, descriptor.Encoding.InvalidBytes);
        var issue = InvalidUnicode(descriptor)!;
        Assert.Equal(1, issue.Count);
        Assert.Equal(new long[] { 65538 }, issue.Offsets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf16_SmallReads_GiveTheSameDescriptorAsWholeReads(bool bigEndian)
    {
        var parts = new List<object>();
        for (var i = 0; i < 40; i++)
        {
            parts.Add(new string('x', 2000));
            parts.Add(i % 2 == 0 ? 0xD800 : 0xDC00);
            parts.Add("\U0001F600\n");
        }

        parts.Add((byte)0x41);
        var bytes = Utf16(bigEndian, true, parts.ToArray());
        var whole = Profiler.Profile(new BytesSource("t.txt", bytes), Options);
        var small = Profiler.Profile(new OneBytePerReadSource("t.txt", bytes), Options);
        Assert.Equal(81, whole.Encoding.InvalidBytes);
        Assert.Equal(41, InvalidUnicode(whole)!.Count);
        Assert.Equal(Encoding.UTF8.GetString(DescriptorWriter.ToBytes(whole)), Encoding.UTF8.GetString(DescriptorWriter.ToBytes(small)));
    }

    [Fact]
    public void Utf32_SmallReads_GiveTheSameDescriptorAsWholeReads()
    {
        var parts = new List<object>();
        for (var i = 0; i < 4000; i++)
        {
            parts.Add(i % 100 == 99 ? 0xD800u : (uint)'x');
        }

        parts.Add((byte)0x41);
        var bytes = Utf32(false, parts.ToArray());
        var whole = Profiler.Profile(new BytesSource("t.txt", bytes), Options);
        var small = Profiler.Profile(new OneBytePerReadSource("t.txt", bytes), Options);
        Assert.Equal((40 * 4) + 1, whole.Encoding.InvalidBytes);
        Assert.Equal(Encoding.UTF8.GetString(DescriptorWriter.ToBytes(whole)), Encoding.UTF8.GetString(DescriptorWriter.ToBytes(small)));
    }

    [Fact]
    public void Utf8AndLatin1_AreNotAffected()
    {
        var utf8 = Profile(TestBytes.Concat(Encoding.UTF8.GetBytes("caf\u00E9\n"), TestBytes.Hex("FF"), TestBytes.Ascii("end\n")));
        Assert.Equal(1, utf8.Encoding.InvalidBytes);
        Assert.Null(InvalidUnicode(utf8));
        Assert.Contains(utf8.Issues, static i => i.Code == "invalidUtf8");
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
            if (count == 0 || _position >= bytes.Length)
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
