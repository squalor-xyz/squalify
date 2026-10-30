// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Nodes;

namespace squalor.squalify.Tests.Schema;

/// <summary>
/// Mutates every golden at every object and value and checks that the schema notices, and where: a removed required key is
/// reported at the object that lost it, an added key at the key, and a value of the wrong kind at the value. It is the check that
/// every <c>required</c>, <c>additionalProperties</c> and <c>type</c> of the schema does work, and that the error pointers are right.
/// </summary>
public class SchemaMutationTests
{
    // Items past the first few of an array are mutated only when the array is short: they repeat the same schema.
    private const int ArrayItemsMutated = 3;

    public static TheoryData<string> Cases() => SchemaTests.GoldenCases();

    private static JsonNode Load(string golden)
    {
        var bytes = golden == "model:FullDelimited"
            ? DescriptorWriter.ToBytes(TestModels.FullDelimited())
            : File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", golden.Replace('/', Path.DirectorySeparatorChar)));
        return JsonNode.Parse(bytes)!;
    }

    private static IReadOnlyList<ValidationError> Validate(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return SchemaFiles.Validator.Validate(document.RootElement);
    }

    /// <summary>Every value of the document with its pointer, its path segments and the node that holds it (post-order).</summary>
    private static IEnumerable<(string Pointer, string[] Segments, JsonNode Node, JsonNode? Parent, string? Key, int Index)> Values(JsonNode node, string pointer = "", string[]? segments = null, JsonNode? parent = null, string? key = null, int index = -1)
    {
        segments ??= [];
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToArray())
                {
                    foreach (var inner in Values(property.Value!, pointer + "/" + SubsetValidator.Escape(property.Key), [.. segments, property.Key], obj, property.Key))
                    {
                        yield return inner;
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < Math.Min(array.Count, ArrayItemsMutated); i++)
                {
                    foreach (var inner in Values(array[i]!, pointer + "/" + i, [.. segments, i.ToString(System.Globalization.CultureInfo.InvariantCulture)], array, null, i))
                    {
                        yield return inner;
                    }
                }

                break;
        }

        yield return (pointer, segments, node, parent, key, index);
    }

    internal static JsonElement[] Concrete(JsonDocument schema, string[] segments)
    {
        return SchemaWalker.NodesAt(schema.RootElement, segments).ToArray();
    }

    // The schema of an object instance among the alternatives at its path: the one that lists every key of the instance and has all its
    // required keys there (the shape the instance has; a layout-only field is not the shape of a full field, which lists its keys too).
    internal static JsonElement? Shape(JsonElement[] nodes, JsonObject instance)
    {
        var keys = instance.Select(static p => p.Key).ToArray();
        foreach (var node in nodes)
        {
            if (!node.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            var closed = node.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False;
            var required = node.TryGetProperty("required", out var list) ? list.EnumerateArray().Select(static e => e.GetString()!) : [];
            if ((keys.All(k => properties.TryGetProperty(k, out _)) || !closed) && required.All(k => keys.Contains(k)))
            {
                return node;
            }
        }

        return null;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Schema_NoticesEveryMissingRequiredKey_AtTheObjectThatLostIt(string golden)
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var root = Load(golden);
        Assert.Empty(Validate(root));
        var checkedKeys = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pointer, segments, node, _, _, _) in Values(root))
        {
            if (node is not JsonObject obj || Shape(Concrete(schema, segments), obj) is not { } shape || !shape.TryGetProperty("required", out var required))
            {
                continue;
            }

            visited.Add(pointer);

            foreach (var key in required.EnumerateArray().Select(static e => e.GetString()!))
            {
                var value = obj[key];
                obj.Remove(key);
                var errors = Validate(root);
                obj[key] = value;
                Assert.True(
                    errors.Any(e => e.Pointer == pointer && e.Message.Contains($"missing required key '{key}'", StringComparison.Ordinal)),
                    $"{golden}: removing '{key}' at {Show(pointer)} was not reported there; errors: {string.Join(" | ", errors)}");
                checkedKeys++;
            }
        }

        Assert.True(checkedKeys > 5, $"{golden}: only {checkedKeys} required keys were found");
        AssertFieldsWereVisited(root, visited, golden);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Schema_NoticesEveryExtraKey_OfAClosedObject(string golden)
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var root = Load(golden);
        var checkedObjects = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pointer, segments, node, _, _, _) in Values(root))
        {
            if (node is not JsonObject obj || Shape(Concrete(schema, segments), obj) is not { } shape)
            {
                continue;
            }

            visited.Add(pointer);

            var closed = shape.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False;
            obj["zz_extra"] = 1;
            var errors = Validate(root);
            obj.Remove("zz_extra");
            var at = pointer + "/zz_extra";
            if (closed)
            {
                Assert.True(
                    errors.Any(e => e.Pointer == at && e.Message.Contains("unexpected key 'zz_extra'", StringComparison.Ordinal)),
                    $"{golden}: an extra key at {Show(pointer)} was not reported; errors: {string.Join(" | ", errors)}");
                checkedObjects++;
            }
            else
            {
                // A map of counts by free key (patterns, nullSpellings) takes any key, and its value is checked instead.
                Assert.DoesNotContain(errors, e => e.Pointer == at && e.Message.Contains("unexpected key", StringComparison.Ordinal));
            }
        }

        Assert.True(checkedObjects > 5, $"{golden}: only {checkedObjects} objects were closed");
        AssertFieldsWereVisited(root, visited, golden);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Schema_NoticesAValueOfTheWrongKind_AtTheValue(string golden)
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var root = Load(golden);
        var checkedValues = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (pointer, segments, node, parent, key, index) in Values(root))
        {
            if (parent is null)
            {
                continue;
            }

            // Where a oneOf offers several shapes, a value is wrong when no shape allows its kind.
            var nodes = Concrete(schema, segments);
            if (nodes.Length == 0 || nodes.Any(static n => n.TryGetProperty("oneOf", out _)))
            {
                continue;
            }

            JsonNode? wrong;
            if (nodes.All(static n => n.TryGetProperty("type", out _)))
            {
                var allowed = nodes
                    .SelectMany(static n => n.GetProperty("type").ValueKind == JsonValueKind.String ? [n.GetProperty("type").GetString()!] : n.GetProperty("type").EnumerateArray().Select(static e => e.GetString()!))
                    .ToHashSet(StringComparer.Ordinal);
                wrong = new JsonNode?[] { JsonValue.Create(true), JsonValue.Create("~wrong~"), JsonValue.Create(12345), new JsonArray(), new JsonObject() }
                    .FirstOrDefault(c => !allowed.Contains(KindOf(c)) && !(allowed.Contains("number") && KindOf(c) == "integer"));
            }
            else if (nodes.All(static n => n.TryGetProperty("enum", out _) || n.TryGetProperty("const", out _)))
            {
                wrong = JsonValue.Create("~wrong~");
            }
            else
            {
                continue;
            }

            if (wrong is null)
            {
                continue;
            }

            Replace(parent, key, index, wrong);
            var errors = Validate(root);
            Replace(parent, key, index, node);
            Assert.True(
                errors.Any(e => e.Pointer == pointer),
                $"{golden}: a value of the wrong kind at {Show(pointer)} was not reported there; errors: {string.Join(" | ", errors.Take(3))}");
            checkedValues++;
            visited.Add(pointer);
        }

        Assert.True(checkedValues > 20, $"{golden}: only {checkedValues} values were changed");
        if (root["table"]?["fields"] is JsonArray { Count: > 0 })
        {
            Assert.Contains(visited, static p => p.StartsWith("/table/fields/0/", StringComparison.Ordinal));
        }
    }

    // The fields sit under a oneOf (a full field or a layout-only one): the mutations must have reached them.
    private static void AssertFieldsWereVisited(JsonNode root, HashSet<string> visited, string golden)
    {
        if (root["table"]?["fields"] is JsonArray { Count: > 0 })
        {
            Assert.True(visited.Contains("/table/fields/0"), $"{golden}: the fields of the table were not mutated (the schema of a field was not found under its oneOf)");
        }
    }

    private static void Replace(JsonNode parent, string? key, int index, JsonNode? value)
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

    private static string KindOf(JsonNode? node)
    {
        return node switch
        {
            null => "null",
            JsonObject => "object",
            JsonArray => "array",
            JsonValue value when value.TryGetValue<bool>(out _) => "boolean",
            JsonValue value when value.TryGetValue<string>(out _) => "string",
            _ => "integer",
        };
    }

    private static string Show(string pointer)
    {
        return pointer.Length == 0 ? "(root)" : pointer;
    }
}
