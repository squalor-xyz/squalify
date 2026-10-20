// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The whole-file facts that the byte pass counts. They do not depend on any threshold.</summary>
/// <param name="Length">The number of bytes read.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the content.</param>
/// <param name="Histogram">The 256-bin byte histogram.</param>
/// <param name="NulEven">NUL bytes at even offsets.</param>
/// <param name="NulOdd">NUL bytes at odd offsets.</param>
/// <param name="ControlBytes">Bytes 0x00-0x08, 0x0E-0x1F and 0x7F.</param>
/// <param name="HighBytes">Bytes at or above 0x80.</param>
/// <param name="C1Bytes">Bytes 0x80-0x9F.</param>
/// <param name="InvalidSequences">Invalid UTF-8 sequences, in the whole-file strict decode.</param>
/// <param name="InvalidBytes">Bytes in those sequences.</param>
/// <param name="InvalidOffsets">Byte offsets of the first 10 invalid sequences.</param>
/// <param name="Bom">The BOM name, or null.</param>
/// <param name="BomLength">The BOM length in bytes (0 when there is none).</param>
/// <param name="Magic">The named binary magic, or null.</param>
internal sealed record SniffFacts(
    long Length,
    string Sha256,
    long[] Histogram,
    long NulEven,
    long NulOdd,
    long ControlBytes,
    long HighBytes,
    long C1Bytes,
    long InvalidSequences,
    long InvalidBytes,
    IReadOnlyList<long> InvalidOffsets,
    string? Bom,
    int BomLength,
    string? Magic)
{
    /// <summary>The NUL byte count.</summary>
    public long Nul => Histogram[0];
}

/// <summary>The result of the byte pass: the facts, and the decision made from them (empty, text or binary).</summary>
internal sealed class SniffResult
{
    private SniffResult(SniffFacts facts, string kind, string? charset, IReadOnlyList<Evidence> evidence)
    {
        Facts = facts;
        Kind = kind;
        Charset = charset;
        Evidence = evidence;
    }

    /// <summary>The counted facts.</summary>
    public SniffFacts Facts { get; }

    /// <summary><c>empty</c>, <c>text</c> or <c>binary</c>.</summary>
    public string Kind { get; }

    /// <summary>The charset of a text file, or null.</summary>
    public string? Charset { get; }

    /// <summary>The evidence entries of the decision (stage <c>encoding</c>).</summary>
    public IReadOnlyList<Evidence> Evidence { get; }

    /// <summary>The file length.</summary>
    public long Length => Facts.Length;

    /// <summary>The BOM name, or null.</summary>
    public string? Bom => Facts.Bom;

    /// <summary>The BOM length in bytes.</summary>
    public int BomLength => Facts.BomLength;

    /// <summary>The named binary magic, or null.</summary>
    public string? Magic => Facts.Magic;

    /// <summary>
    /// The number of invalid UTF-8 sequences that keep an <c>ascii</c> or <c>utf-8</c> charset:
    /// <c>min(invalidSequences, ceil(length / perBytes))</c>, so at most 8 per file and at most 1 per 64 KiB, and
    /// a file shorter than 64 KiB tolerates one.
    /// </summary>
    /// <param name="length">The file length.</param>
    /// <param name="tolerance">The tolerance limits.</param>
    /// <returns>The number of tolerated invalid sequences.</returns>
    public static long AllowedInvalidSequences(long length, CharsetTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(tolerance);
        var perFile = tolerance.InvalidSequences;
        var perSize = tolerance.PerBytes <= 0 ? 0 : (length + tolerance.PerBytes - 1) / tolerance.PerBytes;
        return Math.Min(perFile, perSize);
    }

    /// <summary>Applies the rules of <c>docs/design.md</c> to the facts, in order.</summary>
    /// <param name="facts">The counted facts.</param>
    /// <param name="tolerance">The tolerated stray invalid UTF-8 sequences.</param>
    /// <returns>The decision.</returns>
    public static SniffResult Decide(SniffFacts facts, CharsetTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var length = facts.Length;
        var nul = facts.Nul;

        // Rule 1: a zero-length file is empty.
        if (length == 0)
        {
            return Finish(facts, "empty", null, "rule 1: zero-length file", null);
        }

        // Rule 2: a binary magic means binary.
        if (facts.Magic is { } magic)
        {
            return Finish(facts, "binary", null, F($"rule 2: binary magic {magic}"), null);
        }

        // Rule 3: a UTF-16 or UTF-32 byte order mark means text.
        if (facts.Bom is { } bom && bom != "utf-8")
        {
            return Finish(facts, "text", bom, F($"rule 3: {bom} byte order mark"), "byte order mark");
        }

        // Rule 4: a NUL-parity pattern means UTF-16 without a BOM.
        if (nul > 0 && nul * 10 >= length * 3)
        {
            if (facts.NulOdd * 10 >= nul * 9)
            {
                return Finish(facts, "text", "utf-16le", F($"rule 4: {nul} of {length} bytes are NUL, {facts.NulOdd} at odd offsets"), "NUL parity");
            }

            if (facts.NulEven * 10 >= nul * 9)
            {
                return Finish(facts, "text", "utf-16be", F($"rule 4: {nul} of {length} bytes are NUL, {facts.NulEven} at even offsets"), "NUL parity");
            }
        }

        // Rule 5: any other NUL, or control bytes above 5%, means binary.
        if (nul > 0)
        {
            return Finish(facts, "binary", null, F($"rule 5: {nul} NUL bytes"), null);
        }

        if (facts.ControlBytes * 20 > length)
        {
            return Finish(facts, "binary", null, F($"rule 5: {facts.ControlBytes} of {length} bytes are control bytes, above 5%"), null);
        }

        // Rules 6 and 7: text. The charset is ascii, utf-8 or latin-1.
        var allowed = AllowedInvalidSequences(length, tolerance);
        string charset;
        if (facts.HighBytes == 0)
        {
            charset = "ascii";
        }
        else if (facts.InvalidSequences == 0)
        {
            charset = "utf-8";
        }
        else if (facts.InvalidSequences <= allowed)
        {
            charset = facts.HighBytes == facts.InvalidBytes ? "ascii" : "utf-8";
        }
        else
        {
            charset = "latin-1";
        }

        return Finish(facts, "text", charset, F($"rule 6: no NUL bytes; {facts.ControlBytes} of {length} bytes are control bytes"), null, allowed);
    }

    private static string F(FormattableString text)
    {
        return FormattableString.Invariant(text);
    }

    private static SniffResult Finish(SniffFacts facts, string kind, string? charset, string classReason, string? charsetReason, long allowed = 0)
    {
        var evidence = new List<Evidence>();
        if (charset is not null)
        {
            evidence.Add(CharsetEvidence(facts, charset, charsetReason, allowed));
        }

        evidence.Add(ClassEvidence(kind, classReason));
        return new SniffResult(facts, kind, charset, evidence);
    }

    // Candidates are sorted ordinally by value: binary, empty, text.
    private static Evidence ClassEvidence(string chosen, string reason)
    {
        var candidates = new List<Candidate>();
        foreach (var value in new[] { "binary", "empty", "text" })
        {
            candidates.Add(value == chosen ? new Candidate(value, 1.0, reason) : new Candidate(value, 0.0, "not chosen"));
        }

        return new Evidence("encoding", "class", chosen, candidates);
    }

    // Candidates are sorted ordinally by value. Rules 3 and 4 name the one charset they imply; rule 6 weighs ascii, utf-8 and latin-1.
    private static Evidence CharsetEvidence(SniffFacts facts, string chosen, string? impliedReason, long allowed)
    {
        var candidates = new List<Candidate>();
        if (impliedReason is not null)
        {
            candidates.Add(new Candidate(chosen, 1.0, impliedReason));
            return new Evidence("encoding", "charset", chosen, candidates);
        }

        var invalid = facts.InvalidSequences;
        var tolerated = invalid <= allowed;
        var strayNote = F($"{invalid} invalid UTF-8 sequences within the tolerance of {allowed}");
        var exceedNote = F($"{invalid} invalid UTF-8 sequences exceed the tolerance of {allowed}");

        string asciiReason = chosen == "ascii"
            ? (invalid == 0 ? "no bytes at or above 0x80" : strayNote + "; no valid multi-byte sequence")
            : F($"{facts.HighBytes} bytes at or above 0x80");
        string utf8Reason = chosen == "utf-8"
            ? (invalid == 0 ? "valid UTF-8" : strayNote)
            : (invalid == 0 ? "no bytes at or above 0x80" : (tolerated ? "no valid multi-byte sequence" : exceedNote));
        string latin1Reason = chosen == "latin-1"
            ? exceedNote + (facts.C1Bytes > 0 ? F($"; {facts.C1Bytes} bytes in 0x80-0x9F: possibly windows-1252") : string.Empty)
            : "UTF-8 or ASCII decoding accepted";

        candidates.Add(new Candidate("ascii", chosen == "ascii" ? 1.0 : 0.0, asciiReason));
        candidates.Add(new Candidate("latin-1", chosen == "latin-1" ? 1.0 : 0.0, latin1Reason));
        candidates.Add(new Candidate("utf-8", chosen == "utf-8" ? 1.0 : 0.0, utf8Reason));
        return new Evidence("encoding", "charset", chosen, candidates);
    }
}
