// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The most common values of one field: exact while the field has at most <c>exactCap</c> distinct values (the same
/// cap as the exact distinct set), then Space-Saving with a fixed number of counters. Values are keyed by their
/// <see cref="XxHash64"/> (seed 0, UTF-8), computed by the caller once per value; the text of a value is copied only
/// when a counter is created, so a repeated value costs one dictionary lookup. Two different values collide with
/// probability 2^-64 and would then share a counter, as they would in the distinct set.
/// </summary>
/// <remarks>
/// <para>
/// Past the cap the counters start from the exact counts of the values seen so far (the heaviest ones, by count
/// descending then ordinal value), so every counter is still exact at that point, and an unseen value takes the
/// counter with the lowest count (ties: the lowest ordinal value) and counts one above it, recording that count as its
/// error. An estimate is never below the true count and exceeds it by at most its error; an error never exceeds
/// N / counters, N being the number of values added. Everything is decided by counts and ordinal values, never by
/// dictionary enumeration order, so the result is deterministic.
/// </para>
/// <para>The result lists at most <see cref="K"/> values, by count descending then ordinal value. It holds literal values: values tier.</para>
/// </remarks>
internal sealed class TopK
{
    /// <summary>How many values the result lists.</summary>
    public const int K = 10;

    /// <summary>The number of Space-Saving counters of a field.</summary>
    public const int DefaultCounters = 1000;

    private readonly long _exactCap;
    private readonly int _counters;
    private Dictionary<ulong, Counter> _map = [];
    private SortedSet<Counter>? _byCount;
    private long _textChars;

    /// <summary>Creates the counter set of a field.</summary>
    /// <param name="exactCap">Distinct values counted exactly; the next new value switches to Space-Saving.</param>
    /// <param name="counters">Counters kept past the cap, at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="counters"/> is less than 1.</exception>
    public TopK(long exactCap, int counters = DefaultCounters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(counters, 1);
        _exactCap = exactCap;
        _counters = counters;
    }

    /// <summary>
    /// An estimate of the memory held, in bytes: each counter (the object, its dictionary entry and its node in the
    /// ordered set) and the UTF-16 text of its value. Up to 1,000 counters are held past the exact cap, whatever the
    /// cap is.
    /// </summary>
    public long EstimatedBytes => 128 + (_map.Count * 220L) + (_textChars * 2);

    /// <summary>Counts one occurrence of a value.</summary>
    /// <param name="hash">The <see cref="XxHash64"/> of the value's UTF-8 bytes.</param>
    /// <param name="value">The value, trimmed; copied only when a counter is created.</param>
    public void Add(ulong hash, ReadOnlySpan<char> value)
    {
        if (_map.TryGetValue(hash, out var counter))
        {
            if (_byCount is null)
            {
                counter.Count++;
            }
            else
            {
                _byCount.Remove(counter);
                counter.Count++;
                _byCount.Add(counter);
            }

            return;
        }

        if (_byCount is null)
        {
            _map[hash] = new Counter(hash, value.ToString(), 1, 0);
            _textChars += value.Length;
            if (_map.Count > _exactCap)
            {
                StartSpaceSaving();
            }

            return;
        }

        if (_map.Count < _counters)
        {
            Insert(new Counter(hash, value.ToString(), 1, 0));
            _textChars += value.Length;
            return;
        }

        var lowest = _byCount.Min!;
        _byCount.Remove(lowest);
        _map.Remove(lowest.Hash);
        _textChars += value.Length - lowest.Value.Length;
        Insert(new Counter(hash, value.ToString(), lowest.Count + 1, lowest.Count));
    }

    /// <summary>Builds the result.</summary>
    /// <returns>The most common values; <see cref="TopValues.Approximate"/> is true once the cap was exceeded.</returns>
    public TopValues Build()
    {
        var items = Ranked(_map.Values).Take(K).ToArray();
        var approximate = _byCount is not null;
        return new TopValues(
            items.Select(static c => new TopValue(c.Value, c.Count)).ToArray(),
            approximate,
            approximate ? items.Select(static c => c.Error).DefaultIfEmpty(0).Max() : null);
    }

    private static IEnumerable<Counter> Ranked(IEnumerable<Counter> counters)
    {
        return counters
            .OrderByDescending(static c => c.Count)
            .ThenBy(static c => c.Value, StringComparer.Ordinal);
    }

    private void Insert(Counter counter)
    {
        _map[counter.Hash] = counter;
        _byCount!.Add(counter);
    }

    // The exact counts are kept for the heaviest values; a dropped value has a count no larger than any kept one's, so
    // its error as an unseen value is at most the lowest kept count.
    private void StartSpaceSaving()
    {
        var kept = Ranked(_map.Values).Take(_counters).ToArray();
        _map = new Dictionary<ulong, Counter>(kept.Length);
        _byCount = new SortedSet<Counter>(CounterOrder.Instance);
        _textChars = 0;
        foreach (var counter in kept)
        {
            Insert(counter);
            _textChars += counter.Value.Length;
        }
    }

    private sealed class Counter(ulong hash, string value, long count, long error)
    {
        public ulong Hash { get; } = hash;

        public string Value { get; } = value;

        public long Count { get; set; } = count;

        public long Error { get; } = error;
    }

    // Lowest count first, then lowest ordinal value; the hash only makes the order total.
    private sealed class CounterOrder : IComparer<Counter>
    {
        public static readonly CounterOrder Instance = new();

        public int Compare(Counter? x, Counter? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            var byCount = x!.Count.CompareTo(y!.Count);
            if (byCount != 0)
            {
                return byCount;
            }

            var byValue = string.CompareOrdinal(x.Value, y.Value);
            return byValue != 0 ? byValue : x.Hash.CompareTo(y.Hash);
        }
    }
}
