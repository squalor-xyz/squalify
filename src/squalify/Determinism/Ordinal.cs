// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace squalor.squalify;

/// <summary>
/// Culture-free case folding and sorting. Every sort that reaches a descriptor uses these helpers, so the
/// order is the UTF-16 code unit order of <see cref="string.CompareOrdinal(string, string)"/> on every machine.
/// </summary>
internal static class Ordinal
{
    /// <summary>Case-folds a name with <see cref="string.ToLowerInvariant"/>.</summary>
    /// <param name="value">The text to fold.</param>
    /// <returns>The folded text.</returns>
    public static string CaseFold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.ToLowerInvariant();
    }

    /// <summary>Folds the ASCII letters A to Z to lower case and leaves every other character as it is.</summary>
    /// <param name="value">The text to fold.</param>
    /// <returns>The folded text.</returns>
    public static string AsciiFold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var first = -1;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is >= 'A' and <= 'Z')
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            return value;
        }

        var chars = value.ToCharArray();
        for (var i = first; i < chars.Length; i++)
        {
            if (chars[i] is >= 'A' and <= 'Z')
            {
                chars[i] = (char)(chars[i] + ('a' - 'A'));
            }
        }

        return new string(chars);
    }

    /// <summary>Compares two strings ordinally (UTF-16 code unit order).</summary>
    /// <param name="left">The first string.</param>
    /// <param name="right">The second string.</param>
    /// <returns>A negative, zero or positive number.</returns>
    public static int Compare(string? left, string? right)
    {
        return string.CompareOrdinal(left, right);
    }

    /// <summary>Returns the strings sorted ordinally. Equal strings stay in input order.</summary>
    /// <param name="values">The strings, in any order.</param>
    /// <returns>A new sorted list.</returns>
    public static IReadOnlyList<string> Sorted(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.OrderBy(static v => v, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Returns the items sorted ordinally by a string key. Items with equal keys stay in input order.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items, in any order.</param>
    /// <param name="key">Selects the sort key.</param>
    /// <returns>A new sorted list.</returns>
    public static IReadOnlyList<T> SortedBy<T>(IEnumerable<T> items, Func<T, string> key)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);
        return items.OrderBy(key, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Formats an integer in invariant decimal form.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>The text.</returns>
    public static string Invariant(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
