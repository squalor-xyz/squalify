// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace squalor.squalify.Cli.Tests;

/// <summary>Checks the Markdown docs: relative links resolve and the fixtures they name exist.</summary>
public sealed class DocsTests
{
    private static readonly string[] FixtureDocs = ["docs/getting-started.md", "docs/examples.md", "README.md"];

    /// <summary>
    /// What the guides must not say about renders (owner decision, 2026-10-30: the Markdown is the only copy). The README's
    /// magic-bytes list names pdf as a file type; that is a feature, not a render, and no pattern here matches it.
    /// </summary>
    private static readonly Regex[] RenderMentions =
    [
        new(@"how-it-works\.(html|pdf)", RegexOptions.CultureInvariant),
        new(@"render-docs", RegexOptions.CultureInvariant),
        new(@"\bquire\b", RegexOptions.CultureInvariant),
        new(@"\.(pdf|html)\b", RegexOptions.CultureInvariant),
        new(@"rendering steps", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
    ];

    [Fact]
    public void Docs_RelativeLinks_Resolve()
    {
        var root = DotNetTool.RepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.TopDirectoryOnly)
            .Append(Path.Combine(root, "CONTRIBUTING.md"))
            .Append(Path.Combine(root, "AGENTS.md"))
            .Where(File.Exists)
            .Order(StringComparer.Ordinal);

        var broken = new List<string>();
        foreach (var file in files)
        {
            var text = StripCode(File.ReadAllText(file));
            var targets = Regex.Matches(text, @"\]\(\s*<?([^)\s>]*)").Select(match => match.Groups[1].Value)
                .Concat(Regex.Matches(text, @"^ {0,3}\[[^\]\n]+\]:\s*<?(\S+)", RegexOptions.Multiline).Select(match => match.Groups[1].Value));
            foreach (var target in targets)
            {
                if (target.Length == 0 || target.StartsWith('#') || Regex.IsMatch(target, "^[A-Za-z][A-Za-z0-9+.-]*:"))
                {
                    continue;
                }

                var path = target.Split('#', '?')[0];
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, path.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    broken.Add($"{Path.GetRelativePath(root, file)} -> {target}");
                }
            }
        }

        Assert.True(broken.Count == 0, $"Broken relative links: {string.Join("; ", broken)}.");
    }

    [Fact]
    public void Docs_ExampleFixtures_Exist()
    {
        var root = DotNetTool.RepositoryRoot();
        var missing = new List<string>();
        foreach (var doc in FixtureDocs)
        {
            var path = Path.Combine(root, doc.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{doc} does not exist.");
            foreach (Match match in Regex.Matches(File.ReadAllText(path), @"tests/squalify\.Tests/fixtures/[A-Za-z0-9._/-]*[A-Za-z0-9_]"))
            {
                var fixture = Path.Combine(root, match.Value.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fixture) && !Directory.Exists(fixture))
                {
                    missing.Add($"{doc} -> {match.Value}");
                }
            }
        }

        Assert.True(missing.Count == 0, $"Missing fixtures: {string.Join("; ", missing)}.");
    }

    [Fact]
    public void Docs_RenderFiles_AreRemoved()
    {
        var root = DotNetTool.RepositoryRoot();
        var present = new[] { "docs/how-it-works.html", "docs/how-it-works.pdf", "scripts/render-docs.sh" }
            .Where(file => File.Exists(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();
        Assert.True(present.Length == 0, $"The render files are still in the repository: {string.Join(", ", present)}.");
    }

    [Fact]
    public void Docs_GuidesAndReadme_MentionNoRenders()
    {
        var root = DotNetTool.RepositoryRoot();
        var problems = new List<string>();
        foreach (var file in new[] { "README.md", "CONTRIBUTING.md", "AGENTS.md", "docs/release.md" })
        {
            var text = File.ReadAllText(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var mention in RenderMentions)
            {
                foreach (Match match in mention.Matches(text))
                {
                    problems.Add($"{file} mentions '{match.Value}'");
                }
            }
        }

        Assert.True(problems.Count == 0, $"The guides still mention renders: {string.Join("; ", problems)}.");
    }

    [Fact]
    public void Readme_LinksTheHowItWorksPage()
    {
        var root = DotNetTool.RepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "README.md"));
        Assert.Contains("[How it works](https://github.com/squalor-xyz/squalify/blob/main/docs/how-it-works.md)", text, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "docs", "how-it-works.md")), "docs/how-it-works.md exists.");
    }

    [Fact]
    public void UserGuide_StatesTheCutRuleAsAContract_AndTheProfilePrefixApi()
    {
        var text = File.ReadAllText(Path.Combine(DotNetTool.RepositoryRoot(), "docs", "user-guide.md"));
        var start = text.IndexOf("## Reading only a prefix", StringComparison.Ordinal);
        Assert.True(start >= 0, "the user guide has a 'Reading only a prefix' section");
        var end = text.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
        var section = Regex.Replace(end < 0 ? text[start..] : text[start..end], @"\s+", " ");

        string[] required =
        [
            "The cut rule is a contract",
            "any later change to it is a documented breaking change",
            "Text or binary, and the charset, are decided by sniffing the bytes inside the limit",
            "A file whose bytes inside the limit sniff as binary is cut at the limit",
            "the charset sniffed from the bytes inside the limit",
            "cut after the last LF, CR or CRLF",
            "A CR that ends the limit takes the LF after it",
            "With no terminator inside the limit, the read goes on to the first terminator",
            "or the end of the file",
            "for a source whose `Length` is its true size",
            "A limit shorter than a byte order mark",
            "A change to the sniff that moves a cut is a documented breaking change",
            "The source is never read past its declared `Length`",
            "Profiler.ProfilePrefix",
            "ProfiledPrefix",
            "MaxFirstRecordBytes",
        ];
        var missing = required.Where(phrase => !section.Contains(phrase, StringComparison.Ordinal)).ToArray();
        Assert.True(missing.Length == 0, $"The user guide's prefix section lacks: {string.Join("; ", missing)}.");

        // descriptor.md points to the contract and repeats the caveat, so that it does not drift.
        var schema = Regex.Replace(File.ReadAllText(Path.Combine(DotNetTool.RepositoryRoot(), "docs", "descriptor.md")), @"\s+", " ");
        Assert.Contains("(user-guide.md#reading-only-a-prefix)", schema, StringComparison.Ordinal);
        Assert.Contains("a limit shorter than a byte order mark can sniff differently from the whole file", schema, StringComparison.Ordinal);

        // The library table names both new members.
        Assert.Contains("| `Profiler.ProfilePrefix(IProfileSource source, ProfileOptions options)` |", text, StringComparison.Ordinal);
        Assert.Contains("| `ProfiledPrefix` |", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UserGuide_LabelsTheCurrentChangesWithTheVersionInDirectoryBuildProps()
    {
        var root = DotNetTool.RepositoryRoot();
        var problems = CurrentChangesLabelProblems(PackageVersion(root), File.ReadAllText(Path.Combine(root, "docs", "user-guide.md")));
        Assert.True(problems.Count == 0, $"The user guide's current changes are mislabelled: {string.Join("; ", problems)}.");
    }

    [Theory]
    [MemberData(nameof(AcceptedCurrentChangesLabels))]
    public void CurrentChangesLabel_Accepted(string name, string version, string guide)
    {
        var problems = CurrentChangesLabelProblems(version, guide);
        Assert.True(problems.Count == 0, $"{name}: {string.Join("; ", problems)}.");
    }

    [Theory]
    [MemberData(nameof(RejectedCurrentChangesLabels))]
    public void CurrentChangesLabel_Rejected(string name, string version, string guide, string reason)
    {
        var problems = CurrentChangesLabelProblems(version, guide);
        Assert.True(problems.Any(problem => problem.Contains(reason, StringComparison.Ordinal)),
            $"{name}: no problem says '{reason}'; found {(problems.Count == 0 ? "none" : string.Join("; ", problems))}.");
    }

    private const string Api030 = "- **API added in 0.3.0 (library).** (sq-38) The public record `SketchLimits`.";
    private const string Breaking030 = "- **Breaking changes in 0.3.0 (library).** (sq-39) `ProfileOptions` gained a parameter.";
    private const string Breaking020 = "- **Breaking changes in 0.2.0 (library).** `ProfileOptions` gained a `maxBytes` parameter.";

    public static TheoryData<string, string, string> AcceptedCurrentChangesLabels() => new()
    {
        { "1.0.0 names its own entries", "1.0.0", Guide(
            "- **API added in 1.0.0 (library).** (sq-46) `Issue` gained `Regions`.",
            "- **Breaking changes in 1.0.0 (library).** (sq-46) `Issue` gained `Regions`.",
            Api030, Breaking030, Breaking020) },
        { "patch release with no changes", "0.3.1", Guide(
            "- **No API or breaking changes in 0.3.1 (library).**",
            Api030, Breaking030, Breaking020) },
        { "prerelease with no changes", "0.3.1-rc.1", Guide(
            "- **No API or breaking changes in 0.3.1-rc.1 (library).**",
            Api030, Breaking030) },
        { "patch release with its own entries", "0.3.1", Guide(
            "- **API added in 0.3.1 (library).** (sq-50) `Issue` gained a member.",
            "- **Breaking changes in 0.3.1 (library).** None.",
            Api030, Breaking030) },
        { "next version unreleased while 0.3.0 is current", "0.3.0", Guide(
            Api030,
            "- **API added in 1.0.0 (library, unreleased).** (sq-46) `Issue` gained `Regions`.",
            "- **Breaking changes in 1.0.0 (library, unreleased).** (sq-46) `Issue` gained `Regions`.",
            Breaking030, Breaking020) },
    };

    public static TheoryData<string, string, string, string> RejectedCurrentChangesLabels() => new()
    {
        { "patch release still labelled with the previous version", "0.3.1", Guide(Api030, Breaking030, Breaking020), "found 0" },
        { "older entry marked unreleased beside the no-change line", "0.3.1", Guide(
            "- **No API or breaking changes in 0.3.1 (library).**",
            "- **API added in 0.3.0 (library, unreleased).** (sq-38) The public record `SketchLimits`.",
            Breaking030, Breaking020), "marked unreleased" },
        { "current entry marked unreleased", "0.3.1", Guide(
            "- **API added in 0.3.1 (library, unreleased).** (sq-50) `Issue` gained a member.",
            "- **Breaking changes in 0.3.1 (library).** None.",
            Api030, Breaking030), "marked unreleased" },
        { "1.0.0 release still marked unreleased", "1.0.0", Guide(
            "- **API added in 1.0.0 (library, unreleased).** (sq-46) `Issue` gained `Regions`.",
            "- **Breaking changes in 1.0.0 (library).** (sq-46) `Issue` gained `Regions`.",
            Api030, Breaking030), "marked unreleased" },
        { "current entry names a newer version", "0.3.1", Guide(
            "- **API added in 0.3.1 (library).** (sq-50) `Issue` gained a member, which 0.4.0 removes.",
            "- **Breaking changes in 0.3.1 (library).** None.",
            Api030, Breaking030), "is newer than 0.3.1" },
        { "no-change line beside entries for the same version", "0.3.1", Guide(
            "- **No API or breaking changes in 0.3.1 (library).**",
            "- **API added in 0.3.1 (library).** (sq-50) `Issue` gained a member.",
            "- **Breaking changes in 0.3.1 (library).** None.",
            Api030, Breaking030), "has both" },
        { "two API entries for the same version", "0.3.1", Guide(
            "- **API added in 0.3.1 (library).** (sq-50) `Issue` gained a member.",
            "- **API added in 0.3.1 (library).** (sq-50) `Issue` gained a second member.",
            "- **Breaking changes in 0.3.1 (library).** None.",
            Api030, Breaking030), "found 2" },
        { "API entry without a breaking entry", "0.3.1", Guide(
            "- **API added in 0.3.1 (library).** (sq-50) `Issue` gained a member.",
            Api030, Breaking030), "Breaking changes in 0.3.1 (library)" },
        { "final release still labelled with its prerelease", "1.0.0", Guide(
            "- **API added in 1.0.0-rc.1 (library).** (sq-60) `Issue` gained a member.",
            "- **Breaking changes in 1.0.0-rc.1 (library).** None.",
            Api030, Breaking030), "found 0" },
        { "prerelease 1.0.0-rc.1 still labelled with the final version 1.0.0", "1.0.0-rc.1", Guide(
            "- **API added in 1.0.0 (library).** (sq-60) `Issue` gained a member.",
            "- **Breaking changes in 1.0.0 (library).** None.",
            Api030, Breaking030), "found 0" },
        { "current entry marked Unreleased in capitals", "0.3.1", Guide(
            "- **API added in 0.3.1 (library, Unreleased).** (sq-50) `Issue` gained a member.",
            "- **Breaking changes in 0.3.1 (library).** None.",
            Api030, Breaking030), "marked unreleased" },
    };

    /// <summary>Joins guide lines into the text the check reads.</summary>
    private static string Guide(params string[] lines) => string.Join("\n", lines);

    private static readonly Regex GuaranteeEntry = new(@"^- \*\*(?<kind>API added in|Breaking changes in|No API or breaking changes in) (?<version>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.]+)?) \(library", RegexOptions.CultureInvariant);
    private static readonly Regex VersionNumber = new(@"[0-9]+\.[0-9]+\.[0-9]+", RegexOptions.CultureInvariant);

    /// <summary>
    /// The problems with the library entries of "Guarantees and stability" for <paramref name="version"/>; empty when they are right.
    /// A release labels its changes with one "API added in" and one "Breaking changes in" entry, or, when it has neither,
    /// with the single "No API or breaking changes in" line. No entry for a version up to <paramref name="version"/> may say
    /// unreleased, and a current entry may not name a newer version. Entries for later versions are not checked.
    /// </summary>
    private static List<string> CurrentChangesLabelProblems(string version, string guide)
    {
        var problems = new List<string>();
        var current = CoreVersion(version);
        var api = 0;
        var breaking = 0;
        var none = 0;
        foreach (var line in guide.Split('\n').Select(text => text.TrimEnd('\r')))
        {
            var match = GuaranteeEntry.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var label = match.Groups["version"].Value;
            if (CoreVersion(label) > current)
            {
                continue;
            }

            if (line.Contains("unreleased", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"'{line}' is marked unreleased, but {label} is not later than {version}");
            }

            if (label != version)
            {
                continue;
            }

            switch (match.Groups["kind"].Value)
            {
                case "API added in":
                    api++;
                    break;
                case "Breaking changes in":
                    breaking++;
                    break;
                default:
                    none++;
                    break;
            }

            foreach (Match number in VersionNumber.Matches(line))
            {
                if (CoreVersion(number.Value) > current)
                {
                    problems.Add($"'{line}' names {number.Value}, which is newer than {version}");
                }
            }
        }

        if (none > 0)
        {
            if (api + breaking > 0)
            {
                problems.Add($"the user guide has both '- **No API or breaking changes in {version} (library).**' and entries for {version}");
            }
        }
        else
        {
            foreach (var (kind, count) in new[] { ("API added in", api), ("Breaking changes in", breaking) })
            {
                if (count != 1)
                {
                    problems.Add($"the user guide has one entry that starts with '- **{kind} {version} (library).**', found {count}");
                }
            }
        }

        return problems;
    }

    /// <summary>The numeric part of a version (the text before any prerelease or build suffix).</summary>
    private static Version CoreVersion(string version) => Version.Parse(version.Split('-', '+')[0]);

    [Fact]
    public void GettingStarted_ShowsTheVersionInDirectoryBuildProps()
    {
        var root = DotNetTool.RepositoryRoot();
        var version = PackageVersion(root);
        var text = File.ReadAllText(Path.Combine(root, "docs", "getting-started.md"));
        var start = text.IndexOf("squalify --version", StringComparison.Ordinal);
        Assert.True(start >= 0, "getting-started.md shows 'squalify --version'.");
        var match = Regex.Match(text[start..], @"```text\r?\n([^\r\n]*)\r?\n```");
        Assert.True(match.Success, "the output block follows the command.");
        Assert.Equal(version, match.Groups[1].Value);
    }

    private static string PackageVersion(string root)
    {
        var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        var match = Regex.Match(props, @"<Version>([^<]+)</Version>");
        Assert.True(match.Success, "Directory.Build.props has a <Version>.");
        return match.Groups[1].Value;
    }

    /// <summary>Removes fenced code blocks and inline code, whose brackets are not links.</summary>
    private static string StripCode(string text)
    {
        text = Regex.Replace(text, @"^(```|~~~).*?^\1[^\n]*$", string.Empty, RegexOptions.Singleline | RegexOptions.Multiline);
        return Regex.Replace(text, @"`[^`\n]*`", string.Empty);
    }
}
