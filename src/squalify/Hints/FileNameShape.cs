// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The shape mask of a file name (<c>hints.fileNameShape</c>). It is <see cref="ShapeMask"/>, the one implementation
/// shared with the field shapes, so the rules are written once: see <c>docs/design.md</c>, "Shape masks".
/// </summary>
internal static class FileNameShape
{
    /// <summary>Maps a name to its shape mask.</summary>
    /// <param name="name">The file name.</param>
    /// <returns>The mask.</returns>
    public static string Of(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ShapeMask.Of(name);
    }
}
