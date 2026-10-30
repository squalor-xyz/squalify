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
    RowClassesInfo? RowClasses = null)
{
    /// <inheritdoc />
    public bool Equals(TableSection? other)
    {
        return other is not null
            && EqualityComparer<long>.Default.Equals(Rows, other.Rows)
            && EqualityComparer<MinMax>.Default.Equals(FieldCount, other.FieldCount)
            && ValueEquality.ListEquals(Fields, other.Fields)
            && EqualityComparer<RowClassesInfo?>.Default.Equals(RowClasses, other.RowClasses);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Rows);
        hash.Add(FieldCount);
        ValueEquality.AddList(ref hash, Fields);
        hash.Add(RowClasses);
        return hash.ToHashCode();
    }
}

/// <summary>The <c>table.rowClasses</c> object.</summary>
/// <param name="Count">Number of classes listed (at most <c>limits.rowClasses</c>); <c>Truncated &gt; 0</c> means more exist.</param>
/// <param name="Transitions">Number of class transitions.</param>
/// <param name="AllNullRows">Number of all-null rows.</param>
/// <param name="Truncated">Records counted past the class cap.</param>
/// <param name="Classes">Classes; the caller sorts by count descending, then id.</param>
/// <param name="AllNullRowSpellings">The all-null records whose every cell has one exact spelling (raw, not trimmed, quotes removed), by that spelling; null reads as no entries.</param>
/// <param name="AllNullRowSpellingsMixed">All-null records whose cells do not share one spelling.</param>
/// <param name="AllNullRowSpellingsOther">All-null records of one spelling that is not a key of <paramref name="AllNullRowSpellings"/> (past 64 spellings, or longer than 256 characters).</param>
/// <param name="AllNullRowSpellingsOtherDistinct">The number of distinct spellings among those records (each counted once however often it recurs), exact up to <c>limits.nullSpellings.maxDistinctOther</c> (64); 0 when <paramref name="AllNullRowSpellingsOther"/> is 0.</param>
/// <param name="AllNullRowSpellingsOtherDistinctCapped">True when more than that many distinct spellings were seen, so <paramref name="AllNullRowSpellingsOtherDistinct"/> is a lower bound.</param>
public sealed record RowClassesInfo(
    long Count,
    long Transitions,
    long AllNullRows,
    long Truncated,
    IReadOnlyList<RowClass> Classes,
    CountMap? AllNullRowSpellings = null,
    long AllNullRowSpellingsMixed = 0,
    long AllNullRowSpellingsOther = 0,
    long AllNullRowSpellingsOtherDistinct = 0,
    bool AllNullRowSpellingsOtherDistinctCapped = false)
{
    /// <inheritdoc />
    public bool Equals(RowClassesInfo? other)
    {
        return other is not null
            && EqualityComparer<long>.Default.Equals(Count, other.Count)
            && EqualityComparer<long>.Default.Equals(Transitions, other.Transitions)
            && EqualityComparer<long>.Default.Equals(AllNullRows, other.AllNullRows)
            && EqualityComparer<long>.Default.Equals(Truncated, other.Truncated)
            && ValueEquality.ListEquals(Classes, other.Classes)
            && EqualityComparer<CountMap?>.Default.Equals(AllNullRowSpellings, other.AllNullRowSpellings)
            && EqualityComparer<long>.Default.Equals(AllNullRowSpellingsMixed, other.AllNullRowSpellingsMixed)
            && EqualityComparer<long>.Default.Equals(AllNullRowSpellingsOther, other.AllNullRowSpellingsOther)
            && EqualityComparer<long>.Default.Equals(AllNullRowSpellingsOtherDistinct, other.AllNullRowSpellingsOtherDistinct)
            && EqualityComparer<bool>.Default.Equals(AllNullRowSpellingsOtherDistinctCapped, other.AllNullRowSpellingsOtherDistinctCapped);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Count);
        hash.Add(Transitions);
        hash.Add(AllNullRows);
        hash.Add(Truncated);
        ValueEquality.AddList(ref hash, Classes);
        hash.Add(AllNullRowSpellings);
        hash.Add(AllNullRowSpellingsMixed);
        hash.Add(AllNullRowSpellingsOther);
        hash.Add(AllNullRowSpellingsOtherDistinct);
        hash.Add(AllNullRowSpellingsOtherDistinctCapped);
        return hash.ToHashCode();
    }
}

/// <summary>One row class.</summary>
/// <param name="Id">The class id, <c>rc:</c> plus a hash.</param>
/// <param name="Count">Records in the class.</param>
/// <param name="Fields">Inclusive index ranges of the non-null fields.</param>
/// <param name="FirstLine">First line of the class.</param>
/// <param name="LastLine">Last line of the class.</param>
public sealed record RowClass(string Id, long Count, IReadOnlyList<FieldRange> Fields, long FirstLine, long LastLine)
{
    /// <inheritdoc />
    public bool Equals(RowClass? other)
    {
        return other is not null
            && EqualityComparer<string>.Default.Equals(Id, other.Id)
            && EqualityComparer<long>.Default.Equals(Count, other.Count)
            && ValueEquality.ListEquals(Fields, other.Fields)
            && EqualityComparer<long>.Default.Equals(FirstLine, other.FirstLine)
            && EqualityComparer<long>.Default.Equals(LastLine, other.LastLine);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Count);
        ValueEquality.AddList(ref hash, Fields);
        hash.Add(FirstLine);
        hash.Add(LastLine);
        return hash.ToHashCode();
    }
}

/// <summary>An inclusive range of field indexes, written as a two-element array.</summary>
/// <param name="First">First index.</param>
/// <param name="Last">Last index.</param>
public sealed record FieldRange(long First, long Last);

/// <summary>
/// One entry of <c>table.fields[]</c>. Members that the schema always writes are written as null when they do not apply.
/// In a layout-only profile (<see cref="Descriptor.LayoutOnly"/>) only <see cref="Index"/>, <see cref="Name"/>,
/// <see cref="RawName"/>, <see cref="NameHex"/> and <see cref="NameParts"/> are set: <see cref="Type"/>,
/// <see cref="Dominant"/>, <see cref="TypeVotes"/>, <see cref="Count"/>, <see cref="Nulls"/> and
/// <see cref="NullsExcludingAllNullRows"/> are null (not computed), and so is every other member that is not a name.
/// The record has no positional constructor or deconstruction (sq-49): build it by name, with the members that have no
/// default set (the ones the old constructor required), so that a member added later is neither a constructor nor a
/// deconstruction change.
/// </summary>
public sealed record FieldProfile
{
    /// <summary>Column index.</summary>
    public required long Index { get; init; }

    /// <summary>The header name, trimmed; null when the table has no header.</summary>
    public required string? Name { get; init; }

    /// <summary>Name split into base and unit; null when the table has no header.</summary>
    public required NameParts? NameParts { get; init; }

    /// <summary>The field type; null only in a layout-only profile.</summary>
    public required string? Type { get; init; }

    /// <summary>The dominant vote; null only in a layout-only profile.</summary>
    public required DominantType? Dominant { get; init; }

    /// <summary>Type votes; null only in a layout-only profile.</summary>
    public required CountMap? TypeVotes { get; init; }

    /// <summary>Non-null plus null values; null only in a layout-only profile.</summary>
    public required long? Count { get; init; }

    /// <summary>Null values; null only in a layout-only profile.</summary>
    public required long? Nulls { get; init; }

    /// <summary>Null values outside all-null rows; null only in a layout-only profile.</summary>
    public required long? NullsExcludingAllNullRows { get; init; }

    /// <summary>Null-token counts; null until the field accumulators fill it (sq-05).</summary>
    public required CountMap? NullTokens { get; init; }

    /// <summary>Value-shape flags; null until sq-05.</summary>
    public required FieldFlags? Flags { get; init; }

    /// <summary>Distinct count; null until sq-05.</summary>
    public required DistinctInfo? Distinct { get; init; }

    /// <summary>Length statistics, or null for an all-null field.</summary>
    public required LengthStats? Length { get; init; }

    /// <summary>Shape masks; the caller sorts by count descending, then mask (ordinal). Null until sq-05.</summary>
    public required IReadOnlyList<Shape>? Shapes { get; init; }

    /// <summary><c>increasing</c>, <c>decreasing</c>, <c>nonDecreasing</c>, <c>nonIncreasing</c>, <c>none</c>, or null.</summary>
    public required string? Monotonic { get; init; }

    /// <summary>True when every non-null value is equal; null until sq-05.</summary>
    public required bool? Constant { get; init; }

    /// <summary>True when every non-null value is distinct; null until sq-05.</summary>
    public required bool? Unique { get; init; }

    /// <summary>Hex of the original name bytes; present only when the name contains U+FFFD.</summary>
    public string? NameHex { get; init; }

    /// <summary>Counts of matched date and time patterns.</summary>
    public CountMap? Patterns { get; init; }

    /// <summary>Numeric statistics.</summary>
    [ValueBearing]
    public NumericStats? Numeric { get; init; }

    /// <summary>Temporal statistics.</summary>
    [ValueBearing]
    public TemporalStats? Temporal { get; init; }

    /// <summary><c>constant</c> when statistics are withheld.</summary>
    public string? StatsWithheld { get; init; }

    /// <summary>The unit from a units row. Values tier.</summary>
    [ValueBearing]
    public string? Unit { get; init; }

    /// <summary>Most common values. Values tier.</summary>
    [ValueBearing]
    public TopValues? Top { get; init; }

    /// <summary>Lexical minimum and maximum. Values tier.</summary>
    [ValueBearing]
    public TextStats? Text { get; init; }

    /// <summary>The header text as in the file: quotes removed per the dialect, nothing else changed (spaces kept); null when the table has no header. Written right after <c>name</c>.</summary>
    public string? RawName { get; init; }

    /// <summary>The null values by exact spelling (the field text after dialect unquoting, not trimmed), over all data records, all-null records included; null reads as no entries.</summary>
    public CountMap? NullSpellings { get; init; }

    /// <summary>Null values whose spelling is not in <see cref="NullSpellings"/> because the field already kept 64 spellings, or the spelling is longer than 256 characters.</summary>
    public long NullSpellingsOther { get; init; }

    /// <summary>The number of distinct spellings among the <see cref="NullSpellingsOther"/> values (each counted once however often it recurs), exact up to <c>limits.nullSpellings.maxDistinctOther</c> (64); 0 when <see cref="NullSpellingsOther"/> is 0.</summary>
    public long NullSpellingsOtherDistinct { get; init; }

    /// <summary>True when more than that many distinct spellings were seen, so <see cref="NullSpellingsOtherDistinct"/> is a lower bound.</summary>
    public bool NullSpellingsOtherDistinctCapped { get; init; }

    /// <inheritdoc />
    public bool Equals(FieldProfile? other)
    {
        return other is not null
            && EqualityComparer<long>.Default.Equals(Index, other.Index)
            && EqualityComparer<string?>.Default.Equals(Name, other.Name)
            && EqualityComparer<NameParts?>.Default.Equals(NameParts, other.NameParts)
            && EqualityComparer<string?>.Default.Equals(Type, other.Type)
            && EqualityComparer<DominantType?>.Default.Equals(Dominant, other.Dominant)
            && EqualityComparer<CountMap?>.Default.Equals(TypeVotes, other.TypeVotes)
            && EqualityComparer<long?>.Default.Equals(Count, other.Count)
            && EqualityComparer<long?>.Default.Equals(Nulls, other.Nulls)
            && EqualityComparer<long?>.Default.Equals(NullsExcludingAllNullRows, other.NullsExcludingAllNullRows)
            && EqualityComparer<CountMap?>.Default.Equals(NullTokens, other.NullTokens)
            && EqualityComparer<FieldFlags?>.Default.Equals(Flags, other.Flags)
            && EqualityComparer<DistinctInfo?>.Default.Equals(Distinct, other.Distinct)
            && EqualityComparer<LengthStats?>.Default.Equals(Length, other.Length)
            && ValueEquality.ListEquals(Shapes, other.Shapes)
            && EqualityComparer<string?>.Default.Equals(Monotonic, other.Monotonic)
            && EqualityComparer<bool?>.Default.Equals(Constant, other.Constant)
            && EqualityComparer<bool?>.Default.Equals(Unique, other.Unique)
            && EqualityComparer<string?>.Default.Equals(NameHex, other.NameHex)
            && EqualityComparer<CountMap?>.Default.Equals(Patterns, other.Patterns)
            && EqualityComparer<NumericStats?>.Default.Equals(Numeric, other.Numeric)
            && EqualityComparer<TemporalStats?>.Default.Equals(Temporal, other.Temporal)
            && EqualityComparer<string?>.Default.Equals(StatsWithheld, other.StatsWithheld)
            && EqualityComparer<string?>.Default.Equals(Unit, other.Unit)
            && EqualityComparer<TopValues?>.Default.Equals(Top, other.Top)
            && EqualityComparer<TextStats?>.Default.Equals(Text, other.Text)
            && EqualityComparer<string?>.Default.Equals(RawName, other.RawName)
            && EqualityComparer<CountMap?>.Default.Equals(NullSpellings, other.NullSpellings)
            && EqualityComparer<long>.Default.Equals(NullSpellingsOther, other.NullSpellingsOther)
            && EqualityComparer<long>.Default.Equals(NullSpellingsOtherDistinct, other.NullSpellingsOtherDistinct)
            && EqualityComparer<bool>.Default.Equals(NullSpellingsOtherDistinctCapped, other.NullSpellingsOtherDistinctCapped);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Index);
        hash.Add(Name);
        hash.Add(NameParts);
        hash.Add(Type);
        hash.Add(Dominant);
        hash.Add(TypeVotes);
        hash.Add(Count);
        hash.Add(Nulls);
        hash.Add(NullsExcludingAllNullRows);
        hash.Add(NullTokens);
        hash.Add(Flags);
        hash.Add(Distinct);
        hash.Add(Length);
        ValueEquality.AddList(ref hash, Shapes);
        hash.Add(Monotonic);
        hash.Add(Constant);
        hash.Add(Unique);
        hash.Add(NameHex);
        hash.Add(Patterns);
        hash.Add(Numeric);
        hash.Add(Temporal);
        hash.Add(StatsWithheld);
        hash.Add(Unit);
        hash.Add(Top);
        hash.Add(Text);
        hash.Add(RawName);
        hash.Add(NullSpellings);
        hash.Add(NullSpellingsOther);
        hash.Add(NullSpellingsOtherDistinct);
        hash.Add(NullSpellingsOtherDistinctCapped);
        return hash.ToHashCode();
    }
}

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
    double? RankError = null)
{
    /// <inheritdoc />
    public bool Equals(TemporalStats? other)
    {
        return other is not null
            && ValueEquality.ListEquals(Orders, other.Orders)
            && EqualityComparer<bool>.Default.Equals(Ambiguous, other.Ambiguous)
            && EqualityComparer<string?>.Default.Equals(Clock, other.Clock)
            && EqualityComparer<string?>.Default.Equals(Min, other.Min)
            && EqualityComparer<string?>.Default.Equals(Max, other.Max)
            && EqualityComparer<Quantiles<string>?>.Default.Equals(Quantiles, other.Quantiles)
            && EqualityComparer<bool?>.Default.Equals(Approximate, other.Approximate)
            && EqualityComparer<double?>.Default.Equals(RankError, other.RankError);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        ValueEquality.AddList(ref hash, Orders);
        hash.Add(Ambiguous);
        hash.Add(Clock);
        hash.Add(Min);
        hash.Add(Max);
        hash.Add(Quantiles);
        hash.Add(Approximate);
        hash.Add(RankError);
        return hash.ToHashCode();
    }
}

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
public sealed record TopValues(IReadOnlyList<TopValue> Items, bool Approximate, long? MaxError = null)
{
    /// <inheritdoc />
    public bool Equals(TopValues? other)
    {
        return other is not null
            && ValueEquality.ListEquals(Items, other.Items)
            && EqualityComparer<bool>.Default.Equals(Approximate, other.Approximate)
            && EqualityComparer<long?>.Default.Equals(MaxError, other.MaxError);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        ValueEquality.AddList(ref hash, Items);
        hash.Add(Approximate);
        hash.Add(MaxError);
        return hash.ToHashCode();
    }
}

/// <summary>A value and its count.</summary>
/// <param name="Value">The literal value.</param>
/// <param name="Count">Occurrences.</param>
public sealed record TopValue(string Value, long Count);

/// <summary>The <c>text</c> member: lexical minimum and maximum. Values tier.</summary>
/// <param name="Min">Lexical minimum.</param>
/// <param name="Max">Lexical maximum.</param>
public sealed record TextStats(string Min, string Max);
