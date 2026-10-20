// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>What a format profiler may use besides the file: the options and the limits in effect.</summary>
/// <param name="Options">The profile options.</param>
/// <param name="Limits">The caps and table versions.</param>
internal sealed record ProfileContext(ProfileOptions Options, Limits Limits);

/// <summary>The parts of the descriptor that a format profiler produces.</summary>
/// <param name="Dialect">The family dialect, or null.</param>
/// <param name="Layout">The layout section.</param>
/// <param name="LineEndings">Line-ending counts (zero for non-text files).</param>
/// <param name="Issues">Issues, in any order; the caller sorts by code.</param>
/// <param name="Evidence">Evidence entries, in any order; the caller sorts by stage and topic.</param>
/// <param name="Table">The table section, for tabular files.</param>
/// <param name="Binary">The binary section, for binary files.</param>
internal sealed record FormatProfile(
    Dialect? Dialect,
    LayoutSection Layout,
    LineEndings LineEndings,
    IReadOnlyList<Issue> Issues,
    IReadOnlyList<Evidence> Evidence,
    TableSection? Table = null,
    BinarySection? Binary = null);

/// <summary>A profiler for one format family: it scores how well a file fits, and describes files that it wins.</summary>
internal interface IFormatProfiler
{
    /// <summary>The family name, one of <see cref="FormatRegistry.Order"/>.</summary>
    string Family { get; }

    /// <summary>Scores how well the byte-pass result fits this family.</summary>
    /// <param name="sniff">The byte-pass result.</param>
    /// <returns>A score between 0 and 1, and a fixed-template reason with value-free numbers.</returns>
    (double Score, string Reason) Score(SniffResult sniff);

    /// <summary>Profiles the file. Called only when this profiler wins.</summary>
    /// <param name="source">The source; open it again for each pass.</param>
    /// <param name="sniff">The byte-pass result.</param>
    /// <param name="context">The options and limits.</param>
    /// <returns>The parts of the descriptor.</returns>
    FormatProfile Profile(IProfileSource source, SniffResult sniff, ProfileContext context);
}
