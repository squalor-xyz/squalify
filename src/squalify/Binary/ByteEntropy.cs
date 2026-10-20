// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>Shannon entropy of a byte histogram, computed with the deterministic logarithm.</summary>
internal static class ByteEntropy
{
    /// <summary>
    /// Computes the entropy in bits per byte: <c>-sum(p * log2 p)</c> over the bins 0 to 255 in order, with
    /// <see cref="DetMath.Log2(double)"/>.
    /// </summary>
    /// <param name="histogram">The 256-bin histogram.</param>
    /// <param name="total">The sum of the bins.</param>
    /// <returns>Entropy between 0 and 8.</returns>
    public static double Bits(IReadOnlyList<long> histogram, long total)
    {
        ArgumentNullException.ThrowIfNull(histogram);
        if (total <= 0)
        {
            return 0;
        }

        var sum = 0.0;
        for (var bin = 0; bin < histogram.Count; bin++)
        {
            var count = histogram[bin];
            if (count == 0)
            {
                continue;
            }

            var p = (double)count / total;
            sum += p * DetMath.Log2(p);
        }

        return sum == 0 ? 0.0 : -sum;
    }
}
