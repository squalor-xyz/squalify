// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace squalor.squalify.Cli.Tests;

/// <summary>
/// Package-validation hygiene that needs no pack (sq-74): the comments that name no baseline version, and the check that
/// every suppression in <c>src/squalify/CompatibilitySuppressions.xml</c> is an allowed kind with a user-guide entry.
/// These run in the ordinary suite, not the Package category, since they never run dotnet.
/// </summary>
public sealed class SuppressionTests
{
    private const string Baseline = "1.0.0";

    /// <summary>A user guide in miniature: a 1.1.0 entry that names Issue, Profiler and Descriptor.Sections, an entry for the baseline that names EncodingSection, and the provisional list.</summary>
    private const string Guide = """
        - **Model records.** Constructors and `Deconstruct` of the model records are outside the compatibility promise. The model types of planned members (`TouchstoneDialect`, `StdfSection`, `Descriptor.Sections` and `Descriptor.Stdf`, `BinarySection.Blocks`) are provisional until their profiler ships and may change in a minor.
        - **Breaking changes in 1.1.0 (library, unreleased).** (sq-1) `Issue` gained a trailing member, so `new Issue(...)` and its deconstruction change. `Profiler.Profile` now throws. The `Descriptor.Sections` list moved. `TouchstoneDialect` changed. Limits changed too, named here without a code span. `FileSource` changed.
        - **Breaking changes in 1.0.0 (library).** (sq-2) `EncodingSection` gained a member.
        """;

    public static TheoryData<string, string, bool> Rows() => new()
    {
        { "record constructor with an entry", Target("M", "Issue.#ctor(System.String)"), true },
        { "record Deconstruct with an entry", Target("M", "Issue.Deconstruct(out System.String@)"), true },
        { "record constructor named only in an entry for the baseline or older", Target("M", "EncodingSection.#ctor(System.Int32)"), false },
        { "record constructor named only in prose, without a code span", Target("M", "Limits.#ctor(System.Int32)"), false },
        { "constructor of a class that is not a record", Target("M", "FileSource.#ctor(System.String)"), false },
        { "provisional type removed, with an entry", Target("T", "TouchstoneDialect"), true },
        { "member of a provisional type, with an entry", Target("M", "TouchstoneDialect.Parse(System.String)"), true },
        { "provisional type with no entry", Target("T", "StdfSection"), false },
        { "provisional member, with an entry", Target("P", "Descriptor.Sections"), true },
        { "accessor of a provisional member, with an entry", Target("M", "Descriptor.get_Sections"), true },
        { "provisional member of a type with no entry", Target("P", "BinarySection.Blocks"), false },
        { "other member of a type that has a provisional member", Target("P", "Descriptor.Tool"), false },
        { "other method, even with an entry", Target("M", "Profiler.Profile(System.String)"), false },
        { "removal of a record, even with an entry", Target("T", "Issue"), false },
        { "member of a record other than constructor and Deconstruct", Target("P", "Issue.Message"), false },
        { "target outside the library namespace", "M:squalor.squalifx.Issue.#ctor(System.String)", false },
        { "a property that is named like Deconstruct", Target("P", "Issue.Deconstruct"), false },
        { "no suppression", string.Empty, true },
    };

    private static string Target(string kind, string rest) => $"{kind}:squalor.squalify.{rest}";

    private static string SuppressionsXml(string targets) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Suppressions xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n"
        + string.Concat(targets.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(target =>
            $"  <Suppression>\n    <DiagnosticId>CP0002</DiagnosticId>\n    <Target>{target}</Target>\n    <Left>lib/net10.0/squalor.squalify.dll</Left>\n    <Right>lib/net10.0/squalor.squalify.dll</Right>\n    <IsBaselineSuppression>true</IsBaselineSuppression>\n  </Suppression>\n"))
        + "</Suppressions>\n";

    [Theory]
    [MemberData(nameof(Rows))]
    public void Suppressions_AreAllowedKindsWithAUserGuideEntry_OverSyntheticFiles(string description, string target, bool passes)
    {
        var problems = SuppressionCheck.Problems(SuppressionsXml(target), Guide, Version.Parse(Baseline));
        Assert.True(passes == (problems.Count == 0), $"{description}: expected {(passes ? "no problem" : "a problem")}, got [{string.Join("; ", problems)}].");
    }

    [Fact]
    public void Suppressions_AreAllowedKindsWithAUserGuideEntry_WhenTheEntryIsOnlyInAnOlderRelease()
    {
        // The 1.0.0 entry names Issue too, but it describes 1.0.0, which the baseline already is.
        const string guide = "- **Breaking changes in 1.1.0 (library).** `Limits` changed.\n- **Breaking changes in 1.0.0 (library).** `Issue` changed.\n- **Breaking changes in 0.9.0 (library).** `Issue` changed.\n";
        var problems = SuppressionCheck.Problems(SuppressionsXml(Target("M", "Issue.#ctor(System.String)")), guide, Version.Parse(Baseline));
        Assert.NotEmpty(problems);
        Assert.Empty(SuppressionCheck.Problems(SuppressionsXml(Target("M", "Issue.#ctor(System.String)")), guide, Version.Parse("0.8.0")));
    }

    [Fact]
    public void Suppressions_AreAllowedKindsWithAUserGuideEntry_RejectsAFileThatIsNotSuppressions()
    {
        Assert.NotEmpty(SuppressionCheck.Problems("<Suppressions><Suppression></Suppression></Suppressions>", Guide, Version.Parse(Baseline)));
        Assert.NotEmpty(SuppressionCheck.Problems("not xml", Guide, Version.Parse(Baseline)));
        Assert.NotEmpty(SuppressionCheck.Problems("<Other/>", Guide, Version.Parse(Baseline)));
    }

    [Fact]
    public void Suppressions_AreAllowedKindsWithAUserGuideEntry()
    {
        // The real repository: with no CompatibilitySuppressions.xml this passes vacuously, and a file that sq-09 or a later
        // slice adds is held to the rule.
        var problems = SuppressionCheck.Problems(DotNetTool.RepositoryRoot());
        Assert.True(problems.Count == 0, "CompatibilitySuppressions.xml breaks the rule of docs/release.md:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Suppressions_AreCheckedAtTheFileThatPackValidationReads()
    {
        // Wiring: the check reads src/squalify/CompatibilitySuppressions.xml, next to the project, which is where package
        // validation looks for it. A wrong path would make every check vacuous, so prove that a bad file at the path is seen.
        var root = DotNetTool.RepositoryRoot();
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(SuppressionCheck.PathIn(root))!, "squalify.csproj")), "The suppressions path is not beside squalify.csproj.");

        using var work = TempFolder.Create("squalify-suppressions-");
        Directory.CreateDirectory(Path.Combine(work.Path, "src", "squalify"));
        Directory.CreateDirectory(Path.Combine(work.Path, "docs"));
        File.Copy(Path.Combine(root, "src", "squalify", "squalify.csproj"), Path.Combine(work.Path, "src", "squalify", "squalify.csproj"));
        File.Copy(Path.Combine(root, "docs", "user-guide.md"), Path.Combine(work.Path, "docs", "user-guide.md"));
        Assert.Empty(SuppressionCheck.Problems(work.Path));

        // The file name and place are written out here, not taken from the helper, so that the test pins them.
        Assert.Equal(Path.Combine(root, "src", "squalify", "CompatibilitySuppressions.xml"), SuppressionCheck.PathIn(root));
        File.WriteAllText(Path.Combine(work.Path, "src", "squalify", "CompatibilitySuppressions.xml"), SuppressionsXml(Target("M", "Profiler.Profile(System.String)")));
        Assert.NotEmpty(SuppressionCheck.Problems(work.Path));
    }

    [Fact]
    public void Suppressions_ProvisionalNames_AreReadFromTheUserGuide()
    {
        Assert.Equal(["TouchstoneDialect", "StdfSection", "Descriptor.Sections", "Descriptor.Stdf", "BinarySection.Blocks"], SuppressionCheck.ProvisionalNames(Guide));

        var real = SuppressionCheck.ProvisionalNames(File.ReadAllText(Path.Combine(DotNetTool.RepositoryRoot(), "docs", "user-guide.md")));
        foreach (var name in new[] { "TouchstoneDialect", "KeyValueDialect", "StdfDialect", "StdfTest", "Descriptor.Sections", "Descriptor.Stdf", "BinarySection.Blocks", "BinarySection.Segments" })
        {
            Assert.Contains(name, real);
        }
    }

    [Fact]
    public void Comments_DoNotNameTheBaselineVersion()
    {
        // The baseline moves up with each release, so a comment that names it goes stale; they say "the baseline" instead.
        var root = DotNetTool.RepositoryRoot();
        var version = new Regex(@"\b\d+\.\d+\.\d+\b", RegexOptions.CultureInvariant);
        var problems = new List<string>();

        var csproj = Path.Combine(root, "src", "squalify", "squalify.csproj");
        foreach (var comment in XDocument.Load(csproj).DescendantNodes().OfType<XComment>())
        {
            foreach (Match match in version.Matches(comment.Value))
            {
                problems.Add($"squalify.csproj comment names {match.Value}");
            }
        }

        var tests = File.ReadAllLines(Path.Combine(root, "tests", "squalify.Cli.Tests", "PackageTests.cs"));
        for (var i = 0; i < tests.Length; i++)
        {
            if (tests[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                foreach (Match match in version.Matches(tests[i]))
                {
                    problems.Add($"PackageTests.cs line {i + 1} comment names {match.Value}");
                }
            }
        }

        Assert.True(problems.Count == 0, "A comment hard-codes a version that the baseline bump would make stale:\n" + string.Join("\n", problems));
    }
}
