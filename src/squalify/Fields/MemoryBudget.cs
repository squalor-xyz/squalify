// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The exact-value caps of one field, after the global budget.</summary>
/// <param name="Distinct">Exact distinct values kept.</param>
/// <param name="Quantile">Exact quantile values kept.</param>
/// <param name="Shapes">Shape masks kept.</param>
/// <param name="ShapeChars">Characters of shape mask text kept.</param>
internal readonly record struct FieldCaps(long Distinct, long Quantile, long Shapes, long ShapeChars);

/// <summary>
/// The global budget of exact values: a wide file would otherwise keep 2,000 fields x 100,000 doubles (1.6 GB). The
/// per-field cap is min(cap, budget / field count), in integer arithmetic. The same division applies to the characters
/// of shape mask text of a field, and the class also fixes the memory ceiling that a profile is tested against.
/// </summary>
internal static class MemoryBudget
{
    /// <summary>
    /// The fixed allowance for the I/O buffers and headroom, on top of the accumulators' estimate. It is measured as
    /// growth over a small-profile baseline, so the fixed cost of the runtime is not in it.
    /// </summary>
    public const long AllowanceBytes = 64L * 1024 * 1024;

    /// <summary>
    /// The growth of the peak working set over a baseline that a profile may reach: the peak sum of the accumulators'
    /// estimated bytes plus <see cref="AllowanceBytes"/>. It is a check on the accounting, not a limit the profiler
    /// enforces.
    /// </summary>
    /// <param name="estimatedBytes">The peak sum of the accumulators' estimated bytes.</param>
    /// <returns>The ceiling.</returns>
    public static long CeilingBytes(long estimatedBytes)
    {
        return estimatedBytes + AllowanceBytes;
    }

    /// <summary>The effective caps of each field of a table.</summary>
    /// <param name="nominal">The caps and the budget.</param>
    /// <param name="fieldCount">The number of fields.</param>
    /// <returns>The effective caps.</returns>
    public static FieldCaps Effective(Limits nominal, long fieldCount)
    {
        ArgumentNullException.ThrowIfNull(nominal);
        var fields = Math.Max(1, fieldCount);
        return new FieldCaps(
            Math.Min(nominal.DistinctPerField, nominal.Budget.DistinctValues / fields),
            Math.Min(nominal.QuantilePerField, nominal.Budget.QuantileValues / fields),
            nominal.ShapesPerField,
            Math.Min(nominal.ShapeCharsPerField, nominal.Budget.ShapeChars / fields));
    }

    /// <summary>The limits of a descriptor, with the effective per-field caps of a table.</summary>
    /// <param name="nominal">The caps and the budget.</param>
    /// <param name="caps">The effective caps.</param>
    /// <returns>The limits to write.</returns>
    public static Limits Record(Limits nominal, FieldCaps caps)
    {
        ArgumentNullException.ThrowIfNull(nominal);
        return nominal with { DistinctPerField = caps.Distinct, QuantilePerField = caps.Quantile, ShapeCharsPerField = caps.ShapeChars };
    }
}
