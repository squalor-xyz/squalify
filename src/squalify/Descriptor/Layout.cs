// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The <c>layout</c> section.</summary>
/// <param name="Regions">Regions in file order.</param>
/// <param name="HeaderConvention">The header-unit convention, for tabular files.</param>
/// <param name="MaxLineLength">The longest line, for text files.</param>
public sealed record LayoutSection(
    IReadOnlyList<Region> Regions,
    HeaderConventionInfo? HeaderConvention = null,
    long? MaxLineLength = null)
{
    /// <inheritdoc />
    public bool Equals(LayoutSection? other)
    {
        return other is not null
            && ValueEquality.ListEquals(Regions, other.Regions)
            && EqualityComparer<HeaderConventionInfo?>.Default.Equals(HeaderConvention, other.HeaderConvention)
            && EqualityComparer<long?>.Default.Equals(MaxLineLength, other.MaxLineLength);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        ValueEquality.AddList(ref hash, Regions);
        hash.Add(HeaderConvention);
        hash.Add(MaxLineLength);
        return hash.ToHashCode();
    }
}

/// <summary>One region of the file.</summary>
/// <param name="Kind">The region kind, for example <c>preamble</c> or <c>data</c>.</param>
/// <param name="FirstLine">First line, 1-based, inclusive.</param>
/// <param name="LastLine">Last line, 1-based, inclusive.</param>
/// <param name="StartByte">First byte offset, 0-based.</param>
/// <param name="EndByte">End byte offset, exclusive.</param>
/// <param name="Index">Block index (0-based), for <c>block</c> regions.</param>
/// <param name="LineKinds">Line-kind counts.</param>
/// <param name="Keys">Normalized keys (preamble).</param>
/// <param name="Values">Values aligned with <paramref name="Keys"/>; an entry that is not opted in is null. Values tier.</param>
/// <param name="Rows">Annotation rows.</param>
/// <param name="Regions">The child regions of a <c>block</c> region (its preamble, header and data, and so on), in file order.</param>
public sealed record Region(
    string Kind,
    long FirstLine,
    long LastLine,
    long StartByte,
    long EndByte,
    long? Index = null,
    CountMap? LineKinds = null,
    IReadOnlyList<string>? Keys = null,
    [property: ValueBearing] IReadOnlyList<string?>? Values = null,
    IReadOnlyList<AnnotationRow>? Rows = null,
    IReadOnlyList<Region>? Regions = null)
{
    /// <inheritdoc />
    public bool Equals(Region? other)
    {
        return other is not null
            && EqualityComparer<string>.Default.Equals(Kind, other.Kind)
            && EqualityComparer<long>.Default.Equals(FirstLine, other.FirstLine)
            && EqualityComparer<long>.Default.Equals(LastLine, other.LastLine)
            && EqualityComparer<long>.Default.Equals(StartByte, other.StartByte)
            && EqualityComparer<long>.Default.Equals(EndByte, other.EndByte)
            && EqualityComparer<long?>.Default.Equals(Index, other.Index)
            && EqualityComparer<CountMap?>.Default.Equals(LineKinds, other.LineKinds)
            && ValueEquality.ListEquals(Keys, other.Keys)
            && ValueEquality.ListEquals(Values, other.Values)
            && ValueEquality.ListEquals(Rows, other.Rows)
            && ValueEquality.ListEquals(Regions, other.Regions);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(FirstLine);
        hash.Add(LastLine);
        hash.Add(StartByte);
        hash.Add(EndByte);
        hash.Add(Index);
        hash.Add(LineKinds);
        ValueEquality.AddList(ref hash, Keys);
        ValueEquality.AddList(ref hash, Values);
        ValueEquality.AddList(ref hash, Rows);
        ValueEquality.AddList(ref hash, Regions);
        return hash.ToHashCode();
    }
}

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
    [property: ValueBearing] IReadOnlyList<string?>? Values = null)
{
    /// <inheritdoc />
    public bool Equals(AnnotationRow? other)
    {
        return other is not null
            && EqualityComparer<long>.Default.Equals(Line, other.Line)
            && EqualityComparer<string?>.Default.Equals(Label, other.Label)
            && EqualityComparer<long>.Default.Equals(BlankFields, other.BlankFields)
            && EqualityComparer<CountMap>.Default.Equals(NullTokens, other.NullTokens)
            && EqualityComparer<CountMap>.Default.Equals(TypeVotes, other.TypeVotes)
            && EqualityComparer<string>.Default.Equals(Type, other.Type)
            && ValueEquality.ListEquals(Values, other.Values);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Line);
        hash.Add(Label);
        hash.Add(BlankFields);
        hash.Add(NullTokens);
        hash.Add(TypeVotes);
        hash.Add(Type);
        ValueEquality.AddList(ref hash, Values);
        return hash.ToHashCode();
    }
}

/// <summary>The <c>layout.headerConvention</c> object.</summary>
/// <param name="Dominant">The most common convention.</param>
/// <param name="Share">Its share of header names.</param>
/// <param name="Counts">Counts per convention.</param>
public sealed record HeaderConventionInfo(string Dominant, double Share, CountMap Counts);

/// <summary>Walks the region tree of a layout: a <c>block</c> region holds its own child regions.</summary>
internal static class RegionTree
{
    /// <summary>Every region of a layout, in file order: a block region, then its children, then the next region.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The regions.</returns>
    public static IEnumerable<Region> All(LayoutSection layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        foreach (var region in layout.Regions)
        {
            yield return region;
            foreach (var child in region.Regions ?? [])
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// The regions of the file as one table sees them, for the fingerprint and the hints: the first block's child regions
    /// in place of all the block regions, then the regions after the blocks (the footer). A file with one block has flat
    /// regions and is unchanged. The result does not depend on the number of blocks.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The regions.</returns>
    public static IReadOnlyList<Region> FirstBlockView(LayoutSection layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var view = new List<Region>();
        var seenBlock = false;
        foreach (var region in layout.Regions)
        {
            if (region.Kind != "block")
            {
                view.Add(region);
            }
            else if (!seenBlock)
            {
                seenBlock = true;
                view.AddRange(region.Regions ?? [region]);
            }
        }

        return view;
    }
}
