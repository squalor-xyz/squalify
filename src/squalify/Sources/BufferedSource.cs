// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Opens an inner source once and keeps every byte any pass reads in one buffer, so the passes that re-read the start of the
/// source (the first-record check, the sniffs, the search for the cut, the profile itself) share one read. A stream returned by
/// <see cref="Open"/> starts at byte 0, reads what the buffer already holds and pulls the rest from the inner stream, asking it
/// for exactly the bytes that are missing, so the buffer never holds a byte that no pass asked for. It never reads past the
/// source's declared <see cref="IProfileSource.Length"/>: bytes beyond it are not part of the file as the source declares it.
/// Not thread-safe: the passes read one after another.
/// </summary>
internal sealed class BufferedSource : IProfileSource, IDisposable
{
    private const int InitialCapacity = 64 * 1024;

    // The smallest step by which the buffer grows past the expected size.
    private const int OvershootStep = 4096;

    private readonly IProfileSource _inner;
    private readonly long _expected;
    private readonly long _declared;
    private Stream? _stream;
    private byte[] _buffer = [];
    private int _count;
    private bool _ended;

    /// <summary>Creates a buffered view of a source.</summary>
    /// <param name="inner">The source; it is opened at most once, when the first byte is needed.</param>
    /// <param name="expected">The number of bytes the passes are expected to need, which caps the buffer's growth steps; more can still be read.</param>
    public BufferedSource(IProfileSource inner, long expected)
    {
        _inner = inner;
        _expected = expected;
        _declared = inner.Length;
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public long Length => _declared;

    /// <summary>The buffered bytes, from byte 0.</summary>
    /// <param name="length">How many; at most the number of bytes read so far.</param>
    /// <returns>The bytes, backed by the buffer; call this only when no pass reads any more.</returns>
    public ReadOnlyMemory<byte> Prefix(int length) => new(_buffer, 0, length);

    /// <inheritdoc />
    public Stream Open() => new View(this);

    /// <inheritdoc />
    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }

    // Makes bytes [0, end) available, as far as the inner stream has them; returns how many are buffered.
    private int Fill(int end)
    {
        end = (int)Math.Min(end, Math.Min(_declared, Array.MaxLength));
        if (end <= _count || _ended)
        {
            return _count;
        }

        Grow(end);
        _stream ??= _inner.Open();
        while (_count < end)
        {
            var n = _stream.Read(_buffer.AsSpan(_count, end - _count));
            if (n == 0)
            {
                _ended = true;
                break;
            }

            _count += n;
        }

        return _count;
    }

    // Inside the expected size the capacity doubles, but never past it. Past it (the overshoot: the LF after a CR that ends the
    // window, or the rest of a long first record) only the part past the expected size doubles, by at least OvershootStep, so
    // a one-byte overshoot costs a few KiB and not a copy of the whole window. Never past the declared length.
    private void Grow(int needed)
    {
        if (needed <= _buffer.Length)
        {
            return;
        }

        long capacity;
        if (needed <= _expected)
        {
            capacity = Math.Min(Math.Max(needed, Math.Max((long)_buffer.Length * 2, InitialCapacity)), _expected);
        }
        else
        {
            var pastNow = Math.Max(0L, _buffer.Length - _expected);
            capacity = _expected + Math.Max(Math.Max(needed - _expected, pastNow * 2), OvershootStep);
        }

        capacity = Math.Min(capacity, Math.Min(_declared, Array.MaxLength));
        Array.Resize(ref _buffer, (int)Math.Max(capacity, needed));
    }

    private sealed class View(BufferedSource owner) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => owner.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> destination)
        {
            if (destination.Length == 0)
            {
                return 0;
            }

            var end = (int)Math.Min((long)_position + destination.Length, Array.MaxLength);
            var available = owner.Fill(end);
            var n = Math.Min(available - _position, destination.Length);
            if (n <= 0)
            {
                return 0;
            }

            owner._buffer.AsSpan(_position, n).CopyTo(destination);
            _position += n;
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
