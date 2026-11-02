// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The issue codes squalify writes, named once. Every call site that makes an <see cref="Issue"/> takes its code from here, and
/// <c>schema/descriptor-v1.schema.json</c> (<c>$defs.issueCode</c>) lists the same set. To add a code: add a field here, add it to
/// <see cref="All"/> in ordinal order, add it to the schema enum and to the tables in <c>docs/descriptor.md</c>; the schema tests fail
/// until all of those agree.
/// </summary>
/// <remarks>
/// The values are <c>static readonly</c> fields, not constants: a constant is inlined as a string literal at every use, and the schema
/// tests read the compiled code to find a call site that bypasses this class with a raw literal.
/// </remarks>
internal static class IssueCodes
{
    /// <summary>More block candidates than <c>limits.regions</c> allows.</summary>
    public static readonly string BlocksTruncated = "blocksTruncated";

    /// <summary>A comment line inside the data run.</summary>
    public static readonly string CommentInData = "commentInData";

    /// <summary>Two header names equal under ASCII case folding.</summary>
    public static readonly string DuplicateHeader = "duplicateHeader";

    /// <summary>A key that appears twice in one section of a key=value document.</summary>
    public static readonly string DuplicateKey = "duplicateKey";

    /// <summary>A blank header name.</summary>
    public static readonly string EmptyHeaderName = "emptyHeaderName";

    /// <summary>A block whose header differs from the first block's: later blocks are not profiled.</summary>
    public static readonly string HeterogeneousBlocks = "heterogeneousBlocks";

    /// <summary>A name in <c>options.includeValuesFor</c> that matches nothing.</summary>
    public static readonly string IncludeValuesForUnmatched = "includeValuesForUnmatched";

    /// <summary>Numeric values at the end of the network data or the noise data that do not make a whole block.</summary>
    public static readonly string IncompleteBlock = "incompleteBlock";

    /// <summary>Invalid UTF-16 or UTF-32 units.</summary>
    public static readonly string InvalidUnicode = "invalidUnicode";

    /// <summary>Invalid UTF-8 sequences in a file kept as ascii or utf-8.</summary>
    public static readonly string InvalidUtf8 = "invalidUtf8";

    /// <summary>More than one kind of line ending.</summary>
    public static readonly string MixedLineEndings = "mixedLineEndings";

    /// <summary>A name or key left out of the fingerprint projections.</summary>
    public static readonly string NameExcludedFromFingerprint = "nameExcludedFromFingerprint";

    /// <summary>A table with a header and no data record.</summary>
    public static readonly string NoDataRows = "noDataRows";

    /// <summary>A frequency in the network data or the noise data of a Touchstone file that is lower than the one before it.</summary>
    public static readonly string NonMonotonicFrequency = "nonMonotonicFrequency";

    /// <summary>The port count of a Touchstone file's extension differs from its <c>[Number of Ports]</c>.</summary>
    public static readonly string PortMismatch = "portMismatch";

    /// <summary>A record whose field count differs from the header's.</summary>
    public static readonly string RaggedRows = "raggedRows";

    /// <summary>A key=value document has more sections than <c>limits.regions</c> allows: the rest are counted and not held.</summary>
    public static readonly string SectionsTruncated = "sectionsTruncated";

    /// <summary>The delimited profiler declined a file as too irregular.</summary>
    public static readonly string TooIrregular = "tooIrregular";

    /// <summary>Every code, in ordinal order: the set that the schema enum must equal.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        BlocksTruncated,
        CommentInData,
        DuplicateHeader,
        DuplicateKey,
        EmptyHeaderName,
        HeterogeneousBlocks,
        IncludeValuesForUnmatched,
        IncompleteBlock,
        InvalidUnicode,
        InvalidUtf8,
        MixedLineEndings,
        NameExcludedFromFingerprint,
        NoDataRows,
        NonMonotonicFrequency,
        PortMismatch,
        RaggedRows,
        SectionsTruncated,
        TooIrregular,
    ];
}
