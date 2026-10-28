// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>What the scoring pass of the delimited profiler keeps for the profile pass.</summary>
/// <param name="Selection">The delimiter choice.</param>
/// <param name="Counter">The line counts of the whole file.</param>
/// <param name="BlockCandidates">The places inside the winner's data run where a later block may start, from its field-count runs.</param>
internal sealed record DelimitedStructure(DialectSelection Selection, LineCounter Counter, IReadOnlyList<BlockCandidate> BlockCandidates);

/// <summary>
/// The profiler for delimited text. Pass 1 (scoring) runs a quote-aware tokenizer for every delimiter candidate at the
/// same time over the whole file, keeps run-length-encoded field counts, and picks the dialect and the data run.
/// Pass 2 (profiling) re-reads the file with the chosen dialect and builds the regions, the header, the annotation and
/// units rows and the field type votes. See <c>docs/design.md</c>, "Delimited text".
/// </summary>
internal sealed class DelimitedProfiler : IFormatProfiler
{
    /// <inheritdoc />
    public string Family => "delimited";

    /// <inheritdoc />
    public FormatScore Score(IProfileSource source, SniffResult sniff, ProfileContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sniff);
        ArgumentNullException.ThrowIfNull(context);
        if (sniff.Kind != "text" || sniff.Charset is null)
        {
            return new FormatScore(0.0, sniff.Kind == "empty" ? "file is empty" : "not text: binary content");
        }

        var limits = context.Limits;
        var tallies = new List<(RecordTokenizer Tokenizer, FieldCountRle Rle)>();
        foreach (var kind in DelimiterCandidates.All)
        {
            var rle = new FieldCountRle(limits.FieldCountRuns);
            var tokenizer = new RecordTokenizer(kind, rle, limits.RunawayQuote);
            rle.Attach(tokenizer);
            tallies.Add((tokenizer, rle));
        }

        var counter = new LineCounter();
        TextScanner.Scan(source, sniff, tallies.Select(static t => t.Tokenizer).ToArray(), counter, null);
        var selection = DialectSelector.Select(tallies, limits.FieldCountRuns, limits.RunawayQuote);
        selection = DegenerateShapes.Resolve(selection, source, sniff, context);
        if (selection.Best is { } best)
        {
            var reason = selection.ShapeReason ?? "delimiter " + DelimiterCandidates.Word(best.Kind) + "; " + best.Reason;

            // Only the winner's run-length encoding is read for blocks; what the pass keeps is the short list of candidates.
            // A header-only or one-column table has no repeated blocks.
            var candidates = BlockDetector.None;
            if (best.Shape == DegenerateShape.None)
            {
                var winner = tallies.First(t => t.Tokenizer.Kind == best.Kind).Rle;
                candidates = BlockDetector.Candidates(winner.Runs, best.K, best.RunFirstLine, best.RunLastLine, limits.HeaderSample);
            }

            return new FormatScore(selection.ProfilerScore, reason, new DelimitedStructure(selection, counter, candidates));
        }

        if (selection.TooIrregular is { } irregular)
        {
            return new FormatScore(
                0.0,
                "too irregular: " + Ordinal.Invariant(irregular.Count) + " candidates disqualified and none left",
                NotChosenIssues: [irregular],
                NotChosenEvidence: [selection.Evidence]);
        }

        return new FormatScore(0.0, "no steady delimited run");
    }

    /// <inheritdoc />
    public FormatProfile Profile(IProfileSource source, SniffResult sniff, ProfileContext context, FormatScore score)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sniff);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(score);
        if (score.State is not DelimitedStructure structure || structure.Selection.Best is not { } chosen)
        {
            throw new InvalidOperationException("The delimited profiler needs the structure from its own scoring pass.");
        }

        var pass = new DelimitedPass(chosen, context, structure.BlockCandidates);
        var tokenizer = new RecordTokenizer(
            chosen.Kind,
            pass,
            context.Limits.RunawayQuote,
            collectCells: true,
            captureSpans: true,
            rawCap: pass.RawCap);
        TextScanner.Scan(source, sniff, [tokenizer], new LineCounter(), pass);
        return pass.Build(source, sniff, structure);
    }
}
