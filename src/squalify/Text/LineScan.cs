// SPDX-License-Identifier: Apache-2.0
using System.Text;

namespace squalor.squalify;

/// <summary>One physical line of a text file, as <see cref="LineScan"/> delivers it.</summary>
/// <param name="Number">The 1-based line number.</param>
/// <param name="StartByte">The byte offset of the line's first character (of its terminator, for an empty line).</param>
/// <param name="Text">The line, line break excluded; at most <see cref="LineScan.MaxChars"/> characters.</param>
/// <param name="Truncated">True when the line was longer than <see cref="LineScan.MaxChars"/> and <paramref name="Text"/> is its start.</param>
internal readonly record struct TextLine(long Number, long StartByte, string Text, bool Truncated);

/// <summary>
/// A streaming scan of a text file that hands each physical line, with its text, to a callback. Lines end at CRLF, LF or CR, and a last
/// line without a terminator counts, exactly as in <see cref="TextScanner"/> and <see cref="LineCounter"/>, so the line numbers and byte
/// offsets agree with the other profilers. The text of a line is capped (<see cref="MaxChars"/>) so that a file with no line break does
/// not have to be held whole; a longer line is delivered cut and marked.
/// </summary>
internal static class LineScan
{
    /// <summary>The most characters of one line that are kept.</summary>
    public const int MaxChars = 1 << 20;

    private const int ChunkSize = 16 * 1024;

    /// <summary>Scans the file, or its start.</summary>
    /// <param name="source">The source; it is opened once.</param>
    /// <param name="sniff">The byte-pass result of a text file.</param>
    /// <param name="counter">Receives every character (so its counts cover the file), or null. It is finished only when the scan reaches the end.</param>
    /// <param name="onLine">Receives each line; returns false to stop the scan early (the counter is then not finished).</param>
    /// <returns>True when the scan reached the end of the file, false when the callback stopped it.</returns>
    public static bool Scan(IProfileSource source, SniffResult sniff, LineCounter? counter, Func<TextLine, bool> onLine)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sniff);
        ArgumentNullException.ThrowIfNull(onLine);
        if (sniff.Charset is null)
        {
            throw new InvalidOperationException("Scanning needs a text file.");
        }

        var chars = new char[ChunkSize];
        var offsets = new long[ChunkSize];
        var text = new StringBuilder();
        var line = 1L;
        var lineStart = 0L;
        var lineHasChars = false;
        var truncated = false;
        var pending = Pending.None;

        using var stream = source.Open();
        var decoder = new TextDecoder(stream, sniff.Charset, sniff.BomLength);
        int n;
        while ((n = decoder.Read(chars, offsets)) > 0)
        {
            for (var i = 0; i < n; i++)
            {
                var c = chars[i];
                var offset = offsets[i];
                counter?.Add(c);

                if (pending == Pending.Cr && c == '\n')
                {
                    pending = Pending.Done;
                    continue;
                }

                if (pending != Pending.None)
                {
                    if (!onLine(new TextLine(line, lineStart, text.ToString(), truncated)))
                    {
                        return false;
                    }

                    line++;
                    text.Clear();
                    truncated = false;
                    lineHasChars = false;
                    pending = Pending.None;
                }

                if (c is '\r' or '\n')
                {
                    pending = c == '\r' ? Pending.Cr : Pending.Done;
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

                if (text.Length < MaxChars)
                {
                    text.Append(c);
                }
                else
                {
                    truncated = true;
                }
            }
        }

        counter?.Finish();

        // The last line: ended by a terminator that no character followed, or without one. An empty remainder after a final terminator is not a line.
        if (pending != Pending.None || lineHasChars)
        {
            onLine(new TextLine(line, lineStart, text.ToString(), truncated));
        }

        return true;
    }

    private enum Pending
    {
        None,
        Cr,
        Done,
    }
}
