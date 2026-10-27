// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>What one value votes for. <see cref="Null"/> is a null token, not a type.</summary>
internal enum ValueVote
{
    /// <summary>A member of the null-token vocabulary.</summary>
    Null,

    /// <summary><c>true</c>, <c>false</c>, <c>yes</c> or <c>no</c>.</summary>
    Bool,

    /// <summary>A whole number that fits Int64.</summary>
    Int,

    /// <summary>A finite decimal number.</summary>
    Float,

    /// <summary>A calendar date.</summary>
    Date,

    /// <summary>A date and a time of day.</summary>
    DateTime,

    /// <summary>A time of day.</summary>
    Time,

    /// <summary>Anything else.</summary>
    String,
}

/// <summary>The clock of a time value.</summary>
internal enum ClockKind
{
    /// <summary>The value carries no time of day.</summary>
    None,

    /// <summary>Hours 0 to 23 with no day-period marker.</summary>
    H24,

    /// <summary>Hours with an <c>AM</c> or <c>PM</c> marker.</summary>
    H12,
}

/// <summary>Value-level flags that are counted per field.</summary>
[Flags]
internal enum ValueFlags
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>A number whose integer part has a leading zero, such as <c>007</c>.</summary>
    LeadingZero = 1,

    /// <summary>The value matches <c>^[+-]?[0-9]+,[0-9]+$</c>, whatever the decimal separator.</summary>
    CommaDecimalLike = 2,
}

/// <summary>The result of classifying one value.</summary>
/// <param name="Vote">The vote.</param>
/// <param name="Pattern">
/// For <c>date</c>, <c>datetime</c> and <c>time</c> votes, the order-neutral layout of the value (see
/// <c>docs/descriptor.md</c>, <c>patterns</c>); otherwise null.
/// </param>
/// <param name="Orders">For <c>date</c> and <c>datetime</c> votes, every order that makes the value valid.</param>
/// <param name="Clock">For <c>datetime</c> and <c>time</c> votes, the clock; otherwise <see cref="ClockKind.None"/>.</param>
/// <param name="Flags">The value-level flags.</param>
/// <param name="NullToken">For a <see cref="ValueVote.Null"/> vote, the vocabulary entry that matched, in its vocabulary spelling.</param>
internal readonly record struct ValueClass(
    ValueVote Vote,
    string? Pattern,
    DateOrders Orders,
    ClockKind Clock,
    ValueFlags Flags,
    string? NullToken);
