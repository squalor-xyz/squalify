// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The fingerprint contribution of the key=value family. The <c>structure</c> projection gets the dialect (the separator and whether
/// the document has sections). The <c>layout</c> projection (and <c>typed</c>) gets the dialect and, in file order, every section with
/// its name and its keys, case-folded to ASCII; a name or key that holds a control character or U+FFFD is left out (a key also stays
/// out of the field names, which count it). The section names enter the <c>names</c> projection; the keys do through the fields.
/// </summary>
internal sealed class KeyValueFingerprint : IFingerprintContributor
{
    /// <inheritdoc />
    public string Family => "keyValue";

    /// <inheritdoc />
    public FingerprintContribution Contribute(ProjectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Dialect is not KeyValueDialect dialect)
        {
            return FingerprintContribution.None;
        }

        var sections = new List<(string? Name, List<string> Keys)>();
        var names = new List<string>();
        foreach (var section in input.Sections ?? [])
        {
            var keys = section.Keys.Where(static k => !Projection.IsExcluded(k)).Select(static k => Ordinal.AsciiFold(k)).ToList();
            if (section.Name.Length == 0 || Projection.IsExcluded(section.Name))
            {
                // The unnamed section is a null name; a section whose name is left out is one too (and its name is counted by the projection).
                sections.Add((null, keys));
                if (section.Name.Length != 0)
                {
                    names.Add(section.Name);
                }
            }
            else
            {
                sections.Add((Ordinal.AsciiFold(section.Name), keys));
                names.Add(section.Name);
            }
        }

        return new FingerprintContribution(
            Structure: w => WriteDialect(w, dialect),
            Layout: w =>
            {
                WriteDialect(w, dialect);
                w.Name("sections").BeginArray();
                foreach (var (name, keys) in sections)
                {
                    w.BeginObject();
                    w.Name("name").StringOrNull(name);
                    w.Name("keys").Strings(keys);
                    w.EndObject();
                }

                w.EndArray();
            },
            Typed: null,
            Names: names,
            Excluded: 0);
    }

    private static void WriteDialect(ProjectionJson w, KeyValueDialect dialect)
    {
        w.Name("dialect").BeginObject();
        w.Name("separator").String(dialect.Separator);
        w.Name("hasSections").Bool(dialect.HasSections);
        w.EndObject();
    }
}
