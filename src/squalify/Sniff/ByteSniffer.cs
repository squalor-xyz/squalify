// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Unicode;

namespace squalor.squalify;

/// <summary>Pass 0: one streaming pass over the whole file that counts everything the text-or-binary decision needs.</summary>
internal static class ByteSniffer
{
    private const int BufferSize = 64 * 1024;
    private const int MaxCarry = 3;
    private const int MaxInvalidOffsets = 10;

    /// <summary>Reads the source once and decides whether it is empty, text or binary.</summary>
    /// <param name="source">The source.</param>
    /// <param name="tolerance">The tolerated stray invalid UTF-8 sequences.</param>
    /// <returns>The sniff result.</returns>
    /// <exception cref="SqualifyException">The source cannot be read.</exception>
    public static SniffResult Sniff(IProfileSource source, CharsetTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tolerance);
        return SniffResult.Decide(Scan(source), tolerance);
    }

    private static SniffFacts Scan(IProfileSource source)
    {
        var histogram = new long[256];
        long nulEven = 0;
        long nulOdd = 0;
        var head = new byte[MagicTable.HeadLength];
        var headLength = 0;
        var tail = new byte[MagicTable.TailLength];
        var tailLength = 0;
        var utf8 = new Utf8Counter();
        var buffer = new byte[MaxCarry + BufferSize];
        var carry = 0;
        long offset = 0;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = source.Open();
        try
        {
            while (true)
            {
                var read = ReadFully(stream, buffer.AsSpan(carry, BufferSize));
                if (read == 0)
                {
                    break;
                }

                var chunk = buffer.AsSpan(carry, read);
                hash.AppendData(chunk);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var b = chunk[i];
                    histogram[b]++;
                    if (b == 0)
                    {
                        if (((offset + i) & 1) == 0)
                        {
                            nulEven++;
                        }
                        else
                        {
                            nulOdd++;
                        }
                    }
                }

                if (headLength < head.Length)
                {
                    var take = Math.Min(head.Length - headLength, chunk.Length);
                    chunk[..take].CopyTo(head.AsSpan(headLength));
                    headLength += take;
                }

                tailLength = KeepTail(tail, tailLength, chunk);

                // The carried bytes sit in front of the new bytes, so a sequence that straddles two buffers is decoded whole.
                var total = carry + read;
                var newCarry = utf8.Feed(buffer.AsSpan(0, total), offset - carry, isFinal: false);
                buffer.AsSpan(total - newCarry, newCarry).CopyTo(buffer);
                carry = newCarry;
                offset += read;
            }

            if (carry > 0)
            {
                utf8.Feed(buffer.AsSpan(0, carry), offset - carry, isFinal: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SqualifyException($"Cannot read '{source.Name}': {ex.Message}", ex);
        }

        var headSpan = head.AsSpan(0, headLength);
        var tailSpan = tail.AsSpan(0, tailLength);
        var (bom, bomLength) = DetectBom(headSpan);
        var magic = MagicTable.Find(headSpan, tailSpan, offset);
        return new SniffFacts(
            Length: offset,
            Sha256: Convert.ToHexStringLower(hash.GetHashAndReset()),
            Histogram: histogram,
            NulEven: nulEven,
            NulOdd: nulOdd,
            ControlBytes: Sum(histogram, 0x00, 0x08) + Sum(histogram, 0x0E, 0x1F) + histogram[0x7F],
            HighBytes: Sum(histogram, 0x80, 0xFF),
            C1Bytes: Sum(histogram, 0x80, 0x9F),
            InvalidSequences: utf8.Sequences,
            InvalidBytes: utf8.Bytes,
            InvalidOffsets: utf8.Offsets.ToArray(),
            Bom: bom,
            BomLength: bomLength,
            Magic: magic?.Name,
            MagicDetail: magic is null ? null : MagicTable.Describe(magic));
    }

    private static int ReadFully(Stream stream, Span<byte> destination)
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

        return total;
    }

    // Keeps the last TailLength bytes seen so far.
    private static int KeepTail(byte[] tail, int tailLength, ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length >= tail.Length)
        {
            chunk[^tail.Length..].CopyTo(tail);
            return tail.Length;
        }

        var keep = Math.Min(tailLength, tail.Length - chunk.Length);
        tail.AsSpan(tailLength - keep, keep).CopyTo(tail);
        chunk.CopyTo(tail.AsSpan(keep));
        return keep + chunk.Length;
    }

    private static long Sum(long[] histogram, int first, int last)
    {
        long total = 0;
        for (var i = first; i <= last; i++)
        {
            total += histogram[i];
        }

        return total;
    }

    private static (string? Name, int Length) DetectBom(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            return ("utf-32le", 4);
        }

        if (head.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            return ("utf-32be", 4);
        }

        if (head.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return ("utf-8", 3);
        }

        if (head.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return ("utf-16le", 2);
        }

        if (head.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return ("utf-16be", 2);
        }

        return (null, 0);
    }

    /// <summary>Strict UTF-8 validation across buffers: counts invalid sequences and remembers where the first ones start.</summary>
    private sealed class Utf8Counter
    {
        private readonly char[] _scratch = new char[BufferSize + MaxCarry];

        public long Sequences { get; private set; }

        public long Bytes { get; private set; }

        public List<long> Offsets { get; } = new();

        /// <summary>Validates a buffer.</summary>
        /// <param name="data">The bytes, starting with any carried-over bytes.</param>
        /// <param name="baseOffset">The file offset of <paramref name="data"/>[0].</param>
        /// <param name="isFinal">True for the last buffer.</param>
        /// <returns>The number of trailing bytes (at most 3) that start an incomplete sequence and must be carried over.</returns>
        public int Feed(ReadOnlySpan<byte> data, long baseOffset, bool isFinal)
        {
            var position = 0;
            while (position < data.Length)
            {
                var status = Utf8.ToUtf16(data[position..], _scratch, out var read, out _, replaceInvalidSequences: false, isFinalBlock: isFinal);
                position += read;
                switch (status)
                {
                    case System.Buffers.OperationStatus.Done:
                        return 0;
                    case System.Buffers.OperationStatus.NeedMoreData:
                        return data.Length - position;
                    case System.Buffers.OperationStatus.InvalidData:
                        Rune.DecodeFromUtf8(data[position..], out _, out var consumed);
                        consumed = Math.Max(consumed, 1);
                        Sequences++;
                        Bytes += consumed;
                        if (Offsets.Count < MaxInvalidOffsets)
                        {
                            Offsets.Add(baseOffset + position);
                        }

                        position += consumed;
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected UTF-8 decoder status " + status + ".");
                }
            }

            return 0;
        }
    }
}
