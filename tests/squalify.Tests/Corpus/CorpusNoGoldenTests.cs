// SPDX-License-Identifier: Apache-2.0
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// An entry with no golden is reported as "no golden", not as passed (sq-48). These tests use a small synthetic corpus
/// in a temporary directory, so they run in CI; the real private-corpus run is the Category=Corpus theory.
/// </summary>
public sealed class CorpusNoGoldenTests
{
    [Fact]
    public void Check_EntryWithoutGolden_IsNoGoldenAndWritesNothing()
    {
        using var corpus = new SyntheticCorpus();
        corpus.Add("good.csv", "a,b\n1,2\n3,4\n", golden: true);
        corpus.Add("bare.csv", "a,b\n1,2\n", golden: false);

        var good = CorpusRunner.Check(corpus.Path("good.csv"), update: false);
        var bare = CorpusRunner.Check(corpus.Path("bare.csv"), update: false);

        Assert.Equal(CorpusOutcome.Pass, good.Outcome);
        Assert.Equal(CorpusOutcome.NoGolden, bare.Outcome);
        Assert.Equal("no golden next to the file", bare.Message);
        Assert.False(File.Exists(corpus.Path("bare.csv") + ".squalify.json"));
        Assert.False(CorpusRunner.HasGolden(corpus.Path("bare.csv")));
        Assert.True(CorpusRunner.HasGolden(corpus.Path("good.csv")));
    }

    [Fact]
    public void Summary_OneGoldenOneNone_CountsSeparatelyAndExitsZero()
    {
        using var corpus = new SyntheticCorpus();
        corpus.Add("good.csv", "a,b\n1,2\n3,4\n", golden: true);
        corpus.Add("bare.csv", "a,b\n1,2\n", golden: false);

        var summary = corpus.Run(requireGolden: false);

        Assert.Equal(1, summary.Passed);
        Assert.Equal(0, summary.Failures);
        Assert.Equal(1, summary.NoGolden);
        Assert.Equal(new[] { "bare.csv" }, summary.NoGoldenEntries);
        Assert.Contains("1 passed, 0 failed, 1 no golden", summary.Line, StringComparison.Ordinal);
        Assert.Contains("not passed", summary.Line, StringComparison.Ordinal);
        Assert.Contains("--require-golden", summary.Line, StringComparison.Ordinal);
        Assert.DoesNotContain("FAILED", summary.Line, StringComparison.Ordinal);
        Assert.False(summary.IsFailure);
        Assert.Equal(0, summary.ExitCode);
    }

    [Fact]
    public void Summary_AllGoldens_SaysNothingAboutMissingGoldens()
    {
        using var corpus = new SyntheticCorpus();
        corpus.Add("a.csv", "a\n1\n", golden: true);
        corpus.Add("b.csv", "a\n2\n", golden: true);

        var summary = corpus.Run(requireGolden: true);

        Assert.Equal("corpus: 2 passed, 0 failed, 0 no golden", summary.Line);
        Assert.Equal(0, summary.ExitCode);
    }

    [Fact]
    public void Summary_RequireGolden_NoGoldenFailsTheRun()
    {
        using var corpus = new SyntheticCorpus();
        corpus.Add("good.csv", "a,b\n1,2\n3,4\n", golden: true);
        corpus.Add("bare.csv", "a,b\n1,2\n", golden: false);

        var summary = corpus.Run(requireGolden: true);

        Assert.Equal(1, summary.Passed);
        Assert.Equal(1, summary.NoGolden);
        Assert.True(summary.IsFailure);
        Assert.Equal(1, summary.ExitCode);
        Assert.Contains("1 passed, 0 failed, 1 no golden", summary.Line, StringComparison.Ordinal);
        Assert.Contains("FAILED: require-golden is set (--require-golden or SQUALIFY_REQUIRE_GOLDEN=1) and 1 entry has no golden", summary.Line, StringComparison.Ordinal);

        // The per-entry test (the theory) applies the same rule, so dotnet test exits non-zero.
        Assert.False(CorpusRunner.Accepts(CorpusOutcome.NoGolden, requireGolden: true));
        Assert.True(CorpusRunner.Accepts(CorpusOutcome.NoGolden, requireGolden: false));
    }

    [Fact]
    public void RequireGolden_OnlyTheValueOneTurnsItOn()
    {
        // The value is passed in, so this test never touches the process environment other tests share.
        Assert.Equal("SQUALIFY_REQUIRE_GOLDEN", CorpusRunner.RequireGoldenVariable);
        Assert.False(CorpusRunner.RequireGolden(null));
        Assert.False(CorpusRunner.RequireGolden(string.Empty));
        Assert.False(CorpusRunner.RequireGolden("0"));
        Assert.True(CorpusRunner.RequireGolden("1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Summary_EntriesWithGoldens_PassOrFailOnTheComparison(bool requireGolden)
    {
        using var corpus = new SyntheticCorpus();
        corpus.Add("match.csv", "a,b\n1,2\n", golden: true);
        corpus.Add("differs.csv", "a,b\n1,2\n", golden: false);
        File.WriteAllBytes(
            corpus.Path("differs.csv") + ".squalify.json",
            DescriptorWriter.ToBytes(Profiler.Profile(new BytesSource("differs.csv", TestBytes.Ascii("other\n")), ProfileOptions.Create(false, Array.Empty<string>()))));

        Assert.Equal(CorpusOutcome.Pass, CorpusRunner.Check(corpus.Path("match.csv"), update: false).Outcome);
        Assert.Equal(CorpusOutcome.Diff, CorpusRunner.Check(corpus.Path("differs.csv"), update: false).Outcome);
        Assert.False(CorpusRunner.Accepts(CorpusOutcome.Diff, requireGolden));
        Assert.False(CorpusRunner.Accepts(CorpusOutcome.Fail, requireGolden));
        Assert.True(CorpusRunner.Accepts(CorpusOutcome.Pass, requireGolden));

        var summary = corpus.Run(requireGolden);
        Assert.Equal(1, summary.Passed);
        Assert.Equal(1, summary.Failures);
        Assert.Equal(0, summary.NoGolden);
        Assert.Contains("1 passed, 1 failed, 0 no golden", summary.Line, StringComparison.Ordinal);
        Assert.Contains("FAILED", summary.Line, StringComparison.Ordinal);
        Assert.Equal(1, summary.ExitCode);
    }

    [Fact]
    public void Check_ADeterminismFailure_StaysAFailure()
    {
        // A file that cannot be profiled is a Fail whatever the golden situation (here: the path does not exist).
        using var corpus = new SyntheticCorpus();
        var result = CorpusRunner.Check(corpus.Path("missing.csv"), update: false);
        Assert.Equal(CorpusOutcome.Fail, result.Outcome);

        var summary = new CorpusSummary(requireGolden: false);
        summary.Add("missing.csv", result);
        Assert.Equal(1, summary.Failures);
        Assert.Equal(0, summary.NoGolden);
        Assert.Equal(1, summary.ExitCode);
    }

    [Fact]
    public void Check_UpdateMode_WritesTheMissingGoldenInsteadOfReportingNone()
    {
        using var corpus = new SyntheticCorpus();
        corpus.Add("bare.csv", "a,b\n1,2\n", golden: false);

        var written = CorpusRunner.Check(corpus.Path("bare.csv"), update: true);
        Assert.Equal(CorpusOutcome.Pass, written.Outcome);
        Assert.Equal("pass: golden written", written.Message);
        Assert.Equal(CorpusOutcome.Pass, CorpusRunner.Check(corpus.Path("bare.csv"), update: false).Outcome);
    }

    [Fact]
    public void Rows_NoGoldenEntry_GetsItsOwnKind()
    {
        // The root and the update flag are passed in: no environment variable is set, so a golden test running in
        // parallel can never see SQUALIFY_UPDATE_GOLDEN=1 from here.
        using var corpus = new SyntheticCorpus();
        corpus.Add("good.csv", "a\n1\n", golden: true);
        corpus.Add("bare.csv", "a\n2\n", golden: false);

        var rows = CorpusTests.Rows(corpus.Root, update: false).Select(r => ((string)r[0], (string)r[1])).ToArray();
        Assert.Equal(new[] { (CorpusTests.NoGoldenKind, "bare.csv"), (CorpusTests.FileKind, "good.csv") }, rows);

        // In update mode every entry gets a golden, so none is a "no golden" row.
        Assert.All(CorpusTests.Rows(corpus.Root, update: true), r => Assert.Equal(CorpusTests.FileKind, (string)r[0]));
    }

    [Fact]
    public void Rows_TwoTheories_EachGetsItsOwnEntries()
    {
        using var corpus = new SyntheticCorpus();
        corpus.Add("good.csv", "a\n1\n", golden: true);
        corpus.Add("bare.csv", "a\n2\n", golden: false);
        var mixed = CorpusTests.Rows(corpus.Root, update: false).ToArray();

        Assert.Equal(new[] { "good.csv" }, CorpusTests.Partition(mixed, noGolden: false).Select(r => (string)r[1]));
        Assert.Equal(new[] { "bare.csv" }, CorpusTests.Partition(mixed, noGolden: true).Select(r => (string)r[1]));

        // A theory with no rows would fail xunit's discovery, so an empty side gets one placeholder row that does nothing.
        var onlyBare = new SyntheticCorpus();
        using (onlyBare)
        {
            onlyBare.Add("bare.csv", "a\n2\n", golden: false);
            var rows = CorpusTests.Rows(onlyBare.Root, update: false).ToArray();
            var withGolden = Assert.Single(CorpusTests.Partition(rows, noGolden: false).ToArray());
            Assert.Equal(CorpusTests.NoneKind, (string)withGolden[0]);
            Assert.Equal("bare.csv", (string)Assert.Single(CorpusTests.Partition(rows, noGolden: true).ToArray())[1]);
        }

        // Not configured and problem rows belong to the first theory; the second gets the placeholder.
        var unset = CorpusTests.Rows(null, update: false).ToArray();
        Assert.Equal(CorpusTests.NotConfiguredKind, (string)Assert.Single(CorpusTests.Partition(unset, noGolden: false).ToArray())[0]);
        Assert.Equal(CorpusTests.NoneKind, (string)Assert.Single(CorpusTests.Partition(unset, noGolden: true).ToArray())[0]);
    }

    [Fact]
    public void Run_WritesNothingOutsideTheCorpusDirectory()
    {
        using var corpus = new SyntheticCorpus();
        corpus.Add("good.csv", "a,b\n1,2\n", golden: true);
        corpus.Add("bare.csv", "a,b\n1,2\n", golden: false);
        var before = corpus.ParentListing();
        var rootBefore = corpus.RootListing();

        var summary = corpus.Run(requireGolden: false);
        summary.WriteTo(corpus.Root);

        var after = corpus.ParentListing();
        Assert.Equal(before.Where(p => !p.StartsWith("corpus/", StringComparison.Ordinal)), after.Where(p => !p.StartsWith("corpus/", StringComparison.Ordinal)));
        Assert.Equal(rootBefore.Concat(new[] { ".squalify-corpus", ".squalify-corpus/summary.txt" }).OrderBy(p => p, StringComparer.Ordinal), corpus.RootListing());
        var text = File.ReadAllText(Path.Combine(corpus.Root, ".squalify-corpus", "summary.txt"));
        Assert.Contains("no golden: bare.csv", text, StringComparison.Ordinal);
        Assert.Contains(summary.Line, text, StringComparison.Ordinal);
    }

    /// <summary>A throw-away corpus: a parent directory holding <c>corpus/</c>, so a stray write next to it shows.</summary>
    private sealed class SyntheticCorpus : IDisposable
    {
        private readonly string parent = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "squalify-nogolden-" + Guid.NewGuid().ToString("N"));

        public SyntheticCorpus()
        {
            Directory.CreateDirectory(Root);
        }

        public string Root => System.IO.Path.Combine(parent, "corpus");

        public string Path(string entry) => System.IO.Path.Combine(Root, entry);

        public void Add(string entry, string text, bool golden)
        {
            File.WriteAllText(Path(entry), text);
            if (golden)
            {
                var options = ProfileOptions.Create(false, Array.Empty<string>());
                File.WriteAllBytes(Path(entry) + ".squalify.json", DescriptorWriter.ToBytes(Profiler.Profile(Path(entry), options)));
            }
        }

        public CorpusSummary Run(bool requireGolden)
        {
            var summary = new CorpusSummary(requireGolden);
            foreach (var entry in CorpusRunner.List(Root))
            {
                summary.Add(entry, CorpusRunner.Check(Path(entry), update: false));
            }

            return summary;
        }

        public string[] ParentListing() => Listing(parent);

        public string[] RootListing() => Listing(Root);

        public void Dispose()
        {
            Directory.Delete(parent, recursive: true);
        }

        private static string[] Listing(string directory)
        {
            return Directory
                .EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
                .Select(p => System.IO.Path.GetRelativePath(directory, p).Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
