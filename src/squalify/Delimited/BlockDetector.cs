// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// A place where a second (or later) block of the table may start: a run of at least
/// <see cref="BlockDetector.MinRunRecords"/> records with k fields that follows records with another field count. Whether it
/// is a block is decided in pass 2, where the cells are (see <see cref="BlockDetector"/>).
/// </summary>
internal sealed class BlockCandidate
{
    /// <summary>The 1-based line of the first record after the previous k-field record: where the block's preamble would start.</summary>
    public required long GapFirstLine { get; init; }

    /// <summary>The byte offset where that record starts.</summary>
    public required long GapFirstByte { get; init; }

    /// <summary>The number of records between the previous k-field record and the run (all with another field count).</summary>
    public required long GapRecords { get; init; }

    /// <summary>The 1-based line where the run starts: the header line, if the run is a block.</summary>
    public required long HeaderLine { get; init; }

    /// <summary>The byte offset where the run starts.</summary>
    public required long HeaderByte { get; init; }

    /// <summary>The number of records in the run.</summary>
    public required long Records { get; init; }

    /// <summary>The line where the run ends.</summary>
    public required long LastLine { get; init; }

}

/// <summary>
/// Finds the candidates for a new block in the winner's run-length-encoded field counts (<see cref="FieldCountRun"/>).
/// Only counts are used here: a candidate is a k-run of at least <see cref="MinRunRecords"/> records after the data run's
/// first k-run, and the records between it and the previous k-run are its gap. A candidate becomes a block only in
/// pass 2, by one of two rules (owner decisions 2026-10-27). An exact repeat of the first block's header among the first
/// <see cref="RepeatWindow"/> records of the run is the header of a new block, and the records before it are the block's
/// preamble and annotation. Otherwise the first record must be a header line whose values fail at least
/// <see cref="MinContrast"/> of the typed columns below it, and the gap before it must hold a text, key=value or comment
/// line. If neither holds, the odd records are ragged rows of one table, so one bad row cannot split a table. The
/// run-start rule of <see cref="DialectSelector"/> is not involved: the detector takes the run's first and last line.
/// </summary>
internal static class BlockDetector
{
    /// <summary>The fewest records a run needs to be a block.</summary>
    public const int MinRunRecords = 5;

    /// <summary>
    /// How many records at the start of a candidate run are looked at to decide whether it opens with a header: the column
    /// types below the first record are the join of the records after it, up to this many. It bounds what is held per candidate.
    /// </summary>
    public const int SampleRecords = 100;

    /// <summary>How many records at the start of a candidate run are searched for an exact repeat of the first block's header.</summary>
    public const int RepeatWindow = 5;

    /// <summary>The share of the typed columns that a header line must fail to open a block without being a repeat.</summary>
    public const double MinContrast = 0.5;

    /// <summary>No candidates.</summary>
    public static IReadOnlyList<BlockCandidate> None { get; } = [];

    /// <summary>
    /// Whether a record in the gap before a candidate is not a data row: a comment, a key=value line, or a line of a single
    /// field. A blank line is never a record, and a line of two or more fields that is no key=value line (a ragged row such
    /// as <c>S99,1100</c> or <c>7,8</c>) looks like a short data row, so it does not make a gap.
    /// </summary>
    /// <param name="kind">The line kind of the record.</param>
    /// <param name="fields">The number of fields of the record.</param>
    /// <returns>True for a line that is no data row.</returns>
    public static bool IsNonDataLine(LineKind kind, int fields)
    {
        return kind is LineKind.Comment or LineKind.KeyValue || fields < 2;
    }

    /// <summary>Lists the block candidates inside the data run, in file order.</summary>
    /// <param name="runs">The winner's field-count runs, in file order.</param>
    /// <param name="k">The modal field count of the table.</param>
    /// <param name="runFirstLine">The line where the data run starts.</param>
    /// <param name="runLastLine">The line where the data run ends.</param>
    /// <param name="maxGapRecords">The most records a block's preamble may have (the header sample limit): it is buffered until the block is decided.</param>
    /// <returns>The candidates.</returns>
    public static IReadOnlyList<BlockCandidate> Candidates(IReadOnlyList<FieldCountRun> runs, int k, long runFirstLine, long runLastLine, long maxGapRecords)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var candidates = new List<BlockCandidate>();
        var last = -1;
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.FirstLine < runFirstLine)
            {
                continue;
            }

            if (run.FirstLine > runLastLine)
            {
                break;
            }

            if (run.FieldCount != k)
            {
                continue;
            }

            if (last >= 0 && run.Records >= MinRunRecords)
            {
                long gapRecords = 0;
                for (var j = last + 1; j < i; j++)
                {
                    gapRecords += runs[j].Records;
                }

                if (gapRecords > 0 && gapRecords <= maxGapRecords)
                {
                    candidates.Add(new BlockCandidate
                    {
                        GapFirstLine = runs[last + 1].FirstLine,
                        GapFirstByte = runs[last + 1].FirstByte,
                        GapRecords = gapRecords,
                        HeaderLine = run.FirstLine,
                        HeaderByte = run.FirstByte,
                        Records = run.Records,
                        LastLine = run.LastLine,
                    });
                }
            }

            last = i;
        }

        return candidates;
    }
}
