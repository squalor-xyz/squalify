// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace squalor.squalify;

/// <summary>What an option line says. A member the line leaves out is null (the default applies).</summary>
/// <param name="Unit"><c>Hz</c>, <c>kHz</c>, <c>MHz</c> or <c>GHz</c>, or null.</param>
/// <param name="Parameter"><c>S</c>, <c>Y</c>, <c>Z</c>, <c>H</c> or <c>G</c>, or null.</param>
/// <param name="Format"><c>DB</c>, <c>MA</c> or <c>RI</c>, or null.</param>
/// <param name="Reference">The reference resistance (the first value after <c>R</c>), or null.</param>
internal sealed record OptionLine(string? Unit, string? Parameter, string? Format, double? Reference);

/// <summary>The known keywords of a Version 2 file, by their canonical spelling.</summary>
internal enum TouchstoneKeyword
{
    /// <summary>Not a keyword that this profiler knows.</summary>
    Unknown,

    /// <summary><c>[Version]</c>.</summary>
    Version,

    /// <summary><c>[Number of Ports]</c>.</summary>
    NumberOfPorts,

    /// <summary><c>[Two-Port Data Order]</c>.</summary>
    TwoPortDataOrder,

    /// <summary><c>[Number of Frequencies]</c>.</summary>
    NumberOfFrequencies,

    /// <summary><c>[Number of Noise Frequencies]</c>.</summary>
    NumberOfNoiseFrequencies,

    /// <summary><c>[Reference]</c>.</summary>
    Reference,

    /// <summary><c>[Matrix Format]</c>.</summary>
    MatrixFormat,

    /// <summary><c>[Mixed-Mode Order]</c>.</summary>
    MixedModeOrder,

    /// <summary><c>[Network Data]</c>.</summary>
    NetworkData,

    /// <summary><c>[Noise Data]</c>.</summary>
    NoiseData,

    /// <summary><c>[End]</c>.</summary>
    End,
}

/// <summary>
/// The syntax of Touchstone files, from the IBIS Touchstone 2.0 and 2.1 specifications and scikit-rf's reader (<c>skrf/io/touchstone.py</c>).
/// Everything is ASCII: the case folding, the white space and the number reading do not use the runtime's Unicode tables or the culture.
/// </summary>
internal static class TouchstoneSyntax
{
    /// <summary>The canonical spelling of each known keyword, in the order of the specification.</summary>
    public static readonly string[] KeywordNames =
    [
        "[Version]",
        "[Number of Ports]",
        "[Two-Port Data Order]",
        "[Number of Frequencies]",
        "[Number of Noise Frequencies]",
        "[Reference]",
        "[Matrix Format]",
        "[Mixed-Mode Order]",
        "[Network Data]",
        "[Noise Data]",
        "[End]",
    ];

    /// <summary>The names of the noise fields, in file order.</summary>
    public static readonly string[] NoiseFieldNames = ["noise.freq", "noise.nfmin", "noise.gammaOpt.mag", "noise.gammaOpt.ang", "noise.rn"];

    /// <summary>The number of values in a line of noise data: the frequency, the minimum noise figure, the magnitude and angle of the optimum source reflection, and the effective noise resistance.</summary>
    public const int NoiseValues = 5;

    /// <summary>Whether a character separates tokens: a space, a tab, a vertical tab or a form feed.</summary>
    /// <param name="c">The character.</param>
    /// <returns>True for white space.</returns>
    public static bool IsSpace(char c)
    {
        return c is ' ' or '\t' or '\v' or '\f';
    }

    /// <summary>The line without its comment: the text before the first <c>!</c>, with white space trimmed from both ends.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The content.</returns>
    public static ReadOnlySpan<char> Content(string line)
    {
        var span = line.AsSpan();
        var bang = span.IndexOf('!');
        return ValueClassifier.Trim(bang < 0 ? span : span[..bang]);
    }

    /// <summary>Whether a line is a comment line: its first character that is not white space is <c>!</c>.</summary>
    /// <param name="line">The line.</param>
    /// <returns>True for a comment line.</returns>
    public static bool IsComment(string line)
    {
        var trimmed = ValueClassifier.Trim(line);
        return trimmed.Length > 0 && trimmed[0] == '!';
    }

    /// <summary>Reads an option line. The tokens may come in any order (the specification), and the usual positional order is one of them (scikit-rf).</summary>
    /// <param name="content">The line without its comment, starting with <c>#</c>.</param>
    /// <returns>What the line says, or null when it holds a token that is not an option.</returns>
    public static OptionLine? ParseOption(ReadOnlySpan<char> content)
    {
        if (content.Length == 0 || content[0] != '#')
        {
            return null;
        }

        string? unit = null;
        string? parameter = null;
        string? format = null;
        double? reference = null;
        var afterR = false;
        var rest = content[1..];
        while (NextToken(ref rest, out var token))
        {
            var folded = Ordinal.AsciiFold(token);
            if (afterR && TryNumber(token, out var number))
            {
                reference ??= number;
                continue;
            }

            afterR = false;
            switch (folded)
            {
                case "hz" or "khz" or "mhz" or "ghz" when unit is null:
                    unit = folded switch { "hz" => "Hz", "khz" => "kHz", "mhz" => "MHz", _ => "GHz" };
                    break;
                case "s" or "y" or "z" or "h" or "g" when parameter is null:
                    parameter = folded.ToUpperInvariant();
                    break;
                case "db" or "ma" or "ri" when format is null:
                    format = folded.ToUpperInvariant();
                    break;
                case "r" when reference is null:
                    afterR = true;
                    break;
                default:
                    return null;
            }
        }

        // "R" needs a value.
        return afterR && reference is null ? null : new OptionLine(unit, parameter, format, reference);
    }

    /// <summary>Reads a keyword line: <c>[Name] arguments</c>.</summary>
    /// <param name="content">The line without its comment, starting with <c>[</c>.</param>
    /// <param name="arguments">The text after the closing bracket, trimmed.</param>
    /// <returns>The keyword, or <see cref="TouchstoneKeyword.Unknown"/> for a bracketed name that is not known; null when the line has no closing bracket.</returns>
    public static TouchstoneKeyword? ParseKeyword(ReadOnlySpan<char> content, out ReadOnlySpan<char> arguments)
    {
        arguments = default;
        if (content.Length == 0 || content[0] != '[')
        {
            return null;
        }

        var close = content.IndexOf(']');
        if (close < 0)
        {
            return null;
        }

        arguments = ValueClassifier.Trim(content[(close + 1)..]);

        // Keywords are case-insensitive, and the parts of a multi-word keyword are separated by one space or one dash.
        var name = new System.Text.StringBuilder();
        var lastSpace = false;
        foreach (var c in content[1..close])
        {
            var folded = c is '-' ? ' ' : c;
            if (IsSpace(folded))
            {
                if (!lastSpace && name.Length > 0)
                {
                    name.Append(' ');
                }

                lastSpace = true;
            }
            else
            {
                name.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
                lastSpace = false;
            }
        }

        return name.ToString().TrimEnd() switch
        {
            "version" => TouchstoneKeyword.Version,
            "number of ports" => TouchstoneKeyword.NumberOfPorts,
            "two port data order" => TouchstoneKeyword.TwoPortDataOrder,
            "number of frequencies" => TouchstoneKeyword.NumberOfFrequencies,
            "number of noise frequencies" => TouchstoneKeyword.NumberOfNoiseFrequencies,
            "reference" => TouchstoneKeyword.Reference,
            "matrix format" => TouchstoneKeyword.MatrixFormat,
            "mixed mode order" => TouchstoneKeyword.MixedModeOrder,
            "network data" => TouchstoneKeyword.NetworkData,
            "noise data" => TouchstoneKeyword.NoiseData,
            "end" => TouchstoneKeyword.End,
            _ => TouchstoneKeyword.Unknown,
        };
    }

    /// <summary>The canonical spelling of a keyword.</summary>
    /// <param name="keyword">A known keyword.</param>
    /// <returns>The spelling, with its brackets.</returns>
    public static string Name(TouchstoneKeyword keyword)
    {
        return KeywordNames[(int)keyword - 1];
    }

    /// <summary>The next token of a span (a run of characters that are not white space).</summary>
    /// <param name="rest">The rest of the line; advanced past the token.</param>
    /// <param name="token">The token.</param>
    /// <returns>False when there is none.</returns>
    public static bool NextToken(ref ReadOnlySpan<char> rest, out ReadOnlySpan<char> token)
    {
        var start = 0;
        while (start < rest.Length && IsSpace(rest[start]))
        {
            start++;
        }

        if (start == rest.Length)
        {
            token = default;
            rest = default;
            return false;
        }

        var end = start;
        while (end < rest.Length && !IsSpace(rest[end]))
        {
            end++;
        }

        token = rest[start..end];
        rest = rest[end..];
        return true;
    }

    /// <summary>
    /// Whether a token is a number of the network data: an integer or a decimal number (with or without a leading digit, with an exponent),
    /// or <c>NaN</c>. The value classifier decides, with <c>.</c> as the decimal separator.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="value">The value, NaN for <c>NaN</c>.</param>
    /// <returns>True for a number.</returns>
    public static bool TryNumber(ReadOnlySpan<char> token, out double value)
    {
        var cls = ValueClassifier.Classify(token, '.');
        switch (cls.Vote)
        {
            case ValueVote.Int or ValueVote.Float:
                value = cls.Number;
                return true;
            case ValueVote.Null when string.Equals(cls.NullToken, "NaN", StringComparison.Ordinal):
                value = double.NaN;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>The port count that a file name's extension gives: <c>.s2p</c>, <c>.y4p</c> and the like (a letter of <c>ghsyz</c>, digits, <c>p</c>).</summary>
    /// <param name="fileName">The bare file name.</param>
    /// <param name="isTs">True for the extension <c>.ts</c> (a Version 2 file, which has no count in its name).</param>
    /// <returns>The port count, or null when the extension gives none.</returns>
    public static long? PortsFromExtension(string fileName, out bool isTs)
    {
        isTs = false;
        var extension = Path.GetExtension(fileName);
        var folded = Ordinal.AsciiFold(extension);
        if (folded == ".ts")
        {
            isTs = true;
            return null;
        }

        if (folded.Length >= 4 && folded[0] == '.' && folded[1] is 'g' or 'h' or 's' or 'y' or 'z' && folded[^1] == 'p')
        {
            var digits = folded.AsSpan(2, folded.Length - 3);
            foreach (var c in digits)
            {
                if (c is < '0' or > '9')
                {
                    return null;
                }
            }

            if (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var ports) && ports > 0)
            {
                return ports;
            }
        }

        return null;
    }

    /// <summary>Reads a whole number from a keyword argument.</summary>
    /// <param name="arguments">The text after the keyword.</param>
    /// <param name="value">The first token as a positive whole number.</param>
    /// <returns>True when there is one.</returns>
    public static bool TryCount(ReadOnlySpan<char> arguments, out long value)
    {
        value = 0;
        if (!NextToken(ref arguments, out var token))
        {
            return false;
        }

        foreach (var c in token)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }
}
