// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// The private-corpus check. An entry with no golden is reported as "no golden", not as passed. The theories run only with <c>SQUALIFY_CORPUS</c> set (see <c>scripts/corpus-check.sh</c>);
/// the other tests use a temporary directory and run in CI.
/// </summary>
public sealed class CorpusTests
{
    internal const string NotConfiguredKind = "not-configured";
    internal const string ProblemKind = "problem";
    internal const string FileKind = "file";
    internal const string NoGoldenKind = "no-golden";
    internal const string NoneKind = "none";

    private static readonly object SessionLock = new();
    private static CorpusSummary? session;

    [Fact]
    public void Corpus_Unset_PassesWithMessage()
    {
        // Rows(null, ...) is the unset case: no environment variable is changed, so parallel tests are not affected.
        var row = Assert.Single(Rows(null, update: false).ToArray());
        Assert.Equal(NotConfiguredKind, (string)row[0]);
        Assert.Contains("corpus not configured", (string)row[1], StringComparison.Ordinal);
        Corpus_EachFile_Passes((string)row[0], (string)row[1]);
    }

    [Fact]
    public void CorpusRunner_TempDirectory_PassAndDiff()
    {
        var root = Path.Combine(Path.GetTempPath(), "squalify-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = ProfileOptions.Create(false, Array.Empty<string>());
            var good = Path.Combine(root, "good.csv");
            var bad = Path.Combine(root, "bad.csv");
            File.WriteAllText(good, "a,b\n1,2\n3,4\n");
            File.WriteAllText(bad, "a,b\n1,2\n");
            File.WriteAllBytes(good + ".squalify.json", DescriptorWriter.ToBytes(Profiler.Profile(good, options)));

            // A golden for other bytes under the same name: the size and the hash differ, so the check reports a diff.
            var otherBytes = new BytesSource("bad.csv", TestBytes.Ascii("other\n"));
            File.WriteAllBytes(bad + ".squalify.json", DescriptorWriter.ToBytes(Profiler.Profile(otherBytes, options)));

            File.WriteAllText(Path.Combine(root, ".hidden.csv"), "x\n");
            Directory.CreateDirectory(Path.Combine(root, ".cache"));
            File.WriteAllText(Path.Combine(root, ".cache", "inner.csv"), "x\n");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "sub", "nested.csv"), "a\n1\n");

            Assert.Equal(new[] { "bad.csv", "good.csv", "sub/nested.csv" }, CorpusRunner.List(root));

            // update: false, whatever SQUALIFY_UPDATE_GOLDEN says, so this test never rewrites a golden.
            var cultureBefore = CultureInfo.CurrentCulture.Name;
            Assert.Equal(CorpusOutcome.Pass, CorpusRunner.Check(good, update: false).Outcome);
            var diff = CorpusRunner.Check(bad, update: false);
            Assert.Equal(CorpusOutcome.Diff, diff.Outcome);
            Assert.Contains("Golden mismatch", diff.Message, StringComparison.Ordinal);

            // No golden: the check reports it as "no golden", not as a pass. Update mode then writes the golden from the current output,
            // and a later check against it matches.
            var nested = Path.Combine(root, "sub", "nested.csv");
            var noGolden = CorpusRunner.Check(nested, update: false);
            Assert.Equal(CorpusOutcome.NoGolden, noGolden.Outcome);
            Assert.Equal("no golden next to the file", noGolden.Message);
            Assert.False(File.Exists(nested + ".squalify.json"));

            var written = CorpusRunner.Check(nested, update: true);
            Assert.Equal(CorpusOutcome.Pass, written.Outcome);
            Assert.Equal("pass: golden written", written.Message);
            Assert.Equal(DescriptorWriter.ToBytes(Profiler.Profile(nested, options)), File.ReadAllBytes(nested + ".squalify.json"));
            Assert.Equal("pass: golden matched", CorpusRunner.Check(nested, update: false).Message);
            Assert.Equal(cultureBefore, CultureInfo.CurrentCulture.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CorpusRunner_DirectoryLoop_IsNotFollowed()
    {
        var root = Path.Combine(Path.GetTempPath(), "squalify-corpus-loop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        var link = Path.Combine(root, "sub", "loop");
        try
        {
            File.WriteAllText(Path.Combine(root, "top.csv"), "a\n1\n");
            File.WriteAllText(Path.Combine(root, "sub", "nested.csv"), "a\n1\n");
            try
            {
                // sub/loop points back at the corpus root. Following it would list the corpus again inside itself.
                Directory.CreateSymbolicLink(link, "..");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // This platform or account cannot make links (Windows without the privilege, for example), so the
                // loop case cannot run here. xunit 2.9.3 has no runtime skip, and no new package is allowed.
                return;
            }

            var listing = CorpusRunner.List(root);
            Assert.Equal(new[] { "sub/nested.csv", "top.csv" }, listing);
            Assert.Equal(listing.Count, listing.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            if (Directory.Exists(link))
            {
                // Removes the link itself, not the directory it points at.
                Directory.Delete(link);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The entries that have a golden, and the rows that are not entries (not configured, a problem).</summary>
    /// <param name="kind">The row's kind.</param>
    /// <param name="entry">The entry's path relative to the corpus directory, or the row's message.</param>
    [Theory]
    [Trait("Category", "Corpus")]
    [MemberData(nameof(RowsWithGolden))]
    public static void Corpus_EachFile_Passes(string kind, string entry)
    {
        CheckEntry(kind, entry);
    }

    /// <summary>
    /// The entries with no golden: profiled and checked for determinism, compared with nothing. Passes unless a golden
    /// is required (<c>SQUALIFY_REQUIRE_GOLDEN=1</c>), and the summary counts it apart from the passes.
    /// </summary>
    /// <param name="kind">The row's kind.</param>
    /// <param name="entry">The entry's path relative to the corpus directory.</param>
    [Theory]
    [Trait("Category", "Corpus")]
    [MemberData(nameof(RowsWithoutGolden))]
    public static void Corpus_EachFile_HasNoGolden_NotCompared(string kind, string entry)
    {
        CheckEntry(kind, entry);
    }

    private static void CheckEntry(string kind, string entry)
    {
        if (kind is NotConfiguredKind or NoneKind)
        {
            return;
        }

        if (kind == ProblemKind)
        {
            Assert.Fail(entry);
        }

        var root = CorpusRunner.ConfiguredRoot() ?? throw new InvalidOperationException($"{CorpusRunner.Variable} changed during the run.");
        var result = CorpusRunner.Check(Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar)));
        var require = Record(root, entry, result);

        // An entry with no golden passes unless a golden is required, but it is not counted as passed: it runs in its own
        // theory, and the summary counts it apart.
        Assert.True(CorpusRunner.Accepts(result.Outcome, require), $"{result.Outcome}: {result.Message}{(result.Outcome == CorpusOutcome.NoGolden ? $" ({CorpusRunner.RequireGoldenVariable}=1 or --require-golden is set)" : string.Empty)}");
    }

    // Adds the result to the run's tally and rewrites the summary file under the corpus directory. Returns whether a
    // golden is required.
    private static bool Record(string root, string entry, CorpusResult result)
    {
        lock (SessionLock)
        {
            session ??= new CorpusSummary(CorpusRunner.RequireGolden());
            session.Add(entry, result);
            session.WriteTo(root);
            return CorpusRunner.RequireGolden();
        }
    }

    /// <summary>The rows of <see cref="Corpus_EachFile_Passes"/>, for the corpus named by the environment.</summary>
    public static IEnumerable<object[]> RowsWithGolden()
    {
        return Partition(Rows(), noGolden: false);
    }

    /// <summary>The rows of <see cref="Corpus_EachFile_HasNoGolden_NotCompared"/>, for the corpus named by the environment.</summary>
    public static IEnumerable<object[]> RowsWithoutGolden()
    {
        return Partition(Rows(), noGolden: true);
    }

    /// <summary>
    /// Picks one theory's rows. The no-golden theory gets the <see cref="NoGoldenKind"/> rows; the other gets the rest.
    /// A theory with no rows fails xunit's discovery, so an empty side gets one <see cref="NoneKind"/> row that does nothing.
    /// </summary>
    internal static IEnumerable<object[]> Partition(IEnumerable<object[]> rows, bool noGolden)
    {
        var picked = rows.Where(r => ((string)r[0] == NoGoldenKind) == noGolden).ToArray();
        if (picked.Length > 0)
        {
            return picked;
        }

        var message = noGolden ? "no entry without a golden" : "no entry with a golden";
        return new[] { new object[] { NoneKind, message } };
    }

    /// <summary>All rows, for the corpus and update mode named by the environment.</summary>
    public static IEnumerable<object[]> Rows()
    {
        return Rows(CorpusRunner.ConfiguredRoot(), Environment.GetEnvironmentVariable(Golden.UpdateVariable) == "1");
    }

    /// <summary>
    /// The rows: one per corpus file. When <paramref name="root"/> is null (the variable is unset), one row that passes
    /// and says so. When the directory is missing or has no files, one row that fails with the reason, so a wrong path
    /// cannot look green. A file with no golden gets <see cref="NoGoldenKind"/>, except in update mode, which writes one.
    /// The tests pass the arguments in, so they never change the environment other tests read.
    /// </summary>
    /// <param name="root">The corpus directory, or null when it is not configured.</param>
    /// <param name="update">True in update mode (<c>SQUALIFY_UPDATE_GOLDEN=1</c>).</param>
    internal static IEnumerable<object[]> Rows(string? root, bool update)
    {
        if (root is null)
        {
            yield return new object[] { NotConfiguredKind, $"corpus not configured: set {CorpusRunner.Variable} to a directory to run the private-corpus check" };
            yield break;
        }

        IReadOnlyList<string> files;
        string? problem = null;
        try
        {
            files = CorpusRunner.List(root);
        }
        catch (SqualifyException ex)
        {
            files = Array.Empty<string>();
            problem = ex.Message;
        }

        if (problem is not null)
        {
            yield return new object[] { ProblemKind, problem };
            yield break;
        }

        if (files.Count == 0)
        {
            yield return new object[] { ProblemKind, $"no files under {CorpusRunner.Variable}" };
            yield break;
        }

        foreach (var file in files)
        {
            // In update mode every entry gets a golden, so none is a "no golden" row.
            var full = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            var kind = !update && !CorpusRunner.HasGolden(full) ? NoGoldenKind : FileKind;
            yield return new object[] { kind, file };
        }
    }
}
