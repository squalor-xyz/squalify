// SPDX-License-Identifier: Apache-2.0
using System.Buffers;
using System.Buffers.Binary;

namespace squalor.squalify;

/// <summary>
/// A bounded count of exact spellings (raw text, never trimmed): the first <see cref="MaxSpellings"/> distinct spellings
/// of at most <see cref="MaxLength"/> characters are kept (first seen first; the keys are written in ordinal order), and any other new spelling is counted in
/// <see cref="Other"/>; a spelling already kept keeps counting. It serves a field's <c>nullSpellings</c> and the table's
/// <c>allNullRowSpellings</c>, so the bounds are the same. A dropped spelling is also remembered, as a 64-bit hash, for the
/// first <see cref="MaxDistinctOther"/> distinct ones, so that <see cref="OtherDistinct"/> says how many different spellings
/// <see cref="Other"/> holds (exact up to hash collisions); a further new one sets <see cref="OtherDistinctCapped"/> and is not remembered.
/// </summary>
internal sealed class SpellingCounter
{
    /// <summary>The most distinct spellings kept.</summary>
    public const int MaxSpellings = 64;

    /// <summary>The longest spelling kept, in characters (fixed-width exports pad a blank to the column width).</summary>
    public const int MaxLength = 256;

    /// <summary>The most distinct dropped spellings counted exactly in <see cref="OtherDistinct"/>; one more sets <see cref="OtherDistinctCapped"/>.</summary>
    public const int MaxDistinctOther = 64;

    // The bytes of one hash and the fixed cost of the array that holds them.
    private const int HashBytes = 8;
    private const int ArrayOverheadBytes = 24;

    // A text of at most this many characters is encoded on the stack to hash it; a longer one rents a buffer.
    private const int StackChars = 256;

    private readonly Dictionary<string, long> _counts = new(StringComparer.Ordinal);
    private long _chars;
    private ulong[]? _otherHashes;
    private int _otherDistinct;

    /// <summary>Gets the number of spellings counted that are not a key, because of a bound.</summary>
    public long Other { get; private set; }

    /// <summary>
    /// Gets the number of distinct spellings among those counted in <see cref="Other"/>, each once however often it recurs
    /// (compared as exact text; exact up to 64-bit hash collisions) up to <see cref="MaxDistinctOther"/>; 0 when <see cref="Other"/> is 0.
    /// </summary>
    public long OtherDistinct => _otherDistinct;

    /// <summary>Gets a value indicating whether more than <see cref="MaxDistinctOther"/> distinct spellings were counted in <see cref="Other"/>, so <see cref="OtherDistinct"/> is a lower bound.</summary>
    public bool OtherDistinctCapped { get; private set; }

    /// <summary>An estimate of the memory held: 100 bytes an entry and two bytes a character of the kept keys, and 8 bytes a remembered hash (the array that holds them, by its capacity).</summary>
    public long EstimatedBytes => (_counts.Count * 100L) + (_chars * 2) + (_otherHashes is null ? 0 : ArrayOverheadBytes + ((long)_otherHashes.Length * HashBytes));

    /// <summary>Counts one spelling.</summary>
    /// <param name="raw">The raw text.</param>
    public void Add(ReadOnlySpan<char> raw)
    {
        var lookup = _counts.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(raw, out var count))
        {
            lookup[raw] = count + 1;
        }
        else if (raw.Length <= MaxLength && _counts.Count < MaxSpellings)
        {
            lookup[raw] = 1;
            _chars += raw.Length;
        }
        else
        {
            Other++;
            RememberDropped(raw);
        }
    }

    /// <summary>Builds the map of kept spellings, sorted ordinally.</summary>
    /// <returns>The map; empty when nothing was kept.</returns>
    public CountMap Build()
    {
        return CountMap.FromOrdinal(_counts);
    }

    /// <summary>
    /// The key of a spelling: the <see cref="XxHash64"/> (seed: the length in characters) of its UTF-16 code units, each written
    /// as two bytes, low byte first, on every platform. It is of the exact text, so a lone surrogate is itself and not U+FFFD.
    /// </summary>
    /// <param name="raw">The text.</param>
    /// <returns>The 64-bit key.</returns>
    internal static ulong KeyOf(ReadOnlySpan<char> raw)
    {
        var byteCount = raw.Length * 2;
        byte[]? rented = null;
        Span<byte> bytes = raw.Length <= StackChars ? stackalloc byte[StackChars * 2] : (rented = ArrayPool<byte>.Shared.Rent(byteCount));
        try
        {
            for (var i = 0; i < raw.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes[(i * 2)..], raw[i]);
            }

            return XxHash64.Hash(bytes[..byteCount], (ulong)raw.Length);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    // A dropped spelling that is new takes one of the MaxDistinctOther places; one that finds none sets the marker, and
    // after that nothing more is remembered or hashed.
    private void RememberDropped(ReadOnlySpan<char> raw)
    {
        if (OtherDistinctCapped)
        {
            return;
        }

        var key = KeyOf(raw);
        var known = _otherHashes is { } hashes && new ReadOnlySpan<ulong>(hashes, 0, _otherDistinct).Contains(key);
        if (known)
        {
            return;
        }

        if (_otherDistinct == MaxDistinctOther)
        {
            OtherDistinctCapped = true;
            return;
        }

        if (_otherHashes is null)
        {
            _otherHashes = new ulong[4];
        }
        else if (_otherDistinct == _otherHashes.Length)
        {
            Array.Resize(ref _otherHashes, _otherHashes.Length * 2);
        }

        _otherHashes[_otherDistinct++] = key;
    }
}
