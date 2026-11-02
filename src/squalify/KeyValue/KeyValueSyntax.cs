// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>What a line of a key=value document is.</summary>
internal enum KeyValueLineKind
{
    /// <summary>Empty, or only spaces and tabs.</summary>
    Blank,

    /// <summary>The trimmed line starts with <c>#</c>, <c>;</c>, <c>!</c> or <c>//</c>.</summary>
    Comment,

    /// <summary>A <c>[section]</c> header.</summary>
    Section,

    /// <summary>A <c>key=value</c> or <c>key: value</c> pair.</summary>
    Pair,

    /// <summary>Anything else.</summary>
    Other,
}

/// <summary>The classification of one line of a key=value document.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Name">The section name of a <see cref="KeyValueLineKind.Section"/>, or the key of a <see cref="KeyValueLineKind.Pair"/>.</param>
/// <param name="Value">The value of a pair, trimmed.</param>
/// <param name="Separator">The separator of a pair, <c>=</c> or <c>:</c>.</param>
internal readonly record struct KeyValueLine(KeyValueLineKind Kind, string? Name, string? Value, char Separator);

/// <summary>
/// The syntax of whole-file key=value and INI documents. A key starts with a letter (ASCII, any character above U+007F, or <c>_</c>), holds at
/// most 64 characters, and holds no control character, no <c>,</c> and neither separator; so a line of a table (<c>1,2,3</c>), a time stamp
/// (<c>12:30:00</c>) or a number is not a pair. Everything is ASCII-defined and does not use the runtime's Unicode tables.
/// </summary>
internal static class KeyValueSyntax
{
    /// <summary>The longest key, in characters.</summary>
    public const int MaxKeyLength = 64;

    /// <summary>Classifies a line.</summary>
    /// <param name="line">The line, line break excluded.</param>
    /// <returns>The classification.</returns>
    public static KeyValueLine Classify(string line)
    {
        var text = ValueClassifier.Trim(line);
        if (text.IsEmpty)
        {
            return new KeyValueLine(KeyValueLineKind.Blank, null, null, '\0');
        }

        if (text[0] is '#' or ';' or '!' || text.StartsWith("//", StringComparison.Ordinal))
        {
            return new KeyValueLine(KeyValueLineKind.Comment, null, null, '\0');
        }

        if (text[0] == '[' && text[^1] == ']')
        {
            var name = ValueClassifier.Trim(text[1..^1]);
            if (name.Length > 0 && name.Length <= MaxKeyLength && name.IndexOfAny('[', ']') < 0)
            {
                return new KeyValueLine(KeyValueLineKind.Section, name.ToString(), null, '\0');
            }

            return new KeyValueLine(KeyValueLineKind.Other, null, null, '\0');
        }

        var at = text.IndexOfAny('=', ':');
        if (at > 0)
        {
            var key = ValueClassifier.Trim(text[..at]);
            if (IsKey(key))
            {
                return new KeyValueLine(KeyValueLineKind.Pair, key.ToString(), ValueClassifier.Trim(text[(at + 1)..]).ToString(), text[at]);
            }
        }

        return new KeyValueLine(KeyValueLineKind.Other, null, null, '\0');
    }

    /// <summary>Whether a text may be a key.</summary>
    /// <param name="key">The trimmed text before the separator.</param>
    /// <returns>True for a key.</returns>
    public static bool IsKey(ReadOnlySpan<char> key)
    {
        if (key.Length is 0 or > MaxKeyLength)
        {
            return false;
        }

        var first = key[0];
        if (!(first is >= 'A' and <= 'Z' || first is >= 'a' and <= 'z' || first == '_' || first > '\u007F'))
        {
            return false;
        }

        foreach (var c in key)
        {
            if (c < ' ' || c == '\u007F' || c == ',')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The number of fields of a line under a delimiter, with no quoting: the delimiters plus one, or the runs of white space for <c>\0</c>.</summary>
    /// <param name="line">The line.</param>
    /// <param name="delimiter">The delimiter, or <c>\0</c> for runs of white space.</param>
    /// <returns>The field count.</returns>
    public static int FieldCount(string line, char delimiter)
    {
        var text = ValueClassifier.Trim(line);
        if (delimiter != '\0')
        {
            return text.Count(delimiter) + 1;
        }

        var count = 0;
        var inside = false;
        foreach (var c in text)
        {
            var space = c is ' ' or '\t' or '\u00A0' or '\v' or '\f';
            if (!space && !inside)
            {
                count++;
            }

            inside = !space;
        }

        return count;
    }
}
