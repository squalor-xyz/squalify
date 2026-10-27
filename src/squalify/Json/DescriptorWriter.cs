// SPDX-License-Identifier: Apache-2.0
using System.Text.Encodings.Web;
using System.Text.Json;

namespace squalor.squalify;

/// <summary>
/// Writes a <see cref="Descriptor"/> as canonical JSON: explicit key order (the order in <c>docs/descriptor.md</c>),
/// two-space indentation, LF line endings, UTF-8 without a BOM, and a trailing LF. Printable BMP text is written
/// literally; control characters, non-BMP characters and a few invisible code points are escaped (see
/// <c>docs/descriptor.md</c>, "Canonical form"). The same model always gives the same bytes on every OS, CPU and
/// culture. Nothing is sorted here: producers build every collection sorted by its documented key.
/// </summary>
public static class DescriptorWriter
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serializes a descriptor to canonical UTF-8 bytes.</summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <returns>The bytes, ending in a single LF.</returns>
    /// <exception cref="SqualifyException">The descriptor cannot be written, for example because an <see cref="Issue"/> has both lines and offsets.</exception>
    public static byte[] ToBytes(Descriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        try
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, Options))
            {
                Write(writer, descriptor);
            }

            buffer.WriteByte((byte)'\n');
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new SqualifyException("Could not write the descriptor: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Writes a descriptor to a stream as canonical UTF-8 bytes. The descriptor is serialized in memory first, so a
    /// descriptor that cannot be written leaves the stream untouched.
    /// </summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <param name="stream">The destination. It is not closed.</param>
    /// <exception cref="SqualifyException">The descriptor cannot be written, or the stream failed.</exception>
    public static void Write(Descriptor descriptor, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var bytes = ToBytes(descriptor);
        try
        {
            stream.Write(bytes);
            stream.Flush();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            throw new SqualifyException("Could not write the descriptor: " + ex.Message, ex);
        }
    }

    private static void Write(Utf8JsonWriter w, Descriptor d)
    {
        w.WriteStartObject();
        w.WriteString("schemaVersion", Descriptor.SchemaVersion);
        w.WritePropertyName("tool");
        Write(w, d.Tool);
        w.WritePropertyName("options");
        Write(w, d.Options);
        w.WritePropertyName("limits");
        Write(w, d.Limits);
        w.WritePropertyName("file");
        Write(w, d.File);
        w.WritePropertyName("encoding");
        Write(w, d.Encoding);
        w.WritePropertyName("format");
        Write(w, d.Format);
        w.WritePropertyName("kind");
        Write(w, d.Kind);
        w.WritePropertyName("layout");
        Write(w, d.Layout);
        Optional(w, "table", d.Table, Write);
        OptionalList(w, "sections", d.Sections, Write);
        Optional(w, "binary", d.Binary, Write);
        Optional(w, "stdf", d.Stdf, Write);
        w.WritePropertyName("fingerprint");
        Write(w, d.Fingerprint);
        w.WritePropertyName("hints");
        Write(w, d.Hints);
        List(w, "issues", d.Issues, Write);
        List(w, "evidence", d.Evidence, Write);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, ToolSection v)
    {
        w.WriteStartObject();
        w.WriteString("name", v.Name);
        w.WriteString("version", v.Version);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, ProfileOptions v)
    {
        w.WriteStartObject();
        w.WriteBoolean("includeValues", v.IncludeValues);
        Strings(w, "includeValuesFor", v.IncludeValuesFor);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, Limits v)
    {
        w.WriteStartObject();
        w.WriteNumber("distinctPerField", v.DistinctPerField);
        w.WriteNumber("quantilePerField", v.QuantilePerField);
        w.WriteNumber("shapesPerField", v.ShapesPerField);
        w.WriteNumber("regions", v.Regions);
        w.WriteNumber("headerSample", v.HeaderSample);
        w.WriteNumber("runawayQuote", v.RunawayQuote);
        w.WriteNumber("fieldCountRuns", v.FieldCountRuns);
        w.WriteNumber("rowClasses", v.RowClasses);
        w.WritePropertyName("charsetTolerance");
        w.WriteStartObject();
        w.WriteNumber("invalidSequences", v.CharsetTolerance.InvalidSequences);
        w.WriteNumber("perBytes", v.CharsetTolerance.PerBytes);
        w.WriteEndObject();
        w.WriteNumber("stdfTests", v.StdfTests);
        w.WritePropertyName("budget");
        w.WriteStartObject();
        w.WriteNumber("quantileValues", v.Budget.QuantileValues);
        w.WriteNumber("distinctValues", v.Budget.DistinctValues);
        w.WriteEndObject();
        w.WriteNumber("nullTokens", v.NullTokens);
        w.WriteNumber("unitTable", v.UnitTable);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, FileSection v)
    {
        w.WriteStartObject();
        w.WriteString("name", v.Name);
        w.WriteNumber("size", v.Size);
        w.WriteString("sha256", v.Sha256);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, EncodingSection v)
    {
        w.WriteStartObject();
        w.WriteString("kind", v.Kind);
        w.WriteString("charset", v.Charset);
        w.WriteString("bom", v.Bom);
        w.WritePropertyName("lineEndings");
        w.WriteStartObject();
        w.WriteNumber("crlf", v.LineEndings.Crlf);
        w.WriteNumber("lf", v.LineEndings.Lf);
        w.WriteNumber("cr", v.LineEndings.Cr);
        w.WriteEndObject();
        w.WriteNumber("invalidBytes", v.InvalidBytes);
        w.WriteNumber("controlBytes", v.ControlBytes);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, FormatSection v)
    {
        w.WriteStartObject();
        w.WriteString("family", v.Family);
        Nullable(w, "dialect", v.Dialect, Write);
        Double(w, "score", v.Score);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, Dialect v)
    {
        switch (v)
        {
            case DelimitedDialect d:
                w.WriteStartObject();
                w.WriteString("delimiter", d.Delimiter);
                w.WriteString("quote", d.Quote);
                w.WriteBoolean("hasHeader", d.HasHeader);
                w.WriteBoolean("unitsRow", d.UnitsRow);
                w.WriteString("decimalSeparator", d.DecimalSeparator);
                w.WritePropertyName("padding");
                w.WriteStartObject();
                w.WriteBoolean("trim", d.Padding.Trim);
                Double(w, "paddedShare", d.Padding.PaddedShare);
                w.WriteBoolean("constantLineLength", d.Padding.ConstantLineLength);
                NullableNumber(w, "lineLength", d.Padding.LineLength);
                w.WriteEndObject();
                w.WriteNumber("blocks", d.Blocks);
                w.WriteEndObject();
                break;
            case TouchstoneDialect t:
                w.WriteStartObject();
                w.WriteString("version", t.Version);
                w.WriteNumber("ports", t.Ports);
                w.WriteString("frequencyUnit", t.FrequencyUnit);
                w.WriteString("parameter", t.Parameter);
                w.WriteString("dataFormat", t.DataFormat);
                Double(w, "referenceOhms", t.ReferenceOhms);
                w.WriteString("twoPortOrder", t.TwoPortOrder);
                w.WriteString("matrixFormat", t.MatrixFormat);
                Strings(w, "keywords", t.Keywords);
                Map(w, "valuesPerLine", t.ValuesPerLine);
                w.WriteBoolean("noise", t.Noise);
                w.WriteEndObject();
                break;
            case KeyValueDialect k:
                w.WriteStartObject();
                w.WriteString("separator", k.Separator);
                w.WriteBoolean("hasSections", k.HasSections);
                w.WriteEndObject();
                break;
            case ParquetDialect p:
                w.WriteStartObject();
                w.WriteNumber("formatVersion", p.FormatVersion);
                w.WriteString("createdBy", p.CreatedBy);
                w.WriteNumber("numRows", p.NumRows);
                w.WritePropertyName("rowGroups");
                w.WriteStartObject();
                w.WriteNumber("count", p.RowGroups.Count);
                w.WritePropertyName("rows");
                Write(w, p.RowGroups.Rows);
                Strings(w, "compression", p.RowGroups.Compression);
                Strings(w, "encodings", p.RowGroups.Encodings);
                w.WriteEndObject();
                Strings(w, "kvKeys", p.KvKeys);
                w.WriteEndObject();
                break;
            case StdfDialect s:
                w.WriteStartObject();
                w.WriteNumber("cpuType", s.CpuType);
                w.WriteString("endianness", s.Endianness);
                w.WriteString("version", s.Version);
                w.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException("Unknown dialect type " + v.GetType().Name + ".");
        }
    }

    private static void Write(Utf8JsonWriter w, MinMax v)
    {
        w.WriteStartObject();
        w.WriteNumber("min", v.Min);
        w.WriteNumber("max", v.Max);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, KindLabel v)
    {
        w.WriteStartObject();
        w.WriteString("label", v.Label);
        w.WriteString("ruleId", v.RuleId);
        Double(w, "confidence", v.Confidence);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, LayoutSection v)
    {
        w.WriteStartObject();
        List(w, "regions", v.Regions, Write);
        Optional(w, "headerConvention", v.HeaderConvention, Write);
        OptionalNumber(w, "maxLineLength", v.MaxLineLength);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, Region v)
    {
        w.WriteStartObject();
        w.WriteString("kind", v.Kind);
        OptionalNumber(w, "index", v.Index);
        OptionalString(w, "label", v.Label);
        Pair(w, "lines", v.FirstLine, v.LastLine);
        Pair(w, "bytes", v.StartByte, v.EndByte);
        Optional(w, "lineKinds", v.LineKinds, WriteMap);
        OptionalStrings(w, "keys", v.Keys);
        OptionalStrings(w, "values", v.Values);
        OptionalList(w, "rows", v.Rows, Write);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, AnnotationRow v)
    {
        w.WriteStartObject();
        w.WriteNumber("line", v.Line);
        w.WriteString("label", v.Label);
        w.WriteNumber("blankFields", v.BlankFields);
        Map(w, "nullTokens", v.NullTokens);
        Map(w, "typeVotes", v.TypeVotes);
        w.WriteString("type", v.Type);
        OptionalStrings(w, "values", v.Values);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, HeaderConventionInfo v)
    {
        w.WriteStartObject();
        w.WriteString("dominant", v.Dominant);
        Double(w, "share", v.Share);
        Map(w, "counts", v.Counts);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, TableSection v)
    {
        w.WriteStartObject();
        w.WriteNumber("rows", v.Rows);
        w.WritePropertyName("fieldCount");
        Write(w, v.FieldCount);
        Optional(w, "rowClasses", v.RowClasses, Write);
        List(w, "fields", v.Fields, Write);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, RowClassesInfo v)
    {
        w.WriteStartObject();
        w.WriteNumber("count", v.Count);
        w.WriteNumber("transitions", v.Transitions);
        w.WriteNumber("allNullRows", v.AllNullRows);
        w.WriteNumber("truncated", v.Truncated);
        List(w, "classes", v.Classes, Write);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, RowClass v)
    {
        w.WriteStartObject();
        w.WriteString("id", v.Id);
        w.WriteNumber("count", v.Count);
        List(w, "fields", v.Fields, static (writer, range) => Pair(writer, range.First, range.Last));
        w.WriteNumber("firstLine", v.FirstLine);
        w.WriteNumber("lastLine", v.LastLine);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, FieldProfile v)
    {
        w.WriteStartObject();
        w.WriteNumber("index", v.Index);
        w.WriteString("name", v.Name);
        OptionalString(w, "nameHex", v.NameHex);
        Nullable(
            w,
            "nameParts",
            v.NameParts,
            static (writer, parts) =>
            {
                writer.WriteStartObject();
                writer.WriteString("base", parts.Base);
                writer.WriteString("unit", parts.Unit);
                writer.WriteString("convention", parts.Convention);
                writer.WriteEndObject();
            });
        w.WriteString("type", v.Type);
        w.WritePropertyName("dominant");
        w.WriteStartObject();
        w.WriteString("type", v.Dominant.Type);
        Double(w, "share", v.Dominant.Share);
        w.WriteEndObject();
        Map(w, "typeVotes", v.TypeVotes);
        Nullable(w, "patterns", v.Patterns, WriteMap);
        w.WriteNumber("count", v.Count);
        w.WriteNumber("nulls", v.Nulls);
        w.WriteNumber("nullsExcludingAllNullRows", v.NullsExcludingAllNullRows);
        Nullable(w, "nullTokens", v.NullTokens, WriteMap);
        Nullable(
            w,
            "flags",
            v.Flags,
            static (writer, flags) =>
            {
                writer.WriteStartObject();
                writer.WriteNumber("leadingZero", flags.LeadingZero);
                writer.WriteNumber("commaDecimalLike", flags.CommaDecimalLike);
                writer.WriteNumber("padded", flags.Padded);
                writer.WriteEndObject();
            });
        Nullable(w, "distinct", v.Distinct, Write);
        Nullable(w, "numeric", v.Numeric, Write);
        Nullable(w, "temporal", v.Temporal, Write);
        w.WriteString("statsWithheld", v.StatsWithheld);
        Nullable(w, "length", v.Length, Write);
        if (v.Shapes is null)
        {
            w.WriteNull("shapes");
        }
        else
        {
            List(w, "shapes", v.Shapes, Write);
        }

        w.WriteString("monotonic", v.Monotonic);
        NullableBoolean(w, "constant", v.Constant);
        NullableBoolean(w, "unique", v.Unique);
        w.WriteString("sourceType", v.SourceType);
        w.WriteString("unit", v.Unit);
        Nullable(w, "top", v.Top, Write);
        Nullable(w, "text", v.Text, Write);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, DistinctInfo v)
    {
        w.WriteStartObject();
        NullableNumber(w, "value", v.Value);
        if (v.Approximate is { } approximate)
        {
            w.WriteBoolean("approximate", approximate);
        }

        if (v.ErrorBound is { } errorBound)
        {
            Double(w, "errorBound", errorBound);
        }

        OptionalNumber(w, "exceeds", v.Exceeds);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, NumericStats v)
    {
        w.WriteStartObject();
        Number(w, "min", v.Min);
        Number(w, "max", v.Max);
        if (v.Mean is { } mean)
        {
            Double(w, "mean", mean);
        }
        else
        {
            w.WriteNull("mean");
        }

        if (v.Stddev is { } stddev)
        {
            Double(w, "stddev", stddev);
        }
        else
        {
            w.WriteNull("stddev");
        }

        Nullable(w, "quantiles", v.Quantiles, static (writer, quantiles) => WriteQuantiles(writer, quantiles, static (inner, value) => Number(inner, value)));
        NullableBoolean(w, "approximate", v.Approximate);
        OptionalNumber(w, "exceeds", v.Exceeds);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, TemporalStats v)
    {
        w.WriteStartObject();
        Strings(w, "orders", v.Orders);
        w.WriteBoolean("ambiguous", v.Ambiguous);
        w.WriteString("clock", v.Clock);
        w.WriteString("min", v.Min);
        w.WriteString("max", v.Max);
        Nullable(
            w,
            "quantiles",
            v.Quantiles,
            static (writer, quantiles) => WriteQuantiles(writer, quantiles, static (inner, text) => inner.WriteStringValue(text)));
        OptionalNumber(w, "exceeds", v.Exceeds);
        w.WriteEndObject();
    }

    private static void WriteQuantiles<T>(Utf8JsonWriter w, Quantiles<T> q, Action<Utf8JsonWriter, T> writeValue)
    {
        w.WriteStartObject();
        w.WritePropertyName("p01");
        writeValue(w, q.P01);
        w.WritePropertyName("p05");
        writeValue(w, q.P05);
        w.WritePropertyName("p25");
        writeValue(w, q.P25);
        w.WritePropertyName("p50");
        writeValue(w, q.P50);
        w.WritePropertyName("p75");
        writeValue(w, q.P75);
        w.WritePropertyName("p95");
        writeValue(w, q.P95);
        w.WritePropertyName("p99");
        writeValue(w, q.P99);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, LengthStats v)
    {
        w.WriteStartObject();
        w.WriteNumber("min", v.Min);
        w.WriteNumber("max", v.Max);
        Double(w, "mean", v.Mean);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, Shape v)
    {
        w.WriteStartObject();
        w.WriteString("mask", v.Mask);
        w.WriteNumber("count", v.Count);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, TopValues v)
    {
        w.WriteStartObject();
        List(w, "items", v.Items, Write);
        w.WriteBoolean("approximate", v.Approximate);
        OptionalNumber(w, "maxError", v.MaxError);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, TopValue v)
    {
        w.WriteStartObject();
        w.WriteString("value", v.Value);
        w.WriteNumber("count", v.Count);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, TextStats v)
    {
        w.WriteStartObject();
        w.WriteString("min", v.Min);
        w.WriteString("max", v.Max);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, KeyValueSection v)
    {
        w.WriteStartObject();
        w.WriteString("name", v.Name);
        w.WriteNumber("line", v.Line);
        Strings(w, "keys", v.Keys);
        OptionalStrings(w, "values", v.Values);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, BinarySection v)
    {
        w.WriteStartObject();
        w.WritePropertyName("bytes");
        w.WriteStartObject();
        List(w, "histogram", v.Bytes.Histogram, static (writer, count) => writer.WriteNumberValue(count));
        w.WriteNumber("nul", v.Bytes.Nul);
        w.WriteNumber("printableAscii", v.Bytes.PrintableAscii);
        w.WriteNumber("high", v.Bytes.High);
        Double(w, "entropy", v.Bytes.Entropy);
        w.WriteEndObject();
        if (v.Blocks is { } blocks)
        {
            w.WritePropertyName("blocks");
            w.WriteStartObject();
            w.WriteNumber("count", blocks.Count);
            Double(w, "min", blocks.Min);
            Double(w, "max", blocks.Max);
            Double(w, "mean", blocks.Mean);
            w.WriteEndObject();
        }

        OptionalList(w, "segments", v.Segments, static (writer, entropy) => CanonicalNumber.Write(writer, entropy));
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, StdfSection v)
    {
        w.WriteStartObject();
        List(w, "records", v.Records, Write);
        Strings(w, "firstOccurrenceOrder", v.FirstOccurrenceOrder);
        w.WriteBoolean("farFirst", v.FarFirst);
        w.WriteBoolean("mrrLast", v.MrrLast);
        List(w, "tests", v.Tests, Write);
        w.WriteNumber("testsTruncated", v.TestsTruncated);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, StdfRecordCount v)
    {
        w.WriteStartObject();
        w.WriteString("name", v.Name);
        w.WriteNumber("typ", v.Typ);
        w.WriteNumber("sub", v.Sub);
        w.WriteNumber("count", v.Count);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, StdfTest v)
    {
        w.WriteStartObject();
        w.WriteNumber("testNum", v.TestNum);
        w.WriteString("kind", v.Kind);
        w.WriteString("name", v.Name);
        w.WriteNumber("occurrences", v.Occurrences);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, Fingerprint v)
    {
        w.WriteStartObject();
        w.WriteNumber("version", v.Version);
        w.WriteString("structure", v.Structure);
        w.WriteString("layout", v.Layout);
        w.WriteString("typed", v.Typed);
        w.WriteString("names", v.Names);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, Hints v)
    {
        w.WriteStartObject();
        w.WriteString("extension", v.Extension);
        w.WriteString("magic", v.Magic);
        List(w, "stableLines", v.StableLines, Write);
        NullableStrings(w, "headerFields", v.HeaderFields);
        NullableStrings(w, "headerPrefix", v.HeaderPrefix);
        if (v.NonNullFields is null)
        {
            w.WriteNull("nonNullFields");
        }
        else
        {
            List(w, "nonNullFields", v.NonNullFields, static (writer, index) => writer.WriteNumberValue(index));
        }

        Nullable(w, "fieldCount", v.FieldCount, Write);
        NullableNumber(w, "dataStartLine", v.DataStartLine);
        w.WriteString("delimiter", v.Delimiter);
        w.WriteString("headerConvention", v.HeaderConvention);
        NullableStrings(w, "parquetColumns", v.ParquetColumns);
        Nullable(
            w,
            "stdf",
            v.Stdf,
            static (writer, stdf) =>
            {
                writer.WriteStartObject();
                writer.WriteNumber("cpuType", stdf.CpuType);
                writer.WriteString("version", stdf.Version);
                writer.WriteEndObject();
            });
        w.WriteString("fileNameShape", v.FileNameShape);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, StableLine v)
    {
        w.WriteStartObject();
        w.WriteNumber("line", v.Line);
        w.WriteString("regex", v.Regex);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, Issue v)
    {
        w.WriteStartObject();
        if (v.FirstLines is not null && v.Offsets is not null)
        {
            throw new SqualifyException($"Issue '{v.Code}' has both firstLines and offsets; the schema allows only one.");
        }

        w.WriteString("code", v.Code);
        w.WriteNumber("count", v.Count);
        if (v.FirstLines is not null)
        {
            List(w, "firstLines", v.FirstLines, static (writer, line) => writer.WriteNumberValue(line));
        }

        if (v.Offsets is not null)
        {
            List(w, "offsets", v.Offsets, static (writer, offset) => writer.WriteNumberValue(offset));
        }

        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, Evidence v)
    {
        w.WriteStartObject();
        w.WriteString("stage", v.Stage);
        w.WriteString("topic", v.Topic);
        w.WriteString("chosen", v.Chosen);
        List(w, "candidates", v.Candidates, Write);
        w.WriteEndObject();
    }

    private static void Write(Utf8JsonWriter w, Candidate v)
    {
        w.WriteStartObject();
        w.WriteString("value", v.Value);
        Double(w, "score", v.Score);
        w.WriteString("reason", v.Reason);
        w.WriteEndObject();
    }

    private static void WriteMap(Utf8JsonWriter w, CountMap map)
    {
        w.WriteStartObject();
        foreach (var entry in map)
        {
            w.WriteNumber(entry.Key, entry.Value);
        }

        w.WriteEndObject();
    }

    private static void NullableBoolean(Utf8JsonWriter w, string name, bool? value)
    {
        if (value is { } flag)
        {
            w.WriteBoolean(name, flag);
        }
        else
        {
            w.WriteNull(name);
        }
    }

    private static void Map(Utf8JsonWriter w, string name, CountMap map)
    {
        w.WritePropertyName(name);
        WriteMap(w, map);
    }

    private static void Pair(Utf8JsonWriter w, string name, long first, long second)
    {
        w.WritePropertyName(name);
        Pair(w, first, second);
    }

    private static void Pair(Utf8JsonWriter w, long first, long second)
    {
        w.WriteStartArray();
        w.WriteNumberValue(first);
        w.WriteNumberValue(second);
        w.WriteEndArray();
    }

    private static void Double(Utf8JsonWriter w, string name, double value)
    {
        w.WritePropertyName(name);
        CanonicalNumber.Write(w, value);
    }

    private static void Number(Utf8JsonWriter w, string name, DescriptorNumber value)
    {
        w.WritePropertyName(name);
        Number(w, value);
    }

    private static void Number(Utf8JsonWriter w, DescriptorNumber value)
    {
        if (value.IsInteger)
        {
            CanonicalNumber.Write(w, value.IntegerValue);
        }
        else
        {
            CanonicalNumber.Write(w, value.RealValue);
        }
    }

    private static void NullableNumber(Utf8JsonWriter w, string name, long? value)
    {
        if (value is { } number)
        {
            w.WriteNumber(name, number);
        }
        else
        {
            w.WriteNull(name);
        }
    }

    private static void OptionalNumber(Utf8JsonWriter w, string name, long? value)
    {
        if (value is { } number)
        {
            w.WriteNumber(name, number);
        }
    }

    private static void OptionalString(Utf8JsonWriter w, string name, string? value)
    {
        if (value is not null)
        {
            w.WriteString(name, value);
        }
    }

    private static void Strings(Utf8JsonWriter w, string name, IReadOnlyList<string?> values)
    {
        List(w, name, values, static (writer, text) => writer.WriteStringValue(text));
    }

    private static void OptionalStrings(Utf8JsonWriter w, string name, IReadOnlyList<string?>? values)
    {
        if (values is not null)
        {
            Strings(w, name, values);
        }
    }

    private static void NullableStrings(Utf8JsonWriter w, string name, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            w.WriteNull(name);
        }
        else
        {
            Strings(w, name, values);
        }
    }

    private static void List<T>(Utf8JsonWriter w, string name, IReadOnlyList<T> items, Action<Utf8JsonWriter, T> write)
    {
        w.WritePropertyName(name);
        w.WriteStartArray();
        foreach (var item in items)
        {
            write(w, item);
        }

        w.WriteEndArray();
    }

    private static void OptionalList<T>(Utf8JsonWriter w, string name, IReadOnlyList<T>? items, Action<Utf8JsonWriter, T> write)
    {
        if (items is not null)
        {
            List(w, name, items, write);
        }
    }

    private static void Optional<T>(Utf8JsonWriter w, string name, T? value, Action<Utf8JsonWriter, T> write)
        where T : class
    {
        if (value is not null)
        {
            w.WritePropertyName(name);
            write(w, value);
        }
    }

    private static void Nullable<T>(Utf8JsonWriter w, string name, T? value, Action<Utf8JsonWriter, T> write)
        where T : class
    {
        if (value is null)
        {
            w.WriteNull(name);
        }
        else
        {
            w.WritePropertyName(name);
            write(w, value);
        }
    }
}
