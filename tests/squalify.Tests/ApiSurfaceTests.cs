// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Text;
using squalor.squalify;

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
}
