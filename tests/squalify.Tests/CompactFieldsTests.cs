// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// sq-32: the compact layout of the descriptor (one-line small containers) and of <c>table.fields[]</c> (null,
/// all-zero <c>flags</c> and false <c>approximate</c> members left out).
/// </summary>
public class CompactFieldsTests
{
    private static string Text(Descriptor descriptor)
    {
        return new UTF8Encoding(false, true).GetString(DescriptorWriter.ToBytes(descriptor));
    }

    private static JsonElement Field(JsonDocument document, int index = 0)
    {
        return document.RootElement.GetProperty("table").GetProperty("fields")[index];
    }

    private static JsonDocument Parse(Descriptor descriptor)
    {
        return JsonDocument.Parse(DescriptorWriter.ToBytes(descriptor));
    }

    // ---- size ----

    /// <summary>
    /// A deterministic wide CSV: <paramref name="columns"/> columns of ten kinds (two float kinds with a unit in the
    /// header, integer, zero-padded integer, date, datetime, categorical text, unique text, mostly-null, constant).
    /// A fixed 64-bit LCG, no <see cref="Random"/>, so every run gives the same bytes. Port of the sq-32 measurement script.
    /// </summary>
    internal static byte[] WideCsv(int columns, int rows)
    {
        string[] units = ["dB", "MHz", "V", "mA", "degC", "ns"];
        string[] cats = ["PASS", "FAIL", "RETEST", "SKIP", "ABORT"];
        string[] sparseTokens = ["", "NA", "N/A", "-"];
        var inv = CultureInfo.InvariantCulture;
        ulong state = 12345;

        double Rnd()
        {
            unchecked
            {
                state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            }

            return (state >> 33) / (double)(1UL << 31);
        }

        var sb = new StringBuilder();
        for (var c = 0; c < columns; c++)
        {
            if (c > 0)
            {
                sb.Append(',');
            }

            sb.Append(
                (c % 10) switch
                {
                    0 => string.Create(inv, $"Meas{c}({units[c % units.Length]})"),
                    1 => string.Create(inv, $"Level{c}[{units[(c + 1) % units.Length]}]"),
                    2 => string.Create(inv, $"Count_{c}"),
                    3 => string.Create(inv, $"Code_{c}"),
                    4 => string.Create(inv, $"Day_{c}"),
                    5 => string.Create(inv, $"Stamp_{c}"),
                    6 => string.Create(inv, $"Bin_{c}"),
                    7 => string.Create(inv, $"Serial_{c}"),
                    8 => string.Create(inv, $"Rare_{c}"),
                    _ => string.Create(inv, $"Fixed_{c}"),
                });
        }

        sb.Append('\n');
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                if (c > 0)
                {
                    sb.Append(',');
                }

                var x = Rnd();
                switch (c % 10)
                {
                    case 0:
                    case 1:
                        sb.Append((x * 100 - 20).ToString("F4", inv));
                        break;
                    case 2:
                        sb.Append(((int)(x * 10000)).ToString(inv));
                        break;
                    case 3:
                        sb.Append(((int)(x * 1000)).ToString("D5", inv));
                        break;
                    case 4:
                        sb.Append(string.Create(inv, $"2026-{1 + (int)(x * 12):D2}-{1 + (int)(Rnd() * 28):D2}"));
                        break;
                    case 5:
                        sb.Append(string.Create(inv, $"2026-{1 + (int)(x * 12):D2}-{1 + (int)(Rnd() * 28):D2}T{(int)(Rnd() * 24):D2}:{(int)(Rnd() * 60):D2}:{(int)(Rnd() * 60):D2}"));
                        break;
                    case 6:
                        sb.Append(cats[(int)(x * cats.Length)]);
                        break;
                    case 7:
                        sb.Append(string.Create(inv, $"S{(long)r * 7 + c:D8}"));
                        break;
                    case 8:
                        sb.Append(x < 0.97 ? sparseTokens[(int)(Rnd() * 4)] : (Rnd() * 5).ToString("F3", inv));
                        break;
                    default:
                        sb.Append("42");
                        break;
                }
            }

            sb.Append('\n');
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Fact]
    public void WideCsv_SameArguments_SameBytes()
    {
        Assert.Equal(WideCsv(20, 50), WideCsv(20, 50));
    }

    [Fact]
    public void Size_300ColumnProfile_AtMost1050BytesPerField()
    {
        var descriptor = Profiler.Profile(new BytesSource("wide.csv", WideCsv(300, 2000)), ProfileOptions.Create(false, []));
        var bytes = DescriptorWriter.ToBytes(descriptor);
        using var document = JsonDocument.Parse(bytes);
        var fields = document.RootElement.GetProperty("table").GetProperty("fields");
        Assert.Equal(300, fields.GetArrayLength());

        // The raw text of the array: every byte of every field profile, indentation and separators included.
        var perField = Encoding.UTF8.GetByteCount(fields.GetRawText()) / 300.0;
        Assert.True(perField <= 1050.0, $"table.fields takes {perField:F0} bytes per field, above the target of 1050");
    }

    // ---- omissions inside a field ----

    [Fact]
    public void Field_NullMembers_AreOmitted()
    {
        var numeric = new NumericStats(DescriptorNumber.Int(1), DescriptorNumber.Int(1), 1.0, null, TestModels.IntQuantiles(1), false);
        var field = TestModels.FloatField(numeric) with { Length = null, Monotonic = null, Unique = null, Distinct = null };
        using var document = Parse(TestModels.Minimal([field]));
        var f = Field(document);

        foreach (var absent in new[] { "length", "monotonic", "unique", "distinct", "temporal", "patterns", "unit", "top", "text" })
        {
            Assert.False(f.TryGetProperty(absent, out _), $"{absent} is written although it is null");
        }

        // numeric.stddev is null (fewer than two values): omitted at depth too.
        Assert.False(f.GetProperty("numeric").TryGetProperty("stddev", out _));
        Assert.True(f.GetProperty("numeric").TryGetProperty("mean", out _));

        // A mean that overflowed is null too, and is left out on its own.
        var overflow = numeric with { Mean = null, Stddev = 2.5 };
        using var other = Parse(TestModels.Minimal([TestModels.FloatField(overflow)]));
        Assert.False(Field(other).GetProperty("numeric").TryGetProperty("mean", out _));
        Assert.Equal(2.5, Field(other).GetProperty("numeric").GetProperty("stddev").GetDouble());
    }

    [Fact]
    public void Field_NullsOfTheConsumerSubset_StayAsNull()
    {
        var field = TestModels.FloatField(new NumericStats(DescriptorNumber.Int(1), DescriptorNumber.Int(1), 1.0, null, TestModels.IntQuantiles(1), false));
        using var document = Parse(TestModels.Minimal([field]));
        var f = Field(document);

        Assert.Equal(JsonValueKind.Null, f.GetProperty("statsWithheld").ValueKind);
        Assert.Equal(JsonValueKind.Null, f.GetProperty("nameParts").GetProperty("unit").ValueKind);

        // A headerless table: name and nameParts are documented as null and consumers read them.
        var headerless = field with { Name = null, NameParts = null };
        using var bare = Parse(TestModels.Minimal([headerless]));
        Assert.Equal(JsonValueKind.Null, Field(bare).GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, Field(bare).GetProperty("nameParts").ValueKind);
    }

    [Fact]
    public void Field_NullTokensAndConstant_NullStaysWritten()
    {
        var field = TestModels.FloatField(new NumericStats(DescriptorNumber.Int(1), DescriptorNumber.Int(1), 1.0, null, TestModels.IntQuantiles(1), false))
            with
        { NullTokens = null, Constant = null };
        using var document = Parse(TestModels.Minimal([field]));
        Assert.Equal(JsonValueKind.Null, Field(document).GetProperty("nullTokens").ValueKind);
        Assert.Equal(JsonValueKind.Null, Field(document).GetProperty("constant").ValueKind);
    }

    [Fact]
    public void Field_TemporalNulls_AreOmitted_AmbiguousKeepsOrdersAndAmbiguous()
    {
        var temporal = new TemporalStats(["DMY", "MDY"], true, null, null, null, null, null);
        var numeric = new NumericStats(DescriptorNumber.Int(1), DescriptorNumber.Int(1), 1.0, null, TestModels.IntQuantiles(1), false);
        var field = TestModels.FloatField(numeric) with { Type = "string", Numeric = null, Temporal = temporal };
        using var document = Parse(TestModels.Minimal([field]));
        var t = Field(document).GetProperty("temporal");

        Assert.Equal(2, t.GetProperty("orders").GetArrayLength());
        Assert.True(t.GetProperty("ambiguous").GetBoolean());
        foreach (var absent in new[] { "clock", "min", "max", "quantiles", "approximate" })
        {
            Assert.False(t.TryGetProperty(absent, out _), $"temporal.{absent} is written although it is null");
        }
    }

    [Fact]
    public void Field_AllZeroFlags_AreOmitted_AnyNonZeroCountKeepsAllThree()
    {
        var numeric = new NumericStats(DescriptorNumber.Int(1), DescriptorNumber.Int(1), 1.0, null, TestModels.IntQuantiles(1), false);
        var zero = TestModels.FloatField(numeric);
        using (var document = Parse(TestModels.Minimal([zero])))
        {
            Assert.False(Field(document).TryGetProperty("flags", out _));
        }

        foreach (var flags in new[] { new FieldFlags(1, 0, 0), new FieldFlags(0, 1, 0), new FieldFlags(0, 0, 1) })
        {
            using var document = Parse(TestModels.Minimal([zero with { Flags = flags }]));
            var written = Field(document).GetProperty("flags");
            Assert.Equal(flags.LeadingZero, written.GetProperty("leadingZero").GetInt64());
            Assert.Equal(flags.CommaDecimalLike, written.GetProperty("commaDecimalLike").GetInt64());
            Assert.Equal(flags.Padded, written.GetProperty("padded").GetInt64());
        }
    }

    [Fact]
    public void Field_ApproximateFalse_IsOmitted_TrueIsKept()
    {
        var exact = new NumericStats(DescriptorNumber.Int(1), DescriptorNumber.Int(2), 1.5, 0.5, TestModels.IntQuantiles(1), false);
        var sketched = exact with { Approximate = true, RankError = 0.001 };
        var temporalExact = new TemporalStats(["YMD"], false, null, "2026-01-01", "2026-01-02", new Quantiles<string>("a", "b", "c", "d", "e", "f", "g"), false);
        var temporalSketched = temporalExact with { Approximate = true, RankError = 0.001 };

        var baseField = TestModels.FloatField(exact);
        using (var document = Parse(TestModels.Minimal([baseField with { Temporal = temporalExact, Distinct = new DistinctInfo(5, false) }])))
        {
            var f = Field(document);
            Assert.False(f.GetProperty("numeric").TryGetProperty("approximate", out _));
            Assert.False(f.GetProperty("temporal").TryGetProperty("approximate", out _));
            Assert.False(f.GetProperty("distinct").TryGetProperty("approximate", out _));
            Assert.Equal(5, f.GetProperty("distinct").GetProperty("value").GetInt64());
        }

        using (var document = Parse(TestModels.Minimal([baseField with { Numeric = sketched, Temporal = temporalSketched, Distinct = new DistinctInfo(50000, true, 0.008125) }])))
        {
            var f = Field(document);
            Assert.True(f.GetProperty("numeric").GetProperty("approximate").GetBoolean());
            Assert.Equal(0.001, f.GetProperty("numeric").GetProperty("rankError").GetDouble());
            Assert.True(f.GetProperty("temporal").GetProperty("approximate").GetBoolean());
            Assert.True(f.GetProperty("distinct").GetProperty("approximate").GetBoolean());
            Assert.Equal(0.008125, f.GetProperty("distinct").GetProperty("relativeStdError").GetDouble());
        }
    }

    [Fact]
    public void Field_TopApproximateFalse_IsOmitted()
    {
        var exact = new NumericStats(DescriptorNumber.Int(1), DescriptorNumber.Int(2), 1.5, 0.5, TestModels.IntQuantiles(1), false);
        var top = new TopValues([new TopValue("a", 3)], false);
        var d = TestModels.Minimal([TestModels.FloatField(exact) with { Top = top }]) with { Options = ProfileOptions.Create(true, []) };
        using var document = Parse(d);
        Assert.False(Field(document).GetProperty("top").TryGetProperty("approximate", out _));
        Assert.Equal(1, Field(document).GetProperty("top").GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void OmissionsOutsideFields_AreUnchanged()
    {
        // The omissions are for table.fields[] only: nulls elsewhere (hints, format.dialect) are still written.
        var text = Text(TestModels.Minimal());
        Assert.Contains("\"magic\": null", text, StringComparison.Ordinal);
        Assert.Contains("\"dialect\": null", text, StringComparison.Ordinal);
        Assert.Contains("\"headerFields\": null", text, StringComparison.Ordinal);
    }

    // ---- one-line containers ----

    private static Descriptor WithHeaderFields(params string[] names)
    {
        var d = TestModels.Minimal();
        return d with { Hints = d.Hints! with { HeaderFields = names } };
    }

    [Theory]
    [InlineData(115, true)]
    [InlineData(116, true)]
    [InlineData(117, false)]
    public void Inline_ArrayAtTheThreshold_OneLineUpTo120Characters(int length, bool oneLine)
    {
        // ["<s>"] is length + 4 characters: 119, 120, 121.
        var name = new string('a', length);
        var text = Text(WithHeaderFields(name));
        var line = "\"headerFields\": [\"" + name + "\"]";
        if (oneLine)
        {
            Assert.Contains(line, text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("\n    \"headerFields\": [\n      \"" + name + "\"\n    ]", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Inline_ObjectAtTheThreshold_CountsKeysSeparatorsAndBraces()
    {
        // { "<key>": 1 } is key + 9 characters: 119, 120, 121. The key goes through a nullTokens map of a field.
        string Written(int keyLength)
        {
            var key = new string('k', keyLength);
            var numeric = new NumericStats(DescriptorNumber.Int(1), DescriptorNumber.Int(1), 1.0, null, TestModels.IntQuantiles(1), false);
            var field = TestModels.FloatField(numeric) with { NullTokens = CountMap.FromOrdinal([new(key, 1)]) };
            return Text(TestModels.Minimal([field]));
        }

        Assert.Contains("\"nullTokens\": { \"" + new string('k', 110) + "\": 1 }", Written(110), StringComparison.Ordinal);
        Assert.Contains("\"nullTokens\": { \"" + new string('k', 111) + "\": 1 }", Written(111), StringComparison.Ordinal);
        Assert.Contains("\"nullTokens\": {\n", Written(112), StringComparison.Ordinal);
    }

    // Three elements: ["a", "b", "c"] is the three names, two quotes each, 2 + 2 * 2 separator characters and the
    // brackets: sum of the name lengths + 12 characters. The ", " separators are part of the count.
    [Theory]
    [InlineData(35, true)] // 107 + 12 = 119
    [InlineData(36, true)] // 108 + 12 = 120
    [InlineData(37, false)] // 109 + 12 = 121
    public void Inline_ArrayOfThree_SeparatorsAreCounted(int lastLength, bool oneLine)
    {
        var names = new[] { new string('a', 36), new string('b', 36), new string('c', lastLength) };
        var text = Text(WithHeaderFields(names));
        var inlineForm = "\"headerFields\": [\"" + names[0] + "\", \"" + names[1] + "\", \"" + names[2] + "\"]";
        Assert.Equal(oneLine, text.Contains(inlineForm, StringComparison.Ordinal));
        if (!oneLine)
        {
            Assert.Contains("\n    \"headerFields\": [\n      \"" + names[0] + "\",\n      \"" + names[1] + "\",\n      \"" + names[2] + "\"\n    ]", text, StringComparison.Ordinal);
        }
    }

    // Three members: { "k": 1, "k": 2, "k": 3 } is the sum of the key lengths + 23 characters.
    [Theory]
    [InlineData(32, true)] // 96 + 23 = 119
    [InlineData(33, true)] // 97 + 23 = 120
    [InlineData(34, false)] // 98 + 23 = 121
    public void Inline_ObjectOfThree_SeparatorsAreCounted(int lastLength, bool oneLine)
    {
        var keys = new[] { "a" + new string('k', 31), "b" + new string('k', 31), "c" + new string('k', lastLength - 1) };
        var numeric = new NumericStats(DescriptorNumber.Int(1), DescriptorNumber.Int(1), 1.0, null, TestModels.IntQuantiles(1), false);
        var field = TestModels.FloatField(numeric) with { NullTokens = CountMap.FromOrdinal([new(keys[0], 1), new(keys[1], 2), new(keys[2], 3)]) };
        var text = Text(TestModels.Minimal([field]));
        var inlineForm = "\"nullTokens\": { \"" + keys[0] + "\": 1, \"" + keys[1] + "\": 2, \"" + keys[2] + "\": 3 }";
        Assert.Equal(oneLine, text.Contains(inlineForm, StringComparison.Ordinal));
        if (!oneLine)
        {
            Assert.Contains("\"nullTokens\": {\n", text, StringComparison.Ordinal);
        }
    }

    // An escape counts at its written length: U+0001 is the 6 characters \u0001, so a name that fits as literal
    // characters (one for the control character) can still be too long as written.
    [Theory]
    [InlineData(110, true)] // 110 + 6 + 4 = 120 written (115 if the escape counted as one)
    [InlineData(111, false)] // 111 + 6 + 4 = 121 written (116 if the escape counted as one)
    public void Inline_EscapeCountsAtItsWrittenLength(int plainLength, bool oneLine)
    {
        var name = new string('a', plainLength) + "\u0001";
        var text = Text(WithHeaderFields(name));
        var written = new string('a', plainLength) + "\\u0001";
        Assert.Equal(oneLine, text.Contains("\"headerFields\": [\"" + written + "\"]", StringComparison.Ordinal));
        if (!oneLine)
        {
            Assert.Contains("\n    \"headerFields\": [\n      \"" + written + "\"\n    ]", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Inline_CountsCharactersNotBytes_AndKeepsEscapes()
    {
        // 116 literal U+00E9 characters: 232 bytes, 120 characters. One line.
        var wide = new string((char)0x00E9, 116);
        Assert.Contains("\"headerFields\": [\"" + wide + "\"]", Text(WithHeaderFields(wide)), StringComparison.Ordinal);

        // The escape stays as the canonical text: a quote, a backslash and a control character are 2 and 6 characters.
        var escaped = Text(WithHeaderFields("a\"b\\c\u0001"));
        Assert.Contains("\"headerFields\": [\"a\\\"b\\\\c\\u0001\"]", escaped, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_NestedContainers_AreOneLineWhenTheWholeFits()
    {
        var numeric = new NumericStats(DescriptorNumber.Int(1), DescriptorNumber.Int(1), 1.0, null, TestModels.IntQuantiles(1), false);
        var text = Text(TestModels.Minimal([TestModels.FloatField(numeric)]));
        var wide = new NumericStats(
            DescriptorNumber.Real(-19.9612),
            DescriptorNumber.Real(79.8893),
            29.858331749999998,
            28.834801845478854,
            TestModels.IntQuantiles(1),
            false);
        var wideText = Text(TestModels.Minimal([TestModels.FloatField(wide)]));

        Assert.Contains("\"dominant\": { \"type\": \"float\", \"share\": 1 }", text, StringComparison.Ordinal);
        Assert.Contains("\"shapes\": [{ \"mask\": \"9\", \"count\": 1 }]", text, StringComparison.Ordinal);
        Assert.Contains("\"quantiles\": { \"p01\": 1, \"p05\": 1, \"p25\": 1, \"p50\": 1, \"p75\": 1, \"p95\": 1, \"p99\": 1 }", text, StringComparison.Ordinal);
        Assert.Contains("\"tool\": { \"name\": \"squalify\", \"version\": \"0.1.0\" }", text, StringComparison.Ordinal);

        // The numeric object itself is over 120 characters on one line, so its members stay on their own lines.
        Assert.Contains("\"numeric\": {\n", wideText, StringComparison.Ordinal);
        Assert.Contains("\"quantiles\": { \"p01\": 1,", wideText, StringComparison.Ordinal);
        Assert.StartsWith("{\n  \"schemaVersion\": \"1.0\",\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_EmptyContainers_AreWrittenInline()
    {
        var text = Text(TestModels.Minimal());
        Assert.Contains("\"issues\": []", text, StringComparison.Ordinal);
        Assert.Contains("\"regions\": []", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_FullBinaryStdf_LongArraysStayMultiLine()
    {
        // binary.histogram has 256 numbers: it is one element per line.
        var text = Text(TestModels.FullBinaryStdf());
        Assert.Contains("\"histogram\": [\n", text, StringComparison.Ordinal);
    }
}
