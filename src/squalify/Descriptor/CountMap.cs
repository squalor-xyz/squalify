// SPDX-License-Identifier: Apache-2.0
using System.Collections;
using System.Globalization;

namespace squalor.squalify;

/// <summary>
/// A string-keyed map of counts that is sorted when it is built, so the writer never depends on
/// dictionary enumeration order. Keys are unique.
/// </summary>
public sealed class CountMap : IReadOnlyList<KeyValuePair<string, long>>
{
    private readonly KeyValuePair<string, long>[] _entries;

    private CountMap(KeyValuePair<string, long>[] entries)
    {
        _entries = entries;
    }

    /// <summary>A map with no entries.</summary>
    public static CountMap Empty { get; } = new(Array.Empty<KeyValuePair<string, long>>());

    /// <summary>Number of entries.</summary>
    public int Count => _entries.Length;

    /// <summary>Gets the entry at a position in key order.</summary>
    /// <param name="index">Zero-based position.</param>
    public KeyValuePair<string, long> this[int index] => _entries[index];

    /// <summary>Builds a map sorted by key, ordinally (UTF-16 code unit order).</summary>
    /// <param name="entries">The entries, in any order. Keys must be unique.</param>
    /// <returns>The sorted map.</returns>
    public static CountMap FromOrdinal(IEnumerable<KeyValuePair<string, long>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var array = entries.ToArray();
        Array.Sort(array, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        for (var i = 1; i < array.Length; i++)
        {
            if (string.Equals(array[i - 1].Key, array[i].Key, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Duplicate key '{array[i].Key}'.", nameof(entries));
            }
        }

        return new CountMap(array);
    }

    /// <summary>Builds a map whose keys are integers, sorted numerically ascending and written in invariant decimal form.</summary>
    /// <param name="entries">The entries, in any order. Keys must be unique.</param>
    /// <returns>The sorted map.</returns>
    public static CountMap FromNumeric(IEnumerable<KeyValuePair<long, long>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sorted = entries.ToArray();
        Array.Sort(sorted, static (a, b) => a.Key.CompareTo(b.Key));
        var array = new KeyValuePair<string, long>[sorted.Length];
        for (var i = 0; i < sorted.Length; i++)
        {
            if (i > 0 && sorted[i - 1].Key == sorted[i].Key)
            {
                throw new ArgumentException($"Duplicate key '{sorted[i].Key}'.", nameof(entries));
            }

            array[i] = new(sorted[i].Key.ToString(CultureInfo.InvariantCulture), sorted[i].Value);
        }

        return new CountMap(array);
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, long>> GetEnumerator()
    {
        return ((IEnumerable<KeyValuePair<string, long>>)_entries).GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
