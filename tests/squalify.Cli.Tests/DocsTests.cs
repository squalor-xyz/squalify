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

    /// <summary>Removes fenced code blocks and inline code, whose brackets are not links.</summary>
    private static string StripCode(string text)
    {
        text = Regex.Replace(text, @"^(```|~~~).*?^\1[^\n]*$", string.Empty, RegexOptions.Singleline | RegexOptions.Multiline);
        return Regex.Replace(text, @"`[^`\n]*`", string.Empty);
    }
}
