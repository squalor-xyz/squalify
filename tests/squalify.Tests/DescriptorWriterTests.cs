// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

public class DescriptorWriterTests
{
    private static string Text(Descriptor descriptor)
    {
        return new UTF8Encoding(false, true).GetString(DescriptorWriter.ToBytes(descriptor));
    }

    [Fact]
    public void Writer_KeyOrder_MatchesSchemaDoc()
    {
        // The goldens are hand-written from docs/descriptor.md, not produced by the writer.
        Golden.Verify("full-delimited", TestModels.FullDelimited());
        Golden.Verify("full-binary-stdf", TestModels.FullBinaryStdf());
    }

    [Fact]
    public void Writer_UsesLfAndTrailingNewline_OnEveryOs()
    {
        foreach (var descriptor in new[] { TestModels.Minimal(), TestModels.FullDelimited(), TestModels.FullBinaryStdf() })
        {
            var bytes = DescriptorWriter.ToBytes(descriptor);
            Assert.NotEmpty(bytes);
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.Equal((byte)'\n', bytes[^1]);
            Assert.Equal((byte)'}', bytes[^2]);
        }
    }

    [Fact]
    public void Writer_HasNoBom_AndIsValidUtf8()
    {
        var bytes = DescriptorWriter.ToBytes(TestModels.FullDelimited());
        Assert.Equal((byte)'{', bytes[0]);
        _ = new UTF8Encoding(false, true).GetString(bytes);
    }

    [Fact]
    public void Writer_NonFiniteAndNegativeZero()
    {
        var numeric = new NumericStats(
            DescriptorNumber.Real(-0.0),
            DescriptorNumber.Real(double.PositiveInfinity),
            double.NaN,
            double.NegativeInfinity,
            new Quantiles<DescriptorNumber>(
                DescriptorNumber.Real(0.0),
                DescriptorNumber.Real(-0.0),
                DescriptorNumber.Int(0),
                DescriptorNumber.Real(double.NaN),
                DescriptorNumber.Real(double.PositiveInfinity),
                DescriptorNumber.Real(double.NegativeInfinity),
                DescriptorNumber.Real(1.5)),
            false);
        var text = Text(TestModels.Minimal([TestModels.FloatField(numeric)]));

        Assert.Contains("\"numeric\": {\n          \"min\": 0,\n          \"max\": \"Infinity\",\n          \"mean\": \"NaN\",\n          \"stddev\": \"-Infinity\",", text);
        Assert.Contains(
            "\"quantiles\": {\n            \"p01\": 0,\n            \"p05\": 0,\n            \"p25\": 0,\n            \"p50\": \"NaN\",\n            \"p75\": \"Infinity\",\n            \"p95\": \"-Infinity\",\n            \"p99\": 1.5\n          }",
            text);
        Assert.DoesNotContain("-0", text);
    }

    [Fact]
    public void Writer_NonAsciiHeader_EmittedLiterally()
    {
        var name = "Isat(µA)Ω";
        var numeric = new NumericStats(
            DescriptorNumber.Int(1),
            DescriptorNumber.Int(1),
            1,
            0,
            new Quantiles<DescriptorNumber>(
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1)),
            false);
        var bytes = DescriptorWriter.ToBytes(TestModels.Minimal([TestModels.FloatField(numeric, name)]));
        var text = new UTF8Encoding(false, true).GetString(bytes);

        Assert.Contains("\"name\": \"Isat(µA)Ω\"", text);
        Assert.DoesNotContain("\\u00B5", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\u03A9", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0xC2, 0xB5 }) >= 0, "micro sign must be written as UTF-8 C2 B5");
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0xCE, 0xA9 }) >= 0, "omega must be written as UTF-8 CE A9");
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void Writer_SameModel_SameBytes_UnderDeDeAndTrTrCulture(string cultureName)
    {
        var culture = new CultureInfo(cultureName);
        if (cultureName == "de-DE")
        {
            // Guards against a vacuous pass when the host runs with invariant globalization.
            Assert.Equal(",", culture.NumberFormat.NumberDecimalSeparator);
        }

        var models = new[] { TestModels.FullDelimited(), TestModels.FullBinaryStdf() };
        var expected = models.Select(DescriptorWriter.ToBytes).ToArray();
        Assert.All(expected, static bytes => Assert.NotEmpty(bytes));

        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            var actual = models.Select(DescriptorWriter.ToBytes).ToArray();
            for (var i = 0; i < models.Length; i++)
            {
                Assert.Equal(expected[i], actual[i]);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void Writer_Minimal_OmitsOptionalMembers()
    {
        var text = Text(TestModels.Minimal());
        foreach (var key in new[] { "\"table\"", "\"sections\"", "\"binary\"", "\n  \"stdf\":", "\"headerConvention\": {", "\"maxLineLength\"" })
        {
            Assert.DoesNotContain(key, text);
        }

        Assert.Contains("\"schemaVersion\": \"1.0\"", text);
        Assert.StartsWith("{\n  \"schemaVersion\": \"1.0\",\n  \"tool\": {", text);
    }

    [Fact]
    public void Writer_ToStream_WritesTheSameBytes()
    {
        var descriptor = TestModels.FullDelimited();
        using var stream = new MemoryStream();
        DescriptorWriter.Write(descriptor, stream);
        Assert.Equal(DescriptorWriter.ToBytes(descriptor), stream.ToArray());
    }

    [Fact]
    public void Writer_ToStream_WrapsIoFailureInSqualifyException()
    {
        using var stream = new ThrowingStream();
        var error = Assert.Throws<SqualifyException>(() => DescriptorWriter.Write(TestModels.Minimal(), stream));
        Assert.IsType<IOException>(error.InnerException);
    }

    [Fact]
    public void Writer_LoneSurrogate_IsEscapedAsReplacementCharacter()
    {
        // Text decoded from bytes never holds a lone surrogate, but the writer must still be deterministic.
        var numeric = new NumericStats(
            DescriptorNumber.Int(1),
            DescriptorNumber.Int(1),
            1,
            0,
            new Quantiles<DescriptorNumber>(
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1)),
            false);
        var model = TestModels.Minimal([TestModels.FloatField(numeric, "bad" + (char)0xD800 + "name")]);
        var text = Text(model);
        Assert.Contains("\"name\": \"bad\\uFFFDname\"", text);
        Assert.Equal(DescriptorWriter.ToBytes(model), DescriptorWriter.ToBytes(model));
    }

    [Fact]
    public void Writer_EscapesControlAndSupplementaryCharacters_ByDocumentedRules()
    {
        // JavaScriptEncoder.UnsafeRelaxedJsonEscaping: BMP letters stay literal; controls, a few invisible characters and
        // everything outside the BMP are escaped (surrogate pairs for U+10000 and up). docs/descriptor.md documents this.
        var numeric = new NumericStats(
            DescriptorNumber.Int(1),
            DescriptorNumber.Int(1),
            1,
            0,
            new Quantiles<DescriptorNumber>(
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1)),
            false);
        var name = "a" + char.ConvertFromUtf32(0x1F600) + "\t\n" + (char)0x2028 + (char)0x7F + "\"\\<>&e" + (char)0xE9;
        var text = Text(TestModels.Minimal([TestModels.FloatField(numeric, name)]));
        Assert.Contains("\"name\": \"a\\uD83D\\uDE00\\t\\n\\u2028\\u007F\\\"\\\\<>&e" + (char)0xE9 + "\"", text);
    }

    [Fact]
    public void Writer_UnassignedBmpCodePoint_IsEscaped()
    {
        // U+0378 is unassigned in the Unicode tables of .NET 10.0.x, so the encoder escapes it. A runtime
        // upgrade that changes the tables can change this output; docs/design.md treats that as an output change.
        var numeric = new NumericStats(
            DescriptorNumber.Int(1),
            DescriptorNumber.Int(1),
            1,
            0,
            new Quantiles<DescriptorNumber>(
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1),
                DescriptorNumber.Int(1)),
            false);
        var text = Text(TestModels.Minimal([TestModels.FloatField(numeric, "a" + (char)0x378 + "b")]));
        Assert.Contains("\"name\": \"a\\u0378b\"", text);
    }

    [Fact]
    public void Writer_IssueWithBothLinesAndOffsets_ThrowsSqualifyException()
    {
        var model = TestModels.Minimal() with { Issues = [new Issue("both", 2, FirstLines: [1], Offsets: [5])] };
        var error = Assert.Throws<SqualifyException>(() => DescriptorWriter.ToBytes(model));
        Assert.Contains("both", error.Message);
    }

    [Fact]
    public void Writer_FailedWrite_LeavesStreamUntouched()
    {
        var model = TestModels.Minimal() with { Issues = [new Issue("both", 2, FirstLines: [1], Offsets: [5])] };
        using var stream = new MemoryStream();
        Assert.Throws<SqualifyException>(() => DescriptorWriter.Write(model, stream));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Writer_NullEntriesInValuesLists_AreWrittenAsNull()
    {
        var model = TestModels.Minimal() with
        {
            Layout = new LayoutSection(
            [
                new Region("preamble", 1, 2, 0, 10, Keys: ["a", "b"], Values: ["A12", null]),
                new Region(
                    "annotation",
                    3,
                    3,
                    10,
                    20,
                    Rows: [new AnnotationRow(3, "Upper", 0, CountMap.Empty, CountMap.Empty, "float", [null, "2"])]),
            ]),
            Sections = [new KeyValueSection("S", 1, ["k", "j"], [null, "v"])],
        };
        using var doc = JsonDocument.Parse(DescriptorWriter.ToBytes(model));
        var regions = doc.RootElement.GetProperty("layout").GetProperty("regions");
        var presentValues = regions[0].GetProperty("values");
        Assert.Equal("A12", presentValues[0].GetString());
        Assert.Equal(JsonValueKind.Null, presentValues[1].ValueKind);
        var cells = regions[1].GetProperty("rows")[0].GetProperty("values");
        Assert.Equal(JsonValueKind.Null, cells[0].ValueKind);
        Assert.Equal("2", cells[1].GetString());
        var sectionValues = doc.RootElement.GetProperty("sections")[0].GetProperty("values");
        Assert.Equal(JsonValueKind.Null, sectionValues[0].ValueKind);
        Assert.Equal("v", sectionValues[1].GetString());
    }

    [Fact]
    public void Writer_DialectsOtherThanDelimited_KeyOrder()
    {
        var touchstone = Text(
            TestModels.Minimal() with
            {
                Format = new FormatSection(
                    "touchstone",
                    new TouchstoneDialect(
                        "2.0",
                        2,
                        "GHz",
                        "S",
                        "MA",
                        50,
                        "21_12",
                        "Full",
                        ["Number of Ports", "Reference"],
                        CountMap.FromNumeric([new(9, 2), new(10, 1), new(2, 5)]),
                        true),
                    0.9),
            });
        Assert.Contains(
            "    \"dialect\": {\n      \"version\": \"2.0\",\n      \"ports\": 2,\n      \"frequencyUnit\": \"GHz\",\n      \"parameter\": \"S\",\n      \"dataFormat\": \"MA\",\n      \"referenceOhms\": 50,\n      \"twoPortOrder\": \"21_12\",\n      \"matrixFormat\": \"Full\",\n      \"keywords\": [\n        \"Number of Ports\",\n        \"Reference\"\n      ],\n      \"valuesPerLine\": {\n        \"2\": 5,\n        \"9\": 2,\n        \"10\": 1\n      },\n      \"noise\": true\n    },",
            touchstone);

        var keyValue = Text(TestModels.Minimal() with { Format = new FormatSection("keyValue", new KeyValueDialect("=", true), 0.6) });
        Assert.Contains("    \"dialect\": {\n      \"separator\": \"=\",\n      \"hasSections\": true\n    },", keyValue);

        var parquet = Text(
            TestModels.Minimal() with
            {
                Format = new FormatSection(
                    "parquet",
                    new ParquetDialect(2, "parquet-cpp", 100, new RowGroupInfo(2, new MinMax(40, 60), ["SNAPPY"], ["PLAIN", "RLE"]), ["writer"]),
                    1.0),
            });
        Assert.Contains(
            "    \"dialect\": {\n      \"formatVersion\": 2,\n      \"createdBy\": \"parquet-cpp\",\n      \"numRows\": 100,\n      \"rowGroups\": {\n        \"count\": 2,\n        \"rows\": {\n          \"min\": 40,\n          \"max\": 60\n        },\n        \"compression\": [\n          \"SNAPPY\"\n        ],\n        \"encodings\": [\n          \"PLAIN\",\n          \"RLE\"\n        ]\n      },\n      \"kvKeys\": [\n        \"writer\"\n      ]\n    },",
            parquet);

        var noDialect = Text(TestModels.Minimal());
        Assert.Contains("    \"dialect\": null,", noDialect);
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new IOException("disk full");
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            throw new IOException("disk full");
        }

        public override void WriteByte(byte value)
        {
            throw new IOException("disk full");
        }
    }
}
