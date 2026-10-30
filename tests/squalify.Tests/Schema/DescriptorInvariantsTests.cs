// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.Json.Nodes;

namespace squalor.squalify.Tests.Schema;

/// <summary>
/// The rules that a JSON Schema in the supported subset cannot express and that <c>docs/descriptor.md</c> states: sums, equalities,
/// lists that line up, and the correlation of a layout-only descriptor. <see cref="DescriptorInvariants"/> checks them on any
/// descriptor, and every golden must hold them.
/// </summary>
public class DescriptorInvariantsTests
{
    internal static JsonNode Profile(string fixture, bool layoutOnly = false, bool includeValues = false, long? maxBytes = null)
    {
        var descriptor = Profiler.Profile(TestBytes.Fixture(fixture), ProfileOptions.Create(includeValues, [], maxBytes, null, layoutOnly));
        return JsonNode.Parse(DescriptorWriter.ToBytes(descriptor))!;
    }

    internal static string[] Check(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return DescriptorInvariants.Check(document.RootElement).ToArray();
    }

    internal static JsonNode ModelNode()
    {
        return JsonNode.Parse(DescriptorWriter.ToBytes(TestModels.FullDelimited()))!;
    }

    private static string[] Run(JsonNode node, Action<JsonNode> mutate)
    {
        mutate(node);
        return Check(node);
    }

    // The layout-only correlation rule (sq-47)

    [Fact]
    public void Invariants_LayoutOnly_CorrelationRule()
    {
        // A layout-only descriptor and a full one both hold the rule, and the checks do not read the members a layout-only field lacks.
        var layout = Profile("lab/sweep-preamble.csv", layoutOnly: true);
        var full = Profile("lab/sweep-preamble.csv");
        Assert.Equal(true, (bool?)layout["layoutOnly"]);
        Assert.Null(full["layoutOnly"]);
        Assert.Empty(Check(layout));
        Assert.Empty(Check(full));

        // layoutOnly iff hints null iff typed null iff every field bare, and no evidence of the stage "type".
        // A full descriptor marked layoutOnly:
        var marked = Run(Profile("lab/sweep-preamble.csv"), static d => d.AsObject().Insert(3, "layoutOnly", true));
        Assert.Contains(marked, static v => v.Contains("layoutOnly is true but hints is not null", StringComparison.Ordinal));
        Assert.Contains(marked, static v => v.Contains("layoutOnly is true but fingerprint.typed is not null", StringComparison.Ordinal));
        Assert.Contains(marked, static v => v.Contains("layoutOnly is true but /table/fields/0 holds a member that is not a name", StringComparison.Ordinal));

        // A layout-only descriptor with the marker taken off:
        var unmarked = Run(Profile("lab/sweep-preamble.csv", layoutOnly: true), static d => d.AsObject().Remove("layoutOnly"));
        Assert.Contains(unmarked, static v => v.Contains("hints is null but layoutOnly is not true", StringComparison.Ordinal));
        Assert.Contains(unmarked, static v => v.Contains("fingerprint.typed is null but layoutOnly is not true", StringComparison.Ordinal));
        Assert.Contains(unmarked, static v => v.Contains("/table/fields/0 is bare but layoutOnly is not true", StringComparison.Ordinal));

        // layoutOnly is never false.
        var never = Run(Profile("lab/sweep-preamble.csv"), static d => d.AsObject().Insert(3, "layoutOnly", false));
        Assert.Contains(never, static v => v.Contains("layoutOnly is written only as true", StringComparison.Ordinal));

        // Each leg of the equivalence alone:
        var hints = Run(Profile("lab/sweep-preamble.csv", layoutOnly: true), static d => d["hints"] = new JsonObject());
        Assert.Contains(hints, static v => v.Contains("layoutOnly is true but hints is not null", StringComparison.Ordinal));
        var typed = Run(Profile("lab/sweep-preamble.csv", layoutOnly: true), static d => d["fingerprint"]!["typed"] = "sha256:" + new string('a', 64));
        Assert.Contains(typed, static v => v.Contains("layoutOnly is true but fingerprint.typed is not null", StringComparison.Ordinal));
        var nullHints = Run(Profile("lab/sweep-preamble.csv"), static d => d["hints"] = null);
        Assert.Contains(nullHints, static v => v.Contains("hints is null but layoutOnly is not true", StringComparison.Ordinal));
        var nullTyped = Run(Profile("lab/sweep-preamble.csv"), static d => d["fingerprint"]!["typed"] = null);
        Assert.Contains(nullTyped, static v => v.Contains("fingerprint.typed is null but layoutOnly is not true", StringComparison.Ordinal));

        // A field that is not bare in a layout-only descriptor, and a bare field in a full one:
        var typedField = Run(Profile("lab/sweep-preamble.csv", layoutOnly: true), static d => d["table"]!["fields"]![1]!["type"] = "int");
        Assert.Contains(typedField, static v => v.Contains("/table/fields/1 holds a member that is not a name", StringComparison.Ordinal));
        var bareField = Run(Profile("lab/sweep-preamble.csv"), static d =>
        {
            var field = d["table"]!["fields"]![2]!.AsObject();
            foreach (var key in field.Select(static p => p.Key).Where(static k => k is not ("index" or "name" or "rawName" or "nameParts")).ToArray())
            {
                field.Remove(key);
            }
        });
        Assert.Contains(bareField, static v => v.Contains("/table/fields/2 is bare but layoutOnly is not true", StringComparison.Ordinal));

        // No evidence of the stage "type" in a layout-only descriptor.
        var evidence = Run(Profile("lab/sweep-preamble.csv", layoutOnly: true), static d => d["evidence"]!.AsArray().Add(JsonNode.Parse("""{ "stage": "type", "topic": "dateOrderConflict", "chosen": "string", "candidates": [] }""")));
        Assert.Contains(evidence, static v => v.Contains("layoutOnly is true but the evidence holds an entry of the stage 'type'", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariants_LayoutOnly_DoesNotThrowOnAFieldWithoutCounts()
    {
        // A full descriptor with a bare field: the count members are missing. That is a violation, never an exception.
        var node = Profile("lab/sweep-preamble.csv");
        var field = node["table"]!["fields"]![0]!.AsObject();
        foreach (var key in field.Select(static p => p.Key).Where(static k => k is not ("index" or "name" or "rawName" or "nameParts")).ToArray())
        {
            field.Remove(key);
        }

        var violations = Check(node);
        Assert.NotEmpty(violations);
    }

    // Row classes and counts

    [Fact]
    public void Invariants_RowAccounting_ClassCountsAddUpToTheRows()
    {
        Assert.Empty(Check(ModelNode()));
        var rows = Run(ModelNode(), static d => d["table"]!["rows"] = 10001);
        Assert.Contains(rows, static v => v.Contains("table.rows (10001) is not the sum of the class counts (10000) plus truncated (0) plus ragged (0)", StringComparison.Ordinal));

        var classCount = Run(ModelNode(), static d => d["table"]!["rowClasses"]!["classes"]![0]!["count"] = 8999);
        Assert.Contains(classCount, static v => v.Contains("is not the sum of the class counts", StringComparison.Ordinal));

        var listed = Run(ModelNode(), static d => d["table"]!["rowClasses"]!["count"] = 2);
        Assert.Contains(listed, static v => v.Contains("rowClasses.count (2) is not the number of classes listed (3)", StringComparison.Ordinal));

        var fieldCount = Run(ModelNode(), static d => d["table"]!["fields"]![0]!["count"] = 9999);
        Assert.Contains(fieldCount, static v => v.Contains("/table/fields/0: count (9999) is not table.rows (10000) minus the ragged records (0)", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariants_RowClasses_AreOrderedAndTheirFieldRangesWellFormed()
    {
        var order = Run(ModelNode(), static d =>
        {
            var classes = d["table"]!["rowClasses"]!["classes"]!.AsArray();
            var first = classes[0]!;
            classes.RemoveAt(0);
            classes.Add(first);
        });
        Assert.Contains(order, static v => v.Contains("classes are not sorted by count descending, then by id", StringComparison.Ordinal));

        var overlapping = Run(ModelNode(), static d => d["table"]!["rowClasses"]!["classes"]![1]!["fields"] = JsonNode.Parse("[[0, 1], [2, 3]]"));
        Assert.Contains(overlapping, static v => v.Contains("ranges must be ascending and not adjacent", StringComparison.Ordinal));

        var outOfRange = Run(ModelNode(), static d => d["table"]!["rowClasses"]!["classes"]![1]!["fields"] = JsonNode.Parse("[[0, 4]]"));
        Assert.Contains(outOfRange, static v => v.Contains("is past the last field (3)", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariants_AllNullRows_MatchTheAllNullClassAndTheFieldNulls()
    {
        var allNull = Run(ModelNode(), static d => d["table"]!["rowClasses"]!["allNullRows"] = 99);
        Assert.Contains(allNull, static v => v.Contains("allNullRows (99) is not the count of the class with no non-null field (100)", StringComparison.Ordinal));

        var nulls = Run(ModelNode(), static d => d["table"]!["fields"]![1]!["nulls"] = 101);
        Assert.Contains(nulls, static v => v.Contains("/table/fields/1: nulls (101) minus nullsExcludingAllNullRows (0) is not allNullRows (100)", StringComparison.Ordinal));

        var votes = Run(ModelNode(), static d => d["table"]!["fields"]![0]!["typeVotes"] = JsonNode.Parse("""{ "float": 9000 }"""));
        Assert.Contains(votes, static v => v.Contains("/table/fields/0: the type votes (9000) are not count minus nulls (9897)", StringComparison.Ordinal));

        var tokens = Run(ModelNode(), static d => d["table"]!["fields"]![0]!["nullTokens"] = JsonNode.Parse("""{ "NA": 1 }"""));
        Assert.Contains(tokens, static v => v.Contains("/table/fields/0: nullTokens add up to 1, not nulls (103)", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariants_NullSpellings_AddUpAndKeepTheirCrossMemberRules()
    {
        Assert.Empty(Check(ModelNode()));

        var sum = Run(ModelNode(), static d => d["table"]!["fields"]![1]!["nullSpellings"] = JsonNode.Parse("""{ "NA": 1 }"""));
        Assert.Contains(sum, static v => v.Contains("/table/fields/1: nullSpellings (1) plus nullSpellingsOther (70) is not nulls (100)", StringComparison.Ordinal));

        var rows = Run(ModelNode(), static d => d["table"]!["rowClasses"]!["allNullRowSpellingsMixed"] = 1);
        Assert.Contains(rows, static v => v.Contains("allNullRowSpellings (28) plus Mixed (1) plus Other (70) is not allNullRows (100)", StringComparison.Ordinal));
    }

    // Layout, issues, hints, scope, binary

    [Fact]
    public void Invariants_IssueLists_LineUp()
    {
        var lengths = Run(ModelNode(), static d => d["issues"]!.AsArray().Single(static i => (string?)i!["code"] == "invalidUtf8")!["regions"] = JsonNode.Parse("""["preamble", "data"]"""));
        Assert.Contains(lengths, static v => v.Contains("firstLines, offsets and regions must have the same length", StringComparison.Ordinal));

        var both = Run(ModelNode(), static d => d["issues"]!.AsArray().Single(static i => (string?)i!["code"] == "commentInData")!.AsObject().Add("offsets", JsonNode.Parse("[1, 2]")));
        Assert.Contains(both, static v => v.Contains("has both firstLines and offsets without regions", StringComparison.Ordinal));

        var regionsOnly = Run(ModelNode(), static d => d["issues"]!.AsArray().Single(static i => (string?)i!["code"] == "commentInData")!.AsObject().Add("regions", JsonNode.Parse("""["data", "data"]""")));
        Assert.Contains(regionsOnly, static v => v.Contains("regions without offsets", StringComparison.Ordinal));

        var order = Run(ModelNode(), static d =>
        {
            var issues = d["issues"]!.AsArray();
            var first = issues[0]!;
            issues.RemoveAt(0);
            issues.Add(first);
        });
        Assert.Contains(order, static v => v.Contains("issues are not sorted by code", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariants_Pairs_AreTwoAscendingIntegers()
    {
        var pair = Run(ModelNode(), static d => d["layout"]!["regions"]![0]!["lines"] = JsonNode.Parse("[1, 2, 3]"));
        Assert.Contains(pair, static v => v.Contains("/layout/regions/0/lines must be a pair [first, last]", StringComparison.Ordinal));

        var reversed = Run(ModelNode(), static d => d["layout"]!["regions"]![0]!["bytes"] = JsonNode.Parse("[9, 2]"));
        Assert.Contains(reversed, static v => v.Contains("/layout/regions/0/bytes must be a pair [first, last]", StringComparison.Ordinal));

        var child = Run(ModelNode(), static d => d["layout"]!["regions"]![3]!["regions"]![0]!["bytes"] = JsonNode.Parse("[0, 10]"));
        Assert.Contains(child, static v => v.Contains("is not inside its block", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariants_Hints_RepeatTheTable()
    {
        Assert.Empty(Check(ModelNode()));
        var names = Run(ModelNode(), static d => d["hints"]!["headerFields"] = JsonNode.Parse("""["a", "b"]"""));
        Assert.Contains(names, static v => v.Contains("hints.headerFields is not the field names", StringComparison.Ordinal));

        var prefix = Run(ModelNode(), static d => d["hints"]!["headerPrefix"] = JsonNode.Parse("""["Frequency(MHz)"]"""));
        Assert.Contains(prefix, static v => v.Contains("hints.headerPrefix is not the first 5 header fields", StringComparison.Ordinal));

        var nonNull = Run(ModelNode(), static d => d["hints"]!["nonNullFields"] = JsonNode.Parse("[0, 1]"));
        Assert.Contains(nonNull, static v => v.Contains("hints.nonNullFields is [0,1], not the fields with no null outside the all-null rows ([1,2,3])", StringComparison.Ordinal));

        var count = Run(ModelNode(), static d => d["hints"]!["fieldCount"] = JsonNode.Parse("""{ "min": 1, "max": 4 }"""));
        Assert.Contains(count, static v => v.Contains("hints.fieldCount is not table.fieldCount", StringComparison.Ordinal));

        var shape = Run(ModelNode(), static d => d["hints"]!["fileNameShape"] = "a{3}");
        Assert.Contains(shape, static v => v.Contains("hints.fileNameShape is not the shape of file.name", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariants_Scope_AgreesWithTheFile()
    {
        var limited = Profile("lab/sweep-preamble.csv", maxBytes: 200);
        Assert.NotNull(limited["scope"]);
        Assert.Empty(Check(limited));

        var complete = Run(Profile("lab/sweep-preamble.csv", maxBytes: 200), static d => d["scope"]!["complete"] = true);
        Assert.Contains(complete, static v => v.Contains("scope.complete is true but bytesRead", StringComparison.Ordinal));

        var hash = Run(Profile("lab/sweep-preamble.csv", maxBytes: 200), static d => d["file"]!["sha256"] = new string('a', 64));
        Assert.Contains(hash, static v => v.Contains("file.sha256 is written although only a prefix was read", StringComparison.Ordinal));

        var missing = Run(Profile("lab/sweep-preamble.csv"), static d => d["file"]!.AsObject().Remove("sha256"));
        Assert.Contains(missing, static v => v.Contains("file.sha256 is missing although the whole file was read", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariants_Binary_HistogramMatchesItsTotals()
    {
        var binary = Profile("binary/sample.gz");
        Assert.Empty(Check(binary));
        var length = Run(Profile("binary/sample.gz"), static d => d["binary"]!["bytes"]!["histogram"]!.AsArray().RemoveAt(0));
        Assert.Contains(length, static v => v.Contains("binary.bytes.histogram must have 256 counts", StringComparison.Ordinal));

        var nul = Run(Profile("binary/sample.gz"), static d => d["binary"]!["bytes"]!["nul"] = 12345);
        Assert.Contains(nul, static v => v.Contains("binary.bytes.nul", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariants_Fingerprint_AndFormat_TieTheFamilyToItsDialect()
    {
        var dialect = Run(Profile("text/plain.txt"), static d => d["format"]!["dialect"] = JsonNode.Parse("""{ "delimiter": ",", "quote": "\"", "hasHeader": true, "unitsRow": false, "decimalSeparator": ".", "padding": { "trim": false, "paddedShare": 0, "constantLineLength": false, "lineLength": null }, "blocks": 1 }"""));
        Assert.Contains(dialect, static v => v.Contains("format.dialect must be null unless the family is delimited", StringComparison.Ordinal));

        var table = Run(Profile("lab/sweep-preamble.csv"), static d => d["format"]!["dialect"] = null);
        Assert.Contains(table, static v => v.Contains("format.dialect must not be null for the family delimited", StringComparison.Ordinal));
    }
}
