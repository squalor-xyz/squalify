// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The fingerprint contribution of the delimited family. <c>structure</c> gets the whole dialect (delimiter, quote,
/// hasHeader, unitsRow, decimal separator, constant line length) and the dominant preamble line kind. <c>layout</c>
/// gets the dialect without the last two members, and the preamble keys: normalized, ASCII-case-folded, in file
/// order, with a key that holds a control character or U+FFFD left out (and counted).
/// </summary>
internal sealed class DelimitedFingerprint : IFingerprintContributor
{
    /// <inheritdoc />
    public string Family => "delimited";

    /// <inheritdoc />
    public FingerprintContribution Contribute(ProjectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Dialect is not DelimitedDialect dialect)
        {
            return FingerprintContribution.None;
        }

        var keys = new List<string>();
        long excluded = 0;
        foreach (var region in input.Layout.Regions)
        {
            if (region.Kind != "preamble" || region.Keys is null)
            {
                continue;
            }

            foreach (var key in region.Keys)
            {
                if (Projection.IsExcluded(key))
                {
                    excluded++;
                }
                else
                {
                    keys.Add(Ordinal.AsciiFold(key));
                }
            }
        }

        var preambleKind = DominantPreambleKind(input.Layout);
        return new FingerprintContribution(
            Structure: w =>
            {
                WriteDialect(w, dialect, full: true);
                w.Name("preambleKind").StringOrNull(preambleKind);
            },
            Layout: w =>
            {
                WriteDialect(w, dialect, full: false);
                w.Name("preambleKeys").Strings(keys);
            },
            Typed: null,
            Names: Array.Empty<string>(),
            Excluded: excluded);
    }

    private static void WriteDialect(ProjectionJson w, DelimitedDialect dialect, bool full)
    {
        w.Name("dialect").BeginObject();
        w.Name("delimiter").String(dialect.Delimiter);
        w.Name("quote").StringOrNull(dialect.Quote);
        w.Name("hasHeader").Bool(dialect.HasHeader);
        w.Name("unitsRow").Bool(dialect.UnitsRow);
        if (full)
        {
            w.Name("decimalSeparator").String(dialect.DecimalSeparator);
            w.Name("constantLineLength").Bool(dialect.Padding.ConstantLineLength);
        }

        w.EndObject();
    }

    // The most common line kind of the preamble, blank lines not counted; a tie goes to the kind that sorts first
    // (the line-kind map is ordered by key, ordinally). Null when there is no preamble or it has no such line.
    private static string? DominantPreambleKind(LayoutSection layout)
    {
        foreach (var region in layout.Regions)
        {
            if (region.Kind != "preamble" || region.LineKinds is null)
            {
                continue;
            }

            string? best = null;
            long bestCount = 0;
            foreach (var (kind, count) in region.LineKinds)
            {
                if (kind != "blank" && count > bestCount)
                {
                    best = kind;
                    bestCount = count;
                }
            }

            return best;
        }

        return null;
    }
}
