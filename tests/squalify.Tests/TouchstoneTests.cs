// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>sq-09: the Touchstone profiler (v1 and v2), from synthetic files only.</summary>
public sealed class TouchstoneTests
{
    private static readonly string[] Fixtures =
    [
        "touchstone/s2p-v1-noise.s2p",
        "touchstone/s1p-defaults.s1p",
        "touchstone/s4p-wrapped.s4p",
        "touchstone/v2-2port-noise.ts",
        "touchstone/v2-3port-lower.ts",
    ];

    private static ProfileOptions Options(bool includeValues = false, bool layoutOnly = false, params string[] allow) => ProfileOptions.Create(includeValues, allow, layoutOnly: layoutOnly);

    private static Descriptor Profile(string text, string name = "t.s2p", bool includeValues = false, bool layoutOnly = false)
    {
        return Profiler.Profile(new BytesSource(name, Encoding.ASCII.GetBytes(text)), Options(includeValues, layoutOnly));
    }

    private static Descriptor Profile(TouchstoneBuilder builder, string name)
    {
        return Profiler.Profile(new BytesSource(name, builder.Bytes()), Options());
    }

    private static TouchstoneDialect Dialect(Descriptor d)
    {
        Assert.Equal("touchstone", d.Format.Family);
        return Assert.IsType<TouchstoneDialect>(d.Format.Dialect);
    }

    private static string[] Names(Descriptor d)
    {
        return d.Table!.Fields.Select(static f => f.Name!).ToArray();
    }

    private static string Json(Descriptor d)
    {
        return Encoding.UTF8.GetString(DescriptorWriter.ToBytes(d));
    }

    [Theory]
    [InlineData("#", "GHz", "S", "MA", 50.0, "dataFormat,frequencyUnit,parameter,referenceOhms")]
    [InlineData("# GHz S RI R 100", "GHz", "S", "RI", 100.0, "")]
    [InlineData("# MHz Y DB R 75.5", "MHz", "Y", "DB", 75.5, "")]
    [InlineData("# hz z ma", "Hz", "Z", "MA", 50.0, "referenceOhms")]
    [InlineData("# RI R 20", "GHz", "S", "RI", 20.0, "frequencyUnit,parameter")]
    [InlineData("# kHz R 25 DB", "kHz", "S", "DB", 25.0, "parameter")]
    [InlineData("# S MHz", "MHz", "S", "MA", 50.0, "dataFormat,referenceOhms")]
    public void Touchstone_OptionLine_DefaultsAndOrder(string option, string unit, string parameter, string format, double reference, string defaulted)
    {
        var d = Profile(option + "\n1.0 0.5 10\n2.0 0.4 20\n3.0 0.3 30\n", "t.s1p");
        var dialect = Dialect(d);
        Assert.Equal(unit, dialect.FrequencyUnit);
        Assert.Equal(parameter, dialect.Parameter);
        Assert.Equal(format, dialect.DataFormat);
        Assert.Equal(reference, dialect.ReferenceOhms);

        // Each member the option line leaves out is defaulted, and says so in the evidence; the ones it gives are not.
        var expected = defaulted.Length == 0 ? [] : defaulted.Split(',');
        var written = d.Evidence.Where(static e => e.Stage == "dialect" && e.Topic is "frequencyUnit" or "parameter" or "dataFormat" or "referenceOhms").Select(static e => e.Topic).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, written);
    }

    [Fact]
    public void Touchstone_OptionLine_MissingEntirely_IsDefaultedWithEvidence()
    {
        // The extension and a [Version] line make it a Touchstone file even with no option line of its own: the four members are defaulted.
        var d = Profile("[Version] 2.0\n[Number of Ports] 1\n[Number of Frequencies] 2\n[Network Data]\n1 0.5 10\n2 0.4 20\n[End]\n", "t.ts");
        var dialect = Dialect(d);
        Assert.Equal(("GHz", "S", "MA", 50.0), (dialect.FrequencyUnit, dialect.Parameter, dialect.DataFormat, dialect.ReferenceOhms));
        Assert.Equal(4, d.Evidence.Count(static e => e.Stage == "dialect" && e.Topic is "frequencyUnit" or "parameter" or "dataFormat" or "referenceOhms"));
    }

    [Fact]
    public void Touchstone_PortsFromExtensionAndKeyword_MismatchIssue()
    {
        // The extension alone.
        var v1 = Profile(new TouchstoneBuilder { Ports = 3 }, "a.s3p");
        Assert.Equal(3, Dialect(v1).Ports);
        Assert.DoesNotContain(v1.Issues, static i => i.Code == "portMismatch");

        // The keyword alone (.ts has no count).
        var ts = Profile(new TouchstoneBuilder { Ports = 4, V2 = true }, "a.ts");
        Assert.Equal(4, Dialect(ts).Ports);
        Assert.DoesNotContain(ts.Issues, static i => i.Code == "portMismatch");

        // Both, and they agree.
        Assert.DoesNotContain(Profile(new TouchstoneBuilder { Ports = 2, V2 = true }, "a.s2p").Issues, static i => i.Code == "portMismatch");

        // Both, and they differ: the keyword decides (the specification makes it the number of ports of a Version 2 file), and the
        // mismatch is an issue at the keyword's line.
        var mismatch = Profile(new TouchstoneBuilder { Ports = 3, V2 = true }, "a.s2p");
        Assert.Equal(3, Dialect(mismatch).Ports);
        var issue = Assert.Single(mismatch.Issues, static i => i.Code == "portMismatch");
        Assert.Equal(1, issue.Count);
        Assert.Equal([3L], issue.FirstLines);
    }

    [Fact]
    public void Touchstone_FlatTokenStream_AnyLineBreaks()
    {
        var one = Profile(new TouchstoneBuilder { Ports = 4, Frequencies = 3, ValuesPerLine = 1 }, "a.s4p");
        var four = Profile(new TouchstoneBuilder { Ports = 4, Frequencies = 3, ValuesPerLine = 4 }, "a.s4p");
        var eight = Profile(new TouchstoneBuilder { Ports = 4, Frequencies = 3, ValuesPerLine = 8 }, "a.s4p");

        foreach (var d in new[] { one, four, eight })
        {
            Assert.Equal(4, Dialect(d).Ports);
            Assert.Equal(3, d.Table!.Rows);
            Assert.Equal(33, d.Table.Fields.Count);
            Assert.DoesNotContain(d.Issues, static i => i.Code is "incompleteBlock");
        }

        // The fields are the same, whatever the line breaks; only the histogram of values per line differs.
        Assert.Equal(one.Table!.Fields, four.Table!.Fields);
        Assert.Equal(one.Table.Fields, eight.Table!.Fields);
        Assert.Equal(CountMap.FromNumeric([new(1L, 99L)]), Dialect(one).ValuesPerLine);
        Assert.Equal(CountMap.FromNumeric([new(4L, 24L), new(1L, 3L)]), Dialect(four).ValuesPerLine);
        Assert.Equal(CountMap.FromNumeric([new(8L, 12L), new(1L, 3L)]), Dialect(eight).ValuesPerLine);
    }

    [Fact]
    public void Touchstone_TwoPortOrder_FieldsNamedS21BeforeS12()
    {
        // Version 1: the order is N11, N21, N12, N22 (the file has no keyword to say so).
        var v1 = Profile(new TouchstoneBuilder { Ports = 2 }, "a.s2p");
        Assert.Equal("21_12", Dialect(v1).TwoPortOrder);
        Assert.Equal(
            ["freq", "S11.mag", "S11.ang", "S21.mag", "S21.ang", "S12.mag", "S12.ang", "S22.mag", "S22.ang"],
            Names(v1));

        // Version 2 says it: 12_21 reads N11, N12, N21, N22.
        var swapped = Profile(new TouchstoneBuilder { Ports = 2, V2 = true, TwoPortOrder = "12_21" }, "a.ts");
        Assert.Equal("12_21", Dialect(swapped).TwoPortOrder);
        Assert.Equal(
            ["freq", "S11.mag", "S11.ang", "S12.mag", "S12.ang", "S21.mag", "S21.ang", "S22.mag", "S22.ang"],
            Names(swapped));

        // Other port counts have no order.
        Assert.Null(Dialect(Profile(new TouchstoneBuilder { Ports = 3 }, "a.s3p")).TwoPortOrder);
        Assert.Null(Dialect(Profile(new TouchstoneBuilder { Ports = 1 }, "a.s1p")).TwoPortOrder);
    }

    [Theory]
    [InlineData("# GHz S MA R 50", "mag", "ang", "S")]
    [InlineData("# GHz S DB R 50", "db", "ang", "S")]
    [InlineData("# GHz Y RI R 50", "re", "im", "Y")]
    [InlineData("# GHz Z RI R 50", "re", "im", "Z")]
    public void Touchstone_FieldSuffixes_FollowTheFormat_AndTheLetterTheParameter(string option, string first, string second, string letter)
    {
        var d = Profile(new TouchstoneBuilder { Ports = 1, OptionLine = option }, "a.s1p");
        Assert.Equal(["freq", letter + "11." + first, letter + "11." + second], Names(d));
    }

    [Fact]
    public void Touchstone_V1Noise_DetectedByFrequencyDrop()
    {
        var d = Profile(new TouchstoneBuilder { Ports = 2, Frequencies = 4, NoiseFrequencies = 3 }, "a.s2p");
        Assert.True(Dialect(d).Noise);
        Assert.Equal(4, d.Table!.Rows);
        Assert.Equal(
            ["noise.freq", "noise.nfmin", "noise.gammaOpt.mag", "noise.gammaOpt.ang", "noise.rn"],
            Names(d).Skip(9).ToArray());
        Assert.All(d.Table.Fields.Skip(9), static f => Assert.Equal(3, f.Count));
        Assert.All(d.Table.Fields.Take(9), static f => Assert.Equal(4, f.Count));

        // The drop starts the noise block: it is not a frequency that decreased in the network data, and nothing is incomplete.
        Assert.DoesNotContain(d.Issues, static i => i.Code is "nonMonotonicFrequency" or "incompleteBlock");

        // Without the drop there is no noise: the same file with the noise lines left out.
        var plain = Profile(new TouchstoneBuilder { Ports = 2, Frequencies = 4 }, "a.s2p");
        Assert.False(Dialect(plain).Noise);
        Assert.Equal(9, plain.Table!.Fields.Count);
    }

    [Fact]
    public void Touchstone_V1Noise_OnlyForTwoPortFiles()
    {
        // A frequency that drops in a 3-port file is a network frequency out of order, not noise.
        var text = "# GHz S MA R 50\n1 0.1 1 0.1 1 0.1 1\n0.1 1 0.1 1 0.1 1\n0.1 1 0.1 1 0.1 1\n0.5 0.1 1 0.1 1 0.1 1\n0.1 1 0.1 1 0.1 1\n0.1 1 0.1 1 0.1 1\n";
        var d = Profile(text, "a.s3p");
        Assert.False(Dialect(d).Noise);
        Assert.Equal(2, d.Table!.Rows);
        var issue = Assert.Single(d.Issues, static i => i.Code == "nonMonotonicFrequency");
        Assert.Equal(1, issue.Count);
        Assert.Equal([5L], issue.FirstLines);
    }

    [Fact]
    public void Touchstone_V2Keywords_AndNoiseData()
    {
        var d = Profile(new TouchstoneBuilder { Ports = 2, V2 = true, VersionText = "2.0", Frequencies = 3, NoiseFrequencies = 2, Reference = "50 25.0" }, "a.ts");
        var dialect = Dialect(d);
        Assert.Equal("2.0", dialect.Version);
        Assert.Equal(2, dialect.Ports);
        Assert.Equal("Full", dialect.MatrixFormat);
        Assert.Equal(
            ["[Version]", "[Number of Ports]", "[Two-Port Data Order]", "[Number of Frequencies]", "[Number of Noise Frequencies]", "[Reference]", "[Network Data]", "[Noise Data]", "[End]"],
            dialect.Keywords);
        Assert.True(dialect.Noise);
        Assert.Equal(3, d.Table!.Rows);
        Assert.Equal(5, d.Table.Fields.Count(static f => f.Name!.StartsWith("noise.", StringComparison.Ordinal)));

        // The keyword's case and its dash or space do not matter, and a Version 1 file has no [Version]: version 1.0.
        var shouting = Profile("[VERSION] 2.1\n# GHz S MA R 50\n[NUMBER OF PORTS] 1\n[number of frequencies] 2\n[matrix format] Full\n[Network Data]\n1 0.5 10\n2 0.4 20\n[end]\n", "a.ts");
        Assert.Equal("2.1", Dialect(shouting).Version);
        Assert.Equal(["[Version]", "[Number of Ports]", "[Number of Frequencies]", "[Matrix Format]", "[Network Data]", "[End]"], Dialect(shouting).Keywords);
        Assert.Equal("1.0", Dialect(Profile(new TouchstoneBuilder { Ports = 1 }, "a.s1p")).Version);
        Assert.Empty(Dialect(Profile(new TouchstoneBuilder { Ports = 1 }, "a.s1p")).Keywords);
    }

    [Fact]
    public void Touchstone_MatrixFormat_LowerAndUpper_NameAndCountTheEntries()
    {
        var lower = Profile(new TouchstoneBuilder { Ports = 3, V2 = true, MatrixFormat = "Lower" }, "a.ts");
        Assert.Equal("Lower", Dialect(lower).MatrixFormat);
        Assert.Equal(
            ["freq", "S11.mag", "S11.ang", "S21.mag", "S21.ang", "S22.mag", "S22.ang", "S31.mag", "S31.ang", "S32.mag", "S32.ang", "S33.mag", "S33.ang"],
            Names(lower));
        Assert.Equal(4, lower.Table!.Rows);

        var upper = Profile(new TouchstoneBuilder { Ports = 3, V2 = true, MatrixFormat = "Upper" }, "a.ts");
        Assert.Equal(
            ["freq", "S11.mag", "S11.ang", "S12.mag", "S12.ang", "S13.mag", "S13.ang", "S22.mag", "S22.ang", "S23.mag", "S23.ang", "S33.mag", "S33.ang"],
            Names(upper));
    }

    [Fact]
    public void Touchstone_IncompleteBlock_Issue()
    {
        // The last block of a 2-port file is three values short (the file ends on line 5: the option line and four blocks).
        var d = Profile(new TouchstoneBuilder { Ports = 2, Frequencies = 4, CutLastBlockBy = 3 }, "a.s2p");
        Assert.Equal("touchstone", d.Format.Family);
        Assert.Equal(3, d.Table!.Rows);
        var issue = Assert.Single(d.Issues, static i => i.Code == "incompleteBlock");
        Assert.Equal(1, issue.Count);
        Assert.Equal([5L], issue.FirstLines);

        // A complete file has none.
        Assert.DoesNotContain(Profile(new TouchstoneBuilder { Ports = 2, Frequencies = 4 }, "a.s2p").Issues, static i => i.Code == "incompleteBlock");
    }

    [Fact]
    public void Touchstone_Layout_PreambleDataNoiseAndFooter()
    {
        // Line 1 is the comment, 2 [Version], 3 the option line, 4 to 7 the keywords before the data, 8 [Network Data], 9 to 11 the
        // network data, 12 [Noise Data] (the noise block starts at its keyword), 13 and 14 the noise, 15 [End].
        var text = new TouchstoneBuilder { Ports = 2, V2 = true, Frequencies = 3, NoiseFrequencies = 2 }.Comment(" synthetic").Build();
        var d = Profile(text, "a.ts");
        Assert.Equal(["preamble", "data", "noise", "footer"], d.Layout.Regions.Select(static r => r.Kind).ToArray());

        using var json = JsonDocument.Parse(Json(d));
        var regions = json.RootElement.GetProperty("layout").GetProperty("regions").EnumerateArray().ToArray();
        Assert.Equal([1, 8], Pair(regions[0], "lines"));
        Assert.Equal(1, regions[0].GetProperty("lineKinds").GetProperty("comment").GetInt32());
        Assert.Equal([9, 11], Pair(regions[1], "lines"));
        Assert.Equal("noise", regions[2].GetProperty("kind").GetString());
        Assert.Equal([12, 14], Pair(regions[2], "lines"));
        Assert.Equal([15, 15], Pair(regions[3], "lines"));

        // The regions tile the file.
        long end = 0;
        foreach (var region in regions)
        {
            Assert.Equal(end, region.GetProperty("bytes")[0].GetInt64());
            end = region.GetProperty("bytes")[1].GetInt64();
        }

        Assert.Equal(text.Length, end);

        // A region has no label.
        Assert.DoesNotContain(regions, static r => r.TryGetProperty("label", out _));
    }

    private static int[] Pair(JsonElement region, string name)
    {
        return region.GetProperty(name).EnumerateArray().Select(static e => e.GetInt32()).ToArray();
    }

    [Fact]
    public void Touchstone_InvalidByte_InTheNoiseData_IsLocatedInTheNoiseRegion()
    {
        // A stray invalid UTF-8 byte inside the noise data (found by the architect): the issue says "noise", not "other".
        var text = new TouchstoneBuilder { Ports = 2, V2 = true, Frequencies = 3, NoiseFrequencies = 2 }.Build();
        var at = text.IndexOf("[Noise Data]", StringComparison.Ordinal);
        var noiseLine = text.IndexOf('\n', at) + 1;
        var bytes = Encoding.ASCII.GetBytes(text[..(noiseLine + 3)]).Concat(new byte[] { 0x89 }).Concat(Encoding.ASCII.GetBytes(text[(noiseLine + 3)..])).ToArray();
        var d = Profiler.Profile(new BytesSource("a.s2p", bytes), Options());
        Assert.Equal("touchstone", d.Format.Family);
        var issue = Assert.Single(d.Issues, static i => i.Code == "invalidUtf8");
        Assert.Equal(["noise"], issue.Regions);
        Assert.Equal([noiseLine + 3L], issue.Offsets);
    }

    [Fact]
    public void Touchstone_Comments_AreCounted_TheirTextNeedsTheValuesTier()
    {
        var builder = new TouchstoneBuilder { Ports = 1 }.Comment(" CANARY-vna-note").Comment(" second");
        var plain = Json(Profile(builder.Build(), "a.s1p"));
        Assert.DoesNotContain("CANARY", plain, StringComparison.Ordinal);
        Assert.Contains("\"comment\": 2", plain, StringComparison.Ordinal);
        Assert.Contains("CANARY", Json(Profile(builder.Build(), "a.s1p", includeValues: true)), StringComparison.Ordinal);
    }

    [Fact]
    public void Touchstone_Score_ExtensionAndOptionLine_BeatContentAlone()
    {
        var builder = new TouchstoneBuilder { Ports = 2 };
        var withExtension = Profile(builder, "a.s2p");
        Assert.Equal(0.9, withExtension.Format.Score);
        var contentOnly = Profile(builder, "a.dat");
        Assert.Equal("touchstone", contentOnly.Format.Family);
        Assert.Equal(0.7, contentOnly.Format.Score);

        // The family evidence lists the scores of the touchstone and keyValue profilers.
        var family = Assert.Single(withExtension.Evidence, static e => e.Stage == "format" && e.Topic == "family");
        Assert.Equal(["touchstone", "keyValue", "delimited", "text", "binary"], family.Candidates.Select(static c => c.Value).ToArray());
    }

    [Fact]
    public void Touchstone_NotChosen_WithoutAnOptionLineOrVersion()
    {
        // A table of numbers with a .s2p name and no option line is described as the table it is: the profiler needs the option line or [Version].
        var d = Profile(new TouchstoneBuilder { Ports = 2, OptionLine = null }, "a.s2p");
        Assert.NotEqual("touchstone", d.Format.Family);
    }

    [Fact]
    public void Touchstone_LayoutOnly_KeepsTheStructure_AndBareFields()
    {
        var builder = new TouchstoneBuilder { Ports = 2, NoiseFrequencies = 2 };
        var full = Profile(builder.Build(), "a.s2p");
        var bare = Profile(builder.Build(), "a.s2p", layoutOnly: true);
        Assert.Equal(full.Layout, bare.Layout);
        Assert.Equal(full.Format, bare.Format);
        Assert.Equal(full.Table!.Rows, bare.Table!.Rows);
        Assert.Equal(Names(full), Names(bare));
        Assert.All(bare.Table.Fields, static f => Assert.Null(f.Type));
        Assert.Null(bare.Hints);
        Assert.Null(bare.Fingerprint.Typed);
        Assert.Equal(full.Fingerprint.Layout, bare.Fingerprint.Layout);
        Assert.Equal(full.Fingerprint.Structure, bare.Fingerprint.Structure);
        Assert.Equal(full.Fingerprint.Names, bare.Fingerprint.Names);
    }

    [Fact]
    public void Touchstone_Fingerprint_LayoutFollowsTheDialect_NotTheValues()
    {
        var a = Profile(new TouchstoneBuilder { Ports = 2, Frequencies = 3 }, "a.s2p");
        var b = Profile(new TouchstoneBuilder { Ports = 2, Frequencies = 9 }, "b.s2p");
        Assert.Equal(a.Fingerprint.Layout, b.Fingerprint.Layout);
        Assert.Equal(a.Fingerprint.Structure, b.Fingerprint.Structure);

        // The version alone (same fields, same regions) changes it: the dialect members are in the projections, not only the names.
        var v1 = Profile(new TouchstoneBuilder { Ports = 1, Frequencies = 3 }, "a.s1p");
        var v20 = Profile(new TouchstoneBuilder { Ports = 1, Frequencies = 3, V2 = true, End = false }, "a.ts");
        var v21 = Profile(new TouchstoneBuilder { Ports = 1, Frequencies = 3, V2 = true, End = false, VersionText = "2.1" }, "a.ts");
        Assert.Equal(v1.Table!.Fields.Select(static f => f.Name), v20.Table!.Fields.Select(static f => f.Name));
        Assert.Equal(v1.Layout.Regions.Select(static r => r.Kind), v20.Layout.Regions.Select(static r => r.Kind));
        Assert.NotEqual(v1.Fingerprint.Structure, v20.Fingerprint.Structure);
        Assert.NotEqual(v1.Fingerprint.Layout, v20.Fingerprint.Layout);
        Assert.NotEqual(v20.Fingerprint.Layout, v21.Fingerprint.Layout);

        // Another parameter, data format, port count or a noise block changes it.
        Assert.NotEqual(a.Fingerprint.Layout, Profile(new TouchstoneBuilder { Ports = 2, OptionLine = "# GHz S RI R 50" }, "a.s2p").Fingerprint.Layout);
        Assert.NotEqual(a.Fingerprint.Layout, Profile(new TouchstoneBuilder { Ports = 2, OptionLine = "# GHz Y MA R 50" }, "a.s2p").Fingerprint.Layout);
        Assert.NotEqual(a.Fingerprint.Layout, Profile(new TouchstoneBuilder { Ports = 3 }, "a.s3p").Fingerprint.Layout);
        Assert.NotEqual(a.Fingerprint.Layout, Profile(new TouchstoneBuilder { Ports = 2, NoiseFrequencies = 2 }, "a.s2p").Fingerprint.Layout);
    }

    [Fact]
    public void Touchstone_CrLfAndLoneCr_ReadTheSame()
    {
        var lf = Profile(new TouchstoneBuilder { Ports = 2 }, "a.s2p");
        var crlf = Profile(new TouchstoneBuilder { Ports = 2, NewLine = "\r\n" }, "a.s2p");
        Assert.Equal(lf.Table, crlf.Table);
        Assert.Equal(Dialect(lf), Dialect(crlf));
        Assert.Equal(5, crlf.Encoding.LineEndings.Crlf);
    }

    [Fact]
    public void Touchstone_MoreThanTheMaximumPorts_IsNotTouchstone()
    {
        // A full matrix of 100 ports is 20,001 fields; a port count beyond that is not read as a network (and builds no field).
        var big = Profile("# GHz S MA R 50\n1 0.1 1\n2 0.2 2\n", "a.s101p");
        Assert.NotEqual("touchstone", big.Format.Family);
        var family = Assert.Single(big.Evidence, static e => e.Stage == "format" && e.Topic == "family");
        Assert.Contains("ports", family.Candidates.Single(static c => c.Value == "touchstone").Reason, StringComparison.Ordinal);
        Assert.Equal("touchstone", Profile("# GHz S MA R 50\n1 0.1 1\n2 0.2 2\n", "a.s100p").Format.Family);
    }

    [Fact]
    public void Touchstone_HeaderOnly_HasNoRowsNoDataRegion()
    {
        var d = Profile("! only a header\n# GHz S MA R 50\n", "a.s2p");
        Assert.Equal("touchstone", d.Format.Family);
        Assert.Equal(0, d.Table!.Rows);
        Assert.Equal(["preamble"], d.Layout.Regions.Select(static r => r.Kind).ToArray());
        Assert.All(d.Table.Fields, static f => Assert.Equal(0, f.Count));
    }

    [Fact]
    public void Touchstone_BomLoneCrAndBlankLines_TileTheFile()
    {
        var text = "\uFEFF! a comment\r# GHz S MA R 50\r\r1.0 0.5 10\r2.0 0.4 20\r\r! trailer\r\r";
        var bytes = Encoding.UTF8.GetBytes(text);
        var d = Profiler.Profile(new BytesSource("a.s1p", bytes), Options());
        Assert.Equal("touchstone", d.Format.Family);
        Assert.Equal(2, d.Table!.Rows);
        Assert.Equal(["preamble", "data", "footer"], d.Layout.Regions.Select(static r => r.Kind).ToArray());
        Assert.Equal(0, d.Layout.Regions[0].StartByte);
        for (var i = 1; i < d.Layout.Regions.Count; i++)
        {
            Assert.Equal(d.Layout.Regions[i - 1].EndByte, d.Layout.Regions[i].StartByte);
            Assert.Equal(d.Layout.Regions[i - 1].LastLine + 1, d.Layout.Regions[i].FirstLine);
        }

        Assert.Equal(bytes.Length, d.Layout.Regions[^1].EndByte);
    }

    [Fact]
    public void Touchstone_NoiseKeyword_InAFileThatIsNotTwoPort_IsIgnored()
    {
        var d = Profile("[Version] 2.0\n# GHz S MA R 50\n[Number of Ports] 1\n[Network Data]\n1 0.5 10\n2 0.4 20\n[Noise Data]\n3 0.3 30\n[End]\n", "a.ts");
        // Noise data belongs to a two-port network (the specification): the keyword is listed, and the numbers after it stay network data.
        Assert.False(Dialect(d).Noise);
        Assert.Contains("[Noise Data]", Dialect(d).Keywords);
        Assert.Equal(3, d.Table!.Rows);
    }

    [Fact]
    public void Touchstone_V1Noise_StartingAtTheLastNetworkFrequency_IsNoise()
    {
        // IBIS: the first noise frequency is less than or equal to the highest network frequency. A line of exactly 5 values at a block start
        // whose frequency equals the last network frequency begins the noise block.
        var text = "# GHz S MA R 50\n1.0 0.9 -10 0.02 70 0.02 70 0.87 -9\n2.0 0.8 -20 0.04 60 0.04 60 0.85 -19\n2.0 0.82 0.54 31.5 0.19\n3.0 0.91 0.51 55.0 0.17\n";
        var d = Profile(text, "a.s2p");
        Assert.True(Dialect(d).Noise);
        Assert.Equal(2, d.Table!.Rows);
        Assert.Equal(2, d.Table.Fields.Last().Count);
        Assert.DoesNotContain(d.Issues, static i => i.Code is "incompleteBlock" or "nonMonotonicFrequency");
        var noise = Assert.Single(d.Evidence, static e => e.Stage == "dialect" && e.Topic == "noise");
        Assert.Equal("frequencyDrop", noise.Chosen);

        // An equal frequency on a line of 9 values is a duplicate network point, not noise.
        var duplicate = Profile("# GHz S MA R 50\n1.0 0.9 -10 0.02 70 0.02 70 0.87 -9\n1.0 0.8 -20 0.04 60 0.04 60 0.85 -19\n", "a.s2p");
        Assert.False(Dialect(duplicate).Noise);
        Assert.Equal(2, duplicate.Table!.Rows);
    }

    [Theory]
    [InlineData("#")]
    [InlineData("# hz")]
    [InlineData("# MA")]
    public void Touchstone_ContentOnly_NeedsAnOptionLineWithTwoRecognisedTokens(string first)
    {
        // A whitespace table of numbers whose first line is a bare "#" or one recognised token stays what it was in 1.0 (delimited).
        var rows = string.Concat(Enumerable.Range(1, 10).Select(static i => i.ToString(CultureInfo.InvariantCulture) + ".5 0.9 -10 0.02 70 0.02 70 0.87 -9\n"));
        var table = Profile(first + "\n" + rows, "a.dat");
        Assert.Equal("delimited", table.Format.Family);
        var family = Assert.Single(table.Evidence, static e => e.Stage == "format" && e.Topic == "family");
        Assert.Equal(0.0, family.Candidates.Single(static c => c.Value == "touchstone").Score);

        // The same with the 3-column shape of a 1-port file.
        var one = string.Concat(Enumerable.Range(1, 10).Select(static i => i.ToString(CultureInfo.InvariantCulture) + ".5 0.9 -10\n"));
        Assert.NotEqual("touchstone", Profile(first + "\n" + one, "a.dat").Format.Family);

        // Two recognised tokens, or [Version], make it Touchstone without the extension; the extension path is unchanged.
        Assert.Equal("touchstone", Profile("# GHz S\n" + one, "a.dat").Format.Family);
        Assert.Equal("touchstone", Profile("[Version] 2.0\n[Number of Ports] 1\n[Network Data]\n" + one, "a.dat").Format.Family);
        Assert.Equal("touchstone", Profile(first + "\n" + one, "a.s1p").Format.Family);
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Profile_Golden(string fixture)
    {
        Golden.Verify(fixture, Profiler.Profile(TestBytes.Fixture(fixture), Options()));
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Profile_Fixture_SameTwiceAndUnderOtherCultures(string fixture)
    {
        var baseline = DescriptorWriter.ToBytes(Profiler.Profile(TestBytes.Fixture(fixture), Options()));
        var saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(baseline, DescriptorWriter.ToBytes(Profiler.Profile(TestBytes.Fixture(fixture), Options())));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Profile_Fixture_IsTouchstone(string fixture)
    {
        Assert.Equal("touchstone", Profiler.Profile(TestBytes.Fixture(fixture), Options()).Format.Family);
    }

    public static TheoryData<string> FixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in Fixtures)
        {
            data.Add(fixture);
        }

        return data;
    }
}
