// SPDX-License-Identifier: Apache-2.0
using System.Collections;
using System.Globalization;

namespace squalor.squalify;

/// <summary>
/// A string-keyed map of counts that is sorted when it is built, so the writer never depends on
/// dictionary enumeration order. Keys are unique. Two maps are equal when they hold the same keys (compared ordinally)
/// with the same counts, which is also the same order, as the keys are sorted.
/// </summary>
public sealed class CountMap : IReadOnlyList<KeyValuePair<string, long>>, IEquatable<CountMap>
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
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <exception cref="SqualifyException">Two entries have the same key.</exception>
    public static CountMap FromOrdinal(IEnumerable<KeyValuePair<string, long>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var array = entries.ToArray();
        Array.Sort(array, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        for (var i = 1; i < array.Length; i++)
        {
            if (string.Equals(array[i - 1].Key, array[i].Key, StringComparison.Ordinal))
            {
                throw new SqualifyException($"Duplicate key '{array[i].Key}' in a count map.");
            }
        }

        return new CountMap(array);
    }

    /// <summary>Builds a map whose keys are integers, sorted numerically ascending and written in invariant decimal form.</summary>
    /// <param name="entries">The entries, in any order. Keys must be unique.</param>
    /// <returns>The sorted map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <exception cref="SqualifyException">Two entries have the same key.</exception>
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
                throw new SqualifyException($"Duplicate key '{sorted[i].Key}' in a count map.");
            }

            array[i] = new(sorted[i].Key.ToString(CultureInfo.InvariantCulture), sorted[i].Value);
        }

        return new CountMap(array);
    }

    /// <summary>True when both are null, or hold the same keys and counts.</summary>
    /// <param name="left">The first map, or null.</param>
    /// <param name="right">The second map, or null.</param>
    /// <returns>Whether the maps are equal.</returns>
    public static bool operator ==(CountMap? left, CountMap? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>The negation of the equality operator.</summary>
    /// <param name="left">The first map, or null.</param>
    /// <param name="right">The second map, or null.</param>
    /// <returns>Whether the maps differ.</returns>
    public static bool operator !=(CountMap? left, CountMap? right)
    {
        return !(left == right);
    }

    /// <inheritdoc />
    public bool Equals(CountMap? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || other._entries.Length != _entries.Length)
        {
            return false;
        }

        for (var i = 0; i < _entries.Length; i++)
        {
            if (!string.Equals(_entries[i].Key, other._entries[i].Key, StringComparison.Ordinal) || _entries[i].Value != other._entries[i].Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as CountMap);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_entries.Length);
        foreach (var entry in _entries)
        {
            hash.Add(entry.Key, StringComparer.Ordinal);
            hash.Add(entry.Value);
        }

        return hash.ToHashCode();
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
