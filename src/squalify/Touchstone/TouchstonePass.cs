// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The profile pass of a Touchstone file: one streaming read that finds the option line and the keywords, reads the network data and the
/// noise data as flat streams of numbers (a block is 1 + 2N^2 values, or 1 + N(N+1) for a lower or upper matrix, whatever the line
/// breaks), feeds every value of a complete block to the field accumulators, and tracks the regions. A block that the file ends in the
/// middle of is not counted in any field (the issue <c>incompleteBlock</c>).
/// </summary>
internal sealed class TouchstonePass
{
    private const int MaxListedLines = 10;

    private readonly string _fileName;
    private readonly SniffResult _sniff;
    private readonly ProfileContext _context;
    private readonly ValuesGate _gate;
    private readonly bool _layoutOnly;
    private readonly BlockBuffer _buffer = new();
    private readonly TouchstoneRegions _regions;
    private readonly List<string> _keywords = [];
    private readonly Dictionary<long, long> _valuesPerLine = [];
    private readonly List<long> _incompleteLines = [];
    private readonly List<long> _decreaseLines = [];
    private long _decreases;
    private long _incompleteBlocks;

    // The header.
    private OptionLine? _option;
    private long _optionLine;
    private string? _versionText;
    private bool _versionSeen;
    private long? _keywordPorts;
    private long _keywordPortsLine;
    private string? _orderKeyword;
    private long _orderKeywordLine;
    private string? _matrixKeyword;
    private bool _networkSeen;
    private bool _ended;
    private long _noiseKeywordLine;

    // The structure, known when the first data line arrives.
    private long _ports;
    private string _portsSource = "none";
    private int _blockSize;
    private string[] _names = [];
    private FieldAccumulator[] _accumulators = [];
    private FieldAccumulator[] _noiseAccumulators = [];
    private FieldCaps _caps;
    private bool _structured;
    private long _extensionPorts;
    private bool _extension;
    private int _inferredFrom;

    // The stream.
    private bool _inNoise;
    private string? _noiseSource;
    private long _noiseStartLine;
    private int _position;
    private long _blockStartLine;
    private long _rows;
    private long _noiseRows;
    private bool _haveFrequency;
    private double _lastFrequency;
    private long _lineNumber;

    /// <summary>Creates the pass.</summary>
    /// <param name="fileName">The bare file name.</param>
    /// <param name="sniff">The byte-pass result.</param>
    /// <param name="context">The options and limits.</param>
    public TouchstonePass(string fileName, SniffResult sniff, ProfileContext context)
    {
        _fileName = fileName;
        _sniff = sniff;
        _context = context;
        _gate = context.Gate;
        _layoutOnly = context.Options.LayoutOnly;
        _regions = new TouchstoneRegions(!_layoutOnly && _gate.Allows(ValuesPolicy.StableLineText, null));
        var extensionPorts = TouchstoneSyntax.PortsFromExtension(fileName, out _);
        _extension = extensionPorts is not null;
        _extensionPorts = extensionPorts ?? 0;
    }

    /// <summary>Receives one physical line.</summary>
    /// <param name="line">The line.</param>
    /// <returns>True: the pass reads the whole file.</returns>
    public bool OnLine(TextLine line)
    {
        _lineNumber = line.Number;
        if (line.Truncated)
        {
            _regions.Other(line, LineClass.Text);
            return true;
        }

        if (ValueClassifier.Trim(line.Text).IsEmpty)
        {
            _regions.Other(line, LineClass.Blank);
            return true;
        }

        if (TouchstoneSyntax.IsComment(line.Text))
        {
            _regions.Other(line, LineClass.Comment);
            return true;
        }

        var content = TouchstoneSyntax.Content(line.Text);
        if (content[0] == '#')
        {
            if (_option is null && !_structured && TouchstoneSyntax.ParseOption(content) is { } option)
            {
                _option = option;
                _optionLine = line.Number;
            }

            _regions.Other(line, LineClass.Text);
            return true;
        }

        if (!_ended && TouchstoneSyntax.ParseKeyword(content, out var arguments) is { } keyword)
        {
            Keyword(line, keyword, arguments);
            return true;
        }

        var dataAllowed = !_ended && (!_versionSeen || _networkSeen);
        if (dataAllowed && Tokens(content) is { } count)
        {
            Data(line, content, count);
            return true;
        }

        _regions.Other(line, LineClass.Text);
        return true;
    }

    /// <summary>Ends the pass and builds the profile.</summary>
    /// <param name="counter">The line counts of the whole file.</param>
    /// <returns>The parts of the descriptor.</returns>
    public FormatProfile Build(LineCounter counter)
    {
        // A block that the data ends in the middle of.
        if (_position > 0)
        {
            _incompleteBlocks++;
            if (_incompleteLines.Count < MaxListedLines)
            {
                _incompleteLines.Add(_blockStartLine);
            }
        }

        var issues = new List<Issue>();
        var evidence = new List<Evidence>();
        var ports = _ports;
        var noise = _noiseSource is not null;

        if (_keywordPorts is { } kw && _extension && kw != _extensionPorts)
        {
            issues.Add(new Issue(IssueCodes.PortMismatch, 1, FirstLines: [_keywordPortsLine]));
        }

        if (_incompleteBlocks > 0)
        {
            issues.Add(new Issue(IssueCodes.IncompleteBlock, _incompleteBlocks, FirstLines: _incompleteLines.ToArray()));
        }

        if (_decreases > 0)
        {
            issues.Add(new Issue(IssueCodes.NonMonotonicFrequency, _decreases, FirstLines: _decreaseLines.ToArray()));
        }

        if (counter.MixedLineEndings() is { } mixed)
        {
            issues.Add(mixed);
        }

        var unit = _option?.Unit ?? "GHz";
        var parameter = _option?.Parameter ?? "S";
        var format = _option?.Format ?? "MA";
        var reference = _option?.Reference ?? 50.0;
        var twoPortOrder = ports == 2 ? _orderKeyword ?? "21_12" : null;
        var matrix = _matrixKeyword ?? "Full";
        var version = _versionSeen ? _versionText ?? "2.0" : "1.0";
        evidence.AddRange(DefaultEvidence(unit, parameter, format, reference));
        evidence.Add(PortsEvidence());
        if (ports == 2)
        {
            evidence.Add(OrderEvidence(twoPortOrder!));
        }

        if (noise)
        {
            evidence.Add(NoiseEvidence());
        }

        var dialect = new TouchstoneDialect(
            version,
            ports,
            unit,
            parameter,
            format,
            reference,
            twoPortOrder,
            matrix,
            _keywords.ToArray(),
            CountMap.FromNumeric(_valuesPerLine.Select(static p => new KeyValuePair<long, long>(p.Key, p.Value))),
            noise);

        var names = _names.Concat(noise ? TouchstoneSyntax.NoiseFieldNames : []).ToArray();
        var fields = new List<FieldProfile>();
        for (var i = 0; i < names.Length; i++)
        {
            fields.Add(Field(i, names[i], i < _names.Length ? i : -1, noise ? i - _names.Length : -1));
        }

        _context.Trace?.Sample(Estimate());
        var table = new TableSection(_rows, new MinMax(_names.Length, _names.Length), fields);
        var layout = new LayoutSection(_regions.Finish(counter.Lines, _sniff.Length), MaxLineLength: counter.MaxLineLength);
        return new FormatProfile(
            dialect,
            layout,
            new LineEndings(counter.Crlf, counter.Lf, counter.Cr),
            issues,
            evidence,
            Table: table,
            StableLines: _layoutOnly ? null : _regions.PreambleText(true),
            EffectiveLimits: _structured ? MemoryBudget.Record(_context.Limits, _caps) : null);
    }

    private FieldProfile Field(int index, string name, int network, int noise)
    {
        if (_layoutOnly)
        {
            return new FieldProfile
            {
                Index = index,
                Name = name,
                NameParts = null,
                Type = null,
                Dominant = null,
                TypeVotes = null,
                Count = null,
                Nulls = null,
                NullsExcludingAllNullRows = null,
                NullTokens = null,
                Flags = null,
                Distinct = null,
                Length = null,
                Shapes = null,
                Monotonic = null,
                Constant = null,
                Unique = null,
                RawName = name,
            };
        }

        var accumulator = network >= 0 ? _accumulators[network] : _noiseAccumulators[noise];
        var built = accumulator.Build(index, name, null, 0);

        // There are no all-null rows in this family, so every null is outside them.
        return built with { NameParts = null, RawName = name, NullsExcludingAllNullRows = built.Nulls };
    }

    private int? Tokens(ReadOnlySpan<char> content)
    {
        var count = 0;
        var rest = content;
        while (TouchstoneSyntax.NextToken(ref rest, out var token))
        {
            if (!TouchstoneSyntax.TryNumber(token, out _))
            {
                return null;
            }

            count++;
        }

        return count > 0 ? count : null;
    }

    private void Keyword(TextLine line, TouchstoneKeyword keyword, ReadOnlySpan<char> arguments)
    {
        if (keyword == TouchstoneKeyword.Unknown)
        {
            _regions.Other(line, LineClass.Text);
            return;
        }

        var name = TouchstoneSyntax.Name(keyword);
        if (!_keywords.Contains(name))
        {
            _keywords.Add(name);
        }

        switch (keyword)
        {
            case TouchstoneKeyword.Version:
                _versionSeen = true;
                if (TouchstoneSyntax.NextToken(ref arguments, out var version) && _versionText is null)
                {
                    _versionText = version.ToString();
                }

                break;
            case TouchstoneKeyword.NumberOfPorts when _keywordPorts is null && TouchstoneSyntax.TryCount(arguments, out var ports):
                _keywordPorts = ports;
                _keywordPortsLine = line.Number;
                break;
            case TouchstoneKeyword.TwoPortDataOrder when _orderKeyword is null && TouchstoneSyntax.NextToken(ref arguments, out var order):
                _orderKeyword = Ordinal.AsciiFold(order) switch { "12_21" => "12_21", _ => "21_12" };
                _orderKeywordLine = line.Number;
                break;
            case TouchstoneKeyword.MatrixFormat when _matrixKeyword is null && TouchstoneSyntax.NextToken(ref arguments, out var matrix):
                _matrixKeyword = Ordinal.AsciiFold(matrix) switch { "lower" => "Lower", "upper" => "Upper", _ => "Full" };
                break;
            case TouchstoneKeyword.NetworkData:
                _networkSeen = true;
                break;
            case TouchstoneKeyword.NoiseData:
                if (StartNoise("keyword", line.Number))
                {
                    _noiseKeywordLine = line.Number;
                    _regions.Other(line, LineClass.Text, startsNoise: true);
                    return;
                }

                break;
            case TouchstoneKeyword.End:
                _ended = true;
                break;
        }

        _regions.Other(line, LineClass.Text);
    }

    private void Data(TextLine line, ReadOnlySpan<char> content, int count)
    {
        if (!_structured && !Structure(count))
        {
            _regions.Other(line, LineClass.Text);
            return;
        }

        var rest = content;
        var first = true;
        var networkValues = 0;
        while (TouchstoneSyntax.NextToken(ref rest, out var token))
        {
            if (first)
            {
                first = false;

                // Version 1 marks the noise data of a two-port file by a frequency that is lower than the last one, where a block begins, or
                // equal to it on a line of exactly 5 values (IBIS: the first noise frequency is at most the highest network frequency).
                if (!_inNoise && !_versionSeen && _ports == 2 && _position == 0 && _haveFrequency
                    && TouchstoneSyntax.TryNumber(token, out var frequency)
                    && (frequency < _lastFrequency || (frequency == _lastFrequency && count == TouchstoneSyntax.NoiseValues)))
                {
                    StartNoise("frequencyDrop", line.Number);
                }

                _regions.Data(line, _inNoise);
            }

            if (_position == 0)
            {
                _blockStartLine = line.Number;
            }

            _buffer.Add(token);
            if (!_inNoise)
            {
                networkValues++;
            }

            _position++;
            if (_position == (_inNoise ? TouchstoneSyntax.NoiseValues : _blockSize))
            {
                CompleteBlock();
            }
        }

        if (networkValues > 0)
        {
            _valuesPerLine[networkValues] = _valuesPerLine.GetValueOrDefault(networkValues) + 1;
        }
    }

    // Decides the structure of the blocks, once: the port count, the entries of a block and the names of the fields. False when the port count
    // is not known (the line is then not data).
    private bool Structure(int firstLineValues)
    {
        if (_keywordPorts is { } keywordPorts)
        {
            _ports = keywordPorts;
            _portsSource = "keyword";
        }
        else if (_extension)
        {
            _ports = _extensionPorts;
            _portsSource = "extension";
        }
        else if (TouchstoneProfiler.InferPorts(firstLineValues) is { } inferred)
        {
            _ports = inferred;
            _portsSource = "data";
            _inferredFrom = firstLineValues;
        }
        else
        {
            return false;
        }

        if (_ports > TouchstoneProfiler.MaxPorts)
        {
            _ports = 0;
            return false;
        }

        var matrix = _matrixKeyword ?? "Full";
        var order = _orderKeyword ?? "21_12";
        var format = _option?.Format ?? "MA";
        var letter = _option?.Parameter ?? "S";
        var (first, second) = format switch
        {
            "DB" => ("db", "ang"),
            "RI" => ("re", "im"),
            _ => ("mag", "ang"),
        };

        var names = new List<string> { "freq" };
        foreach (var (row, column) in Entries(_ports, matrix, order))
        {
            var label = letter + (_ports <= 9 ? Ordinal.Invariant(row) + Ordinal.Invariant(column) : Ordinal.Invariant(row) + "_" + Ordinal.Invariant(column));
            names.Add(label + "." + first);
            names.Add(label + "." + second);
        }

        _names = names.ToArray();
        _blockSize = _names.Length;
        _structured = true;

        // The caps follow the number of fields that the table can have, the noise fields of a two-port file included, so that they do not depend on whether noise data turns up later.
        _caps = MemoryBudget.Effective(_context.Limits, _blockSize + (_ports == 2 ? TouchstoneSyntax.NoiseValues : 0));
        if (!_layoutOnly)
        {
            _accumulators = _names.Select(Accumulator).ToArray();
        }

        return true;
    }

    private FieldAccumulator Accumulator(string name)
    {
        var access = new FieldValueAccess(
            _gate.Allows(ValuesPolicy.FieldTop, name),
            _gate.Allows(ValuesPolicy.FieldText, name),
            _gate.Allows(ValuesPolicy.ConstantStatistics, name));
        return new FieldAccumulator(_caps, '.', access);
    }

    private bool StartNoise(string source, long line)
    {
        // Noise data belongs to a two-port network (the specification), and a file has one block of it.
        if (_inNoise || (_structured && _ports != 2))
        {
            return false;
        }

        // The network block that was being read is cut short.
        if (_position > 0)
        {
            _incompleteBlocks++;
            if (_incompleteLines.Count < MaxListedLines)
            {
                _incompleteLines.Add(_blockStartLine);
            }

            _buffer.Clear();
            _position = 0;
        }

        if (!_structured)
        {
            _caps = MemoryBudget.Effective(_context.Limits, TouchstoneSyntax.NoiseValues);
        }

        _inNoise = true;
        _noiseSource = source;
        _noiseStartLine = line;
        _haveFrequency = false;
        if (!_layoutOnly)
        {
            _noiseAccumulators = TouchstoneSyntax.NoiseFieldNames.Select(Accumulator).ToArray();
        }

        return true;
    }

    private void CompleteBlock()
    {
        var accumulators = _inNoise ? _noiseAccumulators : _accumulators;
        var frequency = double.NaN;
        for (var i = 0; i < _position; i++)
        {
            var token = _buffer.Token(i);
            if (i == 0)
            {
                frequency = TouchstoneSyntax.TryNumber(token, out var value) ? value : double.NaN;
            }

            if (!_layoutOnly)
            {
                accumulators[i].AddText(token);
            }
        }

        if (!double.IsNaN(frequency))
        {
            if (_haveFrequency && frequency < _lastFrequency)
            {
                _decreases++;
                if (_decreaseLines.Count < MaxListedLines)
                {
                    _decreaseLines.Add(_blockStartLine);
                }
            }

            _lastFrequency = frequency;
            _haveFrequency = true;
        }

        if (_inNoise)
        {
            _noiseRows++;
        }
        else
        {
            _rows++;
        }

        _buffer.Clear();
        _position = 0;
        if (((_rows + _noiseRows) & 0x3FF) == 0)
        {
            _context.Trace?.Sample(Estimate());
        }
    }

    private long Estimate()
    {
        long bytes = _buffer.EstimatedBytes;
        foreach (var accumulator in _accumulators)
        {
            bytes += accumulator.EstimatedBytes;
        }

        foreach (var accumulator in _noiseAccumulators)
        {
            bytes += accumulator.EstimatedBytes;
        }

        return bytes;
    }

    // The entries of a block, in file order, as (row, column) pairs of 1-based port numbers.
    internal static IEnumerable<(long Row, long Column)> Entries(long ports, string matrix, string order)
    {
        if (ports == 2)
        {
            // Version 1 two-port data runs N11, N21, N12, N22; [Two-Port Data Order] 12_21 reads N11, N12, N21, N22. A lower or upper matrix of
            // two ports holds N11, N21 and N22 (the specification: N21 and N12 are taken to be the same).
            if (matrix != "Full")
            {
                return [(1, 1), (2, 1), (2, 2)];
            }

            return order == "12_21" ? [(1, 1), (1, 2), (2, 1), (2, 2)] : [(1, 1), (2, 1), (1, 2), (2, 2)];
        }

        var entries = new List<(long, long)>();
        for (var row = 1L; row <= ports; row++)
        {
            for (var column = matrix == "Upper" ? row : 1L; column <= (matrix == "Lower" ? row : ports); column++)
            {
                entries.Add((row, column));
            }
        }

        return entries;
    }

    private IEnumerable<Evidence> DefaultEvidence(string unit, string parameter, string format, double reference)
    {
        var why = _option is null ? "default: the file has no option line" : "default: not in the option line";
        if (_option?.Unit is null)
        {
            yield return Defaulted("frequencyUnit", unit, why);
        }

        if (_option?.Parameter is null)
        {
            yield return Defaulted("parameter", parameter, why);
        }

        if (_option?.Format is null)
        {
            yield return Defaulted("dataFormat", format, why);
        }

        if (_option?.Reference is null)
        {
            yield return Defaulted("referenceOhms", CanonicalNumber.FormatFinite(reference), why);
        }
    }

    private static Evidence Defaulted(string topic, string value, string reason)
    {
        return new Evidence(EvidenceStages.Dialect, topic, value, [new Candidate(value, 1.0, reason)]);
    }

    private Evidence PortsEvidence()
    {
        var candidates = new List<Candidate>
        {
            new("extension", _portsSource == "extension" ? 1.0 : 0.0, _extension ? "the extension gives " + Ordinal.Invariant(_extensionPorts) + " ports" : "the extension gives no port count"),
            new("keyword", _portsSource == "keyword" ? 1.0 : 0.0, _keywordPorts is { } k ? "[Number of Ports] gives " + Ordinal.Invariant(k) + " ports" : "no [Number of Ports]"),
            new("data", _portsSource == "data" ? 1.0 : 0.0, _inferredFrom > 0 ? "the first data line holds " + Ordinal.Invariant(_inferredFrom) + " values" : "not needed: the name or a keyword gives the count"),
        };
        return new Evidence(EvidenceStages.Dialect, "ports", Ordinal.Invariant(_ports), candidates);
    }

    private Evidence OrderEvidence(string chosen)
    {
        var given = _orderKeyword is not null;
        var candidates = new List<Candidate>
        {
            new("12_21", chosen == "12_21" ? 1.0 : 0.0, chosen == "12_21" ? "given by [Two-Port Data Order] on line " + Ordinal.Invariant(_orderKeywordLine) : "not the order of this file"),
            new("21_12", chosen == "21_12" ? 1.0 : 0.0, !given ? (_versionSeen ? "default: no [Two-Port Data Order]" : "default: the Version 1 order") : chosen == "21_12" ? "given by [Two-Port Data Order] on line " + Ordinal.Invariant(_orderKeywordLine) : "not the order of this file"),
        };
        return new Evidence(EvidenceStages.Dialect, "twoPortOrder", chosen, candidates);
    }

    private Evidence NoiseEvidence()
    {
        var byKeyword = _noiseSource == "keyword";
        var candidates = new List<Candidate>
        {
            new("frequencyDrop", byKeyword ? 0.0 : 1.0, byKeyword ? "not needed: [Noise Data] marks the block" : "the frequency drops on line " + Ordinal.Invariant(_noiseStartLine)),
            new("keyword", byKeyword ? 1.0 : 0.0, byKeyword ? "[Noise Data] on line " + Ordinal.Invariant(_noiseKeywordLine) : "no [Noise Data]"),
        };
        return new Evidence(EvidenceStages.Dialect, "noise", _noiseSource, candidates);
    }

    /// <summary>One block of values held until it is whole: the characters of its tokens, end to end, and where each starts.</summary>
    private sealed class BlockBuffer
    {
        private char[] _chars = new char[256];
        private int[] _starts = new int[33];
        private int _count;
        private int _length;

        public long EstimatedBytes => (_chars.Length * 2L) + (_starts.Length * 4L);

        public void Add(ReadOnlySpan<char> token)
        {
            if (_length + token.Length > _chars.Length)
            {
                Array.Resize(ref _chars, Math.Max(_chars.Length * 2, _length + token.Length));
            }

            if (_count + 1 >= _starts.Length)
            {
                Array.Resize(ref _starts, _starts.Length * 2);
            }

            token.CopyTo(_chars.AsSpan(_length));
            _starts[_count] = _length;
            _length += token.Length;
            _count++;
            _starts[_count] = _length;
        }

        public ReadOnlySpan<char> Token(int index)
        {
            return _chars.AsSpan(_starts[index], _starts[index + 1] - _starts[index]);
        }

        public void Clear()
        {
            _count = 0;
            _length = 0;
            _starts[0] = 0;
        }
    }
}
