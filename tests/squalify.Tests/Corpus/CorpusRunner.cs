// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using Xunit.Sdk;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>The outcome of checking one file.</summary>
internal enum CorpusOutcome
{
    /// <summary>Profiled, deterministic, and the golden matched (or was just written in update mode).</summary>
    Pass,

    /// <summary>Profiling threw, or a repeat run or another culture gave different bytes.</summary>
    Fail,

    /// <summary>Deterministic, but the output differs from the golden next to the file.</summary>
    Diff,

    /// <summary>Deterministic, but there is no golden next to the file, so the output was not compared with anything.</summary>
    NoGolden,
}

/// <summary>The outcome of one check, with a message for the reader.</summary>
internal sealed record CorpusResult(CorpusOutcome Outcome, string Message);

/// <summary>
/// Lists the private corpus and checks one file of it. Real files never enter the repository, so the harness only
/// reads a directory named by <c>SQUALIFY_CORPUS</c>, and writes nothing there (the golden is written only in update mode).
/// </summary>
internal static class CorpusRunner
{
    /// <summary>The environment variable that names the corpus directory.</summary>
    public const string Variable = "SQUALIFY_CORPUS";

    /// <summary>
    /// The environment variable that makes an entry with no golden fail the run when it is <c>1</c>
    /// (<c>scripts/corpus-check.sh --require-golden</c> sets it).
    /// </summary>
    public const string RequireGoldenVariable = "SQUALIFY_REQUIRE_GOLDEN";

    private const string GoldenSuffix = ".squalify.json";

    // The cultures the output must not depend on, next to the repeat run under the current culture.
    private static readonly (string Label, string? Culture)[] DeterminismRuns =
    [
        ("repeat run", null),
        ("de-DE run", "de-DE"),
        ("tr-TR run", "tr-TR"),
    ];

    /// <summary>True when <see cref="RequireGoldenVariable"/> is <c>1</c>: an entry with no golden then fails the run.</summary>
    public static bool RequireGolden()
    {
        return RequireGolden(Environment.GetEnvironmentVariable(RequireGoldenVariable));
    }

    /// <summary>True when the value of <see cref="RequireGoldenVariable"/> is <c>1</c>. The harness's tests call this directly.</summary>
    /// <param name="value">The variable's value, or null when it is not set.</param>
    public static bool RequireGolden(string? value)
    {
        return value == "1";
    }

    /// <summary>Whether a result lets the run stay green: a pass always does, a missing golden only when it is not required.</summary>
    /// <param name="outcome">The outcome of one entry.</param>
    /// <param name="requireGolden">True when an entry with no golden fails the run.</param>
    public static bool Accepts(CorpusOutcome outcome, bool requireGolden)
    {
        return outcome == CorpusOutcome.Pass || (outcome == CorpusOutcome.NoGolden && !requireGolden);
    }

    /// <summary>Whether the golden for a corpus file (<c>&lt;file&gt;.squalify.json</c> next to it) exists.</summary>
    /// <param name="path">The full path of a file.</param>
    public static bool HasGolden(string path)
    {
        return File.Exists(path + GoldenSuffix);
    }

    /// <summary>The corpus directory from the environment, or null when it is not set or empty.</summary>
    public static string? ConfiguredRoot()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Lists the corpus files as relative paths with <c>/</c> separators, sorted ordinally. The listing is recursive,
    /// skips goldens (<c>*.squalify.json</c>) and hidden files and directories (names that start with <c>.</c>).
    /// Directory symbolic links and junctions are not followed: a loop would never end, and a link would list its
    /// target's files twice. A file symbolic link is listed as a file, and its target's bytes are checked.
    /// </summary>
    /// <exception cref="SqualifyException">The directory does not exist or cannot be read.</exception>
    public static IReadOnlyList<string> List(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new SqualifyException($"{Variable} is not a directory: {root}");
        }

        var files = new List<string>();
        try
        {
            Collect(root, string.Empty, files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SqualifyException($"Cannot list {Variable}: {ex.Message}", ex);
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// Checks one file, with the update mode taken from <c>SQUALIFY_UPDATE_GOLDEN</c>. See <see cref="Check(string, bool)"/>.
    /// </summary>
    /// <param name="path">The full path of a file.</param>
    /// <returns>The outcome of the check.</returns>
    public static CorpusResult Check(string path)
    {
        return Check(path, Environment.GetEnvironmentVariable(Golden.UpdateVariable) == "1");
    }

    /// <summary>
    /// Checks one file. Profiles it, then repeats the profile under the current culture, under <c>de-DE</c> and under
    /// <c>tr-TR</c>, and requires identical bytes. Then compares with <c>&lt;file&gt;.squalify.json</c> next to it, when that
    /// golden exists. With <paramref name="update"/> the golden is written instead. Culture is restored even on failure.
    /// </summary>
    /// <param name="path">The full path of a file.</param>
    /// <param name="update">True to write the golden from the current output, as <c>SQUALIFY_UPDATE_GOLDEN=1</c> does.</param>
    /// <returns>The outcome: <see cref="CorpusOutcome.Pass"/>, <see cref="CorpusOutcome.Fail"/> (exception or determinism) or <see cref="CorpusOutcome.Diff"/> (golden) or <see cref="CorpusOutcome.NoGolden"/>.</returns>
    public static CorpusResult Check(string path, bool update)
    {
        var options = ProfileOptions.Create(false, Array.Empty<string>());
        try
        {
            var first = Profile(path, options, culture: null);
            var expected = DescriptorWriter.ToBytes(first);
            foreach (var (label, culture) in DeterminismRuns)
            {
                var again = DescriptorWriter.ToBytes(Profile(path, options, culture));
                var at = FirstDifference(expected, again);
                if (at >= 0)
                {
                    return new CorpusResult(CorpusOutcome.Fail, $"determinism: the {label} differs from the first run at byte {at}");
                }
            }

            return CheckGolden(path, first, update);
        }
        catch (Exception ex)
        {
            return new CorpusResult(CorpusOutcome.Fail, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Collect(string directory, string relative, List<string> files)
    {
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.') || name.EndsWith(GoldenSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            files.Add(Join(relative, name));
        }

        foreach (var path in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.') || IsDirectoryLink(path))
            {
                continue;
            }

            Collect(path, Join(relative, name), files);
        }
    }

    private static bool IsDirectoryLink(string directory)
    {
        var info = new DirectoryInfo(directory);
        return info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static string Join(string relative, string name)
    {
        return relative.Length == 0 ? name : relative + "/" + name;
    }

    private static Descriptor Profile(string path, ProfileOptions options, string? culture)
    {
        if (culture is null)
        {
            return Profiler.Profile(path, options);
        }

        var savedCulture = CultureInfo.CurrentCulture;
        var savedUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            return Profiler.Profile(path, options);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
            CultureInfo.CurrentUICulture = savedUiCulture;
        }
    }

    private static CorpusResult CheckGolden(string path, Descriptor descriptor, bool update)
    {
        if (!update && !HasGolden(path))
        {
            return new CorpusResult(CorpusOutcome.NoGolden, "no golden next to the file");
        }

        try
        {
            Golden.Verify(Path.GetFileName(path), descriptor, Path.GetDirectoryName(path)!, update);
        }
        catch (XunitException ex)
        {
            return new CorpusResult(CorpusOutcome.Diff, ex.Message);
        }

        return new CorpusResult(CorpusOutcome.Pass, update ? "pass: golden written" : "pass: golden matched");
    }

    private static int FirstDifference(byte[] expected, byte[] actual)
    {
        var common = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < common; i++)
        {
            if (expected[i] != actual[i])
            {
                return i;
            }
        }

        return expected.Length == actual.Length ? -1 : common;
    }
}
