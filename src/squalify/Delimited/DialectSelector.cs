// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>What pass 1 learned about one delimiter candidate.</summary>
internal sealed class CandidateResult
{
    /// <summary>The candidate.</summary>
    public required DelimiterKind Kind { get; init; }

    /// <summary>Why the candidate was disqualified, or none.</summary>
    public Disqualification Disqualified { get; init; }

    /// <summary>The line of the disqualification.</summary>
    public long DisqualifiedLine { get; init; }

    /// <summary>The modal field count among records with at least two fields (1 when there are none).</summary>
    public int K { get; init; } = 1;

    /// <summary>True when a data run was found.</summary>
    public bool HasRun { get; init; }

    /// <summary>Records in the run that have <see cref="K"/> fields.</summary>
    public long InRun { get; init; }

    /// <summary>Non-blank records in the file.</summary>
    public long Total { get; init; }

    /// <summary>The clean share of the run's tokens.</summary>
    public double Clean { get; init; } = 1.0;

    /// <summary>The candidate's score: records in the run with k fields over non-blank records, times the clean share.</summary>
    public double Score { get; init; }

    /// <summary>The line where the run starts.</summary>
    public long RunFirstLine { get; init; }

    /// <summary>The byte offset where the run starts.</summary>
    public long RunFirstByte { get; init; }

    /// <summary>The line where the run ends.</summary>
    public long RunLastLine { get; init; }

    /// <summary>The number of literal quotes the candidate saw.</summary>
    public long LiteralQuotes { get; init; }

    /// <summary>The number of quoted fields the candidate opened.</summary>
    public long QuotedFields { get; init; }

    /// <summary>The evidence reason, a fixed template with value-free numbers.</summary>
    public required string Reason { get; init; }

    /// <summary>True when the candidate may be chosen: its run has at least two records (six for whitespace runs) and its score is at least 0.5.</summary>
    public bool Viable { get; init; }
}

/// <summary>The outcome of choosing the delimiter.</summary>
internal sealed class DialectSelection
{
    /// <summary>Every candidate, in candidate order.</summary>
    public required IReadOnlyList<CandidateResult> Candidates { get; init; }

    /// <summary>The chosen candidate, or null when no candidate is viable.</summary>
    public CandidateResult? Best { get; init; }

    /// <summary>The evidence entry (stage <c>dialect</c>, topic <c>delimiter</c>).</summary>
    public required Evidence Evidence { get; init; }

    /// <summary>The <c>tooIrregular</c> issue when no candidate is viable and at least one was disqualified; otherwise null.</summary>
    public Issue? TooIrregular { get; init; }

    /// <summary>The profiler score: 0.3 plus 0.4 times the best candidate's score, or 0 when there is none.</summary>
    public double ProfilerScore { get; init; }
}

/// <summary>
/// Chooses the delimiter from the pass-1 tally of every candidate. For each candidate, k is the modal field count among
/// records with at least two fields (a tie goes to the larger k); the data run starts at the first record that, with the
/// next min(5, remaining) records, has k fields, and ends at the last record with k fields. The score is the share of
/// non-blank records that are in the run with k fields, times the clean share. The highest score wins; ties go to the
/// larger k, then to candidate order.
/// </summary>
internal static class DialectSelector
{
    /// <summary>A record opens a run when it and the next records, up to this many, have k fields.</summary>
    public const int RunWindow = 5;

    private const double MinScore = 0.5;
    private const long MinRunRecords = 2;

    // Whitespace runs are weak evidence (prose splits into words too), so they need a full window of records.
    private const long MinWhitespaceRunRecords = RunWindow + 1;
    private const int MaxListedLines = 10;

    /// <summary>Evaluates the candidates and chooses one.</summary>
    /// <param name="tallies">One tokenizer and its tally per candidate, in candidate order.</param>
    /// <param name="runLimit">The run limit (<c>limits.fieldCountRuns</c>), for the evidence reason.</param>
    /// <param name="runawayQuote">The runaway-quote limit (<c>limits.runawayQuote</c>), for the evidence reason.</param>
    /// <returns>The selection.</returns>
    public static DialectSelection Select(IReadOnlyList<(RecordTokenizer Tokenizer, FieldCountRle Rle)> tallies, long runLimit, long runawayQuote)
    {
        var results = new List<CandidateResult>();
        foreach (var (tokenizer, rle) in tallies)
        {
            results.Add(Evaluate(tokenizer, rle, runLimit, runawayQuote));
        }

        CandidateResult? best = null;
        foreach (var result in results)
        {
            if (!result.Viable)
            {
                continue;
            }

            if (best is null || result.Score > best.Score || (result.Score == best.Score && result.K > best.K && !TabBeatsWhitespace(best, result)))
            {
                best = result;
            }
        }

        var candidates = results
            .Select(static r => new Candidate(DelimiterCandidates.Text(r.Kind), r.Disqualified == Disqualification.None ? r.Score : 0.0, r.Reason))
            .ToArray();
        var evidence = new Evidence("dialect", "delimiter", best is null ? null : DelimiterCandidates.Text(best.Kind), candidates);

        Issue? irregular = null;
        if (best is null)
        {
            var lines = results
                .Where(static r => r.Disqualified != Disqualification.None)
                .Select(static r => r.DisqualifiedLine)
                .OrderBy(static l => l)
                .ToArray();
            if (lines.Length > 0)
            {
                irregular = new Issue("tooIrregular", lines.Length, FirstLines: lines.Take(MaxListedLines).ToArray());
            }
        }

        return new DialectSelection
        {
            Candidates = results,
            Best = best,
            Evidence = evidence,
            TooIrregular = irregular,
            ProfilerScore = best is null ? 0.0 : 0.3 + (0.4 * best.Score),
        };
    }

    // Whitespace's split always contains tab's, so on a tie tab wins whatever k is (owner decision 9).
    private static bool TabBeatsWhitespace(CandidateResult best, CandidateResult challenger)
    {
        return best.Kind == DelimiterKind.Tab && challenger.Kind == DelimiterKind.Whitespace;
    }

    private static CandidateResult Evaluate(RecordTokenizer tokenizer, FieldCountRle rle, long runLimit, long runawayQuote)
    {
        var kind = tokenizer.Kind;
        if (tokenizer.Disqualified == Disqualification.RunawayQuote)
        {
            return new CandidateResult
            {
                Kind = kind,
                Disqualified = tokenizer.Disqualified,
                DisqualifiedLine = tokenizer.DisqualifiedLine,
                LiteralQuotes = tokenizer.LiteralQuotes,
                QuotedFields = tokenizer.QuotedFields,
                Reason = "disqualified: runaway quote opened at line " + Ordinal.Invariant(tokenizer.DisqualifiedLine),
            };
        }

        if (tokenizer.Disqualified == Disqualification.TooIrregular)
        {
            return new CandidateResult
            {
                Kind = kind,
                Disqualified = tokenizer.Disqualified,
                DisqualifiedLine = tokenizer.DisqualifiedLine,
                LiteralQuotes = tokenizer.LiteralQuotes,
                QuotedFields = tokenizer.QuotedFields,
                Reason = "disqualified: too irregular, more than " + Ordinal.Invariant(runLimit) + " field-count runs by line " + Ordinal.Invariant(tokenizer.DisqualifiedLine),
            };
        }

        var runs = rle.Runs;
        var total = rle.Records;
        var k = ModalFieldCount(runs);
        var literal = QuotedSuffix(tokenizer.QuotedFields) + LiteralSuffix(tokenizer.LiteralQuotes);
        if (k == 0)
        {
            return new CandidateResult
            {
                Kind = kind,
                Total = total,
                LiteralQuotes = tokenizer.LiteralQuotes,
                QuotedFields = tokenizer.QuotedFields,
                Reason = "k=1; no run" + literal,
            };
        }

        var startIndex = -1;
        long before = 0;
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            // The run starts at the first record whose next min(5, remaining) records all have k fields. Only the
            // first record of a k-run can be the start, and it qualifies when the run is long enough or reaches the end.
            if (run.FieldCount == k && (run.Records >= RunWindow + 1 || before + run.Records == total))
            {
                startIndex = i;
                break;
            }

            before += run.Records;
        }

        if (startIndex < 0)
        {
            return new CandidateResult
            {
                Kind = kind,
                K = k,
                Total = total,
                LiteralQuotes = tokenizer.LiteralQuotes,
                QuotedFields = tokenizer.QuotedFields,
                Reason = "k=" + Ordinal.Invariant(k) + "; no run" + literal,
            };
        }

        var lastIndex = startIndex;
        for (var i = runs.Count - 1; i > startIndex; i--)
        {
            if (runs[i].FieldCount == k)
            {
                lastIndex = i;
                break;
            }
        }

        long inRun = 0;
        long tokens = 0;
        long clean = 0;
        for (var i = startIndex; i <= lastIndex; i++)
        {
            tokens += runs[i].Tokens;
            clean += runs[i].CleanTokens;
            if (runs[i].FieldCount == k)
            {
                inRun += runs[i].Records;
            }
        }

        var cleanShare = tokens > 0 ? clean / (double)tokens : 1.0;
        var score = (inRun / (double)total) * cleanShare;
        var first = runs[startIndex];
        var lastLine = runs[lastIndex].LastLine;
        var reason = "k=" + Ordinal.Invariant(k)
            + "; " + Ordinal.Invariant(inRun) + "/" + Ordinal.Invariant(total) + " records"
            + "; clean=" + CanonicalNumber.FormatFinite(cleanShare)
            + "; run lines " + Ordinal.Invariant(first.FirstLine) + "-" + Ordinal.Invariant(lastLine)
            + literal;
        return new CandidateResult
        {
            Kind = kind,
            K = k,
            HasRun = true,
            InRun = inRun,
            Total = total,
            Clean = cleanShare,
            Score = score,
            RunFirstLine = first.FirstLine,
            RunFirstByte = first.FirstByte,
            RunLastLine = lastLine,
            LiteralQuotes = tokenizer.LiteralQuotes,
            QuotedFields = tokenizer.QuotedFields,
            Reason = reason,
            Viable = inRun >= (kind == DelimiterKind.Whitespace ? MinWhitespaceRunRecords : MinRunRecords) && score >= MinScore,
        };
    }

    // The modal field count among records with at least two fields; a tie goes to the larger count. 0 when there is none.
    private static int ModalFieldCount(IReadOnlyList<FieldCountRun> runs)
    {
        var counts = new Dictionary<int, long>();
        foreach (var run in runs)
        {
            if (run.FieldCount >= 2)
            {
                counts[run.FieldCount] = counts.GetValueOrDefault(run.FieldCount) + run.Records;
            }
        }

        var best = 0;
        long bestRecords = 0;
        foreach (var (fieldCount, records) in counts)
        {
            if (records > bestRecords || (records == bestRecords && fieldCount > best))
            {
                best = fieldCount;
                bestRecords = records;
            }
        }

        return best;
    }

    private static string QuotedSuffix(long quotedFields)
    {
        return "; " + Ordinal.Invariant(quotedFields) + " quoted fields";
    }

    private static string LiteralSuffix(long literalQuotes)
    {
        return literalQuotes > 0 ? "; " + Ordinal.Invariant(literalQuotes) + " literal quotes" : string.Empty;
    }
}
