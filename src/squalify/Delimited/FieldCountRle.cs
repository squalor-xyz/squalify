// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>A run of consecutive records that have the same field count.</summary>
internal sealed class FieldCountRun
{
    /// <summary>Creates a run from its first record.</summary>
    /// <param name="record">The first record.</param>
    public FieldCountRun(RecordInfo record)
    {
        FieldCount = record.FieldCount;
        Records = 1;
        FirstLine = record.StartLine;
        FirstByte = record.StartByte;
        LastLine = record.EndLine;
        EndByte = record.EndByte;
        Tokens = record.Tokens;
        CleanTokens = record.CleanTokens;
        CommentRecords = record.IsComment ? 1 : 0;
    }

    /// <summary>The field count of every record in the run.</summary>
    public int FieldCount { get; }

    /// <summary>The number of records.</summary>
    public long Records { get; private set; }

    /// <summary>The line where the first record starts.</summary>
    public long FirstLine { get; }

    /// <summary>The byte offset where the first record starts.</summary>
    public long FirstByte { get; }

    /// <summary>The line where the last record ends.</summary>
    public long LastLine { get; private set; }

    /// <summary>The byte offset where the next line after the last record starts.</summary>
    public long EndByte { get; private set; }

    /// <summary>The tokens of all records in the run.</summary>
    public long Tokens { get; private set; }

    /// <summary>The clean tokens of all records in the run.</summary>
    public long CleanTokens { get; private set; }

    /// <summary>The records in the run that are comment lines.</summary>
    public long CommentRecords { get; private set; }

    /// <summary>Adds a record with the same field count.</summary>
    /// <param name="record">The record.</param>
    public void Add(RecordInfo record)
    {
        Records++;
        LastLine = record.EndLine;
        EndByte = record.EndByte;
        Tokens += record.Tokens;
        CleanTokens += record.CleanTokens;
        if (record.IsComment)
        {
            CommentRecords++;
        }
    }
}

/// <summary>
/// The run-length-encoded field counts of one candidate: runs of <c>(fieldCount, records, firstLine, firstByte)</c>
/// plus the facts that the dialect score needs. A candidate with more runs than the limit is disqualified as too
/// irregular, which also bounds the memory that this structure uses.
/// </summary>
internal sealed class FieldCountRle : IRecordSink
{
    private readonly List<FieldCountRun> _runs = [];
    private readonly long _runLimit;
    private RecordTokenizer? _owner;

    /// <summary>Creates an empty encoding.</summary>
    /// <param name="runLimit">The most runs allowed (<c>limits.fieldCountRuns</c>).</param>
    public FieldCountRle(long runLimit)
    {
        _runLimit = runLimit;
    }

    /// <summary>The runs, in file order.</summary>
    public IReadOnlyList<FieldCountRun> Runs => _runs;

    /// <summary>The number of non-blank records.</summary>
    public long Records { get; private set; }

    /// <summary>The number of blank lines.</summary>
    public long BlankLines { get; private set; }

    /// <summary>Connects the tokenizer that feeds this encoding, so that it can be disqualified.</summary>
    /// <param name="owner">The tokenizer.</param>
    public void Attach(RecordTokenizer owner)
    {
        _owner = owner;
    }

    /// <inheritdoc />
    public void Record(RecordInfo record)
    {
        Records++;
        if (_runs.Count > 0 && _runs[^1].FieldCount == record.FieldCount)
        {
            _runs[^1].Add(record);
            return;
        }

        _runs.Add(new FieldCountRun(record));
        if (_runs.Count > _runLimit)
        {
            _owner?.Disqualify(Disqualification.TooIrregular, record.StartLine);
            _runs.Clear();
        }
    }

    /// <inheritdoc />
    public void Blank(long line)
    {
        BlankLines++;
    }
}
