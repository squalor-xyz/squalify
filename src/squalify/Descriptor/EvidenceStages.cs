// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The evidence stages, named once. Every <see cref="Evidence"/> the profiler writes takes its stage from here, <see cref="Order"/> is the
/// pipeline order that <c>evidence[]</c> is sorted in, and <c>schema/descriptor-v1.schema.json</c> (<c>$defs.evidenceStage</c>) lists the same
/// set. To add a stage: add a field here, add it to <see cref="Order"/> at its place in the pipeline, add it to the schema enum and to
/// <c>docs/descriptor.md</c>. A stage that is not in <see cref="Order"/> is refused when the evidence is sorted.
/// </summary>
/// <remarks>
/// The values are <c>static readonly</c> fields, not constants, so that the schema tests can tell a use of this class from a raw literal
/// in the compiled code (see <see cref="IssueCodes"/>).
/// </remarks>
internal static class EvidenceStages
{
    /// <summary>The byte pass: the class and the charset.</summary>
    public static readonly string Encoding = "encoding";

    /// <summary>The format choice: the family.</summary>
    public static readonly string Format = "format";

    /// <summary>The dialect of a delimited file: delimiter, decimal separator, shape and blocks.</summary>
    public static readonly string Dialect = "dialect";

    /// <summary>The header choice.</summary>
    public static readonly string Header = "header";

    /// <summary>The unit conventions of the header names.</summary>
    public static readonly string Unit = "unit";

    /// <summary>The typing of the fields.</summary>
    public static readonly string Type = "type";

    /// <summary>The pipeline order: <c>evidence[]</c> is sorted by the place of the stage in this list, then by topic.</summary>
    public static readonly IReadOnlyList<string> Order = [Encoding, Format, Dialect, Header, Unit, Type];

    /// <summary>The place of a stage in the pipeline order.</summary>
    /// <param name="stage">The stage.</param>
    /// <returns>The index in <see cref="Order"/>.</returns>
    /// <exception cref="InvalidOperationException">The stage is not in <see cref="Order"/>.</exception>
    public static int Rank(string stage)
    {
        for (var i = 0; i < Order.Count; i++)
        {
            if (string.Equals(Order[i], stage, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"The evidence stage '{stage}' is not in EvidenceStages.Order.");
    }
}
