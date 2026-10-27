// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The kind of a line that may become a stable line.</summary>
internal enum StableLineKind
{
    /// <summary>The header line.</summary>
    Header,

    /// <summary>A preamble line of kind keyValue.</summary>
    PreambleKey,

    /// <summary>A preamble comment or text line; used only with <c>--include-values</c>.</summary>
    PreambleText,
}

/// <summary>A physical line that a format profiler offers to the hint builder, with its text.</summary>
/// <param name="Line">The 1-based line.</param>
/// <param name="Kind">The kind.</param>
/// <param name="Key">The normalized key of a <see cref="StableLineKind.PreambleKey"/> line, otherwise null.</param>
/// <param name="Text">The physical line, line break excluded.</param>
internal sealed record StableLineSource(long Line, StableLineKind Kind, string? Key, string Text);
