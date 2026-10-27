// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The <c>options</c> section: the options that affect output.</summary>
/// <param name="IncludeValues">True when literal values are included.</param>
/// <param name="IncludeValuesFor">Allow-listed names; the caller sorts them ordinally (<see cref="Create"/> does).</param>
public sealed record ProfileOptions(bool IncludeValues, IReadOnlyList<string> IncludeValuesFor)
{
    /// <summary>Creates options with the allow-list sorted ordinally.</summary>
    /// <param name="includeValues">True to include all literal values.</param>
    /// <param name="includeValuesFor">Allow-listed names, in any order.</param>
    /// <returns>The options.</returns>
    public static ProfileOptions Create(bool includeValues, IEnumerable<string> includeValuesFor)
    {
        ArgumentNullException.ThrowIfNull(includeValuesFor);
        return new ProfileOptions(includeValues, Ordinal.Sorted(includeValuesFor));
    }
}

/// <summary>The <c>limits</c> section: caps and table versions in effect.</summary>
/// <param name="DistinctPerField">Effective cap on exact distinct values per field.</param>
/// <param name="QuantilePerField">Effective cap on exact quantile values per field.</param>
/// <param name="ShapesPerField">Cap on shape masks per field.</param>
/// <param name="Regions">Cap on regions.</param>
/// <param name="HeaderSample">Number of records sampled for header detection.</param>
/// <param name="RunawayQuote">Longest open quoted field, in characters, before a delimited candidate is disqualified.</param>
/// <param name="FieldCountRuns">Most field-count runs a delimited candidate may have before it is disqualified as too irregular.</param>
/// <param name="RowClasses">Cap on row classes.</param>
/// <param name="CharsetTolerance">Tolerated invalid UTF-8 sequences.</param>
/// <param name="StdfTests">Cap on STDF test catalog entries.</param>
/// <param name="Budget">Global exact-value budget.</param>
/// <param name="NullTokens">Version of the null-token vocabulary.</param>
/// <param name="UnitTable">Version of the unit table.</param>
public sealed record Limits(
    long DistinctPerField,
    long QuantilePerField,
    long ShapesPerField,
    long Regions,
    long HeaderSample,
    long RunawayQuote,
    long FieldCountRuns,
    long RowClasses,
    CharsetTolerance CharsetTolerance,
    long StdfTests,
    Budget Budget,
    long NullTokens,
    long UnitTable)
{
    /// <summary>The caps and table versions of this tool version, as in <c>docs/descriptor.md</c>.</summary>
    public static Limits Default { get; } = new(
        DistinctPerField: 10_000,
        QuantilePerField: 100_000,
        ShapesPerField: 64,
        Regions: 1_000,
        HeaderSample: 1_000,
        RunawayQuote: 65_536,
        FieldCountRuns: 100_000,
        RowClasses: 64,
        CharsetTolerance: new CharsetTolerance(8, 65_536),
        StdfTests: 10_000,
        Budget: new Budget(10_000_000, 2_000_000),
        NullTokens: ValueClassifier.NullVocabularyVersion,
        UnitTable: squalor.squalify.UnitTable.Version);
}

/// <summary>Tolerated stray invalid UTF-8 sequences.</summary>
/// <param name="InvalidSequences">Maximum invalid sequences tolerated.</param>
/// <param name="PerBytes">Per this many bytes.</param>
public sealed record CharsetTolerance(long InvalidSequences, long PerBytes);

/// <summary>The global budget of exact values.</summary>
/// <param name="QuantileValues">Total exact quantile values.</param>
/// <param name="DistinctValues">Total exact distinct values.</param>
public sealed record Budget(long QuantileValues, long DistinctValues);
