// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The type lattice of <c>docs/design.md</c>, "Value classification and types": <c>empty</c> &lt; <c>bool</c>;
/// <c>empty</c> &lt; <c>int</c> &lt; <c>float</c>; <c>empty</c> &lt; <c>date</c> &lt; <c>datetime</c>; <c>empty</c> &lt;
/// <c>time</c>; any other join is <c>string</c>. It is format-agnostic: the delimited layer and every later format
/// use it through <see cref="FieldAccumulator"/>.
/// </summary>
internal static class TypeLattice
{
    /// <summary>The strict join of two votes. A null vote (no vote) is the identity.</summary>
    /// <param name="a">The first type.</param>
    /// <param name="b">The second type.</param>
    /// <returns>The joined type.</returns>
    public static ValueVote Join(ValueVote a, ValueVote b)
    {
        if (a == ValueVote.Null)
        {
            return b;
        }

        if (b == ValueVote.Null || a == b)
        {
            return a;
        }

        if ((a == ValueVote.Int && b == ValueVote.Float) || (a == ValueVote.Float && b == ValueVote.Int))
        {
            return ValueVote.Float;
        }

        if ((a == ValueVote.Date && b == ValueVote.DateTime) || (a == ValueVote.DateTime && b == ValueVote.Date))
        {
            return ValueVote.DateTime;
        }

        return ValueVote.String;
    }

    /// <summary>The type name written to the descriptor; <c>empty</c> for no votes.</summary>
    /// <param name="type">The joined type (<see cref="ValueVote.Null"/> for none).</param>
    /// <returns>The name.</returns>
    public static string TypeName(ValueVote type)
    {
        return type switch
        {
            ValueVote.Bool => "bool",
            ValueVote.Int => "int",
            ValueVote.Float => "float",
            ValueVote.Date => "date",
            ValueVote.DateTime => "datetime",
            ValueVote.Time => "time",
            ValueVote.String => "string",
            _ => "empty",
        };
    }
}
