// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The sequence comparison behind the value equality of the descriptor model: the records that hold a list compare its
/// members in order, each with its default equality (ordinal for text), so no culture is involved. A null list is not
/// an empty one. The hash codes agree with the equality; like any .NET hash code they are good for one process only
/// and never reach the output.
/// </summary>
internal static class ValueEquality
{
    /// <summary>True when both lists are null, or both hold equal members in the same order.</summary>
    /// <typeparam name="T">The member type.</typeparam>
    /// <param name="left">The first list, or null.</param>
    /// <param name="right">The second list, or null.</param>
    /// <returns>Whether the two compare equal.</returns>
    public static bool ListEquals<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < left.Count; i++)
        {
            if (!comparer.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Adds a list to a hash code: null as its own value, otherwise the count and then the members in order.</summary>
    /// <typeparam name="T">The member type.</typeparam>
    /// <param name="hash">The hash code to add to.</param>
    /// <param name="list">The list, or null.</param>
    public static void AddList<T>(ref HashCode hash, IReadOnlyList<T>? list)
    {
        if (list is null)
        {
            hash.Add(-1);
            return;
        }

        hash.Add(list.Count);
        foreach (var item in list)
        {
            hash.Add(item);
        }
    }
}
