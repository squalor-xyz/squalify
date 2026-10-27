// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The fallback profile for any text file, scoring 0.1. It decodes the text and reports the line endings (CRLF, lone LF,
/// lone CR), the line count, the blank lines, the longest line, and one <c>unknown</c> region over the whole file.
/// A blank line is empty or holds only spaces and tabs. Line lengths count UTF-16 characters of the decoded text.
/// </summary>
internal sealed class GenericTextProfiler : IFormatProfiler
{
    private const int ChunkSize = 16 * 1024;

    /// <inheritdoc />
    public string Family => "text";

    /// <inheritdoc />
    public FormatScore Score(IProfileSource source, SniffResult sniff, ProfileContext context)
    {
        ArgumentNullException.ThrowIfNull(sniff);
        return sniff.Kind switch
        {
            "text" => new FormatScore(0.1, "text content"),
            "empty" => new FormatScore(0.0, "file is empty"),
            _ => new FormatScore(0.0, "not text: binary content"),
        };
    }

    /// <inheritdoc />
    public FormatProfile Profile(IProfileSource source, SniffResult sniff, ProfileContext context, FormatScore score)
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
}
