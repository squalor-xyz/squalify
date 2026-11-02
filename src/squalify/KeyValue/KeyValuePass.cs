// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The profile pass of a key=value document: one streaming read that splits the document into sections, notes the keys of each (and their
/// values, when the values tier allows it), and feeds every value to the accumulator of its key, so that a key that repeats across sections
/// is one field with a count of N. A key that repeats inside one section is the issue <c>duplicateKey</c>.
/// </summary>
internal sealed class KeyValuePass
{
    private const int MaxListedLines = 10;
    private const int MaxStableLines = 64;

    // A longer line is not offered as a stable line: its regex would hold the line.
    private const int MaxStableLength = 4096;

    private readonly ProfileContext _context;
    private readonly ValuesGate _gate;
    private readonly bool _layoutOnly;
    private readonly FieldCaps _caps;
    private readonly List<SectionBuilder> _sections = [];
    private readonly Dictionary<string, int> _fieldOf = new(StringComparer.Ordinal);
    private readonly List<string> _names = [];
    private readonly List<FieldAccumulator?> _accumulators = [];
    private readonly List<StableLineSource> _stable = [];
    private readonly long[] _kinds = new long[5];
    private readonly long[] _separators = new long[2];
    private readonly List<long> _duplicateLines = [];
    private long _duplicates;
    private long _stableText;
    private long _stableKeys;
    private bool _hasSections;
    private SectionBuilder? _current;

    /// <summary>Creates the pass.</summary>
    /// <param name="structure">What the scoring pass learned.</param>
    /// <param name="context">The options and limits.</param>
    public KeyValuePass(KeyValueStructure structure, ProfileContext context)
    {
        _context = context;
        _gate = context.Gate;
        _layoutOnly = context.Options.LayoutOnly;
        _caps = MemoryBudget.Effective(context.Limits, structure.DistinctKeys);
    }

    /// <summary>Receives one physical line.</summary>
    /// <param name="line">The line.</param>
    /// <returns>True: the pass reads the whole file.</returns>
    public bool OnLine(TextLine line)
    {
        var kind = KeyValueSyntax.Classify(line.Text);
        switch (kind.Kind)
        {
            case KeyValueLineKind.Blank:
                _kinds[0]++;
                break;
            case KeyValueLineKind.Comment:
                _kinds[1]++;
                OfferText(line);
                break;
            case KeyValueLineKind.Section:
                _kinds[4]++;
                _hasSections = true;
                _current = new SectionBuilder(kind.Name!, line.Number);
                _sections.Add(_current);
                OfferText(line);
                break;
            case KeyValueLineKind.Pair:
                _kinds[2]++;
                Pair(line, kind);
                break;
            default:
                _kinds[4]++;
                OfferText(line);
                break;
        }

        return true;
    }

    /// <summary>Ends the pass and builds the profile.</summary>
    /// <param name="counter">The line counts of the whole file.</param>
    /// <param name="fileLength">The file size in bytes.</param>
    /// <returns>The parts of the descriptor.</returns>
    public FormatProfile Build(LineCounter counter, long fileLength)
    {
        var separator = Separator();
        var sections = _sections.Select(Section).ToArray();
        var issues = new List<Issue>();
        if (_duplicates > 0)
        {
            issues.Add(new Issue(IssueCodes.DuplicateKey, _duplicates, FirstLines: _duplicateLines.ToArray()));
        }

        if (counter.MixedLineEndings() is { } mixed)
        {
            issues.Add(mixed);
        }

        var fields = new List<FieldProfile>();
        for (var i = 0; i < _names.Count; i++)
        {
            fields.Add(Field(i));
        }

        var minKeys = _sections.Count == 0 ? 0 : _sections.Min(static s => s.Keys.Count);
        var maxKeys = _sections.Count == 0 ? 0 : _sections.Max(static s => s.Keys.Count);
        var table = new TableSection(_sections.Count, new MinMax(minKeys, maxKeys), fields);

        var kinds = new List<KeyValuePair<string, long>>();
        string[] names = ["blank", "comment", "keyValue", "keyDelimValue", "text"];
        for (var i = 0; i < names.Length; i++)
        {
            if (_kinds[i] > 0)
            {
                kinds.Add(new(names[i], _kinds[i]));
            }
        }

        // One region over the whole file: the document has no preamble, header or footer to tell apart.
        var regions = counter.Lines == 0
            ? []
            : new[] { new Region("data", 1, counter.Lines, 0, fileLength, LineKinds: CountMap.FromOrdinal(kinds)) };
        var total = _separators[0] + _separators[1];
        var evidence = new[]
        {
            new Evidence(
                EvidenceStages.Dialect,
                "separator",
                separator,
                [
                    new Candidate(":", _separators[1] > _separators[0] ? 1.0 : 0.0, Ordinal.Invariant(_separators[1]) + " of " + Ordinal.Invariant(total) + " key/value lines"),
                    new Candidate("=", _separators[1] > _separators[0] ? 0.0 : 1.0, Ordinal.Invariant(_separators[0]) + " of " + Ordinal.Invariant(total) + " key/value lines"),
                ]),
        };

        _context.Trace?.Sample(Estimate());
        return new FormatProfile(
            new KeyValueDialect(separator, _hasSections),
            new LayoutSection(regions, MaxLineLength: counter.MaxLineLength),
            new LineEndings(counter.Crlf, counter.Lf, counter.Cr),
            issues,
            evidence,
            Table: table,
            StableLines: _layoutOnly ? null : _stable,
            EffectiveLimits: MemoryBudget.Record(_context.Limits, _caps),
            Sections: sections);
    }

    // The more common of the two separators; a tie goes to =. A method of its own: the literals stay out of the method that makes the issues.
    private string Separator()
    {
        return _separators[1] > _separators[0] ? ":" : "=";
    }

    private void Pair(TextLine line, KeyValueLine pair)
    {
        _separators[pair.Separator == ':' ? 1 : 0]++;
        var key = pair.Name!;
        if (_current is null)
        {
            // Keys before any section header belong to an unnamed section that starts at the first of them.
            _current = new SectionBuilder(string.Empty, line.Number);
            _sections.Add(_current);
        }

        if (!_current.Seen.Add(key))
        {
            _duplicates++;
            if (_duplicateLines.Count < MaxListedLines)
            {
                _duplicateLines.Add(line.Number);
            }
        }

        _current.Keys.Add(key);
        _current.Values.Add(_gate.Allows(ValuesPolicy.SectionValues, key) ? pair.Value : null);
        if (_stableKeys < MaxStableLines && line.Text.Length <= MaxStableLength)
        {
            _stableKeys++;
            _stable.Add(new StableLineSource(line.Number, StableLineKind.PreambleKey, key, line.Text));
        }

        if (!_fieldOf.TryGetValue(key, out var index))
        {
            index = _names.Count;
            _fieldOf[key] = index;
            _names.Add(key);
            _accumulators.Add(_layoutOnly ? null : NewAccumulator(key));
        }

        _accumulators[index]?.AddText(pair.Value);
        if ((_kinds[2] & 0x3FF) == 0)
        {
            _context.Trace?.Sample(Estimate());
        }
    }

    private FieldAccumulator NewAccumulator(string key)
    {
        var access = new FieldValueAccess(
            _gate.Allows(ValuesPolicy.FieldTop, key),
            _gate.Allows(ValuesPolicy.FieldText, key),
            _gate.Allows(ValuesPolicy.ConstantStatistics, key));
        return new FieldAccumulator(_caps, '.', access);
    }

    // The text of a comment, section or other line is a value: only the values tier may carry it, as a stable-line regex.
    private void OfferText(TextLine line)
    {
        if (!_layoutOnly && _stableText < MaxStableLines && line.Text.Length <= MaxStableLength && _gate.Allows(ValuesPolicy.StableLineText, null))
        {
            _stableText++;
            _stable.Add(new StableLineSource(line.Number, StableLineKind.PreambleText, null, line.Text));
        }
    }

    private KeyValueSection Section(SectionBuilder section)
    {
        var values = _gate.Allows(ValuesPolicy.SectionValues, null) || section.Values.Any(static v => v is not null) ? section.Values.ToArray() : null;
        return new KeyValueSection(section.Name, section.Line, section.Keys.ToArray(), values);
    }

    private FieldProfile Field(int index)
    {
        var name = _names[index];
        if (_accumulators[index] is not { } accumulator)
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

        var built = accumulator.Build(index, name, null, 0);
        return built with { NameParts = null, RawName = name, NullsExcludingAllNullRows = built.Nulls };
    }

    private long Estimate()
    {
        long bytes = 0;
        foreach (var accumulator in _accumulators)
        {
            bytes += accumulator?.EstimatedBytes ?? 0;
        }

        return bytes;
    }

    private sealed class SectionBuilder(string name, long line)
    {
        public string Name { get; } = name;

        public long Line { get; } = line;

        public List<string> Keys { get; } = [];

        public List<string?> Values { get; } = [];

        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
    }
}
