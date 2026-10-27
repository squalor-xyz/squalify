// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The fixed unit table, version 1 (<c>limits.unitTable</c>). It decides which suffix of a <c>name_unit</c> header is
/// a unit. Matching is ordinal and case-sensitive, except for the alias list, which is matched ASCII
/// case-insensitively. Nothing here uses the Unicode tables or the current culture.
/// </summary>
/// <remarks>
/// The table follows DataBall's built-in unit table (<c>Config.cs</c>) and extends it as the spec asks: it adds dBc, K,
/// F and H, and combines V, A, W, Hz, s, Ohm, F and H with the SI prefixes f, p, n, u (also U+00B5 and U+03BC), m, k, M
/// and G. It has no <c>id</c> unit: DataBall knows <c>id</c> as a unit, squalify does not.
/// </remarks>
internal static class UnitTable
{
    /// <summary>The table version written as <c>limits.unitTable</c>. Any change to the table bumps it.</summary>
    public const long Version = 1;

    // Units that take an SI prefix, matched exactly. U+03A9 is GREEK CAPITAL LETTER OMEGA; U+2126 is OHM SIGN.
    private static readonly string[] Prefixable =
        ["V", "A", "W", "Hz", "s", "Ohm", "\u03A9", "\u2126", "F", "H"];

    // Units that take no prefix, matched exactly. U+00B0 is DEGREE SIGN, alone for degrees of angle (as is "deg").
    private static readonly string[] Plain = ["dB", "dBm", "dBc", "%", "C", "\u00B0C", "K", "\u00B0", "deg"];

    // Spellings matched ASCII case-insensitively, given in lower case. The prefixable one (ohm) also takes a prefix.
    private static readonly string[] Aliases =
    [
        "amps", "amp", "ampere", "amperes",
        "volt", "volts",
        "sec", "second", "seconds",
        "degc", "celsius",
        "ohm", "ohms",
        "watt", "watts",
        "pct", "percent",
    ];

    // The prefix characters: f p n u, U+00B5 MICRO SIGN, U+03BC GREEK SMALL LETTER MU, m k M G.
    private const string Prefixes = "fpnu\u00B5\u03BCmkMG";

    /// <summary>Tells whether a unit text is in the table.</summary>
    /// <param name="unit">The unit text, already trimmed.</param>
    /// <returns>True when the unit is known.</returns>
    public static bool IsKnown(string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Length == 0)
        {
            return false;
        }

        if (IsBase(unit))
        {
            return true;
        }

        return unit.Length > 1 && Prefixes.Contains(unit[0], StringComparison.Ordinal) && IsPrefixable(unit.AsSpan(1));
    }

    private static bool IsBase(string unit)
    {
        return Array.IndexOf(Plain, unit) >= 0 || IsPrefixable(unit) || IsAlias(unit);
    }

    private static bool IsPrefixable(ReadOnlySpan<char> unit)
    {
        foreach (var candidate in Prefixable)
        {
            if (unit.SequenceEqual(candidate))
            {
                return true;
            }
        }

        return EqualsAsciiFolded(unit, "ohm");
    }

    private static bool IsAlias(ReadOnlySpan<char> unit)
    {
        foreach (var alias in Aliases)
        {
            if (EqualsAsciiFolded(unit, alias))
            {
                return true;
            }
        }

        return false;
    }

    // Compares with the ASCII letters of the unit folded to lower case; the lower-case text is already folded.
    private static bool EqualsAsciiFolded(ReadOnlySpan<char> unit, string lower)
    {
        if (unit.Length != lower.Length)
        {
            return false;
        }

        for (var i = 0; i < unit.Length; i++)
        {
            var c = unit[i];
            if (c is >= 'A' and <= 'Z')
            {
                c = (char)(c + ('a' - 'A'));
            }

            if (c != lower[i])
            {
                return false;
            }
        }

        return true;
    }
}
