// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// A bounded count of exact spellings (raw text, never trimmed): the first <see cref="MaxSpellings"/> distinct spellings
/// of at most <see cref="MaxLength"/> characters are kept (first seen first; the keys are written in ordinal order), and any other new spelling is counted in
/// <see cref="Other"/>; a spelling already kept keeps counting. It serves a field's <c>nullSpellings</c> and the table's
/// <c>allNullRowSpellings</c>, so the two bounds are the same.
/// </summary>
internal sealed class SpellingCounter
{
    /// <summary>The most distinct spellings kept.</summary>
    public const int MaxSpellings = 64;

    /// <summary>The longest spelling kept, in characters (fixed-width exports pad a blank to the column width).</summary>
    public const int MaxLength = 256;

    private readonly Dictionary<string, long> _counts = new(StringComparer.Ordinal);
    private long _chars;

    /// <summary>Gets the number of spellings counted that are not a key, because of a bound.</summary>
    public long Other { get; private set; }

    /// <summary>An estimate of the memory held: 100 bytes an entry and two bytes a character of the kept keys.</summary>
    public long EstimatedBytes => (_counts.Count * 100L) + (_chars * 2);

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
        }
    }

    /// <summary>Builds the map of kept spellings, sorted ordinally.</summary>
    /// <returns>The map; empty when nothing was kept.</returns>
    public CountMap Build()
    {
        return CountMap.FromOrdinal(_counts);
    }
}
