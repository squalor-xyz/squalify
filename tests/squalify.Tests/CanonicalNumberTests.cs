// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

public class CanonicalNumberTests
{
    [Theory]
    [InlineData(0.1, "0.1")]
    [InlineData(1700.0, "1700")]
    [InlineData(584.5, "584.5")]
    [InlineData(0.30000000000000004, "0.30000000000000004")]
    [InlineData(-2.5, "-2.5")]
    [InlineData(1e-7, "1E-07")]
    [InlineData(1.5e300, "1.5E+300")]
    [InlineData(double.MaxValue, "1.7976931348623157E+308")]
    [InlineData(double.Epsilon, "5E-324")]
    [InlineData(123456789012345680.0, "1.2345678901234568E+17")]
    public void FormatFinite_IsShortestRoundTrip(double value, string expected)
    {
        Assert.Equal(expected, CanonicalNumber.FormatFinite(value));
    }

    [Fact]
    public void FormatFinite_NegativeZero_IsZero()
    {
        Assert.Equal("0", CanonicalNumber.FormatFinite(-0.0));
        Assert.Equal("0", CanonicalNumber.FormatFinite(0.0));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FormatFinite_RejectsNonFinite(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanonicalNumber.FormatFinite(value));
    }

    [Fact]
    public void Write_FiniteValues_MatchFormatFinite_InsideAnIndentedArray()
    {
        // Guards the System.Text.Json number format that Write relies on, over many magnitudes.
        var random = new Random(12345);
        var values = new List<double> { 0.1, 1e15, 1e16, 1e-5, 123456789.0, double.MaxValue, double.Epsilon, -1.5 };
        for (var i = 0; i < 2000; i++)
        {
            var bits = ((ulong)random.NextInt64() << 1) ^ (ulong)random.NextInt64();
            var value = BitConverter.UInt64BitsToDouble(bits);
            if (double.IsFinite(value))
            {
                values.Add(value);
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, IndentSize = 2, NewLine = "\n" }))
        {
            writer.WriteStartArray();
            foreach (var value in values)
            {
                CanonicalNumber.Write(writer, value);
            }

            writer.WriteEndArray();
        }

        var lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n');
        Assert.Equal("[", lines[0]);
        for (var i = 0; i < values.Count; i++)
        {
            var expected = "  " + CanonicalNumber.FormatFinite(values[i]) + (i < values.Count - 1 ? "," : string.Empty);
            Assert.Equal(expected, lines[i + 1]);
        }

        Assert.Equal("]", lines[^1]);
    }

    [Fact]
    public void Write_NonFiniteBecomeStrings_AndIgnoresCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();
                foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.0, 0.5 })
                {
                    CanonicalNumber.Write(writer, value);
                }

                writer.WriteEndArray();
            }

            Assert.Equal("[\"NaN\",\"Infinity\",\"-Infinity\",0,0.5]", Encoding.UTF8.GetString(stream.ToArray()));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
