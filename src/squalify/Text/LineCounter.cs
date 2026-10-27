// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Counts lines, blank lines, the longest line and the line endings (CRLF, lone LF, lone CR) over a stream of
/// characters. A blank line is empty or holds only spaces and tabs. Line lengths count UTF-16 characters. Shared by
/// the generic text profile and the delimited profile, so both report the same counts and the same
/// <c>mixedLineEndings</c> issue.
/// </summary>
internal sealed class LineCounter
{
    private const int MaxListedLines = 10;

    private long _currentLength;
    private bool _currentBlank = true;
    private bool _pendingCr;
    private long _pendingCrLine;

    // First lines of each ending kind, for the mixedLineEndings issue: 0 = CRLF, 1 = LF, 2 = CR.
    private readonly List<long>[] _firstLines = [new(), new(), new()];

    /// <summary>The number of lines. A last line without a terminator counts; a trailing line break does not add one.</summary>
    public long Lines { get; private set; }

    /// <summary>The number of blank lines (empty, or only spaces and tabs).</summary>
    public long BlankLines { get; private set; }

    /// <summary>The longest line in UTF-16 characters, line break excluded.</summary>
    public long MaxLineLength { get; private set; }

    /// <summary>CRLF pairs.</summary>
    public long Crlf { get; private set; }

    /// <summary>Lone LF characters.</summary>
    public long Lf { get; private set; }

    /// <summary>Lone CR characters.</summary>
    public long Cr { get; private set; }

    /// <summary>Counts one character.</summary>
    /// <param name="c">The next character of the decoded text.</param>
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

    /// <summary>Ends the stream: resolves a pending CR and counts a last line without a terminator.</summary>
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
