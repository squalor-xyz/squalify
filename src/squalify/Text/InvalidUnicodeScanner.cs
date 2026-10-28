// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The invalid units that a scan of a UTF-16 or UTF-32 file found.</summary>
/// <param name="Units">Invalid units: a lone surrogate, an odd trailing byte, an out-of-range UTF-32 value or a truncated trailing UTF-32 unit.</param>
/// <param name="Bytes">Bytes in those units.</param>
/// <param name="Offsets">Byte offsets (BOM included) where the first 10 units start, ascending.</param>
internal sealed record InvalidUnicode(long Units, long Bytes, IReadOnlyList<long> Offsets)
{
    /// <summary>No invalid unit.</summary>
    public static InvalidUnicode None { get; } = new(0, 0, Array.Empty<long>());
}

/// <summary>
/// Validates a UTF-16 or UTF-32 text file in one streaming pass with <see cref="TextDecoder"/>, which is the single place
/// that defines what a valid unit is. The result depends only on the bytes, not on how the stream splits its reads.
/// </summary>
internal static class InvalidUnicodeScanner
{
    private const int ChunkSize = 4096;

    /// <summary>Scans a source when the byte pass chose a UTF-16 or UTF-32 charset; any other result is <see cref="InvalidUnicode.None"/>.</summary>
    /// <param name="source">The source.</param>
    /// <param name="sniff">The byte-pass result.</param>
    /// <returns>The invalid units.</returns>
    public static InvalidUnicode Scan(IProfileSource source, SniffResult sniff)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sniff);
        if (sniff.Charset is not ("utf-16le" or "utf-16be" or "utf-32le" or "utf-32be"))
        {
            return InvalidUnicode.None;
        }

        using var stream = source.Open();
        var decoder = new TextDecoder(stream, sniff.Charset, sniff.BomLength);
        var chars = new char[ChunkSize];
        var offsets = new long[ChunkSize];
        while (decoder.Read(chars, offsets) > 0)
        {
        }

        return decoder.InvalidUnits == 0 ? InvalidUnicode.None : new InvalidUnicode(decoder.InvalidUnits, decoder.InvalidBytes, decoder.InvalidOffsets.ToArray());
    }
}
