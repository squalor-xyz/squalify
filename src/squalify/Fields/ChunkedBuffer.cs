// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;

namespace squalor.squalify;

/// <summary>
/// An append-only buffer that grows in fixed chunks instead of by doubling one array. A doubling list that reaches
/// 100,000 eight-byte values has allocated about twice that in arrays on the way (the old ones are garbage on the large
/// object heap until a full collection), so its footprint is hard to account for; a chunked buffer holds exactly the
/// chunks it needs, so <see cref="EstimatedBytes"/> is close to the truth. A chunk stays under the 85,000-byte limit of
/// the large object heap.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class ChunkedBuffer<T>
    where T : unmanaged
{
    private const int ChunkLength = 4096;

    private readonly List<T[]> _chunks = [];
    private int _inLast = ChunkLength;

    /// <summary>The number of elements.</summary>
    public int Count => _chunks.Count == 0 ? 0 : ((_chunks.Count - 1) * ChunkLength) + _inLast;

    /// <summary>The memory held, in bytes: the chunks and the object.</summary>
    public long EstimatedBytes => 64 + (_chunks.Count * ((long)ChunkLength * Unsafe.SizeOf<T>() + 32));

    /// <summary>Appends an element.</summary>
    /// <param name="value">The element.</param>
    public void Add(T value)
    {
        if (_inLast == ChunkLength)
        {
            _chunks.Add(new T[ChunkLength]);
            _inLast = 0;
        }

        _chunks[^1][_inLast++] = value;
    }

    /// <summary>The elements, in the order they were added.</summary>
    /// <returns>The elements.</returns>
    public IEnumerable<T> InOrder()
    {
        for (var c = 0; c < _chunks.Count; c++)
        {
            var length = c == _chunks.Count - 1 ? _inLast : ChunkLength;
            for (var i = 0; i < length; i++)
            {
                yield return _chunks[c][i];
            }
        }
    }

    /// <summary>Copies the elements to a new array, in the order they were added.</summary>
    /// <returns>The array.</returns>
    public T[] ToArray()
    {
        var result = new T[Count];
        var at = 0;
        for (var c = 0; c < _chunks.Count; c++)
        {
            var length = c == _chunks.Count - 1 ? _inLast : ChunkLength;
            Array.Copy(_chunks[c], 0, result, at, length);
            at += length;
        }

        return result;
    }
}
