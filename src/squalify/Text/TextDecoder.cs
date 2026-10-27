// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Text;

namespace squalor.squalify;

/// <summary>
/// Streams the characters of a text file in its charset and reports, for each character, the byte offset where it
/// starts in the file (BOM included). A supplementary code point is two characters with the same offset. Invalid input
/// decodes to U+FFFD: one replacement per maximal invalid UTF-8 sequence, per lone UTF-16 surrogate or truncated unit,
/// and per invalid UTF-32 value. The result never contains a lone surrogate.
/// </summary>
internal sealed class TextDecoder
{
    private const int BufferSize = 64 * 1024;
    private const int MaxUnit = 4;
    private const char Replacement = '\uFFFD';

    private readonly Stream _stream;
    private readonly Kind _kind;
    private readonly byte[] _buffer = new byte[BufferSize + MaxUnit];
    private int _position;
    private int _end;
    private long _bufferOffset;
    private bool _eof;
    private bool _hasPendingLow;
    private char _pendingLow;
    private long _pendingOffset;

    private enum Kind
    {
        Latin1,
        Utf8,
        Utf16Le,
        Utf16Be,
        Utf32Le,
        Utf32Be,
    }

    /// <summary>Creates a decoder.</summary>
    /// <param name="stream">The stream, positioned at the start of the file.</param>
    /// <param name="charset"><c>ascii</c>, <c>utf-8</c>, <c>latin-1</c>, <c>utf-16le</c>, <c>utf-16be</c>, <c>utf-32le</c> or <c>utf-32be</c>.</param>
    /// <param name="bomLength">Bytes to skip first (the BOM).</param>
    public TextDecoder(Stream stream, string charset, int bomLength)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(charset);
        _stream = stream;
        _kind = charset switch
        {
            "ascii" or "utf-8" => Kind.Utf8,
            "latin-1" => Kind.Latin1,
            "utf-16le" => Kind.Utf16Le,
            "utf-16be" => Kind.Utf16Be,
            "utf-32le" => Kind.Utf32Le,
            "utf-32be" => Kind.Utf32Be,
            _ => throw new ArgumentException("Unknown charset '" + charset + "'.", nameof(charset)),
        };
        ArgumentOutOfRangeException.ThrowIfNegative(bomLength);
        Fill();
        _position = Math.Min(bomLength, _end);
    }

    /// <summary>Decodes up to <c>chars.Length</c> characters.</summary>
    /// <param name="chars">Receives the characters.</param>
    /// <param name="offsets">Receives the byte offset of each character; at least as long as <paramref name="chars"/>.</param>
    /// <returns>The number of characters written; 0 at the end of the file.</returns>
    public int Read(Span<char> chars, Span<long> offsets)
    {
        if (offsets.Length < chars.Length)
        {
            throw new ArgumentException("offsets must be at least as long as chars.", nameof(offsets));
        }

        var count = 0;
        while (count < chars.Length)
        {
            if (_hasPendingLow)
            {
                chars[count] = _pendingLow;
                offsets[count] = _pendingOffset;
                count++;
                _hasPendingLow = false;
                continue;
            }

            if (_end - _position < MaxUnit && !_eof)
            {
                Fill();
            }

            if (_position >= _end)
            {
                break;
            }

            var offset = _bufferOffset + _position;
            var (value, used) = DecodeOne(_buffer.AsSpan(_position, _end - _position));
            _position += used;
            if (value < 0x10000)
            {
                chars[count] = (char)value;
                offsets[count] = offset;
                count++;
            }
            else
            {
                var scalar = value - 0x10000;
                chars[count] = (char)(0xD800 + (scalar >> 10));
                offsets[count] = offset;
                count++;
                _pendingLow = (char)(0xDC00 + (scalar & 0x3FF));
                _pendingOffset = offset;
                _hasPendingLow = true;
            }
        }

        return count;
    }

    private void Fill()
    {
        var remaining = _end - _position;
        if (remaining > 0)
        {
            Buffer.BlockCopy(_buffer, _position, _buffer, 0, remaining);
        }

        _bufferOffset += _position;
        _position = 0;
        _end = remaining;
        while (_end < BufferSize && !_eof)
        {
            var read = _stream.Read(_buffer, _end, BufferSize - _end);
            if (read == 0)
            {
                _eof = true;
            }
            else
            {
                _end += read;
            }
        }
    }

    // Decodes one code point. At least MaxUnit bytes are available unless the file ends sooner.
    private (int Value, int Used) DecodeOne(ReadOnlySpan<byte> data)
    {
        switch (_kind)
        {
            case Kind.Latin1:
                return (data[0], 1);

            case Kind.Utf8:
                {
                    if (data[0] < 0x80)
                    {
                        return (data[0], 1);
                    }

                    var status = Rune.DecodeFromUtf8(data, out var rune, out var consumed);
                    return status == System.Buffers.OperationStatus.Done ? (rune.Value, consumed) : (Replacement, Math.Max(consumed, 1));
                }

            case Kind.Utf16Le:
            case Kind.Utf16Be:
                return DecodeUtf16(data, _kind == Kind.Utf16Be);

            default:
                {
                    if (data.Length < 4)
                    {
                        return (Replacement, data.Length);
                    }

                    var value = _kind == Kind.Utf32Be ? BinaryPrimitives.ReadUInt32BigEndian(data) : BinaryPrimitives.ReadUInt32LittleEndian(data);
                    return value <= 0x10FFFF && (value < 0xD800 || value > 0xDFFF) ? ((int)value, 4) : (Replacement, 4);
                }
        }
    }

    private static (int Value, int Used) DecodeUtf16(ReadOnlySpan<byte> data, bool bigEndian)
    {
        if (data.Length < 2)
        {
            return (Replacement, data.Length);
        }

        int first = Unit(data, 0, bigEndian);
        if (first is >= 0xD800 and <= 0xDBFF)
        {
            if (data.Length >= 4)
            {
                int second = Unit(data, 2, bigEndian);
                if (second is >= 0xDC00 and <= 0xDFFF)
                {
                    return (0x10000 + ((first - 0xD800) << 10) + (second - 0xDC00), 4);
                }
            }

            return (Replacement, 2);
        }

        return first is >= 0xDC00 and <= 0xDFFF ? (Replacement, 2) : (first, 2);
    }

    private static ushort Unit(ReadOnlySpan<byte> data, int at, bool bigEndian)
    {
        return bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data[at..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
    }
}
