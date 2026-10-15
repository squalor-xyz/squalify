// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace squalor.squalify;

/// <summary>
/// xxHash64 (the 64-bit variant of xxHash by Yann Collet), implemented in-repo so sketches do not depend on a
/// package and never use <see cref="string.GetHashCode()"/>. Multi-byte reads are little-endian, so the result
/// is the same on every CPU. Sketches use seed 0 over the UTF-8 bytes of a value.
/// </summary>
internal static class XxHash64
{
    private const ulong Prime1 = 11400714785074694791UL;
    private const ulong Prime2 = 14029467366897019727UL;
    private const ulong Prime3 = 1609587929392839161UL;
    private const ulong Prime4 = 9650029242287828579UL;
    private const ulong Prime5 = 2870177450012600261UL;

    /// <summary>Hashes the UTF-8 bytes of a string with seed 0.</summary>
    /// <param name="text">The text. A lone surrogate is encoded as U+FFFD, as <see cref="Encoding.UTF8"/> does.</param>
    /// <returns>The 64-bit hash.</returns>
    public static ulong HashString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Hash(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Hashes bytes.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="seed">The seed. Sketches use 0.</param>
    /// <returns>The 64-bit hash.</returns>
    public static ulong Hash(ReadOnlySpan<byte> data, ulong seed = 0)
    {
        unchecked
        {
            var length = data.Length;
            var remaining = data;
            ulong hash;

            if (length >= 32)
            {
                var v1 = seed + Prime1 + Prime2;
                var v2 = seed + Prime2;
                var v3 = seed;
                var v4 = seed - Prime1;
                while (remaining.Length >= 32)
                {
                    v1 = Round(v1, BinaryPrimitives.ReadUInt64LittleEndian(remaining));
                    v2 = Round(v2, BinaryPrimitives.ReadUInt64LittleEndian(remaining[8..]));
                    v3 = Round(v3, BinaryPrimitives.ReadUInt64LittleEndian(remaining[16..]));
                    v4 = Round(v4, BinaryPrimitives.ReadUInt64LittleEndian(remaining[24..]));
                    remaining = remaining[32..];
                }

                hash = BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7)
                    + BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);
                hash = MergeRound(hash, v1);
                hash = MergeRound(hash, v2);
                hash = MergeRound(hash, v3);
                hash = MergeRound(hash, v4);
            }
            else
            {
                hash = seed + Prime5;
            }

            hash += (ulong)length;

            while (remaining.Length >= 8)
            {
                hash ^= Round(0, BinaryPrimitives.ReadUInt64LittleEndian(remaining));
                hash = (BitOperations.RotateLeft(hash, 27) * Prime1) + Prime4;
                remaining = remaining[8..];
            }

            if (remaining.Length >= 4)
            {
                hash ^= BinaryPrimitives.ReadUInt32LittleEndian(remaining) * Prime1;
                hash = (BitOperations.RotateLeft(hash, 23) * Prime2) + Prime3;
                remaining = remaining[4..];
            }

            foreach (var b in remaining)
            {
                hash ^= b * Prime5;
                hash = BitOperations.RotateLeft(hash, 11) * Prime1;
            }

            hash ^= hash >> 33;
            hash *= Prime2;
            hash ^= hash >> 29;
            hash *= Prime3;
            hash ^= hash >> 32;
            return hash;
        }
    }

    private static ulong Round(ulong accumulator, ulong input)
    {
        unchecked
        {
            accumulator += input * Prime2;
            accumulator = BitOperations.RotateLeft(accumulator, 31);
            return accumulator * Prime1;
        }
    }

    private static ulong MergeRound(ulong hash, ulong value)
    {
        unchecked
        {
            hash ^= Round(0, value);
            return (hash * Prime1) + Prime4;
        }
    }
}
