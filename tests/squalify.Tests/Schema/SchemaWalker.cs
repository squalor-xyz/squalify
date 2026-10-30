// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace squalor.squalify.Tests.Schema;

/// <summary>An object of the schema: its path, its member names and the ones that are required.</summary>
/// <param name="Path">
/// The path from the root, names joined by dots, with no <c>[]</c> for an array (<c>table.fields</c> is the object in
/// <c>table.fields[]</c>); the root is the empty string. An alternative of a <c>oneOf</c> whose members differ from the union gets
/// its own section, <c>path#definitionName</c>.
/// </param>
/// <param name="Members">The member names, in the order of the schema (the union over the alternatives of a <c>oneOf</c>).</param>
/// <param name="Required">The names that are required (in every alternative of a <c>oneOf</c>).</param>
internal sealed record SchemaSection(string Path, IReadOnlyList<string> Members, IReadOnlyList<string> Required);

/// <summary>
/// Walks a schema of the supported subset: the keywords it uses, the objects it describes and the schema at an instance path.
/// <c>$ref</c> is followed, and the alternatives of a <c>oneOf</c> are unioned (<see cref="Sections"/>) or all returned
/// (<see cref="NodesAt"/>), never dropped.
/// </summary>
internal static class SchemaWalker
{
    private const string DefsPrefix = "#/$defs/";

    /// <summary>
    /// The keywords a schema uses, found by walking its structure: a key under <c>properties</c> or <c>$defs</c> is a name, not a
    /// keyword, and the values of <c>enum</c> and <c>const</c> are data.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <returns>The distinct keywords, ordinally sorted.</returns>
    public static IReadOnlyList<string> Keywords(JsonElement schema)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        CollectKeywords(schema, found);
        return found.ToArray();
    }

    /// <summary>
    /// Every object the schema describes, by path. A closed map (an object with <c>properties</c> and nothing required: the keys are a
    /// vocabulary, such as the type names of <c>typeVotes</c>) is not a section. Where a <c>oneOf</c> offers several shapes for one
    /// place, the section of the path holds the union of their members, and a shape whose members differ from the union gets a
    /// section of its own (<c>table.fields#layoutOnlyField</c>).
    /// </summary>
    /// <param name="schema">The schema root.</param>
    /// <returns>The sections by path.</returns>
    public static IReadOnlyDictionary<string, SchemaSection> Sections(JsonElement schema)
    {
        var sections = new Dictionary<string, SchemaSection>(StringComparer.Ordinal);
        Visit(schema, schema, string.Empty, sections, 0);
        return sections;
    }

    /// <summary>
    /// The schemas that can apply to the instance value at a path (names for object members, decimal indexes for array elements):
    /// every alternative of a <c>oneOf</c> on the way, <c>$ref</c> followed. Empty when the schema says nothing about the path.
    /// </summary>
    /// <param name="schema">The schema root.</param>
    /// <param name="segments">The path of the instance value, as the segments of its JSON pointer.</param>
    /// <returns>The concrete schemas.</returns>
    public static IEnumerable<JsonElement> NodesAt(JsonElement schema, IReadOnlyList<string> segments)
    {
        var current = Expand(schema, schema, "#").Select(static e => e.Node).ToList();
        foreach (var segment in segments)
        {
            var next = new List<JsonElement>();
            var isIndex = int.TryParse(segment, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _);
            foreach (var node in current)
            {
                if (isIndex && node.TryGetProperty("items", out var items))
                {
                    next.AddRange(Expand(schema, items, "items").Select(static e => e.Node));
                }
                else if (!isIndex && node.TryGetProperty("properties", out var properties) && properties.TryGetProperty(segment, out var property))
                {
                    next.AddRange(Expand(schema, property, segment).Select(static e => e.Node));
                }
                else if (!isIndex && node.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.Object)
                {
                    next.AddRange(Expand(schema, additional, segment).Select(static e => e.Node));
                }
            }

            current = next;
        }

        return current;
    }

    private static void Visit(JsonElement root, JsonElement node, string path, Dictionary<string, SchemaSection> sections, int depth)
    {
        if (depth > 32)
        {
            throw new InvalidOperationException("The schema nests too deeply (a $ref cycle?) at " + path);
        }

        var shapes = Expand(root, node, "#");
        var objects = shapes.Where(static s => s.Node.TryGetProperty("properties", out _)).ToList();

        // An array of objects: its items describe the objects at the same path.
        foreach (var shape in shapes.Where(static s => s.Node.TryGetProperty("items", out _)))
        {
            Visit(root, shape.Node.GetProperty("items"), path, sections, depth + 1);
        }

        if (objects.Count == 0 || objects.All(static o => !HasRequired(o.Node)))
        {
            return;
        }

        var members = objects.SelectMany(static o => o.Node.GetProperty("properties").EnumerateObject().Select(static p => p.Name)).Distinct(StringComparer.Ordinal).ToArray();
        var required = objects.Select(static o => RequiredOf(o.Node)).Aggregate((a, b) => a.Intersect(b, StringComparer.Ordinal).ToArray());
        Add(sections, new SchemaSection(path, members, required));
        if (objects.Count > 1)
        {
            foreach (var shape in objects)
            {
                var own = shape.Node.GetProperty("properties").EnumerateObject().Select(static p => p.Name).ToArray();
                if (!own.OrderBy(static n => n, StringComparer.Ordinal).SequenceEqual(members.OrderBy(static n => n, StringComparer.Ordinal), StringComparer.Ordinal))
                {
                    Add(sections, new SchemaSection(path + "#" + shape.Name, own, RequiredOf(shape.Node)));
                }
            }
        }

        foreach (var name in members)
        {
            foreach (var shape in objects.Where(o => o.Node.GetProperty("properties").TryGetProperty(name, out _)))
            {
                Visit(root, shape.Node.GetProperty("properties").GetProperty(name), path.Length == 0 ? name : path + "." + name, sections, depth + 1);
            }
        }
    }

    private static void Add(Dictionary<string, SchemaSection> sections, SchemaSection section)
    {
        if (sections.TryGetValue(section.Path, out var existing))
        {
            sections[section.Path] = existing with { Members = existing.Members.Union(section.Members, StringComparer.Ordinal).ToArray() };
        }
        else
        {
            sections[section.Path] = section;
        }
    }

    private static bool HasRequired(JsonElement node)
    {
        return node.TryGetProperty("required", out var required) && required.GetArrayLength() > 0;
    }

    private static string[] RequiredOf(JsonElement node)
    {
        return node.TryGetProperty("required", out var required) ? required.EnumerateArray().Select(static e => e.GetString()!).ToArray() : [];
    }

    // The concrete schemas behind a node: $ref followed, the alternatives of a oneOf listed, each with the name of its definition.
    private static List<(string Name, JsonElement Node)> Expand(JsonElement root, JsonElement node, string name)
    {
        var result = new List<(string, JsonElement)>();
        if (node.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        var concrete = true;
        if (node.TryGetProperty("$ref", out var reference))
        {
            var target = reference.GetString()!;
            result.AddRange(Expand(root, root.GetProperty("$defs").GetProperty(target[DefsPrefix.Length..]), target[DefsPrefix.Length..]));
            concrete = node.EnumerateObject().Any(static p => p.Name != "$ref");
        }

        if (node.TryGetProperty("oneOf", out var alternatives))
        {
            foreach (var alternative in alternatives.EnumerateArray())
            {
                result.AddRange(Expand(root, alternative, alternative.TryGetProperty("$ref", out var r) ? r.GetString()![DefsPrefix.Length..] : name));
            }

            concrete = node.EnumerateObject().Any(static p => p.Name is not ("oneOf" or "$ref"));
        }

        if (concrete)
        {
            result.Add((name, node));
        }

        return result;
    }

    private static void CollectKeywords(JsonElement node, SortedSet<string> found)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var member in node.EnumerateObject())
        {
            found.Add(member.Name);
            switch (member.Name)
            {
                case "properties" or "$defs":
                    foreach (var named in member.Value.EnumerateObject())
                    {
                        CollectKeywords(named.Value, found);
                    }

                    break;
                case "items" or "additionalProperties":
                    CollectKeywords(member.Value, found);
                    break;
                case "oneOf":
                    foreach (var alternative in member.Value.EnumerateArray())
                    {
                        CollectKeywords(alternative, found);
                    }

                    break;
            }
        }
    }
}
