// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>The values tier: nothing literal without <c>--include-values</c> or an allow-list entry. Later format slices add canary fixtures to the theory.</summary>
public sealed class ValuesTierTests
{
    private const string Canary = "CANARY";

    public static TheoryData<string> CanaryFixtures() => new() { "canary/canary.csv", "canary/canary-preamble.csv" };

    private static Descriptor ProfileFixture(string fixture, bool includeValues = false, params string[] allow)
    {
        return Profiler.Profile(TestBytes.Fixture(fixture), ProfileOptions.Create(includeValues, allow));
    }

    private static string Json(string fixture, bool includeValues = false, params string[] allow)
    {
        return Encoding.UTF8.GetString(DescriptorWriter.ToBytes(ProfileFixture(fixture, includeValues, allow)));
    }

    private static Descriptor ProfileText(string text, bool includeValues = false, ProfileLimits? limits = null, params string[] allow)
    {
        var source = new BytesSource("t.csv", Encoding.UTF8.GetBytes(text));
        var options = ProfileOptions.Create(includeValues, allow);
        return limits is null ? Profiler.Profile(source, options) : Profiler.Profile(source, options, limits);
    }

    // Every string in the document that contains the canary, with its JSON path.
    private static string[] CanaryPaths(string json)
    {
        using var document = JsonDocument.Parse(json);
        var found = new List<string>();
        Walk(document.RootElement, string.Empty, found);
        found.Sort(StringComparer.Ordinal);
        return found.ToArray();
    }

    private static void Walk(JsonElement element, string path, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name, found);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", found);
                    index++;
                }

                break;
            case JsonValueKind.String when element.GetString()!.Contains(Canary, StringComparison.Ordinal):
                found.Add(path);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(CanaryFixtures))]
    public void Canary_NoValuesWithoutFlag(string fixture)
    {
        var without = Json(fixture);
        Assert.DoesNotContain(Canary, without, StringComparison.Ordinal);
        Assert.Contains(Canary, Json(fixture, includeValues: true), StringComparison.Ordinal);
    }

    [Fact]
    public void ValuesPolicy_AllValueMembersRegistered()
    {
        var marked = typeof(Descriptor).Assembly.GetTypes()
            .Where(static t => t.Namespace == "squalor.squalify")
            .SelectMany(static t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(static p => p.GetCustomAttribute<ValueBearingAttribute>() is not null)
                .Select(p => t.Name + "." + p.Name))
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToArray();
        var listed = ValuesPolicy.Members.SelectMany(static m => m.ModelMembers).OrderBy(static n => n, StringComparer.Ordinal).ToArray();

        // The members that exist today, spelled out so that removing a mark or an entry is a visible change here.
        var expected = new[]
        {
            "AnnotationRow.Values",
            "FieldProfile.Numeric",
            "FieldProfile.Temporal",
            "FieldProfile.Text",
            "FieldProfile.Top",
            "FieldProfile.Unit",
            "Hints.StableLines",
            "KeyValueSection.Values",
            "Region.Values",
        };
        Assert.Equal(expected, marked);
        Assert.Equal(marked, listed);
        Assert.Equal(listed.Length, listed.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ValuesPolicy.Members.Count, ValuesPolicy.Members.Select(static m => m.Name).Distinct(StringComparer.Ordinal).Count());
    }

    private static FieldProfile Field(string name = "x")
    {
        return TestModels.FloatField(new NumericStats(DescriptorNumber.Int(1), DescriptorNumber.Int(1), 1.0, null, null, null), name) with { Numeric = null };
    }

    private static Descriptor WithField(FieldProfile field, ProfileOptions options)
    {
        return TestModels.Minimal([field]) with { Options = options };
    }

    private static Descriptor ValueCase(string name, ProfileOptions options)
    {
        var baseline = TestModels.Minimal() with { Options = options };
        switch (name)
        {
            case "region":
                return baseline with { Layout = new LayoutSection([new Region("preamble", 1, 1, 0, 10, Keys: ["Lot"], Values: [Canary])]) };
            case "annotation":
                var row = new AnnotationRow(1, "Limit", 0, CountMap.Empty, CountMap.Empty, "string", [Canary]);
                return baseline with { Layout = new LayoutSection([new Region("annotation", 1, 1, 0, 10, Rows: [row])]) };
            case "section":
                return baseline with { Sections = [new KeyValueSection("S", 1, ["Lot"], [Canary])] };
            case "unit":
                return WithField(Field("Lot") with { Unit = Canary }, options);
            case "top":
                return WithField(Field("Lot") with { Top = new TopValues([new TopValue(Canary, 1)], false) }, options);
            case "text":
                return WithField(Field("Lot") with { Text = new TextStats(Canary, Canary) }, options);
            case "constantNumeric":
                return WithField(
                    Field("Lot") with { Constant = true, Numeric = new NumericStats(DescriptorNumber.Int(987654321), DescriptorNumber.Int(987654321), 987654321.0, null, null, null) },
                    options);
            case "constantTemporal":
                return WithField(
                    Field("Lot") with { Type = "date", Constant = true, Temporal = new TemporalStats(["YMD"], false, null, "2026-01-02", "2026-01-02", null) },
                    options);
            case "stableLine":
                return baseline with { Hints = baseline.Hints with { StableLines = [new StableLine(0, "^" + Canary + "$", CarriesValue: true)] } };
            default:
                throw new ArgumentOutOfRangeException(nameof(name));
        }
    }

    public static TheoryData<string, bool> ValueCases() => new()
    {
        { "region", true },
        { "annotation", true },
        { "section", true },
        { "unit", true },
        { "top", true },
        { "text", true },
        { "constantNumeric", true },
        { "constantTemporal", true },
        { "stableLine", false },
    };

    [Theory]
    [MemberData(nameof(ValueCases))]
    public void Writer_RefusesValueMemberWithoutFlag(string member, bool allowListable)
    {
        var refused = ValueCase(member, ProfileOptions.Create(false, []));
        var error = Assert.Throws<InvalidOperationException>(() => DescriptorWriter.ToBytes(refused));
        Assert.DoesNotContain(Canary, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", error.Message, StringComparison.Ordinal);

        // An allow-list entry that names another owner does not open it.
        var other = ValueCase(member, ProfileOptions.Create(false, ["other"]));
        Assert.Throws<InvalidOperationException>(() => DescriptorWriter.ToBytes(other));

        // The flag opens it.
        Assert.NotEmpty(DescriptorWriter.ToBytes(ValueCase(member, ProfileOptions.Create(true, []))));

        // The owner's name, in any ASCII case, opens it, unless the member has no owner to name.
        var named = ValueCase(member, ProfileOptions.Create(false, ["LOT", "limit"]));
        if (allowListable)
        {
            Assert.NotEmpty(DescriptorWriter.ToBytes(named));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => DescriptorWriter.ToBytes(named));
        }
    }

    [Fact]
    public void Writer_RefusesAnAlignedValueWhoseKeyIsNotNamed()
    {
        var region = new Region("preamble", 1, 2, 0, 10, Keys: ["Lot", "Wafer"], Values: [Canary, Canary]);
        var descriptor = TestModels.Minimal() with { Layout = new LayoutSection([region]), Options = ProfileOptions.Create(false, ["lot"]) };
        Assert.Throws<InvalidOperationException>(() => DescriptorWriter.ToBytes(descriptor));
        var onlyNamed = region with { Values = [Canary, null] };
        Assert.NotEmpty(DescriptorWriter.ToBytes(descriptor with { Layout = new LayoutSection([onlyNamed]) }));
    }

    [Fact]
    public void Options_IncludeValues_RecordedInDescriptor()
    {
        var with = ProfileFixture("canary/canary.csv", includeValues: true);
        Assert.True(with.Options.IncludeValues);
        using (var document = JsonDocument.Parse(DescriptorWriter.ToBytes(with)))
        {
            var options = document.RootElement.GetProperty("options");
            Assert.True(options.GetProperty("includeValues").GetBoolean());
            Assert.Equal(0, options.GetProperty("includeValuesFor").GetArrayLength());

            // With the flag, a string field carries its values: the most common ones and the lexical range.
            var name = document.RootElement.GetProperty("table").GetProperty("fields")[1];
            Assert.Equal("name", name.GetProperty("name").GetString());
            Assert.Equal(TopK.K, name.GetProperty("top").GetProperty("items").GetArrayLength());
            Assert.Equal("CANARY-alpha", name.GetProperty("text").GetProperty("min").GetString());
        }

        var without = ProfileFixture("canary/canary.csv");
        Assert.False(without.Options.IncludeValues);
        using var plain = JsonDocument.Parse(DescriptorWriter.ToBytes(without));
        Assert.False(plain.RootElement.GetProperty("options").GetProperty("includeValues").GetBoolean());
        Assert.All(plain.RootElement.GetProperty("table").GetProperty("fields").EnumerateArray(), static f =>
        {
            Assert.Equal(JsonValueKind.Null, f.GetProperty("top").ValueKind);
            Assert.Equal(JsonValueKind.Null, f.GetProperty("text").ValueKind);
        });
    }

    [Fact]
    public void Options_IncludeValuesFor_SortedInDescriptor()
    {
        var descriptor = ProfileFixture("canary/canary.csv", false, "b", "A", "a", "serial");
        Assert.Equal(new[] { "A", "a", "b", "serial" }, descriptor.Options.IncludeValuesFor.ToArray());
        using var document = JsonDocument.Parse(DescriptorWriter.ToBytes(descriptor));
        Assert.Equal(
            new[] { "A", "a", "b", "serial" },
            document.RootElement.GetProperty("options").GetProperty("includeValuesFor").EnumerateArray().Select(static e => e.GetString()).ToArray());
    }

    [Fact]
    public void AllowList_ExposesOnlyNamedMembers()
    {
        // One preamble key and one constant column. OperatorId and SerialNo start with those names, and must stay closed.
        var json = Json("canary/canary-preamble.csv", false, "Operator", "Serial");
        Assert.Equal(
            new[]
            {
                "layout.regions[0].values[2]",
                "table.fields[5].text.max",
                "table.fields[5].text.min",
                "table.fields[5].top.items[0].value",
            },
            CanaryPaths(json));
    }

    [Fact]
    public void AllowList_NameMatchesKeyAndField_ExposesBoth()
    {
        // The preamble key "Lot" and the column "Lot" differ from the name "lot" only in case.
        var json = Json("canary/canary-preamble.csv", false, "lot");
        Assert.Equal(
            new[]
            {
                "layout.regions[0].values[1]",
                "table.fields[3].text.max",
                "table.fields[3].text.min",
                "table.fields[3].top.items[0].value",
            },
            CanaryPaths(json));
    }

    [Fact]
    public void AllowList_AnnotationLabel_OpensItsRowCellsOnly()
    {
        var json = Json("canary/canary-preamble.csv", false, "limit");
        Assert.Equal(
            Enumerable.Range(1, 6).Select(static i => "layout.regions[1].rows[0].values[" + i.ToString(CultureInfo.InvariantCulture) + "]").ToArray(),
            CanaryPaths(json));
    }

    [Fact]
    public void Canary_ConstantNumeric_WithheldUnlessOptedIn()
    {
        var plain = Json("canary/canary.csv");
        Assert.DoesNotContain("987654321", plain, StringComparison.Ordinal);
        using (var document = JsonDocument.Parse(plain))
        {
            var level = document.RootElement.GetProperty("table").GetProperty("fields")[3];
            Assert.Equal("level", level.GetProperty("name").GetString());
            Assert.Equal("constant", level.GetProperty("statsWithheld").GetString());
            Assert.Equal(JsonValueKind.Null, level.GetProperty("numeric").ValueKind);
        }

        // Naming another column does not open it; naming this one, in any case, does.
        Assert.DoesNotContain("987654321", Json("canary/canary.csv", false, "serial"), StringComparison.Ordinal);
        var named = Json("canary/canary.csv", false, "LEVEL");
        Assert.Contains("987654321", named, StringComparison.Ordinal);
        using (var document = JsonDocument.Parse(named))
        {
            var level = document.RootElement.GetProperty("table").GetProperty("fields")[3];
            Assert.Equal(987654321L, level.GetProperty("numeric").GetProperty("min").GetInt64());
            Assert.Equal(JsonValueKind.Null, level.GetProperty("statsWithheld").ValueKind);
        }

        Assert.Contains("987654321", Json("canary/canary.csv", includeValues: true), StringComparison.Ordinal);
    }

    [Fact]
    public void AllowList_UnmatchedName_Issue()
    {
        var descriptor = ProfileFixture("canary/canary-preamble.csv", false, "b", "Zed", "operator", "SERIAL");
        var issue = Assert.Single(descriptor.Issues, static i => i.Code == "includeValuesForUnmatched");
        Assert.Equal(2, issue.Count);
        Assert.Equal(new[] { "Zed", "b" }, issue.Names);
        Assert.Null(issue.FirstLines);
        Assert.Null(issue.Offsets);

        using var document = JsonDocument.Parse(DescriptorWriter.ToBytes(descriptor));
        var written = document.RootElement.GetProperty("issues").EnumerateArray().Single(static i => i.GetProperty("code").GetString() == "includeValuesForUnmatched");
        Assert.Equal(2, written.GetProperty("count").GetInt64());
        Assert.Equal(new[] { "Zed", "b" }, written.GetProperty("names").EnumerateArray().Select(static e => e.GetString()).ToArray());

        // Issues stay sorted by code, and a fully matched list gives no issue.
        var codes = descriptor.Issues.Select(static i => i.Code).ToArray();
        Assert.Equal(codes.OrderBy(static c => c, StringComparer.Ordinal).ToArray(), codes);
        Assert.DoesNotContain(ProfileFixture("canary/canary-preamble.csv", false, "operator", "SERIAL").Issues, static i => i.Code == "includeValuesForUnmatched");
        Assert.DoesNotContain(ProfileFixture("canary/canary-preamble.csv").Issues, static i => i.Code == "includeValuesForUnmatched");
    }

    [Fact]
    public void AllowList_UnmatchedName_ReportedWithTheFlagAndForNonTables()
    {
        Assert.Contains(ProfileFixture("canary/canary-preamble.csv", true, "nothing").Issues, static i => i.Code == "includeValuesForUnmatched");
        Assert.DoesNotContain(ProfileFixture("canary/canary-preamble.csv", false, "Limit").Issues, static i => i.Code == "includeValuesForUnmatched");
        var text = Profiler.Profile(new BytesSource("a.txt", Encoding.ASCII.GetBytes("hello\n")), ProfileOptions.Create(false, ["x"]));
        Assert.Equal(new[] { "x" }, Assert.Single(text.Issues, static i => i.Code == "includeValuesForUnmatched").Names);
    }

    [Fact]
    public void Text_LexicalMinMax_IsOrdinal_AndOnlyForStringFields()
    {
        var d = ProfileText("id,v,n\n1,b,10\n2,B,9\n3,a,8\n4,É,7\n5,NA,6\n6,b,5\n", includeValues: true);
        var v = d.Table!.Fields[1];
        Assert.Equal("string", v.Type);
        Assert.Equal(new TextStats("B", "É"), v.Text);
        Assert.Null(d.Table.Fields[0].Text);
        Assert.Null(d.Table.Fields[2].Text);
        Assert.Null(ProfileText("id,v,n\n1,b,10\n2,B,9\n3,a,8\n4,É,7\n5,NA,6\n6,b,5\n").Table!.Fields[1].Text);
    }

    [Fact]
    public void Top_Field_CountsAreExact_OrderedAndLimitedToTheOptedInField()
    {
        var text = "id,v,w\n1,b,x\n2,a,x\n3,b,x\n4,c,y\n5,b,y\n6,a,y\n7,NA,y\n";
        var d = ProfileText(text, false, null, "V");
        var v = d.Table!.Fields[1];
        Assert.Equal(new TopValues([new TopValue("b", 3), new TopValue("a", 2), new TopValue("c", 1)], false), v.Top, new TopEquality());
        Assert.Null(d.Table.Fields[2].Top);
        Assert.Null(d.Table.Fields[0].Top);

        // A field with no value at all has an empty list, not a missing member.
        var empty = ProfileText("id,v\n1,NA\n2,NA\n3,NA\n", true).Table!.Fields[1];
        Assert.Equal(new TopValues([], false), empty.Top, new TopEquality());
    }

    [Fact]
    public void Top_PastTheDistinctCap_IsApproximateWithAMaxError()
    {
        var builder = new StringBuilder("id,v\n");
        for (var i = 0; i < 60; i++)
        {
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(",heavy\n");
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(",light").Append(i % 9).Append('\n');
        }

        var limits = new ProfileLimits(5, 100_000, 64, 64, new Budget(10_000_000, 2_000_000));
        var top = ProfileText(builder.ToString(), true, limits).Table!.Fields[1].Top!;
        Assert.True(top.Approximate);
        Assert.NotNull(top.MaxError);
        Assert.Equal("heavy", top.Items[0].Value);
        Assert.Equal(60, top.Items[0].Count);
    }

    [Fact]
    public void ValuesTier_IsDeterministic_UnderAnotherCulture()
    {
        string Run()
        {
            return Json("canary/canary-preamble.csv", false, "lot", "serial") + Json("canary/canary.csv", includeValues: true);
        }

        var first = Run();
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal(first, Run());
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private sealed class TopEquality : IEqualityComparer<TopValues?>
    {
        public bool Equals(TopValues? x, TopValues? y)
        {
            return x is not null && y is not null && x.Approximate == y.Approximate && x.MaxError == y.MaxError && x.Items.SequenceEqual(y.Items);
        }

        public int GetHashCode(TopValues? obj) => obj?.Items.Count ?? 0;
    }
}
