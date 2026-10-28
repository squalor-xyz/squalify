// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The <c>options</c> section: the options that affect output.</summary>
/// <param name="IncludeValues">True when literal values are included.</param>
/// <param name="IncludeValuesFor">Allow-listed names, a sorted set: ordinal order, no exact duplicates (<see cref="Create"/> does both).</param>
/// <param name="MaxBytes">
/// The byte limit, or null to read the whole source. A positive limit makes squalify read at most that many bytes, cut
/// at the last whole line of a text file, and write the <c>scope</c> section. Two exceptions to "at most": a CRLF that
/// straddles the limit adds one code unit, and a first record longer than the limit is read to its end, however long
/// (no ceiling). A limit of zero or less is rejected by <see cref="Profiler"/>. It is not written to the
/// <c>options</c> section: <c>scope.maxBytes</c> carries it.
/// </param>
/// <param name="MaxFirstRecordBytes">
/// The first-record ceiling, or null for none (the default). It is a companion of <paramref name="MaxBytes"/> and is
/// rejected by <see cref="Profiler"/> without it. When the first record of a text file, its line terminator included, is
/// longer than this many bytes, <see cref="Profiler"/> throws <see cref="SqualifyException"/> and profiles nothing; it
/// reads no further than this many bytes plus the one code unit that tells a CR from a CRLF. A CRLF counts 2 bytes, a
/// UTF-16 unit 2 and a UTF-32 unit 4. A binary file is not subject. A value of zero or less is rejected by
/// <see cref="Profiler"/>. A profile that is not refused is the same as one without the ceiling, so it is not written
/// to the <c>options</c> section.
/// </param>
public sealed record ProfileOptions(bool IncludeValues, IReadOnlyList<string> IncludeValuesFor, long? MaxBytes = null, long? MaxFirstRecordBytes = null)
{
    /// <summary>Creates options with the allow-list as a sorted set: sorted ordinally, exact duplicates dropped.</summary>
    /// <param name="includeValues">True to include all literal values.</param>
    /// <param name="includeValuesFor">Allow-listed names, in any order.</param>
    /// <param name="maxBytes">The byte limit, or null for none.</param>
    /// <param name="maxFirstRecordBytes">The first-record ceiling, or null for none; needs <paramref name="maxBytes"/>.</param>
    /// <returns>The options.</returns>
    public static ProfileOptions Create(bool includeValues, IEnumerable<string> includeValuesFor, long? maxBytes = null, long? maxFirstRecordBytes = null)
    {
        ArgumentNullException.ThrowIfNull(includeValuesFor);
        return new ProfileOptions(includeValues, Ordinal.Sorted(includeValuesFor.Distinct(StringComparer.Ordinal)), maxBytes, maxFirstRecordBytes);
    }
}

/// <summary>The <c>limits</c> section: caps and table versions in effect.</summary>
/// <param name="DistinctPerField">Effective cap on exact distinct values per field: min(cap, budget / field count).</param>
/// <param name="QuantilePerField">Effective cap on exact quantile values per field: min(cap, budget / field count).</param>
/// <param name="ShapesPerField">Cap on shape masks per field.</param>
/// <param name="ShapeCharsPerField">Effective cap on characters of shape mask text per field: min(cap, budget / field count).</param>
/// <param name="Regions">The most blocks of a delimited file that are recognised; later candidates are counted in <c>blocksTruncated</c>.</param>
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
    long ShapeCharsPerField,
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
        DistinctPerField: ProfileLimits.Default.DistinctPerField,
        QuantilePerField: ProfileLimits.Default.QuantilePerField,
        ShapesPerField: ProfileLimits.Default.ShapesPerField,
        ShapeCharsPerField: ProfileLimits.Default.ShapeCharsPerField,
        Regions: 1_000,
        HeaderSample: 1_000,
        RunawayQuote: 65_536,
        FieldCountRuns: 100_000,
        RowClasses: ProfileLimits.Default.RowClasses,
        CharsetTolerance: new CharsetTolerance(8, 65_536),
        StdfTests: 10_000,
        Budget: ProfileLimits.Default.Budget,
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
/// <param name="ShapeChars">Total characters of shape mask text; a field keeps at most this divided by the field count (see <c>ShapeCharsPerField</c>).</param>
public sealed record Budget(long QuantileValues, long DistinctValues, long ShapeChars);
