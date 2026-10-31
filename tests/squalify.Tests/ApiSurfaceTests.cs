// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using squalor.squalify;
using squalor.squalify.Tests.Schema;

namespace squalor.squalify.Tests;

/// <summary>sq-41: the public-API items settled before 1.0.0: the limits default, the exception types, file names.</summary>
public sealed class ApiSurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "squalify-api-" + Guid.NewGuid().ToString("N"));

    public ApiSurfaceTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private static ProfileOptions Options() => ProfileOptions.Create(false, Array.Empty<string>());

    // Limits (Q7)

    [Fact]
    public void Limits_Default_IsNotPublic()
    {
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
        Assert.Null(typeof(Limits).GetProperty("Default", publicStatic));
        Assert.Null(typeof(Limits).GetField("Default", publicStatic));
        Assert.Null(typeof(Limits).GetMethod("get_Default", publicStatic));

        // It is still there for the library and its tests.
        var internalDefault = typeof(Limits).GetProperty("Default", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(internalDefault);
        Assert.Same(Limits.Default, internalDefault.GetValue(null));
    }

    [Fact]
    public void SketchLimits_Default_IsNotPublic()
    {
        // The caps are not API (sq-41): a consumer reads the sketch precision from limits.sketches.
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
        Assert.Null(typeof(SketchLimits).GetProperty("Default", publicStatic));
        Assert.Null(typeof(SketchLimits).GetField("Default", publicStatic));
        Assert.Null(typeof(SketchLimits).GetMethod("get_Default", publicStatic));

        var internalDefault = typeof(SketchLimits).GetProperty("Default", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(internalDefault);
        Assert.Same(SketchLimits.Default, internalDefault.GetValue(null));
    }

    [Fact]
    public void NullSpellingLimits_Default_IsNotPublic()
    {
        // sq-42: the bounds on the null spellings are not API either; a consumer reads them from limits.nullSpellings.
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
        Assert.Null(typeof(NullSpellingLimits).GetProperty("Default", publicStatic));
        Assert.Null(typeof(NullSpellingLimits).GetField("Default", publicStatic));
        Assert.Null(typeof(NullSpellingLimits).GetMethod("get_Default", publicStatic));

        var internalDefault = typeof(NullSpellingLimits).GetProperty("Default", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(internalDefault);
        Assert.Same(NullSpellingLimits.Default, internalDefault.GetValue(null));
    }

    [Fact]
    public void PublicSurface_NoStaticMemberExposesTheDefaultCaps()
    {
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
        var exposing = new List<string>();
        foreach (var type in typeof(Limits).Assembly.GetExportedTypes())
        {
            foreach (var property in type.GetProperties(publicStatic).Where(static p => p.GetIndexParameters().Length == 0))
            {
                Check(exposing, type, property.Name, property.PropertyType, () => property.GetValue(null));
            }

            foreach (var field in type.GetFields(publicStatic))
            {
                Check(exposing, type, field.Name, field.FieldType, () => field.GetValue(null));
            }

            foreach (var method in type.GetMethods(publicStatic).Where(static m => !m.IsSpecialName && m.GetParameters().Length == 0 && !m.IsGenericMethod))
            {
                Check(exposing, type, method.Name, method.ReturnType, () => method.Invoke(null, null));
            }
        }

        Assert.True(exposing.Count == 0, "Public members that expose the default caps: " + string.Join(", ", exposing));
    }

    private static void Check(List<string> exposing, Type owner, string member, Type memberType, Func<object?> read)
    {
        if (memberType == typeof(Limits))
        {
            exposing.Add($"{owner.Name}.{member}");
            return;
        }

        // A member of another type that hands out the very object, or an equal one.
        if (memberType.IsAssignableFrom(typeof(Limits)) && memberType != typeof(object))
        {
            var value = read();
            if (ReferenceEquals(value, Limits.Default) || Equals(value, Limits.Default))
            {
                exposing.Add($"{owner.Name}.{member}");
            }
        }
    }

    [Fact]
    public void Profile_WritesTheSameLimitsSection()
    {
        var text = TestBytes.ProfileText(new BytesSource("plain.txt", TestBytes.Ascii("hello\n")));
        const string expected = """
              "limits": {
                "distinctPerField": 10000,
                "quantilePerField": 100000,
                "shapesPerField": 64,
                "shapeCharsPerField": 53312,
                "regions": 1000,
                "headerSample": 1000,
                "runawayQuote": 65536,
                "fieldCountRuns": 100000,
                "rowClasses": 64,
                "charsetTolerance": { "invalidSequences": 8, "perBytes": 65536 },
                "stdfTests": 10000,
                "budget": { "quantileValues": 10000000, "distinctValues": 2000000, "shapeChars": 16000000 },
                "sketches": { "hllPrecision": 14, "rankError": 0.001, "budgetBytes": 67108864 },
                "nullSpellings": { "max": 64, "maxChars": 256, "maxDistinctOther": 64 },
                "nullTokens": 1,
                "unitTable": 1
              },
            """;
        Assert.Contains(expected.Replace("\r\n", "\n", StringComparison.Ordinal), text, StringComparison.Ordinal);
    }

    // CountMap

    [Fact]
    public void CountMap_FromOrdinal_DuplicateKey_ThrowsSqualifyException()
    {
        var error = Assert.Throws<SqualifyException>(() => CountMap.FromOrdinal([new("a", 1), new("b", 1), new("a", 2)]));
        Assert.Contains("'a'", error.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsNotAssignableFrom<ArgumentException>(error);
    }

    [Fact]
    public void CountMap_FromNumeric_DuplicateKey_ThrowsSqualifyException()
    {
        var error = Assert.Throws<SqualifyException>(() => CountMap.FromNumeric([new(7, 1), new(3, 1), new(7, 2)]));
        Assert.Contains("'7'", error.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsNotAssignableFrom<ArgumentException>(error);
    }

    [Fact]
    public void CountMap_NullEntries_StillThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CountMap.FromOrdinal(null!));
        Assert.Throws<ArgumentNullException>(() => CountMap.FromNumeric(null!));
    }

    [Fact]
    public void CountMap_EmptyAndEnumeration_StayPublic()
    {
        // prism uses CountMap.Empty and the key/value enumeration.
        var empty = typeof(CountMap).GetProperty("Empty", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(empty);
        Assert.Same(CountMap.Empty, empty.GetValue(null));
        Assert.Empty(CountMap.Empty);
        Assert.True(typeof(IEnumerable<KeyValuePair<string, long>>).IsAssignableFrom(typeof(CountMap)));
        Assert.True(typeof(CountMap).IsPublic);

        var map = CountMap.FromOrdinal([new("b", 2), new("a", 1)]);
        Assert.Equal([new("a", 1L), new("b", 2L)], map.ToArray());
        Assert.Equal(2, map.Count);
        Assert.Equal(new KeyValuePair<string, long>("b", 2), map[1]);
    }

    // FileSource

    [Fact]
    public void FileSource_EmptyPath_ThrowsSqualifyExceptionWithAPlainMessage()
    {
        var error = Assert.Throws<SqualifyException>(() => new FileSource(string.Empty));
        Assert.Equal("Cannot profile an empty path: give the path of a data file.", error.Message);
        Assert.DoesNotContain("Parameter", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("path'", error.Message, StringComparison.Ordinal);

        var viaProfiler = Assert.Throws<SqualifyException>(() => Profiler.Profile(string.Empty, Options()));
        Assert.Equal(error.Message, viaProfiler.Message);
    }

    [Fact]
    public void FileSource_NullPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FileSource(null!));
        Assert.Throws<ArgumentNullException>(() => Profiler.Profile((string)null!, Options()));
    }

    // Names with a separator (Q9)

    [Fact]
    public void Profile_NameWithBackslash_IsRefusedWithAMessageThatNamesTheBackslash()
    {
        var error = Assert.Throws<SqualifyException>(() => Profiler.Profile(new BytesSource("a\\b.csv", TestBytes.Ascii("x\n")), Options()));
        Assert.Contains("backslash", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\", error.Message, StringComparison.Ordinal);
        Assert.Contains("file name", error.Message, StringComparison.OrdinalIgnoreCase);
        // It says why: the descriptor name never holds one, whatever the platform.
        Assert.Contains("descriptor", error.Message, StringComparison.OrdinalIgnoreCase);
        // It does not echo the name: a name that is really a path must not travel into logs through the library.
        Assert.DoesNotContain("a\\b.csv", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_NameWithSlash_IsStillRefused_AndTheMessageDoesNotBlameABackslash()
    {
        var error = Assert.Throws<SqualifyException>(() => Profiler.Profile(new BytesSource("dir/a.csv", TestBytes.Ascii("x\n")), Options()));
        Assert.Contains("file name", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'/'", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("backslash", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Profile_EmptyName_IsStillRefused()
    {
        var error = Assert.Throws<SqualifyException>(() => Profiler.Profile(new BytesSource(string.Empty, TestBytes.Ascii("x\n")), Options()));
        Assert.Contains("file name", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("empty", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Profile_NullName_IsRefusedWithAMessageThatSaysNullOrEmpty()
    {
        var error = Assert.Throws<SqualifyException>(() => Profiler.Profile(new NullNameSource(), Options()));
        Assert.Contains("null or empty", error.Message, StringComparison.Ordinal);
        Assert.Contains("file name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NullNameSource : IProfileSource
    {
        public string Name => null!;

        public long Length => 0;

        public Stream Open() => new MemoryStream();
    }

    public static TheoryData<string?> RefusedNames => new() { "a\\b.csv", "dir/a.csv", string.Empty, null };

    [Theory]
    [MemberData(nameof(RefusedNames))]
    public void ProfilePrefix_RefusesTheSameNamesAsProfile_WithTheSameMessages(string? name)
    {
        var options = ProfileOptions.Create(false, Array.Empty<string>(), maxBytes: 100);
        IProfileSource Source() => name is null ? new NullNameSource() : new BytesSource(name, TestBytes.Ascii("x\n"));

        var viaProfile = Assert.Throws<SqualifyException>(() => Profiler.Profile(Source(), options));
        var viaPrefix = Assert.Throws<SqualifyException>(() => Profiler.ProfilePrefix(Source(), options));
        Assert.Equal(viaProfile.Message, viaPrefix.Message);
        if (name is not null && name.Contains('\\', StringComparison.Ordinal))
        {
            Assert.Contains("backslash", viaPrefix.Message, StringComparison.Ordinal);
        }

        if (name is null || name.Length == 0)
        {
            Assert.Contains("null or empty", viaPrefix.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Profile_FileWhoseNameHoldsABackslash_IsRefusedWhereTheFileSystemAllowsIt()
    {
        if (OperatingSystem.IsWindows())
        {
            // A backslash is a path separator there and cannot be in a file name.
            return;
        }

        var path = Path.Combine(_root, "left\\right.csv");
        File.WriteAllText(path, "a,b\n1,2\n", new UTF8Encoding(false));

        var error = Assert.Throws<SqualifyException>(() => Profiler.Profile(path, Options()));
        Assert.Contains("backslash", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Profile_NameWithoutASeparator_IsAccepted_AndTheNameRuleIsUnchanged()
    {
        Assert.Equal("my file (1).txt", Profiler.Profile(new BytesSource("my file (1).txt", TestBytes.Ascii("x\n")), Options()).File.Name);
    }

    // Non-positional records (sq-49): ProfileOptions and FieldProfile are built by name, so a new member is not a constructor change.

    public static TheoryData<Type> NonPositionalRecords => new() { typeof(ProfileOptions), typeof(FieldProfile) };

    private static PropertyInfo[] ModelProperties(Type type)
    {
        // A record's synthesized EqualityContract is protected, so the public instance properties are the model members.
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(static p => p.Name, StringComparer.Ordinal).ToArray();
    }

    [Theory]
    [MemberData(nameof(NonPositionalRecords))]
    public void NonPositionalRecord_HasNoPositionalConstructor(Type type)
    {
        // The only public constructor takes nothing, so no member is a constructor parameter and adding a member
        // changes no constructor signature (the sq-41 style pin of the parameter set).
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var only = Assert.Single(constructors);
        Assert.Empty(only.GetParameters());
    }

    [Theory]
    [MemberData(nameof(NonPositionalRecords))]
    public void NonPositionalRecord_HasNoDeconstruct(Type type)
    {
        Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Instance), static m => m.Name == "Deconstruct");
    }

    [Theory]
    [MemberData(nameof(NonPositionalRecords))]
    public void NonPositionalRecord_EveryMemberIsInitOnly(Type type)
    {
        foreach (var property in ModelProperties(type))
        {
            Assert.NotNull(property.GetMethod);
            Assert.True(property.SetMethod is not null, $"{type.Name}.{property.Name} has no init accessor.");
            var modifiers = property.SetMethod.ReturnParameter.GetRequiredCustomModifiers();
            Assert.True(modifiers.Contains(typeof(IsExternalInit)), $"{type.Name}.{property.Name} is settable after construction.");
        }
    }

    [Fact]
    public void ProfileOptions_RequiredAndOptionalMembers_ArePinned()
    {
        AssertRequired(typeof(ProfileOptions), ["IncludeValues", "IncludeValuesFor"], ["LayoutOnly", "MaxBytes", "MaxFirstRecordBytes"]);
    }

    [Fact]
    public void FieldProfile_RequiredAndOptionalMembers_ArePinned()
    {
        AssertRequired(
            typeof(FieldProfile),
            [
                "Constant", "Count", "Distinct", "Dominant", "Flags", "Index", "Length", "Monotonic", "Name", "NameParts",
                "NullTokens", "Nulls", "NullsExcludingAllNullRows", "Shapes", "Type", "TypeVotes", "Unique",
            ],
            [
                "NameHex", "NullSpellings", "NullSpellingsOther", "NullSpellingsOtherDistinct", "NullSpellingsOtherDistinctCapped",
                "Numeric", "Patterns", "RawName", "StatsWithheld", "Temporal", "Text", "Top", "Unit",
            ]);
    }

    private static void AssertRequired(Type type, string[] required, string[] optional)
    {
        // A member added later is optional (its default is the absent value), so a consumer's `new` keeps compiling:
        // adding a required member, or any member, must be a deliberate edit of this pin.
        var properties = ModelProperties(type);
        Assert.Equal(required, properties.Where(static p => p.GetCustomAttribute<RequiredMemberAttribute>() is not null).Select(static p => p.Name).ToArray());
        Assert.Equal(optional, properties.Where(static p => p.GetCustomAttribute<RequiredMemberAttribute>() is null).Select(static p => p.Name).ToArray());
    }

    [Fact]
    public void ProfileOptions_Create_KeepsItsParameters()
    {
        var create = typeof(ProfileOptions).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(create);
        Assert.Equal(typeof(ProfileOptions), create.ReturnType);
        var parameters = create.GetParameters();
        Assert.Equal(new[] { "includeValues", "includeValuesFor", "maxBytes", "maxFirstRecordBytes", "layoutOnly" }, parameters.Select(static p => p.Name!).ToArray());
        Assert.Equal([typeof(bool), typeof(IEnumerable<string>), typeof(long?), typeof(long?), typeof(bool)], parameters.Select(static p => p.ParameterType).ToArray());
        Assert.Equal([false, false, true, true, true], parameters.Select(static p => p.IsOptional).ToArray());
    }

    // Descriptor.SchemaVersion (sq-68): a static member, not a constant, so that a later minor can change the value
    // without a rebuild of the consumers.

    [Fact]
    public void Descriptor_SchemaVersion_IsNotAConstant_AndEqualsTheSchemaFile()
    {
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
        var property = typeof(Descriptor).GetProperty(nameof(Descriptor.SchemaVersion), publicStatic);
        var field = typeof(Descriptor).GetField(nameof(Descriptor.SchemaVersion), publicStatic);

        // A const is a literal field: a caller compiles its value in, so a later value would not reach that caller.
        Assert.True(field is null || !field.IsLiteral, "Descriptor.SchemaVersion is a const (a literal field).");
        Assert.True(property is not null || field is not null, "Descriptor.SchemaVersion is neither a public static property nor a public static field.");

        // A static property or a static readonly field, of type string.
        Assert.Equal(typeof(string), property?.PropertyType ?? field!.FieldType);
        var value = property is not null ? property.GetValue(null) : field!.GetValue(null);

        // The value is the schema file's schemaVersion const, read through the schema loader of the tests.
        using var schema = JsonDocument.Parse(SchemaFiles.ReadSchema());
        var published = schema.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString();
        Assert.Equal(published, value);
    }
}
