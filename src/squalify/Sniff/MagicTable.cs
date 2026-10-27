// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>One required byte string of a magic entry.</summary>
/// <param name="Offset">Offset from the start of the file, unused when <paramref name="FromEnd"/> is set.</param>
/// <param name="Bytes">The bytes that must be found.</param>
/// <param name="FromEnd">True when <paramref name="Bytes"/> must be the last bytes of the file.</param>
internal sealed record MagicClause(int Offset, byte[] Bytes, bool FromEnd = false);

/// <summary>A named magic: every clause must match.</summary>
/// <param name="Name">The magic name that reaches the descriptor.</param>
/// <param name="Clauses">The clauses, all required.</param>
internal sealed record MagicEntry(string Name, IReadOnlyList<MagicClause> Clauses);

/// <summary>The ordered table of binary magics. The first matching entry wins.</summary>
internal static class MagicTable
{
    /// <summary>Number of leading bytes the sniffer keeps for matching.</summary>
    public const int HeadLength = 512;

    /// <summary>Number of trailing bytes the sniffer keeps for matching.</summary>
    public const int TailLength = 16;

    /// <summary>The entries, in matching order. The first match wins.</summary>
    public static IReadOnlyList<MagicEntry> Entries { get; } =
    [
        new("parquet", [Head(0, "PAR1"u8), new MagicClause(0, "PAR1"u8.ToArray(), FromEnd: true)]),
        new("stdf-le", [Head(0, [0x02, 0x00, 0x00, 0x0A])]),
        new("stdf-be", [Head(0, [0x00, 0x02, 0x00, 0x0A])]),
        new("gzip", [Head(0, [0x1F, 0x8B])]),
        new("zip", [Head(0, [0x50, 0x4B, 0x03, 0x04])]),
        new("zip", [Head(0, [0x50, 0x4B, 0x05, 0x06])]),
        new("xz", [Head(0, [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00])]),
        new("bzip2", [Head(0, "BZh"u8)]),
        new("zstd", [Head(0, [0x28, 0xB5, 0x2F, 0xFD])]),
        new("7z", [Head(0, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C])]),
        new("tar", [Head(257, "ustar"u8)]),
        new("ole2", [Head(0, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1])]),
        new("pdf", [Head(0, "%PDF-"u8)]),
        new("png", [Head(0, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])]),
        new("sqlite", [Head(0, "SQLite format 3\0"u8)]),
        new("hdf5", [Head(0, [0x89, 0x48, 0x44, 0x46, 0x0D, 0x0A, 0x1A, 0x0A])]),
        new("duckdb", [Head(8, "DUCK"u8)]),
        new("arrow", [Head(0, "ARROW1"u8)]),
        new("netcdf", [Head(0, [0x43, 0x44, 0x46, 0x01])]),
        new("netcdf", [Head(0, [0x43, 0x44, 0x46, 0x02])]),
    ];

    /// <summary>Finds the first entry that matches.</summary>
    /// <param name="head">The first bytes of the file (at most <see cref="HeadLength"/>).</param>
    /// <param name="tail">The last bytes of the file (at most <see cref="TailLength"/>).</param>
    /// <param name="length">The file length.</param>
    /// <returns>The magic name, or null.</returns>
    public static string? Match(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail, long length)
    {
        return Find(head, tail, length)?.Name;
    }

    /// <summary>Finds the first entry that matches.</summary>
    /// <param name="head">The first bytes of the file (at most <see cref="HeadLength"/>).</param>
    /// <param name="tail">The last bytes of the file (at most <see cref="TailLength"/>).</param>
    /// <param name="length">The file length.</param>
    /// <returns>The entry, or null.</returns>
    public static MagicEntry? Find(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail, long length)
    {
        foreach (var entry in Entries)
        {
            if (Matches(entry, head, tail, length))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// The hint for a matched entry: its name, and the offset and lower-case hex of its first clause that is anchored
    /// at the start of the file (the condition a consumer can test from the head of a file).
    /// </summary>
    /// <param name="entry">The matched entry.</param>
    /// <returns>The hint.</returns>
    public static MagicHint Describe(MagicEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var clause = entry.Clauses.First(static c => !c.FromEnd);
        return new MagicHint(entry.Name, clause.Offset, Convert.ToHexStringLower(clause.Bytes));
    }

    private static MagicClause Head(int offset, ReadOnlySpan<byte> bytes)
    {
        return new MagicClause(offset, bytes.ToArray());
    }

    private static bool Matches(MagicEntry entry, ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail, long length)
    {
        // A clause anchored at the end must not overlap a clause anchored at the start.
        var headEnd = 0L;
        foreach (var clause in entry.Clauses)
        {
            if (clause.FromEnd)
            {
                continue;
            }

            var end = (long)clause.Offset + clause.Bytes.Length;
            if (end > length || end > head.Length || !head.Slice(clause.Offset, clause.Bytes.Length).SequenceEqual(clause.Bytes))
            {
                return false;
            }

            headEnd = Math.Max(headEnd, end);
        }

        foreach (var clause in entry.Clauses)
        {
            if (!clause.FromEnd)
            {
                continue;
            }

            if (length - clause.Bytes.Length < headEnd || clause.Bytes.Length > tail.Length
                || !tail[^clause.Bytes.Length..].SequenceEqual(clause.Bytes))
            {
                return false;
            }
        }

        return true;
    }
}
