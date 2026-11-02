// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace squalor.squalify;

/// <summary>What the head of a file shows of its Touchstone shape.</summary>
/// <param name="Reason">Why the file is not a Touchstone file, or null when it may be one.</param>
/// <param name="FirstLine">The line of the option line or <c>[Version]</c>.</param>
/// <param name="KeywordPorts">The port count of <c>[Number of Ports]</c>, or null.</param>
/// <param name="DataValues">The number of values on the first numeric data line, or -1 when the head holds none.</param>
internal sealed record TouchstoneHead(string? Reason, long FirstLine, long? KeywordPorts, int DataValues);

/// <summary>
/// The Touchstone profiler (v1 and v2): an option line, a port count, network data read as a flat stream of numbers, noise data, and the
/// keywords of Version 2. Scoring reads only the head of the file; the profile is one streaming pass. See <c>docs/design.md</c>,
/// "Touchstone", and <c>docs/descriptor.md</c>, "format.dialect by family".
/// </summary>
internal sealed class TouchstoneProfiler : IFormatProfiler
{
    /// <summary>The score of a file whose extension names a Touchstone file and whose first non-comment line is an option line or <c>[Version]</c>.</summary>
    public const double ExtensionScore = 0.9;

    /// <summary>The score of such a file with another extension, when numeric data follows the option line.</summary>
    public const double ContentScore = 0.7;

    /// <summary>The most ports of a file that is read as Touchstone: a full matrix of 100 ports is 20,001 fields.</summary>
    public const long MaxPorts = 100;

    // The most lines of the head that scoring reads; a Version 2 file has a dozen keyword lines before the data.
    private const int HeadLines = 2000;

    /// <inheritdoc />
    public string Family => "touchstone";

    /// <inheritdoc />
    public FormatScore Score(IProfileSource source, SniffResult sniff, ProfileContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sniff);
        ArgumentNullException.ThrowIfNull(context);
        if (sniff.Kind != "text" || sniff.Charset is null)
        {
            return new FormatScore(0.0, sniff.Kind == "empty" ? "file is empty" : "not text: binary content");
        }

        var head = ReadHead(source, sniff);
        if (head.Reason is not null)
        {
            return new FormatScore(0.0, head.Reason);
        }

        var extensionPorts = TouchstoneSyntax.PortsFromExtension(source.Name, out var isTs);
        var extension = extensionPorts is not null || isTs;
        var ports = head.KeywordPorts ?? extensionPorts ?? InferPorts(head.DataValues);
        if (ports is null)
        {
            return new FormatScore(0.0, "the port count is not known");
        }

        if (ports > MaxPorts)
        {
            return new FormatScore(0.0, "more than " + Ordinal.Invariant(MaxPorts) + " ports");
        }

        var line = Ordinal.Invariant(head.FirstLine);
        if (extension)
        {
            return new FormatScore(ExtensionScore, "Touchstone extension, option line or [Version] on line " + line);
        }

        return head.DataValues > 0
            ? new FormatScore(ContentScore, "option line or [Version] on line " + line + ", then numeric data")
            : new FormatScore(0.0, "no Touchstone extension, no numeric data after the option line");
    }

    /// <inheritdoc />
    public FormatProfile Profile(IProfileSource source, SniffResult sniff, ProfileContext context, FormatScore score)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sniff);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(score);
        if (sniff.Charset is null)
        {
            throw new InvalidOperationException("The Touchstone profiler needs a text file.");
        }

        var pass = new TouchstonePass(source.Name, sniff, context);
        var counter = new LineCounter();
        LineScan.Scan(source, sniff, counter, pass.OnLine);
        return pass.Build(counter);
    }

    /// <summary>The number of ports that the values on a first data line show, for a file whose name and keywords give none: 3 values are one port, 9 are two.</summary>
    /// <param name="values">The number of values.</param>
    /// <returns>The port count, or null.</returns>
    internal static long? InferPorts(int values)
    {
        return values switch
        {
            3 => 1,
            9 => 2,
            _ => null,
        };
    }

    // Reads lines until the first numeric data line, the end of the file, or the head limit, and says whether the file may be a Touchstone file.
    private static TouchstoneHead ReadHead(IProfileSource source, SniffResult sniff)
    {
        string? reason = null;
        long firstLine = 0;
        long? keywordPorts = null;
        var dataValues = -1;
        var versionSeen = false;
        var networkSeen = false;
        var optionSeen = false;
        var count = 0;
        LineScan.Scan(source, sniff, null, line =>
        {
            if (++count > HeadLines)
            {
                return false;
            }

            if (line.Truncated)
            {
                reason = "a line is too long to be Touchstone data";
                return false;
            }

            if (TouchstoneSyntax.IsComment(line.Text) || ValueClassifier.Trim(line.Text).IsEmpty)
            {
                return true;
            }

            var content = TouchstoneSyntax.Content(line.Text);
            if (firstLine == 0)
            {
                // The first line that is not a comment is the option line (Version 1) or [Version] (Version 2).
                if (content.Length > 0 && content[0] == '#' && TouchstoneSyntax.ParseOption(content) is not null)
                {
                    firstLine = line.Number;
                    optionSeen = true;
                    return true;
                }

                if (TouchstoneSyntax.ParseKeyword(content, out _) == TouchstoneKeyword.Version)
                {
                    firstLine = line.Number;
                    versionSeen = true;
                    return true;
                }

                reason = "first line is not an option line or [Version]";
                return false;
            }

            if (content.Length > 0 && content[0] == '#')
            {
                optionSeen = true;
                return true;
            }

            if (TouchstoneSyntax.ParseKeyword(content, out var arguments) is { } keyword)
            {
                switch (keyword)
                {
                    case TouchstoneKeyword.Version:
                        versionSeen = true;
                        break;
                    case TouchstoneKeyword.NumberOfPorts when TouchstoneSyntax.TryCount(arguments, out var ports):
                        keywordPorts = ports;
                        break;
                    case TouchstoneKeyword.NetworkData:
                        networkSeen = true;
                        break;
                }

                return true;
            }

            // A Version 2 file has keywords and their arguments before [Network Data]; its data lines come after it.
            if (versionSeen && !networkSeen)
            {
                return true;
            }

            var tokens = 0;
            var rest = content;
            while (TouchstoneSyntax.NextToken(ref rest, out var token))
            {
                if (!TouchstoneSyntax.TryNumber(token, out _))
                {
                    reason = "the first data line is not numeric";
                    return false;
                }

                tokens++;
            }

            dataValues = tokens;
            return false;
        });

        if (reason is null && firstLine == 0)
        {
            reason = "no option line or [Version]";
        }

        if (reason is null && !optionSeen && !versionSeen)
        {
            reason = "no option line or [Version]";
        }

        return new TouchstoneHead(reason, firstLine, keywordPorts, dataValues);
    }
}
