// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Pass 2 of the delimited profiler. It receives the records of the chosen dialect in file order and sorts them by
/// where they lie: before the data run (preamble), inside it (header sample, then data) or after it (footer).
/// The first <c>limits.headerSample</c> records of the run are buffered, scored as header candidates, and then
/// processed as annotation, header, units or data; later records stream straight into the field accumulators
/// (<see cref="FieldAccumulator"/>), which hold the type votes and every field statistic.
/// </summary>
internal sealed class DelimitedPass : IRecordSink, IPhysicalLineSink
{
    private const int MaxListedLines = 10;
    private const int CommentPrefix = 16;
    private const int VoteKinds = 8;

    private readonly CandidateResult _dialect;
    private readonly ProfileContext _context;
    private readonly int _k;
    private readonly long _runFirstLine;
    private readonly long _runLastLine;
    private readonly char _delimiterChar;
    private readonly HashSet<string> _allowed = new(StringComparer.Ordinal);

    // Preamble.
    private readonly long[] _preambleKinds = new long[5];
    private readonly List<string> _preambleKeys = [];
    private readonly List<string?> _preambleValues = [];

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

    // Data statistics.
    private FieldAccumulator[] _accumulators = [];
    private readonly FieldCaps _caps;
    private readonly ValueVote[] _kinds;
    private readonly long[] _nullsOutsideAllNull;
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
    public DelimitedPass(CandidateResult dialect, ProfileContext context)
    {
        _dialect = dialect;
        _context = context;
        _k = dialect.K;
        _runFirstLine = dialect.RunFirstLine;
        _runLastLine = dialect.RunLastLine;
        _delimiterChar = DelimiterCandidates.Char(dialect.Kind);
        _caps = MemoryBudget.Effective(context.Limits, _k);
        _kinds = new ValueVote[_k];
        _nullsOutsideAllNull = new long[_k];
        foreach (var name in context.Options.IncludeValuesFor)
        {
            _allowed.Add(AsciiFold(name));
        }
    }

    /// <summary>The per-field caps in effect: the configured caps after the global budget (<see cref="MemoryBudget"/>).</summary>
    internal FieldCaps Caps => _caps;

    /// <summary>The number of preamble values held in memory; only values that may be written are held.</summary>
    internal int PreambleValuesHeld => _preambleValues.Count(static v => v is not null);

    /// <summary>How much raw text to keep for a record that starts on a line: all of it outside the run, a prefix inside it.</summary>
    /// <param name="line">The 1-based line.</param>
    /// <returns>The number of characters.</returns>
    public int RawCap(long line)
    {
        return line >= _runFirstLine && line <= _runLastLine ? CommentPrefix : int.MaxValue;
    }

    /// <inheritdoc />
    public void Blank(long line)
    {
        if (line < _runFirstLine)
        {
            _preambleKinds[(int)LineKind.Blank]++;
        }
        else if (_footerFirstLine >= 0)
        {
            _footerKinds[(int)LineKind.Blank]++;
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

            AddFooter(record);
            return;
        }

        CountPadding(record.Cells);
        var comment = LineClassifier.IsComment(record.Raw ?? string.Empty);
        if (!_decided)
        {
            _sample.Add(new SampleRecord
            {
                Line = record.StartLine,
                StartByte = record.StartByte,
                Cells = record.Cells.ToArray(),
                Spans = record.Spans.ToArray(),
                IsComment = comment,
            });
            if (_sample.Count >= _context.Limits.HeaderSample)
            {
                Decide();
            }

            return;
        }

        MarkDataStart(record.StartLine, record.StartByte);
        ProcessData(record.Cells, record.FieldCount, record.StartLine, comment);
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
        var fields = new List<FieldProfile>();
        var orderConflicts = new List<(long Index, DateOrderCounts Counts)>();
        for (var c = 0; c < _k; c++)
        {
            fields.Add(BuildField(c, names[c], hex[c]));
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

        var regions = BuildRegions(structure.Counter.Lines, sniff.Length);
        var padded = _nonEmptyCells > 0 ? _paddedCells / (double)_nonEmptyCells : 0.0;
        var constant = _runLines > 0 && _constantLineLength;
        var dialect = new DelimitedDialect(
            DelimiterCandidates.Text(_dialect.Kind),
            "\"",
            hasHeader,
            _unitsRecord is not null,
            _decimal == ',' ? "," : ".",
            new PaddingInfo(_paddedCells > 0, padded, constant, constant ? _firstLineLength : null),
            Blocks: 1);
        var table = new TableSection(
            _rows,
            _rows == 0 ? new MinMax(_k, _k) : new MinMax(_minFields, _maxFields),
            fields);
        var evidence = new List<Evidence> { structure.Selection.Evidence };
        if (_decimalEvidence is not null)
        {
            evidence.Add(_decimalEvidence);
        }

        if (_decision is not null)
        {
            evidence.Add(_decision.Evidence);
        }

        if (FieldAccumulator.ConflictEvidence(orderConflicts) is { } conflictEvidence)
        {
            evidence.Add(conflictEvidence);
        }

        return new FormatProfile(
            dialect,
            new LayoutSection(regions, MaxLineLength: counter.MaxLineLength),
            new LineEndings(counter.Crlf, counter.Lf, counter.Cr),
            issues,
            evidence,
            Table: table,
            EffectiveLimits: MemoryBudget.Record(_context.Limits, _caps));
    }

    private static string AsciiFold(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= 'A' and <= 'Z')
            {
                chars[i] = (char)(chars[i] + ('a' - 'A'));
            }
        }

        return new string(chars);
    }

    private static bool IsTrim(char c)
    {
        return ValueClassifier.TrimChars.Contains(c, StringComparison.Ordinal);
    }

    private bool Allows(string? name)
    {
        return _context.Options.IncludeValues || (name is not null && _allowed.Contains(AsciiFold(name)));
    }

    private void AddPreamble(RecordInfo record)
    {
        var result = LineClassifier.Classify(record.Raw ?? string.Empty, record.Cells, _delimiterChar);
        _preambleKinds[(int)result.Kind]++;
        if (result.Key is { } key)
        {
            _preambleKeys.Add(key);
            // A value is held only when it may be written; otherwise it is dropped here.
            _preambleValues.Add(Allows(key) ? result.Value ?? string.Empty : null);
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
        _decimalEvidence = new Evidence(
            "dialect",
            "decimalSeparator",
            useComma ? "," : ".",
            [
                new Candidate(",", useComma ? 1.0 : 0.0, Ordinal.Invariant(commaLike) + " comma-decimal cells" + (_dialect.Kind == DelimiterKind.Comma ? "; the delimiter is a comma" : string.Empty)),
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
            _accumulators[c] = new FieldAccumulator(_caps, _decimal);
        }

        for (var i = start; i < _sample.Count; i++)
        {
            var record = _sample[i];
            MarkDataStart(record.Line, record.StartByte);
            ProcessData(record.Cells, record.FieldCount, record.Line, record.IsComment);
        }

        _sample.Clear();
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

        var allNull = true;
        for (var c = 0; c < _k; c++)
        {
            var vote = _accumulators[c].AddText(cells[c]);
            _kinds[c] = vote;
            if (vote != ValueVote.Null)
            {
                allNull = false;
            }
        }

        if (!allNull)
        {
            for (var c = 0; c < _k; c++)
            {
                if (_kinds[c] == ValueVote.Null)
                {
                    _nullsOutsideAllNull[c]++;
                }
            }
        }
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
        var typeVotes = new long[VoteKinds];
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
        if (Allows(label))
        {
            values = record.Cells.Select(static c => (string?)ValueClassifier.Trim(c).ToString()).ToArray();
        }

        var row = new AnnotationRow(
            record.Line,
            label,
            blank,
            CountMap.FromOrdinal(nullTokens.Select(static e => new KeyValuePair<string, long>(e.Key, e.Value))),
            VotesMap(typeVotes, 0),
            TypeLattice.TypeName(joined),
            values);
        return new AnnotationRowSource(row, record.Line, record.StartByte);
    }

    private static CountMap VotesMap(long[] counts, int offset)
    {
        var entries = new List<KeyValuePair<string, long>>();
        for (var v = (int)ValueVote.Bool; v <= (int)ValueVote.String; v++)
        {
            if (counts[offset + v] > 0)
            {
                entries.Add(new(TypeLattice.TypeName((ValueVote)v), counts[offset + v]));
            }
        }

        return CountMap.FromOrdinal(entries);
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
            else if (!seen.Add(AsciiFold(name)))
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
        var profile = _accumulators[index].Build(index, name, nameHex, _nullsOutsideAllNull[index], Allows(name));
        if (_unitsRecord is { } units && index < units.FieldCount && units.Votes[index] != ValueVote.Null && Allows(name))
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
        var starts = new List<(string Kind, long Line, long Byte)>();
        if (_runFirstLine > 1)
        {
            starts.Add(("preamble", 1, 0));
        }

        if (_annotation.Count > 0)
        {
            starts.Add(("annotation", _annotation[0].Line, _annotation[0].StartByte));
        }

        if (_headerRecord is { } header)
        {
            starts.Add(("header", header.Line, header.StartByte));
        }

        if (_unitsRecord is { } units)
        {
            starts.Add(("units", units.Line, units.StartByte));
        }

        if (_dataFirstLine >= 0)
        {
            starts.Add(("data", _dataFirstLine, _dataFirstByte));
        }

        if (_footerFirstLine >= 0)
        {
            starts.Add(("footer", _footerFirstLine, _footerFirstByte));
        }

        var regions = new List<Region>();
        for (var i = 0; i < starts.Count; i++)
        {
            var (kind, line, startByte) = starts[i];
            var lastLine = i + 1 < starts.Count ? starts[i + 1].Line - 1 : totalLines;
            var endByte = i + 1 < starts.Count ? starts[i + 1].Byte : totalBytes;
            if (i == 0)
            {
                // The first region covers the file from its first byte, a BOM included.
                line = 1;
                startByte = 0;
            }

            regions.Add(kind switch
            {
                "preamble" => PreambleRegion(line, lastLine, startByte, endByte),
                "annotation" => new Region(kind, line, lastLine, startByte, endByte, Rows: _annotation.Select(static a => a.Row).ToArray()),
                "footer" => new Region(kind, line, lastLine, startByte, endByte, LineKinds: KindMap(_footerKinds)),
                _ => new Region(kind, line, lastLine, startByte, endByte),
            });
        }

        return regions;
    }

    private Region PreambleRegion(long line, long lastLine, long startByte, long endByte)
    {
        IReadOnlyList<string?>? values = null;
        if (_context.Options.IncludeValues || _preambleValues.Any(static v => v is not null))
        {
            values = _preambleValues.ToArray();
        }

        return new Region(
            "preamble",
            line,
            lastLine,
            startByte,
            endByte,
            LineKinds: KindMap(_preambleKinds),
            Keys: _preambleKeys.ToArray(),
            Values: values);
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
}
