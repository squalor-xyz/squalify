// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// What a format family adds to the fingerprint projections. <see cref="Structure"/>, <see cref="Layout"/> and
/// <see cref="Typed"/> write members into the open projection object, right after <c>family</c>; <see cref="Typed"/>
/// writes only what the typed projection adds to the layout members.
/// </summary>
/// <param name="Structure">Members of the <c>structure</c> projection, or null.</param>
/// <param name="Layout">Members of the <c>layout</c> projection (and so of <c>typed</c>), or null.</param>
/// <param name="Typed">Further members of the <c>typed</c> projection only, or null.</param>
/// <param name="Names">Names for the <c>names</c> projection, case-folded; the projection sorts and de-duplicates them.</param>
/// <param name="Excluded">Names and keys that the contribution left out because they hold a control character or U+FFFD.</param>
internal sealed record FingerprintContribution(
    Action<ProjectionJson>? Structure,
    Action<ProjectionJson>? Layout,
    Action<ProjectionJson>? Typed,
    IReadOnlyList<string> Names,
    long Excluded)
{
    /// <summary>A contribution that adds nothing.</summary>
    public static FingerprintContribution None { get; } = new(null, null, null, Array.Empty<string>(), 0);
}

/// <summary>The seam through which a format family adds its part to the fingerprint projections.</summary>
internal interface IFingerprintContributor
{
    /// <summary>The family name this contributor serves, one of <see cref="FormatRegistry.Order"/>.</summary>
    string Family { get; }

    /// <summary>Builds the family's contribution. It must be value-free and deterministic.</summary>
    /// <param name="input">The projection input.</param>
    /// <returns>The contribution.</returns>
    FingerprintContribution Contribute(ProjectionInput input);
}

/// <summary>The registered contributors. A later format slice adds its contributor here.</summary>
internal static class FingerprintContributors
{
    private static readonly IFingerprintContributor[] All = [new DelimitedFingerprint()];

    /// <summary>The contributor of a family.</summary>
    /// <param name="family">The family name.</param>
    /// <returns>The contributor, or null when the family adds nothing.</returns>
    public static IFingerprintContributor? For(string family)
    {
        foreach (var contributor in All)
        {
            if (string.Equals(contributor.Family, family, StringComparison.Ordinal))
            {
                return contributor;
            }
        }

        return null;
    }
}
