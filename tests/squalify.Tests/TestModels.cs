// SPDX-License-Identifier: Apache-2.0
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>Descriptor models shared by the writer and golden tests.</summary>
internal static class TestModels
{
    /// <summary>A descriptor with only the always-present members, for tests that vary one member.</summary>
    public static Descriptor Minimal(IReadOnlyList<FieldProfile>? fields = null)
    {
        return new Descriptor(
            Tool: new ToolSection("squalify", "0.1.0"),
            Options: ProfileOptions.Create(false, Array.Empty<string>()),
            Limits: StandardLimits(),
            File: new FileSection("a.txt", 0, Sha256Empty),
            Encoding: new EncodingSection("empty", null, null, new LineEndings(0, 0, 0), 0, 0),
            Format: new FormatSection("empty", null, 1.0),
            Kind: new KindLabel("empty", "K13", 1.0),
            Layout: new LayoutSection(Array.Empty<Region>()),
            Fingerprint: new Fingerprint(1, "sha256:aa", "sha256:bb", "sha256:cc", "sha256:dd"),
            Hints: new Hints(null, null, Array.Empty<StableLine>(), null, null, null, null, null, null, null, null, null, "a{1}"),
            Issues: Array.Empty<Issue>(),
            Evidence: Array.Empty<Evidence>(),
            Table: fields is null ? null : new TableSection(1, new MinMax(1, 1), fields));
    }

    /// <summary>A bare numeric field whose statistics are supplied by the caller.</summary>
    public static FieldProfile FloatField(NumericStats numeric, string name = "x")
    {
        return new FieldProfile(
            Index: 0,
            Name: name,
            NameParts: new NameParts(name, null, "none"),
            Type: "float",
            Dominant: new DominantType("float", 1.0),
            TypeVotes: CountMap.FromOrdinal([new("float", 1)]),
            Count: 1,
            Nulls: 0,
            NullsExcludingAllNullRows: 0,
            NullTokens: CountMap.Empty,
            Flags: new FieldFlags(0, 0, 0),
            Distinct: new DistinctInfo(1, false),
            Length: new LengthStats(1, 1, 1.0),
            Shapes: [new Shape("9", 1)],
            Monotonic: "none",
            Constant: false,
            Unique: true,
            Numeric: numeric);
    }

    public static Limits StandardLimits()
    {
        return new Limits(10000, 100000, 64, 1000, 1000, 64, new CharsetTolerance(8, 65536), 10000, new Budget(10000000, 2000000), 1, 1);
    }

    public const string Sha256Empty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    /// <summary>A fully populated tabular descriptor (delimited family): every optional member of those sections is set.</summary>
    public static Descriptor FullDelimited()
    {
        var f0 = new FieldProfile(
            Index: 0,
            Name: "Frequency(MHz)",
            NameParts: new NameParts("Frequency", "MHz", "nameParenUnit"),
            Type: "float",
            Dominant: new DominantType("float", 0.9998),
            TypeVotes: CountMap.FromOrdinal([new("int", 2), new("float", 9998)]),
            Count: 10000,
            Nulls: 3,
            NullsExcludingAllNullRows: 2,
            NullTokens: CountMap.FromOrdinal([new("NA", 2), new("-", 1)]),
            Flags: new FieldFlags(0, 1, 4),
            Distinct: new DistinctInfo(81, false),
            Length: new LengthStats(3, 6, 5.2),
            Shapes: [new Shape("9{4}", 9120), new Shape("9{3}", 880), new Shape("other", 0)],
            Monotonic: "nonDecreasing",
            Constant: false,
            Unique: false,
            Patterns: CountMap.FromOrdinal([new("yyyy-MM-dd", 3)]),
            Numeric: new NumericStats(
                DescriptorNumber.Int(700),
                DescriptorNumber.Int(2700),
                1700,
                584.5,
                new Quantiles<DescriptorNumber>(
                    DescriptorNumber.Int(700),
                    DescriptorNumber.Int(800),
                    DescriptorNumber.Int(1200),
                    DescriptorNumber.Int(1700),
                    DescriptorNumber.Int(2200),
                    DescriptorNumber.Int(2600),
                    DescriptorNumber.Real(2700.5)),
                false),
            Unit: "MHz");

        var f1 = new FieldProfile(
            Index: 1,
            Name: "Start",
            NameParts: new NameParts("Start", null, "none"),
            Type: "datetime",
            Dominant: new DominantType("datetime", 1.0),
            TypeVotes: CountMap.FromOrdinal([new("datetime", 10000)]),
            Count: 10000,
            Nulls: 0,
            NullsExcludingAllNullRows: 0,
            NullTokens: CountMap.Empty,
            Flags: new FieldFlags(0, 0, 0),
            Distinct: new DistinctInfo(5000, true, 0.0081),
            Length: new LengthStats(19, 19, 19),
            Shapes: [new Shape("9{4}-9{2}-9{2}T9{2}:9{2}:9{2}", 10000)],
            Monotonic: "increasing",
            Constant: false,
            Unique: true,
            Temporal: new TemporalStats(
                ["YMD"],
                false,
                "24h",
                "2026-01-01T00:00:00",
                "2026-01-02T03:46:39",
                new Quantiles<string>(
                    "2026-01-01T00:00:00",
                    "2026-01-01T01:23:19",
                    "2026-01-01T06:56:39",
                    "2026-01-01T13:53:19",
                    "2026-01-01T20:49:59",
                    "2026-01-02T02:23:19",
                    "2026-01-02T03:46:39")));

        var f2 = new FieldProfile(
            Index: 2,
            Name: "Lot�",
            NameParts: new NameParts("Lot�", null, "none"),
            Type: "string",
            Dominant: new DominantType("string", 1.0),
            TypeVotes: CountMap.FromOrdinal([new("string", 10000)]),
            Count: 10000,
            Nulls: 0,
            NullsExcludingAllNullRows: 0,
            NullTokens: CountMap.Empty,
            Flags: new FieldFlags(0, 0, 0),
            Distinct: new DistinctInfo(null, Exceeds: 10000),
            Length: new LengthStats(4, 4, 4),
            Shapes: [new Shape("A{3}9", 10000)],
            Monotonic: "none",
            Constant: true,
            Unique: false,
            NameHex: "4c6f74ff",
            StatsWithheld: "constant",
            SourceType: "VARCHAR",
            Top: new TopValues([new TopValue("ABC1", 9990), new TopValue("ABC2", 10)], true, 3),
            Text: new TextStats("ABC1", "ABC2"));

        var f3 = new FieldProfile(
            Index: 3,
            Name: "Date",
            NameParts: new NameParts("Date", null, "none"),
            Type: "date",
            Dominant: new DominantType("date", 1.0),
            TypeVotes: CountMap.FromOrdinal([new("date", 10000)]),
            Count: 10000,
            Nulls: 0,
            NullsExcludingAllNullRows: 0,
            NullTokens: CountMap.Empty,
            Flags: new FieldFlags(0, 0, 0),
            Distinct: new DistinctInfo(30, false),
            Length: null,
            Shapes: [new Shape("9/9/9{4}", 10000)],
            Monotonic: null,
            Constant: false,
            Unique: false,
            Temporal: new TemporalStats(["DMY", "MDY"], true, null, null, null, null));

        return new Descriptor(
            Tool: new ToolSection("squalify", "0.1.0"),
            Options: ProfileOptions.Create(true, ["Program", "Lot"]),
            Limits: StandardLimits(),
            File: new FileSection("lot42_sweep.csv", 10212, Sha256Empty),
            Encoding: new EncodingSection("text", "utf-8", "utf-8", new LineEndings(0, 10010, 0), 0, 0),
            Format: new FormatSection(
                "delimited",
                new DelimitedDialect(",", "\"", true, false, ".", new PaddingInfo(true, 0.125, true, 118), 2),
                0.95),
            Kind: new KindLabel("labSweepTable", "K05", 0.8),
            Layout: new LayoutSection(
                [
                    new Region(
                        "preamble",
                        1,
                        6,
                        0,
                        212,
                        LineKinds: CountMap.FromOrdinal([new("keyValue", 5), new("comment", 1)]),
                        Keys: ["Lot", "Wafer"],
                        Values: ["A12", "7"]),
                    new Region(
                        "annotation",
                        7,
                        8,
                        212,
                        655,
                        Rows:
                        [
                            new AnnotationRow(
                                7,
                                "Upper",
                                1,
                                CountMap.FromOrdinal([new("NA", 2)]),
                                CountMap.FromOrdinal([new("float", 9)]),
                                "float",
                                ["1", "2"]),
                            new AnnotationRow(8, null, 0, CountMap.Empty, CountMap.Empty, "string"),
                        ]),
                    new Region("header", 9, 9, 655, 700),
                    new Region("block", 10, 12, 700, 760, Index: 1, Label: "noise"),
                ],
                new HeaderConventionInfo("nameParenUnit", 0.769, CountMap.FromOrdinal([new("none", 3), new("nameParenUnit", 10)])),
                118),
            Fingerprint: new Fingerprint(1, "sha256:aa", "sha256:bb", "sha256:cc", "sha256:dd"),
            Hints: new Hints(
                ".csv",
                null,
                [new StableLine(0, "^Lot\\s*[=:]")],
                ["Frequency(MHz)", "Start"],
                ["Frequency(MHz)"],
                [0, 1],
                new MinMax(4, 4),
                9,
                ",",
                "nameParenUnit",
                null,
                null,
                "a{3}9{2}.a{3}"),
            Issues:
            [
                new Issue("commentInData", 2, FirstLines: [5, 9]),
                new Issue("invalidUtf8", 1, Offsets: [100]),
            ],
            Evidence:
            [
                new Evidence(
                    "dialect",
                    "delimiter",
                    ",",
                    [new Candidate(",", 1.0, "k=4; 10000/10000 records; run lines 10-10009"), new Candidate(";", 0.0, "k=1; no run")]),
                new Evidence("encoding", "charset", null, Array.Empty<Candidate>()),
            ],
            Table: new TableSection(
                10000,
                new MinMax(4, 4),
                [f0, f1, f2, f3],
                new RowClassesInfo(
                    2,
                    4,
                    1,
                    0,
                    [
                        new RowClass("rc:3f9a", 900, [new FieldRange(0, 3)], 3, 1002),
                        new RowClass("rc:7b01", 99, [new FieldRange(0, 0), new FieldRange(2, 3)], 4, 1001),
                    ])));
    }

    /// <summary>A fully populated non-tabular descriptor: sections, binary and STDF profiles, hints, offsets.</summary>
    public static Descriptor FullBinaryStdf()
    {
        var histogram = Enumerable.Range(0, 256).Select(static i => (long)i).ToArray();
        var segments = Enumerable.Range(0, 16).Select(static i => i / 2.0).ToArray();
        return new Descriptor(
            Tool: new ToolSection("squalify", "0.1.0"),
            Options: ProfileOptions.Create(false, Array.Empty<string>()),
            Limits: StandardLimits(),
            File: new FileSection("run.stdf", 65536, Sha256Empty),
            Encoding: new EncodingSection("binary", null, null, new LineEndings(0, 0, 0), 0, 17),
            Format: new FormatSection("stdf", new StdfDialect(2, "little", "V4"), 1.0),
            Kind: new KindLabel("stdfTestLog", "K01", 1.0),
            Layout: new LayoutSection(Array.Empty<Region>()),
            Fingerprint: new Fingerprint(1, "sha256:aa", "sha256:bb", "sha256:cc", "sha256:dd"),
            Hints: new Hints(
                ".stdf",
                "stdf",
                Array.Empty<StableLine>(),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                ["a", "b"],
                new StdfHints(2, "V4"),
                "a{3}.a{4}"),
            Issues: [new Issue("truncated", 1, Offsets: [65530])],
            Evidence: Array.Empty<Evidence>(),
            Sections:
            [
                new KeyValueSection("Setup", 1, ["Temp", "Vdd"], ["25", "1.8"]),
                new KeyValueSection("Empty", 4, Array.Empty<string>()),
            ],
            Binary: new BinarySection(
                new ByteStats(histogram, 12, 300, 4000, 7.5),
                new BlockStats(16, 0.5, 7.9, 6.25),
                segments),
            Stdf: new StdfSection(
                [new StdfRecordCount("FAR", 0, 10, 1), new StdfRecordCount("PTR", 15, 10, 4210)],
                ["FAR", "PTR"],
                true,
                false,
                [new StdfTest(1000, "P", "VDD_LEAK", 4210)],
                7));
    }
}
