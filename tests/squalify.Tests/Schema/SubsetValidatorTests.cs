// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;

namespace squalor.squalify.Tests.Schema;

/// <summary>
/// The tests of the JSON Schema subset validator itself, on small inline schemas. The validator implements exactly the keywords
/// that <c>schema/descriptor-v1.schema.json</c> may use, and rejects any other keyword when a schema is loaded.
/// </summary>
public class SubsetValidatorTests
{
    private static IReadOnlyList<ValidationError> Validate(string schema, string instance)
    {
        using var document = JsonDocument.Parse(instance);
        return SubsetValidator.Load(schema).Validate(document.RootElement);
    }

    private static string[] Errors(string schema, string instance)
    {
        return Validate(schema, instance).Select(static e => e.ToString()).ToArray();
    }

    [Fact]
    public void Keywords_AreExactlyTheListedSubset()
    {
        Assert.Equal(
            new[] { "$schema", "$id", "type", "required", "properties", "additionalProperties", "enum", "const", "items", "minimum", "maximum", "pattern", "$ref", "$defs", "oneOf" }.Order(StringComparer.Ordinal),
            SubsetValidator.Keywords.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("minItems")]
    [InlineData("maxItems")]
    [InlineData("description")]
    [InlineData("title")]
    [InlineData("allOf")]
    [InlineData("anyOf")]
    [InlineData("not")]
    [InlineData("format")]
    [InlineData("minLength")]
    [InlineData("prefixItems")]
    [InlineData("dependentRequired")]
    [InlineData("propertyNames")]
    [InlineData("exclusiveMinimum")]
    [InlineData("$comment")]
    [InlineData("$anchor")]
    [InlineData("default")]
    public void Load_RejectsAnyKeywordOutsideTheSubset_AndNamesItWithItsPointer(string keyword)
    {
        var schema = $$"""{ "type": "object", "properties": { "a": { "type": "array", "items": { "type": "integer", "{{keyword}}": 1 } } } }""";
        var error = Assert.Throws<SchemaLoadException>(() => SubsetValidator.Load(schema));
        Assert.Contains("/properties/a/items/" + keyword, error.Message);
        Assert.Contains("unsupported keyword", error.Message);
    }

    [Fact]
    public void Load_RejectsAnUnsupportedKeywordAtTheRoot_AndInADefinition()
    {
        var atRoot = Assert.Throws<SchemaLoadException>(() => SubsetValidator.Load("""{ "type": "object", "minProperties": 1 }"""));
        Assert.Contains("/minProperties", atRoot.Message);
        var inDef = Assert.Throws<SchemaLoadException>(() => SubsetValidator.Load("""{ "$defs": { "d": { "type": "string", "minLength": 2 } }, "$ref": "#/$defs/d" }"""));
        Assert.Contains("/$defs/d/minLength", inDef.Message);
    }

    [Fact]
    public void Load_ReportsEveryProblem_NotOnlyTheFirst()
    {
        var error = Assert.Throws<SchemaLoadException>(() => SubsetValidator.Load("""{ "properties": { "a": { "minItems": 1 }, "b": { "title": "x" } } }"""));
        Assert.Contains("/properties/a/minItems", error.Message);
        Assert.Contains("/properties/b/title", error.Message);
    }

    [Fact]
    public void Load_AcceptsAPropertyThatIsNamedLikeAKeyword()
    {
        // A key under "properties" is a name, not a keyword.
        var schema = """{ "type": "object", "required": ["minItems"], "properties": { "minItems": { "type": "integer" }, "description": { "type": "string" } }, "additionalProperties": false }""";
        Assert.Empty(Errors(schema, """{ "minItems": 1, "description": "x" }"""));
        Assert.Single(Errors(schema, """{ "minItems": "no" }"""));
    }

    [Fact]
    public void Load_AcceptsEveryListedKeyword()
    {
        var schema = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$id": "https://example.invalid/s.json",
              "$defs": { "n": { "type": "integer", "minimum": 0, "maximum": 9 } },
              "type": "object",
              "required": ["a"],
              "properties": { "a": { "$ref": "#/$defs/n" }, "b": { "type": "string", "pattern": "^x" }, "c": { "enum": [1, "two", null] }, "d": { "const": true }, "e": { "type": "array", "items": { "type": "string" } }, "f": { "oneOf": [{ "type": "string" }, { "type": "integer" }] } },
              "additionalProperties": false
            }
            """;
        Assert.Empty(Errors(schema, """{ "a": 1 }"""));
    }

    [Theory]
    [InlineData("""{ "$ref": "#/$defs/missing" }""", "#/$defs/missing")]
    [InlineData("""{ "$ref": "other.json#/x" }""", "other.json#/x")]
    [InlineData("""{ "type": "thing" }""", "thing")]
    [InlineData("""{ "type": [] }""", "type")]
    [InlineData("""{ "required": "a" }""", "required")]
    [InlineData("""{ "properties": [] }""", "properties")]
    [InlineData("""{ "additionalProperties": 1 }""", "additionalProperties")]
    [InlineData("""{ "enum": [] }""", "enum")]
    [InlineData("""{ "items": [] }""", "items")]
    [InlineData("""{ "minimum": "1" }""", "minimum")]
    [InlineData("""{ "pattern": "(" }""", "pattern")]
    [InlineData("""{ "oneOf": [] }""", "oneOf")]
    [InlineData("""{ "$schema": "http://json-schema.org/draft-07/schema#" }""", "$schema")]
    [InlineData("""{ "properties": { "a": { "$defs": {} } } }""", "$defs")]
    [InlineData("""{ "properties": { "a": { "$id": "x" } } }""", "$id")]
    [InlineData("[]", "schema")]
    public void Load_RejectsAMalformedSchema(string schema, string mentions)
    {
        var error = Assert.Throws<SchemaLoadException>(() => SubsetValidator.Load(schema));
        Assert.Contains(mentions, error.Message);
    }

    [Fact]
    public void Load_RejectsTextThatIsNotJson()
    {
        Assert.Throws<SchemaLoadException>(() => SubsetValidator.Load("{ nope"));
    }

    [Theory]
    [InlineData("string", "\"x\"", true)]
    [InlineData("string", "1", false)]
    [InlineData("integer", "3", true)]
    [InlineData("integer", "3.0", true)]
    [InlineData("integer", "1E2", true)]
    [InlineData("integer", "3.5", false)]
    [InlineData("integer", "\"3\"", false)]
    [InlineData("number", "3.5", true)]
    [InlineData("number", "3", true)]
    [InlineData("number", "\"3\"", false)]
    [InlineData("boolean", "true", true)]
    [InlineData("boolean", "0", false)]
    [InlineData("null", "null", true)]
    [InlineData("null", "false", false)]
    [InlineData("object", "{}", true)]
    [InlineData("object", "[]", false)]
    [InlineData("array", "[]", true)]
    [InlineData("array", "{}", false)]
    public void Type_ChecksTheJsonKind(string type, string instance, bool valid)
    {
        var errors = Errors($$"""{ "type": "{{type}}" }""", instance);
        Assert.Equal(valid, errors.Length == 0);
        if (!valid)
        {
            Assert.Contains("expected " + type, errors[0]);
        }
    }

    [Fact]
    public void Type_AsAnArray_AcceptsAnyOfTheListedKinds()
    {
        const string schema = """{ "type": ["object", "null"] }""";
        Assert.Empty(Errors(schema, "null"));
        Assert.Empty(Errors(schema, "{}"));
        var errors = Errors(schema, "[]");
        Assert.Single(errors);
        Assert.Contains("expected object or null", errors[0]);
        Assert.Contains("found array", errors[0]);
    }

    [Fact]
    public void Enum_AcceptsOnlyTheListedValues_AndComparesByValue()
    {
        const string schema = """{ "enum": ["a", 1, null, true, [1, 2], { "k": 1, "j": [2] }] }""";
        foreach (var good in new[] { "\"a\"", "1", "1.0", "null", "true", "[1,2]", """{ "j": [2], "k": 1 }""" })
        {
            Assert.Empty(Errors(schema, good));
        }

        foreach (var bad in new[] { "\"b\"", "2", "false", "[2,1]", """{ "k": 1 }""", "\"1\"" })
        {
            var errors = Errors(schema, bad);
            Assert.Single(errors);
            Assert.Contains("is not one of", errors[0]);
        }
    }

    [Fact]
    public void Const_AcceptsOnlyThatValue()
    {
        Assert.Empty(Errors("""{ "const": "1.0" }""", "\"1.0\""));
        Assert.Empty(Errors("""{ "const": true }""", "true"));
        Assert.Empty(Errors("""{ "const": 64 }""", "64.0"));
        var errors = Errors("""{ "const": "1.0" }""", "\"1.1\"");
        Assert.Equal(["(root): expected the constant \"1.0\", found \"1.1\""], errors);
        Assert.Single(Errors("""{ "const": true }""", "false"));
        Assert.Single(Errors("""{ "const": true }""", "1"));
    }

    [Fact]
    public void Minimum_AndMaximum_AreInclusive_AndApplyToNumbersOnly()
    {
        const string schema = """{ "type": "integer", "minimum": 1, "maximum": 3 }""";
        Assert.Empty(Errors(schema, "1"));
        Assert.Empty(Errors(schema, "3"));
        Assert.Equal(["(root): 0 is below the minimum 1"], Errors(schema, "0"));
        Assert.Equal(["(root): 4 is above the maximum 3"], Errors(schema, "4"));

        // A string is a type error, and the bounds say nothing about it.
        var errors = Errors(schema, "\"9\"");
        Assert.Single(errors);
        Assert.Contains("expected integer", errors[0]);
    }

    [Fact]
    public void Pattern_IsASearch_NotAFullMatch_AndAppliesToStringsOnly()
    {
        const string schema = """{ "type": ["string", "null"], "pattern": "^sha256:[0-9a-f]{4}$" }""";
        Assert.Empty(Errors(schema, "\"sha256:00ff\""));
        Assert.Empty(Errors(schema, "null"));
        Assert.Single(Errors(schema, "\"sha256:00fg\""));
        Assert.Single(Errors(schema, "\"sha256:00ff0\""));
        Assert.Empty(Errors("""{ "pattern": "b" }""", "\"abc\""));
        Assert.Empty(Errors("""{ "pattern": "b" }""", "5"));
        var errors = Errors(schema, "\"zzzz\"");
        Assert.Contains("does not match the pattern", errors[0]);
    }

    [Fact]
    public void Pattern_DollarDoesNotMatchBeforeATrailingNewline()
    {
        // In JSON Schema (ECMA-262) $ is the end of the input; in .NET it also matches before a final line feed.
        const string schema = """{ "type": "string", "pattern": "^[0-9a-f]{2}$" }""";
        Assert.Empty(Errors(schema, "\"ab\""));
        Assert.Single(Errors(schema, "\"ab\\n\""));
        Assert.Single(Errors("""{ "pattern": "^(NaN|Infinity)$" }""", "\"NaN\\n\""));
        Assert.Single(Errors("""{ "pattern": "^a\\.b([-+].+)?$" }""", "\"a.b\\n\""));

        // An escaped dollar at the end is a literal dollar, and a dollar inside a character class is a literal too.
        Assert.Empty(Errors("""{ "pattern": "^a\\$$" }""", "\"a$\""));
        Assert.Single(Errors("""{ "pattern": "^a\\$$" }""", "\"a$\\n\""));
        Assert.Empty(Errors("""{ "pattern": "^[$]$" }""", "\"$\""));
    }

    [Fact]
    public void Load_RejectsADollarThatIsNotTheEndOfThePattern()
    {
        // The validator rewrites a final $ to the end of the input; a $ anywhere else would keep the .NET meaning, so it is refused.
        var error = Assert.Throws<SchemaLoadException>(() => SubsetValidator.Load("""{ "properties": { "a": { "pattern": "a$|b" } } }"""));
        Assert.Contains("/properties/a/pattern", error.Message);
        Assert.Contains("'$'", error.Message);
        _ = SubsetValidator.Load("""{ "pattern": "^[$]a\\$" }""");
    }

    [Fact]
    public void Required_ReportsEachMissingKey_AtTheObjectPointer_InSchemaOrder()
    {
        const string schema = """{ "type": "object", "required": ["b", "a", "c"], "properties": { "a": {}, "b": {}, "c": {} } }""";
        Assert.Equal(
            ["(root): missing required key 'b'", "(root): missing required key 'c'"],
            Errors(schema, """{ "a": 1 }"""));
        Assert.Empty(Errors(schema, """{ "a": 1, "b": 2, "c": 3 }"""));
        Assert.Empty(Errors("""{ "required": ["a"] }""", "[]"));
    }

    [Fact]
    public void AdditionalProperties_False_RejectsUnknownKeys_AtThePointerOfTheKey()
    {
        const string schema = """{ "type": "object", "properties": { "a": { "type": "object", "properties": { "x": {} }, "additionalProperties": false } }, "additionalProperties": false }""";
        Assert.Equal(
            ["/a/y: unexpected key 'y'", "/z: unexpected key 'z'"],
            Errors(schema, """{ "a": { "x": 1, "y": 2 }, "z": 3 }"""));
    }

    [Fact]
    public void AdditionalProperties_AsASchema_ChecksEveryKeyThatIsNotListed()
    {
        const string schema = """{ "type": "object", "properties": { "known": { "type": "string" } }, "additionalProperties": { "type": "integer", "minimum": 1 } }""";
        Assert.Empty(Errors(schema, """{ "known": "s", "a": 1, "b": 7 }"""));
        Assert.Equal(
            ["/a: 0 is below the minimum 1", "/b: expected integer, found string"],
            Errors(schema, """{ "a": 0, "b": "x", "known": "s" }"""));
        Assert.Empty(Errors("""{ "type": "object", "additionalProperties": true }""", """{ "anything": [1] }"""));
    }

    [Fact]
    public void Items_ChecksEveryElement_AtItsIndex()
    {
        const string schema = """{ "type": "array", "items": { "type": "integer" } }""";
        Assert.Empty(Errors(schema, "[]"));
        Assert.Empty(Errors(schema, "[1,2,3]"));
        Assert.Equal(
            ["/1: expected integer, found string", "/3: expected integer, found null"],
            Errors(schema, """[1, "x", 3, null]"""));
    }

    [Fact]
    public void Ref_FollowsALocalDefinition_AlsoNextToOtherKeywords()
    {
        const string schema = """
            { "$defs": { "count": { "type": "integer", "minimum": 0 } },
              "type": "object",
              "properties": { "n": { "$ref": "#/$defs/count" }, "m": { "$ref": "#/$defs/count", "maximum": 5 } } }
            """;
        Assert.Empty(Errors(schema, """{ "n": 3, "m": 5 }"""));
        Assert.Equal(
            ["/n: -1 is below the minimum 0", "/m: 6 is above the maximum 5"],
            Errors(schema, """{ "n": -1, "m": 6 }"""));
    }

    [Fact]
    public void Errors_AreAllReported_InDocumentOrder()
    {
        const string schema = """
            { "type": "object", "required": ["id"],
              "properties": { "id": { "type": "integer" }, "tags": { "type": "array", "items": { "type": "string" } }, "inner": { "type": "object", "required": ["k"], "properties": { "k": { "enum": ["x"] } }, "additionalProperties": false } },
              "additionalProperties": false }
            """;
        Assert.Equal(
            [
                "(root): missing required key 'id'",
                "/tags/0: expected string, found integer",
                "/tags/2: expected string, found boolean",
                "/inner: missing required key 'k'",
                "/inner/extra: unexpected key 'extra'",
                "/top: unexpected key 'top'",
            ],
            Errors(schema, """{ "tags": [1, "ok", true], "inner": { "extra": 1 }, "top": 1 }"""));
    }

    [Fact]
    public void Pointer_EscapesTildeAndSlashInKeys()
    {
        const string schema = """{ "type": "object", "properties": { "a/b": { "type": "integer" }, "c~d": { "type": "integer" } }, "additionalProperties": false }""";
        Assert.Equal(
            ["/a~1b: expected integer, found string", "/c~0d: expected integer, found string", "/e~1~0f: unexpected key 'e/~f'"],
            Errors(schema, """{ "a/b": "x", "c~d": "y", "e/~f": 1 }"""));
    }

    [Fact]
    public void Validate_ReturnsStructuredErrors_WithPointerAndMessage()
    {
        var error = Assert.Single(Validate("""{ "properties": { "a": { "type": "string" } } }""", """{ "a": 1 }"""));
        Assert.Equal("/a", error.Pointer);
        Assert.Equal("expected string, found integer", error.Message);
    }

    private const string TwoShapes = """
        {
          "$defs": {
            "full": { "type": "object", "required": ["id", "type", "count"], "properties": { "id": { "type": "integer" }, "type": { "enum": ["int", "float"] }, "count": { "type": "integer" } }, "additionalProperties": false },
            "bare": { "type": "object", "required": ["id"], "properties": { "id": { "type": "integer" } }, "additionalProperties": false }
          },
          "type": "array",
          "items": { "oneOf": [{ "$ref": "#/$defs/full" }, { "$ref": "#/$defs/bare" }] }
        }
        """;

    [Fact]
    public void OneOf_AcceptsAnInstanceThatMatchesExactlyOneAlternative()
    {
        Assert.Empty(Errors(TwoShapes, """[{ "id": 1, "type": "int", "count": 3 }, { "id": 2 }]"""));
    }

    [Fact]
    public void OneOf_ReportsTheErrorsOfTheAlternativeWhoseRequiredKeysAreAllThere_AndNamesIt()
    {
        // {id, type, count} has the required keys of "full": the errors are those of "full", not "matches none of 2".
        var errors = Errors(TwoShapes, """[{ "id": 1, "type": "string", "count": 3 }]""");
        Assert.Single(errors);
        Assert.Equal("/0/type: [oneOf #/$defs/full] \"string\" is not one of: \"int\", \"float\"", errors[0]);
        Assert.DoesNotContain("matches none", errors[0]);

        // {id, x} has only the required key of "bare": the unexpected key is reported against "bare".
        var bare = Errors(TwoShapes, """[{ "id": 1, "x": 2 }]""");
        Assert.Equal(["/0/x: [oneOf #/$defs/bare] unexpected key 'x'"], bare);
    }

    [Fact]
    public void OneOf_ReportsTheAlternativeWithTheFewestErrors_NotOnlyTheOneWhoseRequiredKeysArePresent()
    {
        // The instance has everything "bare" requires and lacks "count" of "full": the one missing key of "full" is a smaller
        // problem than the four keys that "bare" would not accept.
        var schema = TwoShapes.Replace("\"properties\": { \"id\": { \"type\": \"integer\" }, \"type\"", "\"properties\": { \"id\": { \"type\": \"integer\" }, \"a\": {}, \"b\": {}, \"c\": {}, \"type\"", StringComparison.Ordinal);
        var errors = Errors(schema, """[{ "id": 1, "type": "int", "a": 1, "b": 2, "c": 3 }]""");
        Assert.Equal(["/0: [oneOf #/$defs/full] missing required key 'count'"], errors);
    }

    [Fact]
    public void OneOf_WhenNoAlternativeHasItsRequiredKeys_ReportsThatNoneMatches()
    {
        var errors = Errors(TwoShapes, """[{ "name": "x" }]""");
        Assert.Single(errors);
        Assert.StartsWith("/0: matches none of 2 alternatives", errors[0]);
        Assert.Contains("#/$defs/full", errors[0]);
        Assert.Contains("#/$defs/bare", errors[0]);
    }

    [Fact]
    public void OneOf_WhenMoreThanOneAlternativeMatches_IsAnError()
    {
        const string schema = """{ "oneOf": [{ "type": "integer" }, { "type": "number" }] }""";
        var errors = Errors(schema, "3");
        Assert.Single(errors);
        Assert.Contains("matches 2 of 2 alternatives", errors[0]);
        Assert.Empty(Errors(schema, "3.5"));
    }

    [Fact]
    public void OneOf_WithAnInstanceThatIsNotAnObject_ReportsThatNoneMatches()
    {
        const string schema = """{ "oneOf": [{ "type": "string" }, { "type": "integer" }] }""";
        var errors = Errors(schema, "true");
        Assert.Single(errors);
        Assert.StartsWith("(root): matches none of 2 alternatives", errors[0]);
        Assert.Contains("oneOf/0", errors[0]);
        Assert.Contains("oneOf/1", errors[0]);
    }
}
