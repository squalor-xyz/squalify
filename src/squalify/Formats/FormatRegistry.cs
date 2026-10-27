// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The result of choosing a format.</summary>
/// <param name="Family">The chosen family (<c>empty</c> for a zero-length file).</param>
/// <param name="Score">The winning score (1 for an empty file).</param>
/// <param name="Profiler">The winning profiler, or null for an empty file.</param>
/// <param name="Evidence">The evidence entry that lists every profiler's score.</param>
/// <param name="ProfilerScore">What the winning profiler returned from scoring, to hand back to it; null for an empty file.</param>
/// <param name="NotChosenIssues">Issues that the other profilers want on record because they declined the file.</param>
/// <param name="NotChosenEvidence">Evidence that goes with <paramref name="NotChosenIssues"/>.</param>
internal sealed record FormatChoice(
    string Family,
    double Score,
    IFormatProfiler? Profiler,
    Evidence Evidence,
    FormatScore? ProfilerScore = null,
    IReadOnlyList<Issue>? NotChosenIssues = null,
    IReadOnlyList<Evidence>? NotChosenEvidence = null);

/// <summary>The registered format profilers, in the fixed registry order.</summary>
internal sealed class FormatRegistry
{
    private readonly IReadOnlyList<IFormatProfiler> _profilers;

    /// <summary>Creates a registry. The profilers are sorted into <see cref="Order"/>.</summary>
    /// <param name="profilers">The profilers, in any order.</param>
    /// <exception cref="ArgumentException">A profiler's family is not in <see cref="Order"/>, or two profilers share a family.</exception>
    public FormatRegistry(IEnumerable<IFormatProfiler> profilers)
    {
        ArgumentNullException.ThrowIfNull(profilers);
        var list = profilers.ToList();
        foreach (var profiler in list)
        {
            if (!Order.Contains(profiler.Family))
            {
                throw new ArgumentException("Unknown format family '" + profiler.Family + "'.", nameof(profilers));
            }
        }

        if (list.Select(static p => p.Family).Distinct(StringComparer.Ordinal).Count() != list.Count)
        {
            throw new ArgumentException("Two profilers share a family.", nameof(profilers));
        }

        _profilers = list.OrderBy(static p => IndexOf(p.Family)).ToArray();
    }

    /// <summary>The fixed registry order, used to break ties and to order the evidence candidates.</summary>
    public static IReadOnlyList<string> Order { get; } =
        ["stdf", "touchstone", "keyValue", "delimited", "text", "binary"];

    /// <summary>The registry of this tool version: delimited, text and binary.</summary>
    public static FormatRegistry Default { get; } = new FormatRegistry([new DelimitedProfiler(), new GenericTextProfiler(), new GenericBinaryProfiler()]);

    /// <summary>
    /// Scores every profiler and picks the winner: the highest score, and on a tie the profiler that comes first in
    /// <see cref="Order"/>. A zero-length file is <c>empty</c> by rule, whatever the scores are. A profiler that loses
    /// may still hand back issues and evidence that explain why it declined (<see cref="FormatScore.NotChosenIssues"/>);
    /// these are the "fallback": a delimited profiler that gave up on a file as too irregular lets the generic text
    /// profile describe it, and the issue <c>tooIrregular</c> says why.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="sniff">The byte-pass result.</param>
    /// <param name="context">The options and limits.</param>
    /// <returns>The choice.</returns>
    public FormatChoice Detect(IProfileSource source, SniffResult sniff, ProfileContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sniff);
        ArgumentNullException.ThrowIfNull(context);
        var candidates = new List<Candidate>();
        var scores = new List<(IFormatProfiler Profiler, FormatScore Score)>();
        IFormatProfiler? best = null;
        FormatScore? bestScore = null;
        foreach (var profiler in _profilers)
        {
            var result = profiler.Score(source, sniff, context);
            scores.Add((profiler, result));
            candidates.Add(new Candidate(profiler.Family, result.Score, result.Reason));
            if (bestScore is null || result.Score > bestScore.Score)
            {
                best = profiler;
                bestScore = result;
            }
        }

        if (sniff.Kind == "empty" || best is null || bestScore is null)
        {
            return new FormatChoice("empty", 1.0, null, new Evidence("format", "family", "empty", candidates));
        }

        var issues = new List<Issue>();
        var evidence = new List<Evidence>();
        foreach (var (profiler, result) in scores)
        {
            if (!ReferenceEquals(profiler, best))
            {
                issues.AddRange(result.NotChosenIssues ?? []);
                evidence.AddRange(result.NotChosenEvidence ?? []);
            }
        }

        return new FormatChoice(best.Family, bestScore.Score, best, new Evidence("format", "family", best.Family, candidates), bestScore, issues, evidence);
    }

    private static int IndexOf(string family)
    {
        for (var i = 0; i < Order.Count; i++)
        {
            if (string.Equals(Order[i], family, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return Order.Count;
    }
}
