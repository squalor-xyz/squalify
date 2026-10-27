// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Pass 2 of the delimited profiler. It receives the records of the chosen dialect in file order and sorts them by
/// where they lie: before the data run (preamble), inside it (header sample, then data) or after it (footer).
/// The first <c>limits.headerSample</c> records of the run are buffered, scored as header candidates, and then
/// processed as annotation, header, units or data; later records stream straight into the field accumulators
/// (<see cref="FieldAccumulator"/>), which hold the type votes and every field statistic.
/// A file may hold several blocks (sq-13): the candidates come from the run-length-encoded field counts
/// (<see cref="BlockDetector"/>), and a candidate is a block when its first record is a header line. While a candidate is
/// open its records are held, so that the records can be ragged rows of one table after all.
/// </summary>
internal sealed class DelimitedPass : IRecordSink, IPhysicalLineSink
{
    private const int MaxListedLines = 10;
    private const int CommentPrefix = 16;

    private readonly CandidateResult _dialect;
    private readonly ProfileContext _context;
    private readonly int _k;
    private readonly long _runFirstLine;
    private readonly long _runLastLine;
    private readonly char _delimiterChar;
    private readonly ValuesGate _gate;

    // Preamble (the first block's; a later block keeps its own in its BlockInfo).
    private readonly PreambleData _preamble = new();
    private readonly List<(long Line, StableLineKind Kind, string? Key)> _stableLines = [];

    // Footer.
    private readonly long[] _footerKinds = new long[5];
    private long _footerFirstLine = -1;
    private long _footerFirstByte;

    // The run.
    private readonly List<SampleRecord> _sample = [];
    private bool _decided;
    private char _decimal = '.';
    private HeaderDecision? _decision;
    private Evidence? _decimalEvidence;
    private SampleRecord? _headerRecord;
    private SampleRecord? _unitsRecord;
    private readonly List<AnnotationRowSource> _annotation = [];
    private long _dataFirstLine = -1;
    private long _dataFirstByte;

    // Blocks after the first.
    private readonly IReadOnlyList<BlockCandidate> _candidates;
    private readonly List<BlockInfo> _later = [];
    private readonly long _maxBlocks;
    private PendingBlock? _pending;
    private int _nextCandidate;
    private readonly long[] _gapBlanks;
    private int _gapCursor;
    private int _blankCursor;
    private bool _profiling = true;
    private string[]? _firstNames;
    private long _unprofiledBlocks;
    private readonly List<long> _unprofiledLines = [];
    private long _truncatedBlocks;
    private readonly List<long> _truncatedLines = [];

    // Data statistics.
    private FieldAccumulator[] _accumulators = [];
    private readonly FieldCaps _caps;
    private readonly bool[] _nullMask;
    private readonly RowClassAccumulator _rowClasses;
    private long _rows;
    private long _minFields = long.MaxValue;
    private long _maxFields;
    private long _ragged;
    private readonly List<long> _raggedLines = [];
    private long _commentsInData;
    private readonly List<long> _commentLines = [];

    // Padding facts.
    private long _nonEmptyCells;
    private long _paddedCells;
    private long _runLines;
    private long _firstLineLength = -1;
    private bool _constantLineLength = true;

    /// <summary>Creates the pass for the chosen dialect.</summary>
    /// <param name="dialect">The chosen candidate, with its run.</param>
    /// <param name="context">The options and limits.</param>
    /// <param name="candidates">The places where a later block may start, from the scoring pass; none when null.</param>
    public DelimitedPass(CandidateResult dialect, ProfileContext context, IReadOnlyList<BlockCandidate>? candidates = null)
    {
        _candidates = candidates ?? [];
        _gapBlanks = new long[_candidates.Count];
        _maxBlocks = context.Limits.Regions;
        _dialect = dialect;
        _context = context;
        _k = dialect.K;
        _runFirstLine = dialect.RunFirstLine;
        _runLastLine = dialect.RunLastLine;
        _delimiterChar = DelimiterCandidates.Char(dialect.Kind);
        _caps = MemoryBudget.Effective(context.Limits, _k);
        _nullMask = new bool[_k];
        _rowClasses = new RowClassAccumulator(_k, context.Limits.RowClasses);
        _gate = context.Gate;
    }

    /// <summary>The per-field caps in effect: the configured caps after the global budget (<see cref="MemoryBudget"/>).</summary>
    internal FieldCaps Caps => _caps;

    /// <summary>The number of preamble values held in memory; only values that may be written are held.</summary>
    internal int PreambleValuesHeld => _preamble.Values.Count(static v => v is not null);

    /// <summary>How much raw text to keep for a record that starts on a line: all of it outside the run, a prefix inside it.</summary>
    /// <param name="line">The 1-based line.</param>
    /// <returns>The number of characters.</returns>
    public int RawCap(long line)
    {
        if (line < _runFirstLine || line > _runLastLine)
        {
            return int.MaxValue;
        }

        // The records between a block candidate's previous k-field record and the candidate may become the preamble of a
        // block: they are classified when they end, so they keep all their text for that one call, and no more.
        return InGap(line) ? int.MaxValue : CommentPrefix;
    }

    // Whether a line lies in the gap before a block candidate. The lines arrive in order, so a cursor is enough.
    private bool InGap(long line)
    {
        while (_gapCursor < _candidates.Count && line >= _candidates[_gapCursor].HeaderLine)
        {
            _gapCursor++;
        }

        return _gapCursor < _candidates.Count && line >= _candidates[_gapCursor].GapFirstLine;
    }

    /// <inheritdoc />
    public void Blank(long line)
    {
        if (line < _runFirstLine)
        {
            _preamble.Kinds[(int)LineKind.Blank]++;
        }
        else if (_footerFirstLine >= 0)
        {
            _footerKinds[(int)LineKind.Blank]++;
        }
        else
        {
            // A blank line inside a candidate's gap (after its first record) belongs to the preamble of the block.
            while (_blankCursor < _candidates.Count && line >= _candidates[_blankCursor].HeaderLine)
            {
                _blankCursor++;
            }

            if (_blankCursor < _candidates.Count && line > _candidates[_blankCursor].GapFirstLine)
            {
                _gapBlanks[_blankCursor]++;
            }
        }
    }

    /// <inheritdoc />
    public void Line(long line, long startByte, long contentEnd, bool blank)
    {
        if (blank || line < _runFirstLine || line > _runLastLine)
        {
            return;
        }

        var length = contentEnd - startByte;
        _runLines++;
        if (_firstLineLength < 0)
        {
            _firstLineLength = length;
        }
        else if (length != _firstLineLength)
        {
            _constantLineLength = false;
        }
    }

    /// <inheritdoc />
    public void Record(RecordInfo record)
    {
        if (record.StartLine < _runFirstLine)
        {
            AddPreamble(record);
            return;
        }

        if (record.StartLine > _runLastLine)
        {
            if (!_decided)
            {
                Decide();
            }

            ResolvePending();
            AddFooter(record);
            return;
        }

        CountPadding(record.Cells);
        var comment = record.IsComment;
        if (!_decided)
        {
            _sample.Add(ToSample(record, comment));
            if (_sample.Count >= _context.Limits.HeaderSample)
            {
                Decide();
            }

            return;
        }

        if (_pending is null && !StartsPending(record.StartLine))
        {
            // The common case: a record of the open block streams straight into the accumulators, with no copy.
            MarkDataStart(record.StartLine, record.StartByte);
            ProcessData(record.Cells, record.FieldCount, record.StartLine, comment);
            return;
        }

        Stream(ToSample(record, comment));
    }

    private SampleRecord ToSample(RecordInfo record, bool comment)
    {
        return new SampleRecord
        {
            GapClass = InGap(record.StartLine) ? ClassifyPreambleLine(record.Raw ?? string.Empty, record.Cells) : null,
            Line = record.StartLine,
            StartByte = record.StartByte,
            EndLine = record.EndLine,
            Cells = record.Cells.ToArray(),
            Spans = record.Spans.ToArray(),
            IsComment = comment,
        };
    }

    /// <summary>Finishes the pass and builds the parts of the descriptor.</summary>
    /// <param name="source">The source, read again only when a field name holds invalid bytes.</param>
    /// <param name="sniff">The byte-pass result.</param>
    /// <param name="structure">The scoring pass's result.</param>
    /// <returns>The format profile.</returns>
    public FormatProfile Build(IProfileSource source, SniffResult sniff, DelimitedStructure structure)
    {
        if (!_decided)
        {
            Decide();
        }

        ResolvePending();
        var counter = structure.Counter;
        var issues = new List<Issue>();
        if (counter.MixedLineEndings() is { } mixed)
        {
            issues.Add(mixed);
        }

        var hasHeader = _headerRecord is not null;
        var names = new string?[_k];
        var nameSpans = new (long Start, long End)?[_k];
        if (_headerRecord is { } header)
        {
            for (var c = 0; c < _k; c++)
            {
                names[c] = ValueClassifier.Trim(header.Cells[c]).ToString();
                if (header.Spans is { } spans && (2 * c) + 1 < spans.Length)
                {
                    nameSpans[c] = (spans[2 * c], spans[(2 * c) + 1]);
                }
            }

            AddHeaderIssues(issues, names!, header.Line);
        }

        var hex = ReadNameHex(source, names, nameSpans);
        var conventions = hasHeader ? HeaderConventionParser.Analyze(names!, UnitsCellsWithValue()) : null;
        var fields = new List<FieldProfile>();
        var orderConflicts = new List<(long Index, DateOrderCounts Counts)>();
        for (var c = 0; c < _k; c++)
        {
            fields.Add(BuildField(c, names[c], hex[c]) with { NameParts = conventions?.Parts[c] });
            if (_accumulators[c].OrderConflict is { } conflict)
            {
                orderConflicts.Add((c, conflict));
            }
        }

        if (_ragged > 0)
        {
            issues.Add(new Issue("raggedRows", _ragged, FirstLines: _raggedLines.ToArray()));
        }

        if (_commentsInData > 0)
        {
            issues.Add(new Issue("commentInData", _commentsInData, FirstLines: _commentLines.ToArray()));
        }

        if (_rows == 0)
        {
            // A header (or units row) and no data record: a table with zero rows, described as such.
            issues.Add(new Issue("noDataRows", 1, FirstLines: [_headerRecord?.Line ?? _runFirstLine]));
        }

        if (_unprofiledBlocks > 0)
        {
            issues.Add(new Issue("heterogeneousBlocks", _unprofiledBlocks, FirstLines: _unprofiledLines.ToArray()));
        }

        if (_truncatedBlocks > 0)
        {
            issues.Add(new Issue("blocksTruncated", _truncatedBlocks, FirstLines: _truncatedLines.ToArray()));
        }

        var regions = BuildRegions(structure.Counter.Lines, sniff.Length);
        var padded = _nonEmptyCells > 0 ? _paddedCells / (double)_nonEmptyCells : 0.0;
        // A table with no data record has one line, which is trivially of constant length: that says nothing, so it is false.
        var constant = _rows > 0 && _runLines > 0 && _constantLineLength;
        var dialect = new DelimitedDialect(
            _dialect.Shape == DegenerateShape.SingleColumn ? DelimiterCandidates.NoneName : DelimiterCandidates.Text(_dialect.Kind),
            "\"",
            hasHeader,
            _unitsRecord is not null,
            _decimal == ',' ? "," : ".",
            new PaddingInfo(_paddedCells > 0, padded, constant, constant ? _firstLineLength : null),
            Blocks: 1 + _later.Count);
        var table = new TableSection(
            _rows,
            _rows == 0 ? new MinMax(_k, _k) : new MinMax(_minFields, _maxFields),
            fields,
            _rowClasses.Build());
        var evidence = new List<Evidence> { structure.Selection.Evidence };
        if (structure.Selection.ShapeEvidence is not null)
        {
            evidence.Add(structure.Selection.ShapeEvidence);
        }

        if (_decimalEvidence is not null)
        {
            evidence.Add(_decimalEvidence);
        }

        if (_decision is not null)
        {
            evidence.Add(_decision.Evidence);
        }

        if (conventions is not null)
        {
            evidence.Add(conventions.Evidence);
        }

        if (_later.Count > 0)
        {
            evidence.Add(BlockEvidence());
        }

        if (FieldAccumulator.ConflictEvidence(orderConflicts) is { } conflictEvidence)
        {
            evidence.Add(conflictEvidence);
        }

        return new FormatProfile(
            dialect,
            new LayoutSection(regions, HeaderConvention: conventions?.Info, MaxLineLength: counter.MaxLineLength),
            new LineEndings(counter.Crlf, counter.Lf, counter.Cr),
            issues,
            evidence,
            Table: table,
            StableLines: ReadStableLines(source, sniff),
            EffectiveLimits: MemoryBudget.Record(_context.Limits, _caps));
    }

    // The text of the physical lines that may become stable-line regexes: the header line (when it is one physical line)
    // and the preamble lines noted in AddPreamble. One bounded read of the start of the file.
    private IReadOnlyList<StableLineSource> ReadStableLines(IProfileSource source, SniffResult sniff)
    {
        var wanted = _stableLines.ToList();
        if (_headerRecord is { } header && header.EndLine == header.Line)
        {
            wanted.Add((header.Line, StableLineKind.Header, null));
        }

        var texts = LineReader.Read(source, sniff, wanted.Select(static w => w.Line));
        var result = new List<StableLineSource>();
        foreach (var (line, kind, key) in wanted)
        {
            if (texts.TryGetValue(line, out var text))
            {
                result.Add(new StableLineSource(line, kind, key, text));
            }
        }

        return result;
    }

    // Per column, whether the units row has a value in it; null when the file has no units row.
    private bool[]? UnitsCellsWithValue()
    {
        if (_unitsRecord is not { } units)
        {
            return null;
        }

        var has = new bool[_k];
        for (var c = 0; c < _k; c++)
        {
            has[c] = c < units.FieldCount && units.Votes[c] != ValueVote.Null;
        }

        return has;
    }

    private static bool IsTrim(char c)
    {
        return ValueClassifier.TrimChars.Contains(c, StringComparison.Ordinal);
    }

    private void AddPreamble(RecordInfo record)
    {
        AddPreambleLine(_preamble, ClassifyPreambleLine(record.Raw ?? string.Empty, record.Cells), record.StartLine, record.EndLine, stable: true);
    }

    // The kind and key of a record outside the data run, or between two blocks; the value is kept only when it may be written.
    private LineClassification ClassifyPreambleLine(string raw, IReadOnlyList<string> cells)
    {
        var result = LineClassifier.Classify(raw, cells, _delimiterChar);
        return result.Key is { } key
            ? result with { Value = _gate.Allows(ValuesPolicy.RegionValues, key) ? result.Value ?? string.Empty : null }
            : result;
    }

    // One such record counts in the preamble `target`. Only the first block's preamble feeds the hints: its stable lines are
    // the ones that identify the file.
    private void AddPreambleLine(PreambleData target, LineClassification result, long startLine, long endLine, bool stable)
    {
        target.Kinds[(int)result.Kind]++;
        if (result.Key is { } key)
        {
            target.Keys.Add(key);
            target.Values.Add(result.Value);
        }

        // A stable line is one physical line; its text is read later, and only for the lines that may be written.
        if (stable && endLine == startLine)
        {
            if (result.Kind == LineKind.KeyValue && result.Key is { } stableKey)
            {
                _stableLines.Add((startLine, StableLineKind.PreambleKey, stableKey));
            }
            else if (_gate.Allows(ValuesPolicy.StableLineText, null) && result.Kind is LineKind.Comment or LineKind.Text)
            {
                _stableLines.Add((startLine, StableLineKind.PreambleText, null));
            }
        }
    }

    private void AddFooter(RecordInfo record)
    {
        if (_footerFirstLine < 0)
        {
            _footerFirstLine = record.StartLine;
            _footerFirstByte = record.StartByte;
        }

        var result = LineClassifier.Classify(record.Raw ?? string.Empty, record.Cells, _delimiterChar);
        _footerKinds[(int)result.Kind]++;
    }

    private void CountPadding(IReadOnlyList<string> cells)
    {
        foreach (var cell in cells)
        {
            if (ValueClassifier.Trim(cell).IsEmpty)
            {
                continue;
            }

            _nonEmptyCells++;
            if (IsTrim(cell[0]) || IsTrim(cell[^1]))
            {
                _paddedCells++;
            }
        }
    }

    private void MarkDataStart(long line, long startByte)
    {
        if (_later.Count > 0)
        {
            var block = _later[^1];
            if (block.DataFirstLine < 0)
            {
                block.DataFirstLine = line;
                block.DataFirstByte = startByte;
            }

            return;
        }

        if (_dataFirstLine < 0)
        {
            _dataFirstLine = line;
            _dataFirstByte = startByte;
        }
    }

    // Decides the decimal separator, the header, the annotation rows and the units row from the buffered sample, then
    // processes the sample's data records.
    private void Decide()
    {
        _decided = true;
        long commaLike = 0;
        long dotLike = 0;
        foreach (var record in _sample)
        {
            if (record.IsComment)
            {
                continue;
            }

            foreach (var cell in record.Cells)
            {
                var trimmed = ValueClassifier.Trim(cell);
                if (ValueClassifier.IsCommaDecimalLike(trimmed))
                {
                    commaLike++;
                }
                else if (ValueClassifier.IsDotDecimalLike(trimmed))
                {
                    dotLike++;
                }
            }
        }

        var useComma = _dialect.Kind != DelimiterKind.Comma && commaLike > dotLike;
        _decimal = useComma ? ',' : '.';

        // A block that starts inside the sample ends the first block's records there: the header, the annotation rows and
        // the units row of the first block come from the records before it. The rest is read again as the stream.
        var split = FindSplit();
        List<SampleRecord>? leftover = null;
        if (split >= 0)
        {
            leftover = _sample.GetRange(split, _sample.Count - split);
            _sample.RemoveRange(split, _sample.Count - split);
        }

        _decimalEvidence = new Evidence(
            "dialect",
            "decimalSeparator",
            useComma ? "," : ".",
            [
                new Candidate(",", useComma ? 1.0 : 0.0, Ordinal.Invariant(commaLike) + " comma-decimal cells" + (_dialect.Shape == DegenerateShape.SingleColumn ? "; one column, no delimiter" : _dialect.Kind == DelimiterKind.Comma ? "; the delimiter is a comma" : string.Empty)),
                new Candidate(".", useComma ? 0.0 : 1.0, Ordinal.Invariant(dotLike) + " dot-decimal cells"),
            ]);

        HeaderDetector.Classify(_sample, _k, _decimal);
        var decision = HeaderDetector.Detect(_sample, _k);
        _decision = decision;
        var h = decision.HeaderIndex;
        var start = 0;
        if (h >= 0)
        {
            _headerRecord = _sample[h];
            for (var i = 0; i < h; i++)
            {
                // A comment line is free text: it never becomes an annotation row, a label or a cell value.
                if (_sample[i].IsComment)
                {
                    AddComment(_sample[i].Line);
                }
                else
                {
                    _annotation.Add(BuildAnnotation(_sample[i]));
                }
            }

            start = h + 1;
            if (decision.UnitsIndex >= 0)
            {
                _unitsRecord = _sample[decision.UnitsIndex];
                start = decision.UnitsIndex + 1;
            }
        }

        // The decimal separator is known now, and it is always '.' or ',' (a FieldAccumulator rejects anything else).
        _accumulators = new FieldAccumulator[_k];
        for (var c = 0; c < _k; c++)
        {
            var name = h >= 0 ? ValueClassifier.Trim(_headerRecord!.Cells[c]).ToString() : null;
            var access = new FieldValueAccess(
                _gate.Allows(ValuesPolicy.FieldTop, name),
                _gate.Allows(ValuesPolicy.FieldText, name),
                _gate.Allows(ValuesPolicy.ConstantStatistics, name));
            _accumulators[c] = new FieldAccumulator(_caps, _decimal, access);
        }

        for (var i = start; i < _sample.Count; i++)
        {
            var record = _sample[i];
            MarkDataStart(record.Line, record.StartByte);
            ProcessData(record.Cells, record.FieldCount, record.Line, record.IsComment);
        }

        _sample.Clear();
        foreach (var record in leftover ?? [])
        {
            Stream(record);
        }
    }

    // The sample index where the first block ends, or -1: the gap of the first candidate that is a block (or that the
    // sample does not hold whole, which pass 2 then decides while streaming). A candidate inside the sample that is not a
    // block leaves the sample as it is. Sets the first candidate that is still to be decided.
    private int FindSplit()
    {
        var index = 0;
        for (var c = 0; c < _candidates.Count && _maxBlocks > 1; c++)
        {
            var candidate = _candidates[c];
            _nextCandidate = c;
            if (_sample.Count == 0 || candidate.GapFirstLine > _sample[^1].Line)
            {
                return -1;
            }

            while (_sample[index].Line < candidate.GapFirstLine)
            {
                index++;
            }

            var gap = index;
            var header = gap;
            while (header < _sample.Count && _sample[header].Line < candidate.HeaderLine)
            {
                header++;
            }

            var need = (int)Math.Min(BlockDetector.SampleRecords, candidate.Records);
            if (header >= _sample.Count || _sample.Count - header < need)
            {
                return gap;
            }

            var run = _sample.GetRange(header, need);
            HeaderDetector.Classify(run, _k, _decimal);
            // The first block must be a block too: at least BlockDetector.MinRunRecords records of the table's width, and a
            // header (the one that the records before this gap give: the first block is not decided yet). Without both the
            // records stay one table, as when there are no candidates.
            if (_sample.Take(gap).Count(r => !r.IsComment && r.Cells.Length == _k) >= BlockDetector.MinRunRecords)
            {
                string[]? names = null;
                var haveNames = false;

                // An exact repeat of the first block's header comes first.
                if (RepeatsARecordBefore(run, gap))
                {
                    names = TentativeNames(gap);
                    haveNames = true;
                    if (names is not null && FindRepeat(run, names) >= 0)
                    {
                        return gap;
                    }
                }

                var nonData = _sample.Skip(gap).Take(header - gap).Any(static r => r.GapClass is { } g && BlockDetector.IsNonDataLine(g.Kind, r.FieldCount));
                if (nonData && HeaderDetector.OpensWithHeader(run, _k, BlockDetector.MinContrast, out _) && (haveNames ? names : TentativeNames(gap)) is not null)
                {
                    return gap;
                }
            }

            _nextCandidate = c + 1;
        }

        return -1;
    }

    // Whether one of the first records of a run equals a record of the sample before `gap`: a cheap test that keeps the
    // header detection of the first block off the candidates that cannot repeat its header.
    private bool RepeatsARecordBefore(List<SampleRecord> run, int gap)
    {
        for (var j = 0; j < Math.Min(BlockDetector.RepeatWindow, run.Count); j++)
        {
            for (var p = 0; p < gap; p++)
            {
                if (SameCells(_sample[p], run[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool SameCells(SampleRecord a, SampleRecord b)
    {
        if (a.IsComment || b.IsComment || a.Cells.Length != _k || b.Cells.Length != _k)
        {
            return false;
        }

        for (var c = 0; c < _k; c++)
        {
            if (!ValueClassifier.Trim(a.Cells[c]).SequenceEqual(ValueClassifier.Trim(b.Cells[c])))
            {
                return false;
            }
        }

        return true;
    }

    // The header names of the first block, if its sample ended at `gap`.
    private string[]? TentativeNames(int gap)
    {
        var prefix = _sample.GetRange(0, gap);
        HeaderDetector.Classify(prefix, _k, _decimal);
        var decision = HeaderDetector.Detect(prefix, _k);
        return decision.HeaderIndex >= 0 ? Names(prefix[decision.HeaderIndex]) : null;
    }

    private static string[] Names(SampleRecord record)
    {
        return record.Cells.Select(static c => ValueClassifier.Trim(c).ToString()).ToArray();
    }

    // The sample index of the first of the first records of a run that equals the first block's header (trimmed, ordinal), or -1.
    private int FindRepeat(IReadOnlyList<SampleRecord> run, string[] names)
    {
        for (var j = 0; j < Math.Min(BlockDetector.RepeatWindow, run.Count); j++)
        {
            var record = run[j];
            if (record.IsComment || record.Cells.Length != names.Length)
            {
                continue;
            }

            var same = true;
            for (var c = 0; c < names.Length && same; c++)
            {
                same = ValueClassifier.Trim(record.Cells[c]).SequenceEqual(names[c]);
            }

            if (same)
            {
                return j;
            }
        }

        return -1;
    }

    // A candidate opens when its gap starts: from then on the records are held until the run's sample is complete. At the
    // cap on blocks nothing more is recognised and nothing more is profiled (see TruncateBlocks).
    private bool StartsPending(long line)
    {
        if (_nextCandidate >= _candidates.Count || line < _candidates[_nextCandidate].GapFirstLine)
        {
            return false;
        }

        if (_later.Count + 1 >= _maxBlocks)
        {
            TruncateBlocks();
            return false;
        }

        _pending = new PendingBlock(_nextCandidate, _candidates[_nextCandidate]);
        return true;
    }

    // `limits.regions` blocks are recognised. A further candidate may be another block, so the records from here on are
    // not profiled (as after a block with another header) and the issue blocksTruncated counts the candidates left.
    private void TruncateBlocks()
    {
        _profiling = false;
        _truncatedBlocks = _candidates.Count - _nextCandidate;
        for (var c = _nextCandidate; c < _candidates.Count && _truncatedLines.Count < MaxListedLines; c++)
        {
            _truncatedLines.Add(_candidates[c].HeaderLine);
        }

        _nextCandidate = _candidates.Count;
    }

    // A record of the stream, after the first block is decided: held while a candidate is open, otherwise data.
    private void Stream(SampleRecord record)
    {
        if (_pending is null && !StartsPending(record.Line))
        {
            MarkDataStart(record.Line, record.StartByte);
            ProcessData(record.Cells, record.FieldCount, record.Line, record.IsComment);
            return;
        }

        var pending = _pending!;
        if (record.Line < pending.Candidate.HeaderLine)
        {
            // A record of the gap is kept as what the preamble needs of it, not as cells.
            pending.Gap.Add(new GapRecord(record.Line, record.StartByte, record.EndLine, record.FieldCount, record.IsComment, record.GapClass ?? new LineClassification(LineKind.Text, null, null)));
            return;
        }

        pending.Sample.Add(record);
        if (pending.Sample.Count >= Math.Min(BlockDetector.SampleRecords, pending.Candidate.Records))
        {
            ResolvePending();
        }
    }

    // The open candidate is decided: a block when it repeats the first block's header or opens with a header line, else
    // its records are ragged rows and data rows of the open block, as if no candidate had been looked at.
    private void ResolvePending()
    {
        if (_pending is not { } pending)
        {
            return;
        }

        _pending = null;
        _nextCandidate++;
        HeaderDetector.Classify(pending.Sample, _k, _decimal);
        var header = -1;
        var unitsRow = false;

        // A table whose first block has no header has no later blocks: there is no header to repeat or to differ from.
        if (pending.Gap.Count > 0 && FirstNames() is { } names)
        {
            if (FindRepeat(pending.Sample, names) is var repeat and >= 0)
            {
                header = repeat;
                unitsRow = HeaderDetector.UnitsRowAfter(pending.Sample, repeat, _k);
            }
            else if (pending.Gap.Any(static g => BlockDetector.IsNonDataLine(g.Class.Kind, g.Fields)) && HeaderDetector.OpensWithHeader(pending.Sample, _k, BlockDetector.MinContrast, out unitsRow))
            {
                header = 0;
            }
        }

        if (header < 0)
        {
            foreach (var record in pending.Gap)
            {
                MarkDataStart(record.Line, record.StartByte);
                ProcessData([], record.Fields, record.Line, record.IsComment);
            }

            foreach (var record in pending.Sample)
            {
                MarkDataStart(record.Line, record.StartByte);
                ProcessData(record.Cells, record.FieldCount, record.Line, record.IsComment);
            }

            return;
        }

        StartBlock(pending, header, unitsRow);
    }

    // The header names of the first block, or null when it has no header.
    private string[]? FirstNames()
    {
        return _firstNames ??= _headerRecord is { } header ? Names(header) : null;
    }

    private void StartBlock(PendingBlock pending, int headerIndex, bool unitsRow)
    {
        var first = pending.Gap[0];
        var header = pending.Sample[headerIndex];
        var block = new BlockInfo(1 + _later.Count, first.Line, first.StartByte, header.Line, header.StartByte);
        foreach (var record in pending.Gap)
        {
            AddPreambleLine(block.Preamble, record.Class, record.Line, record.EndLine, stable: false);
        }

        block.Preamble.Kinds[(int)LineKind.Blank] += _gapBlanks[pending.Index];
        var start = headerIndex + 1;
        if (unitsRow)
        {
            block.UnitsLine = pending.Sample[start].Line;
            block.UnitsByte = pending.Sample[start].StartByte;
            start++;
        }

        // The fields are profiled across the blocks that have the first block's header, up to the first that does not.
        block.SameHeader = FirstNames() is { } names && names.AsSpan().SequenceEqual(Names(header));
        if (_profiling && block.SameHeader)
        {
            // Adjacent records of different blocks are not adjacent: no class transition at the boundary.
            _rowClasses.Break();
        }
        else
        {
            _profiling = false;
        }

        block.Profiled = _profiling;
        if (!block.Profiled)
        {
            _unprofiledBlocks++;
            if (_unprofiledLines.Count < MaxListedLines)
            {
                _unprofiledLines.Add(header.Line);
            }
        }

        // Records of the run before the header (a title or limits row that has the table's width) are the block's annotation.
        for (var i = 0; i < headerIndex; i++)
        {
            var record = pending.Sample[i];
            if (!record.IsComment)
            {
                block.Annotation.Add(BuildAnnotation(record));
            }
            else if (block.Profiled)
            {
                AddComment(record.Line);
            }
        }

        _later.Add(block);
        for (var i = start; i < pending.Sample.Count; i++)
        {
            var record = pending.Sample[i];
            MarkDataStart(record.Line, record.StartByte);
            ProcessData(record.Cells, record.FieldCount, record.Line, record.IsComment);
        }
    }

    private void AddComment(long line)
    {
        _commentsInData++;
        if (_commentLines.Count < MaxListedLines)
        {
            _commentLines.Add(line);
        }
    }

    private void ProcessData(IReadOnlyList<string> cells, int fieldCount, long line, bool comment)
    {
        if (!_profiling)
        {
            // A block after one with another header: its records are not profiled.
            return;
        }

        if (comment)
        {
            AddComment(line);
            return;
        }

        _rows++;
        _minFields = Math.Min(_minFields, fieldCount);
        _maxFields = Math.Max(_maxFields, fieldCount);
        if (fieldCount != _k)
        {
            _ragged++;
            if (_raggedLines.Count < MaxListedLines)
            {
                _raggedLines.Add(line);
            }

            return;
        }

        // The null decision is the field accumulator's; the row-class accumulator sees the whole record's mask.
        for (var c = 0; c < _k; c++)
        {
            _nullMask[c] = _accumulators[c].AddText(cells[c]) == ValueVote.Null;
        }

        _rowClasses.Add(_nullMask, line);
    }

    private AnnotationRowSource BuildAnnotation(SampleRecord record)
    {
        string? label = null;
        var first = 0;
        if (record.FieldCount > 0)
        {
            var text = ValueClassifier.Trim(record.Cells[0]);
            if (text.Length > 0 && record.Votes[0] is not (ValueVote.Int or ValueVote.Float or ValueVote.Null))
            {
                label = LineClassifier.NormalizeKey(text);
                first = 1;
            }
        }

        long blank = 0;
        var nullTokens = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var typeVotes = new long[TypeLattice.VoteKinds];
        var joined = ValueVote.Null;
        for (var c = first; c < record.FieldCount; c++)
        {
            if (ValueClassifier.Trim(record.Cells[c]).IsEmpty)
            {
                blank++;
                continue;
            }

            var vote = record.Votes[c];
            if (vote == ValueVote.Null)
            {
                var token = record.NullTokens[c] ?? string.Empty;
                nullTokens[token] = nullTokens.GetValueOrDefault(token) + 1;
                continue;
            }

            typeVotes[(int)vote]++;
            joined = TypeLattice.Join(joined, vote);
        }

        string?[]? values = null;
        if (_gate.Allows(ValuesPolicy.AnnotationRowValues, label))
        {
            values = record.Cells.Select(static c => (string?)ValueClassifier.Trim(c).ToString()).ToArray();
        }

        var row = new AnnotationRow(
            record.Line,
            label,
            blank,
            CountMap.FromOrdinal(nullTokens.Select(static e => new KeyValuePair<string, long>(e.Key, e.Value))),
            TypeLattice.VotesMap(typeVotes),
            TypeLattice.TypeName(joined),
            values);
        return new AnnotationRowSource(row, record.Line, record.StartByte);
    }

    private void AddHeaderIssues(List<Issue> issues, string[] names, long line)
    {
        long empty = 0;
        long repeated = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (name.Length == 0)
            {
                empty++;
            }
            else if (!seen.Add(Ordinal.AsciiFold(name)))
            {
                repeated++;
            }
        }

        if (repeated > 0)
        {
            issues.Add(new Issue("duplicateHeader", repeated, FirstLines: [line]));
        }

        if (empty > 0)
        {
            issues.Add(new Issue("emptyHeaderName", empty, FirstLines: [line]));
        }
    }

    private FieldProfile BuildField(int index, string? name, string? nameHex)
    {
        var profile = _accumulators[index].Build(index, name, nameHex, _rowClasses.NullsExcludingAllNullRows(index));
        if (_unitsRecord is { } units && index < units.FieldCount && units.Votes[index] != ValueVote.Null && _gate.Allows(ValuesPolicy.FieldUnit, name))
        {
            profile = profile with { Unit = ValueClassifier.Trim(units.Cells[index]).ToString() };
        }

        return profile;
    }

    // Header names with a replacement character carry the hex of their original bytes. The bytes are read from the
    // source again, once, and only when such a name exists.
    private static string?[] ReadNameHex(IProfileSource source, string?[] names, (long Start, long End)?[] spans)
    {
        var hex = new string?[names.Length];
        var wanted = new List<(int Index, long Start, long End)>();
        for (var c = 0; c < names.Length; c++)
        {
            if (names[c] is { } name && name.Contains('\uFFFD', StringComparison.Ordinal) && spans[c] is { } span)
            {
                wanted.Add((c, span.Start, span.End));
            }
        }

        if (wanted.Count == 0)
        {
            return hex;
        }

        wanted.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        using var stream = source.Open();
        var buffer = new byte[16 * 1024];
        long position = 0;
        foreach (var (index, start, end) in wanted)
        {
            var bytes = new byte[end - start];
            var filled = 0L;
            while (position < end)
            {
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, end - position));
                if (read <= 0)
                {
                    break;
                }

                for (var i = 0; i < read; i++)
                {
                    var at = position + i;
                    if (at >= start && at < end)
                    {
                        bytes[at - start] = buffer[i];
                        filled++;
                    }
                }

                position += read;
            }

            if (filled == bytes.Length)
            {
                hex[index] = Convert.ToHexStringLower(bytes);
            }
        }

        return hex;
    }

    private List<Region> BuildRegions(long totalLines, long totalBytes)
    {
        var first = new List<(string Kind, long Line, long Byte)>();
        if (_runFirstLine > 1)
        {
            first.Add(("preamble", 1, 0));
        }

        if (_annotation.Count > 0)
        {
            first.Add(("annotation", _annotation[0].Line, _annotation[0].StartByte));
        }

        if (_headerRecord is { } header)
        {
            first.Add(("header", header.Line, header.StartByte));
        }

        if (_unitsRecord is { } units)
        {
            first.Add(("units", units.Line, units.StartByte));
        }

        if (_dataFirstLine >= 0)
        {
            first.Add(("data", _dataFirstLine, _dataFirstByte));
        }

        if (_later.Count == 0)
        {
            // One block: the regions are flat, the footer among them.
            if (_footerFirstLine >= 0)
            {
                first.Add(("footer", _footerFirstLine, _footerFirstByte));
            }

            return Tile(first, 1, 0, totalLines, totalBytes, _preamble, _annotation);
        }

        // Several blocks: a block region per block holds the block's own regions, and the footer follows the last block.
        // The records between the last k-field record of a block and the header of the next are the next block's preamble.
        var regions = new List<Region>();
        var footerLine = _footerFirstLine >= 0 ? _footerFirstLine : totalLines + 1;
        var footerByte = _footerFirstLine >= 0 ? _footerFirstByte : totalBytes;
        for (var b = 0; b <= _later.Count; b++)
        {
            var startLine = 1L;
            var startByte = 0L;
            var starts = first;
            var preamble = _preamble;
            IReadOnlyList<AnnotationRowSource> annotation = _annotation;
            if (b > 0)
            {
                var block = _later[b - 1];
                startLine = block.FirstLine;
                startByte = block.FirstByte;
                preamble = block.Preamble;
                annotation = block.Annotation;
                starts = [("preamble", block.FirstLine, block.FirstByte)];
                if (block.Annotation.Count > 0)
                {
                    starts.Add(("annotation", block.Annotation[0].Line, block.Annotation[0].StartByte));
                }

                starts.Add(("header", block.HeaderLine, block.HeaderByte));
                if (block.UnitsLine >= 0)
                {
                    starts.Add(("units", block.UnitsLine, block.UnitsByte));
                }

                if (block.DataFirstLine >= 0)
                {
                    starts.Add(("data", block.DataFirstLine, block.DataFirstByte));
                }
            }

            var endLine = b < _later.Count ? _later[b].FirstLine - 1 : footerLine - 1;
            var endByte = b < _later.Count ? _later[b].FirstByte : footerByte;
            regions.Add(new Region("block", startLine, endLine, startByte, endByte, Index: b, Regions: Tile(starts, startLine, startByte, endLine, endByte, preamble, annotation)));
        }

        if (_footerFirstLine >= 0)
        {
            regions.Add(new Region("footer", _footerFirstLine, totalLines, _footerFirstByte, totalBytes, LineKinds: KindMap(_footerKinds)));
        }

        return regions;
    }

    // Regions tile a span of the file: each starts at the first record of its kind and ends on the line before the next one
    // starts; the first starts where the span starts (the file's first byte, a BOM included) and the last ends where it ends.
    private List<Region> Tile(List<(string Kind, long Line, long Byte)> starts, long firstLine, long firstByte, long endLine, long endByte, PreambleData preamble, IReadOnlyList<AnnotationRowSource> annotation)
    {
        var regions = new List<Region>();
        for (var i = 0; i < starts.Count; i++)
        {
            var (kind, line, startByte) = starts[i];
            var lastLine = i + 1 < starts.Count ? starts[i + 1].Line - 1 : endLine;
            var regionEndByte = i + 1 < starts.Count ? starts[i + 1].Byte : endByte;
            if (i == 0)
            {
                line = firstLine;
                startByte = firstByte;
            }

            regions.Add(kind switch
            {
                "preamble" => PreambleRegion(preamble, line, lastLine, startByte, regionEndByte),
                "annotation" => new Region(kind, line, lastLine, startByte, regionEndByte, Rows: annotation.Select(static a => a.Row).ToArray()),
                "footer" => new Region(kind, line, lastLine, startByte, regionEndByte, LineKinds: KindMap(_footerKinds)),
                _ => new Region(kind, line, lastLine, startByte, regionEndByte),
            });
        }

        return regions;
    }

    private Region PreambleRegion(PreambleData preamble, long line, long lastLine, long startByte, long endByte)
    {
        IReadOnlyList<string?>? values = null;
        if (_gate.Allows(ValuesPolicy.RegionValues, null) || preamble.Values.Any(static v => v is not null))
        {
            values = preamble.Values.ToArray();
        }

        return new Region(
            "preamble",
            line,
            lastLine,
            startByte,
            endByte,
            LineKinds: KindMap(preamble.Kinds),
            Keys: preamble.Keys.ToArray(),
            Values: values);
    }

    // stage dialect, topic blocks: one candidate per block in file order, the first 10 only, then a summary that counts them
    // all. Value-free numbers only.
    private Evidence BlockEvidence()
    {
        const int Listed = 10;
        var candidates = new List<Candidate>
        {
            new("0", 1.0, "header line " + Ordinal.Invariant(_headerRecord?.Line ?? 0) + "; the first block"),
        };
        long profiled = 1;
        foreach (var block in _later)
        {
            var line = "header line " + Ordinal.Invariant(block.HeaderLine);
            if (block.Profiled)
            {
                profiled++;
            }

            if (candidates.Count >= Listed)
            {
                continue;
            }

            if (block.Profiled)
            {
                candidates.Add(new Candidate(Ordinal.Invariant(block.Index), 1.0, line + "; the same header as block 0"));
            }
            else if (block.SameHeader)
            {
                candidates.Add(new Candidate(Ordinal.Invariant(block.Index), 0.0, line + "; the same header as block 0, but a block before it has another header: not profiled"));
            }
            else
            {
                candidates.Add(new Candidate(Ordinal.Invariant(block.Index), 0.0, line + "; another header than block 0: not profiled"));
            }
        }

        candidates.Add(new Candidate(
            HeaderDetector.SummaryValue,
            0.0,
            "candidates=" + Ordinal.Invariant(_candidates.Count) + "; blocks=" + Ordinal.Invariant(1 + _later.Count) + "; profiled=" + Ordinal.Invariant(profiled)));
        return new Evidence("dialect", "blocks", Ordinal.Invariant(1 + _later.Count), candidates);
    }

    private static CountMap KindMap(long[] counts)
    {
        var entries = new List<KeyValuePair<string, long>>();
        for (var k = 0; k < counts.Length; k++)
        {
            if (counts[k] > 0)
            {
                entries.Add(new(LineClassifier.Name((LineKind)k), counts[k]));
            }
        }

        return CountMap.FromOrdinal(entries);
    }

    private sealed record AnnotationRowSource(AnnotationRow Row, long Line, long StartByte);

    // The preamble of a block: line-kind counts, keys and the values that may be written, aligned with the keys.
    private sealed class PreambleData
    {
        public long[] Kinds { get; } = new long[5];

        public List<string> Keys { get; } = [];

        public List<string?> Values { get; } = [];
    }

    // A record of a gap before a block, as much of it as a preamble needs.
    private sealed record GapRecord(long Line, long StartByte, long EndLine, int Fields, bool IsComment, LineClassification Class);

    // A block after the first: where it starts (its preamble), its header, units row and data, and whether it is profiled.
    private sealed class BlockInfo(int index, long firstLine, long firstByte, long headerLine, long headerByte)
    {
        public int Index { get; } = index;

        public long FirstLine { get; } = firstLine;

        public long FirstByte { get; } = firstByte;

        public long HeaderLine { get; } = headerLine;

        public long HeaderByte { get; } = headerByte;

        public PreambleData Preamble { get; } = new();

        public List<AnnotationRowSource> Annotation { get; } = [];

        public long UnitsLine { get; set; } = -1;

        public long UnitsByte { get; set; }

        public long DataFirstLine { get; set; } = -1;

        public long DataFirstByte { get; set; }

        public bool SameHeader { get; set; }

        public bool Profiled { get; set; }
    }

    // A block candidate that is open: the records of its gap and of the first records of its run.
    private sealed class PendingBlock(int index, BlockCandidate candidate)
    {
        public int Index { get; } = index;

        public BlockCandidate Candidate { get; } = candidate;

        public List<GapRecord> Gap { get; } = [];

        public List<SampleRecord> Sample { get; } = [];
    }
}
