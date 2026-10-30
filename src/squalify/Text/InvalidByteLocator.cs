// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Places invalid bytes: the 1-based line and the layout region of each reported byte offset. The offsets come from the byte
/// pass (invalid UTF-8) or from <see cref="TextDecoder"/> (invalid UTF-16 and UTF-32 units); they are file offsets, BOM included.
/// </summary>
internal static class InvalidByteLocator
{
    /// <summary>The region name of a byte that lies in no layout region.</summary>
    public const string OtherRegion = "other";

    private const int ChunkSize = 16 * 1024;

    /// <summary>
    /// The 1-based line of each offset, in the line numbering of <c>layout.regions[].lines</c> (CRLF, LF or CR ends a line).
    /// It reads the source again from its start up to the last offset, and only for a file that has invalid bytes.
    /// </summary>
    /// <param name="source">The source that was profiled.</param>
    /// <param name="sniff">The byte-pass result of a text file.</param>
    /// <param name="offsets">Byte offsets, ascending.</param>
    /// <returns>One line per offset, in order.</returns>
    public static IReadOnlyList<long> Lines(IProfileSource source, SniffResult sniff, IReadOnlyList<long> offsets)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sniff);
        ArgumentNullException.ThrowIfNull(offsets);
        var lines = new long[offsets.Count];
        if (offsets.Count == 0 || sniff.Charset is null)
        {
            return lines;
        }

        var next = 0;
        var line = 1L;
        var pendingCr = false;
        var chars = new char[ChunkSize];
        var at = new long[ChunkSize];
        using var stream = source.Open();
        var decoder = new TextDecoder(stream, sniff.Charset, sniff.BomLength);
        int n;
        while ((n = decoder.Read(chars, at)) > 0)
        {
            for (var i = 0; i < n; i++)
            {
                var c = chars[i];
                var onLine = line;
                if (pendingCr)
                {
                    pendingCr = false;
                    if (c == '\n')
                    {
                        // The LF of a CRLF ends the line that the CR ended.
                        onLine = line - 1;
                        c = ' ';
                    }
                }

                // An offset belongs to the first character that starts at or after it: that is the U+FFFD that stands for the invalid unit.
                while (next < lines.Length && at[i] >= offsets[next])
                {
                    lines[next++] = onLine;
                }

                if (next == lines.Length)
                {
                    return lines;
                }

                if (c is '\r' or '\n')
                {
                    line++;
                    pendingCr = c == '\r';
                }
            }
        }

        // An offset at or past the last character (a truncated trailing unit) is on the last line.
        while (next < lines.Length)
        {
            lines[next++] = line;
        }

        return lines;
    }

    /// <summary>
    /// The name of the layout region that each offset falls in: the kind of the region whose byte range holds it
    /// (<c>preamble</c>, <c>annotation</c>, <c>header</c>, <c>units</c>, <c>data</c>, <c>footer</c> or <c>unknown</c>). In a file with
    /// several blocks it is the kind of the block's child region, never <c>block</c>. A byte that lies in no region (a gap in the
    /// layout, a block that has no child holding it, or a layout with no regions) is <see cref="OtherRegion"/>.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="offsets">Byte offsets.</param>
    /// <returns>One name per offset, in order.</returns>
    public static IReadOnlyList<string> RegionNames(LayoutSection layout, IReadOnlyList<long> offsets)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(offsets);
        var names = new string[offsets.Count];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = Name(layout.Regions, offsets[i]);
        }

        return names;
    }

    private static string Name(IReadOnlyList<Region> regions, long offset)
    {
        foreach (var region in regions)
        {
            if (offset < region.StartByte || offset >= region.EndByte)
            {
                continue;
            }

            return region.Kind == "block" ? Name(region.Regions ?? [], offset) : region.Kind;
        }

        return OtherRegion;
    }
}
