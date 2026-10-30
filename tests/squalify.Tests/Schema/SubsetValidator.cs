// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace squalor.squalify.Tests.Schema;

/// <summary>A schema that cannot be loaded: a keyword outside the supported subset, or a malformed value.</summary>
/// <param name="message">All the problems found, one per line, each as a pointer into the schema and a message.</param>
internal sealed class SchemaLoadException(string message) : Exception(message);

/// <summary>One violation: where it is (a JSON pointer into the instance) and what is wrong.</summary>
/// <param name="Pointer">An RFC 6901 pointer into the instance; the empty string is the root.</param>
/// <param name="Message">What is wrong.</param>
internal sealed record ValidationError(string Pointer, string Message)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return (Pointer.Length == 0 ? "(root)" : Pointer) + ": " + Message;
    }
}

/// <summary>
/// A validator for exactly the JSON Schema 2020-12 keywords that <c>schema/descriptor-v1.schema.json</c> uses: <c>$schema</c>,
/// <c>$id</c>, <c>type</c>, <c>required</c>, <c>properties</c>, <c>additionalProperties</c>, <c>enum</c>, <c>const</c>,
/// <c>items</c>, <c>minimum</c>, <c>maximum</c>, <c>pattern</c>, <c>$ref</c> / <c>$defs</c> and <c>oneOf</c> (owner decision
/// 2026-10-13: an in-repo subset validator in the test project, no package). A schema that uses any other keyword is
/// rejected when it is loaded, so the published schema cannot quietly rely on a keyword nothing checks.
/// <para>
/// Choices. <b>All</b> errors are reported, not the first, <b>in document order</b>: the errors of an object come in this order
/// (the missing required keys, in the order of <c>required</c>, at the pointer of the object; then, for each key of the instance
/// in the order it appears, the errors of that key's value, or the unexpected-key error at the pointer of the key). An error
/// carries a JSON pointer into the instance and a message. <c>$ref</c> is local only (<c>#/$defs/name</c>) and is applied next
/// to the other keywords of the same schema. <c>pattern</c> is a search, as in the standard (anchor it for a full match); a final <c>$</c> is the end of the input, as in ECMA-262 (in .NET it would also match before a final line feed), and a <c>$</c> anywhere else is refused at load.
/// <c>type</c> may be a name or an array of names; <c>integer</c> accepts any number with no fractional part (<c>3.0</c>, <c>1E2</c>).
/// <c>additionalProperties</c> is <c>true</c>, <c>false</c> or a schema. <c>items</c> is one schema for every element.
/// </para>
/// <para>
/// <c>oneOf</c>: exactly one alternative must match. When none does and the instance is an object that has all the <c>required</c>
/// keys of at least one alternative (so it was meant to be one of them), the errors reported are those of the alternative with the
/// fewest errors (on a tie, one whose required keys are all present, then the first), each prefixed with <c>[oneOf name]</c>, where
/// the name is the alternative's <c>$ref</c>; so a field with one wrong value is reported against its own shape, not as "matches
/// none of 2". If no alternative has its required keys, or the instance is not an object, a single error says that no alternative
/// matches and lists the first error of each. When more than one alternative matches, that is an error too.
/// </para>
/// </summary>
internal sealed class SubsetValidator
{
    /// <summary>The dialect URI that <c>$schema</c> must carry.</summary>
    public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    private const string DefsPrefix = "#/$defs/";

    private static readonly HashSet<string> KeywordSet = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "type", "required", "properties", "additionalProperties", "enum", "const", "items", "minimum", "maximum", "pattern", "$ref", "$defs", "oneOf",
    };

    private static readonly HashSet<string> TypeNames = new(StringComparer.Ordinal)
    {
        "null", "boolean", "object", "array", "number", "integer", "string",
    };

    private readonly JsonElement _root;
    private readonly Dictionary<string, Regex> _patterns = new(StringComparer.Ordinal);

    private SubsetValidator(JsonElement root)
    {
        _root = root;
    }

    /// <summary>The keywords this validator implements.</summary>
    public static IReadOnlyList<string> Keywords { get; } = KeywordSet.Order(StringComparer.Ordinal).ToArray();

    /// <summary>The schema that was loaded.</summary>
    public JsonElement Schema => _root;

    /// <summary>Loads a schema and checks it: only the supported keywords, well-formed values, resolvable references.</summary>
    /// <param name="json">The schema text.</param>
    /// <returns>The validator.</returns>
    /// <exception cref="SchemaLoadException">The text is not JSON, or the schema uses a keyword outside the subset or is malformed.</exception>
    public static SubsetValidator Load(string json)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new SchemaLoadException("The schema is not valid JSON: " + ex.Message);
        }

        var validator = new SubsetValidator(root);
        var problems = new List<string>();
        validator.CheckSchema(root, string.Empty, true, problems);
        if (problems.Count > 0)
        {
            throw new SchemaLoadException("The schema cannot be loaded:\n" + string.Join("\n", problems));
        }

        return validator;
    }

    /// <summary>Validates an instance against the schema.</summary>
    /// <param name="instance">The JSON value.</param>
    /// <returns>Every violation, in document order; empty when the instance is valid.</returns>
    public IReadOnlyList<ValidationError> Validate(JsonElement instance)
    {
        var errors = new List<ValidationError>();
        Check(_root, instance, string.Empty, errors);
        return errors;
    }

    /// <summary>The deep equality that <c>enum</c> and <c>const</c> use: numbers by value, objects without regard to key order.</summary>
    internal static bool JsonEquals(JsonElement a, JsonElement b)
    {
        var aBool = a.ValueKind is JsonValueKind.True or JsonValueKind.False;
        var bBool = b.ValueKind is JsonValueKind.True or JsonValueKind.False;
        if (aBool || bBool)
        {
            return a.ValueKind == b.ValueKind;
        }

        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Null:
                return true;
            case JsonValueKind.String:
                return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Number:
                return a.TryGetDecimal(out var da) && b.TryGetDecimal(out var db) ? da == db : a.GetDouble() == b.GetDouble();
            case JsonValueKind.Array:
                {
                    var left = a.EnumerateArray().ToArray();
                    var right = b.EnumerateArray().ToArray();
                    return left.Length == right.Length && left.Zip(right).All(static pair => JsonEquals(pair.First, pair.Second));
                }

            case JsonValueKind.Object:
                {
                    var left = a.EnumerateObject().ToArray();
                    var right = b.EnumerateObject().ToArray();
                    return left.Length == right.Length && left.All(p => b.TryGetProperty(p.Name, out var other) && JsonEquals(p.Value, other));
                }

            default:
                return false;
        }
    }

    /// <summary>Escapes a key for a JSON pointer: <c>~</c> as <c>~0</c> and <c>/</c> as <c>~1</c>.</summary>
    internal static string Escape(string key)
    {
        return key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }

    // Loading

    private void CheckSchema(JsonElement node, string pointer, bool isRoot, List<string> problems)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{Show(pointer)}: a schema must be an object");
            return;
        }

        foreach (var member in node.EnumerateObject())
        {
            var at = pointer + "/" + Escape(member.Name);
            var value = member.Value;
            if (!KeywordSet.Contains(member.Name))
            {
                problems.Add($"{at}: unsupported keyword '{member.Name}'");
                continue;
            }

            switch (member.Name)
            {
                case "$schema":
                    RootOnly(member.Name, at, isRoot, problems);
                    if (value.ValueKind != JsonValueKind.String || value.GetString() != Dialect)
                    {
                        problems.Add($"{at}: '$schema' must be \"{Dialect}\"");
                    }

                    break;
                case "$id":
                    RootOnly(member.Name, at, isRoot, problems);
                    if (value.ValueKind != JsonValueKind.String)
                    {
                        problems.Add($"{at}: '$id' must be a string");
                    }

                    break;
                case "$defs":
                    RootOnly(member.Name, at, isRoot, problems);
                    if (value.ValueKind != JsonValueKind.Object)
                    {
                        problems.Add($"{at}: '$defs' must be an object of schemas");
                        break;
                    }

                    foreach (var def in value.EnumerateObject())
                    {
                        CheckSchema(def.Value, at + "/" + Escape(def.Name), false, problems);
                    }

                    break;
                case "type":
                    CheckType(value, at, problems);
                    break;
                case "required":
                    if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(static e => e.ValueKind != JsonValueKind.String))
                    {
                        problems.Add($"{at}: 'required' must be an array of strings");
                    }

                    break;
                case "properties":
                    if (value.ValueKind != JsonValueKind.Object)
                    {
                        problems.Add($"{at}: 'properties' must be an object of schemas");
                        break;
                    }

                    foreach (var property in value.EnumerateObject())
                    {
                        CheckSchema(property.Value, at + "/" + Escape(property.Name), false, problems);
                    }

                    break;
                case "additionalProperties":
                    if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        break;
                    }

                    if (value.ValueKind != JsonValueKind.Object)
                    {
                        problems.Add($"{at}: 'additionalProperties' must be a boolean or a schema");
                        break;
                    }

                    CheckSchema(value, at, false, problems);
                    break;
                case "enum":
                    if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                    {
                        problems.Add($"{at}: 'enum' must be a non-empty array");
                    }

                    break;
                case "const":
                    break;
                case "items":
                    if (value.ValueKind != JsonValueKind.Object)
                    {
                        problems.Add($"{at}: 'items' must be a schema (the tuple form is not supported)");
                        break;
                    }

                    CheckSchema(value, at, false, problems);
                    break;
                case "minimum":
                case "maximum":
                    if (value.ValueKind != JsonValueKind.Number)
                    {
                        problems.Add($"{at}: '{member.Name}' must be a number");
                    }

                    break;
                case "pattern":
                    if (value.ValueKind != JsonValueKind.String)
                    {
                        problems.Add($"{at}: 'pattern' must be a string");
                        break;
                    }

                    try
                    {
                        var source = value.GetString()!;
                        if (!TryEndAnchor(source, out var compiled))
                        {
                            problems.Add($"{at}: 'pattern' has a '$' that is not the end of the pattern; only a final '$' (the end of the input) is supported");
                            break;
                        }

                        _patterns[source] = new Regex(compiled, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
                    }
                    catch (ArgumentException ex)
                    {
                        problems.Add($"{at}: 'pattern' is not a valid regular expression: {ex.Message}");
                    }

                    break;
                case "$ref":
                    CheckRef(value, at, problems);
                    break;
                case "oneOf":
                    if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                    {
                        problems.Add($"{at}: 'oneOf' must be a non-empty array of schemas");
                        break;
                    }

                    var index = 0;
                    foreach (var alternative in value.EnumerateArray())
                    {
                        CheckSchema(alternative, at + "/" + index.ToString(CultureInfo.InvariantCulture), false, problems);
                        index++;
                    }

                    break;
            }
        }
    }

    // In JSON Schema (ECMA-262) $ is the end of the input; in .NET it also matches before a final line feed. A final unescaped $ is
    // rewritten to \z, and an unescaped $ anywhere else outside a character class is refused (false when one is found).
    private static bool TryEndAnchor(string pattern, out string compiled)
    {
        compiled = pattern;
        var inClass = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            switch (pattern[i])
            {
                case '\\':
                    i++;
                    break;
                case '[':
                    inClass = true;
                    break;
                case ']':
                    inClass = false;
                    break;
                case '$' when !inClass && i == pattern.Length - 1:
                    compiled = pattern[..i] + "\\z";
                    break;
                case '$' when !inClass:
                    return false;
            }
        }

        return true;
    }

    private static void RootOnly(string keyword, string at, bool isRoot, List<string> problems)
    {
        if (!isRoot)
        {
            problems.Add($"{at}: '{keyword}' is allowed only at the root of the schema");
        }
    }

    private static void CheckType(JsonElement value, string at, List<string> problems)
    {
        var names = value.ValueKind switch
        {
            JsonValueKind.String => [value.GetString()!],
            JsonValueKind.Array => value.EnumerateArray().Select(static e => e.ValueKind == JsonValueKind.String ? e.GetString()! : string.Empty).ToArray(),
            _ => Array.Empty<string>(),
        };
        if (names.Length == 0)
        {
            problems.Add($"{at}: 'type' must be a type name or a non-empty array of type names");
            return;
        }

        foreach (var name in names.Where(static n => !TypeNames.Contains(n)))
        {
            problems.Add($"{at}: unknown type '{name}'");
        }
    }

    private void CheckRef(JsonElement value, string at, List<string> problems)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            problems.Add($"{at}: '$ref' must be a string");
            return;
        }

        var reference = value.GetString()!;
        if (!reference.StartsWith(DefsPrefix, StringComparison.Ordinal))
        {
            problems.Add($"{at}: '$ref' {reference} is not supported (only {DefsPrefix}name)");
            return;
        }

        if (!_root.TryGetProperty("$defs", out var defs) || defs.ValueKind != JsonValueKind.Object || !defs.TryGetProperty(reference[DefsPrefix.Length..], out _))
        {
            problems.Add($"{at}: '$ref' {reference} does not resolve");
        }
    }

    private static string Show(string pointer)
    {
        return pointer.Length == 0 ? "(root)" : pointer;
    }

    // Validating

    private JsonElement Resolve(string reference)
    {
        return _root.GetProperty("$defs").GetProperty(reference[DefsPrefix.Length..]);
    }

    private void Check(JsonElement schema, JsonElement value, string pointer, List<ValidationError> errors)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            Check(Resolve(reference.GetString()!), value, pointer, errors);
        }

        if (schema.TryGetProperty("type", out var type) && !TypeMatches(type, value))
        {
            errors.Add(new ValidationError(pointer, $"expected {TypeNamesOf(type)}, found {Describe(value)}"));
        }

        if (schema.TryGetProperty("enum", out var allowed) && !allowed.EnumerateArray().Any(a => JsonEquals(a, value)))
        {
            errors.Add(new ValidationError(pointer, $"{Raw(value)} is not one of: {string.Join(", ", allowed.EnumerateArray().Select(Raw))}"));
        }

        if (schema.TryGetProperty("const", out var constant) && !JsonEquals(constant, value))
        {
            errors.Add(new ValidationError(pointer, $"expected the constant {Raw(constant)}, found {Raw(value)}"));
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                CheckNumber(schema, value, pointer, errors);
                break;
            case JsonValueKind.String:
                CheckString(schema, value, pointer, errors);
                break;
            case JsonValueKind.Object:
                CheckObject(schema, value, pointer, errors);
                break;
            case JsonValueKind.Array:
                CheckArray(schema, value, pointer, errors);
                break;
        }

        if (schema.TryGetProperty("oneOf", out var alternatives))
        {
            CheckOneOf(alternatives, value, pointer, errors);
        }
    }

    private static void CheckNumber(JsonElement schema, JsonElement value, string pointer, List<ValidationError> errors)
    {
        var number = value.GetDouble();
        if (schema.TryGetProperty("minimum", out var minimum) && number < minimum.GetDouble())
        {
            errors.Add(new ValidationError(pointer, $"{Raw(value)} is below the minimum {Raw(minimum)}"));
        }

        if (schema.TryGetProperty("maximum", out var maximum) && number > maximum.GetDouble())
        {
            errors.Add(new ValidationError(pointer, $"{Raw(value)} is above the maximum {Raw(maximum)}"));
        }
    }

    private void CheckString(JsonElement schema, JsonElement value, string pointer, List<ValidationError> errors)
    {
        if (schema.TryGetProperty("pattern", out var pattern) && !_patterns[pattern.GetString()!].IsMatch(value.GetString()!))
        {
            errors.Add(new ValidationError(pointer, $"{Raw(value)} does not match the pattern '{pattern.GetString()}'"));
        }
    }

    private void CheckObject(JsonElement schema, JsonElement value, string pointer, List<ValidationError> errors)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(static e => e.GetString()!))
            {
                if (!value.TryGetProperty(name, out _))
                {
                    errors.Add(new ValidationError(pointer, $"missing required key '{name}'"));
                }
            }
        }

        var hasProperties = schema.TryGetProperty("properties", out var properties);
        var hasAdditional = schema.TryGetProperty("additionalProperties", out var additional);
        foreach (var member in value.EnumerateObject())
        {
            var at = pointer + "/" + Escape(member.Name);
            if (hasProperties && properties.TryGetProperty(member.Name, out var propertySchema))
            {
                Check(propertySchema, member.Value, at, errors);
            }
            else if (hasAdditional)
            {
                if (additional.ValueKind == JsonValueKind.False)
                {
                    errors.Add(new ValidationError(at, $"unexpected key '{member.Name}'"));
                }
                else if (additional.ValueKind == JsonValueKind.Object)
                {
                    Check(additional, member.Value, at, errors);
                }
            }
        }
    }

    private void CheckArray(JsonElement schema, JsonElement value, string pointer, List<ValidationError> errors)
    {
        if (!schema.TryGetProperty("items", out var items))
        {
            return;
        }

        var index = 0;
        foreach (var element in value.EnumerateArray())
        {
            Check(items, element, pointer + "/" + index.ToString(CultureInfo.InvariantCulture), errors);
            index++;
        }
    }

    private void CheckOneOf(JsonElement alternatives, JsonElement value, string pointer, List<ValidationError> errors)
    {
        var results = new List<(string Name, JsonElement Schema, List<ValidationError> Errors)>();
        var index = 0;
        foreach (var alternative in alternatives.EnumerateArray())
        {
            var found = new List<ValidationError>();
            Check(alternative, value, pointer, found);
            var name = alternative.TryGetProperty("$ref", out var reference) ? reference.GetString()! : "oneOf/" + index.ToString(CultureInfo.InvariantCulture);
            results.Add((name, alternative, found));
            index++;
        }

        var matching = results.Where(static r => r.Errors.Count == 0).ToArray();
        if (matching.Length == 1)
        {
            return;
        }

        if (matching.Length > 1)
        {
            errors.Add(new ValidationError(pointer, $"matches {matching.Length} of {results.Count} alternatives ({string.Join(", ", matching.Select(static m => m.Name))}); exactly one is allowed"));
            return;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var shaped = results.Where(r => RequiredOf(r.Schema).All(name => value.TryGetProperty(name, out _))).ToArray();
            if (shaped.Length > 0)
            {
                var best = results.OrderBy(static r => r.Errors.Count).ThenBy(r => shaped.Contains(r) ? 0 : 1).First();
                errors.AddRange(best.Errors.Select(e => e with { Message = $"[oneOf {best.Name}] {e.Message}" }));
                return;
            }
        }

        var summary = string.Join("; ", results.Select(static r => $"{r.Name}: {r.Errors[0].Message}"));
        errors.Add(new ValidationError(pointer, $"matches none of {results.Count} alternatives ({summary})"));
    }

    // The required keys of an alternative, following its $ref.
    private IEnumerable<string> RequiredOf(JsonElement schema)
    {
        var names = new List<string>();
        if (schema.TryGetProperty("$ref", out var reference))
        {
            names.AddRange(RequiredOf(Resolve(reference.GetString()!)));
        }

        if (schema.TryGetProperty("required", out var required))
        {
            names.AddRange(required.EnumerateArray().Select(static e => e.GetString()!));
        }

        return names;
    }

    private static bool TypeMatches(JsonElement type, JsonElement value)
    {
        var names = type.ValueKind == JsonValueKind.String ? [type.GetString()!] : type.EnumerateArray().Select(static e => e.GetString()!).ToArray();
        return names.Any(name => name switch
        {
            "null" => value.ValueKind == JsonValueKind.Null,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => IsInteger(value),
            _ => false,
        });
    }

    private static bool IsInteger(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (value.TryGetInt64(out _))
        {
            return true;
        }

        var number = value.GetDouble();
        return double.IsFinite(number) && Math.Floor(number) == number;
    }

    private static string TypeNamesOf(JsonElement type)
    {
        return type.ValueKind == JsonValueKind.String ? type.GetString()! : string.Join(" or ", type.EnumerateArray().Select(static e => e.GetString()));
    }

    private static string Describe(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => "null",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.String => "string",
            _ => IsInteger(value) ? "integer" : "number",
        };
    }

    // A value as JSON text, shortened so that an error about a long value stays readable.
    private static string Raw(JsonElement value)
    {
        var text = value.GetRawText();
        return text.Length > 80 ? text[..77] + "..." : text;
    }
}
