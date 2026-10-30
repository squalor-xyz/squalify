// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify.Tests;

/// <summary>
/// sq-49: <see cref="ProfileOptions"/> and <see cref="FieldProfile"/> are built by name (required init members for the
/// mandatory ones, defaults for the others), so a member added after 1.0.0 is not a constructor or deconstruction change.
/// The reflection pins are in <see cref="ApiSurfaceTests"/>.
/// </summary>
public sealed class NonPositionalRecordsTests
{
    private static ProfileOptions Everything() => new()
    {
        IncludeValues = true,
        IncludeValuesFor = ["a", "b"],
        MaxBytes = 100,
        MaxFirstRecordBytes = 10,
        LayoutOnly = true,
    };

    [Fact]
    public void ProfileOptions_BuiltByName_SetsEveryMember()
    {
        var options = Everything();
        Assert.True(options.IncludeValues);
        Assert.Equal(["a", "b"], options.IncludeValuesFor);
        Assert.Equal(100, options.MaxBytes);
        Assert.Equal(10, options.MaxFirstRecordBytes);
        Assert.True(options.LayoutOnly);
    }

    [Fact]
    public void ProfileOptions_OptionalMembers_DefaultToTheirOldDefaults()
    {
        var options = new ProfileOptions { IncludeValues = false, IncludeValuesFor = [] };
        Assert.Null(options.MaxBytes);
        Assert.Null(options.MaxFirstRecordBytes);
        Assert.False(options.LayoutOnly);
    }

    [Fact]
    public void ProfileOptions_Create_ParametersMapToTheSameMembers()
    {
        // Create keeps its parameters and also sorts the allow-list ordinally and drops exact duplicates.
        var created = ProfileOptions.Create(true, ["b", "a", "a"], 100, 10, true);
        Assert.Equal(Everything(), created);
        Assert.Equal(Everything().GetHashCode(), created.GetHashCode());
        Assert.Equal(["a", "b"], created.IncludeValuesFor);
    }

    [Fact]
    public void ProfileOptions_BuiltWithNoOptionalMembers_EqualsCreateWithNoOptionalArguments()
    {
        var named = new ProfileOptions { IncludeValues = false, IncludeValuesFor = [] };
        var created = ProfileOptions.Create(false, []);
        Assert.Equal(created, named);
        Assert.Equal(created.GetHashCode(), named.GetHashCode());
    }

    [Theory]
    [InlineData("delimited/semiconductor-sweep.csv")]
    [InlineData("text/plain.txt")]
    public void Profile_WithNamedDefaults_IsByteIdenticalToCreate(string fixture)
    {
        var path = TestBytes.Fixture(fixture);
        var bytes = File.ReadAllBytes(path);
        var name = Path.GetFileName(path);
        var named = Profiler.Profile(new BytesSource(name, bytes), new ProfileOptions { IncludeValues = false, IncludeValuesFor = [] });
        var created = Profiler.Profile(new BytesSource(name, bytes), ProfileOptions.Create(false, []));
        Assert.Equal(DescriptorWriter.ToBytes(created), DescriptorWriter.ToBytes(named));
        Assert.Equal(created, named);
    }

    public static TheoryData<string> OptionMembers => new() { "IncludeValues", "IncludeValuesFor", "MaxBytes", "MaxFirstRecordBytes", "LayoutOnly" };

    [Theory]
    [MemberData(nameof(OptionMembers))]
    public void ProfileOptions_DiffersWhenOneMemberDiffers(string member)
    {
        var baseline = Everything();
        var changed = member switch
        {
            "IncludeValues" => baseline with { IncludeValues = false },
            "IncludeValuesFor" => baseline with { IncludeValuesFor = ["a", "c"] },
            "MaxBytes" => baseline with { MaxBytes = 101 },
            "MaxFirstRecordBytes" => baseline with { MaxFirstRecordBytes = null },
            "LayoutOnly" => baseline with { LayoutOnly = false },
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };
        Assert.NotEqual(baseline, changed);
        Assert.False(baseline == changed);
        Assert.True(baseline != changed);
        Assert.NotEqual(baseline.GetHashCode(), changed.GetHashCode());
    }

    [Fact]
    public void ProfileOptions_AllowList_IsComparedInOrder_AndByContentNotContainer()
    {
        var baseline = Everything();
        Assert.NotEqual(baseline, baseline with { IncludeValuesFor = ["b", "a"] });
        Assert.Equal(baseline, baseline with { IncludeValuesFor = new List<string> { "a", "b" } });
        Assert.Equal(baseline.GetHashCode(), (baseline with { IncludeValuesFor = new List<string> { "a", "b" } }).GetHashCode());
    }

    private static FieldProfile Minimal(long index = 0) => new()
    {
        Index = index,
        Name = "x",
        NameParts = null,
        Type = "int",
        Dominant = new DominantType("int", 1.0),
        TypeVotes = CountMap.FromOrdinal([new("int", 1)]),
        Count = 1,
        Nulls = 0,
        NullsExcludingAllNullRows = 0,
        NullTokens = CountMap.Empty,
        Flags = new FieldFlags(0, 0, 0),
        Distinct = new DistinctInfo(1, false),
        Length = new LengthStats(1, 1, 1.0),
        Shapes = [new Shape("9", 1)],
        Monotonic = "none",
        Constant = false,
        Unique = true,
    };

    [Fact]
    public void FieldProfile_BuiltByName_OptionalMembersDefaultToTheirOldDefaults()
    {
        var field = Minimal(3);
        Assert.Equal(3, field.Index);
        Assert.Equal("x", field.Name);
        Assert.Null(field.NameHex);
        Assert.Null(field.Patterns);
        Assert.Null(field.Numeric);
        Assert.Null(field.Temporal);
        Assert.Null(field.StatsWithheld);
        Assert.Null(field.Unit);
        Assert.Null(field.Top);
        Assert.Null(field.Text);
        Assert.Null(field.RawName);
        Assert.Null(field.NullSpellings);
        Assert.Equal(0, field.NullSpellingsOther);
        Assert.Equal(0, field.NullSpellingsOtherDistinct);
        Assert.False(field.NullSpellingsOtherDistinctCapped);
    }

    [Fact]
    public void FieldProfile_With_CopiesAndKeepsValueEquality()
    {
        var field = Minimal();
        Assert.Equal(field, Minimal());
        Assert.Equal(field.GetHashCode(), Minimal().GetHashCode());
        Assert.Equal(field, field with { });
        Assert.NotEqual(field, field with { Index = 1 });
        Assert.NotEqual(field, field with { RawName = "x" });
        Assert.NotEqual(field, field with { NullSpellingsOtherDistinctCapped = true });
        Assert.NotEqual(field, field with { Shapes = [new Shape("9", 2)] });
        Assert.Equal(field, field with { Shapes = new List<Shape> { new("9", 1) } });
    }
}
