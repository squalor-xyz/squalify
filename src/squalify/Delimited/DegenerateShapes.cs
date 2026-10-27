// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The degenerate table shapes that the profiler claims although no candidate has a run of two records (sq-34).</summary>
internal enum DegenerateShape
{
    /// <summary>An ordinary table.</summary>
    None,

    /// <summary>A header line and no data record.</summary>
    HeaderOnly,

    /// <summary>One column: no delimiter occurs, and the first line is a header that the values fail.</summary>
    SingleColumn,
}

/// <summary>
/// The second chance for a file that no delimiter candidate claims (sq-34). Two shapes are tables although their data
/// run has fewer than two records: a header line and no data record, and one column with no delimiter at all. Pass 1
/// only sees field counts, so <see cref="DialectSelector"/> marks the candidates that may be one of the shapes
/// (<see cref="CandidateResult.LooksHeaderOnly"/>, <see cref="CandidateResult.LooksSingleColumn"/>), and this class
/// holds the policy: which candidates qualify, how many lines it reads to see the cells, and what it claims. It runs
/// only when no candidate is viable, so it never changes the choice for a file that was a table before.
/// </summary>
internal static class DegenerateShapes
{
    /// <summary>The fewest records of a one-column table: a header and two values.</summary>
    public const int MinSingleColumnRecords = 3;

    /// <summary>The profiler score of a claimed degenerate table: 0.3 plus 0.4 times 0.5, because the evidence is thin.</summary>
    public const double ProfilerScore = 0.5;

    // A header name is short. A long cell is a sentence, and a one-line sentence with a comma is not a table.
    private const int MaxHeaderNameLength = 64;

    /// <summary>Claims a degenerate table, when the file is one.</summary>
    /// <param name="selection">The dialect selection of pass 1.</param>
    /// <param name="source">The source, read again for a few lines.</param>
    /// <param name="sniff">The byte-pass result.</param>
    /// <param name="context">The options and limits.</param>
    /// <returns>The selection with <see cref="DialectSelection.Best"/> and the shape evidence set, or <paramref name="selection"/> unchanged.</returns>
    public static DialectSelection Resolve(DialectSelection selection, IProfileSource source, SniffResult sniff, ProfileContext context)
    {
        if (selection.Best is not null)
        {
            return selection;
        }

        return TryHeaderOnly(selection, source, sniff, context) ?? TrySingleColumn(selection, source, sniff, context) ?? selection;
    }

    // The header-only candidate with the most fields whose one multi-field record reads as a header (ties go to candidate order).
    private static DialectSelection? TryHeaderOnly(DialectSelection selection, IProfileSource source, SniffResult sniff, ProfileContext context)
    {
        CandidateResult? best = null;
        foreach (var candidate in selection.Candidates.Where(static c => c.LooksHeaderOnly))
        {
            if ((best is null || candidate.K > best.K) && ReadsAsHeader(candidate, source, sniff, context))
            {
                best = candidate;
            }
        }

        if (best is null)
        {
            return null;
        }

        // The claimed candidate is the pass-1 result of a run of one record, so its run facts are the header's own; the
        // score and the clean share are set to 1 because there is no other record to be unclean, and are not written.
        var claimed = best with
        {
            Viable = true,
            Shape = DegenerateShape.HeaderOnly,
            Score = 1.0,
            Clean = 1.0,
        };
        var reason = "header only; k=" + Ordinal.Invariant(best.K) + "; header on line " + Ordinal.Invariant(best.RunFirstLine) + "; no data record";
        return Claim(
            selection,
            claimed,
            new Evidence("dialect", "delimiter", DelimiterCandidates.Text(best.Kind), selection.Evidence.Candidates),
            ShapeEvidence("headerOnly", reason, "a line has several fields"),
            "delimiter " + DelimiterCandidates.Word(best.Kind) + "; " + reason);
    }

    // The header line must lie within the first headerSample lines, so that the read is bounded; the lines above it must
    // look like a preamble (blank, comment or key=value), or the file is prose that ends in a line with a comma; and every
    // cell must read like a column name: filled, distinct, not a number, at most 64 characters, and a single word (no
    // white space in an unquoted cell, so `Hello, world` is text and `Time,Vcc(V),Icc` is a table).
    private static bool ReadsAsHeader(CandidateResult candidate, IProfileSource source, SniffResult sniff, ProfileContext context)
    {
        if (candidate.RunFirstLine > context.Limits.HeaderSample)
        {
            return false;
        }

        var texts = ReadLines(source, sniff, 1, candidate.RunFirstLine);
        for (var line = 1L; line < candidate.RunFirstLine; line++)
        {
            if (!texts.TryGetValue(line, out var above) || LineClassifier.Classify(above, [above], DelimiterCandidates.Char(candidate.Kind)).Kind is not (LineKind.Blank or LineKind.Comment or LineKind.KeyValue))
            {
                return false;
            }
        }

        var records = ReadRecords(texts, candidate.Kind, candidate.RunFirstLine, candidate.RunFirstLine, context.Limits.RunawayQuote);
        if (records.Count != 1 || records[0].IsComment || records[0].FieldCount != candidate.K)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var c = 0; c < records[0].FieldCount; c++)
        {
            var cell = records[0].Cells[c];
            var text = ValueClassifier.Trim(cell);
            if (text.IsEmpty || text.Length > MaxHeaderNameLength || !seen.Add(Ordinal.AsciiFold(text.ToString())))
            {
                return false;
            }

            if (IsNumber(cell, '.') || IsNumber(cell, ','))
            {
                return false;
            }

            if (!records[0].Quoted[c] && cell.AsSpan().IndexOfAny(ValueClassifier.TrimChars) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumber(string cell, char decimalSeparator)
    {
        return ValueClassifier.Classify(cell, decimalSeparator).Vote is ValueVote.Int or ValueVote.Float;
    }

    // One column: every non-whitespace candidate sees one field in every record (the comma candidate then tokenizes it),
    // and the first line must be the header that the HeaderDetector finds because the values below fail the column's
    // type. An all-string column gets only an assumed header there, which is not enough: it would call prose a table.
    private static DialectSelection? TrySingleColumn(DialectSelection selection, IProfileSource source, SniffResult sniff, ProfileContext context)
    {
        if (selection.Candidates.Where(static c => c.Kind != DelimiterKind.Whitespace).Any(static c => !c.LooksSingleColumn))
        {
            return null;
        }

        var candidate = selection.Candidates.First(static c => c.Kind == DelimiterKind.Comma);
        var records = ReadRecords(ReadLines(source, sniff, 1, context.Limits.HeaderSample), candidate.Kind, 1, context.Limits.HeaderSample, context.Limits.RunawayQuote);
        if (records.Count < MinSingleColumnRecords || records.Any(static r => r.FieldCount != 1))
        {
            return null;
        }

        HeaderDetector.Classify(records, 1, '.');
        var decision = HeaderDetector.Detect(records, 1);
        if (decision.HeaderIndex != 0 || decision.Assumed || decision.Relaxed)
        {
            return null;
        }

        // The comma candidate saw one field per record, so its pass-1 facts describe the column; the run facts that it
        // does not have (k, the run, the score) are filled in here. The score and the clean share are 1 and are not written.
        var claimed = candidate with
        {
            K = 1,
            HasRun = true,
            InRun = candidate.Total,
            Clean = 1.0,
            Score = 1.0,
            Viable = true,
            Shape = DegenerateShape.SingleColumn,
        };
        var reason = "single column; " + Ordinal.Invariant(candidate.Total) + " records; the header on line " + Ordinal.Invariant(candidate.RunFirstLine) + " fails the column's type";
        return Claim(
            selection,
            claimed,
            new Evidence("dialect", "delimiter", DelimiterCandidates.NoneName, selection.Evidence.Candidates),
            ShapeEvidence("singleColumn", reason, "no line has several fields"),
            reason);
    }

    // The shape evidence lists both shapes in value order: the one taken scores 1 with its reason, the other 0 with why
    // it was declined.
    private static Evidence ShapeEvidence(string taken, string reason, string declinedBecause)
    {
        var headerOnly = taken == "headerOnly"
            ? new Candidate("headerOnly", 1.0, reason)
            : new Candidate("headerOnly", 0.0, "declined: " + declinedBecause);
        var singleColumn = taken == "singleColumn"
            ? new Candidate("singleColumn", 1.0, reason)
            : new Candidate("singleColumn", 0.0, "declined: " + declinedBecause);
        return new Evidence("dialect", "shape", taken, [headerOnly, singleColumn]);
    }

    private static DialectSelection Claim(DialectSelection selection, CandidateResult claimed, Evidence delimiter, Evidence shape, string reason)
    {
        return new DialectSelection
        {
            Candidates = selection.Candidates,
            Best = claimed,
            Evidence = delimiter,
            TooIrregular = null,
            ProfilerScore = ProfilerScore,
            ShapeEvidence = shape,
            ShapeReason = reason,
        };
    }

    // The text of physical lines firstLine..lastLine (fewer when the file ends); the read stops after lastLine.
    private static Dictionary<long, string> ReadLines(IProfileSource source, SniffResult sniff, long firstLine, long lastLine)
    {
        return LineReader.Read(source, sniff, Enumerable.Range(0, (int)Math.Min(lastLine - firstLine + 1, int.MaxValue)).Select(i => firstLine + i));
    }

    // Tokenizes physical lines firstLine..lastLine with one candidate, the way the scan does, and returns the records.
    private static List<SampleRecord> ReadRecords(Dictionary<long, string> texts, DelimiterKind kind, long firstLine, long lastLine, long runawayQuote)
    {
        var records = new List<SampleRecord>();
        var sink = new HeadSink(records, firstLine);
        var tokenizer = new RecordTokenizer(kind, sink, runawayQuote, collectCells: true);
        long offset = 0;
        for (var line = firstLine; line <= lastLine && texts.TryGetValue(line, out var text); line++)
        {
            foreach (var c in text)
            {
                tokenizer.Char(c, offset++);
            }

            tokenizer.Break(offset + 1, offset);
            offset++;
        }

        tokenizer.Flush(offset);
        return records;
    }

    private sealed class HeadSink(List<SampleRecord> records, long firstLine) : IRecordSink
    {
        public void Record(RecordInfo record)
        {
            records.Add(new SampleRecord
            {
                Line = firstLine + record.StartLine - 1,
                EndLine = firstLine + record.EndLine - 1,
                StartByte = record.StartByte,
                Cells = record.Cells.ToArray(),
                Quoted = record.Quoted.ToArray(),
                IsComment = record.IsComment,
            });
        }

        public void Blank(long line)
        {
        }
    }
}
