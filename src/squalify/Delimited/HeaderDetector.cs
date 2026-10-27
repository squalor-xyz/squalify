// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>One record of the header sample: the first records of the data run.</summary>
internal sealed class SampleRecord
{
    /// <summary>The 1-based line where the record starts.</summary>
    public required long Line { get; init; }

    /// <summary>The byte offset where the record starts.</summary>
    public required long StartByte { get; init; }

    /// <summary>The raw field texts.</summary>
    public required string[] Cells { get; init; }

    /// <summary>For each field, the byte offset of its first character and the offset after its last; null when not captured.</summary>
    public long[]? Spans { get; init; }

    /// <summary>True when the record is a comment line.</summary>
    public required bool IsComment { get; init; }

    /// <summary>The number of fields.</summary>
    public int FieldCount => Cells.Length;

    /// <summary>The vote of each field, set once the decimal separator is known.</summary>
    public ValueVote[] Votes { get; set; } = [];

    /// <summary>The matched null-vocabulary entry of each field that voted null, in the vocabulary's spelling.</summary>
    public string?[] NullTokens { get; set; } = [];

    /// <summary>True when every field is null.</summary>
    public bool AllNull { get; set; }
}

/// <summary>The header decision for a sample.</summary>
internal sealed class HeaderDecision
{
    /// <summary>The sample index of the header record, or -1 when the table has no header.</summary>
    public int HeaderIndex { get; init; } = -1;

    /// <summary>The sample index of the units row, or -1.</summary>
    public int UnitsIndex { get; init; } = -1;

    /// <summary>True when the header was assumed because the table is all strings.</summary>
    public bool Assumed { get; init; }

    /// <summary>True when the header was chosen although it has a blank or repeated name.</summary>
    public bool Relaxed { get; init; }

    /// <summary>The evidence entry (stage <c>header</c>, topic <c>candidates</c>).</summary>
    public required Evidence Evidence { get; init; }
}

/// <summary>
/// Chooses the header, the annotation rows above it and the units row below it, from the first records of the data
/// run (<c>limits.headerSample</c>). Every record of the sample is a header candidate. Column types are the strict
/// join of the votes of the records below the candidate (the units row excluded, all-null records skipped). A
/// candidate is eligible when its fields are non-blank and pairwise distinct (ASCII case folding) and, in some column
/// whose type is not <c>string</c>, its value fails that type. Its score is nonEmpty x nonNumeric x unique x
/// (0.5 + 0.5 x contrast).
/// </summary>
internal static class HeaderDetector
{
    /// <summary>The <c>value</c> of the summary entry that ends the header evidence.</summary>
    public const string SummaryValue = "summary";

    private const int MaxUnitLength = 16;

    /// <summary>Casts the votes of every record, using the dialect's decimal separator.</summary>
    /// <param name="records">The sample.</param>
    /// <param name="k">The field count of the data run.</param>
    /// <param name="decimalSeparator">The decimal separator.</param>
    public static void Classify(IReadOnlyList<SampleRecord> records, int k, char decimalSeparator)
    {
        foreach (var record in records)
        {
            var votes = new ValueVote[record.FieldCount];
            var tokens = new string?[record.FieldCount];
            var allNull = record.FieldCount > 0;
            for (var c = 0; c < record.FieldCount; c++)
            {
                var result = ValueClassifier.Classify(record.Cells[c], decimalSeparator);
                votes[c] = result.Vote;
                tokens[c] = result.NullToken;
                if (result.Vote != ValueVote.Null)
                {
                    allNull = false;
                }
            }

            record.Votes = votes;
            record.NullTokens = tokens;
            record.AllNull = allNull;
        }
    }

    /// <summary>Decides the header and the units row.</summary>
    /// <param name="records">The sample, with votes (see <see cref="Classify"/>).</param>
    /// <param name="k">The field count of the data run.</param>
    /// <returns>The decision, with its evidence.</returns>
    public static HeaderDecision Detect(IReadOnlyList<SampleRecord> records, int k)
    {
        var n = records.Count;
        var below = BuildSuffixTypes(records, k);

        var details = new Detail[n];
        var bestIndex = -1;
        var bestScore = -1.0;
        var relaxedIndex = -1;
        var relaxedScore = 0.0;
        for (var i = 0; i < n; i++)
        {
            var record = records[i];
            if (record.IsComment)
            {
                details[i] = Detail.NotACandidate("comment line");
                continue;
            }

            if (record.FieldCount != k)
            {
                details[i] = Detail.NotACandidate(Ordinal.Invariant(record.FieldCount) + " fields");
                continue;
            }

            var types = TypesBelow(records, i, k, below);
            var detail = Evaluate(record, types, k);
            details[i] = detail;
            if (detail.Eligible && detail.Score > bestScore)
            {
                bestIndex = i;
                bestScore = detail.Score;
            }

            if (!detail.Eligible && detail.Fails > 0 && detail.NonNumeric == 1.0 && detail.Score > relaxedScore)
            {
                relaxedIndex = i;
                relaxedScore = detail.Score;
            }
        }

        var assumed = false;
        var relaxed = false;
        var header = bestIndex;
        if (header < 0 && relaxedIndex >= 0)
        {
            header = relaxedIndex;
            relaxed = true;
        }

        if (header < 0)
        {
            var first = FirstParticipating(records, k);
            if (first >= 0 && IsUntyped(below, first, k) && details[first].NonEmpty == 1.0 && details[first].Unique == 1.0)
            {
                header = first;
                assumed = true;
            }
        }

        // The evidence lists every eligible candidate with its sub-scores, the winner (which may be a relaxed or
        // assumed one), and one summary entry that counts the rest by reason.
        var candidates = new List<Candidate>();
        long eligibleCount = 0;
        long commentCount = 0;
        long fieldCountCount = 0;
        long namesCount = 0;
        long fitsCount = 0;
        for (var i = 0; i < n; i++)
        {
            var detail = details[i];
            if (detail.Eligible)
            {
                eligibleCount++;
            }
            else if (!detail.IsCandidate)
            {
                if (records[i].IsComment)
                {
                    commentCount++;
                }
                else
                {
                    fieldCountCount++;
                }
            }
            else if (detail.NonEmpty < 1.0 || detail.Unique < 1.0)
            {
                namesCount++;
            }
            else
            {
                fitsCount++;
            }

            if (detail.Eligible || i == header)
            {
                candidates.Add(new Candidate(Ordinal.Invariant(records[i].Line), detail.Score, detail.Describe(i == header && assumed, i == header && relaxed)));
            }
        }

        var ineligible = n - eligibleCount;
        candidates.Add(new Candidate(
            SummaryValue,
            0.0,
            "sampled=" + Ordinal.Invariant(n)
                + "; eligible=" + Ordinal.Invariant(eligibleCount)
                + "; ineligible=" + Ordinal.Invariant(ineligible)
                + " (comment line=" + Ordinal.Invariant(commentCount)
                + ", field count=" + Ordinal.Invariant(fieldCountCount)
                + ", blank or repeated names=" + Ordinal.Invariant(namesCount)
                + ", fits every typed column=" + Ordinal.Invariant(fitsCount) + ")"));

        var evidence = new Evidence("header", "candidates", header < 0 ? null : Ordinal.Invariant(records[header].Line), candidates);
        var units = -1;
        if (header >= 0 && header + 1 < n && records[header + 1] is { IsComment: false } next && next.FieldCount == k)
        {
            if (IsUnitsRow(next, below[header + 2], k))
            {
                units = header + 1;
            }
        }

        return new HeaderDecision { HeaderIndex = header, UnitsIndex = units, Assumed = assumed, Relaxed = relaxed, Evidence = evidence };
    }

    /// <summary>The strict join of two votes (the type lattice). A null vote is the identity.</summary>
    /// <param name="a">The first type.</param>
    /// <param name="b">The second type.</param>
    /// <returns>The joined type.</returns>
    public static ValueVote Join(ValueVote a, ValueVote b)
    {
        if (a == ValueVote.Null)
        {
            return b;
        }

        if (b == ValueVote.Null || a == b)
        {
            return a;
        }

        if ((a == ValueVote.Int && b == ValueVote.Float) || (a == ValueVote.Float && b == ValueVote.Int))
        {
            return ValueVote.Float;
        }

        if ((a == ValueVote.Date && b == ValueVote.DateTime) || (a == ValueVote.DateTime && b == ValueVote.Date))
        {
            return ValueVote.DateTime;
        }

        return ValueVote.String;
    }

    /// <summary>The type name written to the descriptor; <c>empty</c> for no votes.</summary>
    /// <param name="type">The joined type (<see cref="ValueVote.Null"/> for none).</param>
    /// <returns>The name.</returns>
    public static string TypeName(ValueVote type)
    {
        return type switch
        {
            ValueVote.Bool => "bool",
            ValueVote.Int => "int",
            ValueVote.Float => "float",
            ValueVote.Date => "date",
            ValueVote.DateTime => "datetime",
            ValueVote.Time => "time",
            ValueVote.String => "string",
            _ => "empty",
        };
    }

    /// <summary>
    /// Whether a record is a units row, given the column types below it. Null cells (blank or a null-vocabulary token)
    /// are neutral. Every non-null field must fail its column's type, be at most 16 characters and sit in a column whose
    /// type is not <c>string</c>, and at least one field must be non-null.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="types">The column types of the records below it.</param>
    /// <param name="k">The field count.</param>
    /// <returns>True for a units row.</returns>
    public static bool IsUnitsRow(SampleRecord record, ValueVote[] types, int k)
    {
        var nonNull = 0;
        for (var c = 0; c < k; c++)
        {
            var vote = record.Votes[c];
            if (vote == ValueVote.Null)
            {
                continue;
            }

            nonNull++;
            var type = types[c];
            if (type == ValueVote.Null || type == ValueVote.String)
            {
                return false;
            }

            if (Fits(type, vote) || ValueClassifier.Trim(record.Cells[c]).Length > MaxUnitLength)
            {
                return false;
            }
        }

        return nonNull >= 1;
    }

    private static bool Fits(ValueVote type, ValueVote vote)
    {
        return vote == ValueVote.Null || Join(type, vote) == type;
    }

    private static bool IsTyped(ValueVote type)
    {
        return type != ValueVote.Null && type != ValueVote.String;
    }

    private static bool Participates(SampleRecord record, int k)
    {
        return !record.IsComment && record.FieldCount == k && !record.AllNull;
    }

    // below[i] holds the column types over records i.. of the sample; below[n] and below[n + 1] are empty.
    private static ValueVote[][] BuildSuffixTypes(IReadOnlyList<SampleRecord> records, int k)
    {
        var n = records.Count;
        var below = new ValueVote[n + 2][];
        below[n] = new ValueVote[k];
        below[n + 1] = below[n];
        for (var i = n - 1; i >= 0; i--)
        {
            if (!Participates(records[i], k))
            {
                below[i] = below[i + 1];
                continue;
            }

            var types = (ValueVote[])below[i + 1].Clone();
            for (var c = 0; c < k; c++)
            {
                types[c] = Join(types[c], records[i].Votes[c]);
            }

            below[i] = types;
        }

        return below;
    }

    // The column types below a candidate: the records after it, without a units row that directly follows it.
    private static ValueVote[] TypesBelow(IReadOnlyList<SampleRecord> records, int i, int k, ValueVote[][] below)
    {
        var n = records.Count;
        if (i + 1 < n && records[i + 1] is { IsComment: false } next && next.FieldCount == k && IsUnitsRow(next, below[i + 2], k))
        {
            return below[i + 2];
        }

        return below[i + 1];
    }

    private static int FirstParticipating(IReadOnlyList<SampleRecord> records, int k)
    {
        for (var i = 0; i < records.Count; i++)
        {
            if (!records[i].IsComment && records[i].FieldCount == k)
            {
                return i;
            }
        }

        return -1;
    }

    // True when no column below the record has a type other than string (or none yet).
    private static bool IsUntyped(ValueVote[][] below, int index, int k)
    {
        var types = below[index + 1];
        for (var c = 0; c < k; c++)
        {
            if (IsTyped(types[c]))
            {
                return false;
            }
        }

        return true;
    }

    private static Detail Evaluate(SampleRecord record, ValueVote[] types, int k)
    {
        var nonBlank = 0;
        var nonNumeric = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var typedColumns = 0;
        var fails = 0;
        for (var c = 0; c < k; c++)
        {
            var text = ValueClassifier.Trim(record.Cells[c]);
            if (text.Length > 0)
            {
                nonBlank++;
            }

            seen.Add(AsciiFold(text));
            var vote = record.Votes[c];
            if (vote is not (ValueVote.Int or ValueVote.Float))
            {
                nonNumeric++;
            }

            if (IsTyped(types[c]))
            {
                typedColumns++;
                if (!Fits(types[c], vote))
                {
                    fails++;
                }
            }
        }

        var nonEmptyShare = nonBlank / (double)k;
        var nonNumericShare = nonNumeric / (double)k;
        var uniqueShare = seen.Count / (double)k;
        var contrast = typedColumns > 0 ? fails / (double)typedColumns : 0.0;
        var score = nonEmptyShare * nonNumericShare * uniqueShare * (0.5 + (0.5 * contrast));
        var eligible = nonBlank == k && seen.Count == k && fails > 0;
        return new Detail(true, eligible, score, nonEmptyShare, nonNumericShare, uniqueShare, contrast, fails);
    }

    private static string AsciiFold(ReadOnlySpan<char> text)
    {
        var chars = text.ToArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= 'A' and <= 'Z')
            {
                chars[i] = (char)(chars[i] + ('a' - 'A'));
            }
        }

        return new string(chars);
    }

    private readonly record struct Detail(
        bool IsCandidate,
        bool Eligible,
        double Score,
        double NonEmpty,
        double NonNumeric,
        double Unique,
        double Contrast,
        int Fails)
    {
        public static Detail NotACandidate(string why)
        {
            return new Detail(false, false, 0.0, 0.0, 0.0, 0.0, 0.0, 0) { Why = why };
        }

        private string? Why { get; init; }

        public string Describe(bool assumed, bool relaxed)
        {
            if (!IsCandidate)
            {
                return "not eligible: " + Why;
            }

            var numbers = "nonEmpty=" + CanonicalNumber.FormatFinite(NonEmpty)
                + "; nonNumeric=" + CanonicalNumber.FormatFinite(NonNumeric)
                + "; unique=" + CanonicalNumber.FormatFinite(Unique)
                + "; contrast=" + CanonicalNumber.FormatFinite(Contrast);
            if (assumed)
            {
                return "assumed: all-string table; " + numbers;
            }

            if (relaxed)
            {
                return "relaxed: blank or repeated names; " + numbers;
            }

            return (Eligible ? "eligible; " : "not eligible: " + (Fails > 0 ? "blank or repeated names; " : "fits every typed column; ")) + numbers;
        }
    }
}
