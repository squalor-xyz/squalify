// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Nodes;

namespace squalor.squalify.Tests.Schema;

/// <summary>
/// The values the schema accepts and refuses, written out by hand and not derived from the schema: one value outside each pattern,
/// each bound and each closed enum, and a control value that is allowed. The other schema tests take their expectations from the
/// schema itself (the required keys, the closure) or tie an enum to the code (test 6); a pattern, a bound or an enum that was
/// loosened would pass them, and fails here.
/// </summary>
public class SchemaValueTests
{
    private static readonly string Hex64 = new('a', 64);

    private enum Doc
    {
        Model,
        Gz,
        Limited,
    }

    // One place in a document, the value that is allowed there, and values that are not.
    private sealed record Probe(string What, Doc Document, string Pointer, string Good, params string[] Bad);

    private static Probe P(string what, Doc document, string pointer, string good, params string[] bad) => new(what, document, pointer, good, bad);

    private static string Q(string text) => JsonSerializer.Serialize(text);

    private static IEnumerable<Probe> Probes()
    {
        // Patterns: the end of the input is the end of the value, so a trailing line feed is refused too.
        yield return P("file.sha256 is 64 lower-case hex digits", Doc.Gz, "/file/sha256", Q(Hex64), Q(Hex64[..63]), Q(Hex64 + "a"), Q(new string('A', 64)), Q(new string('g', 64)), Q("sha256:" + Hex64), Q(Hex64 + "\n"), Q("x" + Hex64), Q(string.Empty));
        foreach (var member in new[] { "structure", "layout", "names", "typed" })
        {
            yield return P($"fingerprint.{member} is sha256: and 64 lower-case hex digits", Doc.Model, "/fingerprint/" + member, Q("sha256:" + Hex64), Q("sha256:"), Q("sha256:" + Hex64[..63]), Q("sha256:" + Hex64 + "a"), Q("sha256:" + new string('A', 64)), Q("sha256:" + new string('g', 64)), Q(Hex64), Q("sha256:" + Hex64 + "\n"), Q("x" + "sha256:" + Hex64), "17");
        }

        yield return P("fingerprint.typed may be null", Doc.Model, "/fingerprint/typed", "null");
        yield return P("a row class id is rc: and 16 lower-case hex digits", Doc.Model, "/table/rowClasses/classes/0/id", Q("rc:0123456789abcdef"), Q("rc:"), Q("rc:3f9a"), Q("rc:0123456789abcde"), Q("rc:0123456789abcdef0"), Q("rc:0123456789ABCDEF"), Q("0123456789abcdef"), Q("rc:0123456789abcdeg"), Q("rc:0123456789abcdef\n"), Q("xrc:0123456789abcdef"));
        yield return P("tool.version is major.minor.patch", Doc.Model, "/tool/version", Q("1.2.3"), Q("1.0"), Q("x.y.z"), Q(string.Empty), Q("v1.2.3"), Q("1.2.3\n"), Q("x1.2.3"), Q("1.2"), Q("1.2.x"));
        yield return P("tool.version may carry a suffix", Doc.Model, "/tool/version", Q("1.2.3-rc.1"));
        yield return P("tool.name is squalify", Doc.Model, "/tool/name", Q("squalify"), Q("Squalify"), Q("other"));
        yield return P("nameHex is whole bytes in lower-case hex", Doc.Model, "/table/fields/2/nameHex", Q("4c6f74ff"), Q("zz"), Q("abc"), Q("4C6F"), Q(string.Empty), Q("4c6f\n"), Q("g4c6f"), Q("4c6fg4"));
        yield return P("hints.magic.hex is whole bytes in lower-case hex", Doc.Gz, "/hints/magic/hex", Q("1f8b"), Q("1f8"), Q("1F8B"), Q("xx"), Q("x1f8b"), Q(string.Empty));
        yield return P("the non-finite number strings are three spellings", Doc.Model, "/table/fields/0/numeric/mean", Q("NaN"), Q("nan"), Q("+Infinity"), Q("infinity"), Q("1"), Q("NaN\n"), Q("xNaN"), Q("NaNx"));
        yield return P("Infinity is allowed", Doc.Model, "/table/fields/0/numeric/mean", Q("Infinity"));
        yield return P("-Infinity is allowed", Doc.Model, "/table/fields/0/numeric/mean", Q("-Infinity"), Q("--Infinity"));
        yield return P("a number is a number", Doc.Model, "/table/fields/0/numeric/mean", "1.5", "true", "null", "[]");

        // Bounds: shares and scores are at most 1 and at least 0, counts are at least 0, entry counts and line numbers at least 1.
        yield return P("format.score", Doc.Model, "/format/score", "1", "1.01", "-0.01", "100");
        yield return P("kind.confidence", Doc.Model, "/kind/confidence", "1", "1.01", "-0.01");
        yield return P("padding.paddedShare", Doc.Model, "/format/dialect/padding/paddedShare", "0.5", "1.01", "-0.01");
        yield return P("headerConvention.share", Doc.Model, "/layout/headerConvention/share", "0.75", "1.01", "-0.01");
        yield return P("dominant.share", Doc.Model, "/table/fields/0/dominant/share", "1", "1.01", "-0.01");
        yield return P("table.rows is a count", Doc.Model, "/table/rows", "0", "-1", "1.5");
        yield return P("limits.distinctPerField is a count", Doc.Model, "/limits/distinctPerField", "0", "-1");
        yield return P("limits.nullTokens is positive", Doc.Model, "/limits/nullTokens", "1", "0", "-1");
        yield return P("limits.unitTable is positive", Doc.Model, "/limits/unitTable", "1", "0");
        yield return P("limits.nullSpellings.max is positive", Doc.Model, "/limits/nullSpellings/max", "64", "0");
        yield return P("limits.nullSpellings.maxChars is positive", Doc.Model, "/limits/nullSpellings/maxChars", "256", "0");
        yield return P("limits.nullSpellings.maxDistinctOther is positive", Doc.Model, "/limits/nullSpellings/maxDistinctOther", "64", "0");
        yield return P("limits.sketches.hllPrecision is positive", Doc.Model, "/limits/sketches/hllPrecision", "14", "0");
        yield return P("limits.sketches.budgetBytes is positive", Doc.Model, "/limits/sketches/budgetBytes", "1", "0");
        yield return P("limits.sketches.rankError is not negative", Doc.Model, "/limits/sketches/rankError", "0.001", "-0.5");
        yield return P("limits.charsetTolerance.perBytes is positive", Doc.Model, "/limits/charsetTolerance/perBytes", "65536", "0");
        yield return P("scope.maxBytes is positive", Doc.Limited, "/scope/maxBytes", "200", "0", "-1");
        yield return P("format.dialect.blocks is positive", Doc.Model, "/format/dialect/blocks", "2", "0");
        yield return P("an issue count is positive", Doc.Model, "/issues/0/count", "2", "0");
        yield return P("an issue line is positive", Doc.Model, "/issues/0/firstLines/0", "5", "0");
        yield return P("an issue offset is not negative", Doc.Model, "/issues/2/offsets/0", "100", "-1");
        yield return P("a row class count is positive", Doc.Model, "/table/rowClasses/classes/0/count", "9000", "0");
        yield return P("a row class line is positive", Doc.Model, "/table/rowClasses/classes/0/firstLine", "11", "0");
        yield return P("a field range end is not negative", Doc.Model, "/table/rowClasses/classes/1/fields/0/0", "0", "-1");
        yield return P("a type vote is positive", Doc.Model, "/table/fields/0/typeVotes/float", "9895", "0");
        yield return P("a null token count is positive", Doc.Model, "/table/fields/0/nullTokens/NA", "7", "0");
        yield return P("a null spelling count is positive", Doc.Model, "/table/fields/0/nullSpellings/NA", "7", "0");
        yield return P("nullSpellingsOther is positive", Doc.Model, "/table/fields/1/nullSpellingsOther", "70", "0");
        yield return P("nullSpellingsOtherDistinct is positive", Doc.Model, "/table/fields/1/nullSpellingsOtherDistinct", "64", "0");
        yield return P("a pattern count is positive", Doc.Model, "/table/fields/0/patterns/yyyy-MM-dd", "3", "0");
        yield return P("a top count is positive", Doc.Model, "/table/fields/2/top/items/0/count", "9890", "0");
        yield return P("a region line kind count is positive", Doc.Model, "/layout/regions/0/lineKinds/keyValue", "5", "0");
        yield return P("a header convention count is positive", Doc.Model, "/layout/headerConvention/counts/none", "3", "0");
        yield return P("a histogram count is not negative", Doc.Gz, "/binary/bytes/histogram/0", "1", "-1");
        yield return P("padding.lineLength is not negative", Doc.Model, "/format/dialect/padding/lineLength", "118", "-1", "1.5", Q("118"));
        yield return P("padding.lineLength may be null", Doc.Model, "/format/dialect/padding/lineLength", "null");
        yield return P("distinct.relativeStdError is not negative", Doc.Model, "/table/fields/1/distinct/relativeStdError", "0.008125", "-0.1", Q("0.01"));
        yield return P("numeric.rankError is not negative", Doc.Model, "/table/fields/0/numeric/rankError", "0.001", "-0.5", "null", Q("0.001"));
        yield return P("temporal.rankError is not negative", Doc.Model, "/table/fields/1/temporal/rankError", "0.001", "-0.5", "null", Q("0.001"));
        yield return P("length.mean is not negative", Doc.Model, "/table/fields/0/length/mean", "5.2", "-0.5", Q("5"));
        yield return P("length.min is a count", Doc.Model, "/table/fields/0/length/min", "3", "-1", "1.5");
        yield return P("top.maxError is a count", Doc.Model, "/table/fields/2/top/maxError", "3", "-1", "null");
        yield return P("top.items[].value is a string", Doc.Model, "/table/fields/2/top/items/0/value", Q("ABC1"), "null", "1");
        yield return P("text.min is a string", Doc.Model, "/table/fields/2/text/min", Q("ABC1"), "null", "1");
        yield return P("text.max is a string", Doc.Model, "/table/fields/2/text/max", Q("ABC2"), "null", "1");
        yield return P("top is an object", Doc.Model, "/table/fields/2/top", "{ \"items\": [] }", "null", "[]");
        yield return P("text is an object", Doc.Model, "/table/fields/2/text", "{ \"min\": \"a\", \"max\": \"b\" }", "null", "[]");
        yield return P("a child region keeps values and rows as arrays", Doc.Model, "/layout/regions/3/regions/0/values", "[]", "null", "{}");
        yield return P("a child region rows", Doc.Model, "/layout/regions/3/regions/0/rows", "[]", "null", "{}");
        yield return P("the entropy is not negative", Doc.Gz, "/binary/bytes/entropy", "1.5", "-0.1");
        yield return P("the annotation row line is positive", Doc.Model, "/layout/regions/1/rows/0/line", "7", "0");
        yield return P("dataStartLine is positive", Doc.Model, "/hints/dataStartLine", "9", "0");
        yield return P("dataStartLine may be null", Doc.Model, "/hints/dataStartLine", "null");

        // Closed enums not tied to the code by test 6.
        yield return P("encoding.kind", Doc.Model, "/encoding/kind", Q("binary"), Q("ebcdic"), Q("Text"), Q(string.Empty));
        yield return P("encoding.charset", Doc.Model, "/encoding/charset", Q("latin-1"), Q("ebcdic"), Q("UTF-8"), Q("utf8"), Q("windows-1252"));
        yield return P("encoding.charset may be null", Doc.Model, "/encoding/charset", "null");
        yield return P("encoding.bom", Doc.Model, "/encoding/bom", Q("utf-16le"), Q("utf-7"), Q("ascii"), Q("UTF-8"));
        yield return P("encoding.bom may be null", Doc.Model, "/encoding/bom", "null");
        yield return P("kind.label", Doc.Model, "/kind/label", Q("binary"), Q("labSweepTable"), Q("Text"));
        yield return P("kind.ruleId", Doc.Model, "/kind/ruleId", Q("K11"), Q("K05"), Q("K10"), Q("k09"));
        yield return P("format.dialect.delimiter", Doc.Model, "/format/dialect/delimiter", Q("\t"), Q(":"), Q("tab"), Q("Whitespace"), Q(string.Empty));
        yield return P("format.dialect.quote", Doc.Model, "/format/dialect/quote", Q("\""), Q("'"), Q(string.Empty), "null");
        yield return P("format.dialect.decimalSeparator", Doc.Model, "/format/dialect/decimalSeparator", Q(","), Q(";"), Q("comma"));
        yield return P("layout.headerConvention.dominant", Doc.Model, "/layout/headerConvention/dominant", Q("unitsRow"), Q("nameBraceUnit"), Q("NameParenUnit"), "null");
        yield return P("layout.regions[].kind", Doc.Model, "/layout/regions/0/kind", Q("block"), Q("wedge"), Q("Preamble"));
        yield return P("a child region kind (not block)", Doc.Model, "/layout/regions/3/regions/0/kind", Q("data"), Q("block"), Q("wedge"));
        yield return P("a region line kind name", Doc.Model, "/layout/regions/0/lineKinds/blank", "1");
        yield return P("an annotation row type", Doc.Model, "/layout/regions/1/rows/0/type", Q("string"), Q("duration"), Q("Float"));
        yield return P("a field type", Doc.Model, "/table/fields/0/type", Q("empty"), Q("duration"), Q("integer"), Q("Float"));
        yield return P("a dominant type", Doc.Model, "/table/fields/0/dominant/type", Q("time"), Q("duration"));
        yield return P("a name convention", Doc.Model, "/table/fields/0/nameParts/convention", Q("unitsRow"), Q("nameBraceUnit"), Q("NameParenUnit"));
        yield return P("monotonic", Doc.Model, "/table/fields/0/monotonic", Q("increasing"), Q("sideways"), Q("Increasing"));
        yield return P("temporal clock", Doc.Model, "/table/fields/1/temporal/clock", Q("12h"), Q("36h"), Q("24H"));
        yield return P("a temporal order", Doc.Model, "/table/fields/1/temporal/orders/0", Q("MDY"), Q("YDM"), Q("dmy"));
        yield return P("statsWithheld", Doc.Model, "/table/fields/2/statsWithheld", Q("constant"), Q("always"), Q("Constant"));
        yield return P("statsWithheld may be null", Doc.Model, "/table/fields/2/statsWithheld", "null");
        yield return P("hints.delimiter", Doc.Model, "/hints/delimiter", Q(";"), Q("colon"));
        yield return P("hints.delimiter may be null", Doc.Model, "/hints/delimiter", "null");
        yield return P("hints.headerConvention may be null", Doc.Model, "/hints/headerConvention", "null");
        yield return P("hints.headerConvention", Doc.Model, "/hints/headerConvention", Q("nameParenUnit"), Q("nameBraceUnit"));
        yield return P("an issue region name", Doc.Model, "/issues/2/regions/0", Q("footer"), Q("block"), Q("Data"), Q("middle"));
        yield return P("a type vote key", Doc.Model, "/table/fields/1/typeVotes/string", "1");

        // The marker members are the constant true.
        yield return P("distinct.approximate", Doc.Model, "/table/fields/1/distinct/approximate", "true", "false", "1", Q("true"));
        yield return P("encoding.invalidBytesTolerated", Doc.Model, "/encoding/invalidBytesTolerated", "true", "false");
        yield return P("nullSpellingsOtherDistinctCapped", Doc.Model, "/table/fields/1/nullSpellingsOtherDistinctCapped", "true", "false");
        yield return P("allNullRowSpellingsOtherDistinctCapped", Doc.Model, "/table/rowClasses/allNullRowSpellingsOtherDistinctCapped", "true", "false");
        yield return P("schemaVersion", Doc.Model, "/schemaVersion", Q("1.0"), Q("1.1"), Q("2.0"), "1");
        yield return P("layoutOnly is only true", Doc.Model, "/layoutOnly", "true", "false");
    }

    // Keys of closed maps: an added key is refused unless it is a name of the vocabulary.
    private static IEnumerable<Probe> MapKeys()
    {
        foreach (var type in new[] { "bool", "int", "float", "date", "datetime", "time", "string" })
        {
            yield return P("a type vote key " + type, Doc.Model, "/table/fields/1/typeVotes/" + type, "1");
        }

        foreach (var token in new[] { string.Empty, "NA", "NA.", "N/A", "null", "NaN", "-" })
        {
            yield return P("a null token key '" + token + "'", Doc.Model, "/table/fields/1/nullTokens/" + SubsetValidator.Escape(token), "1");
        }

        foreach (var kind in new[] { "blank", "comment", "keyValue", "keyDelimValue", "text" })
        {
            yield return P("a line kind key " + kind, Doc.Model, "/layout/regions/0/lineKinds/" + kind, "1");
        }

        foreach (var convention in new[] { "nameParenUnit", "nameBracketUnit", "nameUnderscoreUnit", "unitsRow", "none" })
        {
            yield return P("a header convention count key " + convention, Doc.Model, "/layout/headerConvention/counts/" + convention, "1");
        }
    }

    private static JsonNode Base(Doc document)
    {
        return document switch
        {
            Doc.Model => JsonNode.Parse(DescriptorWriter.ToBytes(TestModels.FullDelimited()))!,
            Doc.Gz => SchemaTests.GoldenNode("binary/sample.gz"),
            _ => DescriptorInvariantsTests.Profile("lab/sweep-preamble.csv", maxBytes: 200),
        };
    }

    private static void Set(JsonNode root, string pointer, JsonNode? value)
    {
        var segments = pointer.Split('/').Skip(1).Select(static s => s.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
        var node = root;
        foreach (var segment in segments[..^1])
        {
            if (node is JsonArray array)
            {
                node = array[int.Parse(segment, System.Globalization.CultureInfo.InvariantCulture)]!;
            }
            else
            {
                node = node[segment] ?? (node.AsObject()[segment] = new JsonObject())!;
            }
        }

        if (node is JsonArray last)
        {
            last[int.Parse(segments[^1], System.Globalization.CultureInfo.InvariantCulture)] = value;
        }
        else
        {
            node.AsObject()[segments[^1]] = value;
        }
    }

    private static string[] Problems(IEnumerable<Probe> probes)
    {
        var problems = new List<string>();
        foreach (var probe in probes)
        {
            var good = Base(probe.Document);
            Set(good, probe.Pointer, JsonNode.Parse(probe.Good));
            var goodErrors = SchemaTests.Validate(good);
            if (goodErrors.Count > 0)
            {
                problems.Add($"{probe.What}: the allowed value {probe.Good} at {probe.Pointer} was refused: {goodErrors[0]}");
            }

            foreach (var bad in probe.Bad)
            {
                var root = Base(probe.Document);
                Set(root, probe.Pointer, JsonNode.Parse(bad));
                var errors = SchemaTests.Validate(root);
                if (!errors.Any(e => e.Pointer == probe.Pointer))
                {
                    problems.Add($"{probe.What}: {bad} at {probe.Pointer} was accepted");
                }
            }
        }

        return problems.ToArray();
    }

    [Fact]
    public void Validator_RejectsAValueOutsideEveryPatternBoundAndClosedEnum_AndAcceptsTheValuesInside()
    {
        var probes = Probes().ToArray();
        Assert.True(probes.Length > 80, "only " + probes.Length + " probes");
        var problems = Problems(probes);
        Assert.True(problems.Length == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Schema_ClosedEnums_AreExactlyTheValuesListedHere()
    {
        // The enums of the schema, by where they are, written out. A value added to or taken from one is a change of the schema that
        // this list must follow (adding a value is a minor change, taking one away a major one).
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["$defs/valueType"] = ["empty", "bool", "int", "float", "date", "datetime", "time", "string"],
            ["$defs/convention"] = ["nameParenUnit", "nameBracketUnit", "nameUnderscoreUnit", "unitsRow", "none"],
            ["$defs/delimiter"] = [",", "\t", ";", "|", "whitespace", "none"],
            ["$defs/regionKind"] = ["preamble", "annotation", "header", "units", "data", "footer", "block", "unknown"],
            ["$defs/childRegionKind"] = ["preamble", "annotation", "header", "units", "data", "footer", "unknown"],
            ["$defs/issueRegion"] = ["preamble", "annotation", "header", "units", "data", "footer", "unknown", "other"],
            ["$defs/issueCode"] = ["blocksTruncated", "commentInData", "duplicateHeader", "emptyHeaderName", "heterogeneousBlocks", "includeValuesForUnmatched", "invalidUnicode", "invalidUtf8", "mixedLineEndings", "nameExcludedFromFingerprint", "noDataRows", "raggedRows", "tooIrregular"],
            ["$defs/evidenceStage"] = ["encoding", "format", "dialect", "header", "unit", "type"],
            ["$defs/delimitedDialect/properties/quote"] = ["\""],
            ["$defs/delimitedDialect/properties/decimalSeparator"] = [".", ","],
            ["$defs/field/properties/temporal/properties/orders/items"] = ["DMY", "MDY", "YMD"],
            ["$defs/field/properties/temporal/properties/clock"] = ["24h", "12h"],
            ["$defs/field/properties/statsWithheld"] = ["constant", "null"],
            ["$defs/field/properties/monotonic"] = ["increasing", "decreasing", "nonDecreasing", "nonIncreasing", "none"],
            ["properties/encoding/properties/kind"] = ["empty", "text", "binary"],
            ["properties/encoding/properties/charset"] = ["ascii", "utf-8", "latin-1", "utf-16le", "utf-16be", "utf-32le", "utf-32be", "null"],
            ["properties/encoding/properties/bom"] = ["utf-8", "utf-16le", "utf-16be", "utf-32le", "utf-32be", "null"],
            ["properties/format/properties/family"] = ["delimited", "text", "binary", "empty"],
            ["properties/kind/properties/label"] = ["binary", "empty", "text"],
            ["properties/kind/properties/ruleId"] = ["K09", "K11", "K12"],
            ["properties/hints/properties/delimiter"] = [",", "\t", ";", "|", "whitespace", "none", "null"],
            ["properties/hints/properties/headerConvention"] = ["nameParenUnit", "nameBracketUnit", "nameUnderscoreUnit", "unitsRow", "none", "null"],
        };

        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var found = new Dictionary<string, string[]>(StringComparer.Ordinal);
        CollectEnums(schema.RootElement, string.Empty, found);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), found.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, values) in expected)
        {
            Assert.True(values.Order(StringComparer.Ordinal).SequenceEqual(found[path].Order(StringComparer.Ordinal), StringComparer.Ordinal), $"{path}: {string.Join(", ", found[path])} is not {string.Join(", ", values)}");
        }
    }

    private static void CollectEnums(JsonElement node, string path, Dictionary<string, string[]> found)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var member in node.EnumerateObject())
        {
            var at = path.Length == 0 ? member.Name : path + "/" + member.Name;
            switch (member.Name)
            {
                case "enum":
                    found[path] = member.Value.EnumerateArray().Select(static e => e.ValueKind == JsonValueKind.Null ? "null" : e.GetString()!).ToArray();
                    break;
                case "properties" or "$defs":
                    foreach (var named in member.Value.EnumerateObject())
                    {
                        CollectEnums(named.Value, at + "/" + named.Name, found);
                    }

                    break;
                case "items" or "additionalProperties":
                    CollectEnums(member.Value, at, found);
                    break;
                case "oneOf":
                    var index = 0;
                    foreach (var alternative in member.Value.EnumerateArray())
                    {
                        CollectEnums(alternative, at + "/" + index++, found);
                    }

                    break;
            }
        }
    }

    [Fact]
    public void Validator_RejectsAKeyOutsideEveryClosedMapOfTheSchema_AndAcceptsTheVocabulary()
    {
        // The vocabulary keys are accepted (the map is not empty of them); a key outside it is refused at the key.
        var probes = MapKeys().ToArray();
        var mapProblems = Problems(probes);
        Assert.True(mapProblems.Length == 0, string.Join("\n", mapProblems));
        var refused = new[]
        {
            P("a type vote key", Doc.Model, "/table/fields/1/typeVotes/duration", "1"),
            P("a null token key", Doc.Model, "/table/fields/1/nullTokens/NULL", "1"),
            P("a null token key", Doc.Model, "/table/fields/1/nullTokens/n~1a", "1"),
            P("a line kind key", Doc.Model, "/layout/regions/0/lineKinds/poem", "1"),
            P("a line kind key of a child region", Doc.Model, "/layout/regions/3/regions/0/lineKinds/poem", "1"),
            P("a header convention count key", Doc.Model, "/layout/headerConvention/counts/nameBraceUnit", "1"),
            P("an annotation null token key", Doc.Model, "/layout/regions/1/rows/0/nullTokens/Nil", "1"),
            P("an annotation type vote key", Doc.Model, "/layout/regions/1/rows/0/typeVotes/duration", "1"),
        };
        foreach (var probe in refused)
        {
            var root = Base(probe.Document);
            Set(root, probe.Pointer, JsonNode.Parse(probe.Good));
            Assert.Contains(SchemaTests.Validate(root), e => e.Pointer == probe.Pointer && e.Message.Contains("unexpected key", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Schema_Id_IsATagPinnedRawUrlOfTheSchemaVersion()
    {
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var id = schema.RootElement.GetProperty("$id").GetString()!;
        var version = schema.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString()!;
        Assert.True(IdMatches(id, version), $"$id {id} is not a raw.githubusercontent.com URL of squalor-xyz/squalify pinned to a tag v{version}.x ending in schema/descriptor-v1.schema.json");

        // The rule itself: the tag must say the major and minor of the schema, whatever follows the minor.
        Assert.True(IdMatches("https://raw.githubusercontent.com/squalor-xyz/squalify/v1.0.0/schema/descriptor-v1.schema.json", "1.0"));
        Assert.True(IdMatches("https://raw.githubusercontent.com/squalor-xyz/squalify/v1.1.3/schema/descriptor-v1.schema.json", "1.1"));
        Assert.True(IdMatches("https://raw.githubusercontent.com/squalor-xyz/squalify/v1.1/schema/descriptor-v1.schema.json", "1.1"));
        Assert.False(IdMatches("https://raw.githubusercontent.com/squalor-xyz/squalify/v1.0.0/schema/descriptor-v1.schema.json", "1.1"));
        Assert.False(IdMatches("https://raw.githubusercontent.com/squalor-xyz/squalify/v1.10.0/schema/descriptor-v1.schema.json", "1.1"));
        Assert.False(IdMatches("https://raw.githubusercontent.com/squalor-xyz/squalify/main/schema/descriptor-v1.schema.json", "1.0"));
        Assert.False(IdMatches("https://github.com/squalor-xyz/squalify/blob/v1.0.0/schema/descriptor-v1.schema.json", "1.0"));
        Assert.False(IdMatches("https://raw.githubusercontent.com/other/squalify/v1.0.0/schema/descriptor-v1.schema.json", "1.0"));
        Assert.False(IdMatches("https://raw.githubusercontent.com/squalor-xyz/squalify/v1.0.0/schema/other.schema.json", "1.0"));
    }

    private static bool IdMatches(string id, string schemaVersion)
    {
        const string Prefix = "https://raw.githubusercontent.com/squalor-xyz/squalify/";
        const string Suffix = "/schema/descriptor-v1.schema.json";
        if (!id.StartsWith(Prefix, StringComparison.Ordinal) || !id.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var tag = id[Prefix.Length..^Suffix.Length];
        var pinned = "v" + schemaVersion;
        return tag == pinned || tag.StartsWith(pinned + ".", StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_DoesNotDependOnTheIdOfTheSchema()
    {
        var node = JsonNode.Parse(SchemaFiles.ReadSchema())!.AsObject();
        node["$id"] = "https://example.invalid/anything.json";
        var other = SubsetValidator.Load(node.ToJsonString());
        using var document = JsonDocument.Parse(SchemaTests.GoldenNode().ToJsonString());
        Assert.Empty(other.Validate(document.RootElement));
        node.Remove("$id");
        Assert.Empty(SubsetValidator.Load(node.ToJsonString()).Validate(document.RootElement));
    }
}
