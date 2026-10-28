// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace squalor.squalify;

/// <summary>
/// Groups data records into row classes by which of their fields are non-null, in file order and in bounded memory. It
/// is format-agnostic: a profiler gives it one null mask per record (<see cref="Add"/>).
/// It also counts the all-null records and, per field, the nulls outside them, so those facts have one source. See
/// <c>docs/design.md</c>, "Row classes".
/// </summary>
/// <remarks>
/// <para>
/// A class is identified by <c>rc:</c> plus the 16 lowercase hex digits of <see cref="XxHash64"/> (seed 0) over the
/// ascending non-null field indexes, written as invariant decimal UTF-8 numbers separated by commas (for fields 1 and
/// 10, the bytes of <c>1,10</c>; for no non-null field, no bytes). The id therefore depends on the column order and
/// on nothing else: not on the line, the order of the records, the file or the machine. Two different sets collide with
/// probability 2^-64, and classes are keyed by this hash.
/// </para>
/// <para>
/// A record whose mask equals the previous one is counted without hashing. A record of another mask is encoded into a
/// buffer allocated once, so a record never allocates once its class exists. The first <c>maxClasses</c> classes seen
/// are kept with exact counts; records of later classes are counted in <c>truncated</c> (their transitions are still
/// counted, by hash). An all-null record is a class with no fields, and is also counted as an all-null record.
/// </para>
/// </remarks>
internal sealed class RowClassAccumulator
{
    // The longest decimal int is 10 digits, plus a comma.
    private const int BytesPerIndex = 11;

    private readonly int _fieldCount;
    private readonly long _maxClasses;
    private readonly byte[] _encoded;
    private readonly bool[] _previous;
    private readonly int[] _nullIndexes;
    private readonly long[] _nullsOutsideAllNull;
    private readonly Dictionary<ulong, int> _slots = [];
    private readonly List<ClassState> _classes = [];

    private bool _hasPrevious;
    private ulong _previousHash;
    private int _previousSlot;
    private int _nullCount;
    private long _transitions;
    private long _allNullRows;
    private long _truncated;

    /// <summary>Creates an accumulator.</summary>
    /// <param name="fieldCount">The number of fields in every mask.</param>
    /// <param name="maxClasses">The most classes kept (<c>limits.rowClasses</c>).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fieldCount"/> is negative or <paramref name="maxClasses"/> is below 1.</exception>
    public RowClassAccumulator(int fieldCount, long maxClasses)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fieldCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxClasses, 1L);
        _fieldCount = fieldCount;
        _maxClasses = maxClasses;
        _encoded = new byte[checked(fieldCount * BytesPerIndex)];
        _previous = new bool[fieldCount];
        _nullIndexes = new int[fieldCount];
        _nullsOutsideAllNull = new long[fieldCount];
    }

    /// <summary>
    /// An estimate of the memory held, in bytes: the encode buffer and the per-field arrays, which are allocated once,
    /// and the classes kept (at most <c>maxClasses</c>), each with its field ranges.
    /// </summary>
    public long EstimatedBytes
    {
        get
        {
            long bytes = 256 + _encoded.Length + _previous.Length + (_nullIndexes.Length * 4L) + (_nullsOutsideAllNull.Length * 8L);
            bytes += _slots.Count * 48L;
            foreach (var state in _classes)
            {
                bytes += 200 + (state.Fields.Length * 16L);
            }

            return bytes;
        }
    }

    /// <summary>Adds one record.</summary>
    /// <param name="isNull">One entry per field, true when the field is null. The span is read, not kept.</param>
    /// <param name="line">The 1-based line of the record in the file (a row number for a format without lines); it only bounds the class's first and last line.</param>
    /// <returns>True when every field of the record is null, so the caller knows whether the record is an all-null (separator) record.</returns>
    /// <exception cref="ArgumentException"><paramref name="isNull"/> does not have one entry per field.</exception>
    public bool Add(ReadOnlySpan<bool> isNull, long line)
    {
        if (isNull.Length != _fieldCount)
        {
            throw new ArgumentException("The mask needs one entry per field.", nameof(isNull));
        }

        if (!_hasPrevious || !isNull.SequenceEqual(_previous))
        {
            Switch(isNull);
        }

        var allNull = _nullCount == _fieldCount;
        if (allNull)
        {
            _allNullRows++;
        }
        else
        {
            for (var i = 0; i < _nullCount; i++)
            {
                _nullsOutsideAllNull[_nullIndexes[i]]++;
            }
        }

        if (_previousSlot < 0)
        {
            _truncated++;
            return allNull;
        }

        var state = _classes[_previousSlot];
        if (state.Count == 0)
        {
            state.FirstLine = line;
        }

        state.Count++;
        state.LastLine = line;
        return allNull;
    }

    /// <summary>
    /// Ends the adjacency of records: the next record is not compared with the previous one, so a block boundary is never
    /// a class transition. Classes, counts and the first and last lines span the blocks of a file.
    /// </summary>
    public void Break()
    {
        _hasPrevious = false;
    }

    /// <summary>The number of nulls of a field in records where at least one field is not null.</summary>
    /// <param name="field">The field index.</param>
    /// <returns>The count.</returns>
    public long NullsExcludingAllNullRows(int field)
    {
        return _nullsOutsideAllNull[field];
    }

    /// <summary>Builds <c>table.rowClasses</c>: the classes sorted by count descending, then id ordinally.</summary>
    /// <returns>The row classes. <c>Count</c> is the number of classes kept, so it never exceeds the cap; <c>Truncated</c> says whether more exist.</returns>
    public RowClassesInfo Build()
    {
        var classes = _classes
            .Select(static c => new RowClass(c.Id, c.Count, c.Fields, c.FirstLine, c.LastLine))
            .OrderByDescending(static c => c.Count)
            .ThenBy(static c => c.Id, StringComparer.Ordinal)
            .ToArray();
        return new RowClassesInfo(classes.Length, _transitions, _allNullRows, _truncated, classes);
    }

    // The mask differs from the previous record's (or there is none): find its class.
    private void Switch(ReadOnlySpan<bool> isNull)
    {
        var length = 0;
        var nulls = 0;
        for (var i = 0; i < isNull.Length; i++)
        {
            if (isNull[i])
            {
                _nullIndexes[nulls++] = i;
                continue;
            }

            if (length > 0)
            {
                _encoded[length++] = (byte)',';
            }

            length += WriteDecimal(_encoded.AsSpan(length), i);
        }

        _nullCount = nulls;
        var hash = XxHash64.Hash(_encoded.AsSpan(0, length));
        if (_hasPrevious && hash != _previousHash)
        {
            _transitions++;
        }

        if (!_slots.TryGetValue(hash, out var slot))
        {
            if (_classes.Count < _maxClasses)
            {
                slot = _classes.Count;
                _slots.Add(hash, slot);
                _classes.Add(new ClassState(hash, Ranges(isNull)));
            }
            else
            {
                slot = -1;
            }
        }

        isNull.CopyTo(_previous);
        _hasPrevious = true;
        _previousHash = hash;
        _previousSlot = slot;
    }

    private static int WriteDecimal(Span<byte> destination, int value)
    {
        // A field index is never negative; digits are written most significant first, without a culture.
        var digits = 1;
        for (var rest = value; rest >= 10; rest /= 10)
        {
            digits++;
        }

        for (var i = digits - 1; i >= 0; i--)
        {
            destination[i] = (byte)('0' + (value % 10));
            value /= 10;
        }

        return digits;
    }

    private static FieldRange[] Ranges(ReadOnlySpan<bool> isNull)
    {
        var ranges = new List<FieldRange>();
        var i = 0;
        while (i < isNull.Length)
        {
            if (isNull[i])
            {
                i++;
                continue;
            }

            var first = i;
            while (i + 1 < isNull.Length && !isNull[i + 1])
            {
                i++;
            }

            ranges.Add(new FieldRange(first, i));
            i++;
        }

        return ranges.ToArray();
    }

    private sealed class ClassState
    {
        public ClassState(ulong hash, FieldRange[] fields)
        {
            Id = "rc:" + hash.ToString("x16", CultureInfo.InvariantCulture);
            Fields = fields;
        }

        public string Id { get; }

        public FieldRange[] Fields { get; }

        public long Count { get; set; }

        public long FirstLine { get; set; }

        public long LastLine { get; set; }
    }
}
