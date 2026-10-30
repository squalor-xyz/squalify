// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Nodes;

namespace squalor.squalify.Tests.Schema;

/// <summary>
/// Checks on the schema itself, against the descriptors the profiler writes: it is closed everywhere (owner decision S12-7), and it
/// requires every member that every descriptor of the corpus holds, except the ones listed here with their reason. The mutation
/// tests take the required keys and the closure from the schema, so a schema that lost a <c>required</c> entry or its
/// <c>additionalProperties: false</c> would otherwise go unnoticed.
/// </summary>
public class SchemaCoverageTests
{
    // Members that are in every descriptor of the corpus and are still optional in the schema, by the shape (path, with "[]" left out,
    // and "#layoutOnlyField" for the layout-only shape of a field): each is written only in some cases (docs/descriptor.md, "Optional
    // versus null"), and the corpus happens to hold none of the other cases.
    private static readonly Dictionary<string, string[]> OptionalYetInEveryDescriptor = new(StringComparer.Ordinal)
    {
        // The hash is written only when the whole file was read; no golden has a byte limit that stops short.
        ["file"] = ["sha256"],

        // The mean and the deviation are left out when the sums overflow, and the deviation for fewer than two values.
        ["table.fields.numeric"] = ["mean", "stddev"],

        // The marker and the error bound belong to a top list that is approximate; only the writer model has one.
        ["table.fields.top"] = ["approximate", "maxError"],
    };

    [Fact]
    public void Schema_EveryObjectIsClosed_ExceptTheMapsOfCountsByFreeKey()
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var open = new List<string>();
        Walk(schema.RootElement, "#", open);
        foreach (var def in schema.RootElement.GetProperty("$defs").EnumerateObject())
        {
            Walk(def.Value, "#/$defs/" + def.Name, open);
        }

        Assert.Empty(open);
    }

    private static void Walk(JsonElement node, string pointer, List<string> open)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (node.TryGetProperty("properties", out var properties))
        {
            if (!node.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False)
            {
                open.Add(pointer + " has properties but is not closed (additionalProperties must be false)");
            }

            foreach (var property in properties.EnumerateObject())
            {
                Walk(property.Value, pointer + "/properties/" + property.Name, open);
            }
        }
        else if (!node.TryGetProperty("$ref", out _) && node.TryGetProperty("type", out var type) && (type.ValueKind == JsonValueKind.String ? type.GetString() == "object" : type.EnumerateArray().Any(static t => t.GetString() == "object")))
        {
            // An object with no listed members is a map from a free key (a pattern, a null spelling) to a count: the only open
            // objects, and the values are counts.
            if (!node.TryGetProperty("additionalProperties", out var map) || map.ValueKind != JsonValueKind.Object || !map.TryGetProperty("$ref", out var reference) || reference.GetString() != "#/$defs/positive")
            {
                open.Add(pointer + " is an object with no members whose values are not positive counts");
            }
        }

        if (node.TryGetProperty("items", out var items))
        {
            Walk(items, pointer + "/items", open);
        }

        if (node.TryGetProperty("oneOf", out var alternatives))
        {
            var index = 0;
            foreach (var alternative in alternatives.EnumerateArray())
            {
                Walk(alternative, pointer + "/oneOf/" + index++, open);
            }
        }
    }

    [Fact]
    public void Schema_RequiresEveryMemberThatEveryDescriptorOfTheCorpusHolds()
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var always = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var required = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var golden in SchemaTests.GoldenNames())
        {
            var root = JsonNode.Parse(golden == "model:FullDelimited" ? DescriptorWriter.ToBytes(TestModels.FullDelimited()) : File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", golden)))!;
            Visit(schema, root, [], always, required);
        }

        // A profile with a byte limit: the only one that has a scope.
        Visit(schema, DescriptorInvariantsTests.Profile("lab/sweep-preamble.csv", maxBytes: 200), [], always, required);

        var unrequired = new List<string>();
        foreach (var (shape, keys) in always.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            var path = shape[..shape.IndexOf('|', StringComparison.Ordinal)];
            var allowed = OptionalYetInEveryDescriptor.GetValueOrDefault(path, []);
            var missing = keys.Except(required[shape], StringComparer.Ordinal).Except(allowed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
            {
                unrequired.Add($"{(path.Length == 0 ? "(root)" : path)}: {string.Join(", ", missing)}");
            }
        }

        Assert.Empty(unrequired);
        Assert.True(always.Count > 20, "only " + always.Count + " shapes of objects were found in the corpus");
    }

    private static void Visit(JsonDocument schema, JsonNode? node, List<string> segments, Dictionary<string, HashSet<string>> always, Dictionary<string, HashSet<string>> required)
    {
        switch (node)
        {
            case JsonObject obj:
                var path = string.Join(".", segments.Where(static s => !int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)));
                if (SchemaMutationTests.Shape(SchemaMutationTests.Concrete(schema, [.. segments]), obj) is { } shape && shape.TryGetProperty("properties", out _) && shape.TryGetProperty("required", out _))
                {
                    var key = path + "|" + (shape.TryGetProperty("required", out var list) ? string.Join(",", list.EnumerateArray().Select(static e => e.GetString())) : string.Empty);
                    var keys = obj.Select(static p => p.Key).ToHashSet(StringComparer.Ordinal);
                    if (always.TryGetValue(key, out var seen))
                    {
                        seen.IntersectWith(keys);
                    }
                    else
                    {
                        always[key] = keys;
                        required[key] = (shape.TryGetProperty("required", out var names) ? names.EnumerateArray().Select(static e => e.GetString()!) : []).ToHashSet(StringComparer.Ordinal);
                    }
                }

                foreach (var (name, value) in obj.ToArray())
                {
                    Visit(schema, value, [.. segments, name], always, required);
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    Visit(schema, array[i], [.. segments, i.ToString(System.Globalization.CultureInfo.InvariantCulture)], always, required);
                }

                break;
        }
    }

    // Paths where the schema takes a kind of value that no descriptor of the corpus holds there, and why.
    private static readonly Dictionary<string, string[]> KindsAllowedBeyondTheCorpus = new(StringComparer.Ordinal)
    {
        // A file name with no extension.
        ["hints/extension"] = ["null"],

        // An entry of an aligned list whose owner is not opted in (see Aligned values lists in the document).
        ["layout/regions/[]/values/[]"] = ["null"],

        // Nullable members that the consumer subset lists (see Optional versus null in the document).
        ["table/fields/[]/constant"] = ["null"],
        ["table/fields/[]/nullTokens"] = ["null"],
    };

    [Fact]
    public void Schema_AcceptsOnlyTheKindsOfValueThatTheCorpusHoldsAtThatPlace()
    {
        // For every value of every descriptor of the corpus, a value of each other JSON kind is put in its place. If no descriptor
        // holds that kind at that path (or the path is listed), the schema must refuse it. The kinds come from the descriptors,
        // so a type that was widened in the schema (null added, integer made a number) is noticed.
        var documents = SchemaTests.GoldenNames().Select(static g => JsonNode.Parse(g == "model:FullDelimited" ? DescriptorWriter.ToBytes(TestModels.FullDelimited()) : File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", g)))!).ToList();
        documents.Add(DescriptorInvariantsTests.Profile("lab/sweep-preamble.csv", maxBytes: 200));
        documents.Add(JsonNode.Parse(DescriptorWriter.ToBytes(TestModels.FullBinaryStdf()))!);

        var kinds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var document in documents.Take(documents.Count - 1))
        {
            Record(document, string.Empty, kinds);
        }

        var accepted = new List<string>();
        var probed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents.Take(documents.Count - 1))
        {
            Probe(document, document, string.Empty, kinds, accepted, probed);
        }

        Assert.Empty(accepted.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    private static readonly (string Kind, string Json)[] Others =
    [
        ("null", "null"),
        ("boolean", "true"),
        ("string", "\"~x~\""),
        ("integer", "7"),
        ("number", "1.5"),
        ("array", "[]"),
        ("object", "{}"),
    ];

    private static string KindOf(JsonNode? node)
    {
        return node switch
        {
            null => "null",
            JsonObject => "object",
            JsonArray => "array",
            JsonValue value when value.TryGetValue<bool>(out _) => "boolean",
            JsonValue value when value.TryGetValue<string>(out _) => "string",
            JsonValue value when value.ToJsonString().Contains('.', StringComparison.Ordinal) || value.ToJsonString().Contains('E', StringComparison.Ordinal) => "number",
            _ => "integer",
        };
    }

    private static string PathOf(string parent, string segment)
    {
        return parent.Length == 0 ? segment : parent + "/" + segment;
    }

    private static void Record(JsonNode? node, string path, Dictionary<string, HashSet<string>> kinds)
    {
        if (!kinds.TryGetValue(path, out var set))
        {
            kinds[path] = set = new HashSet<string>(StringComparer.Ordinal);
        }

        set.Add(KindOf(node));
        if (node is JsonObject obj)
        {
            foreach (var (name, value) in obj)
            {
                Record(value, PathOf(path, name), kinds);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                Record(item, PathOf(path, "[]"), kinds);
            }
        }
    }

    private static void Probe(JsonNode root, JsonNode? node, string path, Dictionary<string, HashSet<string>> kinds, List<string> accepted, HashSet<string> probed)
    {
        if (node is JsonObject obj)
        {
            foreach (var (name, value) in obj.ToArray())
            {
                ProbeValue(root, obj, name, -1, value, PathOf(path, name), kinds, accepted, probed);
            }
        }
        else if (node is JsonArray array)
        {
            // Every element is visited, but a path and a kind are probed once (every element has the same schema).
            for (var i = 0; i < array.Count; i++)
            {
                ProbeValue(root, array, null, i, array[i], PathOf(path, "[]"), kinds, accepted, probed);
            }
        }
    }

    private static void ProbeValue(JsonNode root, JsonNode parent, string? key, int index, JsonNode? value, string path, Dictionary<string, HashSet<string>> kinds, List<string> accepted, HashSet<string> probed)
    {
        Probe(root, value, path, kinds, accepted, probed);
        var held = kinds[path];
        var allowed = KindsAllowedBeyondTheCorpus.GetValueOrDefault(path, []);
        foreach (var (kind, json) in Others)
        {
            if (!probed.Add(path + "|" + kind) || held.Contains(kind) || allowed.Contains(kind) || (kind == "integer" && held.Contains("number")) || (kind == "number" && held.Contains("integer") && path.Contains("numeric", StringComparison.Ordinal)))
            {
                continue;
            }

            Set(parent, key, index, JsonNode.Parse(json));
            var errors = SchemaTests.Validate(root);
            Set(parent, key, index, value);
            if (errors.Count == 0)
            {
                accepted.Add($"{path}: a {kind} value is accepted (the corpus holds {string.Join(", ", held.Order(StringComparer.Ordinal))})");
            }
        }
    }

    private static void Set(JsonNode parent, string? key, int index, JsonNode? value)
    {
        if (parent is JsonObject obj)
        {
            obj[key!] = value;
        }
        else
        {
            ((JsonArray)parent)[index] = value;
        }
    }
}
