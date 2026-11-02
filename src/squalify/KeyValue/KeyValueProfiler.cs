// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>What the scoring pass of the key=value profiler learned for the profile pass.</summary>
/// <param name="DistinctKeys">The number of different keys, which is the number of fields.</param>
internal sealed record KeyValueStructure(long DistinctKeys);

/// <summary>
/// The profiler for whole-file <c>key=value</c> and <c>key: value</c> documents, with optional <c>[section]</c> headers. It scores
/// <see cref="Score"/> when at least 80 percent of the lines that are neither blank nor comments are pairs or section headers, there are at
/// least two pairs, and no run of the lines that are not pairs looks like a table. That is above the delimited profiler's highest score (0.7),
/// so a document of <c>Lot = A12</c> lines, which the whitespace delimiter would read as a table, is a key=value document; and a table with
/// a key=value preamble is not one, because its table is a run. See <c>docs/design.md</c>, "Key=value / INI documents".
/// </summary>
internal sealed class KeyValueProfiler : IFormatProfiler
{
    /// <summary>The score of a key=value document.</summary>
    public const double DocumentScore = 0.8;

    /// <summary>The most different keys a document may have; a document with more is not read as one (each key is a field).</summary>
    public const long MaxKeys = 10_000;

    // A run of lines that are not pairs and have the same number of fields under one delimiter is table-like at this length: the same
    // thresholds as the delimited profiler's viable runs (two records, six for runs of white space).
    private const int TableRun = 2;
    private const int WhitespaceTableRun = 6;

    private static readonly char[] Delimiters = [',', '\t', ';', '|', '\0'];

    /// <inheritdoc />
    public string Family => "keyValue";

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

        long lines = 0;
        long pairs = 0;
        long sections = 0;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var sectionKeys = new HashSet<string>(StringComparer.Ordinal);
        long repeats = 0;
        var runK = new int[Delimiters.Length];
        var runLength = new int[Delimiters.Length];
        var tableLike = false;
        var tooManyKeys = false;
        LineScan.Scan(source, sniff, null, line =>
        {
            var kind = KeyValueSyntax.Classify(line.Text);
            switch (kind.Kind)
            {
                case KeyValueLineKind.Blank or KeyValueLineKind.Comment:
                    return true;
                case KeyValueLineKind.Pair:
                    lines++;
                    pairs++;
                    keys.Add(kind.Name!);
                    if (!sectionKeys.Add(kind.Name!))
                    {
                        repeats++;
                    }

                    Array.Clear(runLength);
                    if (keys.Count > MaxKeys)
                    {
                        tooManyKeys = true;
                        return false;
                    }

                    return true;
                case KeyValueLineKind.Section:
                    lines++;
                    sections++;
                    sectionKeys.Clear();
                    Array.Clear(runLength);
                    return true;
                default:
                    lines++;
                    for (var d = 0; d < Delimiters.Length; d++)
                    {
                        var k = KeyValueSyntax.FieldCount(line.Text, Delimiters[d]);
                        if (k >= 2 && k == runK[d] && runLength[d] > 0)
                        {
                            runLength[d]++;
                        }
                        else
                        {
                            runK[d] = k;
                            runLength[d] = k >= 2 ? 1 : 0;
                        }

                        if (runLength[d] >= (Delimiters[d] == '\0' ? WhitespaceTableRun : TableRun))
                        {
                            tableLike = true;
                        }
                    }

                    // A table is decided by its run, however long the rest of the file is.
                    return !tableLike;
            }
        });

        if (tableLike)
        {
            return new FormatScore(0.0, "table-like run of lines that are not key/value lines");
        }

        if (tooManyKeys)
        {
            return new FormatScore(0.0, "more than " + Ordinal.Invariant(MaxKeys) + " different keys");
        }

        if (pairs < 2)
        {
            return new FormatScore(0.0, "fewer than 2 key/value lines");
        }

        var counts = Ordinal.Invariant(pairs) + " key/value lines, " + Ordinal.Invariant(sections) + " section headers, " + Ordinal.Invariant(lines) + " lines in all";
        if ((pairs + sections) * 5 < lines * 4)
        {
            return new FormatScore(0.0, "key/value lines and section headers below 80 percent of the lines");
        }

        // A key identifies a value: a file in which most keys repeat inside their section is a log or a list of records, not a document.
        if (repeats * 2 > pairs)
        {
            return new FormatScore(0.0, "most keys repeat inside their section");
        }

        // The last word is the delimited profiler's: a table that a comma, a tab, a semicolon or a bar gives (a record whose first cell holds
        // a colon reads as a pair) stays a table. Only files that pass the checks above pay for the pass, and white space does not count: a
        // document of "Lot = A12" lines is read as a table of three columns by the white-space delimiter, which is what this profiler is for.
        if (new DelimitedProfiler().Score(source, sniff, context).State is DelimitedStructure { Selection.Best: { } best } && best.Kind != DelimiterKind.Whitespace && best.Shape != DegenerateShape.SingleColumn)
        {
            return new FormatScore(0.0, "table-like: a delimiter other than white space gives a steady run");
        }

        return new FormatScore(DocumentScore, counts, new KeyValueStructure(keys.Count));
    }

    /// <inheritdoc />
    public FormatProfile Profile(IProfileSource source, SniffResult sniff, ProfileContext context, FormatScore score)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sniff);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(score);
        if (score.State is not KeyValueStructure structure)
        {
            throw new InvalidOperationException("The key=value profiler needs the structure from its own scoring pass.");
        }

        var pass = new KeyValuePass(structure, context);
        var counter = new LineCounter();
        LineScan.Scan(source, sniff, counter, pass.OnLine);
        return pass.Build(counter, sniff.Length);
    }
}
