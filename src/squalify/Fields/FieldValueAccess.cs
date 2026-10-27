// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Which values-tier members of a field profile may be written, decided by <see cref="ValuesGate"/> when the field's
/// accumulator is created. A member that is not allowed is never collected, so its literal values are not held in memory.
/// </summary>
/// <param name="Top">The most common values (<c>top</c>).</param>
/// <param name="Text">The lexical minimum and maximum (<c>text</c>).</param>
/// <param name="ConstantStatistics">The numeric and temporal statistics of a constant field.</param>
internal readonly record struct FieldValueAccess(bool Top, bool Text, bool ConstantStatistics)
{
    /// <summary>No values-tier member may be written.</summary>
    public static FieldValueAccess None => default;

    /// <summary>Every values-tier member may be written.</summary>
    public static FieldValueAccess All => new(true, true, true);
}
