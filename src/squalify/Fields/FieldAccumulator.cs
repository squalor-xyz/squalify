// SPDX-License-Identifier: Apache-2.0
using System.Buffers;
using System.Text;

namespace squalor.squalify;

/// <summary>Counts of date values per order, kept as evidence for a field that joined to string.</summary>
/// <param name="Values">Date and datetime values.</param>
/// <param name="Dmy">Values that allow day-month-year.</param>
/// <param name="Mdy">Values that allow month-day-year.</param>
/// <param name="Ymd">Values that allow year-month-day.</param>
internal sealed record DateOrderCounts(long Values, long Dmy, long Mdy, long Ymd);

/// <summary>
/// Profiles one field from typed input in file order, in bounded memory: the strict type with its votes, null
/// tokens, numeric and temporal statistics, lengths, shape masks, flags, monotonicity, and the constant and unique
/// flags, with exact distinct counts and quantiles up to the caps of <see cref="FieldCaps"/>. It is format-agnostic:
/// the delimited pass feeds it text (<see cref="AddText"/>), and later formats feed it typed values. See
/// <c>docs/design.md</c>, "Field profiling".
/// </summary>
/// <remarks>
/// Everything that reaches the descriptor uses only IEEE <c>+ - * /</c>, <c>Math.Sqrt</c>, integer arithmetic and the
/// in-repo <see cref="XxHash64"/>. Distinct values compare the XxHash64 (seed 0, UTF-8) of the trimmed value, so
/// memory stays at 8 bytes per distinct value however long the values are; two different values collide with
/// probability 2^-64. The constant flag of a number or a date or time compares the parsed value with the first one
/// (O(1) state); that of any other field compares the same hash of the text.
/// </remarks>
internal sealed class FieldAccumulator
{
    private const int VoteKinds = 8;
    private const int Slots = 4;
    private const DateOrders AllOrders = DateOrders.YMD | DateOrders.MDY | DateOrders.DMY;
    private static readonly int[] QuantilePercents = [1, 5, 25, 50, 75, 95, 99];
    private static readonly DateOrders[] SlotOrders = [DateOrders.None, DateOrders.YMD, DateOrders.MDY, DateOrders.DMY];

    private readonly FieldCaps _caps;
    private readonly char _decimal;

    // Votes and types.
    private readonly long[] _votes = new long[VoteKinds];
    private readonly Dictionary<string, long> _nullTokens = new(StringComparer.Ordinal);
    private Dictionary<string, long>? _patterns;
    private ValueVote _joined = ValueVote.Null;
    private long _nonNull;

    // Flags and lengths.
    private long _leadingZero;
    private long _commaDecimalLike;
    private long _padded;
    private long _lengthMin = long.MaxValue;
    private long _lengthMax;
    private long _lengthSum;

    // Shapes.
    private readonly Dictionary<string, long> _shapes = new(StringComparer.Ordinal);
    private long _shapeOther;

    // Distinct, constant and unique.
    private HashSet<ulong>? _distinct;
    private bool _distinctExceeded;
    private bool _duplicateSeen;
    private bool _haveFirst;
    private ulong _firstHash;
    private bool _allEqual = true;

    // Numeric.
    private bool _numericLive = true;
    private long _numericCount;
    private double _mean;
    private double _m2;
    private bool _allInt = true;
    private long _minInt = long.MaxValue;
    private long _maxInt = long.MinValue;
    private double _minReal = double.PositiveInfinity;
    private double _maxReal = double.NegativeInfinity;
    private List<long>? _ints = [];
    private List<double>? _reals;
    private bool _numericQuantilesExceeded;
    private Monotonic _numericOrder;
    private bool _prevIsInt;
    private long _prevInt;
    private double _prevReal;
    private bool _numEqual = true;
    private bool _firstIsInt;
    private long _firstInt;
    private double _firstReal;

    // Temporal.
    private bool _temporalLive = true;
    private DateOrders _orders = AllOrders;
    private long _dateValues;
    private long _allowDmy;
    private long _allowMdy;
    private long _allowYmd;
    private bool _clock24;
    private bool _clock12;
    private long _temporalCount;
    private readonly long[] _slotMin = new long[Slots];
    private readonly long[] _slotMax = new long[Slots];
    private readonly bool[] _slotHas = new bool[Slots];
    private readonly Monotonic[] _slotOrder = new Monotonic[Slots];
    private readonly long[] _slotPrev = new long[Slots];
    private List<ulong>? _codes = [];
    private bool _temporalQuantilesExceeded;
    private bool _tempEqual = true;
    private TemporalParts _firstParts;

    /// <summary>Creates an accumulator.</summary>
    /// <param name="caps">The effective caps of this field.</param>
    /// <param name="decimalSeparator">The dialect's decimal separator for <see cref="AddText"/>: <c>.</c> or <c>,</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="decimalSeparator"/> is neither <c>.</c> nor <c>,</c>.</exception>
    public FieldAccumulator(FieldCaps caps, char decimalSeparator = '.')
    {
        if (decimalSeparator != '.' && decimalSeparator != ',')
        {
            throw new ArgumentOutOfRangeException(nameof(decimalSeparator), "The decimal separator must be '.' or ','.");
        }

        _caps = caps;
        _decimal = decimalSeparator;
    }

    /// <summary>
    /// The date-order counts when the field joined to string because no order fits every date value; otherwise null.
    /// </summary>
    public DateOrderCounts? OrderConflict =>
        _dateValues > 0 && _orders == DateOrders.None && _joined is ValueVote.Date or ValueVote.DateTime
            ? new DateOrderCounts(_dateValues, _allowDmy, _allowMdy, _allowYmd)
            : null;

    /// <summary>
    /// Adds a value as written: it is trimmed with <see cref="ValueClassifier.Trim"/>, classified, and counted as a
    /// null token or as a typed value.
    /// </summary>
    /// <param name="text">The raw text of the value.</param>
    /// <returns>The vote of the value.</returns>
    public ValueVote AddText(ReadOnlySpan<char> text)
    {
        var trimmed = ValueClassifier.Trim(text);
        if (trimmed.Length > 0 && trimmed.Length < text.Length)
        {
            _padded++;
        }

        var cls = ValueClassifier.Classify(trimmed, _decimal);
        if (cls.Vote == ValueVote.Null)
        {
            AddNull(cls.NullToken ?? string.Empty);
            return ValueVote.Null;
        }

        AddValue(in cls, trimmed);
        return cls.Vote;
    }

    /// <summary>Adds an integer.</summary>
    /// <param name="value">The value.</param>
    public void AddInt64(long value)
    {
        var cls = new ValueClass(ValueVote.Int, null, DateOrders.None, ClockKind.None, ValueFlags.None, null, value, value);
        AddValue(in cls, Ordinal.Invariant(value));
    }

    /// <summary>
    /// Adds a double. NaN is a null token (<c>NaN</c>, as in the vocabulary); an infinity is a string, as the text
    /// <c>Infinity</c> is; any other value is a float.
    /// </summary>
    /// <param name="value">The value.</param>
    public void AddDouble(double value)
    {
        if (double.IsNaN(value))
        {
            AddNull("NaN");
            return;
        }

        if (double.IsInfinity(value))
        {
            var text = value > 0 ? "Infinity" : "-Infinity";
            var infinity = new ValueClass(ValueVote.String, null, DateOrders.None, ClockKind.None, ValueFlags.None, null);
            AddValue(in infinity, text);
            return;
        }

        var cls = new ValueClass(ValueVote.Float, null, DateOrders.None, ClockKind.None, ValueFlags.None, null, 0, value);
        AddValue(in cls, CanonicalNumber.FormatFinite(value));
    }

    /// <summary>Adds a boolean.</summary>
    /// <param name="value">The value.</param>
    public void AddBool(bool value)
    {
        var cls = new ValueClass(ValueVote.Bool, null, DateOrders.None, ClockKind.None, ValueFlags.None, null);
        AddValue(in cls, value ? "true" : "false");
    }

    /// <summary>
    /// Adds a date, datetime or time that a format stored as a typed value. Its order is not ambiguous: a date reads
    /// as year, month, day.
    /// </summary>
    /// <param name="ticks">The ticks of a date or datetime, counted from 0001-01-01 like <see cref="DateTime.Ticks"/>; the time of day for a time.</param>
    /// <param name="kind"><see cref="ValueVote.Date"/>, <see cref="ValueVote.DateTime"/> or <see cref="ValueVote.Time"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a temporal type, or <paramref name="ticks"/> is outside 0 to <see cref="DateTime.MaxValue"/>'s ticks.</exception>
    public void AddTemporal(long ticks, ValueVote kind)
    {
        if (kind is not (ValueVote.Date or ValueVote.DateTime or ValueVote.Time))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "The kind must be date, datetime or time.");
        }

        // The temporal code of a value holds a 14-bit year, so only 0001-01-01 to 9999-12-31 can be kept. The text
        // path cannot leave this range: a date has a 4-digit year of 1 to 9999 and a time of day is below 24 hours.
        if (ticks < 0 || ticks > DateTime.MaxValue.Ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), "The ticks must lie between 0 and DateTime.MaxValue.Ticks.");
        }

        var parts = TemporalMath.Split(ticks, kind);
        var orders = kind == ValueVote.Time ? DateOrders.None : DateOrders.YMD;
        var clock = kind == ValueVote.Date ? ClockKind.None : ClockKind.H24;
        var cls = new ValueClass(kind, null, orders, clock, ValueFlags.None, null, Temporal: parts);
        AddValue(in cls, TemporalMath.Iso(TemporalMath.Ticks(parts, DateOrders.YMD), kind));
    }

    /// <summary>Adds a null value.</summary>
    /// <param name="token">The null-vocabulary entry, in its vocabulary spelling.</param>
    public void AddNull(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _votes[(int)ValueVote.Null]++;
        _nullTokens[token] = _nullTokens.GetValueOrDefault(token) + 1;
    }

    /// <summary>The evidence entry that explains the fields that joined to string for lack of a common date order.</summary>
    /// <param name="conflicts">The field indexes and their counts, in ascending index order.</param>
    /// <returns>The entry, or null when there is no conflict.</returns>
    public static Evidence? ConflictEvidence(IReadOnlyList<(long Index, DateOrderCounts Counts)> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        if (conflicts.Count == 0)
        {
            return null;
        }

        var candidates = conflicts
            .Select(static c => new Candidate(
                Ordinal.Invariant(c.Index),
                0.0,
                "string: no date order fits every value; " + Ordinal.Invariant(c.Counts.Values) + " date values; DMY " + Ordinal.Invariant(c.Counts.Dmy)
                    + ", MDY " + Ordinal.Invariant(c.Counts.Mdy) + ", YMD " + Ordinal.Invariant(c.Counts.Ymd)))
            .ToArray();
        return new Evidence("type", "dateOrderConflict", "string", candidates);
    }

    /// <summary>Builds the field profile. <c>nameParts</c> is left null (sq-14), and so are the values-tier members.</summary>
    /// <param name="index">The column index.</param>
    /// <param name="name">The header name, or null.</param>
    /// <param name="nameHex">The hex of the original name bytes, or null.</param>
    /// <param name="nullsExcludingAllNullRows">Null values outside all-null rows, counted by the caller, which knows the rows.</param>
    /// <param name="valuesAllowed">True when values are opted in for this field: statistics of a constant field are then kept.</param>
    /// <returns>The profile.</returns>
    public FieldProfile Build(long index, string? name, string? nameHex, long nullsExcludingAllNullRows, bool valuesAllowed)
    {
        var nulls = _votes[(int)ValueVote.Null];
        var conflict = OrderConflict is not null;
        var type = conflict ? ValueVote.String : _joined;
        var dominant = ValueVote.Null;
        long dominantCount = 0;
        for (var v = (int)ValueVote.Bool; v <= (int)ValueVote.String; v++)
        {
            if (_votes[v] > dominantCount)
            {
                dominant = (ValueVote)v;
                dominantCount = _votes[v];
            }
        }

        var constant = Constant(nulls, type);
        var numeric = type is ValueVote.Int or ValueVote.Float && _numericLive ? BuildNumeric(type) : null;
        var temporal = type is ValueVote.Date or ValueVote.DateTime or ValueVote.Time && _temporalLive ? BuildTemporal(type) : null;
        string? withheld = null;
        if (constant == true && !valuesAllowed && (numeric is not null || temporal is not null))
        {
            numeric = null;
            temporal = null;
            withheld = "constant";
        }

        return new FieldProfile(
            Index: index,
            Name: name,
            NameParts: null,
            Type: TypeLattice.TypeName(type),
            Dominant: new DominantType(TypeLattice.TypeName(dominant), _nonNull > 0 ? dominantCount / (double)_nonNull : 0.0),
            TypeVotes: VotesMap(),
            Count: _nonNull + nulls,
            Nulls: nulls,
            NullsExcludingAllNullRows: nullsExcludingAllNullRows,
            NullTokens: CountMap.FromOrdinal(_nullTokens),
            Flags: new FieldFlags(_leadingZero, _commaDecimalLike, _padded),
            Distinct: _distinctExceeded ? new DistinctInfo(null, Exceeds: _caps.Distinct) : new DistinctInfo(_distinct?.Count ?? 0, false),
            Length: _nonNull == 0 ? null : new LengthStats(_lengthMin, _lengthMax, _lengthSum / (double)_nonNull),
            Shapes: BuildShapes(),
            Monotonic: MonotonicOf(type),
            Constant: constant,
            Unique: Unique(),
            NameHex: nameHex,
            Patterns: _patterns is null ? null : CountMap.FromOrdinal(_patterns),
            Numeric: numeric,
            Temporal: temporal,
            StatsWithheld: withheld);
    }

    // True when every non-null value is equal, which a field without a non-null value is too (it has one value, null);
    // false when the field has no value at all. An int, float, date, datetime or time field compares the parsed
    // values (1, 1.0 and 01 are equal); any other field, including one whose type ended as string, compares the
    // trimmed text. Both comparisons keep one value, so memory stays constant.
    private bool? Constant(long nulls, ValueVote type)
    {
        if (_nonNull == 0)
        {
            return nulls > 0;
        }

        return type switch
        {
            ValueVote.Int or ValueVote.Float when _numericLive => _numEqual,
            ValueVote.Date or ValueVote.DateTime or ValueVote.Time when _temporalLive => _tempEqual,
            _ => _allEqual,
        };
    }

    // True when every non-null value is distinct. False once any value repeats, even past the cap; null when the
    // distinct set overflowed before a repeat was seen.
    private bool? Unique()
    {
        if (_nonNull == 0 || _duplicateSeen)
        {
            return false;
        }

        return _distinctExceeded ? null : true;
    }

    private CountMap VotesMap()
    {
        var entries = new List<KeyValuePair<string, long>>();
        for (var v = (int)ValueVote.Bool; v <= (int)ValueVote.String; v++)
        {
            if (_votes[v] > 0)
            {
                entries.Add(new(TypeLattice.TypeName((ValueVote)v), _votes[v]));
            }
        }

        return CountMap.FromOrdinal(entries);
    }

    private void AddValue(in ValueClass cls, ReadOnlySpan<char> trimmed)
    {
        var vote = cls.Vote;
        _votes[(int)vote]++;
        _nonNull++;
        _joined = TypeLattice.Join(_joined, vote);
        if ((cls.Flags & ValueFlags.LeadingZero) != 0)
        {
            _leadingZero++;
        }

        if ((cls.Flags & ValueFlags.CommaDecimalLike) != 0)
        {
            _commaDecimalLike++;
        }

        if (cls.Pattern is { } pattern)
        {
            _patterns ??= new Dictionary<string, long>(StringComparer.Ordinal);
            _patterns[pattern] = _patterns.GetValueOrDefault(pattern) + 1;
        }

        long length = trimmed.Length;
        _lengthMin = Math.Min(_lengthMin, length);
        _lengthMax = Math.Max(_lengthMax, length);
        _lengthSum += length;

        AddShape(trimmed);
        AddDistinct(trimmed);

        if (_joined is ValueVote.Int or ValueVote.Float)
        {
            AddNumeric(in cls);
        }
        else if (_numericLive)
        {
            FreeNumeric();
        }

        if (cls.Orders != DateOrders.None)
        {
            CountOrders(cls.Orders);
        }

        if (_joined is ValueVote.Date or ValueVote.DateTime or ValueVote.Time && _orders != DateOrders.None)
        {
            AddTemporalValue(in cls);
        }
        else if (_temporalLive)
        {
            FreeTemporal();
        }
    }

    private void AddShape(ReadOnlySpan<char> trimmed)
    {
        Span<char> buffer = stackalloc char[ShapeMask.MaxLength];
        var mask = buffer[..ShapeMask.Write(trimmed, buffer)];
        var lookup = _shapes.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(mask, out var count))
        {
            lookup[mask] = count + 1;
        }
        else if (_shapes.Count < _caps.Shapes)
        {
            lookup[mask] = 1;
        }
        else
        {
            _shapeOther++;
        }
    }

    private IReadOnlyList<Shape> BuildShapes()
    {
        var shapes = _shapes.Select(static e => new Shape(e.Key, e.Value)).ToList();
        shapes.Sort(static (a, b) => a.Count != b.Count ? b.Count.CompareTo(a.Count) : string.CompareOrdinal(a.Mask, b.Mask));
        if (_shapeOther > 0)
        {
            shapes.Add(new Shape("other", _shapeOther));
        }

        return shapes;
    }

    private void AddDistinct(ReadOnlySpan<char> trimmed)
    {
        if (_distinctExceeded && !_allEqual)
        {
            return;
        }

        var hash = Hash(trimmed);
        if (!_haveFirst)
        {
            _haveFirst = true;
            _firstHash = hash;
        }
        else if (hash != _firstHash)
        {
            _allEqual = false;
        }

        if (_distinctExceeded)
        {
            return;
        }

        _distinct ??= [];
        if (!_distinct.Add(hash))
        {
            _duplicateSeen = true;
        }
        else if (_distinct.Count > _caps.Distinct)
        {
            _distinct = null;
            _distinctExceeded = true;
        }
    }

    private static ulong Hash(ReadOnlySpan<char> text)
    {
        if (text.Length <= 128)
        {
            Span<byte> small = stackalloc byte[512];
            return XxHash64.Hash(small[..Encoding.UTF8.GetBytes(text, small)]);
        }

        var rented = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(text.Length));
        try
        {
            return XxHash64.Hash(rented.AsSpan(0, Encoding.UTF8.GetBytes(text, rented)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private void FreeNumeric()
    {
        _numericLive = false;
        _ints = null;
        _reals = null;
    }

    private void AddNumeric(in ValueClass cls)
    {
        if (!_numericLive)
        {
            return;
        }

        var x = cls.Number;
        if (cls.Vote == ValueVote.Float && _allInt)
        {
            _allInt = false;
            if (_ints is not null)
            {
                _reals = new List<double>(_ints.Count + 1);
                foreach (var i in _ints)
                {
                    _reals.Add(i);
                }

                _ints = null;
            }
        }

        _numericCount++;
        if (_numericCount == 1)
        {
            _firstIsInt = cls.Vote == ValueVote.Int;
            _firstInt = cls.Integer;
            _firstReal = x;
        }
        else if (_numEqual && !SameNumber(cls.Vote == ValueVote.Int, cls.Integer, x))
        {
            _numEqual = false;
        }

        var delta = x - _mean;
        _mean += delta / _numericCount;
        _m2 += delta * (x - _mean);
        if (cls.Vote == ValueVote.Int)
        {
            _minInt = Math.Min(_minInt, cls.Integer);
            _maxInt = Math.Max(_maxInt, cls.Integer);
        }

        if (x < _minReal)
        {
            _minReal = x;
        }

        if (x > _maxReal)
        {
            _maxReal = x;
        }

        if (!_numericQuantilesExceeded)
        {
            if (_numericCount > _caps.Quantile)
            {
                _numericQuantilesExceeded = true;
                _ints = null;
                _reals = null;
            }
            else if (_allInt)
            {
                _ints!.Add(cls.Integer);
            }
            else
            {
                _reals!.Add(x);
            }
        }

        if (_numericCount > 1)
        {
            _numericOrder.Step(_prevIsInt && cls.Vote == ValueVote.Int ? cls.Integer.CompareTo(_prevInt) : x.CompareTo(_prevReal));
        }

        _prevIsInt = cls.Vote == ValueVote.Int;
        _prevInt = cls.Integer;
        _prevReal = x;
    }

    // Whether a value equals the first numeric value as a number: two integers by value, a float against an integer
    // only when the float is that exact integer, two floats by value (so -0 equals 0).
    private bool SameNumber(bool isInt, long integer, double real)
    {
        if (isInt && _firstIsInt)
        {
            return integer == _firstInt;
        }

        if (!isInt && !_firstIsInt)
        {
            return real == _firstReal;
        }

        var (whole, fraction) = isInt ? (integer, _firstReal) : (_firstInt, real);
        return fraction == Math.Floor(fraction) && Math.Abs(fraction) < 9.2e18 && (long)fraction == whole;
    }

    private NumericStats BuildNumeric(ValueVote type)
    {
        var integer = type == ValueVote.Int;
        var min = integer ? DescriptorNumber.Int(_minInt) : DescriptorNumber.Real(_minReal);
        var max = integer ? DescriptorNumber.Int(_maxInt) : DescriptorNumber.Real(_maxReal);
        // The running mean and sum of squares can overflow for values near the double limits; a non-finite result
        // would be written as a string, so it is reported as null instead.
        double? mean = double.IsFinite(_mean) ? _mean : null;
        double? stddev = null;
        if (_numericCount >= 2 && double.IsFinite(_mean) && double.IsFinite(_m2))
        {
            var spread = Math.Sqrt(_m2 / (_numericCount - 1));
            stddev = double.IsFinite(spread) ? spread : null;
        }

        if (_numericQuantilesExceeded)
        {
            return new NumericStats(min, max, mean, stddev, null, null, _caps.Quantile);
        }

        Quantiles<DescriptorNumber> quantiles;
        if (integer)
        {
            var values = _ints!.ToArray();
            Array.Sort(values);
            quantiles = Pick(values, static v => DescriptorNumber.Int(v));
        }
        else
        {
            var values = _reals!.ToArray();
            Array.Sort(values);
            quantiles = Pick(values, static v => DescriptorNumber.Real(v));
        }

        return new NumericStats(min, max, mean, stddev, quantiles, false);
    }

    // Lower nearest rank: the value at zero-based index ceil(p * n) - 1 of the sorted values. The percentages are
    // integers, so the rank is exact integer arithmetic.
    private static Quantiles<T> Pick<TValue, T>(TValue[] sorted, Func<TValue, T> convert)
    {
        var picked = new T[QuantilePercents.Length];
        for (var i = 0; i < picked.Length; i++)
        {
            var rank = ((QuantilePercents[i] * (long)sorted.Length) + 99) / 100;
            picked[i] = convert(sorted[Math.Max(rank, 1) - 1]);
        }

        return new Quantiles<T>(picked[0], picked[1], picked[2], picked[3], picked[4], picked[5], picked[6]);
    }

    private void CountOrders(DateOrders orders)
    {
        _dateValues++;
        _orders &= orders;
        if ((orders & DateOrders.DMY) != 0)
        {
            _allowDmy++;
        }

        if ((orders & DateOrders.MDY) != 0)
        {
            _allowMdy++;
        }

        if ((orders & DateOrders.YMD) != 0)
        {
            _allowYmd++;
        }
    }

    private void FreeTemporal()
    {
        _temporalLive = false;
        _codes = null;
    }

    private void AddTemporalValue(in ValueClass cls)
    {
        if (!_temporalLive)
        {
            return;
        }

        if (cls.Clock == ClockKind.H24)
        {
            _clock24 = true;
        }
        else if (cls.Clock == ClockKind.H12)
        {
            _clock12 = true;
        }

        _temporalCount++;
        var parts = cls.Temporal;
        if (_temporalCount == 1)
        {
            _firstParts = parts;
        }
        else if (_tempEqual && parts != _firstParts)
        {
            _tempEqual = false;
        }

        if (cls.Orders == DateOrders.None)
        {
            Track(0, TemporalMath.Ticks(in parts, DateOrders.None));
        }
        else
        {
            // Only the orders that are still possible for the whole field need tracking.
            for (var slot = 1; slot < Slots; slot++)
            {
                if ((SlotOrders[slot] & _orders) != 0)
                {
                    Track(slot, TemporalMath.Ticks(in parts, SlotOrders[slot]));
                }
            }
        }

        if (!_temporalQuantilesExceeded)
        {
            if (_temporalCount > _caps.Quantile)
            {
                _temporalQuantilesExceeded = true;
                _codes = null;
            }
            else
            {
                // Year (14 bits), the two groups (5 bits each) and the time of day (40 bits); decoded under the final order.
                _codes!.Add(((ulong)parts.Year << 50) | ((ulong)parts.First << 45) | ((ulong)parts.Second << 40) | (ulong)parts.TimeTicks);
            }
        }
    }

    private void Track(int slot, long ticks)
    {
        if (!_slotHas[slot])
        {
            _slotHas[slot] = true;
            _slotMin[slot] = ticks;
            _slotMax[slot] = ticks;
        }
        else
        {
            _slotMin[slot] = Math.Min(_slotMin[slot], ticks);
            _slotMax[slot] = Math.Max(_slotMax[slot], ticks);
            _slotOrder[slot].Step(ticks.CompareTo(_slotPrev[slot]));
        }

        _slotPrev[slot] = ticks;
    }

    // The slot of the field's single date order, 0 for a time, or -1 when the order is ambiguous.
    private int FinalSlot(ValueVote type)
    {
        if (type == ValueVote.Time)
        {
            return 0;
        }

        return _orders switch
        {
            DateOrders.YMD => 1,
            DateOrders.MDY => 2,
            DateOrders.DMY => 3,
            _ => -1,
        };
    }

    private TemporalStats BuildTemporal(ValueVote type)
    {
        var orders = type == ValueVote.Time ? [] : _orders.Names();
        var slot = FinalSlot(type);
        var clock = type == ValueVote.Date ? null : _clock24 == _clock12 ? null : _clock24 ? "24h" : "12h";
        if (slot < 0)
        {
            return new TemporalStats(orders, true, clock, null, null, null);
        }

        var min = TemporalMath.Iso(_slotMin[slot], type);
        var max = TemporalMath.Iso(_slotMax[slot], type);
        if (_temporalQuantilesExceeded)
        {
            return new TemporalStats(orders, false, clock, min, max, null, _caps.Quantile);
        }

        var order = SlotOrders[slot];
        var ticks = _codes!.Select(code => TemporalMath.Ticks(Decode(code, type), order)).ToArray();
        Array.Sort(ticks);
        return new TemporalStats(orders, false, clock, min, max, Pick(ticks, value => TemporalMath.Iso(value, type)));
    }

    private static TemporalParts Decode(ulong code, ValueVote type)
    {
        var year = (int)(code >> 50);
        var time = (long)(code & ((1UL << 40) - 1));
        return type == ValueVote.Time ? new TemporalParts(0, 0, 0, time) : new TemporalParts(year, (int)((code >> 45) & 31), (int)((code >> 40) & 31), time);
    }

    private string? MonotonicOf(ValueVote type)
    {
        switch (type)
        {
            case ValueVote.Int or ValueVote.Float when _numericLive:
                return _numericOrder.Result();
            case ValueVote.Date or ValueVote.DateTime or ValueVote.Time when _temporalLive:
                var slot = FinalSlot(type);
                return slot < 0 ? null : _slotOrder[slot].Result();
            default:
                return null;
        }
    }

    /// <summary>The ordering of a sequence, from the comparison of each value with the one before it.</summary>
    private struct Monotonic
    {
        private long _steps;
        private bool _notIncreasing;
        private bool _notDecreasing;
        private bool _notNonDecreasing;
        private bool _notNonIncreasing;

        public void Step(int comparison)
        {
            _steps++;
            if (comparison > 0)
            {
                _notDecreasing = true;
                _notNonIncreasing = true;
            }
            else if (comparison < 0)
            {
                _notIncreasing = true;
                _notNonDecreasing = true;
            }
            else
            {
                _notIncreasing = true;
                _notDecreasing = true;
            }
        }

        // Null for fewer than two values; a run of equal values is nonDecreasing.
        public readonly string? Result()
        {
            if (_steps == 0)
            {
                return null;
            }

            if (!_notIncreasing)
            {
                return "increasing";
            }

            if (!_notDecreasing)
            {
                return "decreasing";
            }

            if (!_notNonDecreasing)
            {
                return "nonDecreasing";
            }

            return !_notNonIncreasing ? "nonIncreasing" : "none";
        }
    }
}
