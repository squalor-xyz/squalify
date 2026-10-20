// SPDX-License-Identifier: Apache-2.0
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>An in-memory source.</summary>
internal sealed class BytesSource(string name, byte[] bytes) : IProfileSource
{
    public string Name { get; } = name;

    public long Length => bytes.Length;

    public Stream Open()
    {
        return new MemoryStream(bytes, writable: false);
    }
}

/// <summary>A source that generates its bytes on the fly, so large inputs need no memory or disk.</summary>
internal sealed class GeneratedSource(string name, long length, Func<long, byte> byteAt) : IProfileSource
{
    public string Name { get; } = name;

    public long Length => length;

    public Stream Open()
    {
        return new GeneratedStream(length, byteAt);
    }

    private sealed class GeneratedStream(long length, Func<long, byte> byteAt) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Deliberately return short reads, to prove that the sniffer fills its buffer.
            var n = (int)Math.Min(Math.Min(count, 4093), length - _position);
            for (var i = 0; i < n; i++)
            {
                buffer[offset + i] = byteAt(_position + i);
            }

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

/// <summary>Helpers for building test bytes and locating fixtures.</summary>
internal static class TestBytes
{
    public static byte[] Hex(string hex)
    {
        return Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));
    }

    public static byte[] Ascii(string text)
    {
        return System.Text.Encoding.ASCII.GetBytes(text);
    }

    public static byte[] Concat(params byte[][] parts)
    {
        return parts.SelectMany(static p => p).ToArray();
    }

    /// <summary>The full path of a fixture under <c>tests/squalify.Tests/fixtures</c>.</summary>
    public static string Fixture(string relative)
    {
        var source = Path.Combine(Golden.SourceFixturesRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(source) ? source : Path.Combine(AppContext.BaseDirectory, "fixtures", relative.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string ProfileText(IProfileSource source, bool includeValues = false)
    {
        var descriptor = Profiler.Profile(source, ProfileOptions.Create(includeValues, Array.Empty<string>()));
        return System.Text.Encoding.UTF8.GetString(DescriptorWriter.ToBytes(descriptor));
    }
}
