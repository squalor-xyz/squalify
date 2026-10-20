// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The result of choosing a format.</summary>
/// <param name="Family">The chosen family (<c>empty</c> for a zero-length file).</param>
/// <param name="Score">The winning score (1 for an empty file).</param>
/// <param name="Profiler">The winning profiler, or null for an empty file.</param>
/// <param name="Evidence">The evidence entry that lists every profiler's score.</param>
internal sealed record FormatChoice(string Family, double Score, IFormatProfiler? Profiler, Evidence Evidence);

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
        ["parquet", "stdf", "touchstone", "keyValue", "delimited", "text", "binary"];

    /// <summary>The registry of this tool version: text and binary.</summary>
    public static FormatRegistry Default { get; } = new FormatRegistry([new GenericTextProfiler(), new GenericBinaryProfiler()]);

    /// <summary>
    /// Scores every profiler and picks the winner: the highest score, and on a tie the profiler that comes first in
    /// <see cref="Order"/>. A zero-length file is <c>empty</c> by rule, whatever the scores are.
    /// </summary>
    /// <param name="sniff">The byte-pass result.</param>
    /// <returns>The choice.</returns>
    public FormatChoice Detect(SniffResult sniff)
    {
        ArgumentNullException.ThrowIfNull(sniff);
        var candidates = new List<Candidate>();
        IFormatProfiler? best = null;
        var bestScore = double.NegativeInfinity;
        foreach (var profiler in _profilers)
        {
            var (score, reason) = profiler.Score(sniff);
            candidates.Add(new Candidate(profiler.Family, score, reason));
            if (score > bestScore)
            {
                best = profiler;
                bestScore = score;
            }
        }

        if (sniff.Kind == "empty" || best is null)
        {
            return new FormatChoice("empty", 1.0, null, new Evidence("format", "family", "empty", candidates));
        }

        return new FormatChoice(best.Family, bestScore, best, new Evidence("format", "family", best.Family, candidates));
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
