// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class FieldProfilingTests
{
    private static readonly string[] TypeNames = ["empty", "bool", "int", "float", "date", "datetime", "time", "string"];

    private static readonly Dictionary<string, string> Samples = new(StringComparer.Ordinal)
    {
        ["empty"] = string.Empty,
        ["bool"] = "true",
        ["int"] = "5",
        ["float"] = "1.5",
        ["date"] = "2026-03-17",
        ["datetime"] = "2026-03-17 10:00:00",
        ["time"] = "10:00:00",
        ["string"] = "abc",
    };

    private static readonly FieldCaps DefaultCaps = new(10_000, 100_000, 64, 1_000_000);

    public static TheoryData<string, string> AllPairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var a in TypeNames)
        {
            foreach (var b in TypeNames)
            {
                data.Add(a, b);
            }
        }

        return data;
    }

    private static ProfileOptions Options(bool includeValues = false, params string[] allow) => ProfileOptions.Create(includeValues, allow);

    private static Descriptor ProfileText(string text, bool includeValues = false, ProfileLimits? limits = null, params string[] allow)
    {
        var source = new BytesSource("t.csv", Encoding.UTF8.GetBytes(text));
        return limits is null
            ? Profiler.Profile(source, Options(includeValues, allow))
            : Profiler.Profile(source, Options(includeValues, allow), limits);
    }

    // A header line, then one value per line in a single field "v", plus a key field so that one-column quirks of the
    // delimiter vote never matter.
    private static string Column(params string[] values)
    {
        var builder = new StringBuilder("id,v\n");
        for (var i = 0; i < values.Length; i++)
        {
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',').Append(values[i]).Append('\n');
        }

        return builder.ToString();
    }

    private static FieldProfile Field(string text, bool includeValues = false, ProfileLimits? limits = null, int index = 1, params string[] allow)
    {
        return ProfileText(text, includeValues, limits, allow).Table!.Fields[index];
    }

    private static FieldProfile Build(FieldAccumulator accumulator)
    {
        return accumulator.Build(0, "f", null, 0);
    }

    private static FieldAccumulator Accumulate(IEnumerable<string> values, char decimalSeparator = '.', FieldCaps? caps = null, bool valuesAllowed = false)
    {
        var accumulator = new FieldAccumulator(caps ?? DefaultCaps, decimalSeparator, valuesAllowed ? FieldValueAccess.All : FieldValueAccess.None);
        foreach (var value in values)
        {
            accumulator.AddText(value);
        }

        return accumulator;
    }

    private static string ExpectedJoin(string a, string b)
    {
        if (a == "empty")
        {
            return b;
        }

        if (b == "empty" || a == b)
        {
            return a;
        }

        var pair = new[] { a, b };
        if (pair.Contains("int") && pair.Contains("float"))
        {
            return "float";
        }

        if (pair.Contains("date") && pair.Contains("datetime"))
        {
            return "datetime";
        }

        return "string";
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Type_StrictJoin(string a, string b)
    {
        var field = Build(Accumulate([Samples[a], Samples[b]]));
        Assert.Equal(ExpectedJoin(a, b), field.Type);
        var swapped = Build(Accumulate([Samples[b], Samples[a]]));
        Assert.Equal(field.Type, swapped.Type);
    }

    [Fact]
    public void Type_DominantReported_WhenMixed()
    {
        var values = Enumerable.Repeat("1.5", 99).Append("abc");
        var field = Build(Accumulate(values));
        Assert.Equal("string", field.Type);
        Assert.Equal("float", field.Dominant.Type);
        Assert.Equal(0.99, field.Dominant.Share, 12);
        Assert.Equal(new[] { ("float", 99L), ("string", 1L) }, field.TypeVotes.Select(static e => (e.Key, e.Value)).ToArray());
    }

    [Fact]
    public void Type_DominantTie_GoesToTheNarrowerType()
    {
        var field = Build(Accumulate(["1", "2", "1.5", "2.5"]));
        Assert.Equal("float", field.Type);
        Assert.Equal("int", field.Dominant.Type);
        Assert.Equal(0.5, field.Dominant.Share, 12);
    }

    [Fact]
    public void NumericStats_WelfordAndNearestRank()
    {
        var eight = Build(Accumulate(["9", "2", "4", "5", "4", "7", "4", "5"]));
        var stats = Assert.IsType<NumericStats>(eight.Numeric);
        Assert.Equal(DescriptorNumber.Int(2), stats.Min);
        Assert.Equal(DescriptorNumber.Int(9), stats.Max);
        Assert.Equal(5.0, stats.Mean!.Value, 12);
        Assert.Equal(Math.Sqrt(32.0 / 7.0), stats.Stddev!.Value, 12);
        Assert.False(stats.Approximate);
        Assert.Null(stats.ErrorBound);

        // 1..100 in a scrambled order: ceil(p * n) - 1 is the zero-based index into the sorted values.
        var hundred = Build(Accumulate(Enumerable.Range(0, 100).Select(static i => (((i * 37) % 100) + 1).ToString(CultureInfo.InvariantCulture))));
        var q = Assert.IsType<NumericStats>(hundred.Numeric).Quantiles!;
        Assert.Equal(new long[] { 1, 5, 25, 50, 75, 95, 99 }, Ints(q));
        Assert.Equal(50.5, hundred.Numeric!.Mean!.Value, 12);
        Assert.Equal(Math.Sqrt(100.0 * 101.0 / 12.0), hundred.Numeric.Stddev!.Value, 12);

        // Seven values: ranks 1, 1, 2, 4, 6, 7, 7.
        var seven = Build(Accumulate(["70", "10", "40", "20", "60", "30", "50"]));
        Assert.Equal(new long[] { 10, 10, 20, 40, 60, 70, 70 }, Ints(seven.Numeric!.Quantiles!));
    }

    private static long[] Ints(Quantiles<DescriptorNumber> q)
    {
        return new[] { q.P01, q.P05, q.P25, q.P50, q.P75, q.P95, q.P99 }.Select(static n => n.IntegerValue).ToArray();
    }

    [Fact]
    public void NumericStats_FloatField_WritesDoublesAndMixedIntsJoinToFloat()
    {
        var field = Build(Accumulate(["1", "2.5", "4"]));
        Assert.Equal("float", field.Type);
        var stats = field.Numeric!;
        Assert.False(stats.Min.IsInteger);
        Assert.Equal(1.0, stats.Min.RealValue);
        Assert.Equal(4.0, stats.Max.RealValue);
        Assert.Equal(2.5, stats.Quantiles!.P50.RealValue);
    }

    [Fact]
    public void NumericStats_SingleValue_HasNoStddev()
    {
        var stats = Build(Accumulate(["3"], valuesAllowed: true)).Numeric!;
        Assert.Null(stats.Stddev);
        Assert.Equal(3.0, stats.Mean!.Value);
    }

    [Fact]
    public void NumericStats_QuantilesPastTheCap_AreSketched()
    {
        var field = Build(Accumulate(["1", "2", "3", "4"], caps: DefaultCaps with { Quantile = 3 }));
        var stats = field.Numeric!;
        Assert.True(stats.Approximate);
        Assert.Equal(0.001, stats.ErrorBound);
        Assert.Equal(DescriptorNumber.Int(1), stats.Min);
        Assert.Equal(DescriptorNumber.Int(4), stats.Max);
        Assert.Equal(2.5, stats.Mean!.Value, 12);

        // A sketch of four values has compressed nothing: the quantiles are the exact nearest ranks.
        Assert.Equal(new long[] { 1, 1, 1, 2, 3, 4, 4 }, Ints(stats.Quantiles));

        var atTheCap = Build(Accumulate(["1", "2", "3"], caps: DefaultCaps with { Quantile = 3 }));
        Assert.False(atTheCap.Numeric!.Approximate);
    }

    [Fact]
    public void NumericStats_NonNumericField_HasNone()
    {
        Assert.Null(Build(Accumulate(["1", "2", "x"])).Numeric);
        Assert.Null(Build(Accumulate(["true", "false"])).Numeric);
        Assert.Null(Build(Accumulate(["NA", "-"])).Numeric);
    }

    [Fact]
    public void Temporal_MinMaxIso_DefaultOn()
    {
        var dates = Field(Column("2026-03-17", "2026-01-02", "2026-12-31", "2026-06-30"));
        Assert.Equal("date", dates.Type);
        var t = Assert.IsType<TemporalStats>(dates.Temporal);
        Assert.Equal(new[] { "YMD" }, t.Orders);
        Assert.False(t.Ambiguous);
        Assert.Null(t.Clock);
        Assert.Equal("2026-01-02", t.Min);
        Assert.Equal("2026-12-31", t.Max);
        Assert.Equal("2026-01-02", t.Quantiles!.P01);
        Assert.Equal("2026-03-17", t.Quantiles.P50);
        Assert.Equal("2026-06-30", t.Quantiles.P75);
        Assert.Equal("2026-12-31", t.Quantiles.P99);

        var stamps = Field(Column("2026-03-17T10:30:15.25Z", "2026-03-17T09:00:00Z", "2026-03-18T00:00:00Z"));
        Assert.Equal("datetime", stamps.Type);
        Assert.Equal("2026-03-17T09:00:00", stamps.Temporal!.Min);
        Assert.Equal("2026-03-18T00:00:00", stamps.Temporal.Max);
        Assert.Equal("2026-03-17T10:30:15.25", stamps.Temporal.Quantiles!.P50);
        Assert.Equal("24h", stamps.Temporal.Clock);

        var times = Field(Column("9:05 AM", "1:00 PM", "12:30 AM", "12:00 PM"));
        Assert.Equal("time", times.Type);
        Assert.Equal("12h", times.Temporal!.Clock);
        Assert.Empty(times.Temporal.Orders);
        Assert.False(times.Temporal.Ambiguous);
        Assert.Equal("00:30:00", times.Temporal.Min);
        Assert.Equal("13:00:00", times.Temporal.Max);
    }

    [Fact]
    public void Temporal_UnambiguousDayFirstValue_FixesTheOrderForTheWholeField()
    {
        // 6/5/2026 allows DMY and MDY, 17/3/2026 only DMY: the field is DMY and every value reads as DMY.
        var field = Field(Column("17/3/2026", "6/5/2026"));
        Assert.Equal("date", field.Type);
        Assert.Equal(new[] { "DMY" }, field.Temporal!.Orders);
        Assert.False(field.Temporal.Ambiguous);
        Assert.Equal("2026-03-17", field.Temporal.Min);
        Assert.Equal("2026-05-06", field.Temporal.Max);
        Assert.Equal("increasing", field.Monotonic);
    }

    [Fact]
    public void Temporal_AmbiguousOrder_StatsOmitted()
    {
        var field = Field(Column("6/5/2026", "6/5/2026", "7/5/2026"));
        Assert.Equal("date", field.Type);
        var t = field.Temporal!;
        Assert.Equal(new[] { "DMY", "MDY" }, t.Orders);
        Assert.True(t.Ambiguous);
        Assert.Null(t.Min);
        Assert.Null(t.Max);
        Assert.Null(t.Quantiles);
        Assert.Null(field.Monotonic);

        using var json = JsonDocument.Parse(DescriptorWriter.ToBytes(ProfileText(Column("6/5/2026", "6/5/2026", "7/5/2026"))));
        var written = json.RootElement.GetProperty("table").GetProperty("fields")[1].GetProperty("temporal");
        Assert.Equal(JsonValueKind.Null, written.GetProperty("min").ValueKind);
        Assert.Equal(JsonValueKind.Null, written.GetProperty("max").ValueKind);
        Assert.Equal(JsonValueKind.Null, written.GetProperty("quantiles").ValueKind);
        Assert.Equal(JsonValueKind.True, written.GetProperty("ambiguous").ValueKind);
    }

    [Fact]
    public void Temporal_ConflictingOrders_String()
    {
        var descriptor = ProfileText(Column("3/17/2026", "17/3/2026", "5/5/2026"));
        var field = descriptor.Table!.Fields[1];
        Assert.Equal("string", field.Type);
        Assert.Null(field.Temporal);
        Assert.Null(field.Monotonic);
        Assert.Equal("date", field.Dominant.Type);
        var evidence = Assert.Single(descriptor.Evidence, static e => e.Stage == "type" && e.Topic == "dateOrderConflict");
        Assert.Equal("string", evidence.Chosen);
        var candidate = Assert.Single(evidence.Candidates);
        Assert.Equal("1", candidate.Value);
        Assert.Contains("3 date values", candidate.Reason, StringComparison.Ordinal);
        Assert.Contains("DMY 2", candidate.Reason, StringComparison.Ordinal);
        Assert.Contains("MDY 2", candidate.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("17", candidate.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Temporal_DifferentDateLayouts_JoinToString()
    {
        // YMD only on one side, DMY or MDY on the other: no common order.
        var field = Build(Accumulate(["2026-03-17", "3/5/2026"]));
        Assert.Equal("string", field.Type);
        Assert.Null(field.Temporal);
    }

    [Fact]
    public void Patterns_CountTheMatchedDateAndTimePatterns()
    {
        var field = Build(Accumulate(["2026-03-17", "2026-03-18", "2026-03-19 10:00:00", "x"]));
        Assert.Equal(new[] { ("yyyy-nn-nn", 2L), ("yyyy-nn-nn HH:mm:ss", 1L) }.OrderBy(static e => e.Item1, StringComparer.Ordinal).ToArray(), field.Patterns!.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Null(Build(Accumulate(["1", "2"])).Patterns);
    }

    [Fact]
    public void Shapes_MaskRulesAndCap()
    {
        Assert.Equal("A{2}9{2}-a{2}", ShapeMask.Of("AB12-cd"));
        Assert.Equal("Aa", ShapeMask.Of("Ab"));
        Assert.Equal("a a", ShapeMask.Of("a b"));
        Assert.Equal("(9)", ShapeMask.Of("(1)"));
        Assert.Equal("", ShapeMask.Of(""));

        // Past 64 tokens the mask is cut and ends with '+'.
        Assert.Equal(string.Concat(Enumerable.Repeat("a9", 32)) + "+", ShapeMask.Of(string.Concat(Enumerable.Repeat("a1", 40))));

        // At most 64 masks per field; the rest are counted as other, and the order is count descending, then ordinal.
        var accumulator = new FieldAccumulator(DefaultCaps);
        for (var i = 1; i <= 70; i++)
        {
            accumulator.AddText(new string('a', i));
        }

        accumulator.AddText("a");
        accumulator.AddText("a");
        var shapes = Build(accumulator).Shapes!;
        Assert.Equal(65, shapes.Count);
        Assert.Equal(new Shape("a", 3), shapes[0]);
        Assert.Equal(new Shape("other", 6), shapes[^1]);
        var rest = shapes.Skip(1).Take(63).Select(static s => s.Mask).ToArray();
        Assert.All(shapes.Skip(1).Take(63), static s => Assert.Equal(1, s.Count));
        Assert.Equal(rest.OrderBy(static m => m, StringComparer.Ordinal).ToArray(), rest);
        Assert.Equal("a{10}", rest[0]);
        Assert.Equal(shapes.Sum(static s => s.Count), Build(accumulator).Count);
    }

    [Fact]
    public void Shapes_AreAsciiOnly_AndSurrogatePairsAreTwoUnits()
    {
        Assert.Equal("?", ShapeMask.Of("É"));
        Assert.Equal("?", ShapeMask.Of("é"));
        Assert.Equal("a{3}?", ShapeMask.Of("café"));
        Assert.Equal("?{2}", ShapeMask.Of("日本"));
        Assert.Equal("?{2}", ShapeMask.Of("😀"));
        Assert.Equal("?", ShapeMask.Of("İ"));
        Assert.Equal("9", ShapeMask.Of("٣").Replace("?", "9", StringComparison.Ordinal));
        Assert.Equal("?", ShapeMask.Of("٣"));
    }

    [Fact]
    public void Shapes_CountOnlyNonNullValues_OfTheTrimmedText()
    {
        var field = Build(Accumulate(["  ab1 ", "NA", "cd2", ""]));
        Assert.Equal(new[] { new Shape("a{2}9", 2) }, field.Shapes!.ToArray());
        Assert.Equal(new LengthStats(3, 3, 3.0), field.Length);
    }

    [Fact]
    public void Monotonic_SweepColumn_Increasing()
    {
        var d = Profiler.Profile(TestBytes.Fixture("lab/sweep-preamble.csv"), Options());
        var freq = Assert.Single(d.Table!.Fields, static f => f.Name == "Freq(MHz)");
        Assert.Equal("increasing", freq.Monotonic);
        Assert.Equal("int", freq.Type);
        var numeric = freq.Numeric!;
        Assert.Equal(DescriptorNumber.Int(700), numeric.Min);
        Assert.Equal(DescriptorNumber.Int(2600), numeric.Max);
        Assert.Equal(1650.0, numeric.Mean!.Value, 12);
        Assert.Equal("increasing", Assert.Single(d.Table.Fields, static f => f.Name == "Pout(dBm)").Monotonic);

        var sweeps = Profiler.Profile(TestBytes.Fixture("delimited/semiconductor-sweep.csv"), Options());
        Assert.Equal("none", Assert.Single(sweeps.Table!.Fields, static f => f.Name == "Frequency(MHz)").Monotonic);
        Assert.Null(Assert.Single(sweeps.Table.Fields, static f => f.Name == "SN").Monotonic);
    }

    [Theory]
    [InlineData("increasing", "1,2,3")]
    [InlineData("decreasing", "3,2,1")]
    [InlineData("nonDecreasing", "1,1,2")]
    [InlineData("nonIncreasing", "2,1,1")]
    [InlineData("none", "1,3,2")]
    [InlineData("nonDecreasing", "4,4,4")]
    [InlineData("nonDecreasing", "1,NA,2,NA,2")]
    [InlineData("increasing", "1,1.5,2")]
    public void Monotonic_Variants(string expected, string values)
    {
        var field = Build(Accumulate(values.Split(','), valuesAllowed: true));
        Assert.Equal(expected, field.Monotonic);
    }

    [Fact]
    public void Monotonic_IsNullForAFieldWithFewerThanTwoValuesOrANonOrderedType()
    {
        Assert.Null(Build(Accumulate(["5"])).Monotonic);
        Assert.Null(Build(Accumulate(["NA", "NA"])).Monotonic);
        Assert.Null(Build(Accumulate(["a", "b"])).Monotonic);
        Assert.Null(Build(Accumulate(["true", "false"])).Monotonic);
        Assert.Equal("increasing", Build(Accumulate(["2026-01-01", "2026-01-02"])).Monotonic);
        Assert.Equal("decreasing", Build(Accumulate(["10:00:00", "09:00:00"])).Monotonic);
    }

    [Fact]
    public void Distinct_ExactUpToCap_ThenSketched()
    {
        var limits = ProfileLimits.Default with { DistinctPerField = 3 };
        var atCap = ProfileText(Column("a", "b", "c", "a", "b", "NA"), limits: limits);
        Assert.Equal(3, atCap.Limits.DistinctPerField);
        var exact = atCap.Table!.Fields[1].Distinct!;
        Assert.Equal(3, exact.Value);
        Assert.False(exact.Approximate);
        Assert.Null(exact.ErrorBound);
        Assert.False(atCap.Table.Fields[1].Unique);

        var past = ProfileText(Column("a", "b", "c", "d", "a"), limits: limits).Table!.Fields[1];
        Assert.Equal(4, past.Distinct!.Value);
        Assert.True(past.Distinct.Approximate);
        Assert.Equal(1.04 / 128.0, past.Distinct.ErrorBound);
        Assert.Null(past.Unique);

        using var json = JsonDocument.Parse(DescriptorWriter.ToBytes(ProfileText(Column("a", "b", "c", "d", "a"), limits: limits)));
        var distinct = json.RootElement.GetProperty("table").GetProperty("fields")[1].GetProperty("distinct");
        Assert.Equal(4, distinct.GetProperty("value").GetInt64());
        Assert.True(distinct.GetProperty("approximate").GetBoolean());
        Assert.Equal(0.008125, distinct.GetProperty("errorBound").GetDouble());
        Assert.False(distinct.TryGetProperty("exceeds", out _));
    }

    [Fact]
    public void Unique_NeedsAnExactDistinctCount_UnlessADuplicateWasSeen()
    {
        Assert.True(Build(Accumulate(["a", "b", "c"])).Unique);
        Assert.False(Build(Accumulate(["a", "b", "a"])).Unique);
        Assert.False(Build(Accumulate(["NA", "NA"])).Unique);

        var capped = DefaultCaps with { Distinct = 2 };
        Assert.Null(Build(Accumulate(["a", "b", "c"], caps: capped)).Unique);
        Assert.False(Build(Accumulate(["a", "a", "b", "c"], caps: capped)).Unique);
    }

    [Fact]
    public void MemoryBudget_WideFile_ReducesPerFieldCaps_RecordedInLimits()
    {
        const int Columns = 2000;
        var builder = new StringBuilder();
        builder.Append(string.Join(',', Enumerable.Range(0, Columns).Select(static i => "c" + i.ToString(CultureInfo.InvariantCulture)))).Append('\n');
        for (var row = 0; row < 3; row++)
        {
            builder.Append(string.Join(',', Enumerable.Range(0, Columns).Select(i => ((i + row) % 7).ToString(CultureInfo.InvariantCulture)))).Append('\n');
        }

        var d = ProfileText(builder.ToString());
        Assert.Equal(Columns, d.Table!.Fields.Count);
        Assert.Equal(10_000_000 / Columns, d.Limits.QuantilePerField);
        Assert.Equal(2_000_000 / Columns, d.Limits.DistinctPerField);
        Assert.Equal(64, d.Limits.ShapesPerField);
        Assert.Equal(new Budget(10_000_000, 2_000_000, 16_000_000), d.Limits.Budget);
        Assert.Equal(ValueClassifier.NullVocabularyVersion, d.Limits.NullTokens);

        // A narrow file keeps the caps of the tool.
        Assert.Equal(Limits.Default, ProfileText(Column("1", "2")).Limits);
    }

    [Fact]
    public void MemoryBudget_PerFieldCaps_AreMinOfCapAndBudgetOverFields()
    {
        var nominal = Limits.Default;
        Assert.Equal(new FieldCaps(10_000, 100_000, 64, 5_333_333), MemoryBudget.Effective(nominal, 3));
        Assert.Equal(new FieldCaps(10_000, 100_000, 64, 160_000), MemoryBudget.Effective(nominal, 100));
        Assert.Equal(new FieldCaps(10_000, 99_009, 64, 158_415), MemoryBudget.Effective(nominal, 101));
        Assert.Equal(new FieldCaps(1_000, 5_000, 64, 8_000), MemoryBudget.Effective(nominal, 2000));
        Assert.Equal(new FieldCaps(10_000, 100_000, 64, 16_000_000), MemoryBudget.Effective(nominal, 0));

        var small = ProfileLimits.Default with { Budget = new Budget(8, 4, 16_000_000) };
        var d = ProfileText("a,b\n1,x\n2,y\n3,z\n4,w\n5,v\n", limits: small);
        Assert.Equal(4, d.Limits.QuantilePerField);
        Assert.Equal(2, d.Limits.DistinctPerField);
        Assert.True(d.Table!.Fields[0].Numeric!.Approximate);
        Assert.True(d.Table.Fields[0].Distinct!.Approximate);
    }

    [Fact]
    public void ProfileLimits_CannotSetTheNullVocabularyVersion()
    {
        var limits = ProfileLimits.Default with { DistinctPerField = 1 };
        Assert.DoesNotContain(typeof(ProfileLimits).GetProperties(), static p => p.Name == "NullTokens");
        Assert.Equal(ValueClassifier.NullVocabularyVersion, limits.ApplyTo(Limits.Default).NullTokens);
        Assert.Equal(ValueClassifier.NullVocabularyVersion, ProfileText(Column("1", "2"), limits: limits).Limits.NullTokens);
    }

    [Fact]
    public void NoLiteralStrings_WithoutIncludeValues()
    {
        const string Text = "id,tag,note,reading\n1,Lot-A12,xyzzy secret,5\n2,Lot-B77,plugh hidden,6\n3,Lot-A12,xyzzy secret,7\n";
        var d = ProfileText(Text);
        var json = Encoding.UTF8.GetString(DescriptorWriter.ToBytes(d));
        foreach (var literal in new[] { "Lot-A12", "Lot-B77", "xyzzy", "secret", "plugh", "hidden" })
        {
            Assert.DoesNotContain(literal, json, StringComparison.Ordinal);
        }

        foreach (var index in new[] { 1, 2 })
        {
            var field = d.Table!.Fields[index];
            Assert.Equal("string", field.Type);
            Assert.Null(field.Numeric);
            Assert.Null(field.Temporal);
            Assert.Null(field.Top);
            Assert.Null(field.Text);
            Assert.NotNull(field.Length);
            Assert.NotEmpty(field.Shapes!);
            Assert.All(field.Shapes!, static s => Assert.Matches("^[AaA9{}0-9?\\- ]+$", s.Mask));
        }

        Assert.Equal(new[] { "Aa{2}-A9{2}" }, d.Table!.Fields[1].Shapes!.Select(static s => s.Mask).ToArray());

        // A value that is not a vocabulary spelling never reaches the descriptor as a null-token key either.
        var tokens = ProfileText(Column("NULL", "Null", "x"));
        Assert.DoesNotContain("NULL", Encoding.UTF8.GetString(DescriptorWriter.ToBytes(tokens)).Replace("\"null\"", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Constant_StatsWithheld_WithoutFlag()
    {
        const string Text = "id,k,sparse,when,s,other\n1,7,5,2026-03-17,abc,1\n2,7,NULL,2026-03-17,abc,2\n3,7,NULL,2026-03-17,abc,3\n";
        var d = ProfileText(Text);
        var fields = d.Table!.Fields;
        var k = fields[1];
        Assert.True(k.Constant);
        Assert.Equal("constant", k.StatsWithheld);
        Assert.Null(k.Numeric);
        Assert.Equal(new Shape("9", 3), Assert.Single(k.Shapes!));
        Assert.Equal(new LengthStats(1, 1, 1.0), k.Length);

        var sparse = fields[2];
        Assert.True(sparse.Constant);
        Assert.Equal("constant", sparse.StatsWithheld);
        Assert.Null(sparse.Numeric);
        Assert.Equal(2, sparse.Nulls);

        var when = fields[3];
        Assert.True(when.Constant);
        Assert.Equal("constant", when.StatsWithheld);
        Assert.Null(when.Temporal);

        var text = fields[4];
        Assert.True(text.Constant);
        Assert.Null(text.StatsWithheld);

        var varying = fields[5];
        Assert.False(varying.Constant);
        Assert.Null(varying.StatsWithheld);
        Assert.NotNull(varying.Numeric);

        var with = ProfileText(Text, includeValues: true).Table!.Fields;
        Assert.Equal(DescriptorNumber.Int(7), with[1].Numeric!.Min);
        Assert.Null(with[1].StatsWithheld);
        Assert.Equal(DescriptorNumber.Int(5), with[2].Numeric!.Max);
        Assert.Equal("2026-03-17", with[3].Temporal!.Min);

        var allowed = ProfileText(Text, includeValues: false, limits: null, "K").Table!.Fields;
        Assert.NotNull(allowed[1].Numeric);
        Assert.Null(allowed[1].StatsWithheld);
        Assert.Null(allowed[2].Numeric);
        Assert.Equal("constant", allowed[2].StatsWithheld);
    }

    [Fact]
    public void Constant_AllNullAndEmptyFields()
    {
        var d = ProfileText("id,v\n1,NA\n2,NULL\n");
        var allNull = d.Table!.Fields[1];
        Assert.Equal("empty", allNull.Type);
        Assert.True(allNull.Constant);
        Assert.False(allNull.Unique);
        Assert.Null(allNull.StatsWithheld);
        Assert.Null(allNull.Length);
        Assert.Empty(allNull.Shapes!);
        Assert.Equal(0, allNull.Distinct!.Value);

        var none = Build(new FieldAccumulator(DefaultCaps));
        Assert.False(none.Constant);
        Assert.False(none.Unique);
        Assert.Equal(0, none.Count);
    }

    [Fact]
    public void NullTokens_AreKeyedByVocabularyEntry_NotBySpelling()
    {
        var field = Build(Accumulate(["NULL", "null", "Null", "na", "NA", "N/A", "n/a", "", "   ", "nan", "NaN", "-", "NA.", "x"]));
        Assert.Equal(
            new[] { ("", 2L), ("-", 1L), ("N/A", 2L), ("NA", 2L), ("NA.", 1L), ("NaN", 2L), ("null", 3L) },
            field.NullTokens!.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.Equal(13, field.Nulls);
        Assert.Equal(14, field.Count);
        Assert.Empty(Build(Accumulate(["1", "2"])).NullTokens!);
    }

    [Fact]
    public void AteShapedColumns_GiveSensibleConstantAndNullCounts()
    {
        const string Text = "SN,Freq(MHz),Pout(dBm)\nA1,2400,1.5\nA1,2450,NULL\nNULL,NULL,NULL\nA1,2500,2.5\nNULL,NULL,NULL\n";
        var d = ProfileText(Text);
        var sn = d.Table!.Fields[0];
        Assert.Equal("string", sn.Type);
        Assert.Equal(5, sn.Count);
        Assert.Equal(2, sn.Nulls);
        Assert.Equal(0, sn.NullsExcludingAllNullRows);
        Assert.Equal(new[] { ("null", 2L) }, sn.NullTokens!.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.True(sn.Constant);
        Assert.Equal(1, sn.Distinct!.Value);

        var freq = d.Table.Fields[1];
        Assert.Equal("int", freq.Type);
        Assert.False(freq.Constant);
        Assert.Equal(2, freq.Nulls);
        Assert.Equal(0, freq.NullsExcludingAllNullRows);
        Assert.Equal("increasing", freq.Monotonic);

        var pout = d.Table.Fields[2];
        Assert.Equal("float", pout.Type);
        Assert.Equal(3, pout.Nulls);
        Assert.Equal(1, pout.NullsExcludingAllNullRows);
        Assert.Equal(new[] { ("null", 3L) }, pout.NullTokens!.Select(static e => (e.Key, e.Value)).ToArray());
        Assert.False(pout.Constant);
    }

    [Fact]
    public void FlagsAndLength_AreCountedOnTheRawAndTrimmedValues()
    {
        var field = Build(Accumulate([" 007", "12 ", "1,5", "3", "NA", "abcdef"]));
        Assert.Equal(new FieldFlags(1, 1, 2), field.Flags);
        // Trimmed non-null lengths: 3, 2, 3, 1, 6.
        Assert.Equal(new LengthStats(1, 6, 3.0), field.Length);
    }

    [Fact]
    public void DecimalSeparator_IsCheckedWhereItEntersTheAccumulator()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FieldAccumulator(DefaultCaps, ';'));
        var comma = Build(Accumulate(["1,5", "2,25"], ','));
        Assert.Equal("float", comma.Type);
        Assert.Equal(1.5, comma.Numeric!.Min.RealValue);
        Assert.Equal(new FieldFlags(0, 2, 0), comma.Flags);
        Assert.Equal("string", Build(Accumulate(["1,5", "2,25"], '.')).Type);
    }

    [Fact]
    public void DelimitedPass_PassesOnlyDotOrComma_ToTheClassifier()
    {
        var semicolon = Field("a;b\n1,5;2\n2,25;3\n", index: 0);
        Assert.Equal("float", semicolon.Type);
        Assert.Equal(1.5, semicolon.Numeric!.Min.RealValue);
        var tab = Field("a\tb\n1.5\t2\n2.25\t3\n", index: 0);
        Assert.Equal("float", tab.Type);
    }

    [Fact]
    public void TypedInputs_FeedTheSameAccumulator()
    {
        var accumulator = new FieldAccumulator(DefaultCaps);
        accumulator.AddInt64(5);
        accumulator.AddInt64(-3);
        accumulator.AddNull("NaN");
        var ints = Build(accumulator);
        Assert.Equal("int", ints.Type);
        Assert.Equal(DescriptorNumber.Int(-3), ints.Numeric!.Min);
        Assert.Equal(1, ints.Nulls);
        Assert.Equal(new[] { ("NaN", 1L) }, ints.NullTokens!.Select(static e => (e.Key, e.Value)).ToArray());

        var doubles = new FieldAccumulator(DefaultCaps);
        doubles.AddDouble(0.5);
        doubles.AddDouble(double.NaN);
        doubles.AddInt64(2);
        var mixed = Build(doubles);
        Assert.Equal("float", mixed.Type);
        Assert.Equal(1, mixed.Nulls);
        Assert.Equal(2.0, mixed.Numeric!.Max.RealValue);

        var bools = new FieldAccumulator(DefaultCaps);
        bools.AddBool(true);
        bools.AddBool(false);
        Assert.Equal("bool", Build(bools).Type);

        var stamps = new FieldAccumulator(DefaultCaps);
        stamps.AddTemporal(new DateTime(2026, 3, 17, 10, 0, 0, DateTimeKind.Unspecified).Ticks, ValueVote.DateTime);
        stamps.AddTemporal(new DateTime(2026, 1, 2).Ticks, ValueVote.Date);
        var temporal = Build(stamps);
        Assert.Equal("datetime", temporal.Type);
        Assert.Equal("2026-01-02T00:00:00", temporal.Temporal!.Min);
        Assert.Equal("2026-03-17T10:00:00", temporal.Temporal.Max);
        Assert.False(temporal.Temporal.Ambiguous);
    }

    [Theory]
    [InlineData(-1L, (int)ValueVote.Date)]
    [InlineData(-1L, (int)ValueVote.DateTime)]
    [InlineData(-1L, (int)ValueVote.Time)]
    [InlineData(long.MinValue, (int)ValueVote.DateTime)]
    [InlineData(long.MaxValue, (int)ValueVote.DateTime)]
    [InlineData(3_155_378_975_999_999_999L + 1, (int)ValueVote.Date)]
    public void AddTemporal_RejectsTicksOutsideTheDateTimeRange(long ticks, int kind)
    {
        var accumulator = new FieldAccumulator(DefaultCaps);
        Assert.Throws<ArgumentOutOfRangeException>(() => accumulator.AddTemporal(ticks, (ValueVote)kind));
        Assert.Equal(0, Build(accumulator).Count);
    }

    [Fact]
    public void AddTemporal_AcceptsTheWholeDateTimeRange_AndTheClassifierStaysInsideIt()
    {
        var accumulator = new FieldAccumulator(DefaultCaps, '.', FieldValueAccess.All);
        accumulator.AddTemporal(0, ValueVote.DateTime);
        accumulator.AddTemporal(DateTime.MaxValue.Ticks, ValueVote.DateTime);
        var field = Build(accumulator);
        Assert.Equal("0001-01-01T00:00:00", field.Temporal!.Min);
        Assert.Equal("9999-12-31T23:59:59.9999999", field.Temporal.Max);

        // The classifier path: 4-digit years only (1 to 9999 once valid) and a time of day below 24 hours.
        var text = Build(Accumulate(["0001-01-01", "9999-12-31 23:59:59.9999999"], valuesAllowed: true));
        Assert.Equal("0001-01-01T00:00:00", text.Temporal!.Min);
        Assert.Equal("9999-12-31T23:59:59.9999999", text.Temporal.Max);
    }

    [Fact]
    public void Fields_AreDeterministic_AcrossRunsAndCultures()
    {
        var text = File.ReadAllText(TestBytes.Fixture("delimited/semiconductor-sweep.csv"));
        var first = DescriptorWriter.ToBytes(ProfileText(text));
        var second = DescriptorWriter.ToBytes(ProfileText(text));
        Assert.Equal(first, second);

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(first, DescriptorWriter.ToBytes(ProfileText(text)));
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal(first, DescriptorWriter.ToBytes(ProfileText(text)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void NumericStats_MeanAndStddevOverflow_AreNull()
    {
        var field = Build(Accumulate(["1E308", "1.7E308", "-1.7E308"]));
        var stats = field.Numeric!;
        Assert.Null(stats.Mean);
        Assert.Null(stats.Stddev);
        Assert.Equal(-1.7E308, stats.Min.RealValue);
        Assert.Equal(1.7E308, stats.Max.RealValue);
        Assert.NotNull(stats.Quantiles);

        var d = ProfileText(Column("1E308", "1.7E308", "-1.7E308"));
        var json = Encoding.UTF8.GetString(DescriptorWriter.ToBytes(d));
        Assert.DoesNotContain("\"NaN\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Infinity\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"-Infinity\"", json, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(json);
        var numeric = doc.RootElement.GetProperty("table").GetProperty("fields")[1].GetProperty("numeric");
        Assert.Equal(JsonValueKind.Null, numeric.GetProperty("mean").ValueKind);
        Assert.Equal(JsonValueKind.Null, numeric.GetProperty("stddev").ValueKind);

        // An ordinary field keeps both.
        var plain = Build(Accumulate(["1", "2", "4"])).Numeric!;
        Assert.NotNull(plain.Mean);
        Assert.NotNull(plain.Stddev);

        // A spread that overflows only the sum of squares keeps the mean.
        var wide = Build(Accumulate(["1E200", "-1E200", "1E200", "-1E200"])).Numeric!;
        Assert.Equal(0.0, wide.Mean!.Value);
        Assert.Null(wide.Stddev);
    }

    [Theory]
    [InlineData("true,5", "bool")]
    [InlineData("5,true", "bool")]
    [InlineData("5,2026-01-01", "int")]
    [InlineData("2026-01-01,5", "int")]
    [InlineData("2026-01-01,10:00:00", "date")]
    [InlineData("10:00:00,2026-01-01", "date")]
    [InlineData("1.5,2026-01-01", "float")]
    [InlineData("2026-01-01 10:00:00,10:00:00", "datetime")]
    [InlineData("10:00:00,abc", "time")]
    [InlineData("abc,true", "bool")]
    public void Type_DominantTie_UsesTheDocumentedOrder(string values, string expected)
    {
        var field = Build(Accumulate(values.Split(',')));
        Assert.Equal(expected, field.Dominant.Type);
        Assert.Equal(0.5, field.Dominant.Share, 12);
    }

    [Fact]
    public void Shapes_LiteralBraces_AreQuestionMarks()
    {
        Assert.Equal("9?9?", ShapeMask.Of("1{2}"));
        Assert.Equal("9{9}", ShapeMask.Of("999999999"));
        Assert.NotEqual(ShapeMask.Of("999999999"), ShapeMask.Of("1{2}"));
        Assert.Equal("a?{2}", ShapeMask.Of("a}}"));
        Assert.Equal("9?9?", FileNameShape.Of("1{2}"));
        var field = Build(Accumulate(["1{2}", "999999999"]));
        Assert.Equal(new[] { new Shape("9?9?", 1), new Shape("9{9}", 1) }.OrderBy(static x => x.Mask, StringComparer.Ordinal).ToArray(), field.Shapes!.ToArray());
    }

    [Theory]
    [InlineData("1,1.0,01", true)]
    [InlineData("1,01,001", true)]
    [InlineData("1.50,1.5,+1.5", true)]
    [InlineData("0,-0,0.0,-0.0", true)]
    [InlineData("1,2", false)]
    [InlineData("1,1.5", false)]
    [InlineData("1E2,100,100.0", true)]
    [InlineData("9007199254740993,9007199254740992.0", false)]
    public void Constant_NumericFields_ComparesParsedValues(string values, bool constant)
    {
        var field = Build(Accumulate(values.Split(',')));
        Assert.True(field.Type is "int" or "float");
        Assert.Equal(constant, field.Constant);
        Assert.Equal(constant ? "constant" : null, field.StatsWithheld);
        Assert.Equal(constant, field.Numeric is null);
    }

    [Fact]
    public void Constant_ParsedValues_AreWithheldThroughTheProfiler_AndDistinctStaysText()
    {
        var d = ProfileText(Column("1", "1.0", "01", "NULL"));
        var field = d.Table!.Fields[1];
        Assert.Equal("float", field.Type);
        Assert.True(field.Constant);
        Assert.Equal("constant", field.StatsWithheld);
        Assert.Null(field.Numeric);
        Assert.Equal(3, field.Distinct!.Value);
        Assert.True(field.Unique);

        var shown = ProfileText(Column("1", "1.0", "01"), includeValues: true).Table!.Fields[1];
        Assert.True(shown.Constant);
        Assert.Equal(1.0, shown.Numeric!.Min.RealValue);
        Assert.Equal(1.0, shown.Numeric.Max.RealValue);
    }

    [Fact]
    public void Constant_StringTypedFields_CompareText()
    {
        Assert.False(Build(Accumulate(["1", "1.0", "x"])).Constant);
        Assert.False(Build(Accumulate(["1", "01", "x"])).Constant);
        Assert.True(Build(Accumulate(["x", "x", "NA"])).Constant);
        Assert.False(Build(Accumulate(["true", "TRUE"])).Constant);
        // Numbers that stop being numbers fall back to the text, even if the numbers alone were equal.
        var mixed = Build(Accumulate(["1", "01", "x", "x"]));
        Assert.Equal("string", mixed.Type);
        Assert.False(mixed.Constant);
    }

    [Fact]
    public void Constant_TemporalFields_CompareParsedParts()
    {
        var iso = Build(Accumulate(["2026-03-17", "2026-03-17"]));
        Assert.True(iso.Constant);
        Assert.Equal("constant", iso.StatsWithheld);

        // The same date written two ways, and the same moment as a date and as a datetime at midnight.
        var padded = Build(Accumulate(["2026-3-5", "2026-03-05"]));
        Assert.Equal("date", padded.Type);
        Assert.True(padded.Constant);
        Assert.Null(padded.Temporal);

        var ambiguous = Build(Accumulate(["6/5/2026", "06/05/2026"]));
        Assert.Equal("date", ambiguous.Type);
        Assert.True(ambiguous.Constant);
        Assert.Equal("constant", ambiguous.StatsWithheld);

        var datetime = Build(Accumulate(["2026-03-17", "2026-03-17 00:00:00"]));
        Assert.Equal("datetime", datetime.Type);
        Assert.True(datetime.Constant);

        var times = Build(Accumulate(["9:05 PM", "21:05:00"]));
        Assert.Equal("time", times.Type);
        Assert.True(times.Constant);

        // Different dates under the order the pair allows are different, ambiguous or not.
        Assert.False(Build(Accumulate(["6/5/2026", "5/6/2026"])).Constant);
        Assert.False(Build(Accumulate(["2026-03-17", "2026-03-18"])).Constant);

        // No common order: the field is a string and compares text.
        var conflict = Build(Accumulate(["3/17/2026", "03/17/2026", "17/3/2026"]));
        Assert.Equal("string", conflict.Type);
        Assert.False(conflict.Constant);
    }

    [Fact]
    public void Fields_NameParts_AreSetByTheDelimitedPass_NotByTheAccumulator()
    {
        Assert.Null(Build(Accumulate(["1", "2"])).NameParts);
        var d = ProfileText("Freq(MHz),Pout(dBm)\n700,1.5\n800,2.5\n");
        Assert.All(d.Table!.Fields, static f => Assert.NotNull(f.NameParts));
        Assert.Equal("MHz", d.Table.Fields[0].NameParts!.Unit);
    }
}
