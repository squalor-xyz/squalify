// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text.Json;

namespace squalor.squalify;

/// <summary>
/// Canonical JSON number formatting for descriptors, independent of culture and platform.
/// <list type="bullet">
/// <item>A <see cref="double"/> is written in .NET's shortest round-trip form ("R"), with the invariant culture.
/// Large and small magnitudes use exponent form such as <c>1E-07</c> or <c>1.5E+300</c>, which is valid JSON.</item>
/// <item><c>-0</c> is written as <c>0</c>.</item>
/// <item>NaN, <c>Infinity</c> and <c>-Infinity</c> have no JSON number form and are written as the strings
/// <c>"NaN"</c>, <c>"Infinity"</c> and <c>"-Infinity"</c>.</item>
/// <item>A <see cref="long"/> is written as an integer.</item>
/// </list>
/// </summary>
internal static class CanonicalNumber
{
    /// <summary>Formats a finite double as the JSON number text that <see cref="Write(Utf8JsonWriter, double)"/> emits.</summary>
    /// <param name="value">A finite double.</param>
    /// <returns>The JSON number text. Negative zero gives <c>0</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite.</exception>
    public static string FormatFinite(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Only finite values have a JSON number form.");
        }

        return value == 0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Writes a double as a canonical JSON value.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value, possibly non-finite.</param>
    public static void Write(Utf8JsonWriter writer, double value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (double.IsNaN(value))
        {
            writer.WriteStringValue("NaN");
        }
        else if (double.IsPositiveInfinity(value))
        {
            writer.WriteStringValue("Infinity");
        }
        else if (double.IsNegativeInfinity(value))
        {
            writer.WriteStringValue("-Infinity");
        }
        else if (value == 0)
        {
            writer.WriteNumberValue(0L);
        }
        else
        {
            // Not WriteRawValue: it skips the indentation and line breaks of array elements.
            writer.WriteNumberValue(value);
        }
    }

    /// <summary>Writes an integer as a JSON number.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value.</param>
    public static void Write(Utf8JsonWriter writer, long value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value);
    }
}
