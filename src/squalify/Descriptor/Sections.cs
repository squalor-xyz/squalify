// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>One <c>[section]</c> of a key=value document (an entry of <c>sections[]</c>).</summary>
/// <param name="Name">The section name.</param>
/// <param name="Line">The 1-based line of the header.</param>
/// <param name="Keys">Normalized keys in file order.</param>
/// <param name="Values">Values aligned with <paramref name="Keys"/>; an entry that is not opted in is null. Values tier.</param>
public sealed record KeyValueSection(string Name, long Line, IReadOnlyList<string> Keys, [property: ValueBearing] IReadOnlyList<string?>? Values = null);

/// <summary>The <c>binary</c> section.</summary>
/// <param name="Bytes">Whole-file byte statistics.</param>
/// <param name="Blocks">Statistics of the per-block entropy, or null before sq-10 adds them.</param>
/// <param name="Segments">The fixed 16-segment entropy profile, or null before sq-10 adds it.</param>
public sealed record BinarySection(ByteStats Bytes, BlockStats? Blocks = null, IReadOnlyList<double>? Segments = null);

/// <summary>Whole-file byte statistics.</summary>
/// <param name="Histogram">The 256-bin byte histogram.</param>
/// <param name="Nul">Count of NUL bytes.</param>
/// <param name="PrintableAscii">Count of printable ASCII bytes.</param>
/// <param name="High">Count of bytes at or above 0x80.</param>
/// <param name="Entropy">Shannon entropy in bits per byte.</param>
public sealed record ByteStats(IReadOnlyList<long> Histogram, long Nul, long PrintableAscii, long High, double Entropy);

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
    long TestsTruncated);

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
