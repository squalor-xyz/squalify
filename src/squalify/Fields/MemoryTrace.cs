// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Records the peak of the accumulators' estimated bytes during one profile, so that a test or a benchmark can compare
/// the peak working set of the process with <see cref="MemoryBudget.CeilingBytes(long)"/>. It is never written to the
/// descriptor.
/// </summary>
internal sealed class MemoryTrace
{
    /// <summary>The peak of the sum of the accumulators' estimated bytes, sampled while the file streams.</summary>
    public long PeakEstimatedBytes { get; private set; }

    /// <summary>The ceiling for the peak working set: the peak estimate plus the fixed allowance.</summary>
    public long CeilingBytes => MemoryBudget.CeilingBytes(PeakEstimatedBytes);

    /// <summary>Called with each sample, so that a test can compare the estimate with the live heap at that moment. Null in a normal run.</summary>
    public Action<long>? Observer { get; init; }

    /// <summary>Records a sample of the sum of estimated bytes.</summary>
    /// <param name="estimatedBytes">The sum now.</param>
    public void Sample(long estimatedBytes)
    {
        if (estimatedBytes > PeakEstimatedBytes)
        {
            PeakEstimatedBytes = estimatedBytes;
        }

        Observer?.Invoke(estimatedBytes);
    }
}
