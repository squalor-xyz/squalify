// SPDX-License-Identifier: Apache-2.0
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class HeaderConventionTests
{
    private const string UnitsRowText = "Freq,Pout,Idx,Gain(dB)\nMHz,dBm,,dB\n"
        + "700,10.5,1,15.5\n800,11.5,2,16.5\n900,12.5,3,17.5\n1000,13.5,4,18.5\n1100,14.5,5,19.5\n1200,15.5,6,20.5\n";

    private static Descriptor ProfileText(string text, bool includeValues = false)
    {
        return Profiler.Profile(
            new BytesSource("t.csv", Encoding.UTF8.GetBytes(text)),
            ProfileOptions.Create(includeValues, Array.Empty<string>()));
    }

    private static Descriptor ProfileFixture(string fixture)
    {
        return Profiler.Profile(TestBytes.Fixture(fixture), ProfileOptions.Create(false, Array.Empty<string>()));
    }

    private static (string Key, long Value)[] Pairs(CountMap map)
    {
        return map.Select(static e => (e.Key, e.Value)).ToArray();
    }

    [Theory]
    [InlineData("I_Total(A)", "I_Total", "A", "nameParenUnit", false)]
    [InlineData("VCC_V", "VCC", "V", "nameUnderscoreUnit", false)]
    [InlineData("ICC_A", "ICC", "A", "nameUnderscoreUnit", false)]
    [InlineData("I_Total", "I_Total", null, "none", false)]
    [InlineData("Pout[dBm]", "Pout", "dBm", "nameBracketUnit", false)]
    [InlineData("Temp(degC)", "Temp", "degC", "nameParenUnit", false)]
    [InlineData("Date(date)", "Date", "date", "nameParenUnit", true)]
    [InlineData("Gain_dB", "Gain", "dB", "nameUnderscoreUnit", false)]
    [InlineData("Freq (MHz)", "Freq", "MHz", "nameParenUnit", false)]
    [InlineData("Freq( MHz )", "Freq", "MHz", "nameParenUnit", false)]
    [InlineData("Vcc[V]", "Vcc", "V", "nameBracketUnit", false)]
    [InlineData("Zeta[x]", "Zeta", "x", "nameBracketUnit", true)]
    [InlineData("Run_x", "Run_x", null, "none", false)]
    [InlineData("SN", "SN", null, "none", false)]
    [InlineData("(V)", "(V)", null, "none", false)]
    [InlineData("Foo()", "Foo()", null, "none", false)]
    [InlineData("Foo(V)_A", "Foo(V)", "A", "nameUnderscoreUnit", false)]
    [InlineData("Foo_", "Foo_", null, "none", false)]
    [InlineData("_V", "_V", null, "none", false)]
    [InlineData("", "", null, "none", false)]
    [InlineData("Phase(\u00B0)", "Phase", "\u00B0", "nameParenUnit", false)]
    [InlineData("Phase(deg)", "Phase", "deg", "nameParenUnit", false)]
    [InlineData("X(kdeg)", "X", "kdeg", "nameParenUnit", true)]
    [InlineData("Temp(\u00B0C)", "Temp", "\u00B0C", "nameParenUnit", false)]
    [InlineData("Trace[0]", "Trace[0]", null, "none", false)]
    [InlineData("Trace[1]", "Trace[1]", null, "none", false)]
    [InlineData("Trace[2]", "Trace[2]", null, "none", false)]
    [InlineData("Trace[3]", "Trace[3]", null, "none", false)]
    [InlineData("Trace[128]", "Trace[128]", null, "none", false)]
    [InlineData("S21[1]", "S21[1]", null, "none", false)]
    [InlineData("X[foo]", "X", "foo", "nameBracketUnit", true)]
    [InlineData("X[1a]", "X", "1a", "nameBracketUnit", true)]
    [InlineData("Pout(dBm)[1]", "Pout(dBm)[1]", null, "none", false)]
    [InlineData("Gain_dB[1]", "Gain_dB[1]", null, "none", false)]
    public void Convention_Theory(string name, string expectedBase, string? expectedUnit, string expectedConvention, bool unknownUnit)
    {
        var parts = HeaderConventionParser.Parse(name);
        Assert.Equal(new NameParts(expectedBase, expectedUnit, expectedConvention, unknownUnit), parts);
    }

    [Theory]
    [InlineData("RowID(id)", "RowID", "id")]
    [InlineData("GroupID(id)", "GroupID", "id")]
    [InlineData("rowid(id)", "rowid", "id")]
    [InlineData("RowID(ID)", "RowID", "ID")]
    [InlineData("RowID (id)", "RowID", "id")]
    [InlineData("RowID( id )", "RowID", "id")]
    public void Convention_PrismStyleIdNames_AreParenWithAnUnknownUnit(string name, string expectedBase, string expectedUnit)
    {
        // prism splits on the unit text "id" and maps nameParenUnit to its Unit and Id conventions.
        Assert.Equal(new NameParts(expectedBase, expectedUnit, "nameParenUnit", true), HeaderConventionParser.Parse(name));
    }

    [Fact]
    public void Convention_PrismStyleHeader_ThroughTheProfiler()
    {
        var text = "Pout(dBm),VCC(V),RowID(id),GroupID(id),MODESET,TableName\n"
            + "1.5,3.3,1,1,2,A\n1.6,3.3,2,1,2,A\n1.7,3.3,3,1,2,A\n1.8,3.3,4,2,2,B\n1.9,3.3,5,2,2,B\n2.0,3.3,6,2,2,B\n";
        var d = ProfileText(text);
        var parts = d.Table!.Fields.Select(static f => f.NameParts).ToArray();
        Assert.Equal(
            new NameParts?[]
            {
                new("Pout", "dBm", "nameParenUnit"),
                new("VCC", "V", "nameParenUnit"),
                new("RowID", "id", "nameParenUnit", true),
                new("GroupID", "id", "nameParenUnit", true),
                new("MODESET", null, "none"),
                new("TableName", null, "none"),
            },
            parts);
        Assert.Equal("nameParenUnit", d.Layout.HeaderConvention!.Dominant);
        Assert.Equal(new[] { ("nameParenUnit", 4L), ("none", 2L) }, Pairs(d.Layout.HeaderConvention.Counts));
    }

    [Fact]
    public void Convention_TrailingSpaceInTheHeader_IsTrimmedBeforeParsing()
    {
        // The descriptor name is trimmed, so a consumer that matches RowID(id) exactly sees the trimmed form.
        var text = "Pout(dBm),RowID(id) ,Gain(dB)\n1.5,1,3.3\n1.6,2,3.3\n1.7,3,3.3\n1.8,4,3.3\n1.9,5,3.3\n2.0,6,3.3\n";
        var field = ProfileText(text).Table!.Fields[1];
        Assert.Equal("RowID(id)", field.Name);
        Assert.Equal(new NameParts("RowID", "id", "nameParenUnit", true), field.NameParts);
    }

    [Fact]
    public void Convention_DominantAndShare()
    {
        var d = HeaderConventionParser.Analyze(["A(V)", "B(V)", "C(V)", "D[V]", "E_V", "F"], null);
        Assert.Equal("nameParenUnit", d.Info.Dominant);
        Assert.Equal(0.5, d.Info.Share);
        Assert.Equal(new[] { ("nameBracketUnit", 1L), ("nameParenUnit", 3L), ("nameUnderscoreUnit", 1L), ("none", 1L) }, Pairs(d.Info.Counts));

        // A plain majority is dominant even when some names carry units.
        var plain = HeaderConventionParser.Analyze(["A", "B", "C", "D(V)"], null);
        Assert.Equal("none", plain.Info.Dominant);
        Assert.Equal(0.75, plain.Info.Share);

        // A tie goes to the earlier convention in the order paren, bracket, underscore, unitsRow, none.
        Assert.Equal("nameParenUnit", HeaderConventionParser.Analyze(["A", "B", "C(V)", "D(V)"], null).Info.Dominant);
        Assert.Equal("nameBracketUnit", HeaderConventionParser.Analyze(["A[V]", "B[V]", "C_V", "D_V"], null).Info.Dominant);
        Assert.Equal("nameUnderscoreUnit", HeaderConventionParser.Analyze(["A_V", "B_V", "C", "D"], null).Info.Dominant);
        Assert.Equal("unitsRow", HeaderConventionParser.Analyze(["A", "B"], [true, true]).Info.Dominant);
    }

    [Fact]
    public void Convention_ZeroNames_AreNoneWithShareZero()
    {
        var empty = HeaderConventionParser.Analyze([], null);
        Assert.Equal("none", empty.Info.Dominant);
        Assert.Equal(0.0, empty.Info.Share);
        Assert.Empty(empty.Info.Counts);
        Assert.Empty(empty.Parts);
        Assert.Equal("none", empty.Evidence.Chosen);
    }

    [Fact]
    public void Convention_UnitsRow_OnlyWhenTheUnitsCellHasAValue()
    {
        var d = HeaderConventionParser.Analyze(["Index", "VCC", "Pout"], [false, true, true]);
        Assert.Equal(new[] { "none", "unitsRow", "unitsRow" }, d.Parts.Select(static p => p.Convention).ToArray());
        Assert.Equal("unitsRow", d.Info.Dominant);
        Assert.Equal(2 / 3.0, d.Info.Share);
        Assert.Equal(new[] { ("none", 1L), ("unitsRow", 2L) }, Pairs(d.Info.Counts));

        // A name with a unit of its own keeps its convention, whatever its units cell holds.
        var own = HeaderConventionParser.Analyze(["Gain(dB)", "Idx"], [false, false]);
        Assert.Equal(new[] { "nameParenUnit", "none" }, own.Parts.Select(static p => p.Convention).ToArray());

        var d2 = ProfileText("Index,VCC,Pout\n,V,dBm\n1,3.3,10.5\n2,3.3,11.5\n3,3.3,12.5\n4,3.3,13.5\n5,3.3,14.5\n6,3.3,15.5\n");
        Assert.True(Assert.IsType<DelimitedDialect>(d2.Format.Dialect).UnitsRow);
        Assert.Equal("unitsRow", d2.Layout.HeaderConvention!.Dominant);
        Assert.Equal(2 / 3.0, d2.Layout.HeaderConvention.Share);
        Assert.Equal(new[] { ("none", 1L), ("unitsRow", 2L) }, Pairs(d2.Layout.HeaderConvention.Counts));
    }

    [Fact]
    public void Convention_UnitsRow_ConventionAlways_TextOptIn()
    {
        var closed = ProfileText(UnitsRowText);
        Assert.True(Assert.IsType<DelimitedDialect>(closed.Format.Dialect).UnitsRow);
        var conventions = closed.Table!.Fields.Select(static f => f.NameParts!.Convention).ToArray();
        Assert.Equal(new[] { "unitsRow", "unitsRow", "none", "nameParenUnit" }, conventions);
        Assert.All(closed.Table.Fields, static f => Assert.Null(f.Unit));
        Assert.All(closed.Table.Fields.Where(static f => f.NameParts!.Convention == "unitsRow"), static f => Assert.Null(f.NameParts!.Unit));
        Assert.Equal("dB", closed.Table.Fields[3].NameParts!.Unit);
        Assert.Equal("unitsRow", closed.Layout.HeaderConvention!.Dominant);
        Assert.Equal(new[] { ("nameParenUnit", 1L), ("none", 1L), ("unitsRow", 2L) }, Pairs(closed.Layout.HeaderConvention.Counts));
        var json = Encoding.UTF8.GetString(DescriptorWriter.ToBytes(closed));
        Assert.DoesNotContain("MHz", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"dBm\"", json, StringComparison.Ordinal);

        var open = ProfileText(UnitsRowText, includeValues: true);
        Assert.Equal(conventions, open.Table!.Fields.Select(static f => f.NameParts!.Convention).ToArray());
        Assert.Equal(new string?[] { "MHz", "dBm", null, "dB" }, open.Table.Fields.Select(static f => f.Unit).ToArray());
        Assert.Equal(closed.Layout.HeaderConvention.Dominant, open.Layout.HeaderConvention!.Dominant);
        Assert.Equal(Pairs(closed.Layout.HeaderConvention.Counts), Pairs(open.Layout.HeaderConvention.Counts));
    }

    [Fact]
    public void Convention_NoHeader_HasNoNameParts()
    {
        var d = ProfileFixture("delimited/no-header.tsv");
        Assert.All(d.Table!.Fields, static f => Assert.Null(f.NameParts));
        Assert.Null(d.Layout.HeaderConvention);
        Assert.Null(d.Hints.HeaderConvention);
        Assert.DoesNotContain(d.Evidence, static e => e.Stage == "unit");
    }

    [Fact]
    public void Convention_SweepFixture_DominantIsParenWithSnAndTeststandPlain()
    {
        var d = ProfileFixture("delimited/semiconductor-sweep.csv");
        var info = d.Layout.HeaderConvention!;
        Assert.Equal("nameParenUnit", info.Dominant);
        Assert.Equal(11 / 13.0, info.Share);
        Assert.Equal(new[] { ("nameParenUnit", 11L), ("none", 2L) }, Pairs(info.Counts));
        Assert.Equal("nameParenUnit", d.Hints.HeaderConvention);
        var plain = d.Table!.Fields.Where(static f => f.NameParts!.Convention == "none").Select(static f => f.Name).ToArray();
        Assert.Equal(new[] { "SN", "Teststand" }, plain);
        var unknown = d.Table.Fields.Where(static f => f.NameParts!.UnknownUnit).Select(static f => f.Name).ToArray();
        Assert.Equal(new[] { "stimulusGrp(id)", "sweep(id)", "Date(date)" }, unknown);
        Assert.Equal(new NameParts("I_Total", "A", "nameParenUnit"), d.Table.Fields[11].NameParts);
    }

    [Fact]
    public void Convention_Evidence_IsInItsOwnStageAfterHeader()
    {
        var d = ProfileFixture("delimited/semiconductor-sweep.csv");
        var evidence = Assert.Single(d.Evidence, static e => e.Stage == "unit");
        Assert.Equal("headerConvention", evidence.Topic);
        Assert.Equal("nameParenUnit", evidence.Chosen);
        Assert.Equal(
            new[] { "nameParenUnit", "nameBracketUnit", "nameUnderscoreUnit", "unitsRow", "none" },
            evidence.Candidates.Select(static c => c.Value).ToArray());
        Assert.Equal(new[] { 11 / 13.0, 0, 0, 0, 2 / 13.0 }, evidence.Candidates.Select(static c => c.Score).ToArray());
        Assert.Equal("11 of 13 headers", evidence.Candidates[0].Reason);
        Assert.Equal("unit", d.Evidence[^1].Stage);
        Assert.Equal("header", d.Evidence[^2].Stage);
    }

    [Theory]
    [InlineData("mA")]
    [InlineData("\u00B5A")]
    [InlineData("\u03BCA")]
    [InlineData("uA")]
    [InlineData("GHz")]
    [InlineData("kOhm")]
    [InlineData("k\u03A9")]
    [InlineData("MHz")]
    [InlineData("ms")]
    [InlineData("pF")]
    [InlineData("nH")]
    [InlineData("fs")]
    [InlineData("V")]
    [InlineData("dBc")]
    [InlineData("%")]
    [InlineData("\u00B0C")]
    [InlineData("degC")]
    [InlineData("K")]
    [InlineData("ohm")]
    [InlineData("ohms")]
    [InlineData("mohm")]
    [InlineData("Mohm")]
    [InlineData("kOHM")]
    [InlineData("\u2126")]
    [InlineData("\u00B0")]
    [InlineData("deg")]
    [InlineData("Volts")]
    [InlineData("SEC")]
    public void UnitTable_KnownUnits(string unit)
    {
        Assert.True(UnitTable.IsKnown(unit), unit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("id")]
    [InlineData("date")]
    [InlineData("ma")]
    [InlineData("khz")]
    [InlineData("v")]
    [InlineData("hz")]
    [InlineData("db")]
    [InlineData("kdB")]
    [InlineData("mdBm")]
    [InlineData("k%")]
    [InlineData("kC")]
    [InlineData("k\u00B0")]
    [InlineData("kdeg")]
    [InlineData("Deg")]
    [InlineData("Kohm")]
    [InlineData("kohms")]
    [InlineData("m")]
    [InlineData("u")]
    [InlineData("xV")]
    [InlineData("VV")]
    public void UnitTable_NotUnits(string unit)
    {
        Assert.False(UnitTable.IsKnown(unit), unit);
    }

    [Fact]
    public void UnitTable_PrefixedUnits()
    {
        foreach (var prefix in new[] { "f", "p", "n", "u", "\u00B5", "\u03BC", "m", "k", "M", "G" })
        {
            foreach (var symbol in new[] { "V", "A", "W", "Hz", "s", "Ohm", "F", "H" })
            {
                Assert.True(UnitTable.IsKnown(prefix + symbol), prefix + symbol);
            }
        }

        Assert.Equal(1, UnitTable.Version);
        Assert.Equal(UnitTable.Version, Limits.Default.UnitTable);
    }

    [Fact]
    public void Convention_Parse_DoesNotDependOnTheCulture()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(new NameParts("VCC", "V", "nameUnderscoreUnit"), HeaderConventionParser.Parse("VCC_V"));
            Assert.Equal(new NameParts("X", "HZ", "nameParenUnit", true), HeaderConventionParser.Parse("X(HZ)"));
            Assert.Equal(new NameParts("X", "kOHM", "nameParenUnit"), HeaderConventionParser.Parse("X(kOHM)"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }
}
