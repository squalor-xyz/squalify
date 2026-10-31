// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.RegularExpressions;

namespace squalor.squalify.Tests.Schema;

/// <summary>
/// What <c>docs/descriptor.md</c> marks as planned for a 1.x minor release is produced by nothing in this version: no descriptor that
/// the profiler writes holds it, and the published schema does not describe it. The planned members and objects are the ones that
/// <see cref="DocumentedMembers"/> reads out of the document (the one reader of its tables); the planned enum values (families and
/// kind labels) are listed here and must be named in the document's section on what is planned.
/// </summary>
public partial class PlannedMembersTests
{
    // The writer-model golden that pins the key order of the planned members. It is built by hand (TestModels), not by a profile,
    // so it holds them on purpose; every other golden is the output of the profiler.
    private const string KeyOrderPin = "full-binary-stdf.squalify.json";

    // The families no profiler produces yet (owner decision S12-6) and the kind labels beyond the three interim ones (docs/design.md,
    // "Kind label"; the interim labels are binary, empty and text).
    private static readonly string[] PlannedFamilies = ["touchstone", "keyValue", "stdf"];

    private static readonly string[] PlannedLabels =
    [
        "stdfTestLog", "touchstoneNetwork", "keyValueDocument", "labSweepTable", "measurementTable", "genericTable", "textLog", "archive",
    ];

    [Fact]
    public void Docs_PlannedMembers_AreNotInTheSchemaOrTheWriter()
    {
        var document = SchemaFiles.ReadDoc();
        var planned = DocumentedMembers.Parse(document).Planned;

        // The document marks what the owner decided is planned (S12-6, 2026-10-30), and nothing that is produced: limits.stdfTests
        // and the key hints.stdf (written as null by every profile) are real members.
        string[] mustBePlanned =
        [
            "stdf", "stdf.records", "stdf.tests", "sections", "binary/blocks", "binary/segments", "hints.stdf",
        ];
        Assert.Empty(mustBePlanned.Except(planned, StringComparer.Ordinal));

        // The planned dialects are marked in the by-family table and the planned section, not as key-order rows (owner decision, 2026-10-30).
        Assert.DoesNotContain(planned, static p => p.StartsWith("format.dialect.", StringComparison.Ordinal));
        Assert.DoesNotContain("limits/stdfTests", planned);
        Assert.DoesNotContain("hints/stdf", planned);

        // The document names the planned enum values in its section on what is planned.
        var section = PlannedSection(document);
        foreach (var value in PlannedFamilies.Concat(PlannedLabels))
        {
            Assert.True(section.Contains('`' + value + '`', StringComparison.Ordinal), $"the planned section of descriptor.md does not name the enum value {value}");
        }

        // No descriptor holds a planned member or value: the goldens of the profiler, and a live profile of each fixture.
        var descriptors = new List<(string Name, string Json)>();
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        foreach (var golden in Directory.GetFiles(fixtures, "*.squalify.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetRelativePath(fixtures, golden).Replace('\\', '/');
            if (name == KeyOrderPin)
            {
                continue;
            }

            descriptors.Add((name, File.ReadAllText(golden)));
            var fixture = golden[..^".squalify.json".Length];
            if (File.Exists(fixture))
            {
                descriptors.Add(("profile of " + Path.GetRelativePath(fixtures, fixture).Replace('\\', '/'), Live(fixture, layoutOnly: false)));
            }
        }

        descriptors.Add(("layout-only profile of lab/sweep-preamble.csv", Live(Path.Combine(fixtures, "lab", "sweep-preamble.csv"), layoutOnly: true)));
        Assert.True(descriptors.Count >= 20, "the goldens and the live profiles were found (" + descriptors.Count + ")");

        var found = new List<string>();
        foreach (var (name, json) in descriptors)
        {
            using var parsed = JsonDocument.Parse(json);
            foreach (var path in planned)
            {
                foreach (var node in At(parsed.RootElement, Segments(path)))
                {
                    // hints.stdf is planned as an object; every profile writes the key, with null.
                    if (node.ValueKind != JsonValueKind.Null)
                    {
                        found.Add($"{name}: planned {path} is written ({Brief(node)})");
                    }
                }
            }

            foreach (var family in At(parsed.RootElement, ["format", "family"]))
            {
                if (PlannedFamilies.Contains(family.GetString()))
                {
                    found.Add($"{name}: planned family {family.GetString()} is written");
                }
            }

            foreach (var label in At(parsed.RootElement, ["kind", "label"]))
            {
                if (PlannedLabels.Contains(label.GetString()))
                {
                    found.Add($"{name}: planned kind label {label.GetString()} is written");
                }
            }
        }

        Assert.Empty(found);

        // The finder is not blind: the hand-built pin does hold the planned members.
        using (var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, KeyOrderPin))))
        {
            Assert.Contains(planned, p => At(pin.RootElement, Segments(p)).Any(static n => n.ValueKind != JsonValueKind.Null));
            Assert.Contains("stdf", At(pin.RootElement, ["format", "family"]).Select(static e => e.GetString()));
            Assert.Contains("stdfTestLog", At(pin.RootElement, ["kind", "label"]).Select(static e => e.GetString()));
        }

        // The schema describes none of them: no object or member at a planned path (hints.stdf may be there, as null only), and no
        // planned value in the enums of the family and of the kind label.
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var described = new List<string>();
        foreach (var path in planned)
        {
            foreach (var node in SchemaWalker.NodesAt(schema.RootElement, Segments(path)))
            {
                if (!node.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "null")
                {
                    described.Add($"the schema describes planned {path}");
                }
            }
        }

        AssertEnumExcludes(schema.RootElement, ["format", "family"], PlannedFamilies, described);
        AssertEnumExcludes(schema.RootElement, ["kind", "label"], PlannedLabels, described);
        Assert.Empty(described);
    }

    private static void AssertEnumExcludes(JsonElement schema, string[] path, string[] values, List<string> problems)
    {
        var nodes = SchemaWalker.NodesAt(schema, path).ToList();
        Assert.NotEmpty(nodes);
        foreach (var node in nodes)
        {
            Assert.True(node.TryGetProperty("enum", out var allowed), string.Join('.', path) + " is a closed enum");
            foreach (var value in allowed.EnumerateArray().Select(static e => e.GetString()!).Where(values.Contains))
            {
                problems.Add($"the enum of {string.Join('.', path)} holds the planned value {value}");
            }
        }
    }

    private static string Live(string fixture, bool layoutOnly)
    {
        var descriptor = Profiler.Profile(fixture, ProfileOptions.Create(false, [], layoutOnly: layoutOnly));
        return System.Text.Encoding.UTF8.GetString(DescriptorWriter.ToBytes(descriptor));
    }

    // A planned path is "member" (of the root), "a.b" (an object) or "object/member"; arrays in the way are walked through.
    private static string[] Segments(string planned)
    {
        return planned.Replace('/', '.').Split('.');
    }

    private static IEnumerable<JsonElement> At(JsonElement root, string[] segments)
    {
        var current = new List<JsonElement> { root };
        foreach (var segment in segments)
        {
            var next = new List<JsonElement>();
            foreach (var node in current.SelectMany(Flatten))
            {
                if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(segment, out var member))
                {
                    next.Add(member);
                }
            }

            current = next;
        }

        return current;
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node)
    {
        return node.ValueKind == JsonValueKind.Array ? node.EnumerateArray().SelectMany(Flatten) : [node];
    }

    private static string Brief(JsonElement node)
    {
        var text = node.GetRawText();
        return text.Length <= 40 ? text : text[..40] + "...";
    }

    private static string PlannedSection(string document)
    {
        const string Heading = "\n## Planned for a 1.x minor release\n";
        var start = document.IndexOf(Heading, StringComparison.Ordinal);
        Assert.True(start >= 0, "descriptor.md has the section '## Planned for a 1.x minor release'");
        var next = NextSecondLevelHeading().Match(document, start + Heading.Length);
        return next.Success ? document[start..next.Index] : document[start..];
    }

    [GeneratedRegex(@"\n## ")]
    private static partial Regex NextSecondLevelHeading();
}
