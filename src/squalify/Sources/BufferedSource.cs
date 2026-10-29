// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Opens an inner source once and keeps every byte any pass reads in one buffer, so the passes that re-read the start of the
/// source (the first-record check, the sniffs, the search for the cut, the profile itself) share one read. A stream returned by
/// <see cref="Open"/> starts at byte 0, reads what the buffer already holds and pulls the rest from the inner stream, asking it
/// for exactly the bytes that are missing, so the buffer never holds a byte that no pass asked for. Not thread-safe: the passes
/// read one after another.
/// </summary>
internal sealed class BufferedSource : IProfileSource, IDisposable
{
    private const int InitialCapacity = 64 * 1024;

    private readonly IProfileSource _inner;
    private readonly long _expected;
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
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public long Length => _inner.Length;

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

    // Capacity grows by doubling, but never past what the passes are expected to need unless they read further.
    private void Grow(int needed)
    {
        if (needed <= _buffer.Length)
        {
            return;
        }

        var ceiling = needed <= _expected ? _expected : Math.Max(_inner.Length, needed);
        var doubled = Math.Max((long)_buffer.Length * 2, InitialCapacity);
        var capacity = (int)Math.Min(Math.Max(needed, doubled), Math.Min(ceiling, Array.MaxLength));
        capacity = Math.Max(capacity, needed);
        Array.Resize(ref _buffer, capacity);
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
