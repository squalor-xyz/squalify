// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;

namespace squalor.squalify;

/// <summary>
/// Calendar arithmetic on ticks (100 ns, counted from 0001-01-01 like <see cref="DateTime.Ticks"/>) in the proleptic
/// Gregorian calendar, with integer arithmetic only: no <see cref="DateTime"/> parsing, culture or time zone.
/// </summary>
internal static class TemporalMath
{
    /// <summary>Ticks per second.</summary>
    public const long TicksPerSecond = 10_000_000;

    /// <summary>Ticks per day.</summary>
    public const long TicksPerDay = 86_400 * TicksPerSecond;

    // Days from 0001-01-01 to 1970-01-01.
    private const long EpochShift = 719_162;

    /// <summary>The ticks of a parsed value under one date order.</summary>
    /// <param name="parts">The parsed parts.</param>
    /// <param name="order"><see cref="DateOrders.DMY"/> reads the two groups as day and month; any other order as month and day. A time (year 0) has no date part.</param>
    /// <returns>The ticks.</returns>
    public static long Ticks(in TemporalParts parts, DateOrders order)
    {
        if (parts.Year == 0)
        {
            return parts.TimeTicks;
        }

        var month = order == DateOrders.DMY ? parts.Second : parts.First;
        var day = order == DateOrders.DMY ? parts.First : parts.Second;
        return (DaysFromCivil(parts.Year, month, day) * TicksPerDay) + parts.TimeTicks;
    }

    /// <summary>Splits ticks into parts, reading a date as year, month, day (<see cref="DateOrders.YMD"/>).</summary>
    /// <param name="ticks">The ticks of a date or datetime; the time of day of a time.</param>
    /// <param name="kind">The vote.</param>
    /// <returns>The parts.</returns>
    public static TemporalParts Split(long ticks, ValueVote kind)
    {
        if (kind == ValueVote.Time)
        {
            return new TemporalParts(0, 0, 0, ((ticks % TicksPerDay) + TicksPerDay) % TicksPerDay);
        }

        var days = Math.DivRem(ticks, TicksPerDay, out var timeOfDay);
        CivilFromDays(days, out var year, out var month, out var day);
        return new TemporalParts(year, month, day, kind == ValueVote.Date ? 0 : timeOfDay);
    }

    /// <summary>Formats ticks as ISO 8601: <c>yyyy-MM-dd</c>, <c>yyyy-MM-ddTHH:mm:ss[.f]</c> or <c>HH:mm:ss[.f]</c>.</summary>
    /// <param name="ticks">The ticks.</param>
    /// <param name="kind">The type: date, datetime or time. A fraction of a second is written only when it is not zero, without trailing zeros.</param>
    /// <returns>The text.</returns>
    public static string Iso(long ticks, ValueVote kind)
    {
        var text = new StringBuilder(32);
        var days = Math.DivRem(ticks, TicksPerDay, out var timeOfDay);
        if (kind != ValueVote.Time)
        {
            CivilFromDays(days, out var year, out var month, out var day);
            Pad(text, year, 4).Append('-');
            Pad(text, month, 2).Append('-');
            Pad(text, day, 2);
        }

        if (kind == ValueVote.Date)
        {
            return text.ToString();
        }

        if (kind == ValueVote.DateTime)
        {
            text.Append('T');
        }

        var seconds = Math.DivRem(timeOfDay, TicksPerSecond, out var fraction);
        Pad(text, (int)(seconds / 3600), 2).Append(':');
        Pad(text, (int)(seconds / 60 % 60), 2).Append(':');
        Pad(text, (int)(seconds % 60), 2);
        if (fraction != 0)
        {
            var digits = fraction.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0');
            text.Append('.').Append(digits);
        }

        return text.ToString();
    }

    // Days from 0001-01-01 (Howard Hinnant's days_from_civil, shifted).
    private static long DaysFromCivil(int year, int month, int day)
    {
        long y = month <= 2 ? year - 1 : year;
        var era = (y >= 0 ? y : y - 399) / 400;
        var yoe = y - (era * 400);
        var doy = (((153 * (month + (month > 2 ? -3 : 9))) + 2) / 5) + day - 1;
        var doe = (yoe * 365) + (yoe / 4) - (yoe / 100) + doy;
        return (era * 146_097) + doe - 719_468 + EpochShift;
    }

    private static void CivilFromDays(long days, out int year, out int month, out int day)
    {
        var z = days - EpochShift + 719_468;
        var era = (z >= 0 ? z : z - 146_096) / 146_097;
        var doe = z - (era * 146_097);
        var yoe = (doe - (doe / 1460) + (doe / 36_524) - (doe / 146_096)) / 365;
        var y = yoe + (era * 400);
        var doy = doe - ((365 * yoe) + (yoe / 4) - (yoe / 100));
        var mp = ((5 * doy) + 2) / 153;
        day = (int)(doy - (((153 * mp) + 2) / 5) + 1);
        month = (int)(mp < 10 ? mp + 3 : mp - 9);
        year = (int)(month <= 2 ? y + 1 : y);
    }

    private static StringBuilder Pad(StringBuilder text, int value, int width)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        return text.Append('0', Math.Max(0, width - digits.Length)).Append(digits);
    }
}
