// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// An entry of <c>issues[]</c>. At most one of <paramref name="FirstLines"/> and <paramref name="Offsets"/> is set;
/// the writer throws <see cref="SqualifyException"/> when both are.
/// </summary>
/// <param name="Code">The issue code, for example <c>raggedRows</c>.</param>
/// <param name="Count">Occurrences.</param>
/// <param name="FirstLines">First 1-based lines where the issue occurs.</param>
/// <param name="Offsets">First byte offsets where the issue occurs.</param>
public sealed record Issue(
    string Code,
    long Count,
    IReadOnlyList<long>? FirstLines = null,
    IReadOnlyList<long>? Offsets = null);

/// <summary>An entry of <c>evidence[]</c>: one inference and the candidates considered.</summary>
/// <param name="Stage">The pipeline stage.</param>
/// <param name="Topic">The topic within the stage.</param>
/// <param name="Chosen">The chosen value, or null when nothing was chosen.</param>
/// <param name="Candidates">The candidates in scoring order.</param>
public sealed record Evidence(string Stage, string Topic, string? Chosen, IReadOnlyList<Candidate> Candidates);

/// <summary>One candidate of an evidence entry.</summary>
/// <param name="Value">The candidate value.</param>
/// <param name="Score">Its score.</param>
/// <param name="Reason">A fixed-template reason with value-free numbers.</param>
public sealed record Candidate(string Value, double Score, string Reason);

/// <summary>The <c>hints</c> section: neutral facts for detect rules. Members are written as null when they do not apply.</summary>
/// <param name="Extension">The file extension with its dot, or null.</param>
/// <param name="Magic">The matched magic, or null.</param>
/// <param name="StableLines">Anchored regexes for stable lines.</param>
/// <param name="HeaderFields">Header field names.</param>
/// <param name="HeaderPrefix">The first header field names.</param>
/// <param name="NonNullFields">Indexes of fields with no nulls in this file.</param>
/// <param name="FieldCount">Minimum and maximum field count, or null for non-tabular files.</param>
/// <param name="DataStartLine">The 1-based line where data starts, or null.</param>
/// <param name="Delimiter">The delimiter, or null.</param>
/// <param name="HeaderConvention">The dominant header-unit convention, or null.</param>
/// <param name="ParquetColumns">Parquet column names, or null.</param>
/// <param name="Stdf">STDF hints, or null.</param>
/// <param name="FileNameShape">The shape mask of the file name.</param>
public sealed record Hints(
    string? Extension,
    MagicHint? Magic,
    IReadOnlyList<StableLine> StableLines,
    IReadOnlyList<string>? HeaderFields,
    IReadOnlyList<string>? HeaderPrefix,
    IReadOnlyList<long>? NonNullFields,
    MinMax? FieldCount,
    long? DataStartLine,
    string? Delimiter,
    string? HeaderConvention,
    IReadOnlyList<string>? ParquetColumns,
    StdfHints? Stdf,
    string FileNameShape);

/// <summary>The structured form of <c>hints.magic</c>: a named byte string at a fixed offset.</summary>
/// <param name="Name">The magic name, for example <c>gzip</c>.</param>
/// <param name="Offset">The 0-based byte offset from the start of the file where the bytes lie.</param>
/// <param name="Hex">The bytes that must be found there, as lower-case hex.</param>
public sealed record MagicHint(string Name, long Offset, string Hex);

/// <summary>A stable line and the regex that matches it.</summary>
/// <param name="Line">The 0-based line number.</param>
/// <param name="Regex">The anchored, portable regex.</param>
public sealed record StableLine(long Line, string Regex);

/// <summary>STDF hints.</summary>
/// <param name="CpuType">The CPU_TYPE byte.</param>
/// <param name="Version">The STDF version.</param>
public sealed record StdfHints(long CpuType, string Version);
