// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The field orders under which a date value is a valid calendar date. squalify never picks one: a value such as
/// <c>6/5/2026</c> allows both <see cref="DMY"/> and <see cref="MDY"/>.
/// </summary>
[Flags]
internal enum DateOrders
{
    /// <summary>No order makes the value a valid date.</summary>
    None = 0,

    /// <summary>Year, month, day.</summary>
    YMD = 1,

    /// <summary>Month, day, year.</summary>
    MDY = 2,

    /// <summary>Day, month, year.</summary>
    DMY = 4,
}

/// <summary>Helpers for <see cref="DateOrders"/>.</summary>
internal static class DateOrdersExtensions
{
    /// <summary>The names of the orders in the set, sorted ordinally (<c>DMY</c>, <c>MDY</c>, <c>YMD</c>).</summary>
    /// <param name="orders">The set.</param>
    /// <returns>The names; empty for <see cref="DateOrders.None"/>.</returns>
    public static IReadOnlyList<string> Names(this DateOrders orders)
    {
        var names = new List<string>(3);
        if ((orders & DateOrders.DMY) != 0)
        {
            names.Add("DMY");
        }

        if ((orders & DateOrders.MDY) != 0)
        {
            names.Add("MDY");
        }

        if ((orders & DateOrders.YMD) != 0)
        {
            names.Add("YMD");
        }

        return names;
    }
}
