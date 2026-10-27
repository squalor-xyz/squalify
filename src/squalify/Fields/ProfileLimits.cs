// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The caps that bound field profiling. They are internal so that tests can inject small ones; the effective values
/// (after <see cref="MemoryBudget"/>) are always written to the descriptor's <c>limits</c>. The version of the null
/// vocabulary is deliberately not here: <c>limits.nullTokens</c> is always <see cref="ValueClassifier.NullVocabularyVersion"/>.
/// </summary>
/// <param name="DistinctPerField">Exact distinct values kept per field.</param>
/// <param name="QuantilePerField">Exact quantile values kept per field.</param>
/// <param name="ShapesPerField">Shape masks kept per field.</param>
/// <param name="RowClasses">Row classes kept, with exact counts; the records of later classes are counted as truncated.</param>
/// <param name="Budget">The global budget of exact values.</param>
internal sealed record ProfileLimits(long DistinctPerField, long QuantilePerField, long ShapesPerField, long RowClasses, Budget Budget)
{
    /// <summary>The caps of this tool version, as in <c>docs/descriptor.md</c>.</summary>
    public static ProfileLimits Default { get; } = new(10_000, 100_000, 64, 64, new Budget(10_000_000, 2_000_000));

    /// <summary>Applies these caps to a limits record, leaving every other member as it is.</summary>
    /// <param name="limits">The limits to start from.</param>
    /// <returns>The limits with these caps.</returns>
    public Limits ApplyTo(Limits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return limits with
        {
            DistinctPerField = DistinctPerField,
            QuantilePerField = QuantilePerField,
            ShapesPerField = ShapesPerField,
            RowClasses = RowClasses,
            Budget = Budget,
        };
    }
}
