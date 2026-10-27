// SPDX-License-Identifier: Apache-2.0
using System.Buffers;

namespace squalor.squalify;

/// <summary>
/// A Greenwald-Khanna quantile summary over <see cref="long"/> keys, with rank error at most
/// <c>epsilon * n</c> for <c>n</c> values and <c>epsilon = 1 / inverseEpsilon</c> (0.001 by default).
/// </summary>
/// <remarks>
/// <para>
/// The summary is a sorted list of tuples <c>(v, g, delta)</c>: <c>g</c> is the number of values between this tuple's
/// value and the one before it (inclusive of this one), and <c>delta</c> bounds how far the true rank of <c>v</c> can
/// exceed the sum of the <c>g</c> up to it. Incoming values are buffered; when <c>floor(1 / (2 epsilon))</c> have
/// arrived (500 by default) the buffer is sorted and merged into the summary in one pass, and the summary is
/// compressed: a tuple is folded into the next when <c>g + g' + delta' &lt; floor(2 epsilon n)</c>, walking from the
/// largest value down, and the smallest and largest values are always kept. A value that equals stored ones goes after them;
/// a value below every stored one or above every stored one has <c>delta = 0</c>, any other has <c>floor(2 epsilon n)</c>.
/// </para>
/// <para>
/// Nothing is random, no hash is involved and all arithmetic is on integers: the result depends only on the sequence
/// of keys. The keys of a field are a function of its values (an integer, the ordered bits of a double, or the ticks
/// of a date under one order), see <see cref="KeyOfDouble(double)"/>.
/// </para>
/// </remarks>
internal sealed class GreenwaldKhanna
{
    /// <summary>The default <c>1 / epsilon</c>: epsilon is 0.001.</summary>
    public const int InverseEpsilon = 1000;

    private readonly int _inverseEpsilon;
    private readonly long[] _pending;
    private int _pendingCount;
    private Tuple[] _tuples = [];
    private int _size;
    private long _merged;

    /// <summary>Creates a summary.</summary>
    /// <param name="inverseEpsilon"><c>1 / epsilon</c>, at least 2; the rank error is at most <c>n / inverseEpsilon</c>.</param>
    public GreenwaldKhanna(int inverseEpsilon = InverseEpsilon)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(inverseEpsilon, 2);
        _inverseEpsilon = inverseEpsilon;
        _pending = new long[inverseEpsilon / 2];
    }

    /// <summary>The number of keys added.</summary>
    public long Count => _merged + _pendingCount;

    /// <summary>The number of tuples kept, after the buffer is merged.</summary>
    public int TupleCount
    {
        get
        {
            Flush();
            return _size;
        }
    }

    /// <summary>
    /// The memory held by the summary, in bytes: the tuple array at its capacity, the buffer and the object. The array
    /// the merge works in is rented from a shared pool for the duration of one merge and is not held between merges.
    /// </summary>
    public long EstimatedBytes => ((long)_tuples.Length * 24) + ((long)_pending.Length * 8) + 128;

    /// <summary>The rank error as a fraction of the number of keys: <c>1 / inverseEpsilon</c> for the default.</summary>
    public static double Epsilon => 1.0 / InverseEpsilon;

    /// <summary>The key of a double, such that keys compare like the doubles do (negative zero sorts just below zero).</summary>
    /// <param name="value">A finite double or an infinity.</param>
    /// <returns>The key.</returns>
    public static long KeyOfDouble(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        return bits >= 0 ? bits : bits ^ long.MaxValue;
    }

    /// <summary>The double of a key made by <see cref="KeyOfDouble(double)"/>.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The double.</returns>
    public static double DoubleOfKey(long key)
    {
        return BitConverter.Int64BitsToDouble(key >= 0 ? key : key ^ long.MaxValue);
    }

    /// <summary>Adds a key.</summary>
    /// <param name="key">The key.</param>
    public void Add(long key)
    {
        _pending[_pendingCount++] = key;
        if (_pendingCount == _pending.Length)
        {
            Flush();
        }
    }

    /// <summary>Replaces every stored key by a function of it that does not change their order (for example an integer key by the key of the same number as a double).</summary>
    /// <param name="map">A non-decreasing function.</param>
    public void MapKeys(Func<long, long> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        Flush();
        for (var i = 0; i < _size; i++)
        {
            _tuples[i] = _tuples[i] with { Value = map(_tuples[i].Value) };
        }
    }

    /// <summary>The key whose rank is nearest to the wanted rank.</summary>
    /// <param name="rank">The wanted rank, from 1 to <see cref="Count"/>. A rank outside that range is not rejected: it selects the smallest or the largest stored key.</param>
    /// <returns>A stored key; its true rank is within <c>Count / inverseEpsilon</c> of <paramref name="rank"/>.</returns>
    /// <exception cref="InvalidOperationException">No key was added.</exception>
    public long Query(long rank)
    {
        Flush();
        if (_size == 0)
        {
            throw new InvalidOperationException("The summary is empty.");
        }

        long minRank = 0;
        var best = 0;
        var bestError = long.MaxValue;
        for (var i = 0; i < _size; i++)
        {
            minRank += _tuples[i].Gap;
            var maxRank = minRank + _tuples[i].Delta;
            var error = Math.Max(rank - minRank, maxRank - rank);
            if (error < bestError)
            {
                bestError = error;
                best = i;
            }
        }

        return _tuples[best].Value;
    }

    private void Flush()
    {
        if (_pendingCount == 0)
        {
            return;
        }

        var batch = _pending.AsSpan(0, _pendingCount);
        batch.Sort();

        // The merge and the compression work in a rented array, so that a summary holds only the tuples it keeps.
        var scratch = ArrayPool<Tuple>.Shared.Rent(_size + batch.Length);
        try
        {
            var output = 0;
            var next = 0;
            var count = _merged;
            foreach (var value in batch)
            {
                while (next < _size && _tuples[next].Value <= value)
                {
                    scratch[output++] = _tuples[next++];
                }

                var delta = next == 0 || next == _size ? 0 : (2 * count) / _inverseEpsilon;
                scratch[output++] = new Tuple(value, 1, delta);
                count++;
            }

            while (next < _size)
            {
                scratch[output++] = _tuples[next++];
            }

            _merged = count;
            _pendingCount = 0;
            var (start, end) = Compress(scratch, output);
            Keep(scratch, start, end);
        }
        finally
        {
            ArrayPool<Tuple>.Shared.Return(scratch);
        }
    }

    // Walks from the largest value down, folding a tuple into the one after it while the folded tuple stays within
    // floor(2 epsilon n). The first and the last tuples are kept, so the minimum and maximum are exact. The result is
    // kept at the end of the array, and its start and end are returned.
    private (int Start, int End) Compress(Tuple[] tuples, int size)
    {
        if (size < 3)
        {
            return (0, size);
        }

        var threshold = (2 * _merged) / _inverseEpsilon;
        var head = tuples[size - 1];
        var write = size;
        for (var i = size - 2; i >= 1; i--)
        {
            var current = tuples[i];
            if (current.Gap + head.Gap + head.Delta < threshold)
            {
                head = head with { Gap = head.Gap + current.Gap };
            }
            else
            {
                tuples[--write] = head;
                head = current;
            }
        }

        tuples[--write] = head;
        tuples[--write] = tuples[0];
        return (write, size);
    }

    // Copies the kept tuples into the summary's own array, which is replaced when it is too small or well over half empty.
    private void Keep(Tuple[] scratch, int start, int end)
    {
        var length = end - start;
        if (_tuples.Length < length || _tuples.Length > length + (length / 2) + 64)
        {
            _tuples = new Tuple[length + (length / 4) + 16];
        }

        Array.Copy(scratch, start, _tuples, 0, length);
        _size = length;
    }

    private readonly record struct Tuple(long Value, long Gap, long Delta);
}
