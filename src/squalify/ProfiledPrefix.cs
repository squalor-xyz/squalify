// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>A descriptor together with the exact bytes it describes: the result of <see cref="Profiler.ProfilePrefix"/>.</summary>
/// <param name="Descriptor">The descriptor of <paramref name="Prefix"/>. Its <c>file.size</c> is the length of the whole source.</param>
/// <param name="Prefix">The bytes that the descriptor describes: the first <c>Scope.BytesRead</c> bytes of the source.</param>
/// <param name="Scope">The scope: the limit, the length of <paramref name="Prefix"/> and whether the end of the source was reached.</param>
public sealed record ProfiledPrefix(Descriptor Descriptor, ReadOnlyMemory<byte> Prefix, ScopeSection Scope);
