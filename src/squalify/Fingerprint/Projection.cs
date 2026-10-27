// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;

namespace squalor.squalify;

/// <summary>What the fingerprint projections are built from: the parts of the descriptor that are not data values.</summary>
/// <param name="Family">The format family.</param>
/// <param name="Charset">The charset of a text file, or null.</param>
/// <param name="Dialect">The family dialect, or null.</param>
/// <param name="Layout">The layout section.</param>
/// <param name="Table">The table section, or null for a file that is not tabular.</param>
internal sealed record ProjectionInput(string Family, string? Charset, Dialect? Dialect, LayoutSection Layout, TableSection? Table)
{
    /// <summary>Takes the input from a finished descriptor.</summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <returns>The input.</returns>
    public static ProjectionInput From(Descriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new ProjectionInput(descriptor.Format.Family, descriptor.Encoding.Charset, descriptor.Format.Dialect, descriptor.Layout, descriptor.Table);
    }
}

/// <summary>The fingerprint, and how many names were left out of it.</summary>
/// <param name="Fingerprint">The four hashes.</param>
/// <param name="Excluded">Names and keys with a control character or U+FFFD that the projections left out.</param>
internal sealed record ProjectionResult(Fingerprint Fingerprint, long Excluded);

/// <summary>
/// Builds the value-free projections and hashes them. See <c>docs/design.md</c>, "Structural fingerprint". Every
/// projection is compact JSON written by <see cref="ProjectionJson"/> with the members in the order shown there.
/// </summary>
internal static class Projection
{
    /// <summary>The projection version written as <c>fingerprint.version</c>.</summary>
    public const long Version = 1;

    /// <summary>The bytes of the <c>structure</c> projection.</summary>
    /// <param name="input">The input.</param>
    /// <returns>Compact canonical JSON, UTF-8.</returns>
    public static byte[] Structure(ProjectionInput input)
    {
        return Parts.Of(input).Structure();
    }

    /// <summary>The bytes of the <c>layout</c> projection.</summary>
    /// <param name="input">The input.</param>
    /// <returns>Compact canonical JSON, UTF-8.</returns>
    public static byte[] Layout(ProjectionInput input)
    {
        return Parts.Of(input).Layout(typed: false);
    }

    /// <summary>The bytes of the <c>typed</c> projection.</summary>
    /// <param name="input">The input.</param>
    /// <returns>Compact canonical JSON, UTF-8.</returns>
    public static byte[] Typed(ProjectionInput input)
    {
        return Parts.Of(input).Layout(typed: true);
    }

    /// <summary>The bytes of the <c>names</c> projection.</summary>
    /// <param name="input">The input.</param>
    /// <returns>Compact canonical JSON, UTF-8.</returns>
    public static byte[] Names(ProjectionInput input)
    {
        return Parts.Of(input).Names();
    }

    /// <summary>The bytes of the <c>names</c> projection, with a given contributor instead of the registered one.</summary>
    /// <param name="input">The input.</param>
    /// <param name="contributor">The contributor.</param>
    /// <returns>Compact canonical JSON, UTF-8.</returns>
    internal static byte[] Names(ProjectionInput input, IFingerprintContributor? contributor)
    {
        return Parts.Of(input, contributor).Names();
    }

    /// <summary>Computes the fingerprint with a given contributor instead of the registered one.</summary>
    /// <param name="input">The input.</param>
    /// <param name="contributor">The contributor.</param>
    /// <returns>The hashes, and the count of excluded names.</returns>
    internal static ProjectionResult Compute(ProjectionInput input, IFingerprintContributor? contributor)
    {
        return Compute(Parts.Of(input, contributor));
    }

    /// <summary>Computes the fingerprint.</summary>
    /// <param name="input">The input.</param>
    /// <returns>The hashes, and the count of excluded names.</returns>
    public static ProjectionResult Compute(ProjectionInput input)
    {
        return Compute(Parts.Of(input));
    }

    private static ProjectionResult Compute(Parts parts)
    {
        var fingerprint = new Fingerprint(
            Version,
            Hash(parts.Structure()),
            Hash(parts.Layout(typed: false)),
            Hash(parts.Layout(typed: true)),
            Hash(parts.Names()));
        return new ProjectionResult(fingerprint, parts.Excluded);
    }

    /// <summary>Whether a name or key is left out of the projections: it holds a control character or U+FFFD.</summary>
    /// <param name="name">The name.</param>
    /// <returns>True when it is excluded.</returns>
    public static bool IsExcluded(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var c in name)
        {
            // C0 controls, DEL, C1 controls and the replacement character: plain code point ranges, not Unicode tables.
            if (c < ' ' || (c >= '\u007F' && c <= '\u009F') || c == '\uFFFD')
            {
                return true;
            }
        }

        return false;
    }

    private static string Hash(byte[] bytes)
    {
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    // What the four projections are written from: gathered once per input.
    private sealed class Parts
    {
        private readonly ProjectionInput _input;
        private readonly FingerprintContribution _contribution;
        private readonly string[] _fields;
        private readonly string[] _types;
        private readonly string[] _names;

        private Parts(ProjectionInput input, IFingerprintContributor? contributor)
        {
            _input = input;
            _contribution = contributor?.Contribute(input) ?? FingerprintContribution.None;
            var excluded = _contribution.Excluded;
            var fields = new List<string>();
            var types = new List<string>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            // A family's own names get the same hygiene as field names: excluded ones are left out and counted.
            foreach (var name in _contribution.Names)
            {
                if (IsExcluded(name))
                {
                    excluded++;
                }
                else
                {
                    names.Add(Ordinal.AsciiFold(name));
                }
            }

            if (input.Table is { } table)
            {
                foreach (var field in table.Fields)
                {
                    types.Add(field.Type);
                    if (string.IsNullOrEmpty(field.Name))
                    {
                        fields.Add("#" + Ordinal.Invariant(field.Index));
                    }
                    else if (IsExcluded(field.Name))
                    {
                        fields.Add("#" + Ordinal.Invariant(field.Index));
                        excluded++;
                    }
                    else
                    {
                        var folded = Ordinal.AsciiFold(field.Name);
                        fields.Add(folded);
                        names.Add(folded);
                    }
                }
            }

            Excluded = excluded;
            _fields = fields.ToArray();
            _types = types.ToArray();
            _names = Ordinal.Sorted(names).ToArray();
        }

        public long Excluded { get; }

        public static Parts Of(ProjectionInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            return new Parts(input, FingerprintContributors.For(input.Family));
        }

        public static Parts Of(ProjectionInput input, IFingerprintContributor? contributor)
        {
            ArgumentNullException.ThrowIfNull(input);
            return new Parts(input, contributor);
        }

        public byte[] Structure()
        {
            var w = new ProjectionJson().BeginObject();
            w.Name("family").String(_input.Family);
            _contribution.Structure?.Invoke(w);
            w.Name("charsetFamily").StringOrNull(CharsetFamily(_input.Charset));
            w.Name("regions").Strings(RegionTree.FirstBlockView(_input.Layout).Select(static r => r.Kind));
            return w.EndObject().ToBytes();
        }

        public byte[] Layout(bool typed)
        {
            var w = new ProjectionJson().BeginObject();
            w.Name("family").String(_input.Family);
            _contribution.Layout?.Invoke(w);
            w.Name("charsetFamily").StringOrNull(CharsetFamily(_input.Charset));
            w.Name("regions").Strings(RegionTree.FirstBlockView(_input.Layout).Select(static r => r.Kind));

            // Absent (no header, or a file that is not a table) is JSON null; the convention none is the string "none".
            w.Name("headerConvention").StringOrNull(_input.Layout.HeaderConvention?.Dominant);
            w.Name("fields").Strings(_fields);
            if (typed)
            {
                _contribution.Typed?.Invoke(w);
                w.Name("types").Strings(_types);
            }

            return w.EndObject().ToBytes();
        }

        public byte[] Names()
        {
            return new ProjectionJson().Strings(_names).ToBytes();
        }

        // The charset family: latin-1 files are one family, every Unicode charset (ascii, utf-8, utf-16, utf-32) another.
        private static string? CharsetFamily(string? charset)
        {
            return charset switch
            {
                null => null,
                "latin-1" => "latin1",
                _ => "unicode",
            };
        }
    }
}
