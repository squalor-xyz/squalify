// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>sq-41 (Q8): the descriptor model compares by value, with the members of every list compared in order.</summary>
public sealed class DescriptorEqualityTests
{
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

    private static Descriptor Profile(string fixture, bool includeValues = false)
    {
        var path = TestBytes.Fixture(fixture);
        var source = new BytesSource(Path.GetFileName(path), File.ReadAllBytes(path));
        return Profiler.Profile(source, ProfileOptions.Create(includeValues, Array.Empty<string>()));
    }

    public static TheoryData<string, bool> Fixtures => new()
    {
        { "delimited/semiconductor-sweep.csv", false },
        { "delimited/per-site-blocks.csv", false },
        { "delimited/padded-annotated.csv", true },
        { "delimited/ragged-first-rows.csv", false },
        { "lab/sweep-preamble.csv", true },
        { "canary/canary-preamble.csv", true },
        { "text/plain.txt", false },
        { "binary/random.bin", false },
        { "binary/sample.gz", false },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Descriptors_OfTheSameBytes_InTwoRuns_CompareEqual(string fixture, bool includeValues)
    {
        var first = Profile(fixture, includeValues);
        var second = Profile(fixture, includeValues);

        Assert.NotSame(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.True(first.Equals(second));
        Assert.True(((object)first).Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(first, second);
    }

    [Fact]
    public void Descriptors_OfDifferentBytes_DoNotCompareEqual()
    {
        Assert.NotEqual(Profile("delimited/semiconductor-sweep.csv"), Profile("delimited/per-site-blocks.csv"));
    }

    [Fact]
    public void Descriptor_WhenOneIssueDiffers_DiffersFromTheOriginal()
    {
        var original = Profile("delimited/ragged-first-rows.csv");
        Assert.NotEmpty(original.Issues);

        var changedCount = original with { Issues = original.Issues.Select((issue, i) => i == 0 ? issue with { Count = issue.Count + 1 } : issue).ToArray() };
        var added = original with { Issues = [.. original.Issues, new Issue("zzz", 1)] };
        var changedLines = original with
        {
            Issues = original.Issues.Select((issue, i) => i == 0 ? issue with { FirstLines = [.. issue.FirstLines ?? [], 99L] } : issue).ToArray(),
        };

        AssertDiffers(original, changedCount);
        AssertDiffers(original, added);
        AssertDiffers(original, changedLines);
    }

    [Fact]
    public void Descriptor_WhenOneEvidenceEntryDiffers_DiffersFromTheOriginal()
    {
        var original = Profile("delimited/semiconductor-sweep.csv");
        var withCandidates = original.Evidence.First(static e => e.Candidates.Count > 0);
        var index = original.Evidence.ToList().IndexOf(withCandidates);

        Evidence[] Replace(Evidence entry)
        {
            var copy = original.Evidence.ToArray();
            copy[index] = entry;
            return copy;
        }

        var otherScore = withCandidates with { Candidates = withCandidates.Candidates.Select((c, i) => i == 0 ? c with { Score = c.Score + 1 } : c).ToArray() };
        var fewer = withCandidates with { Candidates = withCandidates.Candidates.Skip(1).ToArray() };
        AssertDiffers(original, original with { Evidence = Replace(otherScore) });
        AssertDiffers(original, original with { Evidence = Replace(fewer) });
        AssertDiffers(original, original with { Evidence = Replace(withCandidates with { Chosen = "different" }) });
    }

    [Fact]
    public void Descriptor_WhenOneFieldDiffers_DiffersFromTheOriginal()
    {
        var original = Profile("delimited/semiconductor-sweep.csv");
        var fields = original.Table!.Fields;
        Assert.True(fields.Count >= 2);
        Assert.NotNull(fields[0].Shapes);

        Descriptor WithField(int index, FieldProfile field)
        {
            var copy = fields.ToArray();
            copy[index] = field;
            return original with { Table = original.Table with { Fields = copy } };
        }

        AssertDiffers(original, WithField(0, fields[0] with { Name = "renamed" }));
        AssertDiffers(original, WithField(0, fields[0] with { Count = fields[0].Count + 1 }));
        AssertDiffers(original, WithField(0, fields[0] with { Shapes = [.. fields[0].Shapes!, new Shape("zz", 1)] }));
        AssertDiffers(original, WithField(0, fields[0] with { TypeVotes = CountMap.FromOrdinal([new("int", 12345)]) }));
    }

    [Fact]
    public void Descriptor_ListMembersAreComparedInOrder()
    {
        var original = Profile("delimited/semiconductor-sweep.csv");
        var fields = original.Table!.Fields;
        Assert.True(fields.Count >= 2);

        AssertDiffers(original, original with { Table = original.Table with { Fields = fields.Reverse().ToArray() } });
        AssertDiffers(original, original with { Evidence = original.Evidence.Reverse().ToArray() });
    }

    [Fact]
    public void Descriptor_Lists_CompareByContent_NotByContainerType()
    {
        var original = Profile("delimited/semiconductor-sweep.csv");
        var asList = original with { Issues = original.Issues.ToList(), Evidence = original.Evidence.ToList() };
        var asArray = original with { Issues = original.Issues.ToArray(), Evidence = original.Evidence.ToArray() };
        Assert.Equal(asList, asArray);
        Assert.Equal(asList.GetHashCode(), asArray.GetHashCode());
        Assert.Equal(original, asList);
    }

    [Fact]
    public void Descriptor_NullAndEmptyOptionalLists_AreDifferent_AndNullsAreEqual()
    {
        var issue = new Issue("x", 1);
        Assert.Equal(issue, new Issue("x", 1));
        Assert.NotEqual(issue, issue with { FirstLines = [] });
        Assert.Equal(issue with { FirstLines = [] }, issue with { FirstLines = Array.Empty<long>() });
    }

    [Fact]
    public void Descriptor_Equality_IsOrdinal_UnderAnyCulture()
    {
        var first = Profile("delimited/semiconductor-sweep.csv");
        var second = Profile("delimited/semiconductor-sweep.csv");
        var saved = Thread.CurrentThread.CurrentCulture;
        try
        {
            // Text compares ordinally, so "I" and "i" differ here and everywhere (a culture or case-insensitive comparer could equate them).
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            Assert.Equal(first, second);
            Assert.NotEqual(new FileSection("I.csv", 1, null), new FileSection("i.csv", 1, null));
            Assert.NotEqual(new Issue("I", 1, Names: ["I"]), new Issue("I", 1, Names: ["i"]));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = saved;
        }
    }

    [Fact]
    public void CountMap_ComparesByValue_KeysOrdinally_AndCounts()
    {
        var map = CountMap.FromOrdinal([new("b", 2), new("a", 1)]);
        var same = CountMap.FromOrdinal([new("a", 1), new("b", 2)]);
        Assert.True(map == same);
        Assert.False(map != same);
        Assert.True(map.Equals(same));
        Assert.True(((object)map).Equals(same));
        Assert.Equal(map.GetHashCode(), same.GetHashCode());
        Assert.True(CountMap.Empty == CountMap.FromOrdinal([]));
        Assert.Equal(CountMap.Empty.GetHashCode(), CountMap.FromOrdinal([]).GetHashCode());

        Assert.Equal(CountMap.FromNumeric([new(10, 1), new(2, 3)]), CountMap.FromNumeric([new(2, 3), new(10, 1)]));

        Assert.False(map == CountMap.FromOrdinal([new("A", 1), new("b", 2)]));
        Assert.False(map == CountMap.FromOrdinal([new("a", 1), new("b", 3)]));
        Assert.False(map == CountMap.FromOrdinal([new("a", 1)]));
        Assert.False(map == CountMap.Empty);
        Assert.False(map.Equals(null));
        Assert.False(map.Equals("a"));
        Assert.True(map != null);
        Assert.True((CountMap?)null == null);
        Assert.False(map == null);
        Assert.False(null == map);
    }

    // Every record of the model: the members of each list compare in order, and every member counts.

    public static TheoryData<string> ModelRecords
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var type in ModelRecordTypes())
            {
                data.Add(type.FullName!);
            }

            return data;
        }
    }

    private static IEnumerable<Type> ModelRecordTypes()
    {
        return typeof(Descriptor).Assembly.GetExportedTypes()
            .Where(static t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition && t.GetMethod("<Clone>$") is not null)
            .OrderBy(static t => t.FullName, StringComparer.Ordinal);
    }

    [Fact]
    public void ModelRecords_AreFound()
    {
        var names = ModelRecordTypes().Select(static t => t.Name).ToArray();
        Assert.Contains("Descriptor", names);
        Assert.Contains("Issue", names);
        Assert.Contains("Evidence", names);
        Assert.Contains("FieldProfile", names);
        Assert.Contains("Region", names);
        Assert.Contains("TouchstoneDialect", names);
        Assert.Contains("NullSpellingLimits", names);
        Assert.True(names.Length >= 40, $"Found only {names.Length} records.");
    }

    [Theory]
    [MemberData(nameof(ModelRecords))]
    public void ModelRecord_EqualsByValue_WithListsInOrder_AndHashCodeAgrees(string typeName)
    {
        var type = typeof(Descriptor).Assembly.GetType(typeName, throwOnError: true)!;
        var parameters = MembersOf(type);

        var listBacked = Construct(type, parameters.Select(p => Make(p.Type, 0, asArrays: false)).ToArray());
        var arrayBacked = Construct(type, parameters.Select(p => Make(p.Type, 0, asArrays: true)).ToArray());

        Assert.NotSame(listBacked, arrayBacked);
        Assert.True(listBacked.Equals(arrayBacked), $"{type.Name}: two instances with equal members are not equal.");
        Assert.True(arrayBacked.Equals(listBacked));
        Assert.Equal(listBacked.GetHashCode(), arrayBacked.GetHashCode());
        var equals = type.GetMethod("op_Equality", PublicStatic, [type, type])!;
        var notEquals = type.GetMethod("op_Inequality", PublicStatic, [type, type])!;
        Assert.True((bool)equals.Invoke(null, [listBacked, arrayBacked])!);
        Assert.False((bool)notEquals.Invoke(null, [listBacked, arrayBacked])!);

        for (var i = 0; i < parameters.Length; i++)
        {
            foreach (var variant in VariantsOf(parameters[i].Type))
            {
                var arguments = parameters.Select(p => Make(p.Type, 0, asArrays: false)).ToArray();
                arguments[i] = Make(parameters[i].Type, variant, asArrays: false);
                var other = Construct(type, arguments);
                var how = variant switch { 1 => "differs", 2 => "is reordered", 3 => "is shorter", _ => "is longer or changed" };
                Assert.False(listBacked.Equals(other), $"{type.Name}: still equal when member '{parameters[i].Name}' {how}.");
                Assert.False(other.Equals(listBacked), $"{type.Name}: still equal (reversed) when member '{parameters[i].Name}' {how}.");
                Assert.False((bool)equals.Invoke(null, [listBacked, other])!, $"{type.Name}: == is true when member '{parameters[i].Name}' {how}.");
                Assert.True((bool)notEquals.Invoke(null, [listBacked, other])!, $"{type.Name}: != is false when member '{parameters[i].Name}' {how}.");
            }
        }
    }

    // The members of a model record in construction order: the parameters of its one public constructor, or, for a
    // non-positional record (sq-49: ProfileOptions, FieldProfile), its public init properties.
    private static (string Name, Type Type)[] MembersOf(Type type)
    {
        var constructor = type.GetConstructors().Single();
        var parameters = constructor.GetParameters();
        if (parameters.Length > 0)
        {
            return parameters.Select(static p => (p.Name!, p.ParameterType)).ToArray();
        }

        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.SetMethod is not null)
            .Select(static p => (p.Name, p.PropertyType))
            .ToArray();
    }

    private static object Construct(Type type, object?[] arguments)
    {
        var constructor = type.GetConstructors().Single();
        if (constructor.GetParameters().Length > 0)
        {
            return constructor.Invoke(arguments);
        }

        var instance = constructor.Invoke([]);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(static p => p.SetMethod is not null).ToArray();
        Assert.Equal(properties.Length, arguments.Length);
        for (var i = 0; i < properties.Length; i++)
        {
            properties[i].SetValue(instance, arguments[i]);
        }

        return instance;
    }

    private static int[] VariantsOf(Type type)
    {
        if (IsList(type))
        {
            return [1, 2, 3, 4];
        }

        return (Nullable.GetUnderlyingType(type) ?? type) == typeof(CountMap) ? [1, 3, 4] : [1];
    }

    private static bool IsList(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>);
    }

    // Variant 0 is the base value. Variant 1 is a different value. For a list, 2 is the reverse, 3 drops a member and 4
    // adds one; for a map, 3 drops an entry and 4 changes a count.
    private static object Make(Type type, int variant, bool asArrays, Type[]? building = null)
    {
        var under = Nullable.GetUnderlyingType(type) ?? type;
        if (under == typeof(string))
        {
            return variant == 0 ? "a" : "b";
        }

        if (under == typeof(long))
        {
            return variant == 0 ? 1L : 2L;
        }

        if (under == typeof(double))
        {
            return variant == 0 ? 1.5 : 2.5;
        }

        if (under == typeof(bool))
        {
            return variant == 0;
        }

        if (under == typeof(DescriptorNumber))
        {
            return DescriptorNumber.Int(variant == 0 ? 1 : 2);
        }

        if (under == typeof(CountMap))
        {
            return variant switch
            {
                0 => CountMap.FromOrdinal([new("a", 1), new("b", 2)]),
                1 => CountMap.FromOrdinal([new("a", 1), new("c", 2)]),
                3 => CountMap.FromOrdinal([new("a", 1)]),
                _ => CountMap.FromOrdinal([new("a", 1), new("b", 3)]),
            };
        }

        if (IsList(under))
        {
            var element = under.GetGenericArguments()[0];

            // A record that holds a list of itself (Region): the nested lists are empty, so the build ends.
            var nested = building is not null && Array.IndexOf(building, element) >= 0;
            var e0 = nested ? null : Make(element, 0, asArrays, building);
            var e1 = nested ? null : Make(element, 1, asArrays, building);
            object?[] items = [];
            if (!nested)
            {
                items = variant switch
                {
                    0 => [e0, e1],
                    1 => [e0, e0],
                    2 => [e1, e0],
                    3 => [e0],
                    _ => [e0, e1, e1],
                };
            }

            var typed = Array.CreateInstance(element, items.Length);
            for (var i = 0; i < items.Length; i++)
            {
                typed.SetValue(items[i], i);
            }

            if (asArrays)
            {
                return typed;
            }

            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            foreach (var item in typed)
            {
                list.Add(item);
            }

            return list;
        }

        var concrete = under == typeof(Dialect) ? typeof(DelimitedDialect) : under;
        Type[] inside = [.. building ?? [], concrete];
        var arguments = MembersOf(concrete).Select(p => Make(p.Type, variant == 0 ? 0 : 1, asArrays, inside)).ToArray();
        return Construct(concrete, arguments);
    }

    private static void AssertDiffers(Descriptor original, Descriptor changed)
    {
        Assert.False(original == changed);
        Assert.True(original != changed);
        Assert.False(original.Equals(changed));
        Assert.False(changed.Equals(original));
        Assert.NotEqual(original, changed);
    }
}
