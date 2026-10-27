// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The exact-value caps of one field, after the global budget.</summary>
/// <param name="Distinct">Exact distinct values kept.</param>
/// <param name="Quantile">Exact quantile values kept.</param>
/// <param name="Shapes">Shape masks kept.</param>
internal readonly record struct FieldCaps(long Distinct, long Quantile, long Shapes);

/// <summary>
/// The global budget of exact values: a wide file would otherwise keep 2,000 fields x 100,000 doubles (1.6 GB). The
/// per-field cap is min(cap, budget / field count), in integer arithmetic.
/// </summary>
internal static class MemoryBudget
{
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
            nominal.ShapesPerField);
    }

    /// <summary>The limits of a descriptor, with the effective per-field caps of a table.</summary>
    /// <param name="nominal">The caps and the budget.</param>
    /// <param name="caps">The effective caps.</param>
    /// <returns>The limits to write.</returns>
    public static Limits Record(Limits nominal, FieldCaps caps)
    {
        ArgumentNullException.ThrowIfNull(nominal);
        return nominal with { DistinctPerField = caps.Distinct, QuantilePerField = caps.Quantile };
    }
}
