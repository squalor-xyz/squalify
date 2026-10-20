// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The fallback profile for any binary file: the byte histogram, the byte-class counts and the entropy. It scores 0.1,
/// or 0.2 when the file has a named magic. Block entropy and segments come with sq-10.
/// </summary>
internal sealed class GenericBinaryProfiler : IFormatProfiler
{
    /// <inheritdoc />
    public string Family => "binary";

    /// <inheritdoc />
    public (double Score, string Reason) Score(SniffResult sniff)
    {
        ArgumentNullException.ThrowIfNull(sniff);
        return sniff.Kind switch
        {
            "binary" when sniff.Magic is { } magic => (0.2, "binary content; named magic " + magic),
            "binary" => (0.1, "binary content; no named magic"),
            "empty" => (0.0, "file is empty"),
            _ => (0.0, "not binary: text content"),
        };
    }

    /// <inheritdoc />
    public FormatProfile Profile(IProfileSource source, SniffResult sniff, ProfileContext context)
    {
        ArgumentNullException.ThrowIfNull(sniff);
        var histogram = sniff.Facts.Histogram;
        long printable = 0;
        for (var b = 0x20; b <= 0x7E; b++)
        {
            printable += histogram[b];
        }

        var bytes = new ByteStats(
            histogram,
            Nul: histogram[0],
            PrintableAscii: printable,
            High: sniff.Facts.HighBytes,
            Entropy: ByteEntropy.Bits(histogram, sniff.Length));
        return new FormatProfile(
            Dialect: null,
            Layout: new LayoutSection(Array.Empty<Region>()),
            LineEndings: new LineEndings(0, 0, 0),
            Issues: Array.Empty<Issue>(),
            Evidence: Array.Empty<Evidence>(),
            Binary: new BinarySection(bytes));
    }
}
