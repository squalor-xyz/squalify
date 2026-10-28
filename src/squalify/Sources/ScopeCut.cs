// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>A byte-limited view of a source: the first <c>length</c> bytes, so every pass sees the prefix and nothing more.</summary>
internal sealed class PrefixSource(IProfileSource inner, long length) : IProfileSource
{
    /// <inheritdoc />
    public string Name => inner.Name;

    /// <inheritdoc />
    public long Length => length;

    /// <inheritdoc />
    public Stream Open()
    {
        return new PrefixStream(inner.Open(), length);
    }

    private sealed class PrefixStream(Stream inner, long limit) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => limit;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var room = limit - _position;
            if (room <= 0 || buffer.Length == 0)
            {
                return 0;
            }

            var n = inner.Read(buffer[..(int)Math.Min(buffer.Length, room)]);
            _position += n;
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
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>What a byte limit resolved to: the view to profile, its byte-pass result and the <c>scope</c> section.</summary>
/// <param name="Source">The source that the passes read: the whole source, or a prefix view.</param>
/// <param name="Sniff">The byte-pass result of <paramref name="Source"/>.</param>
/// <param name="Scope">The scope section.</param>
internal sealed record ScopedSource(IProfileSource Source, SniffResult Sniff, ScopeSection Scope);

/// <summary>
/// Applies <see cref="ProfileOptions.MaxBytes"/>. The cut depends on the content class: a binary file is cut at the
/// limit; a text file is cut after its last line terminator (LF, CR or CRLF, in the charset's own code units) that fits
/// in the limit. A text file with no terminator in the limit is read to the end of its first line, so a file with data
/// never gives an empty profile. The cut is a byte offset only; no format is decided here, so a quoted field that spans
/// lines and straddles the limit is cut at a line break inside it.
/// </summary>
internal static class ScopeCut
{
    // The size of the reads that look for the end of a first record that is longer than the limit.
    private const int LookaheadChunk = 4096;

    /// <summary>Resolves the limit against a source.</summary>
    /// <param name="source">The whole source.</param>
    /// <param name="maxBytes">The limit; positive.</param>
    /// <param name="tolerance">The tolerated stray invalid UTF-8 sequences.</param>
    /// <returns>The view to profile, its byte-pass result and the scope.</returns>
    public static ScopedSource Apply(IProfileSource source, long maxBytes, CharsetTolerance tolerance)
    {
        var total = source.Length;
        if (total <= maxBytes)
        {
            var whole = ByteSniffer.Sniff(source, tolerance);
            return new ScopedSource(source, whole, new ScopeSection(maxBytes, whole.Length, true));
        }

        // The class of the content (text or binary) and the charset come from the bytes inside the limit.
        var window = new PrefixSource(source, maxBytes);
        var sniff = ByteSniffer.Sniff(window, tolerance);
        var cut = maxBytes;
        if (sniff.Kind == "text" && sniff.Charset is { } charset)
        {
            cut = FindLineCut(source, maxBytes, UnitWidth(charset), BigEndian(charset), total);
            if (cut != maxBytes)
            {
                window = new PrefixSource(source, cut);
                sniff = ByteSniffer.Sniff(window, tolerance);
            }
        }

        return new ScopedSource(window, sniff, new ScopeSection(maxBytes, sniff.Length, sniff.Length >= total));
    }

    private static int UnitWidth(string charset)
    {
        return charset switch
        {
            "utf-16le" or "utf-16be" => 2,
            "utf-32le" or "utf-32be" => 4,
            _ => 1,
        };
    }

    private static bool BigEndian(string charset)
    {
        return charset is "utf-16be" or "utf-32be";
    }

    // Returns the byte offset just after the last line terminator whose unit lies inside [0, maxBytes], or, when there is
    // none, just after the first terminator past it, or the total when the file has no terminator at all. A CR that ends
    // the window may be the first half of a CRLF, so one more unit is read to see.
    private static long FindLineCut(IProfileSource source, long maxBytes, int width, bool bigEndian, long total)
    {
        var windowEnd = maxBytes - (maxBytes % width);
        var chunkBytes = LookaheadChunk - (LookaheadChunk % width);
        var buffer = new byte[chunkBytes];
        var filled = 0;
        var index = 0;
        long position = 0;
        var lastCut = -1L;
        var lastWasCr = false;
        using var stream = source.Open();
        while (true)
        {
            if (index == filled)
            {
                int want;
                if (position < windowEnd)
                {
                    // Inside the window: read exactly the window, so no byte past it is read when a terminator lies inside it.
                    want = (int)Math.Min(chunkBytes, windowEnd - position);
                }
                else if (lastCut < 0)
                {
                    want = chunkBytes;
                }
                else if (lastWasCr && lastCut == position)
                {
                    want = width;
                }
                else
                {
                    return lastCut;
                }

                filled = ReadUnits(stream, buffer.AsSpan(0, want), width);
                index = 0;
                if (filled == 0)
                {
                    return lastCut >= 0 ? lastCut : total;
                }
            }

            var kind = Terminator(buffer.AsSpan(index, width), bigEndian);
            index += width;
            var past = position >= windowEnd;
            position += width;
            if (past && lastCut >= 0)
            {
                // Only the unit after a CR that ends the window is read here.
                return kind == '\n' ? position : lastCut;
            }

            if (kind != '\0')
            {
                lastCut = position;
                lastWasCr = kind == '\r';
                if (past && !lastWasCr)
                {
                    return lastCut;
                }
            }
        }
    }

    // '\n' or '\r' when the unit is that line terminator; 0 otherwise.
    private static char Terminator(ReadOnlySpan<byte> unit, bool bigEndian)
    {
        var low = bigEndian ? unit[^1] : unit[0];
        if (low != 0x0A && low != 0x0D)
        {
            return '\0';
        }

        for (var i = 0; i < unit.Length; i++)
        {
            var isLow = bigEndian ? i == unit.Length - 1 : i == 0;
            if (!isLow && unit[i] != 0)
            {
                return '\0';
            }
        }

        return low == 0x0A ? '\n' : '\r';
    }

    // Reads whole units; returns the bytes read, a multiple of the width, 0 at the end of the stream.
    private static int ReadUnits(Stream stream, Span<byte> destination, int width)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var n = stream.Read(destination[total..]);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total - (total % width);
    }
}
