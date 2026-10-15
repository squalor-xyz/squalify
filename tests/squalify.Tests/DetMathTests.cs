// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public class DetMathTests
{
    private static string FixturePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "fixtures", "detmath-log2.tsv");
    }

    [Fact]
    public void DetMath_Log2_MatchesReferenceTable()
    {
        var rows = File.ReadAllLines(FixturePath(), Encoding.UTF8)
            .Where(static line => line.Length > 0 && line[0] != '#')
            .Select(static line => line.Split('\t'))
            .ToArray();
        Assert.True(rows.Length >= 500, "reference table is unexpectedly small");

        var mismatches = new List<string>();
        foreach (var row in rows)
        {
            var xBits = ulong.Parse(row[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            var expectedBits = ulong.Parse(row[2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            var x = BitConverter.UInt64BitsToDouble(xBits);
            var actualBits = BitConverter.DoubleToUInt64Bits(DetMath.Log2(x));
            if (actualBits != expectedBits)
            {
                mismatches.Add($"x={row[1]} (0x{xBits:X16}): expected 0x{expectedBits:X16}, got 0x{actualBits:X16}");
            }
        }

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {rows.Length} rows differ:\n{string.Join('\n', mismatches.Take(20))}");
    }

    [Fact]
    public void DetMath_Log2_PowersOfTwoExact()
    {
        for (var e = -1074; e <= 1023; e++)
        {
            var x = Math.ScaleB(1.0, e);
            Assert.Equal((double)e, DetMath.Log2(x));
        }
    }

    [Fact]
    public void DetMath_Log2_OfOne_IsPositiveZero()
    {
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(DetMath.Log2(1.0)));
    }

    [Fact]
    public void DetMath_Log2_SpecialValues()
    {
        Assert.Equal(double.NegativeInfinity, DetMath.Log2(0.0));
        Assert.Equal(double.NegativeInfinity, DetMath.Log2(-0.0));
        Assert.Equal(double.PositiveInfinity, DetMath.Log2(double.PositiveInfinity));
        Assert.True(double.IsNaN(DetMath.Log2(-1.0)));
        Assert.True(double.IsNaN(DetMath.Log2(double.NegativeInfinity)));
        Assert.True(double.IsNaN(DetMath.Log2(double.NaN)));
    }

    [Fact]
    public void DetMath_Log2_StaysWithinDocumentedError_OnAGrid()
    {
        // Math.Log2 is used only as a coarse yardstick here; it never reaches an output path.
        for (var i = 1; i <= 4000; i++)
        {
            var x = i / 64.0;
            var expected = Math.Log2(x);
            var actual = DetMath.Log2(x);
            Assert.True(Math.Abs(actual - expected) <= 1e-12 * Math.Abs(expected) + 1e-300, $"x={x}: {actual} vs {expected}");
        }
    }

    [Fact]
    public void DetMath_Ln_IsLog2TimesLn2()
    {
        Assert.Equal(0.6931471805599453, DetMath.Ln2);
        foreach (var x in new[] { 0.5, 2.0, 3.0, 10.0, 1e-5 })
        {
            Assert.Equal(DetMath.Log2(x) * DetMath.Ln2, DetMath.Ln(x));
        }

        Assert.Equal(0.0, DetMath.Ln(1.0));
        Assert.True(Math.Abs(DetMath.Ln(10.0) - 2.302585092994046) <= 1e-15 * 2.302585092994046);
    }
}
