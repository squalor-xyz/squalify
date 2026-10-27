// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>Builds <c>hints</c>. See <c>docs/design.md</c>, "Detect-rule hints".</summary>
internal static class HintBuilder
{
    /// <summary>How many header names the header-prefix hint and its regex keep.</summary>
    public const int HeaderPrefixLength = 5;

    /// <summary>Builds the hints of a file.</summary>
    /// <param name="fileName">The bare file name.</param>
    /// <param name="sniff">The byte-pass result.</param>
    /// <param name="dialect">The family dialect, or null.</param>
    /// <param name="layout">The layout section.</param>
    /// <param name="table">The table section, or null.</param>
    /// <param name="lines">The lines, with their text, that the format profiler offers.</param>
    /// <returns>The hints.</returns>
    public static Hints Build(
        string fileName,
        SniffResult sniff,
        Dialect? dialect,
        LayoutSection layout,
        TableSection? table,
        IReadOnlyList<StableLineSource> lines)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(sniff);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(lines);
        var extension = Path.GetExtension(fileName);
        var delimited = dialect as DelimitedDialect;
        string[]? names = null;
        if (delimited is { HasHeader: true } && table is not null)
        {
            names = table.Fields.Select(static f => f.Name ?? string.Empty).ToArray();
        }

        long[]? nonNull = null;
        long? dataStart = null;
        if (table is not null)
        {
            nonNull = table.Fields.Where(static f => f.Nulls == 0 && f.Count > 0).Select(static f => f.Index).ToArray();
            dataStart = layout.Regions.FirstOrDefault(static r => r.Kind == "data")?.FirstLine;
        }

        return new Hints(
            Extension: extension.Length == 0 ? null : Ordinal.AsciiFold(extension),
            Magic: sniff.MagicDetail,
            StableLines: StableLines(delimited, names, lines),
            HeaderFields: names,
            HeaderPrefix: names?.Take(HeaderPrefixLength).ToArray(),
            NonNullFields: nonNull,
            FieldCount: table?.FieldCount,
            DataStartLine: dataStart,
            Delimiter: delimited?.Delimiter,
            HeaderConvention: layout.HeaderConvention?.Dominant,
            ParquetColumns: null,
            Stdf: null,
            FileNameShape: FileNameShape.Of(fileName));
    }

    // Each line offered gives its regexes; a regex enters only when it matches its own line under both engines (a
    // quoted key, or a key trimmed of a non-ASCII space, may not). The result is ordered by line, then by regex, ordinally.
    private static IReadOnlyList<StableLine> StableLines(DelimitedDialect? dialect, string[]? names, IReadOnlyList<StableLineSource> lines)
    {
        var result = new List<StableLine>();
        foreach (var source in lines)
        {
            switch (source.Kind)
            {
                case StableLineKind.Header:
                    Add(result, source, PortableRegex.Line(source.Text));
                    if (dialect is not null && names is { Length: > 0 })
                    {
                        var prefix = names.Take(HeaderPrefixLength).ToArray();
                        Add(result, source, PortableRegex.Prefix(prefix, dialect.Delimiter, dialect.Quote is not null));
                    }

                    break;
                case StableLineKind.PreambleKey:
                    Add(result, source, PortableRegex.Key(source.Key ?? string.Empty));
                    break;
                default:
                    Add(result, source, PortableRegex.Line(source.Text));
                    break;
            }
        }

        result.Sort(static (a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : string.CompareOrdinal(a.Regex, b.Regex));
        return result;
    }

    private static void Add(List<StableLine> result, StableLineSource source, string regex)
    {
        if (PortableRegex.MatchesBoth(regex, source.Text))
        {
            result.Add(new StableLine(source.Line - 1, regex));
        }
    }
}
