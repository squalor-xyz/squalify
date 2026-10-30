// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Nodes;

namespace squalor.squalify.Tests.Schema;

/// <summary>
/// sq-12: the published descriptor schema, <c>schema/descriptor-v1.schema.json</c>, and the checks that keep it equal to what
/// squalify writes. The schema is closed (<c>additionalProperties: false</c>, closed enums) and describes what 1.0.0 produces.
/// </summary>
public class SchemaTests
{
    private static readonly string[] ListedKeywords =
        ["$schema", "$id", "type", "required", "properties", "additionalProperties", "enum", "const", "items", "minimum", "maximum", "pattern", "$ref", "$defs", "oneOf"];

    /// <summary>A golden of a full profile of a table, as editable JSON.</summary>
    internal static JsonNode GoldenNode(string fixtureName = "delimited/semiconductor-sweep.csv")
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", fixtureName.Replace('/', Path.DirectorySeparatorChar) + ".squalify.json");
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    internal static IReadOnlyList<ValidationError> Validate(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return SchemaFiles.Validator.Validate(document.RootElement);
    }

    [Fact]
    public void Schema_UsesOnlySupportedKeywords()
    {
        using var document = JsonDocument.Parse(SchemaFiles.ReadSchema());

        // The keywords the schema uses, found by walking its structure (a key under "properties" or "$defs" is a name).
        var used = SchemaWalker.Keywords(document.RootElement);
        Assert.NotEmpty(used);
        Assert.Empty(used.Except(ListedKeywords, StringComparer.Ordinal));

        // The validator accepts the schema, and its own subset is the listed one.
        Assert.Equal(ListedKeywords.Order(StringComparer.Ordinal), SubsetValidator.Keywords.Order(StringComparer.Ordinal));
        _ = SubsetValidator.Load(SchemaFiles.ReadSchema());

        // A keyword outside the subset, anywhere in the schema, makes the load fail with its pointer.
        var tampered = JsonNode.Parse(SchemaFiles.ReadSchema())!.AsObject();
        tampered["properties"]!["tool"]!.AsObject()["minProperties"] = 1;
        var error = Assert.Throws<SchemaLoadException>(() => SubsetValidator.Load(tampered.ToJsonString()));
        Assert.Contains("/properties/tool/minProperties", error.Message);

        var atRoot = JsonNode.Parse(SchemaFiles.ReadSchema())!.AsObject();
        atRoot["description"] = "not in the subset";
        Assert.Contains("/description", Assert.Throws<SchemaLoadException>(() => SubsetValidator.Load(atRoot.ToJsonString())).Message);
    }

    [Fact]
    public void Validator_RejectsKnownBadDescriptors()
    {
        // The unchanged golden is valid, so each error below comes from the one change made to it.
        Assert.Empty(Validate(GoldenNode()));

        // The descriptor is an object.
        foreach (var notAnObject in new[] { "null", "[]", "\"x\"", "1", "true" })
        {
            using var rootDocument = JsonDocument.Parse(notAnObject);
            var rootErrors = SchemaFiles.Validator.Validate(rootDocument.RootElement);
            Assert.Equal(string.Empty, Assert.Single(rootErrors).Pointer);
        }

        // A missing required key.
        var missing = GoldenNode();
        missing["limits"]!.AsObject().Remove("sketches");
        Assert.Equal(["/limits: missing required key 'sketches'"], Validate(missing).Select(static e => e.ToString()));

        var missingRoot = GoldenNode();
        missingRoot.AsObject().Remove("fingerprint");
        Assert.Equal(["(root): missing required key 'fingerprint'"], Validate(missingRoot).Select(static e => e.ToString()));

        // A wrong type.
        var wrongType = GoldenNode();
        wrongType["table"]!["rows"] = "12";
        Assert.Equal(["/table/rows: expected integer, found string"], Validate(wrongType).Select(static e => e.ToString()));

        var wrongNested = GoldenNode();
        wrongNested["table"]!["fields"]![0]!["count"] = 1.5;
        var nested = Validate(wrongNested);
        Assert.Contains(nested, static e => e.Pointer == "/table/fields/0/count" && e.Message == "[oneOf #/$defs/field] expected integer, found number");

        // An unknown enum value.
        var family = GoldenNode();
        family["format"]!["family"] = "parquet";
        var familyErrors = Validate(family);
        Assert.Single(familyErrors);
        Assert.Equal("/format/family", familyErrors[0].Pointer);
        Assert.Contains("\"parquet\" is not one of", familyErrors[0].Message);

        var stage = GoldenNode();
        stage["evidence"]![0]!["stage"] = "mystery";
        Assert.Equal("/evidence/0/stage", Assert.Single(Validate(stage)).Pointer);

        var code = GoldenNode("delimited/ragged-first-rows.csv");
        code["issues"]![0]!["code"] = "mysteryIssue";
        var codeError = Assert.Single(Validate(code));
        Assert.Equal("/issues/0/code", codeError.Pointer);
        Assert.Contains("\"mysteryIssue\" is not one of", codeError.Message);

        // A pattern ends at the end of the input: a trailing line feed is not accepted (in .NET a bare $ would accept it).
        var hashNewline = GoldenNode();
        hashNewline["file"]!["sha256"] = new string('a', 64) + "\n";
        Assert.Equal("/file/sha256", Assert.Single(Validate(hashNewline)).Pointer);
        var fingerprintNewline = GoldenNode();
        fingerprintNewline["fingerprint"]!["layout"] = "sha256:" + new string('a', 64) + "\n";
        Assert.Equal("/fingerprint/layout", Assert.Single(Validate(fingerprintNewline)).Pointer);
        var idNewline = GoldenNode();
        idNewline["table"]!["rowClasses"]!["classes"]![0]!["id"] = "rc:0123456789abcdef\n";
        Assert.Equal("/table/rowClasses/classes/0/id", Assert.Single(Validate(idNewline)).Pointer);
        var versionNewline = GoldenNode();
        versionNewline["tool"]!["version"] = "1.2.3\n";
        Assert.Equal("/tool/version", Assert.Single(Validate(versionNewline)).Pointer);

        // An extra key: the schema is closed.
        var extra = GoldenNode();
        extra["tool"]!["vendor"] = "x";
        Assert.Equal(["/tool/vendor: unexpected key 'vendor'"], Validate(extra).Select(static e => e.ToString()));

        var extraRoot = GoldenNode();
        extraRoot["sections"] = new JsonArray();
        Assert.Equal(["/sections: unexpected key 'sections'"], Validate(extraRoot).Select(static e => e.ToString()));

        var extraField = GoldenNode();
        extraField["table"]!["fields"]![0]!["surprise"] = true;
        var fieldErrors = Validate(extraField);
        Assert.Equal("/table/fields/0/surprise", Assert.Single(fieldErrors).Pointer);
        Assert.Equal("[oneOf #/$defs/field] unexpected key 'surprise'", fieldErrors[0].Message);
    }

    // Every golden, and the writer model

    private const string StdfModelGolden = "full-binary-stdf.squalify.json";
    private const string DelimitedModel = "model:FullDelimited";

    public static string[] GoldenNames()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var names = new List<string>();
        foreach (var path in Directory.GetFiles(root, "*.squalify.json", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Order(StringComparer.Ordinal))
        {
            // The STDF writer model is a key-order pin only: the schema covers what 1.0.0 produces (owner decision S12-5, S12-6).
            if (path != StdfModelGolden)
            {
                names.Add(path);
            }
        }

        names.Add(DelimitedModel);
        return names.ToArray();
    }

    public static TheoryData<string> GoldenCases()
    {
        var data = new TheoryData<string>();
        foreach (var name in GoldenNames())
        {
            data.Add(name);
        }

        return data;
    }

    private static byte[] GoldenBytes(string golden)
    {
        return golden == DelimitedModel
            ? DescriptorWriter.ToBytes(TestModels.FullDelimited())
            : File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", golden.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void AllGoldens_ValidateAgainstSchema(string golden)
    {
        using var document = JsonDocument.Parse(GoldenBytes(golden));
        Assert.Empty(SchemaFiles.Validator.Validate(document.RootElement).Select(static e => e.ToString()));
        Assert.Empty(DescriptorInvariants.Check(document.RootElement));
    }

    [Fact]
    public void AllGoldens_ValidateAgainstSchema_CoversEveryFamilyTheProfilerWrites()
    {
        var families = GoldenNames()
            .Select(static golden =>
            {
                using var document = JsonDocument.Parse(GoldenBytes(golden));
                return document.RootElement.GetProperty("format").GetProperty("family").GetString()!;
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["binary", "delimited", "text"], families);
    }

    [Fact]
    public void AllGoldens_TheStdfWriterModel_IsOnlyAKeyOrderPin()
    {
        // Its members (stdf, sections, binary.blocks and segments, an STDF dialect) are planned for a 1.x minor release, so the closed
        // schema rejects them. The model stays in the repository as a key-order pin (Writer_KeyOrder_MatchesSchemaDoc).
        using var document = JsonDocument.Parse(GoldenBytes(StdfModelGolden));
        var errors = SchemaFiles.Validator.Validate(document.RootElement);
        Assert.Contains(errors, static e => e.Pointer == "/stdf");
        Assert.Contains(errors, static e => e.Pointer == "/sections");
        Assert.DoesNotContain(StdfModelGolden, GoldenNames());
    }

    // Test 5

    [Fact]
    public void Descriptor_SchemaVersion_MatchesTheSchema()
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var published = schema.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString();

        var descriptor = Profiler.Profile(TestBytes.Fixture("text/plain.txt"), ProfileOptions.Create(false, []));
        using var written = JsonDocument.Parse(DescriptorWriter.ToBytes(descriptor));
        Assert.Equal(published, written.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("1.0", Descriptor.SchemaVersion);
        Assert.Equal(Descriptor.SchemaVersion, published);
    }

    // Test 6 and 13: the closed enums equal what the code can produce

    private static JsonElement Definition(JsonDocument schema, string name)
    {
        return schema.RootElement.GetProperty("$defs").GetProperty(name);
    }

    private static string[] EnumOf(JsonElement node)
    {
        return node.GetProperty("enum").EnumerateArray().Select(static e => e.GetString()!).ToArray();
    }

    [Fact]
    public void Schema_ClosedEnumsMatchTheCode()
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());

        // No code the profiler can emit is missing from the schema, and none is listed that it cannot emit.
        Assert.Equal(CodeVocabulary.IssueCodes(), EnumOf(Definition(schema, "issueCode")).Order(StringComparer.Ordinal));
        Assert.Equal(CodeVocabulary.EvidenceStages(), EnumOf(Definition(schema, "evidenceStage")).Order(StringComparer.Ordinal));
        var family = schema.RootElement.GetProperty("properties").GetProperty("format").GetProperty("properties").GetProperty("family");
        Assert.Equal(CodeVocabulary.Families(), EnumOf(family).Order(StringComparer.Ordinal));

        // The enums are the ones the descriptor members use.
        var members = schema.RootElement.GetProperty("properties");
        Assert.Equal("#/$defs/issueCode", members.GetProperty("issues").GetProperty("items").GetProperty("properties").GetProperty("code").GetProperty("$ref").GetString());
        Assert.Equal("#/$defs/evidenceStage", members.GetProperty("evidence").GetProperty("items").GetProperty("properties").GetProperty("stage").GetProperty("$ref").GetString());
    }

    [Fact]
    public void Schema_ClosedEnums_RejectACodeStageOrFamilyTheCodeCannotProduce()
    {
        var node = GoldenNode("delimited/ragged-first-rows.csv");
        foreach (var code in CodeVocabulary.IssueCodes())
        {
            node["issues"]![0]!["code"] = code;
            Assert.Empty(Validate(node));
        }

        node["issues"]![0]!["code"] = "tooRagged";
        Assert.Single(Validate(node));
    }

    [Fact]
    public void Schema_RegionsEnum_MatchesTheCode()
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var names = CodeVocabulary.RegionNames();

        // The eight names of issues[].regions: the seven region kinds that hold bytes, and "other" for a byte that no region holds.
        Assert.Equal(8, names.Count);
        Assert.Equal(names, EnumOf(Definition(schema, "issueRegion")).Order(StringComparer.Ordinal));
        var issues = schema.RootElement.GetProperty("properties").GetProperty("issues").GetProperty("items").GetProperty("properties");
        Assert.Equal("#/$defs/issueRegion", issues.GetProperty("regions").GetProperty("items").GetProperty("$ref").GetString());

        // layout.regions[].kind is the same set plus "block", which holds regions and is never the name of a byte's region.
        var kinds = EnumOf(Definition(schema, "regionKind")).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(CodeVocabulary.RegionKinds(), kinds);
        Assert.Equal(names.Where(static n => n != "other").Append("block").Order(StringComparer.Ordinal), kinds);
        Assert.DoesNotContain("block", names);
        Assert.Equal(EnumOf(Definition(schema, "childRegionKind")).Order(StringComparer.Ordinal), kinds.Where(static k => k != "block"));
    }

    [Fact]
    public void Schema_RegionsEnum_ValidatesEachNameAndRejectsOthers()
    {
        var node = GoldenNode("delimited/ragged-first-rows.csv");
        var issue = node["issues"]![0]!.AsObject();
        issue["offsets"] = new JsonArray(1, 2);
        issue["firstLines"] = new JsonArray(1, 2);
        foreach (var name in CodeVocabulary.RegionNames())
        {
            issue["regions"] = new JsonArray(name, name);
            Assert.Empty(Validate(node));
        }

        issue["regions"] = new JsonArray("block", "data");
        var error = Assert.Single(Validate(node));
        Assert.Equal("/issues/0/regions/0", error.Pointer);
    }

    // Test 7

    [Fact]
    public void Schema_Tests_DoNotNeedTheSourceTree()
    {
        // The schema and the document are read from the copy in the test output.
        Assert.Equal(AppContext.BaseDirectory, SchemaFiles.Root);
        Assert.True(File.Exists(Path.Combine(SchemaFiles.Root, "schema", "descriptor-v1.schema.json")));
        Assert.True(File.Exists(Path.Combine(SchemaFiles.Root, "docs", "descriptor.md")));

        // Without the copies the read fails and names the file; it does not fall back to the source tree.
        var empty = Path.Combine(Path.GetTempPath(), "squalify-no-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var schemaError = Assert.Throws<FileNotFoundException>(() => SchemaFiles.ReadSchema(empty));
            Assert.Contains("schema/descriptor-v1.schema.json", schemaError.Message);
            var docError = Assert.Throws<FileNotFoundException>(() => SchemaFiles.ReadDoc(empty));
            Assert.Contains("docs/descriptor.md", docError.Message);

            // Two copies in a directory of their own are all that the tests need: what is read is what is there.
            Directory.CreateDirectory(Path.Combine(empty, "schema"));
            Directory.CreateDirectory(Path.Combine(empty, "docs"));
            File.WriteAllText(Path.Combine(empty, "schema", "descriptor-v1.schema.json"), SchemaFiles.ReadSchema().Replace("\"const\": \"1.0\"", "\"const\": \"9.9\"", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(empty, "docs", "descriptor.md"), "# a copy\n");
            var copy = SubsetValidator.Load(SchemaFiles.ReadSchema(empty));
            using var document = JsonDocument.Parse(GoldenBytes("delimited/semiconductor-sweep.csv.squalify.json"));
            Assert.Contains(copy.Validate(document.RootElement), static e => e.Pointer == "/schemaVersion");
            Assert.Equal("# a copy\n", SchemaFiles.ReadDoc(empty));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    // Tests 10 and 11: the layout-only shape

    private const string LayoutOnlyGolden = "lab/sweep-preamble.layout-only";

    [Fact]
    public void Goldens_IncludeALayoutOnlyDescriptor()
    {
        var descriptor = Profiler.Profile(TestBytes.Fixture("lab/sweep-preamble.csv"), ProfileOptions.Create(false, [], layoutOnly: true));
        Golden.Verify(LayoutOnlyGolden, descriptor);

        var cases = GoldenNames().Where(static g => g.Contains("layout-only", StringComparison.Ordinal)).ToArray();
        Assert.Equal([LayoutOnlyGolden + ".squalify.json"], cases);
        using var document = JsonDocument.Parse(GoldenBytes(cases[0]));
        var root = document.RootElement;
        Assert.True(root.GetProperty("layoutOnly").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("hints").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("fingerprint").GetProperty("typed").ValueKind);
        Assert.NotEmpty(root.GetProperty("table").GetProperty("fields").EnumerateArray());
        Assert.Empty(SchemaFiles.Validator.Validate(root));
    }

    [Fact]
    public void Schema_LayoutOnlyFields_UseTheirOwnShape()
    {
        var layout = DescriptorInvariantsTests.Profile("lab/sweep-preamble.csv", layoutOnly: true);
        var full = DescriptorInvariantsTests.Profile("lab/sweep-preamble.csv");
        Assert.Empty(Validate(layout));
        Assert.Empty(DescriptorInvariantsTests.Check(layout));
        Assert.Empty(Validate(full));

        // A layout-only field with one more member than its shape allows: the error names the layout-only alternative.
        var extra = DescriptorInvariantsTests.Profile("lab/sweep-preamble.csv", layoutOnly: true);
        extra["table"]!["fields"]![1]!["type"] = "int";
        var errors = Validate(extra);
        var error = Assert.Single(errors);
        Assert.Equal("/table/fields/1/type", error.Pointer);
        Assert.Equal("[oneOf #/$defs/layoutOnlyField] unexpected key 'type'", error.Message);
        Assert.DoesNotContain("matches none", error.Message);

        // A full field that lacks one member: the error names the full alternative.
        var lacking = DescriptorInvariantsTests.Profile("lab/sweep-preamble.csv");
        lacking["table"]!["fields"]![1]!.AsObject().Remove("count");
        var missing = Assert.Single(Validate(lacking));
        Assert.Equal("/table/fields/1", missing.Pointer);
        Assert.Equal("[oneOf #/$defs/field] missing required key 'count'", missing.Message);

        // Neither shape: the error says so and lists both.
        var neither = DescriptorInvariantsTests.Profile("lab/sweep-preamble.csv", layoutOnly: true);
        neither["table"]!["fields"]![0]!.AsObject().Remove("index");
        neither["table"]!["fields"]![0]!.AsObject().Remove("name");
        var none = Assert.Single(Validate(neither), static e => e.Pointer == "/table/fields/0");
        Assert.Contains("matches none of 2 alternatives", none.Message);

        // A full descriptor whose field is bare, and a layout-only descriptor whose field is full: each field matches one of the two
        // shapes, so the schema alone accepts them. The correlation rule of the invariants rejects them (test 12).
        var bareInFull = DescriptorInvariantsTests.Profile("lab/sweep-preamble.csv");
        var fields = bareInFull["table"]!["fields"]!.AsArray();
        fields[0] = JsonNode.Parse(layout["table"]!["fields"]![0]!.ToJsonString());
        Assert.Empty(Validate(bareInFull));
        Assert.NotEmpty(DescriptorInvariantsTests.Check(bareInFull));

        var fullInLayout = DescriptorInvariantsTests.Profile("lab/sweep-preamble.csv", layoutOnly: true);
        fullInLayout["table"]!["fields"]!.AsArray()[0] = JsonNode.Parse(full["table"]!["fields"]![0]!.ToJsonString());
        Assert.Empty(Validate(fullInLayout));
        Assert.NotEmpty(DescriptorInvariantsTests.Check(fullInLayout));
    }

    // Test 15

    [Fact]
    public void Schema_NullSpellingRules_AreNotPinned()
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());

        // The bounds are published in limits.nullSpellings and a consumer reads them there: the schema fixes no maximum and no
        // constant for them (nor for the counts that depend on them), so a later minor release can change a bound.
        var pinned = new List<string>();
        var limits = schema.RootElement.GetProperty("properties").GetProperty("limits").GetProperty("properties").GetProperty("nullSpellings");
        var field = Definition(schema, "field").GetProperty("properties");
        var table = schema.RootElement.GetProperty("properties").GetProperty("table").GetProperty("properties").GetProperty("rowClasses").GetProperty("properties");
        var checkedNodes = new List<(string Name, JsonElement Node)> { ("limits.nullSpellings", limits) };
        checkedNodes.AddRange(new[] { "nullSpellings", "nullSpellingsOther", "nullSpellingsOtherDistinct", "nullSpellingsOtherDistinctCapped" }.Select(n => ("fields[]." + n, field.GetProperty(n))));
        checkedNodes.AddRange(new[] { "allNullRowSpellings", "allNullRowSpellingsMixed", "allNullRowSpellingsOther", "allNullRowSpellingsOtherDistinct", "allNullRowSpellingsOtherDistinctCapped" }.Select(n => ("rowClasses." + n, table.GetProperty(n))));
        foreach (var (name, node) in checkedNodes)
        {
            Pins(node, schema.RootElement, name, pinned);
        }

        Assert.Empty(pinned);

        // The marker is the constant true and nothing else; the counts have a minimum only.
        Assert.Equal("true", field.GetProperty("nullSpellingsOtherDistinctCapped").GetProperty("const").GetRawText());
        Assert.Equal("true", table.GetProperty("allNullRowSpellingsOtherDistinctCapped").GetProperty("const").GetRawText());
        Assert.Equal("#/$defs/positive", field.GetProperty("nullSpellingsOtherDistinct").GetProperty("$ref").GetString());
        Assert.Equal("#/$defs/count", table.GetProperty("allNullRowSpellingsOtherDistinct").GetProperty("$ref").GetString());
    }

    private static void Pins(JsonElement node, JsonElement root, string name, List<string> pinned)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var member in node.EnumerateObject())
        {
            switch (member.Name)
            {
                case "maximum":
                    pinned.Add($"{name}: maximum {member.Value.GetRawText()}");
                    break;
                case "const" when member.Value.ValueKind == JsonValueKind.Number:
                case "enum" when member.Value.EnumerateArray().Any(static e => e.ValueKind == JsonValueKind.Number):
                    pinned.Add($"{name}: {member.Name} {member.Value.GetRawText()}");
                    break;
                case "$ref":
                    Pins(root.GetProperty("$defs").GetProperty(member.Value.GetString()!["#/$defs/".Length..]), root, name, pinned);
                    break;
                case "properties":
                    foreach (var property in member.Value.EnumerateObject())
                    {
                        Pins(property.Value, root, name + "." + property.Name, pinned);
                    }

                    break;
                case "items" or "additionalProperties":
                    Pins(member.Value, root, name + "[]", pinned);
                    break;
            }
        }
    }

    [Fact]
    public void Schema_NullSpellingRules_AreCheckedByTheInvariantsAndStatedInTheDocument()
    {
        // The model has a field that spills, one of them capped, and a table whose all-null rows spill and are capped.
        var model = DescriptorInvariantsTests.ModelNode();
        var fields = model["table"]!["fields"]!.AsArray();
        Assert.Contains(fields, static f => f!["nullSpellingsOtherDistinctCapped"] is not null);
        Assert.Contains(fields, static f => f!["nullSpellingsOther"] is not null && f["nullSpellingsOtherDistinctCapped"] is null);
        Assert.NotNull(model["table"]!["rowClasses"]!["allNullRowSpellingsOtherDistinctCapped"]);
        Assert.Empty(DescriptorInvariantsTests.Check(model));

        string[] After(Action<JsonNode> mutate)
        {
            var copy = DescriptorInvariantsTests.ModelNode();
            mutate(copy);
            return DescriptorInvariantsTests.Check(copy);
        }

        // Distinct is present exactly when Other is.
        Assert.Contains(After(static d => d["table"]!["fields"]![2]!.AsObject().Remove("nullSpellingsOtherDistinct")), static v => v.Contains("/table/fields/2: nullSpellingsOther is present but nullSpellingsOtherDistinct is not", StringComparison.Ordinal));
        Assert.Contains(After(static d => d["table"]!["fields"]![2]!.AsObject().Remove("nullSpellingsOther")), static v => v.Contains("/table/fields/2: nullSpellingsOtherDistinct is present but nullSpellingsOther is not", StringComparison.Ordinal));

        // Distinct <= Other.
        Assert.Contains(After(static d => d["table"]!["fields"]![2]!["nullSpellingsOtherDistinct"] = 71), static v => v.Contains("/table/fields/2: nullSpellingsOtherDistinct (71) is more than nullSpellingsOther (70)", StringComparison.Ordinal));

        // Capped implies Distinct == maxDistinctOther, and needs the count.
        Assert.Contains(After(static d => d["table"]!["fields"]![2]!["nullSpellingsOtherDistinctCapped"] = true), static v => v.Contains("/table/fields/2: nullSpellingsOtherDistinctCapped is true but nullSpellingsOtherDistinct (1) is not limits.nullSpellings.maxDistinctOther (64)", StringComparison.Ordinal));
        Assert.Contains(After(static d => d["table"]!["fields"]![1]!["nullSpellingsOtherDistinct"] = 63), static v => v.Contains("/table/fields/1: nullSpellingsOtherDistinctCapped is true but nullSpellingsOtherDistinct (63) is not limits.nullSpellings.maxDistinctOther (64)", StringComparison.Ordinal));
        Assert.Contains(After(static d => d["limits"]!["nullSpellings"]!["maxDistinctOther"] = 32), static v => v.Contains("nullSpellingsOtherDistinctCapped is true but nullSpellingsOtherDistinct (64) is not limits.nullSpellings.maxDistinctOther (32)", StringComparison.Ordinal));

        // The same rules for the all-null rows of the table.
        Assert.Contains(After(static d => d["table"]!["rowClasses"]!["allNullRowSpellingsOtherDistinct"] = 0), static v => v.Contains("rowClasses: allNullRowSpellingsOther (70) is positive but allNullRowSpellingsOtherDistinct is 0", StringComparison.Ordinal));
        Assert.Contains(After(static d => d["table"]!["rowClasses"]!["allNullRowSpellingsOtherDistinct"] = 71), static v => v.Contains("rowClasses: allNullRowSpellingsOtherDistinct (71) is more than allNullRowSpellingsOther (70)", StringComparison.Ordinal));
        Assert.Contains(After(static d => d["table"]!["rowClasses"]!["allNullRowSpellingsOtherDistinct"] = 40), static v => v.Contains("rowClasses: allNullRowSpellingsOtherDistinctCapped is true but allNullRowSpellingsOtherDistinct (40) is not limits.nullSpellings.maxDistinctOther (64)", StringComparison.Ordinal));
        Assert.Contains(After(static d =>
        {
            d["table"]!["rowClasses"]!["allNullRowSpellingsOther"] = 0;
            d["table"]!["rowClasses"]!["allNullRowSpellingsMixed"] = 72;
        }), static v => v.Contains("rowClasses: allNullRowSpellingsOtherDistinct (64) is positive but allNullRowSpellingsOther is 0", StringComparison.Ordinal));

        // The document states them.
        var doc = SchemaFiles.ReadDoc();
        Assert.Contains("`0 <= nullSpellingsOtherDistinct <= nullSpellingsOther`", doc, StringComparison.Ordinal);
        Assert.Contains("nullSpellingsOtherDistinctCapped`** is written as `true`", doc, StringComparison.Ordinal);
        Assert.Contains("allNullRowSpellingsOtherDistinctCapped`** is written as `true`", doc, StringComparison.Ordinal);
        Assert.Contains("limits.nullSpellings.maxDistinctOther", doc, StringComparison.Ordinal);
    }
}
