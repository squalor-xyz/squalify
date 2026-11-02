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
            "are major changes, except the widenings listed below and the rules that follow",
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
            "set by the first commit that bumps `schemaVersion` after a release, to the final tag `v<schemaVersion>.0` of the release that will write it",
            "a release that leaves the schema as it is keeps the `$id` of the release that first wrote the schema",
            "and so does every patch and prerelease of the series, so a release candidate and its final share one `$id`",
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

        // sq-09: the touchstone and keyValue dialects are produced; only stdf is still marked planned.
        foreach (var row in new[] { "| touchstone |", "| keyValue |", "| stdf (planned) |" })
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
            "`stdf`", "`hints.stdf`", "`binary.blocks`", "`binary.segments`",
            "`format.dialect`", "`format.family`", "`kind.label`",
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
        Require(problems, "user-guide.md", Collapse(userGuide), "is planned for a 1.x minor release ([the list](descriptor.md#planned-for-a-1x-minor-release))");
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
        var packageVersion = Version.Parse(PackageVersion(root).Split('-', '+')[0]);
        Require(problems, "how-it-works.md", Collapse(how), $"This page describes squalify {packageVersion.Major}.{packageVersion.Minor}.x");
        Require(problems, "how-it-works.md", Collapse(how), "its enum values against the code and lists in the tests");
        if (Collapse(how).Contains("describes the code of v0.3.0", StringComparison.Ordinal))
        {
            problems.Add("how-it-works.md still says 'describes the code of v0.3.0'");
        }

        // A diagram marks (planned) until the code exists: FormatRegistry.Default registers touchstone, keyValue, delimited, text and binary
        // (sq-09), so the STDF profiler is the one that is still planned.
        foreach (var marker in new[] { "Profilers[\"Format profilers", "--> P2[", "A1[\"Header names" })
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
        Require(problems, "release.md", release, "Check that the `$id` of `schema/descriptor-v1.schema.json` is already the raw URL of the final tag of the series");
        Require(problems, "release.md", release, "a release that leaves the schema as it is keeps the `$id` of the release that first wrote it");
        Require(problems, "release.md", release, "and edit it only if it is not");
        Require(problems, "release.md", release, "so every patch and every prerelease of the series, the rc included, keeps the `$id` of its `x.y.0`, and the rc and the final share one `$id`");
        Require(problems, "release.md", release, "re-record them (`SQUALIFY_UPDATE_GOLDEN=1 dotnet test squalify.sln -c Release --filter \"Category!=Corpus&Category!=Large\"`)");
        Require(problems, "release.md", release, "In a minor or major release also set the `Status (vX.Y.x)` banner of `docs/how-it-works.md` to the new series");
        Require(problems, "release.md", release, "runs the tests (everything except `Category=Corpus`, `Category=Package` and `Category=Large`, then the Package tests as their own step)");
        if (Collapse(userGuide).Contains("adding optional keys or enum values bumps", StringComparison.Ordinal))
        {
            problems.Add("user-guide.md still says 'adding optional keys or enum values bumps'");
        }
        if (release.Contains("for a new minor schema version", StringComparison.Ordinal))
        {
            problems.Add("release.md still says 'for a new minor schema version'");
        }

        Require(problems, "CONTRIBUTING.md", Collapse(ReadDoc(root, "CONTRIBUTING.md")), "the first schema-changing commit after a release bumps it and points `$id` at `v<schemaVersion>.0`, the final tag of the coming release (the schema tests tie it to the const), and later ones before the next release share the bump. The `$id` is the same for every patch and prerelease of the series, so an rc and its final share one `$id`, and a release only checks it");

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

    /// <summary>
    /// Package validation (sq-71): release.md says what the check is, where the baseline comes from, which 1.x changes may take a
    /// suppression (the positional records' constructors and Deconstruct, the provisional types; any other CP error is a bug),
    /// how one is added (with a user-guide "Breaking changes" entry), and the baseline lifecycle (a release step moves it up after
    /// each release); CONTRIBUTING.md says the same in brief.
    /// </summary>
    [Fact]
    public void Docs_DescribePackageValidation()
    {
        var root = DotNetTool.RepositoryRoot();
        var problems = new List<string>();
        var releaseText = ReadDoc(root, "docs/release.md");
        var release = Collapse(releaseText);
        var section = Collapse(Section(releaseText, "## Package validation"));
        if (section.Length == 0)
        {
            problems.Add("release.md has no '## Package validation' section");
        }

        Require(problems, "release.md", section, "`squalor.squalify` is packed with package validation against a baseline release from nuget.org (`EnablePackageValidation` and `PackageValidationBaselineVersion` in `src/squalify/squalify.csproj`)");
        Require(problems, "release.md", section, "a change that removes or renames a public member or changes a signature fails `dotnet pack`");
        Require(problems, "release.md", section, "The baseline package is fetched from nuget.org at restore, not at pack");
        Require(problems, "release.md", section, "the CLI tool project is not validated");
        Require(problems, "release.md", section, "Only two kinds of change may take a suppression in 1.x: (a) constructors and `Deconstruct` of the positional model records, which are outside the compatibility promise");
        Require(problems, "release.md", section, "and (b) the provisional model types that the user guide says \"may change in a minor\" (`TouchstoneDialect`, `KeyValueDialect`, `StdfDialect`, `KeyValueSection`, `BlockStats`, `StdfSection`, `StdfRecordCount`, `StdfTest`, `StdfHints`, `Descriptor.Sections` and `Descriptor.Stdf`, `BinarySection.Blocks` and `BinarySection.Segments`");
        Require(problems, "release.md", section, "Any other `CP` error in 1.x is a bug to fix, not to suppress: removing, renaming or changing the signature of any other public member needs a major version");
        Require(problems, "release.md", section, "-p:ApiCompatGenerateSuppressionFile=true");
        Require(problems, "release.md", section, "`src/squalify/CompatibilitySuppressions.xml`");
        Require(problems, "release.md", section, "a `Breaking changes in X (library)` entry in the user guide in the same commit");
        Require(problems, "release.md", section, "A suppression that is no longer needed fails the pack (`error : Unnecessary suppressions found`)");
        Require(problems, "release.md", section, "After each release is listed on nuget.org, the next commit to `main` sets `PackageValidationBaselineVersion` to that release and removes the suppressions the pack then reports as unnecessary");
        Require(problems, "release.md", section, "A new major release moves the baseline to X.0.0 and drops the suppressions");
        Require(problems, "release.md", release, "5. When nuget.org lists the release, move the package validation baseline up: commit to `main` the new `PackageValidationBaselineVersion` in `src/squalify/squalify.csproj`");
        Require(problems, "release.md", release, "[Package validation](#package-validation)");
        foreach (var stale in new[] { "The baseline stays 1.0.0", "the baseline is not bumped", "Leave `PackageValidationBaselineVersion` alone" })
        {
            if (release.Contains(stale, StringComparison.Ordinal))
            {
                problems.Add($"release.md still says '{stale}'");
            }
        }

        var contributing = Collapse(ReadDoc(root, "CONTRIBUTING.md"));
        Require(problems, "CONTRIBUTING.md", contributing, "`dotnet pack` of the library runs package validation against a baseline release from nuget.org (`PackageValidationBaselineVersion` in `src/squalify/squalify.csproj`)");
        Require(problems, "CONTRIBUTING.md", contributing, "After each release is listed on nuget.org, the next commit to `main` moves the baseline up to it and drops the suppressions that are no longer needed");
        Require(problems, "CONTRIBUTING.md", contributing, "Only constructors and `Deconstruct` of the positional model records and the provisional model types of the user guide may be suppressed in 1.x");
        Require(problems, "CONTRIBUTING.md", contributing, "[Package validation](docs/release.md#package-validation)");

        Assert.True(problems.Count == 0, "Package validation is not documented:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// Package validation follow-ups (sq-74): only a stable release moves <c>PackageValidationBaselineVersion</c> (step 5 of
    /// release.md says so itself, since a prerelease value fails the Package test that parses it as a Version), and the docs name the
    /// test that checks every suppression for an allowed kind and a user-guide entry.
    /// </summary>
    [Fact]
    public void Docs_SayOnlyStableReleasesMoveTheBaseline_AndNameTheSuppressionCheck()
    {
        var root = DotNetTool.RepositoryRoot();
        var problems = new List<string>();
        var releaseText = ReadDoc(root, "docs/release.md");
        var step5 = Collapse(Regex.Match(releaseText, @"^5\. .*$", RegexOptions.Multiline).Value);
        var section = Collapse(Section(releaseText, "## Package validation"));
        var contributing = Collapse(ReadDoc(root, "CONTRIBUTING.md"));

        Require(problems, "release.md step 5", step5, "Only a stable release moves `PackageValidationBaselineVersion`");
        Require(problems, "release.md step 5", step5, "a prerelease such as `1.1.0-rc.1` never does");
        Require(problems, "release.md", section, "Only a stable release is a baseline");
        Require(problems, "release.md", section, "a constructor or `Deconstruct` of a positional model record");
        Require(problems, "CONTRIBUTING.md", contributing, "`Deconstruct` of the positional model records");
        Require(problems, "release.md", section, "`Suppressions_AreAllowedKindsWithAUserGuideEntry`");
        Require(problems, "CONTRIBUTING.md", contributing, "Only a stable release moves the baseline");
        Require(problems, "CONTRIBUTING.md", contributing, "`Suppressions_AreAllowedKindsWithAUserGuideEntry`");

        Assert.True(problems.Count == 0, "The baseline rule or the suppression check is not documented:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// sq-09 (fix round 1): the docs say exactly which files change family in 1.1.0, and none says that a delimited file keeps its family
    /// without the exception (a whitespace file of "Key = Value" lines that 1.0.0 scored delimited 0.7 becomes keyValue 0.8).
    /// </summary>
    [Fact]
    public void Docs_SayWhichFilesChangeFamilyIn110()
    {
        var root = DotNetTool.RepositoryRoot();
        var problems = new List<string>();
        var descriptor = Collapse(ReadDoc(root, "docs/descriptor.md"));
        Require(problems, "descriptor.md, Changelog 1.1", descriptor, "Files that change family in 1.1.0 are exactly these.");
        Require(problems, "descriptor.md, Changelog 1.1", descriptor, "(a file of `Lot = A12` lines: delimited, score 0.7); it is now `keyValue`, score 0.8");
        Require(problems, "descriptor.md, Changelog 1.1", descriptor, "a bare `#` or `# hz` does not qualify, and the file stays what it was");
        Require(problems, "descriptor.md, Changelog 1.1", descriptor, "a comma, tab, semicolon or bar table, with or without a key=value preamble, stays delimited");
        Forbid(problems, "descriptor.md", descriptor, "a delimited file keeps its family, its score and its descriptor, apart from");

        var guide = Collapse(ReadDoc(root, "docs/user-guide.md"));
        Require(problems, "user-guide.md, Formats", guide, "Two kinds of file change family in 1.1.0");
        Require(problems, "user-guide.md, Formats", guide, "a file of `Lot = A12` lines was delimited, score 0.7; it is now `keyValue`, score 0.8");
        Require(problems, "user-guide.md, Breaking changes in 1.1.0", guide, "Files that change family: a whitespace-delimited file of `Key = Value` or `Key: Value` lines that 1.0.0 scored delimited 0.7 is now `keyValue` (0.8)");
        Forbid(problems, "user-guide.md", guide, "a delimited file keeps its family.");
        Assert.True(problems.Count == 0, "The docs do not say which files change family:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// sq-09 (fix round 2): the docs follow the code on the noise region (a region kind of its own, not a labelled block), the v1 noise
    /// rule, the cap on key=value sections, the scope of row classes and the Touchstone scoring without the extension.
    /// </summary>
    [Fact]
    public void Docs_FollowTheNoiseRegionAndTheScoringRules()
    {
        var root = DotNetTool.RepositoryRoot();
        var problems = new List<string>();
        var descriptor = Collapse(ReadDoc(root, "docs/descriptor.md"));
        Require(problems, "descriptor.md", descriptor, "- `noise`: the noise data of a two-port file.");
        Require(problems, "descriptor.md", descriptor, "or equal to it on a line of exactly 5 values");
        Require(problems, "descriptor.md", descriptor, "`regions` is the most blocks of a delimited file that are recognised, and the most sections of a key=value document that are listed");
        Require(problems, "descriptor.md", descriptor, "`headerConvention` (delimited tables with a header), `maxLineLength` (text, delimited, touchstone and keyValue; in characters)");
        Forbid(problems, "descriptor.md", descriptor, "`block` with `label` `noise`", "the line whose frequency is lower than the last network frequency", "`label` (block: `noise`)");

        var guide = Collapse(ReadDoc(root, "docs/user-guide.md"));
        Require(problems, "user-guide.md", guide, "without a Touchstone extension the first line must be `[Version]` or an option line with at least two recognised tokens");
        Forbid(problems, "user-guide.md", guide, "`Region` gained the optional init member `Label`");

        var how = Collapse(ReadDoc(root, "docs/how-it-works.md"));
        Require(problems, "how-it-works.md", how, "Row classes are for delimited tables only");
        Require(problems, "how-it-works.md", how, "when the first line is [Version] or an option line of 2+ recognised tokens, then numeric data");
        Require(problems, "how-it-works.md", how, "(1 + N(N+1) for Lower/Upper)");
        Assert.True(problems.Count == 0, "The docs do not follow the code:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// Release 1.0.0 (sq-53): the docs speak of the freeze and of the version in the present tense. None of them says that the freeze
    /// "takes effect with 1.0.0", that "before 1.0 a minor version may still change the descriptor", or that the project is "early
    /// (0.3.x)"; the how-it-works banner names the release series of <c>Directory.Build.props</c>.
    /// </summary>
    [Fact]
    public void Docs_SpeakOfTheFreezeAndTheVersionInThePresentTense()
    {
        var root = DotNetTool.RepositoryRoot();
        var problems = new List<string>();

        var descriptor = Collapse(ReadDoc(root, "docs/descriptor.md"));
        Require(problems, "descriptor.md", descriptor, "The schema is frozen from squalify 1.0.0; the 0.x releases also wrote `schemaVersion` `1.0` but changed the descriptor without a bump, so tell them apart by `tool.version`.");
        Require(problems, "descriptor.md", descriptor, "Since 0.3.0 only `layoutOnly`, `encoding.invalidBytesTolerated`, `issues[].regions` and the `firstLines` of `invalidUtf8` and `invalidUnicode` are new; `limits.sketches` and `limits.nullSpellings` were written by 0.3.0 already.");
        Forbid(problems, "descriptor.md", descriptor, "takes effect with squalify 1.0.0", "may still change the descriptor", "the first release that writes it", "before 1.0 a minor");

        var guide = Collapse(ReadDoc(root, "docs/user-guide.md"));
        Require(problems, "user-guide.md", guide, "Descriptor schema v1 is frozen (`schemaVersion` `1.0` from 1.0.0, `1.1` from 1.1.0, which adds the Touchstone and key=value families). In 1.x, adding a key, written always or only in some cases, or an enum value is a minor change");
        Require(problems, "user-guide.md", guide, "**A descriptor changed after an upgrade.** Expected across squalify versions: the [versioning policy](descriptor.md#versioning-policy) lists what may change in 1.x (new optional keys and enum values, refined sketched values, other kind labels), and the 0.x releases changed the descriptor without a version bump.");
        Forbid(problems, "user-guide.md", guide, "the freeze takes effect with 1.0.0", "before 1.0 a minor", "1.0.0 (library, unreleased)");

        var readme = Collapse(ReadDoc(root, "README.md"));
        Require(problems, "README.md", readme, "> **Status: stable (1.x).**");
        Require(problems, "README.md", readme, "the library API and the descriptor schema v1 are frozen");
        Forbid(problems, "README.md", readme, "early (0.3.x)", "takes effect with 1.0.0", "before 1.0 a minor");

        var gettingStarted = Collapse(ReadDoc(root, "docs/getting-started.md"));
        Require(problems, "getting-started.md", gettingStarted, "descriptor schema v1 is frozen (the [versioning policy](descriptor.md#versioning-policy) says what may change).");
        Forbid(problems, "getting-started.md", gettingStarted, "takes effect with 1.0.0", "before 1.0 a minor");

        var how = Collapse(ReadDoc(root, "docs/how-it-works.md"));
        var version = Version.Parse(PackageVersion(root).Split('-', '+')[0]);
        var series = $"v{version.Major}.{version.Minor}.x";
        Require(problems, "how-it-works.md", how, $"> **Status ({series}).**");
        Forbid(problems, "how-it-works.md", how, "(sq-46", "(sq-47", "Status (v0.", "takes effect with 1.0.0", "main after v0.3.0", "before 1.0 a minor");

        var banner = how[..how.IndexOf(" When an architectural review", StringComparison.Ordinal)];
        if (Regex.Matches(banner, "scaled by the field count", RegexOptions.CultureInvariant).Count != 1)
        {
            problems.Add("how-it-works.md: the banner must say once that the sketch precision is scaled by the field count");
        }

        var design = Collapse(ReadDoc(root, "docs/design.md"));
        Forbid(problems, "design.md", design, "early (0.3.x)", "takes effect with 1.0.0", "before 1.0 a minor");

        Assert.True(problems.Count == 0, "The docs do not speak of the released version in the present tense:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// The owner decision for 1.0.0: a later option is a new optional <c>init</c> member of <c>ProfileOptions</c>, and
    /// <c>ProfileOptions.Create</c> keeps its five parameters and defaults and never changes after 1.0. No doc promises a new
    /// <c>Create</c> overload or explains the ambiguity that overloads would cause.
    /// </summary>
    [Fact]
    public void Docs_SayThatCreateIsFrozenAndANewOptionIsAnInitMember()
    {
        var root = DotNetTool.RepositoryRoot();
        var problems = new List<string>();
        const string Rule = "a later option is a new optional `init` member of `ProfileOptions`; `Create` keeps its five parameters and defaults and never changes after 1.0";
        foreach (var file in new[] { "docs/user-guide.md", "docs/design.md" })
        {
            var text = Collapse(ReadDoc(root, file));
            Require(problems, file, text, Rule);
            Forbid(problems, file, text, "a new `Create` overload", "CS0121", "absorbs the short calls");
        }

        Assert.True(problems.Count == 0, "The Create rule is not stated:\n" + string.Join("\n", problems));
    }

    private const string FlipChecklistHeading = "- **Making a planned member, family or dialect produced.**";

    private static readonly string[] FlipChecklistAreas =
    [
        "  - Code.",
        "  - Schema.",
        "  - Tests.",
        "  - `docs/descriptor.md`:",
        "  - `docs/design.md`:",
        "  - `docs/user-guide.md`:",
        "  - `README.md`:",
        "  - `docs/how-it-works.md`:",
        "  - Corpus check.",
    ];

    /// <summary>
    /// sq-69: the CONTRIBUTING checklist for making a planned member, family or dialect produced names types, test classes, methods,
    /// files and doc sections. The checklist cannot rot silently: every type, <c>Type.Member</c> and method it names in code spans is
    /// declared in the source, every path exists, every quoted section or phrase on a line about a doc occurs in that doc, and all
    /// nine areas are still there.
    /// </summary>
    [Fact]
    public void Contributing_FlipChecklist_NamesOnlyWhatExists()
    {
        var root = DotNetTool.RepositoryRoot();
        var lines = ReadDoc(root, "CONTRIBUTING.md").Split('\n');
        var start = Array.FindIndex(lines, static l => l.StartsWith(FlipChecklistHeading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"CONTRIBUTING.md has no bullet that starts with '{FlipChecklistHeading}'.");
        var block = new List<string> { lines[start] };
        for (var i = start + 1; i < lines.Length && (lines[i].StartsWith("  ", StringComparison.Ordinal) || lines[i].Length == 0); i++)
        {
            block.Add(lines[i]);
        }

        var problems = new List<string>();
        var areas = block.Skip(1).Where(static l => l.StartsWith("  - ", StringComparison.Ordinal)).ToArray();
        for (var i = 0; i < FlipChecklistAreas.Length; i++)
        {
            if (i >= areas.Length || !areas[i].StartsWith(FlipChecklistAreas[i], StringComparison.Ordinal))
            {
                problems.Add($"area {i + 1} of the checklist does not start with '{FlipChecklistAreas[i]}'");
            }
        }

        var declared = DeclaredTypes(root);
        foreach (var line in block)
        {
            foreach (Match span in Regex.Matches(line, "`([^`]+)`"))
            {
                CheckChecklistName(problems, root, declared, span.Groups[1].Value);
            }

            // A line about a doc: each quoted section or phrase occurs in that doc (white space collapsed, code ticks ignored).
            var doc = Regex.Match(line, @"^  - `((?:docs/)?[A-Za-z-]+\.md)`:");
            if (doc.Success && File.Exists(Path.Combine(root, doc.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar))))
            {
                var text = Collapse(ReadDoc(root, doc.Groups[1].Value).Replace("`", string.Empty, StringComparison.Ordinal));
                var quoted = Regex.Matches(line, "\"([^\"]+)\"").Select(static m => m.Groups[1].Value).ToArray();
                if (quoted.Length == 0)
                {
                    problems.Add($"the line for {doc.Groups[1].Value} quotes no section or phrase");
                }

                foreach (var phrase in quoted)
                {
                    Require(problems, doc.Groups[1].Value, text, phrase);
                }
            }
        }

        Assert.True(problems.Count == 0, "The flip checklist in CONTRIBUTING.md has drifted:\n" + string.Join("\n", problems));
    }

    /// <summary>One name from a code span of the checklist: a path, a type, a <c>Type.Member</c> or a test method; anything else is prose.</summary>
    private static void CheckChecklistName(List<string> problems, string root, Dictionary<string, List<string>> declared, string name)
    {
        if (Regex.IsMatch(name, @"^[A-Za-z0-9._/-]+$") && (name.Contains('/', StringComparison.Ordinal) || Regex.IsMatch(name, @"\.(md|json|cs|sh)$")))
        {
            var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                problems.Add($"`{name}` is not a file or folder of the repository");
            }

            return;
        }

        var member = Regex.Match(name, @"^(?<type>[A-Z][A-Za-z0-9]*)\.(?<member>[A-Za-z_][A-Za-z0-9_]*)$");
        var typeName = member.Success ? member.Groups["type"].Value : name;
        if (Regex.IsMatch(name, @"^[A-Z][A-Za-z0-9]*_[A-Za-z0-9_]+$"))
        {
            if (!declared.Values.SelectMany(static files => files).Distinct().Any(text => Regex.IsMatch(text, @"\b" + Regex.Escape(name) + @"\s*\(")))
            {
                problems.Add($"`{name}` is not a method in the source");
            }

            return;
        }

        if (!member.Success && !Regex.IsMatch(name, @"^[A-Z][A-Za-z0-9]*$"))
        {
            return;
        }

        if (!declared.TryGetValue(typeName, out var texts))
        {
            problems.Add($"`{name}`: no type {typeName} is declared in src or tests");
        }
        else if (member.Success && !texts.Any(text => Regex.IsMatch(text, @"\b" + Regex.Escape(member.Groups["member"].Value) + @"\b")))
        {
            problems.Add($"`{name}`: {typeName} has no member {member.Groups["member"].Value}");
        }
    }

    /// <summary>The source text of every declaration of each type (class, record, struct, interface or enum) under src and tests.</summary>
    private static Dictionary<string, List<string>> DeclaredTypes(string root)
    {
        var declared = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var folder in new[] { "src", "tests" })
        {
            var files = Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories)
                .Where(static f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            foreach (var file in files)
            {
                var text = File.ReadAllText(file);
                foreach (Match match in Regex.Matches(text, @"\b(?:class|record|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)"))
                {
                    if (!declared.TryGetValue(match.Groups[1].Value, out var list))
                    {
                        declared[match.Groups[1].Value] = list = [];
                    }

                    list.Add(text);
                }
            }
        }

        return declared;
    }

    private static void Forbid(List<string> problems, string where, string text, params string[] phrases)
    {
        foreach (var phrase in phrases.Where(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add($"{where} still says '{phrase}'");
        }
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
    public void UserGuide_StatesWhatAConsumerOfTheSchemaVersionConstGetsAfterTheChange()
    {
        // sq-68: a consumer compiled against the old const keeps running with the "1.0" it compiled in, so the entries
        // must not say "callers recompile" (that phrase means an old binary fails to link elsewhere in the guide).
        var lines = File.ReadAllLines(Path.Combine(DotNetTool.RepositoryRoot(), "docs", "user-guide.md"));

        var breaking = Assert.Single(lines, static l => l.StartsWith("- **Breaking changes in 1.0.0 (library).**", StringComparison.Ordinal));
        Assert.Contains("(sq-68)", breaking, StringComparison.Ordinal);
        var sentence = breaking[breaking.IndexOf("(sq-68)", StringComparison.Ordinal)..];
        Assert.Contains("; binary-compatible: a consumer compiled against the `const` still runs, with the `\"1.0\"` it compiled in, and reads the property only once it is recompiled; recompile so that a later value reaches it.", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("callers recompile", sentence, StringComparison.Ordinal);

        var added = Assert.Single(lines, static l => l.StartsWith("- **API added in 1.0.0 (library).**", StringComparison.Ordinal));
        Assert.Contains("(sq-68)", added, StringComparison.Ordinal);
        var addedSentence = added[added.IndexOf("(sq-68)", StringComparison.Ordinal)..];
        Assert.Contains("a consumer compiled against the property reads the value at run time", addedSentence, StringComparison.Ordinal);
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
