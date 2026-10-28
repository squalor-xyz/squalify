// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The <c>table</c> section, present for tabular files.</summary>
/// <param name="Rows">Number of data rows.</param>
/// <param name="FieldCount">Minimum and maximum field count over the data rows.</param>
/// <param name="Fields">Field profiles in column order.</param>
/// <param name="RowClasses">Row classes of a delimited table.</param>
public sealed record TableSection(
    long Rows,
    MinMax FieldCount,
    IReadOnlyList<FieldProfile> Fields,
    RowClassesInfo? RowClasses = null);

/// <summary>The <c>table.rowClasses</c> object.</summary>
/// <param name="Count">Number of classes listed (at most <c>limits.rowClasses</c>); <c>Truncated &gt; 0</c> means more exist.</param>
/// <param name="Transitions">Number of class transitions.</param>
/// <param name="AllNullRows">Number of all-null rows.</param>
/// <param name="Truncated">Records counted past the class cap.</param>
/// <param name="Classes">Classes; the caller sorts by count descending, then id.</param>
public sealed record RowClassesInfo(
    long Count,
    long Transitions,
    long AllNullRows,
    long Truncated,
    IReadOnlyList<RowClass> Classes);

/// <summary>One row class.</summary>
/// <param name="Id">The class id, <c>rc:</c> plus a hash.</param>
/// <param name="Count">Records in the class.</param>
/// <param name="Fields">Inclusive index ranges of the non-null fields.</param>
/// <param name="FirstLine">First line of the class.</param>
/// <param name="LastLine">Last line of the class.</param>
public sealed record RowClass(string Id, long Count, IReadOnlyList<FieldRange> Fields, long FirstLine, long LastLine);

/// <summary>An inclusive range of field indexes, written as a two-element array.</summary>
/// <param name="First">First index.</param>
/// <param name="Last">Last index.</param>
public sealed record FieldRange(long First, long Last);

/// <summary>One entry of <c>table.fields[]</c>. Members that the schema always writes are written as null when they do not apply.</summary>
/// <param name="Index">Column index.</param>
/// <param name="Name">The header name, trimmed; null when the table has no header.</param>
/// <param name="NameParts">Name split into base and unit; null when the table has no header.</param>
/// <param name="Type">The field type.</param>
/// <param name="Dominant">The dominant vote.</param>
/// <param name="TypeVotes">Type votes.</param>
/// <param name="Count">Non-null plus null values.</param>
/// <param name="Nulls">Null values.</param>
/// <param name="NullsExcludingAllNullRows">Null values outside all-null rows.</param>
/// <param name="NullTokens">Null-token counts; null until the field accumulators fill it (sq-05).</param>
/// <param name="Flags">Value-shape flags; null until sq-05.</param>
/// <param name="Distinct">Distinct count; null until sq-05.</param>
/// <param name="Length">Length statistics, or null for an all-null field.</param>
/// <param name="Shapes">Shape masks; the caller sorts by count descending, then mask (ordinal). Null until sq-05.</param>
/// <param name="Monotonic"><c>increasing</c>, <c>decreasing</c>, <c>nonDecreasing</c>, <c>nonIncreasing</c>, <c>none</c>, or null.</param>
/// <param name="Constant">True when every non-null value is equal; null until sq-05.</param>
/// <param name="Unique">True when every non-null value is distinct; null until sq-05.</param>
/// <param name="NameHex">Hex of the original name bytes; present only when the name contains U+FFFD.</param>
/// <param name="RawName">The header text as in the file: quotes removed per the dialect, nothing else changed (spaces kept); null when the table has no header.</param>
/// <param name="Patterns">Counts of matched date and time patterns.</param>
/// <param name="Numeric">Numeric statistics.</param>
/// <param name="Temporal">Temporal statistics.</param>
/// <param name="StatsWithheld"><c>constant</c> when statistics are withheld.</param>
/// <param name="Unit">The unit from a units row. Values tier.</param>
/// <param name="Top">Most common values. Values tier.</param>
/// <param name="Text">Lexical minimum and maximum. Values tier.</param>
public sealed record FieldProfile(
    long Index,
    string? Name,
    NameParts? NameParts,
    string Type,
    DominantType Dominant,
    CountMap TypeVotes,
    long Count,
    long Nulls,
    long NullsExcludingAllNullRows,
    CountMap? NullTokens,
    FieldFlags? Flags,
    DistinctInfo? Distinct,
    LengthStats? Length,
    IReadOnlyList<Shape>? Shapes,
    string? Monotonic,
    bool? Constant,
    bool? Unique,
    string? NameHex = null,
    string? RawName = null,
    CountMap? Patterns = null,
    [property: ValueBearing] NumericStats? Numeric = null,
    [property: ValueBearing] TemporalStats? Temporal = null,
    string? StatsWithheld = null,
    [property: ValueBearing] string? Unit = null,
    [property: ValueBearing] TopValues? Top = null,
    [property: ValueBearing] TextStats? Text = null);

/// <summary>A header name split into base and unit.</summary>
/// <param name="Base">The name without the unit.</param>
/// <param name="Unit">The unit, or null when none was parsed.</param>
/// <param name="Convention">The header-unit convention: <c>nameParenUnit</c>, <c>nameBracketUnit</c>, <c>nameUnderscoreUnit</c>, <c>unitsRow</c> or <c>none</c>.</param>
/// <param name="UnknownUnit">True when the name carries unit text that is not in the unit table.</param>
public sealed record NameParts(string Base, string? Unit, string Convention, bool UnknownUnit = false);

/// <summary>The dominant type vote.</summary>
/// <param name="Type">The type with the most votes.</param>
/// <param name="Share">Its share of the votes.</param>
public sealed record DominantType(string Type, double Share);

/// <summary>Value-shape flag counts.</summary>
/// <param name="LeadingZero">Values with a leading zero.</param>
/// <param name="CommaDecimalLike">Values that look like comma decimals.</param>
/// <param name="Padded">Padded values.</param>
public sealed record FieldFlags(long LeadingZero, long CommaDecimalLike, long Padded);

/// <summary>A distinct count, exact or from a HyperLogLog sketch.</summary>
/// <param name="Value">The count.</param>
/// <param name="Approximate">False when the count is exact, true when it comes from the sketch.</param>
/// <param name="RelativeStdError">The relative standard error (one sigma) of a sketched count, 1.04 / sqrt(m); written only when <paramref name="Approximate"/> is true.</param>
public sealed record DistinctInfo(long Value, bool Approximate, double? RelativeStdError = null);

/// <summary>Numeric statistics of an <c>int</c> or <c>float</c> field.</summary>
/// <param name="Min">Minimum.</param>
/// <param name="Max">Maximum.</param>
/// <param name="Mean">Mean; null when the running computation overflowed to a non-finite value.</param>
/// <param name="Stddev">Sample standard deviation; null for fewer than two values or when the computation overflowed.</param>
/// <param name="Quantiles">Quantiles: exact up to the cap, from a Greenwald-Khanna sketch past it.</param>
/// <param name="Approximate">True when the quantiles come from a sketch, false when they are exact.</param>
/// <param name="RankError">The worst-case rank error of the sketched quantiles as a fraction of the value count (epsilon); null, and not written, unless <paramref name="Approximate"/> is true.</param>
public sealed record NumericStats(
    DescriptorNumber Min,
    DescriptorNumber Max,
    double? Mean,
    double? Stddev,
    Quantiles<DescriptorNumber> Quantiles,
    bool Approximate,
    double? RankError = null);

/// <summary>The fixed quantile set p01, p05, p25, p50, p75, p95 and p99.</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="P01">The 1st percentile.</param>
/// <param name="P05">The 5th percentile.</param>
/// <param name="P25">The 25th percentile.</param>
/// <param name="P50">The median.</param>
/// <param name="P75">The 75th percentile.</param>
/// <param name="P95">The 95th percentile.</param>
/// <param name="P99">The 99th percentile.</param>
public sealed record Quantiles<T>(T P01, T P05, T P25, T P50, T P75, T P95, T P99);

/// <summary>Temporal statistics of a <c>date</c>, <c>datetime</c> or <c>time</c> field.</summary>
/// <param name="Orders">Possible field orders, such as <c>YMD</c>.</param>
/// <param name="Ambiguous">True when more than one order remains.</param>
/// <param name="Clock"><c>12h</c> or <c>24h</c>, or null for dates.</param>
/// <param name="Min">Minimum as ISO 8601, or null when ambiguous.</param>
/// <param name="Max">Maximum as ISO 8601, or null when ambiguous.</param>
/// <param name="Quantiles">Quantiles as ISO 8601, or null when ambiguous.</param>
/// <param name="Approximate">True when the quantiles come from a sketch, false when they are exact, null when there are none.</param>
/// <param name="RankError">The worst-case rank error of the sketched quantiles as a fraction of the value count (epsilon); null, and not written, unless <paramref name="Approximate"/> is true.</param>
public sealed record TemporalStats(
    IReadOnlyList<string> Orders,
    bool Ambiguous,
    string? Clock,
    string? Min,
    string? Max,
    Quantiles<string>? Quantiles,
    bool? Approximate = null,
    double? RankError = null);

/// <summary>Length statistics, in characters.</summary>
/// <param name="Min">Shortest value.</param>
/// <param name="Max">Longest value.</param>
/// <param name="Mean">Mean length.</param>
public sealed record LengthStats(long Min, long Max, double Mean);

/// <summary>One shape mask and its count.</summary>
/// <param name="Mask">The mask, or <c>other</c> for the overflow bucket.</param>
/// <param name="Count">Values with this mask.</param>
public sealed record Shape(string Mask, long Count);

/// <summary>The <c>top</c> member: most common values. Values tier.</summary>
/// <param name="Items">Values by count.</param>
/// <param name="Approximate">True when counts come from a sketch.</param>
/// <param name="MaxError">Maximum count error of a sketch.</param>
public sealed record TopValues(IReadOnlyList<TopValue> Items, bool Approximate, long? MaxError = null);

/// <summary>A value and its count.</summary>
/// <param name="Value">The literal value.</param>
/// <param name="Count">Occurrences.</param>
public sealed record TopValue(string Value, long Count);

/// <summary>The <c>text</c> member: lexical minimum and maximum. Values tier.</summary>
/// <param name="Min">Lexical minimum.</param>
/// <param name="Max">Lexical maximum.</param>
public sealed record TextStats(string Min, string Max);
