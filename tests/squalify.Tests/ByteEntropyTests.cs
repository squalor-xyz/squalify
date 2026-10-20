// SPDX-License-Identifier: Apache-2.0
using squalor.squalify;

namespace squalor.squalify.Tests;

public class ByteEntropyTests
{
    [Fact]
    public void ByteEntropy_UniformHistogram_IsEightBits()
    {
        Assert.Equal(8.0, ByteEntropy.Bits(Enumerable.Repeat(4L, 256).ToArray(), 1024));
    }

    [Fact]
    public void ByteEntropy_OneValue_IsZero()
    {
        var histogram = new long[256];
        histogram[7] = 100;
        Assert.Equal(0.0, ByteEntropy.Bits(histogram, 100));
        Assert.False(double.IsNegative(ByteEntropy.Bits(histogram, 100)));
    }

    [Fact]
    public void ByteEntropy_TwoEqualValues_IsOneBit()
    {
        var histogram = new long[256];
        histogram[0] = 50;
        histogram[255] = 50;
        Assert.Equal(1.0, ByteEntropy.Bits(histogram, 100));
    }

    [Fact]
    public void ByteEntropy_Skewed_MatchesTheFormula()
    {
        var histogram = new long[256];
        histogram[1] = 3;
        histogram[2] = 1;
        // H = -(3/4 log2 3/4 + 1/4 log2 1/4) = 0.8112781244591328 (to 1e-12)
        Assert.InRange(ByteEntropy.Bits(histogram, 4), 0.8112781244591328 - 1e-12, 0.8112781244591328 + 1e-12);
    }
}
