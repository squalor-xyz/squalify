// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The exact-value caps of one field, after the global budget, and the precision of its sketches.</summary>
/// <param name="Distinct">Exact distinct values kept.</param>
/// <param name="Quantile">Exact quantile values kept.</param>
/// <param name="Shapes">Shape masks kept.</param>
/// <param name="ShapeChars">Characters of shape mask text kept.</param>
/// <param name="HllPrecision">The index bits of the HyperLogLog sketch the field makes past its distinct cap (<see cref="HyperLogLog"/>); 14 unless the sketches are scaled.</param>
/// <param name="GkInverseEpsilon"><c>1 / epsilon</c> of the Greenwald-Khanna summaries the field makes past its quantile cap (<see cref="GreenwaldKhanna"/>); 1,000 unless the sketches are scaled.</param>
internal readonly record struct FieldCaps(
    long Distinct,
    long Quantile,
    long Shapes,
    long ShapeChars,
    int HllPrecision = HyperLogLog.Precision,
    int GkInverseEpsilon = GreenwaldKhanna.InverseEpsilon);

/// <summary>
/// The global budget of exact values: a wide file would otherwise keep 2,000 fields x 100,000 doubles (1.6 GB). The
/// per-field cap is min(cap, budget / field count), in integer arithmetic. The same division applies to the characters
/// of shape mask text of a field. The sketches that a field makes past its caps are budgeted too: their precision is
/// scaled by the field count (<see cref="SketchLevel(long)"/>) so that every field's sketches, at their nominal size,
/// fit <see cref="AllowanceBytes"/> together. The class also fixes the memory ceiling that a profile is tested against.
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

    /// <summary>
    /// The most Greenwald-Khanna summaries one field holds at once: one for a numeric field, and two for a date field
    /// while its day-month-year and month-day-year readings both fit (the year-first reading excludes both of those).
    /// </summary>
    public const int MaxSummariesPerField = 2;

    // The precision levels, finest first: the HyperLogLog index bits and 1 / epsilon of the Greenwald-Khanna summaries.
    // Each step halves both sketches. Level 0 is the full precision of a narrow table (p 14, epsilon 0.001).
    private static readonly (int HllPrecision, int GkInverseEpsilon)[] Levels =
    [
        (14, 1_000),
        (13, 500),
        (12, 250),
        (11, 125),
        (10, 64),
        (9, 32),
        (8, GreenwaldKhanna.MinInverseEpsilon),
    ];

    /// <summary>The nominal memory of the sketches of one field at a precision level: a HyperLogLog and <see cref="MaxSummariesPerField"/> summaries.</summary>
    /// <param name="hllPrecision">The index bits of the HyperLogLog.</param>
    /// <param name="gkInverseEpsilon"><c>1 / epsilon</c> of the summaries.</param>
    /// <returns>The bytes.</returns>
    public static long SketchBytesPerField(int hllPrecision, int gkInverseEpsilon)
    {
        return HyperLogLog.BytesOf(hllPrecision) + (MaxSummariesPerField * GreenwaldKhanna.NominalBytes(gkInverseEpsilon));
    }

    /// <summary>
    /// The finest precision level whose sketches, for every field of the table, fit <see cref="AllowanceBytes"/>:
    /// a function of the field count only, in integer arithmetic. Past the coarsest level (about 16,600 fields) the
    /// coarsest is used and the allowance is exceeded.
    /// </summary>
    /// <param name="fieldCount">The number of fields.</param>
    /// <returns>The index into the levels, 0 for the full precision.</returns>
    public static int SketchLevel(long fieldCount)
    {
        var share = AllowanceBytes / Math.Max(1, fieldCount);
        for (var level = 0; level < Levels.Length; level++)
        {
            if (SketchBytesPerField(Levels[level].HllPrecision, Levels[level].GkInverseEpsilon) <= share)
            {
                return level;
            }
        }

        return Levels.Length - 1;
    }

    /// <summary>The precision of the sketches of each field of a table.</summary>
    /// <param name="fieldCount">The number of fields.</param>
    /// <returns>The HyperLogLog index bits and <c>1 / epsilon</c> of the summaries.</returns>
    public static (int HllPrecision, int GkInverseEpsilon) SketchPrecision(long fieldCount)
    {
        return Levels[SketchLevel(fieldCount)];
    }

    /// <summary>The effective caps of each field of a table.</summary>
    /// <param name="nominal">The caps and the budget.</param>
    /// <param name="fieldCount">The number of fields.</param>
    /// <returns>The effective caps.</returns>
    public static FieldCaps Effective(Limits nominal, long fieldCount)
    {
        ArgumentNullException.ThrowIfNull(nominal);
        var fields = Math.Max(1, fieldCount);
        var (hllPrecision, gkInverseEpsilon) = SketchPrecision(fields);
        return new FieldCaps(
            Math.Min(nominal.DistinctPerField, nominal.Budget.DistinctValues / fields),
            Math.Min(nominal.QuantilePerField, nominal.Budget.QuantileValues / fields),
            nominal.ShapesPerField,
            Math.Min(nominal.ShapeCharsPerField, nominal.Budget.ShapeChars / fields),
            hllPrecision,
            gkInverseEpsilon);
    }

    /// <summary>
    /// The limits of a descriptor, with the effective per-field caps of a table, and <c>limits.sketches</c> when the
    /// sketches are below the full precision (a narrow table writes none, so its descriptor is what it was before).
    /// </summary>
    /// <param name="nominal">The caps and the budget.</param>
    /// <param name="caps">The effective caps.</param>
    /// <returns>The limits to write.</returns>
    public static Limits Record(Limits nominal, FieldCaps caps)
    {
        ArgumentNullException.ThrowIfNull(nominal);
        var scaled = caps.HllPrecision != HyperLogLog.Precision || caps.GkInverseEpsilon != GreenwaldKhanna.InverseEpsilon;
        return nominal with
        {
            DistinctPerField = caps.Distinct,
            QuantilePerField = caps.Quantile,
            ShapeCharsPerField = caps.ShapeChars,
            Sketches = scaled ? new SketchLimits(caps.HllPrecision, GreenwaldKhanna.EpsilonOf(caps.GkInverseEpsilon), AllowanceBytes) : null,
        };
    }
}
