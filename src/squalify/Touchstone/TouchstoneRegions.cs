// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>What a line of a Touchstone file is, apart from its numbers.</summary>
internal enum LineClass
{
    /// <summary>Empty, or only spaces and tabs.</summary>
    Blank,

    /// <summary>A comment line: <c>!</c> is its first character that is not white space.</summary>
    Comment,

    /// <summary>Any other line that holds no network or noise data: the option line, a keyword, a line of text.</summary>
    Text,
}

/// <summary>
/// Tracks the regions of a Touchstone file as its lines stream by: the <c>preamble</c> (everything before the first data line), the
/// <c>data</c> (the network data), the <c>block</c> labelled <c>noise</c> (a Version 2 file's block starts at its <c>[Noise Data]</c>
/// keyword, a Version 1 file's at its first line of noise data), and the <c>footer</c> (the lines after the last line of data that are not
/// blank). A blank line belongs to the region before it, and so does a comment or a keyword line that comes before the next line of data,
/// so the regions tile the file. Lines that are not data are counted by kind (<c>blank</c>, <c>comment</c>, <c>text</c>); the lines of
/// numbers are not.
/// </summary>
internal sealed class TouchstoneRegions
{
    private const int MaxTextLines = 100;

    // A longer line is not offered as a stable line: its regex would hold the line.
    private const int MaxTextLength = 4096;

    private readonly bool _keepText;
    private readonly List<Region> _done = [];
    private readonly List<StableLineSource> _text = [];
    private Open _current = new("preamble", 1, 0);

    // The lines since the last line of data of the current region: the blanks before the first line that is not blank, then the rest.
    private long _leadBlank;
    private bool _tailStarted;
    private long _tailFirstLine;
    private long _tailFirstByte;
    private readonly long[] _tail = new long[3];

    /// <summary>Creates the tracker.</summary>
    /// <param name="keepText">True to keep the text of the comment and text lines of the preamble (values tier: only when the options allow it).</param>
    public TouchstoneRegions(bool keepText)
    {
        _keepText = keepText;
    }

    /// <summary>Notes a line that holds no data.</summary>
    /// <param name="line">The line.</param>
    /// <param name="kind">What it is.</param>
    /// <param name="startsNoise">True for the <c>[Noise Data]</c> keyword, which starts the noise block.</param>
    public void Other(TextLine line, LineClass kind, bool startsNoise = false)
    {
        if (startsNoise)
        {
            Transition("block", line);
            _current.Counts[(int)kind]++;
            return;
        }

        if (_current.Kind == "preamble")
        {
            _current.Counts[(int)kind]++;
            if (_keepText && kind != LineClass.Blank && _text.Count < MaxTextLines && line.Text.Length <= MaxTextLength)
            {
                _text.Add(new StableLineSource(line.Number, StableLineKind.PreambleText, null, line.Text));
            }

            return;
        }

        if (!_tailStarted)
        {
            if (kind == LineClass.Blank)
            {
                _leadBlank++;
                return;
            }

            _tailStarted = true;
            _tailFirstLine = line.Number;
            _tailFirstByte = line.StartByte;
        }

        _tail[(int)kind]++;
    }

    /// <summary>Notes a line of network data or noise data.</summary>
    /// <param name="line">The line.</param>
    /// <param name="noise">True for a line of noise data.</param>
    public void Data(TextLine line, bool noise)
    {
        var kind = noise ? "block" : "data";
        if (_current.Kind == "preamble" || _current.Kind != kind)
        {
            Transition(kind, line);
            return;
        }

        FoldTail();
    }

    /// <summary>Ends the file.</summary>
    /// <param name="totalLines">The number of lines.</param>
    /// <param name="fileLength">The file size in bytes.</param>
    /// <returns>The regions, in file order.</returns>
    public IReadOnlyList<Region> Finish(long totalLines, long fileLength)
    {
        _current.Counts[(int)LineClass.Blank] += _leadBlank;
        _leadBlank = 0;
        if (_tailStarted)
        {
            Close(_tailFirstLine - 1, _tailFirstByte);
            var footer = new Open("footer", _tailFirstLine, _tailFirstByte);
            for (var i = 0; i < 3; i++)
            {
                footer.Counts[i] = _tail[i];
            }

            Close(footer, totalLines, fileLength);
        }
        else
        {
            Close(totalLines, fileLength);
        }

        return _done;
    }

    /// <summary>The comment and text lines of the preamble, when their text may be kept.</summary>
    /// <param name="allowed">True when the values tier allows it.</param>
    /// <returns>The lines.</returns>
    public IReadOnlyList<StableLineSource> PreambleText(bool allowed)
    {
        return allowed ? _text : [];
    }

    private void Transition(string kind, TextLine line)
    {
        FoldTail();
        Close(line.Number - 1, line.StartByte);
        _current = new Open(kind, line.Number, line.StartByte);
    }

    // The lines since the last line of data belong to the current region, when more data of it follows.
    private void FoldTail()
    {
        _current.Counts[(int)LineClass.Blank] += _leadBlank;
        for (var i = 0; i < 3; i++)
        {
            _current.Counts[i] += _tail[i];
            _tail[i] = 0;
        }

        _leadBlank = 0;
        _tailStarted = false;
    }

    private void Close(long lastLine, long endByte)
    {
        Close(_current, lastLine, endByte);
    }

    private void Close(Open open, long lastLine, long endByte)
    {
        if (lastLine < open.FirstLine)
        {
            return;
        }

        var kinds = new List<KeyValuePair<string, long>>();
        string[] names = ["blank", "comment", "text"];
        for (var i = 0; i < 3; i++)
        {
            if (open.Counts[i] > 0)
            {
                kinds.Add(new(names[i], open.Counts[i]));
            }
        }

        _done.Add(new Region(
            open.Kind,
            open.FirstLine,
            lastLine,
            open.StartByte,
            endByte,
            LineKinds: kinds.Count == 0 ? null : CountMap.FromOrdinal(kinds))
        {
            Label = open.Kind == "block" ? "noise" : null,
        });
    }

    private sealed class Open(string kind, long firstLine, long startByte)
    {
        public string Kind { get; } = kind;

        public long FirstLine { get; } = firstLine;

        public long StartByte { get; } = startByte;

        public long[] Counts { get; } = new long[3];
    }
}
