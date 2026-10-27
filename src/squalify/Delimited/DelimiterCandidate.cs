// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>One delimiter candidate of the delimited profiler.</summary>
internal enum DelimiterKind
{
    /// <summary><c>,</c></summary>
    Comma,

    /// <summary>A tab.</summary>
    Tab,

    /// <summary><c>;</c></summary>
    Semicolon,

    /// <summary><c>|</c></summary>
    Pipe,

    /// <summary>Runs of white space; leading and trailing white space is trimmed.</summary>
    Whitespace,
}

/// <summary>The fixed list of delimiter candidates, in candidate order (the order that breaks score ties last).</summary>
internal static class DelimiterCandidates
{
    /// <summary>The value written to <c>format.dialect.delimiter</c> for the whitespace-run candidate.</summary>
    public const string WhitespaceName = "whitespace";

    /// <summary>The value written to <c>format.dialect.delimiter</c> for a one-column table, where no delimiter occurs.</summary>
    public const string NoneName = "none";

    /// <summary>The candidates in candidate order: comma, tab, semicolon, pipe, whitespace run.</summary>
    public static IReadOnlyList<DelimiterKind> All { get; } =
        [DelimiterKind.Comma, DelimiterKind.Tab, DelimiterKind.Semicolon, DelimiterKind.Pipe, DelimiterKind.Whitespace];

    /// <summary>The delimiter text of a candidate, as written to the descriptor.</summary>
    /// <param name="kind">The candidate.</param>
    /// <returns>The character, or <c>whitespace</c> for the whitespace-run candidate.</returns>
    public static string Text(DelimiterKind kind)
    {
        return kind switch
        {
            DelimiterKind.Comma => ",",
            DelimiterKind.Tab => "\t",
            DelimiterKind.Semicolon => ";",
            DelimiterKind.Pipe => "|",
            _ => WhitespaceName,
        };
    }

    /// <summary>A plain word for a candidate, for evidence reasons.</summary>
    /// <param name="kind">The candidate.</param>
    /// <returns>The word.</returns>
    public static string Word(DelimiterKind kind)
    {
        return kind switch
        {
            DelimiterKind.Comma => "comma",
            DelimiterKind.Tab => "tab",
            DelimiterKind.Semicolon => "semicolon",
            DelimiterKind.Pipe => "pipe",
            _ => "whitespace",
        };
    }

    /// <summary>The delimiter character of a candidate, or NUL for the whitespace-run candidate.</summary>
    /// <param name="kind">The candidate.</param>
    /// <returns>The character.</returns>
    public static char Char(DelimiterKind kind)
    {
        return kind switch
        {
            DelimiterKind.Comma => ',',
            DelimiterKind.Tab => '\t',
            DelimiterKind.Semicolon => ';',
            DelimiterKind.Pipe => '|',
            _ => '\0',
        };
    }
}
