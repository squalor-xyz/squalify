// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>Receives each physical line of a scan.</summary>
internal interface IPhysicalLineSink
{
    /// <summary>A physical line ended.</summary>
    /// <param name="line">The 1-based line.</param>
    /// <param name="startByte">The byte offset of the line's first character (of its terminator, for an empty line).</param>
    /// <param name="contentEnd">The byte offset of the line terminator, or the file length for a last line without one.</param>
    /// <param name="blank">True when the line is empty or holds only spaces and tabs.</param>
    void Line(long line, long startByte, long contentEnd, bool blank);
}

/// <summary>
/// One streaming scan of a text file: it decodes the characters, finds the physical lines (CRLF, LF and CR), and feeds
/// every tokenizer, the line counter and an optional line sink in a single pass. The tokenizers are told where a line
/// ends only when the next character (or the end of the file) shows where the next line starts, so each record knows
/// its end byte.
/// </summary>
internal static class TextScanner
{
    private const int ChunkSize = 16 * 1024;

    /// <summary>Scans the whole file.</summary>
    /// <param name="source">The source; it is opened once.</param>
    /// <param name="sniff">The byte-pass result of a text file.</param>
    /// <param name="tokenizers">The tokenizers to feed.</param>
    /// <param name="counter">The line counter to feed.</param>
    /// <param name="lines">Receives each physical line, or null.</param>
    public static void Scan(IProfileSource source, SniffResult sniff, IReadOnlyList<RecordTokenizer> tokenizers, LineCounter counter, IPhysicalLineSink? lines)
    {
        if (sniff.Charset is null)
        {
            throw new InvalidOperationException("Scanning needs a text file.");
        }

        var chars = new char[ChunkSize];
        var offsets = new long[ChunkSize];
        var line = 1L;
        var lineStart = 0L;
        var lineHasChars = false;
        var lineBlank = true;
        var pending = Pending.None;
        var pendingContentEnd = 0L;

        using var stream = source.Open();
        var decoder = new TextDecoder(stream, sniff.Charset, sniff.BomLength);
        int n;
        while ((n = decoder.Read(chars, offsets)) > 0)
        {
            for (var i = 0; i < n; i++)
            {
                var c = chars[i];
                var offset = offsets[i];
                counter.Add(c);

                if (pending == Pending.Cr && c == '\n')
                {
                    pending = Pending.Done;
                    continue;
                }

                if (pending != Pending.None)
                {
                    EndLine(offset);
                }

                if (c == '\r' || c == '\n')
                {
                    pending = c == '\r' ? Pending.Cr : Pending.Done;
                    pendingContentEnd = offset;
                    if (!lineHasChars)
                    {
                        lineStart = offset;
                    }

                    continue;
                }

                if (!lineHasChars)
                {
                    lineHasChars = true;
                    lineStart = offset;
                }

                if (c != ' ' && c != '\t')
                {
                    lineBlank = false;
                }

                foreach (var tokenizer in tokenizers)
                {
                    tokenizer.Char(c, offset);
                }
            }
        }

        counter.Finish();
        var length = sniff.Length;
        if (pending != Pending.None)
        {
            EndLine(length);
        }
        else if (lineHasChars)
        {
            // A last line without a terminator.
            lines?.Line(line, lineStart, length, lineBlank);
            foreach (var tokenizer in tokenizers)
            {
                tokenizer.Break(length, length);
            }
        }

        foreach (var tokenizer in tokenizers)
        {
            tokenizer.Flush(length);
        }

        void EndLine(long nextStart)
        {
            lines?.Line(line, lineStart, pendingContentEnd, lineBlank);
            foreach (var tokenizer in tokenizers)
            {
                tokenizer.Break(nextStart, pendingContentEnd);
            }

            line++;
            pending = Pending.None;
            lineHasChars = false;
            lineBlank = true;
        }
    }

    private enum Pending
    {
        None,
        Cr,
        Done,
    }
}
