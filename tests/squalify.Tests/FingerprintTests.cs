// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class FingerprintTests
{
    private static ProfileOptions Options(bool includeValues = false) => ProfileOptions.Create(includeValues, Array.Empty<string>());

    private static Descriptor Profile(string text, bool includeValues = false, string name = "t.csv")
    {
        return Profiler.Profile(new BytesSource(name, Encoding.UTF8.GetBytes(text)), Options(includeValues));
    }

    private static Descriptor ProfileBytes(byte[] bytes, string name = "t.csv")
    {
        return Profiler.Profile(new BytesSource(name, bytes), Options());
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static string Sha(string text) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // 1
    [Fact]
    public void Fingerprint_SameLayoutDifferentValues_AllEqual()
    {
        var a = Profile("Site,Vth(V),Name\n1,0.50,alpha\n2,0.51,beta\n3,0.52,gamma\n");
        var b = Profile("Site,Vth(V),Name\n7,0.90,delta\n8,0.80,eps\n9,0.70,zeta\n10,0.60,eta\n");
        Assert.Equal(1, a.Fingerprint.Version);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.StartsWith("sha256:", a.Fingerprint.Layout, StringComparison.Ordinal);
        Assert.Equal(71, a.Fingerprint.Layout.Length);
    }

    // 2
    [Fact]
    public void Fingerprint_ColumnEmptyInOneFile_LayoutEqualTypedDiffers()
    {
        var full = Profile("Site,Vth,Note\n1,0.5,x\n2,0.6,y\n3,0.7,z\n");
        var empty = Profile("Site,Vth,Note\n1,0.5,\n2,0.6,\n3,0.7,\n");
        Assert.Equal("empty", empty.Table!.Fields[2].Type);
        Assert.Equal(full.Fingerprint.Structure, empty.Fingerprint.Structure);
        Assert.Equal(full.Fingerprint.Layout, empty.Fingerprint.Layout);
        Assert.Equal(full.Fingerprint.Names, empty.Fingerprint.Names);
        Assert.NotEqual(full.Fingerprint.Typed, empty.Fingerprint.Typed);
    }

    // 3
    [Fact]
    public void Fingerprint_HeaderCaseDiffers_Equal()
    {
        var a = Profile("Site,Vth,Idsat\n1,0.5,3\n2,0.6,4\n");
        var b = Profile("SITE,vth,IDSat\n1,0.5,3\n2,0.6,4\n");
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(1, b.Fingerprint.Version);
        Assert.Contains("\"fields\":[\"site\",\"vth\",\"idsat\"]", Text(Projection.Layout(ProjectionInput.From(b))), StringComparison.Ordinal);
        Assert.Equal("[\"idsat\",\"site\",\"vth\"]", Text(Projection.Names(ProjectionInput.From(b))));
    }

    // 4
    [Fact]
    public void Fingerprint_ReorderedColumns_NamesEqualLayoutDiffers()
    {
        var a = Profile("alpha,beta,gamma\n1,x,0.5\n2,y,0.6\n");
        var b = Profile("gamma,alpha,beta\n0.5,1,x\n0.6,2,y\n");
        Assert.Equal(a.Fingerprint.Names, b.Fingerprint.Names);
        Assert.Equal(a.Fingerprint.Structure, b.Fingerprint.Structure);
        Assert.NotEqual(a.Fingerprint.Layout, b.Fingerprint.Layout);
        Assert.NotEqual(a.Fingerprint.Typed, b.Fingerprint.Typed);
    }

    // 5
    [Fact]
    public void Fingerprint_NoValuesInProjection()
    {
        var text = "# CANARY comment line\nLot=CANARYLOT\nCANARY free text here\nSite,Vth,Name\n1,0.5,CANARYA\n2,0.6,CANARYB\n3,0.7,CANARYC\n";
        var d = Profile(text, includeValues: true);
        var input = ProjectionInput.From(d);
        foreach (var bytes in new[] { Projection.Structure(input), Projection.Layout(input), Projection.Typed(input), Projection.Names(input) })
        {
            var projection = Text(bytes);
            Assert.NotEmpty(projection);
            Assert.DoesNotContain("CANARY", projection, StringComparison.Ordinal);
        }

        var layout = Text(Projection.Layout(input));
        Assert.Contains("\"family\":\"delimited\"", layout, StringComparison.Ordinal);
        Assert.Contains("\"preambleKeys\":[\"lot\"]", layout, StringComparison.Ordinal);
        Assert.Contains("\"fields\":[\"site\",\"vth\",\"name\"]", layout, StringComparison.Ordinal);

        var without = Profile(text, includeValues: false);
        Assert.Equal(without.Fingerprint, d.Fingerprint);
    }

    // 6
    [Fact]
    public void Fingerprint_Structure_NameFree_EqualAcrossColumnSets()
    {
        var path = TestBytes.Fixture("delimited/padded-annotated.csv");
        var original = File.ReadAllText(path);
        var renamed = original.Replace("Pout(dBm)", "Zzzz(dBm)", StringComparison.Ordinal).Replace("Gain(dB)", "Loss(dB)", StringComparison.Ordinal);
        Assert.NotEqual(original, renamed);
        Assert.Equal(original.Length, renamed.Length);

        var a = Profile(original, name: "a.csv");
        var b = Profile(renamed, name: "b.csv");
        Assert.Equal("delimited", a.Format.Family);
        Assert.Contains(a.Layout.Regions, static r => r.Kind == "annotation");
        Assert.Equal(a.Fingerprint.Structure, b.Fingerprint.Structure);
        Assert.NotEqual(a.Fingerprint.Layout, b.Fingerprint.Layout);
        Assert.NotEqual(a.Fingerprint.Names, b.Fingerprint.Names);
        Assert.DoesNotContain("zzzz", Text(Projection.Structure(ProjectionInput.From(b))), StringComparison.Ordinal);
    }

    // 7
    [Fact]
    public void Fingerprint_InvalidByteName_Excluded()
    {
        byte[] File(params byte[] stray)
        {
            return TestBytes.Concat(
                TestBytes.Ascii("Lot=A12\nLo"),
                stray,
                TestBytes.Ascii("t=5\nSite,Vth\n1,0.5\n2,0.6\n3,0.7\n"));
        }

        var invalid = ProfileBytes(File(0xFF));
        var control = ProfileBytes(File(0x01));
        Assert.Contains(invalid.Encoding.Charset, new[] { "ascii", "utf-8" });
        Assert.Contains("�", invalid.Layout.Regions[0].Keys![1], StringComparison.Ordinal);
        Assert.Contains("\u0001", control.Layout.Regions[0].Keys![1], StringComparison.Ordinal);
        Assert.Equal(invalid.Fingerprint.Layout, control.Fingerprint.Layout);
        Assert.Equal(invalid.Fingerprint.Typed, control.Fingerprint.Typed);
        foreach (var d in new[] { invalid, control })
        {
            var issue = Assert.Single(d.Issues, static i => i.Code == "nameExcludedFromFingerprint");
            Assert.Equal(1, issue.Count);
            Assert.Null(issue.FirstLines);
            Assert.Null(issue.Offsets);
            Assert.DoesNotContain("�", Text(Projection.Layout(ProjectionInput.From(d))), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Fingerprint_InvalidByteFieldName_ProjectedByIndex()
    {
        var a = ProfileBytes(TestBytes.Concat(TestBytes.Ascii("Site,V"), [0xFF], TestBytes.Ascii("th,Idsat\n1,0.5,3\n2,0.6,4\n")));
        var b = ProfileBytes(TestBytes.Concat(TestBytes.Ascii("Site,V"), [0x01], TestBytes.Ascii("th,Idsat\n1,0.5,3\n2,0.6,4\n")));
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(1, Assert.Single(a.Issues, static i => i.Code == "nameExcludedFromFingerprint").Count);
        Assert.Contains("\"fields\":[\"site\",\"#1\",\"idsat\"]", Text(Projection.Layout(ProjectionInput.From(a))), StringComparison.Ordinal);
        Assert.Equal(Sha("[\"idsat\",\"site\"]"), a.Fingerprint.Names);
    }

    [Fact]
    public void Fingerprint_ProjectionBytes_ArePinned()
    {
        var d = Profile("a,b\n1,2\n3,4\n");
        var input = ProjectionInput.From(d);
        const string structure = "{\"family\":\"delimited\",\"dialect\":{\"delimiter\":\",\",\"quote\":\"\\\"\",\"hasHeader\":true,\"unitsRow\":false,\"decimalSeparator\":\".\",\"constantLineLength\":true},\"preambleKind\":null,\"charsetFamily\":\"unicode\",\"regions\":[\"header\",\"data\"]}";
        const string layout = "{\"family\":\"delimited\",\"dialect\":{\"delimiter\":\",\",\"quote\":\"\\\"\",\"hasHeader\":true,\"unitsRow\":false},\"preambleKeys\":[],\"charsetFamily\":\"unicode\",\"regions\":[\"header\",\"data\"],\"headerConvention\":\"none\",\"fields\":[\"a\",\"b\"]}";
        var typed = layout[..^1] + ",\"types\":[\"int\",\"int\"]}";
        Assert.Equal(structure, Text(Projection.Structure(input)));
        Assert.Equal(layout, Text(Projection.Layout(input)));
        Assert.Equal(typed, Text(Projection.Typed(input)));
        Assert.Equal("[\"a\",\"b\"]", Text(Projection.Names(input)));
        Assert.Equal(new Fingerprint(1, Sha(structure), Sha(layout), Sha(typed), Sha("[\"a\",\"b\"]")), d.Fingerprint);
    }

    [Fact]
    public void Fingerprint_NonAsciiCase_IsNotFolded()
    {
        var upper = Profile("Éa,b\n1,2\n3,4\n");
        var lower = Profile("éa,b\n1,2\n3,4\n");
        Assert.NotEqual(upper.Fingerprint.Layout, lower.Fingerprint.Layout);
        Assert.NotEqual(upper.Fingerprint.Names, lower.Fingerprint.Names);
        Assert.Contains("\"fields\":[\"Éa\",\"b\"]", Text(Projection.Layout(ProjectionInput.From(upper))), StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_UnderTurkishCulture_IsIdentical()
    {
        var text = "ID,TITLE,Idsat\n1,a,3\n2,b,4\n";
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = Profile(text);
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var turkish = Profile(text);
            Assert.Equal(invariant.Fingerprint, turkish.Fingerprint);
            Assert.Equal(DescriptorWriter.ToBytes(invariant), DescriptorWriter.ToBytes(turkish));
            Assert.Contains("\"fields\":[\"id\",\"title\",\"idsat\"]", Text(Projection.Layout(ProjectionInput.From(turkish))), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Fingerprint_HeaderConvention_IsInLayoutNotStructure()
    {
        var paren = Profile("a(V),b(A)\n1,2\n3,4\n");
        var none = Profile("aaaa,bbbb\n1,2\n3,4\n");
        Assert.Contains("\"headerConvention\":\"nameParenUnit\"", Text(Projection.Layout(ProjectionInput.From(paren))), StringComparison.Ordinal);
        Assert.DoesNotContain("headerConvention", Text(Projection.Structure(ProjectionInput.From(paren))), StringComparison.Ordinal);
        Assert.Equal(paren.Fingerprint.Structure, none.Fingerprint.Structure);
        Assert.NotEqual(paren.Fingerprint.Layout, none.Fingerprint.Layout);
    }

    [Fact]
    public void Fingerprint_NoHeaderConvention_ProjectsNull()
    {
        var headerless = Profiler.Profile(TestBytes.Fixture("delimited/no-header.tsv"), Options());
        Assert.False(((DelimitedDialect)headerless.Format.Dialect!).HasHeader);
        Assert.Null(headerless.Layout.HeaderConvention);
        var layout = Text(Projection.Layout(ProjectionInput.From(headerless)));
        Assert.Contains("\"headerConvention\":null", layout, StringComparison.Ordinal);
        Assert.Contains("\"fields\":[\"#0\"", layout, StringComparison.Ordinal);

        var plain = Profile("just some text\nand a second line\n", name: "a.txt");
        Assert.Equal("text", plain.Format.Family);
        Assert.Contains("\"headerConvention\":null", Text(Projection.Layout(ProjectionInput.From(plain))), StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_IndexedColumns_AreNotCollapsed()
    {
        var three = Profile("Trace[0],Trace[1],Trace[2]\n1,2,3\n4,5,6\n");
        var four = Profile("Trace[0],Trace[1],Trace[2],Trace[3]\n1,2,3,4\n5,6,7,8\n");
        Assert.NotEqual(three.Fingerprint.Layout, four.Fingerprint.Layout);
        Assert.NotEqual(three.Fingerprint.Names, four.Fingerprint.Names);
    }

    [Fact]
    public void Fingerprint_PreambleKeyOrderAndCase()
    {
        var a = Profile("Lot=1\nWafer=2\nSite,Vth\n1,0.5\n2,0.6\n3,0.7\n");
        var upper = Profile("LOT=9\nwafer=8\nSite,Vth\n1,0.5\n2,0.6\n3,0.7\n");
        var swapped = Profile("Wafer=2\nLot=1\nSite,Vth\n1,0.5\n2,0.6\n3,0.7\n");
        Assert.Equal(a.Fingerprint, upper.Fingerprint);
        Assert.NotEqual(a.Fingerprint.Layout, swapped.Fingerprint.Layout);
        Assert.Equal(a.Fingerprint.Structure, swapped.Fingerprint.Structure);
        Assert.Contains("\"preambleKind\":\"keyValue\"", Text(Projection.Structure(ProjectionInput.From(a))), StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_PreambleKind_IsDominantNonBlankKind()
    {
        var comments = Profile("# one\n# two\n\n\n\nLot=1\nSite,Vth\n1,0.5\n2,0.6\n3,0.7\n");
        Assert.Contains("\"preambleKind\":\"comment\"", Text(Projection.Structure(ProjectionInput.From(comments))), StringComparison.Ordinal);
        var tie = Profile("# one\nLot=1\nSite,Vth\n1,0.5\n2,0.6\n3,0.7\n");
        Assert.Contains("\"preambleKind\":\"comment\"", Text(Projection.Structure(ProjectionInput.From(tie))), StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_TextAndBinaryAndEmpty_HaveVersionOneHashes()
    {
        var text = Profile("hello world\n", name: "a.txt");
        var binary = ProfileBytes(TestBytes.Hex("00 01 02 03 04 05 06 07 08"), "a.bin");
        var empty = ProfileBytes([], "e.txt");
        foreach (var d in new[] { text, binary, empty })
        {
            Assert.Equal(1, d.Fingerprint.Version);
        }

        Assert.Contains("\"charsetFamily\":null", Text(Projection.Structure(ProjectionInput.From(binary))), StringComparison.Ordinal);
        Assert.NotEqual(text.Fingerprint.Structure, binary.Fingerprint.Structure);
        Assert.Equal(Sha("[]"), text.Fingerprint.Names);
    }

    [Fact]
    public void Fingerprint_Latin1AndUtf8_DifferInCharsetFamily()
    {
        var utf8 = ProfileBytes(TestBytes.Concat(Encoding.UTF8.GetBytes("a,b\n1,"), [0xC3, 0xA9], Encoding.UTF8.GetBytes("\n2,x\n3,y\n")));
        var latin1 = ProfileBytes(TestBytes.Concat(Encoding.UTF8.GetBytes("a,b\n1,"), [0xE9, 0xE9, 0xE9], Encoding.UTF8.GetBytes("\n2,x\n3,y\n")));
        Assert.Equal("utf-8", utf8.Encoding.Charset);
        Assert.Equal("latin-1", latin1.Encoding.Charset);
        Assert.Contains("\"charsetFamily\":\"unicode\"", Text(Projection.Structure(ProjectionInput.From(utf8))), StringComparison.Ordinal);
        Assert.Contains("\"charsetFamily\":\"latin1\"", Text(Projection.Structure(ProjectionInput.From(latin1))), StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_DelimitedContributor_IsRegisteredForItsFamily()
    {
        Assert.IsType<DelimitedFingerprint>(FingerprintContributors.For("delimited"));
        Assert.Null(FingerprintContributors.For("text"));
    }

    private sealed class FakeContributor(params string[] names) : IFingerprintContributor
    {
        public string Family => "text";

        public FingerprintContribution Contribute(ProjectionInput input)
        {
            return new FingerprintContribution(null, null, null, names, 0);
        }
    }

    [Fact]
    public void Fingerprint_ContributorNames_GetTheSameHygiene()
    {
        var input = ProjectionInput.From(Profile("hello world\n", name: "a.txt"));
        var contributor = new FakeContributor("Good", "Ba\u0001d", "\uFFFD", "OK", "good", "x\u0085y");
        Assert.Equal("[\"good\",\"ok\"]", Text(Projection.Names(input, contributor)));
        var result = Projection.Compute(input, contributor);
        Assert.Equal(3, result.Excluded);
        Assert.Equal(Sha("[\"good\",\"ok\"]"), result.Fingerprint.Names);
    }

    [Fact]
    public void Fingerprint_UnnamedFieldAndFieldNamedHashIndex_ShareLayoutButNotNames()
    {
        var headerless = Profiler.Profile(new BytesSource("t.tsv", TestBytes.Ascii("1\t2\n3\t4\n5\t6\n")), Options());
        var named = Profile("a,#1\n1,2\n3,4\n5,6\n");
        Assert.Contains("\"fields\":[\"#0\",\"#1\"]", Text(Projection.Layout(ProjectionInput.From(headerless))), StringComparison.Ordinal);
        Assert.Contains("\"fields\":[\"a\",\"#1\"]", Text(Projection.Layout(ProjectionInput.From(named))), StringComparison.Ordinal);
        var asIndex = Profile("\"#0\",\"#1\"\n1,2\n3,4\n5,6\n");
        Assert.Contains("\"fields\":[\"#0\",\"#1\"]", Text(Projection.Layout(ProjectionInput.From(asIndex))), StringComparison.Ordinal);
        Assert.Equal("[]", Text(Projection.Names(ProjectionInput.From(headerless))));
        Assert.Equal("[\"#0\",\"#1\"]", Text(Projection.Names(ProjectionInput.From(asIndex))));
        Assert.NotEqual(headerless.Fingerprint.Names, asIndex.Fingerprint.Names);
    }

    [Fact]
    public void Ordinal_AsciiFold_FoldsOnlyAsciiLetters()
    {
        Assert.Equal("abc xyz-09 ÉİIé".Replace('I', 'i'), Ordinal.AsciiFold("ABC XYZ-09 ÉİIé"));
        Assert.Equal(string.Empty, Ordinal.AsciiFold(string.Empty));
    }
}
