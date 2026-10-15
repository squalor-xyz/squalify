// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>A descriptor statistic that is an integer for <c>int</c> fields and a double otherwise.</summary>
public readonly record struct DescriptorNumber
{
    private readonly long _integer;
    private readonly double _real;

    private DescriptorNumber(bool isInteger, long integer, double real)
    {
        IsInteger = isInteger;
        _integer = integer;
        _real = real;
    }

    /// <summary>True when the value is written as an integer.</summary>
    public bool IsInteger { get; }

    /// <summary>The integer value. Meaningful only when <see cref="IsInteger"/> is true.</summary>
    public long IntegerValue => _integer;

    /// <summary>The floating-point value. Meaningful only when <see cref="IsInteger"/> is false.</summary>
    public double RealValue => _real;

    /// <summary>Creates an integer statistic.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>The statistic.</returns>
    public static DescriptorNumber Int(long value)
    {
        return new DescriptorNumber(true, value, 0);
    }

    /// <summary>Creates a floating-point statistic.</summary>
    /// <param name="value">The double, possibly non-finite.</param>
    /// <returns>The statistic.</returns>
    public static DescriptorNumber Real(double value)
    {
        return new DescriptorNumber(false, 0, value);
    }
}
