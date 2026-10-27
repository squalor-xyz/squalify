// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.RegularExpressions;

namespace squalor.squalify;

/// <summary>
/// Builds the regexes of <c>hints.stableLines</c> so that they compile and match the same under .NET and ECMAScript.
/// A literal text is escaped by putting a backslash before each of <c>\ . ^ $ | ? * + ( ) [ ] { }</c> and nothing else;
/// the only other regex syntax used is <c>^</c>, <c>$</c>, <c>\s</c> (an ASCII white-space class in ECMAScript, so a
/// regex that needs a non-ASCII space does not match there and is left out), <c>*</c>, <c>+</c>, <c>?</c> and the class
/// <c>[=:]</c>. See <c>docs/design.md</c>, "Detect-rule hints".
/// </summary>
internal static class PortableRegex
{
    private const string Meta = "\\.^$|?*+()[]{}";

    /// <summary>Escapes a literal text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with <c>\.^$|?*+()[]{}</c> escaped.</returns>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length + 16);
        AppendEscaped(builder, text);
        return builder.ToString();
    }

    /// <summary>The anchored regex of a whole line.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The regex: <c>^</c>, the escaped line, <c>$</c>.</returns>
    public static string Line(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var builder = new StringBuilder(line.Length + 18);
        builder.Append('^');
        AppendEscaped(builder, line);
        return builder.Append('$').ToString();
    }

    /// <summary>The regex of a preamble key line.</summary>
    /// <param name="key">The normalized key.</param>
    /// <returns>The regex: <c>^\s*</c>, the escaped key, <c>\s*[=:]</c>.</returns>
    public static string Key(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var builder = new StringBuilder(key.Length + 24);
        builder.Append("^\\s*");
        AppendEscaped(builder, key);
        return builder.Append("\\s*[=:]").ToString();
    }

    /// <summary>The regex of the start of a header line: the first names, each allowing spaces around it (and a quote, when the dialect has one).</summary>
    /// <param name="names">The first header names.</param>
    /// <param name="delimiter">The delimiter text of the dialect; <c>whitespace</c> for runs of white space.</param>
    /// <param name="quoted">True when a name may be quoted.</param>
    /// <returns>The regex, anchored at the start only.</returns>
    public static string Prefix(IReadOnlyList<string> names, string delimiter, bool quoted)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(delimiter);
        var quote = quoted ? "\"?" : string.Empty;
        var whitespace = string.Equals(delimiter, DelimiterCandidates.WhitespaceName, StringComparison.Ordinal);
        var builder = new StringBuilder("^");
        builder.Append(whitespace ? "\\s*" : " *");
        for (var i = 0; i < names.Count; i++)
        {
            if (i > 0)
            {
                if (whitespace)
                {
                    builder.Append("\\s+");
                }
                else
                {
                    AppendEscaped(builder, delimiter);
                    builder.Append(" *");
                }
            }

            builder.Append(quote);
            AppendEscaped(builder, names[i]);
            builder.Append(quote);
            if (!whitespace)
            {
                builder.Append(" *");
            }
        }

        if (whitespace)
        {
            builder.Append("\\s*");
        }

        return builder.ToString();
    }

    /// <summary>Whether a regex matches a line under both engines.</summary>
    /// <param name="regex">The regex.</param>
    /// <param name="line">The line.</param>
    /// <returns>True when it compiles and matches under the .NET default and under <see cref="RegexOptions.ECMAScript"/>.</returns>
    public static bool MatchesBoth(string regex, string line)
    {
        ArgumentNullException.ThrowIfNull(regex);
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            return new Regex(regex, RegexOptions.CultureInvariant).IsMatch(line)
                && new Regex(regex, RegexOptions.ECMAScript).IsMatch(line);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void AppendEscaped(StringBuilder builder, string text)
    {
        foreach (var c in text)
        {
            if (Meta.Contains(c, StringComparison.Ordinal))
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }
    }
}
