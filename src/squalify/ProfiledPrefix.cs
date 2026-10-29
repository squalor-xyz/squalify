// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace squalor.squalify;

/// <summary>
/// A descriptor together with the exact bytes it describes: the result of <see cref="Profiler.ProfilePrefix"/>. Only
/// <see cref="Profiler.ProfilePrefix"/> creates one, so the descriptor describes <see cref="Prefix"/> by construction. Equality
/// is by reference (two results are never equal unless they are the same object), and <see cref="ToString"/> prints the sizes,
/// never the bytes.
/// </summary>
public sealed class ProfiledPrefix
{
    internal ProfiledPrefix(Descriptor descriptor, ReadOnlyMemory<byte> prefix)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
        Prefix = prefix;
        Scope = descriptor.Scope ?? throw new ArgumentException("A prefix has a scope.", nameof(descriptor));
    }

    /// <summary>The descriptor of <see cref="Prefix"/>. Its <c>file.size</c> is the length of the whole source.</summary>
    public Descriptor Descriptor { get; }

    /// <summary>
    /// The bytes that the descriptor describes: the first <c>Scope.BytesRead</c> bytes of the source. The profiler copies
    /// nothing and keeps no reference to them: the caller owns the memory, and writing to it changes neither the descriptor nor
    /// any other result. It is a view of a larger array, which may be a little longer than the prefix (see the user guide's
    /// bound) and stays in memory for as long as this memory or this result is held. Several threads may read it, as long as
    /// none writes to it.
    /// </summary>
    public ReadOnlyMemory<byte> Prefix { get; }

    /// <summary>
    /// The scope: the limit, the length of <see cref="Prefix"/> and whether the end of the source was reached. It is the same
    /// instance as <c>Descriptor.Scope</c> and is never null.
    /// </summary>
    public ScopeSection Scope { get; }

    /// <summary>A short description: the file name, the size of the prefix and the scope. It never prints the bytes.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"ProfiledPrefix {{ File = {Descriptor.File.Name}, Prefix = {Prefix.Length} bytes, Scope = {Scope} }}");
    }
}
