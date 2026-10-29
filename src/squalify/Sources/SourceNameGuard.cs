// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The guard on <see cref="IProfileSource.Name"/>: a path must never reach the descriptor. A backslash is refused on
/// every platform, also where it is a legal file name (macOS and Linux), so a descriptor name never holds one and the
/// same file name gives the same descriptor everywhere.
/// </summary>
internal static class SourceNameGuard
{
    /// <summary>Throws unless the name is a non-empty bare file name.</summary>
    /// <param name="name">The source name.</param>
    /// <exception cref="SqualifyException">The name is empty or contains <c>/</c> or a backslash; the message says which.</exception>
    public static void Check(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new SqualifyException("The source name must be a bare file name, and it is empty; a path never reaches the descriptor.");
        }

        if (name.Contains('/', StringComparison.Ordinal))
        {
            throw new SqualifyException("The source name must be a bare file name, with no directory part, and this one contains '/'; a path never reaches the descriptor.");
        }

        if (name.Contains('\\', StringComparison.Ordinal))
        {
            throw new SqualifyException("The source name must be a bare file name, and this one contains a backslash ('\\'): a backslash separates directories on Windows, so a descriptor name never holds one, whatever the platform. Rename the file to profile it.");
        }
    }
}
