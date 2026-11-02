// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace squalor.squalify.Tests.Schema;

/// <summary>
/// The members of every object of the schema are the members that <c>docs/descriptor.md</c> documents (the top-level table and the
/// table "Key order of nested objects"), so the document and the schema cannot drift apart. A member the document marks as
/// planned for a 1.x minor release is not in the schema and is not a produced member.
/// </summary>
public class DocumentedMembersTests
{
    [Fact]
    public void Schema_PropertiesMatchTheDocumentedMembers()
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var documented = DocumentedMembers.Parse(SchemaFiles.ReadDoc());
        var sections = SchemaWalker.Sections(schema.RootElement);

        var differences = new List<string>();

        // Every documented, produced object is in the schema with the same members.
        foreach (var (path, section) in documented.Sections.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            if (!sections.TryGetValue(path, out var found))
            {
                differences.Add($"{Show(path)}: documented with {Join(section.Members)} but the schema has no such object");
                continue;
            }

            var missingInSchema = section.Members.Except(found.Members, StringComparer.Ordinal).ToArray();
            var undocumented = found.Members.Except(section.Members, StringComparer.Ordinal).ToArray();
            if (missingInSchema.Length > 0)
            {
                differences.Add($"{Show(path)}: documented and produced but not in the schema: {Join(missingInSchema)}");
            }

            if (undocumented.Length > 0)
            {
                differences.Add($"{Show(path)}: in the schema but not documented: {Join(undocumented)}");
            }
        }

        // Every object of the schema is documented.
        foreach (var path in sections.Keys.Except(documented.Sections.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            differences.Add($"{Show(path)}: in the schema ({Join(sections[path].Members)}) but not documented");
        }

        // A planned member or object is documented and is not in the schema. "object/member" names one member of an object.
        foreach (var planned in documented.Planned.Order(StringComparer.Ordinal))
        {
            var slash = planned.IndexOf('/', StringComparison.Ordinal);
            var inSchema = slash >= 0
                ? sections.TryGetValue(planned[..slash], out var owner) && owner.Members.Contains(planned[(slash + 1)..], StringComparer.Ordinal)
                : sections.ContainsKey(planned);
            if (inSchema)
            {
                differences.Add($"{Show(planned)}: marked planned in the document but present in the schema");
            }
        }

        Assert.Empty(differences);
    }

    [Fact]
    public void Schema_PlannedMembers_AreTheOnesNothingProducesYet()
    {
        // Owner decision S12-6: the schema describes what 1.0.0 emits. These are documented, marked planned, and absent from it.
        var documented = DocumentedMembers.Parse(SchemaFiles.ReadDoc());
        string[] planned =
        [
            "binary/blocks",
            "binary/segments",
            "hints.stdf",
            "sections",
            "stdf",
            "stdf.records",
            "stdf.tests",
        ];
        Assert.Equal(planned.Order(StringComparer.Ordinal), documented.Planned.Order(StringComparer.Ordinal));

        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var root = schema.RootElement.GetProperty("properties");
        Assert.False(root.TryGetProperty("sections", out _));
        Assert.False(root.TryGetProperty("stdf", out _));
        Assert.False(root.GetProperty("binary").GetProperty("properties").TryGetProperty("blocks", out _));
        Assert.False(root.GetProperty("binary").GetProperty("properties").TryGetProperty("segments", out _));

        // hints.stdf is written, as null, by every profile of this version, so the key is in the schema and its only value is null.
        Assert.Equal("null", root.GetProperty("hints").GetProperty("properties").GetProperty("stdf").GetProperty("type").GetString());
    }

    [Fact]
    public void Docs_IssueCodeTables_EqualSchemaEnum()
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var enumerated = schema.RootElement.GetProperty("$defs").GetProperty("issueCode").GetProperty("enum").EnumerateArray().Select(static e => e.GetString()!).ToArray();
        var document = SchemaFiles.ReadDoc();

        // One table row per code, in the section on issues: no code missing, none listed that the schema and the registry do not have.
        var tables = DocumentedMembers.IssueCodeTableCodes(document);
        Assert.Equal(tables.Count, tables.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(enumerated, tables.Order(StringComparer.Ordinal));
        Assert.Equal(IssueCodes.All, tables.Order(StringComparer.Ordinal));

        // The layout-only section says that no code is dropped, and names them all.
        Assert.Equal(enumerated, DocumentedMembers.LayoutOnlyIssueCodes(document).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Docs_EvidenceStageList_IsThePipelineOrderOfTheRegistry()
    {
        var order = DocumentedMembers.Section(SchemaFiles.ReadDoc(), "## `evidence[]`")
            .Split('\n')
            .First(static l => l.StartsWith("**Order of entries.**", StringComparison.Ordinal));
        var listed = System.Text.RegularExpressions.Regex.Matches(order[..order.IndexOf("; a stage added later", StringComparison.Ordinal)], "`([a-z]+)`")
            .Select(static m => m.Groups[1].Value)
            .ToArray();
        Assert.Equal(EvidenceStages.Order, listed);
        Assert.DoesNotContain("Profiler.StageOrder", order, StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_FamilyAndLabelLists_MarkPlannedValues()
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var properties = schema.RootElement.GetProperty("properties");
        string[] Enum(JsonElement node) => node.GetProperty("enum").EnumerateArray().Select(static e => e.GetString()!).ToArray();
        var families = Enum(properties.GetProperty("format").GetProperty("properties").GetProperty("family"));
        var labels = Enum(properties.GetProperty("kind").GetProperty("properties").GetProperty("label"));
        var ruleIds = Enum(properties.GetProperty("kind").GetProperty("properties").GetProperty("ruleId"));
        var document = SchemaFiles.ReadDoc();
        var names = new System.Text.RegularExpressions.Regex("`([^`]+)`");
        string[] Names(string text) => names.Matches(text).Select(static m => m.Groups[1].Value).ToArray();

        // The by-family table: a family is marked "(planned)" exactly when the schema does not have it.
        var byFamily = DocumentedMembers.Section(document, "## `format.dialect` by family").Split('\n').Where(static l => l.StartsWith("| ", StringComparison.Ordinal)).Skip(1).ToArray();
        var produced = byFamily.Where(static l => !l.Split('|')[1].Contains("(planned)", StringComparison.Ordinal)).Select(static l => l.Split('|')[1].Trim()).ToArray();
        var planned = byFamily.Where(static l => l.Split('|')[1].Contains("(planned)", StringComparison.Ordinal)).Select(static l => l.Split('|')[1].Replace("(planned)", string.Empty, StringComparison.Ordinal).Trim()).ToArray();
        Assert.Equal(["delimited"], produced);
        Assert.Empty(planned.Intersect(families));
        Assert.Empty(produced.Except(families));

        // The top-level table names the families the schema has, then those that are planned.
        var formatRow = document.Split('\n').First(static l => l.StartsWith("| `format` | object", StringComparison.Ordinal));
        var familyCell = formatRow[(formatRow.IndexOf("`family` (", StringComparison.Ordinal) + "`family` (".Length)..];
        familyCell = familyCell[..familyCell.IndexOf(')', StringComparison.Ordinal)];
        var semicolon = familyCell.IndexOf(';', StringComparison.Ordinal);
        Assert.Equal(families.Order(StringComparer.Ordinal), Names(familyCell[..semicolon]).Order(StringComparer.Ordinal));
        Assert.Contains("planned", familyCell[semicolon..], StringComparison.Ordinal);
        Assert.Equal(planned.Order(StringComparer.Ordinal), Names(familyCell[semicolon..]).Order(StringComparer.Ordinal));

        // The kind section and the interim members name each label with its rule id, and the planned section says what the schema writes.
        var pair = new System.Text.RegularExpressions.Regex("`([a-zA-Z]+)` \\(`(K[0-9]+)`\\)");
        var expected = InterimKinds.Labels.Select(static l => InterimKinds.For(l)).Select(static k => k.Label + ":" + k.RuleId).Order(StringComparer.Ordinal).ToArray();
        foreach (var heading in new[] { "## `kind`", "## Interim members" })
        {
            var section = DocumentedMembers.Section(document, heading);
            var line = section.Split('\n').First(static l => l.Contains("`K09`", StringComparison.Ordinal));
            Assert.Equal(expected, pair.Matches(line).Select(static m => m.Groups[1].Value + ":" + m.Groups[2].Value).Order(StringComparer.Ordinal));
        }

        var plannedSection = DocumentedMembers.Section(document, "## Planned for a 1.x minor release");
        var labelBullet = plannedSection.Split('\n').First(static l => l.StartsWith("- `kind.label`:", StringComparison.Ordinal));
        var writes = labelBullet[..labelBullet.IndexOf(" The labels", StringComparison.Ordinal)];
        Assert.Equal(labels, Names(writes[..writes.IndexOf('(', StringComparison.Ordinal)]).Skip(1).Order(StringComparer.Ordinal));
        Assert.Equal(ruleIds, Names(writes[writes.IndexOf('(', StringComparison.Ordinal)..]).Order(StringComparer.Ordinal));
        var plannedLabels = Names(labelBullet[labelBullet.IndexOf(" The labels", StringComparison.Ordinal)..]);
        Assert.NotEmpty(plannedLabels);
        Assert.Empty(plannedLabels.Intersect(labels));

        var familyBullet = plannedSection.Split('\n').First(static l => l.StartsWith("- `format.family`:", StringComparison.Ordinal));
        var plannedByBullet = Names(familyBullet[..familyBullet.IndexOf(" Schema 1.0 writes", StringComparison.Ordinal)]).Skip(1).ToArray();
        Assert.Equal(planned.Order(StringComparer.Ordinal), plannedByBullet.Order(StringComparer.Ordinal));
        Assert.Equal(families.Order(StringComparer.Ordinal), Names(familyBullet[familyBullet.IndexOf(" Schema 1.0 writes", StringComparison.Ordinal)..]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DocumentedMembers_ReadsIssueCodeTablesAndSections()
    {
        const string markdown = """
            # t

            ## `issues[]`

            Intro.

            | Code | When |
            |---|---|
            | `a` | x |
            | `b` | y |

            ### Sub

            text `notACode`

            ## `evidence[]`

            | Code | When |
            |---|---|
            | `later` | z |
            """;
        Assert.Equal(["a", "b"], DocumentedMembers.IssueCodeTableCodes(markdown));
        Assert.Throws<InvalidDataException>(() => DocumentedMembers.IssueCodeTableCodes("# t\n"));
        Assert.Throws<InvalidDataException>(() => DocumentedMembers.IssueCodeTableCodes("## `issues[]`\n\n| Code | When |\n|---|---|\n| plain | x |\n"));
        Assert.Equal(["a", "b"], DocumentedMembers.LayoutOnlyIssueCodes("- **Issues.** Every issue code that squalify writes comes from here. So no code is dropped: `a` and `b` are written with the same counts"));
    }

    [Fact]
    public void DocumentedMembers_ReadsRowsAndNestedKeys()
    {
        const string markdown = """
            # t

            ## Top level

            | Key | Type | Always | Description |
            |---|---|---|---|
            | `schemaVersion` | string | yes | `"1.0"` |
            | `layoutOnly` | boolean | only with `--layout-only` | marker |
            | `stdf` | object | planned | later |

            ### Key order of nested objects

            | Object | Keys, in order |
            |---|---|
            | `limits` | `a`, `b` (always; sq-1), `c` (`c1`, `c2`), `d`; trailing prose is not a key |
            | `table.fields[]` | `index`, `name` (null when none), then `type` |
            | `table.fields[]` of a layout-only profile (sq-47) | `index`, `name` |
            | `fields[].numeric` | `min`, `max` |
            | `binary` | `bytes`, `blocks` (planned; sq-10) |
            | `stdf.records[]` (planned) | `name` |
            """;
        var parsed = DocumentedMembers.Parse(markdown);
        Assert.Equal(["layoutOnly", "schemaVersion"], parsed.Sections[string.Empty].Members.Order(StringComparer.Ordinal));
        Assert.Equal(["a", "b", "c", "d"], parsed.Sections["limits"].Members);
        Assert.Equal(["c1", "c2"], parsed.Sections["limits.c"].Members);
        Assert.Equal(["index", "name", "type"], parsed.Sections["table.fields"].Members);
        Assert.Equal(["index", "name"], parsed.Sections["table.fields#layoutOnlyField"].Members);
        Assert.Equal(["min", "max"], parsed.Sections["table.fields.numeric"].Members);
        Assert.Equal(["bytes"], parsed.Sections["binary"].Members);
        Assert.Equal(["binary/blocks", "stdf", "stdf.records"], parsed.Planned.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("stdf.records", parsed.Sections.Keys);
    }

    [Fact]
    public void DocumentedMembers_TopLevelRow_IsPlannedOnlyWhenItsAlwaysColumnSaysSo()
    {
        const string markdown = """
            ## Top level

            | Key | Type | Always | Description |
            |---|---|---|---|
            | `produced` | object | yes | what was once planned for sq-1 is written now |
            | `plannedOne` | array | planned (keyValue) | later |
            | `plannedTwo` | object | Planned | later |
            | `optional` | object | only with `--x` | a planned option is not a planned member |
            """;
        var parsed = DocumentedMembers.Parse(markdown);
        Assert.Equal(["produced", "optional"], parsed.Sections[string.Empty].Members);
        Assert.Equal(["plannedOne", "plannedTwo"], parsed.Planned.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DocumentedMembers_RejectsAnObjectDocumentedTwice()
    {
        const string markdown = """
            ### Key order of nested objects

            | Object | Keys, in order |
            |---|---|
            | `limits` | `a` |
            | `limits` | `b` |
            """;
        var error = Assert.Throws<InvalidDataException>(() => DocumentedMembers.Parse(markdown));
        Assert.Contains("limits documented twice", error.Message);
    }

    [Fact]
    public void DocumentedMembers_RejectsARowItCannotRead()
    {
        const string markdown = """
            ### Key order of nested objects

            | Object | Keys, in order |
            |---|---|
            | `limits` | as in the example |
            """;
        Assert.Throws<InvalidDataException>(() => DocumentedMembers.Parse(markdown));
    }

    private static string Show(string path)
    {
        return path.Length == 0 ? "(root)" : path;
    }

    private static string Join(IEnumerable<string> names)
    {
        return string.Join(", ", names);
    }
}
