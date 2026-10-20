// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The fallback profile for any text file, scoring 0.1. It decodes the text and reports the line endings (CRLF, lone LF,
/// lone CR), the line count, the blank lines, the longest line, and one <c>unknown</c> region over the whole file.
/// A blank line is empty or holds only spaces and tabs. Line lengths count UTF-16 characters of the decoded text.
/// </summary>
internal sealed class GenericTextProfiler : IFormatProfiler
{
    private const int MaxListedLines = 10;
    private const int ChunkSize = 16 * 1024;

    /// <inheritdoc />
    public string Family => "text";

    /// <inheritdoc />
    public (double Score, string Reason) Score(SniffResult sniff)
    {
        ArgumentNullException.ThrowIfNull(sniff);
        return sniff.Kind switch
        {
            "text" => (0.1, "text content"),
            "empty" => (0.0, "file is empty"),
            _ => (0.0, "not text: binary content"),
        };
    }

    /// <inheritdoc />
    public FormatProfile Profile(IProfileSource source, SniffResult sniff, ProfileContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sniff);
        if (sniff.Charset is null)
        {
            throw new InvalidOperationException("The text profiler needs a text file.");
        }

        var counter = new LineCounter();
        using (var stream = source.Open())
        {
            var decoder = new TextDecoder(stream, sniff.Charset, sniff.BomLength);
            var chars = new char[ChunkSize];
            var offsets = new long[ChunkSize];
            int n;
            while ((n = decoder.Read(chars, offsets)) > 0)
            {
                for (var i = 0; i < n; i++)
                {
                    counter.Add(chars[i]);
                }
            }
        }

        counter.Finish();

        var regions = new List<Region>();
        if (counter.Lines > 0)
        {
            var kinds = new List<KeyValuePair<string, long>>();
            if (counter.BlankLines > 0)
            {
                kinds.Add(new("blank", counter.BlankLines));
            }

            if (counter.Lines - counter.BlankLines > 0)
            {
                kinds.Add(new("text", counter.Lines - counter.BlankLines));
            }

            regions.Add(new Region("unknown", 1, counter.Lines, 0, sniff.Length, LineKinds: CountMap.FromOrdinal(kinds)));
        }

        var issues = new List<Issue>();
        var mixed = counter.MixedLineEndings();
        if (mixed is not null)
        {
            issues.Add(mixed);
        }

        return new FormatProfile(
            Dialect: null,
            Layout: new LayoutSection(regions, MaxLineLength: counter.MaxLineLength),
            LineEndings: new LineEndings(counter.Crlf, counter.Lf, counter.Cr),
            Issues: issues,
            Evidence: Array.Empty<Evidence>());
    }

    /// <summary>Counts lines and line endings over a stream of characters.</summary>
    private sealed class LineCounter
    {
        private long _currentLength;
        private bool _currentBlank = true;
        private bool _pendingCr;
        private long _pendingCrLine;

        // First lines of each ending kind, for the mixedLineEndings issue: 0 = CRLF, 1 = LF, 2 = CR.
        private readonly List<long>[] _firstLines = [new(), new(), new()];

        public long Lines { get; private set; }

        public long BlankLines { get; private set; }

        public long MaxLineLength { get; private set; }

        public long Crlf { get; private set; }

        public long Lf { get; private set; }

        public long Cr { get; private set; }

        public void Add(char c)
        {
            if (_pendingCr)
            {
                _pendingCr = false;
                if (c == '\n')
                {
                    Crlf++;
                    Remember(0, _pendingCrLine);
                    return;
                }

                Cr++;
                Remember(2, _pendingCrLine);
            }

            if (c == '\r')
            {
                EndLine();
                _pendingCr = true;
                _pendingCrLine = Lines;
            }
            else if (c == '\n')
            {
                EndLine();
                Lf++;
                Remember(1, Lines);
            }
            else
            {
                _currentLength++;
                if (c != ' ' && c != '\t')
                {
                    _currentBlank = false;
                }
            }
        }

        public void Finish()
        {
            if (_pendingCr)
            {
                _pendingCr = false;
                Cr++;
                Remember(2, _pendingCrLine);
            }

            // A last line without a terminator still counts.
            if (_currentLength > 0)
            {
                EndLine();
            }
        }

        /// <summary>The <c>mixedLineEndings</c> issue, or null when at most one kind of line ending occurs.</summary>
        public Issue? MixedLineEndings()
        {
            var kinds = (Crlf > 0 ? 1 : 0) + (Lf > 0 ? 1 : 0) + (Cr > 0 ? 1 : 0);
            if (kinds < 2)
            {
                return null;
            }

            // The dominant kind is the most frequent one; a tie goes to CRLF, then LF, then CR.
            var counts = new[] { Crlf, Lf, Cr };
            var dominant = 0;
            for (var k = 1; k < counts.Length; k++)
            {
                if (counts[k] > counts[dominant])
                {
                    dominant = k;
                }
            }

            var others = new List<long>();
            for (var k = 0; k < counts.Length; k++)
            {
                if (k != dominant)
                {
                    others.AddRange(_firstLines[k]);
                }
            }

            others.Sort();
            return new Issue("mixedLineEndings", Crlf + Lf + Cr - counts[dominant], FirstLines: others.Take(MaxListedLines).ToArray());
        }

        private void Remember(int kind, long line)
        {
            if (_firstLines[kind].Count < MaxListedLines)
            {
                _firstLines[kind].Add(line);
            }
        }

        private void EndLine()
        {
            Lines++;
            if (_currentBlank)
            {
                BlankLines++;
            }

            MaxLineLength = Math.Max(MaxLineLength, _currentLength);
            _currentLength = 0;
            _currentBlank = true;
        }
    }
}
