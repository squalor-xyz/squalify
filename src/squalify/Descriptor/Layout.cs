// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The <c>layout</c> section.</summary>
/// <param name="Regions">Regions in file order.</param>
/// <param name="HeaderConvention">The header-unit convention, for tabular files.</param>
/// <param name="MaxLineLength">The longest line, for text files.</param>
public sealed record LayoutSection(
    IReadOnlyList<Region> Regions,
    HeaderConventionInfo? HeaderConvention = null,
    long? MaxLineLength = null);

/// <summary>One region of the file.</summary>
/// <param name="Kind">The region kind, for example <c>preamble</c> or <c>data</c>.</param>
/// <param name="FirstLine">First line, 1-based, inclusive.</param>
/// <param name="LastLine">Last line, 1-based, inclusive.</param>
/// <param name="StartByte">First byte offset, 0-based.</param>
/// <param name="EndByte">End byte offset, exclusive.</param>
/// <param name="Index">Block index, for <c>block</c> regions.</param>
/// <param name="Label">Block label, for <c>block</c> regions.</param>
/// <param name="LineKinds">Line-kind counts.</param>
/// <param name="Keys">Normalized keys (preamble).</param>
/// <param name="Values">Values aligned with <paramref name="Keys"/>; an entry that is not opted in is null. Values tier.</param>
/// <param name="Rows">Annotation rows.</param>
public sealed record Region(
    string Kind,
    long FirstLine,
    long LastLine,
    long StartByte,
    long EndByte,
    long? Index = null,
    string? Label = null,
    CountMap? LineKinds = null,
    IReadOnlyList<string>? Keys = null,
    IReadOnlyList<string?>? Values = null,
    IReadOnlyList<AnnotationRow>? Rows = null);

/// <summary>One row of an annotation region.</summary>
/// <param name="Line">The 1-based line.</param>
/// <param name="Label">The normalized label, or null when the first field is numeric.</param>
/// <param name="BlankFields">Count of blank fields.</param>
/// <param name="NullTokens">Null-token counts.</param>
/// <param name="TypeVotes">Type votes.</param>
/// <param name="Type">The row's type.</param>
/// <param name="Values">The row cells; a cell that is not opted in is null. Values tier.</param>
public sealed record AnnotationRow(
    long Line,
    string? Label,
    long BlankFields,
    CountMap NullTokens,
    CountMap TypeVotes,
    string Type,
    IReadOnlyList<string?>? Values = null);

/// <summary>The <c>layout.headerConvention</c> object.</summary>
/// <param name="Dominant">The most common convention.</param>
/// <param name="Share">Its share of header names.</param>
/// <param name="Counts">Counts per convention.</param>
public sealed record HeaderConventionInfo(string Dominant, double Share, CountMap Counts);
