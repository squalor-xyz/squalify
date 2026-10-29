// SPDX-License-Identifier: Apache-2.0
using System.Numerics;

namespace squalor.squalify;

/// <summary>
/// A HyperLogLog sketch of the number of distinct values in a stream: <c>m</c> = 2^p byte registers, over the 64-bit
/// <see cref="XxHash64"/> of each value, which the caller computes. The precision <c>p</c> is 14 (16 KiB) unless the
/// memory budget asks for less (<see cref="MemoryBudget"/>); it is fixed when the sketch is made and is between
/// <see cref="MinPrecision"/> and <see cref="Precision"/>.
/// </summary>
/// <remarks>
/// <para>
/// The top <c>p</c> bits of a hash pick a register, and the register keeps the largest rank seen: the position of the first
/// set bit in the remaining 64 - <c>p</c> bits, counted from 1. A register only ever grows, so the sketch is the same for the same
/// set of hashes whatever their order and however often each repeats.
/// </para>
/// <para>
/// The estimate is the raw harmonic-mean estimate <c>alpha * m^2 / sum(2^-M[j])</c>; the powers of two come from
/// <see cref="Math.ScaleB(double, int)"/> (exact), the sum runs in register order, and every operation is an IEEE
/// <c>+ - * /</c>, so the result is the same on every platform. While the raw estimate is at most <c>3.5 m</c> (about
/// 57,000 values at the default precision) and some register is still empty, linear counting is used instead: <c>m * ln(m / V)</c> with
/// <c>V</c> the number of empty registers, through <see cref="DetMath.Ln(double)"/>. The result is rounded to a whole
/// number, half to even. The 64-bit hash needs no large-range correction.
/// </para>
/// <para>
/// The raw estimate runs several percent high just above <c>2.5 m</c>, where HyperLogLog++ corrects it with empirical
/// bias tables that are not implemented here. Linear counting stays accurate further than the textbook switch point
/// (its standard error at <c>3.5 m</c> is about 1 percent), and the two estimators meet at about 57,000 values, so
/// switching there instead keeps the worst-case error near 1.4 percent (measured over 30,000 to 120,000 values) without any table.
/// </para>
/// <para>
/// <see cref="RelativeStdErrorOf(int)"/> is the relative standard error of the raw estimate, 1.04 / sqrt(m), about
/// 0.8125 percent at the default precision; it is a one-sigma figure for large counts, and the worst case in the range
/// just above the switch is somewhat higher. The switch point and the constant <c>alpha</c> follow <c>m</c> (the
/// constant of the textbook is exact for m of 128 or more, which is why the precision does not go below 7).
/// </para>
/// </remarks>
internal sealed class HyperLogLog
{
    /// <summary>The default and the largest number of index bits.</summary>
    public const int Precision = 14;

    /// <summary>The smallest number of index bits: 128 registers, the least for which the constant of the estimate holds.</summary>
    public const int MinPrecision = 7;

    /// <summary>The number of registers at the default precision, <c>m</c>.</summary>
    public const int RegisterCount = 1 << Precision;

    // The raw estimate is replaced by linear counting up to this multiple of m.
    private const double LinearCountingLimit = 3.5;

    // 2^-rank for every rank a register can hold (at most 64 - 7 + 1 = 58).
    private static readonly double[] InversePowers = BuildInversePowers();

    private readonly int _precision;
    private readonly byte[] _registers;
    private readonly double _alpha;
    private int _empty;

    /// <summary>Creates a sketch.</summary>
    /// <param name="precision">The number of index bits, from <see cref="MinPrecision"/> to <see cref="Precision"/>.</param>
    public HyperLogLog(int precision = Precision)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, MinPrecision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, Precision);
        _precision = precision;
        _registers = new byte[1 << precision];
        _empty = _registers.Length;

        // The constant of the harmonic mean for m >= 128.
        _alpha = 0.7213 / (1.0 + (1.079 / _registers.Length));
    }

    /// <summary>The relative standard error of an estimate at the default precision, <c>1.04 / sqrt(m)</c>. The square root of 2^14 is exactly 128.</summary>
    public static double RelativeStdError => RelativeStdErrorOf(Precision);

    /// <summary>The relative standard error of an estimate, <c>1.04 / sqrt(m)</c> for <c>m</c> = 2^precision (a correctly rounded square root, so the same on every platform).</summary>
    /// <param name="precision">The number of index bits.</param>
    /// <returns>One standard error, as a fraction of the count.</returns>
    public static double RelativeStdErrorOf(int precision)
    {
        return 1.04 / Math.Sqrt(1L << precision);
    }

    /// <summary>The memory held by a sketch of a precision, in bytes: the registers and the object.</summary>
    /// <param name="precision">The number of index bits.</param>
    /// <returns>The bytes.</returns>
    public static long BytesOf(int precision)
    {
        return (1L << precision) + 64;
    }

    /// <summary>The number of index bits of this sketch.</summary>
    public int PrecisionBits => _precision;

    /// <summary>The relative standard error of an estimate of this sketch.</summary>
    public double StdError => RelativeStdErrorOf(_precision);

    /// <summary>The number of registers that have not seen a value yet.</summary>
    public int EmptyRegisters => _empty;

    /// <summary>The memory held by the sketch, in bytes: the registers and the object.</summary>
    public long EstimatedBytes => BytesOf(_precision);

    /// <summary>Adds the hash of a value.</summary>
    /// <param name="hash">The 64-bit hash.</param>
    public void Add(ulong hash)
    {
        var index = (int)(hash >> (64 - _precision));

        // The sentinel bit caps the rank at 65 - p when all the remaining bits are zero.
        var rest = (hash << _precision) | (1UL << (_precision - 1));
        var rank = (byte)(BitOperations.LeadingZeroCount(rest) + 1);
        var current = _registers[index];
        if (rank > current)
        {
            if (current == 0)
            {
                _empty--;
            }

            _registers[index] = rank;
        }
    }

    /// <summary>The estimated number of distinct hashes added.</summary>
    /// <returns>The estimate, rounded to a whole number.</returns>
    public long Estimate()
    {
        double sum = 0;
        foreach (var rank in _registers)
        {
            sum += InversePowers[rank];
        }

        double m = _registers.Length;
        var estimate = _alpha * m * m / sum;
        if (estimate <= LinearCountingLimit * m && _empty > 0)
        {
            estimate = m * DetMath.Ln(m / _empty);
        }

        return (long)Math.Round(estimate, MidpointRounding.ToEven);
    }

    private static double[] BuildInversePowers()
    {
        var table = new double[65];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = Math.ScaleB(1.0, -i);
        }

        return table;
    }
}
