// SPDX-License-Identifier: Apache-2.0
using System.Numerics;

namespace squalor.squalify;

/// <summary>
/// A HyperLogLog sketch of the number of distinct values in a stream, in a fixed 16 KiB: <c>m</c> = 2^14 byte
/// registers (precision 14), over the 64-bit <see cref="XxHash64"/> of each value, which the caller computes.
/// </summary>
/// <remarks>
/// <para>
/// The top 14 bits of a hash pick a register, and the register keeps the largest rank seen: the position of the first
/// set bit in the remaining 50 bits, counted from 1. A register only ever grows, so the sketch is the same for the same
/// set of hashes whatever their order and however often each repeats.
/// </para>
/// <para>
/// The estimate is the raw harmonic-mean estimate <c>alpha * m^2 / sum(2^-M[j])</c>; the powers of two come from
/// <see cref="Math.ScaleB(double, int)"/> (exact), the sum runs in register order, and every operation is an IEEE
/// <c>+ - * /</c>, so the result is the same on every platform. While the raw estimate is at most <c>2.5 m</c> and some
/// register is still empty, linear counting is used instead: <c>m * ln(m / V)</c> with <c>V</c> the number of empty
/// registers, through <see cref="DetMath.Ln(double)"/>. The result is rounded to a whole number, half to even. The
/// 64-bit hash needs no large-range correction. The empirical bias correction of HyperLogLog++ is not applied, so
/// the estimate has a small positive bias just above the switch to the raw estimate, inside the standard error.
/// </para>
/// <para>The relative standard error is 1.04 / sqrt(m), about 0.8125 percent; see <see cref="ErrorBound"/>.</para>
/// </remarks>
internal sealed class HyperLogLog
{
    /// <summary>The number of index bits.</summary>
    public const int Precision = 14;

    /// <summary>The number of registers, <c>m</c>.</summary>
    public const int RegisterCount = 1 << Precision;

    // The constant of the harmonic mean for m >= 128.
    private const double Alpha = 0.7213 / (1.0 + (1.079 / RegisterCount));

    // 2^-rank for every rank a register can hold (at most 64 - 14 + 1 = 51).
    private static readonly double[] InversePowers = BuildInversePowers();

    private readonly byte[] _registers = new byte[RegisterCount];
    private int _empty = RegisterCount;

    /// <summary>The relative standard error of an estimate, <c>1.04 / sqrt(m)</c>. The square root of 2^14 is exactly 128.</summary>
    public static double ErrorBound => 1.04 / Math.Sqrt(RegisterCount);

    /// <summary>The number of registers that have not seen a value yet.</summary>
    public int EmptyRegisters => _empty;

    /// <summary>The memory held by the sketch, in bytes: the registers and the object.</summary>
    public long EstimatedBytes => RegisterCount + 64;

    /// <summary>Adds the hash of a value.</summary>
    /// <param name="hash">The 64-bit hash.</param>
    public void Add(ulong hash)
    {
        var index = (int)(hash >> (64 - Precision));

        // The sentinel bit caps the rank at 51 when all the remaining bits are zero.
        var rest = (hash << Precision) | (1UL << (Precision - 1));
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

        const double M = RegisterCount;
        var estimate = Alpha * M * M / sum;
        if (estimate <= 2.5 * M && _empty > 0)
        {
            estimate = M * DetMath.Ln(M / _empty);
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
