// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The parsed names of one header and the file's convention summary.</summary>
/// <param name="Parts">The parts of each name, in column order.</param>
/// <param name="Info">The <c>layout.headerConvention</c> object.</param>
/// <param name="Evidence">The evidence entry of the inference.</param>
internal sealed record HeaderConventions(IReadOnlyList<NameParts> Parts, HeaderConventionInfo Info, Evidence Evidence);

/// <summary>
/// Parses header names against the header-unit conventions, mirroring DataBall's <c>HeaderParser</c>: the
/// parenthesis form, then the underscore form (which needs a known unit), then the plain name. squalify adds the
/// bracket form between the two and the <c>unitsRow</c> convention.
/// </summary>
internal static class HeaderConventionParser
{
    private const string Paren = "nameParenUnit";
    private const string Bracket = "nameBracketUnit";
    private const string Underscore = "nameUnderscoreUnit";
    private const string UnitsRow = "unitsRow";
    private const string None = "none";

    /// <summary>The conventions in the order that breaks a tie for the dominant one: <c>none</c> comes last.</summary>
    public static IReadOnlyList<string> Conventions { get; } = [Paren, Bracket, Underscore, UnitsRow, None];

    /// <summary>
    /// Parses one header name. It never returns <c>unitsRow</c>, which depends on the file, not on the name.
    /// </summary>
    /// <param name="name">The header name. <see cref="ValueClassifier.TrimChars"/> at either end are ignored.</param>
    /// <returns>The parts: base, unit text as written (null when none), convention and whether the unit is unknown.</returns>
    public static NameParts Parse(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var header = ValueClassifier.Trim(name).ToString();

        if (TryEnclosed(header, '(', ')', digitsAreNotUnits: false, out var enclosedBase, out var enclosedUnit))
        {
            return new NameParts(enclosedBase, enclosedUnit, Paren, !UnitTable.IsKnown(enclosedUnit));
        }

        if (TryEnclosed(header, '[', ']', digitsAreNotUnits: true, out enclosedBase, out enclosedUnit))
        {
            return new NameParts(enclosedBase, enclosedUnit, Bracket, !UnitTable.IsKnown(enclosedUnit));
        }

        if (TryUnderscore(header, out var underscoreBase, out var underscoreUnit))
        {
            return new NameParts(underscoreBase, underscoreUnit, Underscore);
        }

        return new NameParts(header, null, None);
    }

    /// <summary>Parses every name of a header and summarizes the conventions.</summary>
    /// <param name="names">The names, in column order.</param>
    /// <param name="unitsRowCells">Null when the file has no units row; otherwise, per column, whether its units cell has a value. A name without a unit of its own gets <c>unitsRow</c> only when its cell has a value; an empty or null cell leaves it <c>none</c>.</param>
    /// <returns>The parts, the summary and the evidence. The dominant convention is the most common one, a tie going to the earlier in <see cref="Conventions"/>.</returns>
    public static HeaderConventions Analyze(IReadOnlyList<string> names, IReadOnlyList<bool>? unitsRowCells = null)
    {
        ArgumentNullException.ThrowIfNull(names);
        var parts = new NameParts[names.Count];
        var counts = new long[Conventions.Count];
        for (var i = 0; i < parts.Length; i++)
        {
            var parsed = Parse(names[i]);
            if (parsed.Convention == None && unitsRowCells is not null && i < unitsRowCells.Count && unitsRowCells[i])
            {
                // The units text is a value, so it stays in fields[].unit (values tier) and not here.
                parsed = parsed with { Convention = UnitsRow };
            }

            parts[i] = parsed;
            counts[IndexOf(parsed.Convention)]++;
        }

        // With no names there is nothing to count: the dominant convention is none, with share 0.
        var dominant = parts.Length == 0 ? Conventions.Count - 1 : 0;
        for (var c = 1; c < counts.Length; c++)
        {
            if (counts[c] > counts[dominant])
            {
                dominant = c;
            }
        }

        var total = (double)parts.Length;
        var share = parts.Length == 0 ? 0.0 : counts[dominant] / total;
        var entries = new List<KeyValuePair<string, long>>();
        var candidates = new List<Candidate>();
        for (var c = 0; c < counts.Length; c++)
        {
            if (counts[c] > 0)
            {
                entries.Add(new(Conventions[c], counts[c]));
            }

            candidates.Add(new Candidate(
                Conventions[c],
                parts.Length == 0 ? 0.0 : counts[c] / total,
                Ordinal.Invariant(counts[c]) + " of " + Ordinal.Invariant(parts.Length) + " headers"));
        }

        var info = new HeaderConventionInfo(Conventions[dominant], share, CountMap.FromOrdinal(entries));
        return new HeaderConventions(parts, info, new Evidence("unit", "headerConvention", info.Dominant, candidates));
    }

    private static int IndexOf(string convention)
    {
        for (var i = 0; i < Conventions.Count; i++)
        {
            if (Conventions[i] == convention)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Unknown convention.");
    }

    // The last '(' and the last ')' as in DataBall: the name ends with the closer, the opener lies after the first
    // character, and the text between them is not empty. The name part and the unit are trimmed and not empty. The
    // bracket form also rejects a unit that is only ASCII digits.
    private static bool TryEnclosed(string header, char opener, char closer, bool digitsAreNotUnits, out string name, out string unit)
    {
        name = header;
        unit = string.Empty;
        var close = header.LastIndexOf(closer);
        var open = header.LastIndexOf(opener);
        if (close != header.Length - 1 || open <= 0 || close <= open + 1)
        {
            return false;
        }

        var n = ValueClassifier.Trim(header.AsSpan(0, open)).ToString();
        var u = ValueClassifier.Trim(header.AsSpan(open + 1, close - open - 1)).ToString();
        if (n.Length == 0 || u.Length == 0 || (digitsAreNotUnits && IsAsciiDigits(u)))
        {
            return false;
        }

        name = n;
        unit = u;
        return true;
    }

    // Bracket text of ASCII digits only is an array index, as in the trace columns Trace[0] to Trace[n].
    private static bool IsAsciiDigits(string text)
    {
        foreach (var c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    // The last '_' as in DataBall, and the suffix must be a known unit.
    private static bool TryUnderscore(string header, out string name, out string unit)
    {
        name = header;
        unit = string.Empty;
        var underscore = header.LastIndexOf('_');
        if (underscore <= 0 || underscore >= header.Length - 1)
        {
            return false;
        }

        var n = ValueClassifier.Trim(header.AsSpan(0, underscore)).ToString();
        var u = ValueClassifier.Trim(header.AsSpan(underscore + 1)).ToString();
        if (n.Length == 0 || u.Length == 0 || !UnitTable.IsKnown(u))
        {
            return false;
        }

        name = n;
        unit = u;
        return true;
    }
}
