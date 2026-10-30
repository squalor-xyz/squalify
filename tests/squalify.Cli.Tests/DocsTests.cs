// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;

namespace squalor.squalify.Cli.Tests;

/// <summary>Checks the Markdown docs: relative links resolve and the fixtures they name exist.</summary>
public sealed class DocsTests
{
    private static readonly string[] FixtureDocs = ["docs/getting-started.md", "docs/examples.md", "README.md"];

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
        var version = PackageVersion(root);
        var lines = File.ReadAllLines(Path.Combine(root, "docs", "user-guide.md"));

        // The library entries of "Guarantees and stability" for the current version name it, and call nothing unreleased.
        foreach (var prefix in new[] { $"- **API added in {version} (library).**", $"- **Breaking changes in {version} (library).**" })
        {
            var entries = lines.Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            Assert.True(entries.Length == 1, $"the user guide has one entry that starts with '{prefix}', found {entries.Length}.");
            Assert.DoesNotContain("unreleased", entries[0], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("1.0.0", entries[0], StringComparison.Ordinal);
        }
    }

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
