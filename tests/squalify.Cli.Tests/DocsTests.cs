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

    /// <summary>
    /// Sentences of the freeze (sq-54): descriptor.md is normative, the versioning policy names every kind of change, the known limits
    /// are written down, the unproduced members and enum values are marked planned, and the guides no longer call the schema a draft.
    /// A phrase is looked up with its white space collapsed, so a line break does not matter.
    /// </summary>
    [Fact]
    public void Docs_StateTheFreezeRules()
    {
        var root = DotNetTool.RepositoryRoot();
        var descriptor = Collapse(ReadDoc(root, "docs/descriptor.md"));
        var problems = new List<string>();

        // The status: frozen and normative, and no draft label.
        Require(problems, "descriptor.md", descriptor, "# Descriptor schema v1 Status: frozen.");
        Require(problems, "descriptor.md", descriptor, "this document is normative");
        if (Regex.IsMatch(descriptor, @"\bdraft\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            problems.Add("descriptor.md still says 'draft'");
        }

        foreach (var stale in new[] { "frozen by sq-12", "records it in the schema changelog", "pending sq-12", "sq-12 makes this policy normative", "Until sq-12 freezes v1" })
        {
            if (descriptor.Contains(stale, StringComparison.Ordinal))
            {
                problems.Add($"descriptor.md still says '{stale}'");
            }
        }

        // The versioning policy.
        var policy = Collapse(Section(ReadDoc(root, "docs/descriptor.md"), "## Versioning policy"));
        foreach (var phrase in new[]
        {
            "This policy is normative",
            "adding an enum value is a minor change",
            "a change that moves `scope.bytesRead` is a change of meaning",
            "the sniff of the bytes inside the limit",
            "a member may become null only behind a documented top-level marker such as `layoutOnly: true`",
            "otherwise making an always-present member null is a major change",
            "Sketched values (quantiles, approximate distinct and top counts) and the precision mapping in `limits.sketches` may be refined in a minor; keys, types and the stated error bounds stay",
            "Replacing a sketch whose stated bound is deterministic (the `rankError` of the quantiles, the `maxError` of `top`) with one whose bound is only probabilistic (for example KLL) is a major change",
            "`schemaVersion` is `major.minor`, in lock-step with the squalify minor: it is the major.minor of the squalify release that first writes the schema it describes",
            "The first schema-changing commit after a release bumps it, and later ones before the next release share the bump",
            "Adding a key, written always or only in some cases, is a minor change",
            "are major changes, except the widenings listed below",
            "A profiler added in a minor may claim files an earlier minor described as text, binary or delimited; their family, dialect, sections and hashes change (minor); `fingerprint.version` bumps only when a projection of an existing family changes",
            "The model types of planned members (`TouchstoneDialect`, `KeyValueDialect`, `StdfDialect`, `KeyValueSection`, `BlockStats`, `StdfSection`, `StdfRecordCount`, `StdfTest`, `StdfHints`, `Descriptor.Sections` and `Descriptor.Stdf`, `BinarySection.Blocks` and `BinarySection.Segments`) are provisional until their profiler ships and may change in a minor",
            "New options are added to `ProfileOptions` as init members, and the parameter list of `ProfileOptions.Create` never changes after 1.0",
            "These widen constructs that exist in 1.0. Each is a minor change.",
            "absent only in tables of a family that 1.0 does not write; a delimited table keeps it",
            "A refinement changes no key and no type, so it bumps no `schemaVersion`",
            "`format.dialect` becomes a `oneOf` over the per-family dialects",
            "`hints.stdf` changes from `null` to an object",
            "`table.rowClasses` changes from required to optional",
            "The `const` of `schemaVersion` changes at each minor that changes the schema, to that release's major.minor (see Rules); `Descriptor.SchemaVersion` changes with it",
            "a release that carries a schema change is a minor or major release, never a patch",
            "edited only in a release that writes a new `schemaVersion`, to name that release's tag",
            "a release that leaves the schema as it is keeps the `$id` of the release that first wrote the schema",
            "consumers must not validate with the closed schema",
            "kind label values may change for the same input in a minor release, and rule ids are permanent",
            "constructors and `Deconstruct` of the model records are outside the compatibility promise",
            "tag-pinned raw URL",
            "is not packed in the NuGet packages",
        })
        {
            Require(problems, "descriptor.md, Versioning policy", policy, phrase);
        }

        // One wording of the schemaVersion rule everywhere, and the key rule without its older form.
        foreach (var stale in new[] { "with every minor release", "making an optional key required", "removing, renaming or changing the meaning or the type of a key", "A sketch with only a probabilistic bound", "changes at each minor, in lock-step", "edited at each release" })
        {
            if (policy.Contains(stale, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"descriptor.md, Versioning policy still says '{stale}'");
            }
        }

        var minorBullet = ReadDoc(root, "docs/descriptor.md").Split('\n').FirstOrDefault(static l => l.StartsWith("- **Minor changes.**", StringComparison.Ordinal)) ?? string.Empty;
        if (minorBullet.Length == 0 || minorBullet.Contains("adding an enum value is a minor change", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("descriptor.md has no '- **Minor changes.**' bullet, or it repeats 'adding an enum value is a minor change' (the sentence is pinned in the consumers rule)");
        }

        // Internal slice ids and slice-relative wording are gone outside the history of the draft period.
        var current = ReadDoc(root, "docs/descriptor.md");
        var history = current.IndexOf("#### Before 1.0", StringComparison.Ordinal);
        var currentText = history < 0 ? current : current[..history];
        foreach (var stale in new[] { "sq-10", "sq-15", "later slices", "added by sq", "except those of later slices" })
        {
            if (currentText.Contains(stale, StringComparison.Ordinal))
            {
                problems.Add($"descriptor.md still says '{stale}'");
            }
        }

        if (new[] { "touchstone", "keyValue", "stdf" }.Any(family => currentText.Contains("| `format.dialect." + family + "`", StringComparison.Ordinal)))
        {
            problems.Add("descriptor.md has a key-order row for a planned dialect (the dialects are marked in the by-family table)");
        }

        foreach (var row in new[] { "| touchstone (planned) |", "| keyValue (planned) |", "| stdf (planned) |" })
        {
            Require(problems, "descriptor.md, by-family dialect table", descriptor, row);
        }

        var changelog = Section(ReadDoc(root, "docs/descriptor.md"), "### Changelog");
        if (changelog.Length == 0)
        {
            problems.Add("descriptor.md has no section '### Changelog'");
        }
        else
        {
            if (!Regex.IsMatch(changelog, @"^#### 1\.0\b", RegexOptions.Multiline | RegexOptions.CultureInvariant))
            {
                problems.Add("descriptor.md has no changelog entry '#### 1.0' under '### Changelog'");
            }

            Require(problems, "descriptor.md, Changelog", Collapse(changelog), "One entry per `schemaVersion`, newest first; the version of an entry is the major.minor of the squalify release that first writes it");
        }

        // The known limits that freeze with v1.
        var limits = Collapse(Section(ReadDoc(root, "docs/descriptor.md"), "## Known limits of v1"));
        foreach (var phrase in new[]
        {
            "`Site_A` has the unit `A` (amps)",
            "`Temp_F` has the unit `F` (farad)",
            "`DEGC` is a unit and `DEG` is not",
            "`limits.unitTable` stays at 1",
            "A `?` in a shape mask is a literal `?` or a character with no class",
            "any non-ASCII character, a control character, each half of a surrogate pair, `{` or `}`",
            "A cut mask has exactly 65 tokens and ends in a bare `+`",
        })
        {
            Require(problems, "descriptor.md, Known limits of v1", limits, phrase);
        }

        // What no profiler produces yet is marked planned, and what is produced is not.
        var planned = Collapse(Section(ReadDoc(root, "docs/descriptor.md"), "## Planned for a 1.x minor release"));
        foreach (var phrase in new[]
        {
            "`stdf`", "`hints.stdf`", "`sections`", "`binary.blocks`", "`binary.segments`",
            "`format.dialect`", "touchstone", "keyValue", "`format.family`", "`kind.label`",
            "`limits.stdfTests` is a produced member",
        })
        {
            Require(problems, "descriptor.md, Planned for a 1.x minor release", planned, phrase);
        }

        // The guides.
        var userGuide = ReadDoc(root, "docs/user-guide.md");
        var stability = Collapse(Section(userGuide, "## Guarantees and stability"));
        foreach (var phrase in new[]
        {
            "constructors and `Deconstruct` of the model records are outside the compatibility promise",
            "reading members is stable",
            "the stable construction paths are",
            "`FieldProfile` built by name with init members",
            "The model types of planned members (`TouchstoneDialect`, `KeyValueDialect`, `StdfDialect`, `KeyValueSection`, `BlockStats`, `StdfSection`, `StdfRecordCount`, `StdfTest`, `StdfHints`, `Descriptor.Sections` and `Descriptor.Stdf`, `BinarySection.Blocks` and `BinarySection.Segments`) are provisional until their profiler ships and may change in a minor",
            "New options are added to `ProfileOptions` as init members, and the parameter list of `ProfileOptions.Create` never changes after 1.0",
            "adding a key, written always or only in some cases, or an enum value is a minor change (it bumps the schema's minor version)",
            "A file that a new profiler reclaims (one an earlier minor described as text, binary or delimited) changes its hashes without a `fingerprint.version` bump",
        })
        {
            Require(problems, "user-guide.md, Guarantees and stability", stability, phrase);
        }

        Require(problems, "user-guide.md", Collapse(userGuide), "but reading does not:");
        Require(problems, "user-guide.md", Collapse(userGuide), "are planned for 1.x minor releases ([the list](descriptor.md#planned-for-a-1x-minor-release))");
        if (Collapse(userGuide).Contains("They are not profiled in 0.1.x", StringComparison.Ordinal))
        {
            problems.Add("user-guide.md still says 'They are not profiled in 0.1.x'");
        }

        if (Collapse(userGuide).Contains("but reading is not:", StringComparison.Ordinal))
        {
            problems.Add("user-guide.md still says 'but reading is not:'");
        }

        // design.md says in its decisions table that the other formats and the kind rules are planned, and links the list.
        var design = ReadDoc(root, "docs/design.md").Split('\n');
        foreach (var row in new[] { "| v1 formats |", "| Kind label |" })
        {
            var line = design.FirstOrDefault(l => l.StartsWith(row, StringComparison.Ordinal));
            if (line is null)
            {
                problems.Add($"design.md has no decisions-table row '{row}'");
            }
            else
            {
                Require(problems, $"design.md, row '{row}'", Collapse(line), "[planned for a 1.x minor release](descriptor.md#planned-for-a-1x-minor-release)");
            }
        }

        var how = ReadDoc(root, "docs/how-it-works.md");
        Require(problems, "how-it-works.md", Collapse(how), "The descriptor schema v1 is frozen");
        Require(problems, "how-it-works.md", Collapse(how), "This page describes main after v0.3.0");
        Require(problems, "how-it-works.md", Collapse(how), "its enum values against the code and lists in the tests");
        if (Collapse(how).Contains("describes the code of v0.3.0", StringComparison.Ordinal))
        {
            problems.Add("how-it-works.md still says 'describes the code of v0.3.0'");
        }

        // A diagram marks (planned) until the code exists: FormatRegistry.Default registers only delimited, text and binary.
        foreach (var marker in new[] { "Profilers[\"Format profilers", "--> P2[", "--> P3[", "--> P4[", "A1[\"Header names" })
        {
            var line = how.Split('\n').FirstOrDefault(l => l.Contains(marker, StringComparison.Ordinal));
            if (line is null)
            {
                problems.Add($"how-it-works.md has no diagram line with '{marker}'");
            }
            else if (!line.Contains("(planned)", StringComparison.Ordinal))
            {
                problems.Add($"how-it-works.md: the diagram line with '{marker}' does not mark the unbuilt profiler (planned)");
            }
        }

        // The release steps and CONTRIBUTING say the same about schemaVersion as the policy.
        var release = Collapse(ReadDoc(root, "docs/release.md"));
        Require(problems, "release.md", release, "and, in a minor release that changed the schema, the `schemaVersion` value");
        Require(problems, "release.md", release, "Edit the `$id` of `schema/descriptor-v1.schema.json` only in a release that writes a new `schemaVersion`");
        Require(problems, "release.md", release, "a release that leaves the schema as it is keeps the `$id` of the release that first wrote it");
        if (Collapse(userGuide).Contains("adding optional keys or enum values bumps", StringComparison.Ordinal))
        {
            problems.Add("user-guide.md still says 'adding optional keys or enum values bumps'");
        }
        if (release.Contains("for a new minor schema version", StringComparison.Ordinal))
        {
            problems.Add("release.md still says 'for a new minor schema version'");
        }

        Require(problems, "CONTRIBUTING.md", Collapse(ReadDoc(root, "CONTRIBUTING.md")), "the first schema-changing commit after a release bumps it, and later ones before the next release share the bump");

        foreach (var file in new[] { "README.md", "docs/getting-started.md", "docs/user-guide.md", "docs/design.md", "docs/how-it-works.md" })
        {
            var text = Collapse(ReadDoc(root, file));
            foreach (Match match in SchemaDraftMentions.Matches(text))
            {
                problems.Add($"{file} still calls the schema a draft: '{match.Value}'");
            }
        }

        Assert.True(problems.Count == 0, "The freeze is not stated:\n" + string.Join("\n", problems));
    }

    // "the schema ... draft" or "draft ... schema" within one sentence, and the "until the v1 freeze" wording that went with it.
    private static readonly Regex SchemaDraftMentions = new(
        @"schema[^.]{0,60}\bdraft\b|\bdraft\b[^.]{0,40}schema|until the v1 (schema )?freeze",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static string ReadDoc(string root, string relative) =>
        File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    private static void Require(List<string> problems, string where, string text, string phrase)
    {
        if (!text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{where} lacks '{phrase}'");
        }
    }

    /// <summary>The text of a section: from its heading to the next heading of the same or a higher level; empty when there is none.</summary>
    private static string Section(string text, string heading)
    {
        var start = text.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var level = heading.TakeWhile(static c => c == '#').Count();
        var rest = text[(start + heading.Length + 2)..];
        var next = Regex.Match(rest, "\n#{1," + level.ToString(System.Globalization.CultureInfo.InvariantCulture) + "} ");
        return next.Success ? text.Substring(start, heading.Length + 2 + next.Index) : text[start..];
    }

    /// <summary>
    /// The README's quick start is real output: the command's fixture is the one the README links, a real run of the CLI on it
    /// gives the JSON that the README shows (the command's <c>jq</c> filter is applied here in code, so the test does not need
    /// <c>jq</c>), and the sentences about the file (its lines, preamble, data rows) and about the length of the whole
    /// descriptor are true of that run.
    /// </summary>
    [Fact]
    public async Task Readme_Examples_AreRealOutput()
    {
        var root = DotNetTool.RepositoryRoot();
        var readme = ReadDoc(root, "README.md");

        // The command and the output block that follows it.
        var command = Regex.Match(readme, @"```bash\n(squalify profile (?<file>[^\s|]+) \| jq -c '(?<filter>[^']+)')\n```\n\n```json\n(?<output>[^\n]+)\n```");
        Assert.True(command.Success, "the README shows a 'squalify profile <file> | jq -c ...' command followed by its json output");
        const string Filter = "{family: .format.family, rows: .table.rows, regions: [.layout.regions[] | [.kind, .lines[0], .lines[1]]], issues}";
        Assert.True(command.Groups["filter"].Value == Filter, "the README's jq filter changed: update the projection in this test to match it.\n" + command.Groups["filter"].Value);

        // The fixture the command names is the one the README links.
        var fixtureName = command.Groups["file"].Value;
        var link = Regex.Match(readme, @"\[`(?<path>tests/squalify\.Tests/fixtures/[^`]*/" + Regex.Escape(fixtureName) + @")`\]\(https://github\.com/squalor-xyz/squalify/blob/main/\k<path>\)");
        Assert.True(link.Success, $"the README links the fixture {fixtureName} by its repository path");
        var fixture = Path.Combine(root, link.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar));

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CommandFactory.InvokeAsync(["profile", fixture], stdout, stderr);
        Assert.Equal(0, code);
        var descriptor = stdout.ToString();
        using var json = System.Text.Json.JsonDocument.Parse(descriptor);
        var top = json.RootElement;

        var regions = top.GetProperty("layout").GetProperty("regions").EnumerateArray().ToList();
        var projected = new System.Text.Json.Nodes.JsonObject
        {
            ["family"] = top.GetProperty("format").GetProperty("family").GetString(),
            ["rows"] = top.GetProperty("table").GetProperty("rows").GetInt64(),
            ["regions"] = new System.Text.Json.Nodes.JsonArray(regions.Select(static r => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonArray(
                r.GetProperty("kind").GetString(), r.GetProperty("lines")[0].GetInt64(), r.GetProperty("lines")[1].GetInt64())).ToArray()),
            ["issues"] = System.Text.Json.Nodes.JsonNode.Parse(top.GetProperty("issues").GetRawText()),
        };
        Assert.Equal(projected.ToJsonString(), command.Groups["output"].Value);

        // The sentences about the fixture and about the length of the descriptor.
        var sentence = Regex.Match(readme, @"a (?<pre>\d+)-line preamble, a header, a units row, (?<data>\d+) data rows and a footer");
        Assert.True(sentence.Success, "the README describes the fixture's preamble, header, units row, data rows and footer");
        Assert.Equal(long.Parse(sentence.Groups["pre"].Value, System.Globalization.CultureInfo.InvariantCulture), RegionLines(regions, "preamble"));
        Assert.Equal(long.Parse(sentence.Groups["data"].Value, System.Globalization.CultureInfo.InvariantCulture), RegionLines(regions, "data"));

        var fixtureLines = File.ReadAllLines(fixture).Length;
        var lengths = Regex.Match(readme, @"about (?<descriptor>\d+) lines for this (?<file>\d+)-line file");
        Assert.True(lengths.Success, "the README says how long the descriptor is for this file");
        Assert.Equal(fixtureLines, int.Parse(lengths.Groups["file"].Value, System.Globalization.CultureInfo.InvariantCulture));
        AssertAbout(descriptor.Split('\n').Length - 1, int.Parse(lengths.Groups["descriptor"].Value, System.Globalization.CultureInfo.InvariantCulture), "README.md");

        // getting-started.md makes the same claim about the same fixture.
        var guide = ReadDoc(root, "docs/getting-started.md");
        var guideLength = Regex.Match(guide, @"The file is about (?<descriptor>\d+) lines of JSON");
        Assert.True(guideLength.Success, "getting-started.md says how long the descriptor is");
        AssertAbout(descriptor.Split('\n').Length - 1, int.Parse(guideLength.Groups["descriptor"].Value, System.Globalization.CultureInfo.InvariantCulture), "docs/getting-started.md");
    }

    private static long RegionLines(List<System.Text.Json.JsonElement> regions, string kind)
    {
        var region = regions.Single(r => r.GetProperty("kind").GetString() == kind);
        return region.GetProperty("lines")[1].GetInt64() - region.GetProperty("lines")[0].GetInt64() + 1;
    }

    // "about N": within a tenth of the real count.
    private static void AssertAbout(int actual, int stated, string where)
    {
        Assert.True(Math.Abs(actual - stated) <= actual / 10, $"{where} says 'about {stated} lines', the real descriptor has {actual}.");
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
