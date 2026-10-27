// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The kind of a line or record outside the data run.</summary>
internal enum LineKind
{
    /// <summary>Empty, or only spaces and tabs.</summary>
    Blank,

    /// <summary>The trimmed line starts with <c>#</c>, <c>!</c> or <c>//</c>.</summary>
    Comment,

    /// <summary><c>key=value</c> or <c>key: value</c> with a non-numeric key.</summary>
    KeyValue,

    /// <summary>Two fields whose first field is non-numeric.</summary>
    KeyDelimValue,

    /// <summary>Anything else.</summary>
    Text,
}

/// <summary>The classification of one record.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Key">The normalized key, for the two key kinds.</param>
/// <param name="Value">The value, for the two key kinds.</param>
internal readonly record struct LineClassification(LineKind Kind, string? Key, string? Value);

/// <summary>
/// Classifies a record that lies outside the data run, for the line-kind counts and the preamble keys. It uses fixed
/// character sets and the ASCII-only <see cref="ValueClassifier"/>, so the result does not depend on the runtime's
/// Unicode tables or the culture. The kinds are tested in order: blank, comment, keyValue, keyDelimValue, text.
/// </summary>
internal static class LineClassifier
{
    private const int MaxKeyLength = 64;

    /// <summary>The kind name used in <c>layout.regions[].lineKinds</c>.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name.</returns>
    public static string Name(LineKind kind)
    {
        return kind switch
        {
            LineKind.Blank => "blank",
            LineKind.Comment => "comment",
            LineKind.KeyValue => "keyValue",
            LineKind.KeyDelimValue => "keyDelimValue",
            _ => "text",
        };
    }

    /// <summary>Whether a record's text is a comment line: trimmed, it starts with <c>#</c>, <c>!</c> or <c>//</c>.</summary>
    /// <param name="raw">The raw text of the record (or its start).</param>
    /// <returns>True for a comment.</returns>
    public static bool IsComment(string raw)
    {
        var text = ValueClassifier.Trim(raw);
        return text.Length > 0 && (text[0] is '#' or '!' || text.StartsWith("//", StringComparison.Ordinal));
    }

    /// <summary>Classifies a record.</summary>
    /// <param name="raw">The raw text of the record, line break excluded.</param>
    /// <param name="cells">The fields of the record under the chosen delimiter.</param>
    /// <param name="delimiter">The delimiter character, or NUL for the whitespace-run candidate.</param>
    /// <returns>The classification.</returns>
    public static LineClassification Classify(string raw, IReadOnlyList<string> cells, char delimiter)
    {
        var text = ValueClassifier.Trim(raw);
        if (text.IsEmpty)
        {
            return new LineClassification(LineKind.Blank, null, null);
        }

        if (IsComment(raw))
        {
            return new LineClassification(LineKind.Comment, null, null);
        }

        // key<padding>:,value and key=,value: two fields, the first ending in the separator.
        if (cells.Count == 2)
        {
            var first = ValueClassifier.Trim(cells[0]);
            if (first.Length > 1 && first[^1] is ':' or '=')
            {
                var key = NormalizeKey(first);
                if (key.Length > 0 && !IsNumeric(key))
                {
                    return new LineClassification(LineKind.KeyValue, key, ValueClassifier.Trim(cells[1]).ToString());
                }
            }
        }

        var separator = text.IndexOfAny('=', ':');
        if (separator > 0)
        {
            var key = ValueClassifier.Trim(text[..separator]);
            if (key.Length is >= 1 and <= MaxKeyLength && !IsNumeric(key.ToString()))
            {
                var value = ValueClassifier.Trim(text[(separator + 1)..]);
                if (delimiter != '\0')
                {
                    // Excel-style key:,value,,, lines: drop the delimiter that follows the separator and trailing ones.
                    if (value.Length > 0 && value[0] == delimiter)
                    {
                        value = ValueClassifier.Trim(value[1..]);
                    }

                    value = ValueClassifier.Trim(value.TrimEnd(delimiter));
                }

                return new LineClassification(LineKind.KeyValue, NormalizeKey(key), value.ToString());
            }
        }

        if (cells.Count == 2)
        {
            var key = NormalizeKey(ValueClassifier.Trim(cells[0]));
            if (key.Length > 0 && !IsNumeric(key))
            {
                return new LineClassification(LineKind.KeyDelimValue, key, ValueClassifier.Trim(cells[1]).ToString());
            }
        }

        return new LineClassification(LineKind.Text, null, null);
    }

    /// <summary>Normalizes a key or label: trimmed, with one trailing <c>:</c> or <c>=</c> removed, then trimmed again.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The normalized text.</returns>
    public static string NormalizeKey(ReadOnlySpan<char> text)
    {
        var trimmed = ValueClassifier.Trim(text);
        if (trimmed.Length > 0 && trimmed[^1] is ':' or '=')
        {
            trimmed = ValueClassifier.Trim(trimmed[..^1]);
        }

        return trimmed.ToString();
    }

    /// <summary>Whether a text is a number under either decimal separator.</summary>
    /// <param name="text">The text.</param>
    /// <returns>True when it votes <c>int</c> or <c>float</c>.</returns>
    public static bool IsNumeric(string text)
    {
        return ValueClassifier.Classify(text, '.').Vote is ValueVote.Int or ValueVote.Float
            || ValueClassifier.Classify(text, ',').Vote is ValueVote.Int or ValueVote.Float;
    }
}
