// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Something that squalify can profile: a named sequence of bytes that can be opened again for every pass.
/// Only <see cref="Name"/> (a file name, never a path) reaches the descriptor.
/// </summary>
public interface IProfileSource
{
    /// <summary>
    /// The file name, without any directory: not empty, and containing neither <c>/</c> nor <c>\</c>.
    /// <see cref="Profiler"/> rejects any other name with a <see cref="SqualifyException"/>, because a path must never reach the descriptor.
    /// </summary>
    string Name { get; }

    /// <summary>The length in bytes.</summary>
    long Length { get; }

    /// <summary>Opens a new stream over the bytes, positioned at the start. The caller disposes it.</summary>
    /// <returns>A readable stream.</returns>
    /// <exception cref="SqualifyException">The source cannot be opened.</exception>
    Stream Open();
}
