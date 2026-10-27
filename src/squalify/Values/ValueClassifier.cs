// SPDX-License-Identifier: Apache-2.0
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace squalor.squalify;

/// <summary>
/// Classifies one value: a null token, or a vote for <c>bool</c>, <c>int</c>, <c>float</c>, <c>date</c>,
/// <c>datetime</c>, <c>time</c> or <c>string</c>. It is a pure function that never reads the current culture, the
/// clock or the machine, and it never assumes a locale: a date reports every order it allows, and the decimal
/// separator is a parameter. See <c>docs/design.md</c>, "Value classification and types".
/// </summary>
internal static class ValueClassifier
{
    /// <summary>The version of <see cref="NullVocabulary"/>, recorded as <c>limits.nullTokens</c>.</summary>
    public const long NullVocabularyVersion = 1;

    /// <summary>
    /// The characters trimmed from both ends of a value before it is classified: U+0009 to U+000D, U+0020 and U+00A0
    /// (no-break space). The set is fixed, not taken from <see cref="char.IsWhiteSpace(char)"/>, because that follows
    /// the runtime's Unicode tables. Other space characters, such as U+2028 and U+3000, are not trimmed. The field
    /// accumulator (sq-05) trims with <see cref="Trim(ReadOnlySpan{char})"/> so that it sees the same value.
    /// </summary>
    public const string TrimChars = "\t\n\v\f\r \u00A0";

    private static readonly string[] NullEntries = ["", "NA", "NA.", "N/A", "null", "NaN", "-"];

    private static readonly string[] BoolWords = ["true", "false", "yes", "no"];

    private static readonly NumberFormatInfo CommaFormat = CreateCommaFormat();

    /// <summary>
    /// Null vocabulary 1, matched ASCII case-insensitively after trimming. It is fixed and never learned from the
    /// data, so one column's type cannot depend on another's.
    /// </summary>
    public static IReadOnlyList<string> NullVocabulary { get; } = new ReadOnlyCollection<string>(NullEntries);

    /// <summary>Classifies one value.</summary>
    /// <param name="value">The value. <see cref="TrimChars"/> at either end are ignored.</param>
    /// <param name="decimalSeparator">The dialect's decimal separator, <c>.</c> or <c>,</c>.</param>
    /// <returns>The classification.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="decimalSeparator"/> is neither <c>.</c> nor <c>,</c>.</exception>
    public static ValueClass Classify(ReadOnlySpan<char> value, char decimalSeparator)
    {
        if (decimalSeparator != '.' && decimalSeparator != ',')
        {
            throw new ArgumentOutOfRangeException(nameof(decimalSeparator), "The decimal separator must be '.' or ','.");
        }

        var v = Trim(value);

        foreach (var entry in NullEntries)
        {
            if (EqualsAsciiIgnoreCase(v, entry))
            {
                return new ValueClass(ValueVote.Null, null, DateOrders.None, ClockKind.None, ValueFlags.None, entry);
            }
        }

        var flags = IsCommaDecimalLike(v) ? ValueFlags.CommaDecimalLike : ValueFlags.None;

        foreach (var word in BoolWords)
        {
            if (EqualsAsciiIgnoreCase(v, word))
            {
                return Plain(ValueVote.Bool, flags);
            }
        }

        if (TryNumber(v, decimalSeparator, out var numeric))
        {
            return Plain(numeric, flags | (HasLeadingZero(v) ? ValueFlags.LeadingZero : ValueFlags.None));
        }

        if (TryDate(v, out var orders, out var pattern))
        {
            return new ValueClass(ValueVote.Date, pattern, orders, ClockKind.None, flags, null);
        }

        if (TryDateTime(v, out orders, out var clock, out pattern))
        {
            return new ValueClass(ValueVote.DateTime, pattern, orders, clock, flags, null);
        }

        if (TryTime(v, allowMarker: true, allowOffset: false, twoDigitHour: false, out clock, out pattern))
        {
            return new ValueClass(ValueVote.Time, pattern, DateOrders.None, clock, flags, null);
        }

        return Plain(ValueVote.String, flags);
    }

    /// <summary>Trims <see cref="TrimChars"/> from both ends of a value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The trimmed value.</returns>
    public static ReadOnlySpan<char> Trim(ReadOnlySpan<char> value)
    {
        return value.Trim(TrimChars);
    }

    private static ValueClass Plain(ValueVote vote, ValueFlags flags)
    {
        return new ValueClass(vote, null, DateOrders.None, ClockKind.None, flags, null);
    }

    private static NumberFormatInfo CreateCommaFormat()
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberDecimalSeparator = ",";
        format.NumberGroupSeparator = string.Empty;
        return format;
    }

    private static char LowerAscii(char c)
    {
        return c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
    }

    private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<char> value, string expected)
    {
        if (value.Length != expected.Length)
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (LowerAscii(value[i]) != LowerAscii(expected[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDigit(char c)
    {
        return c is >= '0' and <= '9';
    }

    private static int SignLength(ReadOnlySpan<char> v)
    {
        return v.Length > 0 && (v[0] == '+' || v[0] == '-') ? 1 : 0;
    }

    private static bool AllDigits(ReadOnlySpan<char> v)
    {
        if (v.IsEmpty)
        {
            return false;
        }

        foreach (var c in v)
        {
            if (!IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    // ^[+-]?\d+,\d+$ with ASCII digits.
    private static bool IsCommaDecimalLike(ReadOnlySpan<char> v)
    {
        var digits = v[SignLength(v)..];
        var comma = digits.IndexOf(',');
        return comma > 0 && AllDigits(digits[..comma]) && AllDigits(digits[(comma + 1)..]);
    }

    // The integer part starts with a zero that is followed by another digit, as in 007 or -00.5.
    private static bool HasLeadingZero(ReadOnlySpan<char> v)
    {
        var digits = v[SignLength(v)..];
        return digits.Length >= 2 && digits[0] == '0' && IsDigit(digits[1]);
    }

    private static bool TryNumber(ReadOnlySpan<char> v, char decimalSeparator, out ValueVote vote)
    {
        vote = ValueVote.String;
        if (v.IsEmpty)
        {
            return false;
        }

        // Only characters a number can contain. This also keeps out the words and symbols that double.TryParse
        // accepts (Infinity, NaN) and trailing NULs.
        foreach (var c in v)
        {
            if (!IsDigit(c) && c != '+' && c != '-' && c != 'e' && c != 'E' && c != decimalSeparator)
            {
                return false;
            }
        }

        if (AllDigits(v[SignLength(v)..]) && long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
        {
            vote = ValueVote.Int;
            return true;
        }

        var format = decimalSeparator == ',' ? CommaFormat : CultureInfo.InvariantCulture.NumberFormat;
        if (double.TryParse(v, NumberStyles.Float, format, out var number) && double.IsFinite(number))
        {
            vote = ValueVote.Float;
            return true;
        }

        return false;
    }

    private static bool IsLeapYear(int year)
    {
        return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
    }

    private static bool IsValidDate(int year, int month, int day)
    {
        if (year < 1 || month < 1 || month > 12 || day < 1)
        {
            return false;
        }

        var days = month switch
        {
            2 => IsLeapYear(year) ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            _ => 31,
        };
        return day <= days;
    }

    private static int ParseDigits(ReadOnlySpan<char> digits)
    {
        var n = 0;
        foreach (var c in digits)
        {
            n = (n * 10) + (c - '0');
        }

        return n;
    }

    private static string DatePart(int length)
    {
        return length switch
        {
            4 => "yyyy",
            2 => "nn",
            _ => "n",
        };
    }

    // Three digit groups with the same separator (/, - or .): a 4-digit year first or last and 1-2 digit day and
    // month. The pattern keeps the separator and each group's width, and no order, so it is the same for every
    // order the value allows.
    private static bool TryDate(ReadOnlySpan<char> v, out DateOrders orders, out string pattern)
    {
        orders = DateOrders.None;
        pattern = string.Empty;

        var first = 0;
        while (first < v.Length && IsDigit(v[first]))
        {
            first++;
        }

        if (first == 0 || first >= v.Length || v[first] is not ('/' or '-' or '.'))
        {
            return false;
        }

        var separator = v[first];
        var rest = v[(first + 1)..];
        var cut = rest.IndexOf(separator);
        if (cut < 0)
        {
            return false;
        }

        var second = rest[..cut];
        var third = rest[(cut + 1)..];
        var a = v[..first];
        if (!AllDigits(second) || !AllDigits(third))
        {
            return false;
        }

        if (a.Length == 4 && second.Length <= 2 && third.Length <= 2)
        {
            if (IsValidDate(ParseDigits(a), ParseDigits(second), ParseDigits(third)))
            {
                orders = DateOrders.YMD;
            }
        }
        else if (a.Length <= 2 && second.Length <= 2 && third.Length == 4)
        {
            var year = ParseDigits(third);
            if (IsValidDate(year, ParseDigits(a), ParseDigits(second)))
            {
                orders |= DateOrders.MDY;
            }

            if (IsValidDate(year, ParseDigits(second), ParseDigits(a)))
            {
                orders |= DateOrders.DMY;
            }
        }

        if (orders == DateOrders.None)
        {
            return false;
        }

        var sep = separator.ToString();
        pattern = DatePart(a.Length) + sep + DatePart(second.Length) + sep + DatePart(third.Length);
        return true;
    }

    // A date, then T or one space, then a time. T is only for the ISO shape yyyy-MM-dd; a zone offset is only
    // allowed after the ISO date, and a day-period marker only after a space.
    private static bool TryDateTime(ReadOnlySpan<char> v, out DateOrders orders, out ClockKind clock, out string pattern)
    {
        orders = DateOrders.None;
        clock = ClockKind.None;
        pattern = string.Empty;

        var cut = v.IndexOfAny('T', ' ');
        if (cut <= 0 || !TryDate(v[..cut], out orders, out var datePattern))
        {
            return false;
        }

        var separator = v[cut];
        var isoDate = datePattern == "yyyy-nn-nn";
        if (separator == 'T' && !isoDate)
        {
            return false;
        }

        if (!TryTime(v[(cut + 1)..], allowMarker: separator == ' ', allowOffset: isoDate, twoDigitHour: separator == 'T', out clock, out var timePattern))
        {
            orders = DateOrders.None;
            clock = ClockKind.None;
            return false;
        }

        pattern = datePattern + separator + timePattern;
        return true;
    }

    private static bool TryTwoDigits(ReadOnlySpan<char> s, ref int position, int max, out int value)
    {
        value = 0;
        if (position + 2 > s.Length || !IsDigit(s[position]) || !IsDigit(s[position + 1]))
        {
            return false;
        }

        value = ParseDigits(s.Slice(position, 2));
        position += 2;
        return value <= max;
    }

    // H:mm[:ss[.f+]] with an optional ASCII AM/PM marker (12h clock, hours 1 to 12) or, when allowed, a zone offset.
    private static bool TryTime(ReadOnlySpan<char> s, bool allowMarker, bool allowOffset, bool twoDigitHour, out ClockKind clock, out string pattern)
    {
        clock = ClockKind.None;
        pattern = string.Empty;

        var position = 0;
        while (position < s.Length && IsDigit(s[position]))
        {
            position++;
        }

        var hourDigits = position;
        if (hourDigits is < 1 or > 2 || (twoDigitHour && hourDigits != 2))
        {
            return false;
        }

        var hour = ParseDigits(s[..hourDigits]);
        if (position >= s.Length || s[position] != ':')
        {
            return false;
        }

        position++;
        var tail = new StringBuilder(":mm");
        if (!TryTwoDigits(s, ref position, 59, out _))
        {
            return false;
        }

        if (position < s.Length && s[position] == ':')
        {
            position++;
            if (!TryTwoDigits(s, ref position, 59, out _))
            {
                return false;
            }

            tail.Append(":ss");
            if (position < s.Length && s[position] == '.')
            {
                position++;
                var start = position;
                while (position < s.Length && IsDigit(s[position]))
                {
                    position++;
                }

                if (position == start)
                {
                    return false;
                }

                tail.Append(".f+");
            }
        }

        var remainder = s[position..];
        var marker = false;
        if (remainder.IsEmpty)
        {
            // Plain 24h time.
        }
        else if (allowOffset && TryOffset(remainder, out var offsetPattern))
        {
            tail.Append(offsetPattern);
        }
        else if (allowMarker && TryMarker(remainder, out var spaced))
        {
            marker = true;
            tail.Append(spaced ? " tt" : "tt");
        }
        else
        {
            return false;
        }

        if (marker ? (hour > 12 || hour < 1) : hour > 23)
        {
            return false;
        }

        var token = marker ? 'h' : 'H';
        pattern = new string(token, hourDigits) + tail;
        clock = marker ? ClockKind.H12 : ClockKind.H24;
        return true;
    }

    private static bool TryMarker(ReadOnlySpan<char> s, out bool spaced)
    {
        spaced = s.Length > 0 && s[0] == ' ';
        var marker = spaced ? s[1..] : s;
        return marker.Length == 2
            && (LowerAscii(marker[0]) == 'a' || LowerAscii(marker[0]) == 'p')
            && LowerAscii(marker[1]) == 'm';
    }

    // Z, +hh, +hhmm or +hh:mm (and the same with -). The pattern tokens are Z, zz, zzzz and zzz.
    private static bool TryOffset(ReadOnlySpan<char> s, out string pattern)
    {
        pattern = string.Empty;
        if (s.Length == 1 && s[0] == 'Z')
        {
            pattern = "Z";
            return true;
        }

        if (s.Length < 3 || (s[0] != '+' && s[0] != '-'))
        {
            return false;
        }

        var position = 1;
        if (!TryTwoDigits(s, ref position, 23, out _))
        {
            return false;
        }

        if (position == s.Length)
        {
            pattern = "zz";
            return true;
        }

        var colon = s[position] == ':';
        if (colon)
        {
            position++;
        }

        if (!TryTwoDigits(s, ref position, 59, out _) || position != s.Length)
        {
            return false;
        }

        pattern = colon ? "zzz" : "zzzz";
        return true;
    }
}
