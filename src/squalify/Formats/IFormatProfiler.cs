// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>What a format profiler may use besides the file: the options and the limits in effect.</summary>
/// <param name="Options">The profile options.</param>
/// <param name="Limits">The caps and table versions.</param>
/// <param name="Trace">Receives the peak of the estimated memory, when a test or benchmark wants it; null otherwise.</param>
internal sealed record ProfileContext(ProfileOptions Options, Limits Limits, MemoryTrace? Trace = null)
{
    /// <summary>Decides, member by member and owner by owner, which values-tier members may be collected and written.</summary>
    public ValuesGate Gate { get; } = ValuesPolicy.Gate(Options);
}

/// <summary>The parts of the descriptor that a format profiler produces.</summary>
/// <param name="Dialect">The family dialect, or null.</param>
/// <param name="Layout">The layout section.</param>
/// <param name="LineEndings">Line-ending counts (zero for non-text files).</param>
/// <param name="Issues">Issues, in any order; the caller sorts by code.</param>
/// <param name="Evidence">Evidence entries, in any order; the caller sorts by stage and topic.</param>
/// <param name="Table">The table section, for tabular files.</param>
/// <param name="Binary">The binary section, for binary files.</param>
/// <param name="StableLines">The lines, with their text, that the hint builder may turn into stable-line regexes; null for none.</param>
/// <param name="EffectiveLimits">
/// The limits with the per-field caps that the profiler used after the global budget, or null when the profiler
/// profiles no fields.
/// </param>
internal sealed record FormatProfile(
    Dialect? Dialect,
    LayoutSection Layout,
    LineEndings LineEndings,
    IReadOnlyList<Issue> Issues,
    IReadOnlyList<Evidence> Evidence,
    TableSection? Table = null,
    BinarySection? Binary = null,
    IReadOnlyList<StableLineSource>? StableLines = null,
    Limits? EffectiveLimits = null);

/// <summary>
/// The result of scoring one file against one format family.
/// </summary>
/// <param name="Score">A score between 0 and 1.</param>
/// <param name="Reason">A fixed-template reason with value-free numbers.</param>
/// <param name="State">
/// What the scoring pass learned, handed back to <see cref="IFormatProfiler.Profile"/> when the profiler wins, so
/// that a structure pass is not repeated. It is never written to the descriptor.
/// </param>
/// <param name="NotChosenIssues">
/// Issues that explain why this profiler declined a file, written to the descriptor when another profiler wins
/// (for example <c>tooIrregular</c>, when the delimited profiler gave up on a file with too many irregular records).
/// </param>
/// <param name="NotChosenEvidence">Evidence that goes with <paramref name="NotChosenIssues"/>.</param>
internal sealed record FormatScore(
    double Score,
    string Reason,
    object? State = null,
    IReadOnlyList<Issue>? NotChosenIssues = null,
    IReadOnlyList<Evidence>? NotChosenEvidence = null);

/// <summary>A profiler for one format family: it scores how well a file fits, and describes files that it wins.</summary>
internal interface IFormatProfiler
{
    /// <summary>The family name, one of <see cref="FormatRegistry.Order"/>.</summary>
    string Family { get; }

    /// <summary>
    /// Scores how well the file fits this family. A profiler that needs the structure of the file reads the source
    /// here (a delimited table is scored by a whole-file tokenizer pass); one that needs only the byte pass reads
    /// <paramref name="sniff"/>.
    /// </summary>
    /// <param name="source">The source; open it again for each pass.</param>
    /// <param name="sniff">The byte-pass result.</param>
    /// <param name="context">The options and limits.</param>
    /// <returns>The score, a fixed-template reason with value-free numbers, and anything to hand to <see cref="Profile"/>.</returns>
    FormatScore Score(IProfileSource source, SniffResult sniff, ProfileContext context);

    /// <summary>Profiles the file. Called only when this profiler wins.</summary>
    /// <param name="source">The source; open it again for each pass.</param>
    /// <param name="sniff">The byte-pass result.</param>
    /// <param name="context">The options and limits.</param>
    /// <param name="score">What <see cref="Score"/> returned for this file.</param>
    /// <returns>The parts of the descriptor.</returns>
    FormatProfile Profile(IProfileSource source, SniffResult sniff, ProfileContext context, FormatScore score);
}
