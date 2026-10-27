// SPDX-License-Identifier: Apache-2.0
using System.Text;

namespace squalor.squalify;

/// <summary>
/// Reads chosen physical lines of a text file in one streaming pass that stops after the last wanted line. Lines end at
/// CRLF, LF or CR, as in <see cref="TextScanner"/>, so the line numbers agree with the record tokenizer's.
/// </summary>
internal static class LineReader
{
    private const int ChunkSize = 16 * 1024;

    /// <summary>Reads the wanted lines.</summary>
    /// <param name="source">The source; it is opened once.</param>
    /// <param name="sniff">The byte-pass result of a text file.</param>
    /// <param name="lines">The 1-based line numbers wanted.</param>
    /// <returns>Each wanted line that exists, line break excluded, by line number.</returns>
    public static Dictionary<long, string> Read(IProfileSource source, SniffResult sniff, IEnumerable<long> lines)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sniff);
        ArgumentNullException.ThrowIfNull(lines);
        var result = new Dictionary<long, string>();
        var wanted = new HashSet<long>(lines);
        if (wanted.Count == 0 || sniff.Charset is null)
        {
            return result;
        }

        var last = wanted.Max();
        var line = 1L;
        var current = wanted.Contains(line) ? new StringBuilder() : null;
        var pendingCr = false;
        var chars = new char[ChunkSize];
        var offsets = new long[ChunkSize];
        using var stream = source.Open();
        var decoder = new TextDecoder(stream, sniff.Charset, sniff.BomLength);
        int n;
        while ((n = decoder.Read(chars, offsets)) > 0)
        {
            for (var i = 0; i < n; i++)
            {
                var c = chars[i];
                if (pendingCr)
                {
                    pendingCr = false;
                    if (c == '\n')
                    {
                        continue;
                    }
                }

                if (c is '\r' or '\n')
                {
                    if (current is not null)
                    {
                        result[line] = current.ToString();
                    }

                    line++;
                    if (line > last)
                    {
                        return result;
                    }

                    current = wanted.Contains(line) ? new StringBuilder() : null;
                    pendingCr = c == '\r';
                    continue;
                }

                current?.Append(c);
            }
        }

        // A last line without a terminator; an empty remainder after a final terminator is not a line.
        if (current is { Length: > 0 })
        {
            result[line] = current.ToString();
        }

        return result;
    }
}
