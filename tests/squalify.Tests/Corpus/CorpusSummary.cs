// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;

namespace squalor.squalify.Tests;

/// <summary>
/// The tally of a private-corpus run: entries that passed, failed, or had no golden. An entry with no golden is
/// profiled and checked for determinism but compared with nothing, so it is not counted as passed. With
/// <c>requireGolden</c> such an entry fails the run. The summary is written to <c>.squalify-corpus/summary.txt</c>
/// inside the corpus directory, where <c>scripts/corpus-check.sh</c> reads it.
/// </summary>
internal sealed class CorpusSummary
{
    /// <summary>The summary file's path under the corpus directory, with <c>/</c> separators.</summary>
    public const string RelativePath = ".squalify-corpus/summary.txt";

    private readonly bool requireGolden;
    private readonly List<string> noGoldenEntries = new();

    /// <summary>Creates an empty tally.</summary>
    /// <param name="requireGolden">True when an entry with no golden fails the run.</param>
    public CorpusSummary(bool requireGolden)
    {
        this.requireGolden = requireGolden;
    }

    /// <summary>Entries that were profiled, deterministic and matched their golden.</summary>
    public int Passed { get; private set; }

    /// <summary>Entries that threw, were not deterministic, or differed from their golden.</summary>
    public int Failures { get; private set; }

    /// <summary>Entries with no golden next to the file.</summary>
    public int NoGolden => noGoldenEntries.Count;

    /// <summary>The entries with no golden, in the order they were added.</summary>
    public IReadOnlyList<string> NoGoldenEntries => noGoldenEntries;

    /// <summary>True when the run fails: an entry failed, or an entry has no golden and a golden is required.</summary>
    public bool IsFailure => Failures > 0 || (requireGolden && NoGolden > 0);

    /// <summary>0 when the run is not a failure, otherwise 1.</summary>
    public int ExitCode => IsFailure ? 1 : 0;

    /// <summary>The one-line summary: the three counts, then what they mean for the run.</summary>
    public string Line
    {
        get
        {
            var line = string.Create(CultureInfo.InvariantCulture, $"corpus: {Passed} passed, {Failures} failed, {NoGolden} no golden");
            if (Failures > 0)
            {
                line += $"; FAILED: {Failures} {(Failures == 1 ? "entry" : "entries")} failed";
            }

            if (NoGolden > 0)
            {
                var one = NoGolden == 1;
                var who = $"{NoGolden} {(one ? "entry has" : "entries have")} no golden";
                line += requireGolden
                    ? $"; FAILED: --require-golden is set and {who}"
                    : $"; {who} and {(one ? "is" : "are")} not passed (--require-golden makes that a failure)";
            }

            return line;
        }
    }

    /// <summary>Counts one entry's result.</summary>
    /// <param name="entry">The entry's path relative to the corpus directory.</param>
    /// <param name="result">The result of <see cref="CorpusRunner.Check(string, bool)"/>.</param>
    public void Add(string entry, CorpusResult result)
    {
        switch (result.Outcome)
        {
            case CorpusOutcome.Pass:
                Passed++;
                break;
            case CorpusOutcome.NoGolden:
                noGoldenEntries.Add(entry);
                break;
            default:
                Failures++;
                break;
        }
    }

    /// <summary>
    /// Writes the summary file under the corpus directory (the entries with no golden, then <see cref="Line"/>), and
    /// nothing anywhere else.
    /// </summary>
    /// <param name="root">The corpus directory.</param>
    public void WriteTo(string root)
    {
        var text = new StringBuilder();
        foreach (var entry in noGoldenEntries)
        {
            text.Append("no golden: ").Append(entry).Append('\n');
        }

        text.Append(Line).Append('\n');
        var path = Path.Combine(root, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
}
