// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace squalor.squalify;

/// <summary>One value-bearing member of the descriptor, as <see cref="ValuesPolicy"/> lists it.</summary>
/// <param name="Name">The policy name.</param>
/// <param name="ModelMembers">The descriptor model members it covers, as <c>Type.Member</c>; each carries <see cref="ValueBearingAttribute"/>.</param>
/// <param name="AllowListable">True when <c>--include-values-for</c> can open it, by the name of its owner (a preamble key, an annotation label, a key=value key or a field name).</param>
/// <param name="When">When the member carries values, in words.</param>
internal sealed record ValueMember(string Name, IReadOnlyList<string> ModelMembers, bool AllowListable, string When);

/// <summary>
/// The single list of value-bearing descriptor members, and the two checks built on it. Profilers ask the
/// <see cref="ValuesGate"/> before they collect a value (so a value that may not be written is never held), and
/// <see cref="DescriptorWriter"/> calls <see cref="Verify"/> before it writes (so a producer that forgot to ask cannot
/// leak). A member that is not in <see cref="Members"/> is not gated by either, so a new value-bearing member must be
/// marked <see cref="ValueBearingAttribute"/> and listed here; a test compares the marks with the list by reflection,
/// and the canary fixtures prove that nothing literal reaches the output without the flag or an allow-list entry.
/// See <c>docs/design.md</c>, "Values tier".
/// </summary>
internal static class ValuesPolicy
{
    /// <summary>The values of a preamble or <c>key=value</c> region, aligned with its keys. Owner: the key.</summary>
    public static readonly ValueMember RegionValues = new(
        "regionValues",
        ["Region.Values"],
        AllowListable: true,
        When: "a preamble value is opted in by its key");

    /// <summary>The cells of an annotation row. Owner: the row's label.</summary>
    public static readonly ValueMember AnnotationRowValues = new(
        "annotationRowValues",
        ["AnnotationRow.Values"],
        AllowListable: true,
        When: "the cells of an annotation row are opted in by its label");

    /// <summary>The values of a <c>key=value</c> section, aligned with its keys. Owner: the key.</summary>
    public static readonly ValueMember SectionValues = new(
        "sectionValues",
        ["KeyValueSection.Values"],
        AllowListable: true,
        When: "a key=value value is opted in by its key");

    /// <summary>The units-row text of a field. Owner: the field name.</summary>
    public static readonly ValueMember FieldUnit = new(
        "fieldUnit",
        ["FieldProfile.Unit"],
        AllowListable: true,
        When: "the text of a units-row cell is opted in by the field name");

    /// <summary>The most common values of a field. Owner: the field name.</summary>
    public static readonly ValueMember FieldTop = new(
        "fieldTop",
        ["FieldProfile.Top"],
        AllowListable: true,
        When: "always, when present");

    /// <summary>The lexical minimum and maximum of a string field. Owner: the field name.</summary>
    public static readonly ValueMember FieldText = new(
        "fieldText",
        ["FieldProfile.Text"],
        AllowListable: true,
        When: "always, when present");

    /// <summary>The numeric and temporal statistics of a constant field, which are the value itself. Owner: the field name.</summary>
    public static readonly ValueMember ConstantStatistics = new(
        "constantStatistics",
        ["FieldProfile.Numeric", "FieldProfile.Temporal"],
        AllowListable: true,
        When: "the field is constant");

    /// <summary>The stable-line regexes built from a comment or text line. They have no owner, so only the flag opens them.</summary>
    public static readonly ValueMember StableLineText = new(
        "stableLineText",
        ["Hints.StableLines"],
        AllowListable: false,
        When: "the regex carries a data value (StableLine.CarriesValue)");

    /// <summary>Every value-bearing member. Parquet key-value values and comment text join this list when their members join the model.</summary>
    public static IReadOnlyList<ValueMember> Members { get; } =
    [
        RegionValues,
        AnnotationRowValues,
        SectionValues,
        FieldUnit,
        FieldTop,
        FieldText,
        ConstantStatistics,
        StableLineText,
    ];

    /// <summary>Creates the gate for a run's options.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The gate.</returns>
    public static ValuesGate Gate(ProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ValuesGate(options);
    }

    /// <summary>
    /// Checks that no value-bearing member of a descriptor is present unless its owner is opted in: by
    /// <c>includeValues</c>, or by its name in <c>includeValuesFor</c>. The message names the member by its path and
    /// never quotes a value.
    /// </summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <exception cref="InvalidOperationException">A member carries values that are not opted in. That is a bug in the producer, not a user error.</exception>
    public static void Verify(Descriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var gate = Gate(descriptor.Options);
        var regions = descriptor.Layout.Regions;
        for (var r = 0; r < regions.Count; r++)
        {
            var region = regions[r];
            VerifyAligned(gate, RegionValues, region.Keys, region.Values, "layout.regions[" + Index(r) + "].values");
            for (var i = 0; region.Rows is not null && i < region.Rows.Count; i++)
            {
                var row = region.Rows[i];
                if (HasValue(row.Values))
                {
                    Require(gate, AnnotationRowValues, row.Label, "layout.regions[" + Index(r) + "].rows[" + Index(i) + "].values");
                }
            }
        }

        var sections = descriptor.Sections;
        for (var s = 0; sections is not null && s < sections.Count; s++)
        {
            VerifyAligned(gate, SectionValues, sections[s].Keys, sections[s].Values, "sections[" + Index(s) + "].values");
        }

        var fields = descriptor.Table?.Fields;
        for (var f = 0; fields is not null && f < fields.Count; f++)
        {
            var field = fields[f];
            var path = "table.fields[" + Index(f) + "].";
            if (field.Unit is not null)
            {
                Require(gate, FieldUnit, field.Name, path + "unit");
            }

            if (field.Top is not null)
            {
                Require(gate, FieldTop, field.Name, path + "top");
            }

            if (field.Text is not null)
            {
                Require(gate, FieldText, field.Name, path + "text");
            }

            if (field.Constant == true && field.Numeric is not null)
            {
                Require(gate, ConstantStatistics, field.Name, path + "numeric");
            }

            if (field.Constant == true && field.Temporal is not null)
            {
                Require(gate, ConstantStatistics, field.Name, path + "temporal");
            }
        }

        var stable = descriptor.Hints.StableLines;
        for (var i = 0; i < stable.Count; i++)
        {
            if (stable[i].CarriesValue)
            {
                Require(gate, StableLineText, null, "hints.stableLines[" + Index(i) + "]");
            }
        }
    }

    /// <summary>
    /// The names of <c>includeValuesFor</c> that match no member of the descriptor: no preamble key, annotation label,
    /// key=value key (a section's, or a Parquet file's) or field name. Names are matched case-folded (ASCII), whole.
    /// </summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <returns>The unmatched names as given, without duplicates, sorted ordinally.</returns>
    public static IReadOnlyList<string> UnmatchedNames(Descriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var wanted = descriptor.Options.IncludeValuesFor;
        if (wanted.Count == 0)
        {
            return Array.Empty<string>();
        }

        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in descriptor.Layout.Regions)
        {
            AddAll(present, region.Keys);
            foreach (var row in region.Rows ?? [])
            {
                AddFolded(present, row.Label);
            }
        }

        foreach (var section in descriptor.Sections ?? [])
        {
            AddAll(present, section.Keys);
        }

        foreach (var field in descriptor.Table?.Fields ?? [])
        {
            AddFolded(present, field.Name);
        }

        if (descriptor.Format.Dialect is ParquetDialect parquet)
        {
            AddAll(present, parquet.KvKeys);
        }

        return Ordinal.Sorted(wanted.Where(name => !present.Contains(Ordinal.AsciiFold(name))).Distinct(StringComparer.Ordinal));
    }

    internal static bool IsListed(ValueMember member)
    {
        foreach (var listed in Members)
        {
            if (ReferenceEquals(listed, member))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddAll(HashSet<string> present, IReadOnlyList<string>? names)
    {
        foreach (var name in names ?? [])
        {
            AddFolded(present, name);
        }
    }

    private static void AddFolded(HashSet<string> present, string? name)
    {
        if (name is not null)
        {
            present.Add(Ordinal.AsciiFold(name));
        }
    }

    private static bool HasValue(IReadOnlyList<string?>? values)
    {
        return values is not null && values.Any(static v => v is not null);
    }

    private static void VerifyAligned(ValuesGate gate, ValueMember member, IReadOnlyList<string>? keys, IReadOnlyList<string?>? values, string path)
    {
        for (var i = 0; values is not null && i < values.Count; i++)
        {
            if (values[i] is not null)
            {
                Require(gate, member, keys is not null && i < keys.Count ? keys[i] : null, path + "[" + Index(i) + "]");
            }
        }
    }

    private static void Require(ValuesGate gate, ValueMember member, string? owner, string path)
    {
        if (!gate.Allows(member, owner))
        {
            throw new InvalidOperationException(
                "The values tier is not opted in for '" + path + "' (policy member " + member.Name + "): a producer wrote a value that needs --include-values or an --include-values-for entry for its owner. This is a bug.");
        }
    }

    private static string Index(int i)
    {
        return i.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Decides whether a value-bearing member may carry values for one owner, from the run's options. Names in
/// <c>includeValuesFor</c> are compared with ASCII case folding (<see cref="Ordinal.AsciiFold(string)"/>) and whole,
/// never by prefix or with a Unicode table; one name opens every member of that name.
/// </summary>
internal sealed class ValuesGate
{
    private readonly bool _all;
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>Creates the gate.</summary>
    /// <param name="options">The options.</param>
    public ValuesGate(ProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _all = options.IncludeValues;
        foreach (var name in options.IncludeValuesFor)
        {
            _names.Add(Ordinal.AsciiFold(name));
        }
    }

    /// <summary>True when the member may carry values for this owner.</summary>
    /// <param name="member">The member, a <see cref="ValuesPolicy"/> entry. A member that the policy does not list is not gated.</param>
    /// <param name="owner">The owner's name (key, label or field name), or null when it has none; only the flag opens a member of a null owner.</param>
    /// <returns>True to collect and write the values.</returns>
    public bool Allows(ValueMember member, string? owner)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (_all || !ValuesPolicy.IsListed(member))
        {
            return true;
        }

        return member.AllowListable && owner is not null && _names.Contains(Ordinal.AsciiFold(owner));
    }
}
