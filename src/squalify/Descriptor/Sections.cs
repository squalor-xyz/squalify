// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>One <c>[section]</c> of a key=value document (an entry of <c>sections[]</c>).</summary>
/// <param name="Name">The section name.</param>
/// <param name="Line">The 1-based line of the header.</param>
/// <param name="Keys">Normalized keys in file order.</param>
/// <param name="Values">Values aligned with <paramref name="Keys"/>; an entry that is not opted in is null. Values tier.</param>
public sealed record KeyValueSection(string Name, long Line, IReadOnlyList<string> Keys, [property: ValueBearing] IReadOnlyList<string?>? Values = null)
{
    /// <inheritdoc />
    public bool Equals(KeyValueSection? other)
    {
        return other is not null
            && EqualityComparer<string>.Default.Equals(Name, other.Name)
            && EqualityComparer<long>.Default.Equals(Line, other.Line)
            && ValueEquality.ListEquals(Keys, other.Keys)
            && ValueEquality.ListEquals(Values, other.Values);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Line);
        ValueEquality.AddList(ref hash, Keys);
        ValueEquality.AddList(ref hash, Values);
        return hash.ToHashCode();
    }
}

/// <summary>The <c>binary</c> section.</summary>
/// <param name="Bytes">Whole-file byte statistics.</param>
/// <param name="Blocks">Statistics of the per-block entropy, or null before sq-10 adds them.</param>
/// <param name="Segments">The fixed 16-segment entropy profile, or null before sq-10 adds it.</param>
public sealed record BinarySection(ByteStats Bytes, BlockStats? Blocks = null, IReadOnlyList<double>? Segments = null)
{
    /// <inheritdoc />
    public bool Equals(BinarySection? other)
    {
        return other is not null
            && EqualityComparer<ByteStats>.Default.Equals(Bytes, other.Bytes)
            && EqualityComparer<BlockStats?>.Default.Equals(Blocks, other.Blocks)
            && ValueEquality.ListEquals(Segments, other.Segments);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Bytes);
        hash.Add(Blocks);
        ValueEquality.AddList(ref hash, Segments);
        return hash.ToHashCode();
    }
}

/// <summary>Whole-file byte statistics.</summary>
/// <param name="Histogram">The 256-bin byte histogram.</param>
/// <param name="Nul">Count of NUL bytes.</param>
/// <param name="PrintableAscii">Count of printable ASCII bytes.</param>
/// <param name="High">Count of bytes at or above 0x80.</param>
/// <param name="Entropy">Shannon entropy in bits per byte.</param>
public sealed record ByteStats(IReadOnlyList<long> Histogram, long Nul, long PrintableAscii, long High, double Entropy)
{
    /// <inheritdoc />
    public bool Equals(ByteStats? other)
    {
        return other is not null
            && ValueEquality.ListEquals(Histogram, other.Histogram)
            && EqualityComparer<long>.Default.Equals(Nul, other.Nul)
            && EqualityComparer<long>.Default.Equals(PrintableAscii, other.PrintableAscii)
            && EqualityComparer<long>.Default.Equals(High, other.High)
            && EqualityComparer<double>.Default.Equals(Entropy, other.Entropy);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        ValueEquality.AddList(ref hash, Histogram);
        hash.Add(Nul);
        hash.Add(PrintableAscii);
        hash.Add(High);
        hash.Add(Entropy);
        return hash.ToHashCode();
    }
}

/// <summary>Per-block entropy statistics.</summary>
/// <param name="Count">Number of blocks.</param>
/// <param name="Min">Lowest block entropy.</param>
/// <param name="Max">Highest block entropy.</param>
/// <param name="Mean">Mean block entropy.</param>
public sealed record BlockStats(long Count, double Min, double Max, double Mean);

/// <summary>The <c>stdf</c> section.</summary>
/// <param name="Records">Record-type counts; the caller sorts by type, then subtype.</param>
/// <param name="FirstOccurrenceOrder">Record names in order of first occurrence.</param>
/// <param name="FarFirst">True when FAR is the first record.</param>
/// <param name="MrrLast">True when MRR is the last record.</param>
/// <param name="Tests">The test catalog.</param>
/// <param name="TestsTruncated">Tests counted past the cap.</param>
public sealed record StdfSection(
    IReadOnlyList<StdfRecordCount> Records,
    IReadOnlyList<string> FirstOccurrenceOrder,
    bool FarFirst,
    bool MrrLast,
    IReadOnlyList<StdfTest> Tests,
    long TestsTruncated)
{
    /// <inheritdoc />
    public bool Equals(StdfSection? other)
    {
        return other is not null
            && ValueEquality.ListEquals(Records, other.Records)
            && ValueEquality.ListEquals(FirstOccurrenceOrder, other.FirstOccurrenceOrder)
            && EqualityComparer<bool>.Default.Equals(FarFirst, other.FarFirst)
            && EqualityComparer<bool>.Default.Equals(MrrLast, other.MrrLast)
            && ValueEquality.ListEquals(Tests, other.Tests)
            && EqualityComparer<long>.Default.Equals(TestsTruncated, other.TestsTruncated);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        ValueEquality.AddList(ref hash, Records);
        ValueEquality.AddList(ref hash, FirstOccurrenceOrder);
        hash.Add(FarFirst);
        hash.Add(MrrLast);
        ValueEquality.AddList(ref hash, Tests);
        hash.Add(TestsTruncated);
        return hash.ToHashCode();
    }
}

/// <summary>Count of one STDF record type.</summary>
/// <param name="Name">The record name, such as <c>PTR</c>.</param>
/// <param name="Typ">REC_TYP.</param>
/// <param name="Sub">REC_SUB.</param>
/// <param name="Count">Occurrences.</param>
public sealed record StdfRecordCount(string Name, long Typ, long Sub, long Count);

/// <summary>One STDF test catalog entry.</summary>
/// <param name="TestNum">TEST_NUM.</param>
/// <param name="Kind"><c>P</c>, <c>M</c> or <c>F</c>.</param>
/// <param name="Name">The first non-empty test name.</param>
/// <param name="Occurrences">Occurrences.</param>
public sealed record StdfTest(long TestNum, string Kind, string Name, long Occurrences);
