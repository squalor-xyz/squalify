// SPDX-License-Identifier: Apache-2.0
using System.Text;

namespace squalor.squalify.Tests;

/// <summary>
/// Generates synthetic Touchstone files (v1 and v2) for a given port count, data format, line wrap and noise block. The values are
/// made from integers, so the text does not depend on the culture or on floating-point formatting. Nothing here comes from a real
/// file.
/// </summary>
internal sealed class TouchstoneBuilder
{
    private readonly List<string> _comments = [];

    /// <summary>The number of ports.</summary>
    public int Ports { get; init; } = 2;

    /// <summary>The number of frequency points.</summary>
    public int Frequencies { get; init; } = 4;

    /// <summary>The option line, or null for a file with none.</summary>
    public string? OptionLine { get; init; } = "# GHz S MA R 50";

    /// <summary>True for a Version 2 file: <c>[Version]</c>, <c>[Number of Ports]</c> and the other keywords.</summary>
    public bool V2 { get; init; }

    /// <summary>The <c>[Version]</c> argument of a Version 2 file.</summary>
    public string VersionText { get; init; } = "2.0";

    /// <summary>The <c>[Two-Port Data Order]</c> argument of a Version 2 two-port file, or null for none.</summary>
    public string? TwoPortOrder { get; init; } = "21_12";

    /// <summary>The <c>[Matrix Format]</c> argument, or null for none (Full).</summary>
    public string? MatrixFormat { get; init; }

    /// <summary>The number of values on each line (the frequency counts as the first), or 0 for the layout of the specification: a row of the matrix to a line.</summary>
    public int ValuesPerLine { get; init; }

    /// <summary>The number of noise points (two-port files only), or 0 for none.</summary>
    public int NoiseFrequencies { get; init; }

    /// <summary>True to end a Version 2 file with <c>[End]</c>.</summary>
    public bool End { get; init; } = true;

    /// <summary>The number of values to cut off the last network block (an incomplete block), or 0.</summary>
    public int CutLastBlockBy { get; init; }

    /// <summary>Extra <c>[Reference]</c> line text of a Version 2 file, or null for none.</summary>
    public string? Reference { get; init; }

    /// <summary>The line ending.</summary>
    public string NewLine { get; init; } = "\n";

    /// <summary>A comment line to put at the top (the <c>!</c> is added).</summary>
    public TouchstoneBuilder Comment(string text)
    {
        _comments.Add(text);
        return this;
    }

    /// <summary>The number of values of one block: the frequency and the entries as pairs.</summary>
    public int ValuesPerBlock => 1 + (2 * Entries());

    /// <summary>The number of matrix entries of a block, by the matrix format.</summary>
    public int Entries()
    {
        return MatrixFormat is "Lower" or "Upper" ? (Ports * (Ports + 1)) / 2 : Ports * Ports;
    }

    /// <summary>Builds the file.</summary>
    public string Build()
    {
        var text = new StringBuilder();
        foreach (var comment in _comments)
        {
            Line(text, "!" + comment);
        }

        if (V2)
        {
            Line(text, "[Version] " + VersionText);
        }

        if (OptionLine is not null)
        {
            Line(text, OptionLine);
        }

        if (V2)
        {
            Line(text, "[Number of Ports] " + Ports);
            if (Ports == 2 && TwoPortOrder is not null)
            {
                Line(text, "[Two-Port Data Order] " + TwoPortOrder);
            }

            Line(text, "[Number of Frequencies] " + Frequencies);
            if (NoiseFrequencies > 0)
            {
                Line(text, "[Number of Noise Frequencies] " + NoiseFrequencies);
            }

            if (Reference is not null)
            {
                Line(text, "[Reference] " + Reference);
            }

            if (MatrixFormat is not null)
            {
                Line(text, "[Matrix Format] " + MatrixFormat);
            }

            Line(text, "[Network Data]");
        }

        for (var k = 0; k < Frequencies; k++)
        {
            var tokens = new List<string> { Frequency(k) };
            for (var j = 0; j < 2 * Entries(); j++)
            {
                tokens.Add(Value(k, j));
            }

            if (k == Frequencies - 1 && CutLastBlockBy > 0)
            {
                tokens.RemoveRange(tokens.Count - CutLastBlockBy, CutLastBlockBy);
            }

            Wrap(text, tokens);
        }

        if (V2 && NoiseFrequencies > 0)
        {
            Line(text, "[Noise Data]");
        }

        for (var k = 0; k < NoiseFrequencies; k++)
        {
            // The noise frequencies start below the last network frequency (the drop that marks the noise block in Version 1).
            var tokens = new List<string> { NoiseFrequency(k), Fixed(300 + (k * 20)), Fixed(400 + (k * 10)), Angle(k + 3), Fixed(200 + k) };
            Line(text, string.Join(" ", tokens));
        }

        if (V2 && End)
        {
            Line(text, "[End]");
        }

        return text.ToString();
    }

    /// <summary>Builds the file as ASCII bytes.</summary>
    public byte[] Bytes()
    {
        return Encoding.ASCII.GetBytes(Build());
    }

    /// <summary>The frequency of point <paramref name="k"/>, in the unit of the option line.</summary>
    public static string Frequency(int k)
    {
        return (1 + (k * 2)).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".5";
    }

    /// <summary>The frequency of noise point <paramref name="k"/>: below the first network frequency, increasing.</summary>
    public static string NoiseFrequency(int k)
    {
        return (k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".25";
    }

    /// <summary>Value <paramref name="j"/> of block <paramref name="k"/>: a magnitude-like number for an even index, an angle-like one for an odd index.</summary>
    public static string Value(int k, int j)
    {
        return j % 2 == 0 ? Fixed(((k * 37) + (j * 11) + 50) % 997) : Angle((k * 5) + j);
    }

    private static string Fixed(int thousandths)
    {
        return "0." + (thousandths % 1000).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Angle(int n)
    {
        var degrees = ((n * 17) % 360) - 180;
        return degrees.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + ((n * 7) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Wrap(StringBuilder text, List<string> tokens)
    {
        if (ValuesPerLine > 0)
        {
            for (var i = 0; i < tokens.Count; i += ValuesPerLine)
            {
                Line(text, string.Join(" ", tokens.Skip(i).Take(ValuesPerLine)));
            }

            return;
        }

        // The layout of the specification: one line for a 1-port or 2-port network, a matrix row to a line for more ports.
        if (Ports <= 2 || tokens.Count <= 1 + (2 * Ports))
        {
            Line(text, string.Join(" ", tokens));
            return;
        }

        var at = 0;
        var row = 0;
        var first = true;
        while (at < tokens.Count)
        {
            var width = first ? 1 + (2 * (MatrixFormat == "Lower" ? 1 : MatrixFormat == "Upper" ? Ports : Ports)) : 2 * RowEntries(row);
            Line(text, string.Join(" ", tokens.Skip(at).Take(width)));
            at += width;
            first = false;
            row++;
        }
    }

    // The entries of the row after the first row (row 1, 2, and so on), by the matrix format.
    private int RowEntries(int row)
    {
        return MatrixFormat switch
        {
            "Lower" => row + 1,
            "Upper" => Ports - row,
            _ => Ports,
        };
    }

    private void Line(StringBuilder text, string line)
    {
        text.Append(line).Append(NewLine);
    }
}
