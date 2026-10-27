// SPDX-License-Identifier: Apache-2.0
using System.Text;

namespace squalor.squalify;

/// <summary>One record that a <see cref="RecordTokenizer"/> has finished. The tokenizer reuses the instance: a sink copies what it keeps.</summary>
internal sealed class RecordInfo
{
    /// <summary>The number of fields.</summary>
    public int FieldCount { get; set; }

    /// <summary>The 1-based physical line where the record starts.</summary>
    public long StartLine { get; set; }

    /// <summary>The 1-based physical line where the record ends (later than <see cref="StartLine"/> when a quoted field holds a line break).</summary>
    public long EndLine { get; set; }

    /// <summary>The byte offset of the first character of the record.</summary>
    public long StartByte { get; set; }

    /// <summary>The byte offset where the next line starts (the record's line terminator is inside the range).</summary>
    public long EndByte { get; set; }

    /// <summary>The number of tokens (fields) counted for the clean share.</summary>
    public long Tokens { get; set; }

    /// <summary>The tokens that are clean: they do not contain another candidate's delimiter character (see the remarks of <see cref="RecordTokenizer"/>).</summary>
    public long CleanTokens { get; set; }

    /// <summary>The raw field text (quotes removed, <c>""</c> unescaped), when the tokenizer collects cells.</summary>
    public List<string> Cells { get; } = [];

    /// <summary>For each field, true when it was written in quotes, when the tokenizer collects cells.</summary>
    public List<bool> Quoted { get; } = [];

    /// <summary>For each field, the byte offset of its first character and the offset after its last, when the tokenizer captures spans.</summary>
    public List<long> Spans { get; } = [];

    /// <summary>The record's text up to the raw cap, with a line break inside a quoted field written as LF; null when the cap is 0.</summary>
    public string? Raw { get; set; }

    /// <summary>True when the record is a comment line (see <see cref="LineClassifier.IsComment"/>); known in pass 1, which keeps no raw text.</summary>
    public bool IsComment { get; set; }
}

/// <summary>Receives the records and the blank lines that a <see cref="RecordTokenizer"/> finds.</summary>
internal interface IRecordSink
{
    /// <summary>A record ended.</summary>
    /// <param name="record">The record; valid only during the call.</param>
    void Record(RecordInfo record);

    /// <summary>A blank line (no record) ended.</summary>
    /// <param name="line">The 1-based line.</param>
    void Blank(long line);
}

/// <summary>Why a candidate was disqualified.</summary>
internal enum Disqualification
{
    /// <summary>The candidate is still in the race.</summary>
    None,

    /// <summary>A quoted field stayed open for more than the runaway-quote limit.</summary>
    RunawayQuote,

    /// <summary>The field counts changed more often than the run limit.</summary>
    TooIrregular,
}

/// <summary>
/// A quote-aware record tokenizer for one delimiter candidate. It is a character-level state machine, so a file is
/// tokenized in one streaming pass with bounded memory, and the same class serves both passes: pass 1 runs one per
/// candidate and only counts, pass 2 runs the chosen one and collects the cells.
/// </summary>
/// <remarks>
/// Quoting is RFC 4180 with <c>"</c>. A quote opens a quoted field only at the start of a field. Inside one, <c>""</c>
/// is an escape and a line break is part of the field. A <c>"</c> that is followed by neither another <c>"</c> nor the
/// delimiter nor the end of the line is literal text (owner decision 2026-10-27: an ATE export's writer does not escape
/// quotes inside a quoted field), and is counted in <see cref="LiteralQuotes"/>. A closing quote followed by spaces or
/// tabs and then the delimiter or the end of the line still closes the field (padded quoted cells; owner decision
/// 2026-10-27). A line that holds only blank
/// characters is a blank line, not a record. A token is clean when it is quoted or contains none of the characters
/// <c>,</c>, <c>;</c> and <c>|</c> other than the candidate's own, and no tab inside a trimmed token of the comma,
/// semicolon or pipe candidate; spaces never count, and a comma inside a comma-decimal number such as <c>1,5</c> is
/// exempt. The whitespace-run candidate treats any run of the characters in
/// <see cref="ValueClassifier.TrimChars"/> as one delimiter and trims leading and trailing white space.
/// </remarks>
internal sealed class RecordTokenizer
{
    private readonly char _delimiter;
    private readonly bool _whitespace;
    private readonly bool _tabCandidate;
    private readonly IRecordSink _sink;
    private readonly bool _collect;
    private readonly bool _capture;
    private readonly Func<long, int>? _rawCap;
    private readonly long _runaway;
    private readonly RecordInfo _info = new();
    private readonly StringBuilder _cell = new();
    private readonly StringBuilder _raw = new();
    private readonly StringBuilder _head = new();
    private readonly StringBuilder _pending = new();

    private State _state;
    private bool _open;
    private bool _blank;
    private long _line = 1;
    private long _recordStartLine;
    private long _recordStartByte;
    private int _fieldCount;
    private long _tokens;
    private long _clean;
    private bool _dirtyOther;
    private bool _dirtyComma;
    private bool _sawNonTrim;
    private bool _tabPending;
    private bool _headOverflow;
    private bool _fieldQuoted;
    private long _quotedLength;
    private long _quoteLine;
    private int _rawLimit;
    private long _spanStart = -1;
    private long _spanEnd;
    private bool _awaitingEnd;
    private long _lastOffset = -1;

    // The comment probe: the first character that is not trim-set white space, and the one right after it ('//' needs both).
    private int _probe;
    private bool _probeComment;

    private enum State
    {
        FieldStart,
        Unquoted,
        Quoted,
        QuoteSeen,
        QuoteSpace,
    }

    // A token longer than this cannot be a decimal-comma number, so only its start is kept for that test.
    private const int HeadCap = 80;

    /// <summary>Creates a tokenizer.</summary>
    /// <param name="kind">The delimiter candidate.</param>
    /// <param name="sink">Receives the records and blank lines.</param>
    /// <param name="runawayQuote">The runaway-quote limit in characters.</param>
    /// <param name="collectCells">True to collect the field texts (pass 2).</param>
    /// <param name="captureSpans">True to record each field's byte span (pass 2); needs <paramref name="collectCells"/>.</param>
    /// <param name="rawCap">Gives, for a record that starts on a line, how many characters of its raw text to keep; null keeps none.</param>
    public RecordTokenizer(DelimiterKind kind, IRecordSink sink, long runawayQuote, bool collectCells = false, bool captureSpans = false, Func<long, int>? rawCap = null)
    {
        Kind = kind;
        _delimiter = DelimiterCandidates.Char(kind);
        _whitespace = kind == DelimiterKind.Whitespace;
        _tabCandidate = kind == DelimiterKind.Tab;
        _sink = sink;
        _runaway = runawayQuote;
        _collect = collectCells;
        _capture = captureSpans;
        _rawCap = rawCap;
    }

    /// <summary>The delimiter candidate.</summary>
    public DelimiterKind Kind { get; }

    /// <summary>Why the candidate was disqualified, or <see cref="Disqualification.None"/>.</summary>
    public Disqualification Disqualified { get; private set; }

    /// <summary>The line where the disqualification arose (where the runaway quote opened, or where the run limit was passed).</summary>
    public long DisqualifiedLine { get; private set; }

    /// <summary>The number of literal quotes seen inside quoted fields.</summary>
    public long LiteralQuotes { get; private set; }

    /// <summary>The number of quoted fields opened.</summary>
    public long QuotedFields { get; private set; }

    /// <summary>Takes the candidate out of the race.</summary>
    /// <param name="reason">The reason.</param>
    /// <param name="line">The line where it arose.</param>
    public void Disqualify(Disqualification reason, long line)
    {
        Disqualified = reason;
        DisqualifiedLine = line;
        _open = false;
        _cell.Clear();
        _raw.Clear();
        _info.Cells.Clear();
        _info.Quoted.Clear();
        _info.Spans.Clear();
    }

    /// <summary>Feeds one character of a line (never a CR or LF).</summary>
    /// <param name="c">The character.</param>
    /// <param name="offset">The byte offset where it starts.</param>
    public void Char(char c, long offset)
    {
        if (Disqualified != Disqualification.None)
        {
            return;
        }

        if (_capture)
        {
            if (_awaitingEnd && offset != _lastOffset)
            {
                _spanEnd = offset;
                _awaitingEnd = false;
            }

            _lastOffset = offset;
        }

        if (!_open)
        {
            StartRecord(offset);
        }

        if (!IsBlankChar(c))
        {
            _blank = false;
        }

        if (_raw.Length < _rawLimit)
        {
            _raw.Append(c);
        }

        if (_probe < 2)
        {
            Probe(c);
        }

        switch (_state)
        {
            case State.FieldStart:
                if (c == '"')
                {
                    OpenQuote(offset);
                }
                else if (IsDelimiter(c))
                {
                    if (!_whitespace)
                    {
                        EndField(offset);
                    }
                }
                else
                {
                    _state = State.Unquoted;
                    AppendUnquoted(c, offset);
                }

                break;
            case State.Unquoted:
                if (IsDelimiter(c))
                {
                    EndField(offset);
                }
                else
                {
                    AppendUnquoted(c, offset);
                }

                break;
            case State.Quoted:
                if (c == '"')
                {
                    _state = State.QuoteSeen;
                    _awaitingEnd = true;
                }
                else
                {
                    AppendQuoted(c);
                }

                break;
            case State.QuoteSeen:
                if (c == '"')
                {
                    _state = State.Quoted;
                    AppendQuoted('"');
                }
                else if (IsDelimiter(c))
                {
                    EndField(offset);
                }
                else if (c is ' ' or '\t')
                {
                    // Maybe a padded closing quote: hold the white space until the next character shows which.
                    _state = State.QuoteSpace;
                    _pending.Clear();
                    AddPending(c);
                }
                else
                {
                    LiteralQuotes++;
                    _state = State.Quoted;
                    AppendQuoted('"');
                    AppendQuoted(c);
                }

                break;
            default:
                if (IsDelimiter(c))
                {
                    EndField(offset);
                }
                else if (c is ' ' or '\t')
                {
                    AddPending(c);
                }
                else
                {
                    // Not a padded closing quote after all: the quote and the held white space are literal text.
                    LiteralQuotes++;
                    _state = State.Quoted;
                    AppendQuoted('"');
                    if (_collect)
                    {
                        _cell.Append(_pending);
                    }

                    if (c == '"')
                    {
                        _state = State.QuoteSeen;
                        _awaitingEnd = true;
                    }
                    else
                    {
                        AppendQuoted(c);
                    }
                }

                break;
        }
    }

    /// <summary>Ends a physical line (one CR, LF or CRLF).</summary>
    /// <param name="nextStart">The byte offset where the next line starts.</param>
    /// <param name="contentEnd">The byte offset of the line terminator.</param>
    public void Break(long nextStart, long contentEnd)
    {
        if (Disqualified != Disqualification.None)
        {
            return;
        }

        if (!_open)
        {
            _sink.Blank(_line);
            _line++;
            return;
        }

        if (_state == State.Quoted)
        {
            // A line break inside a quoted field belongs to the field.
            if (_raw.Length < _rawLimit)
            {
                _raw.Append('\n');
            }

            AppendQuoted('\n');
            _line++;
            return;
        }

        if (_blank)
        {
            _open = false;
            _sink.Blank(_line);
            _line++;
            return;
        }

        FinishRecord(nextStart, contentEnd);
        _line++;
    }

    /// <summary>Ends the input: a record that is still open (an unterminated quote at the end of the file) ends here.</summary>
    /// <param name="endByte">The file length.</param>
    public void Flush(long endByte)
    {
        if (Disqualified != Disqualification.None || !_open)
        {
            return;
        }

        if (_blank)
        {
            _open = false;
            _sink.Blank(_line);
            return;
        }

        FinishRecord(endByte, endByte);
    }

    private static bool IsTrim(char c)
    {
        return ValueClassifier.TrimChars.Contains(c, StringComparison.Ordinal);
    }

    private bool IsDelimiter(char c)
    {
        return _whitespace ? IsTrim(c) : c == _delimiter;
    }

    // A character that does not make a line a record: spaces and tabs (a tab is a delimiter for the tab candidate), and
    // every trimmed white-space character for the whitespace-run candidate.
    private bool IsBlankChar(char c)
    {
        if (c == ' ' || (c == '\t' && !_tabCandidate))
        {
            return true;
        }

        return _whitespace && IsTrim(c);
    }

    private void AddPending(char c)
    {
        _pending.Append(c);
        if (++_quotedLength > _runaway)
        {
            Disqualify(Disqualification.RunawayQuote, _quoteLine);
        }
    }

    // Decides whether the record is a comment line from its first one or two characters, as LineClassifier.IsComment does
    // on the whole text: trimmed, it starts with '#', '!' or '//'.
    private void Probe(char c)
    {
        if (_probe == 0)
        {
            if (IsTrim(c))
            {
                return;
            }

            _probeComment = c is '#' or '!';
            _probe = _probeComment || c != '/' ? 2 : 1;
            return;
        }

        _probeComment = c == '/';
        _probe = 2;
    }

    private void StartRecord(long offset)
    {
        _probe = 0;
        _probeComment = false;
        _open = true;
        _recordStartLine = _line;
        _recordStartByte = offset;
        _fieldCount = 0;
        _tokens = 0;
        _clean = 0;
        _blank = true;
        _state = State.FieldStart;
        _info.Cells.Clear();
        _info.Quoted.Clear();
        _info.Spans.Clear();
        _raw.Clear();
        _rawLimit = _rawCap?.Invoke(_line) ?? 0;
        BeginField();
    }

    private void BeginField()
    {
        _cell.Clear();
        _dirtyOther = false;
        _dirtyComma = false;
        _sawNonTrim = false;
        _tabPending = false;
        _headOverflow = false;
        _head.Clear();
        _fieldQuoted = false;
        _spanStart = -1;
        _awaitingEnd = false;
    }

    private void OpenQuote(long offset)
    {
        _state = State.Quoted;
        _fieldQuoted = true;
        _quotedLength = 0;
        _quoteLine = _line;
        QuotedFields++;
        if (_capture)
        {
            _spanStart = offset;
            _awaitingEnd = true;
        }
    }

    private void AppendUnquoted(char c, long offset)
    {
        if (_collect)
        {
            _cell.Append(c);
        }

        // Another candidate's delimiter character inside the token makes it unclean (see IsClean). So does a tab
        // inside a trimmed comma, semicolon or pipe token (a TSV cell is not a token of those candidates); a tab at
        // either end is trimmed away, and a space never counts.
        if (!IsTrim(c))
        {
            if (_tabPending)
            {
                _dirtyOther = true;
            }

            _sawNonTrim = true;
        }
        else if (c == '\t' && _sawNonTrim && !_whitespace && !_tabCandidate)
        {
            _tabPending = true;
        }

        if (c == ',')
        {
            if (_delimiter != ',')
            {
                _dirtyComma = true;
            }
        }
        else if ((c == ';' || c == '|') && c != _delimiter)
        {
            _dirtyOther = true;
        }

        if (_head.Length < HeadCap)
        {
            _head.Append(c);
        }
        else
        {
            _headOverflow = true;
        }

        if (!IsTrim(c) && _capture)
        {
            if (_spanStart < 0)
            {
                _spanStart = offset;
            }

            _awaitingEnd = true;
        }
    }

    private void AppendQuoted(char c)
    {
        if (_collect)
        {
            _cell.Append(c);
        }

        if (++_quotedLength > _runaway)
        {
            Disqualify(Disqualification.RunawayQuote, _quoteLine);
            return;
        }

        _awaitingEnd = true;
    }

    private void EndField(long at)
    {
        _fieldCount++;
        _tokens++;
        if (IsClean())
        {
            _clean++;
        }

        if (_collect)
        {
            _info.Cells.Add(_cell.ToString());
            _info.Quoted.Add(_fieldQuoted);
        }

        if (_capture)
        {
            if (_spanStart >= 0)
            {
                _info.Spans.Add(_spanStart);
                _info.Spans.Add(_awaitingEnd ? at : _spanEnd);
            }
            else
            {
                _info.Spans.Add(at);
                _info.Spans.Add(at);
            }
        }

        BeginField();
        _state = State.FieldStart;
    }

    // A quoted token is clean. An unquoted token is unclean when it contains another candidate's delimiter character
    // anywhere, except that a comma inside a comma-decimal number (1,5) is not counted: a semicolon file with decimal
    // commas must not lose its own tokens to the comma candidate's character. The shape is the one that
    // ValueClassifier flags as commaDecimalLike.
    private bool IsClean()
    {
        if (_fieldQuoted || !(_dirtyOther || _dirtyComma))
        {
            return true;
        }

        if (_dirtyOther || _headOverflow)
        {
            return false;
        }

        return ValueClassifier.IsCommaDecimalLike(ValueClassifier.Trim(_head.ToString()));
    }

    private void FinishRecord(long nextStart, long contentEnd)
    {
        if (_whitespace)
        {
            // Trailing white space is trimmed: only an open token ends here.
            if (_state != State.FieldStart)
            {
                EndField(contentEnd);
            }
        }
        else
        {
            EndField(contentEnd);
        }

        _info.FieldCount = _fieldCount;
        _info.StartLine = _recordStartLine;
        _info.EndLine = _line;
        _info.StartByte = _recordStartByte;
        _info.EndByte = nextStart;
        _info.Tokens = _tokens;
        _info.CleanTokens = _clean;
        _info.Raw = _rawLimit > 0 ? _raw.ToString() : null;
        _info.IsComment = _probeComment;
        _open = false;
        _sink.Record(_info);
    }
}
