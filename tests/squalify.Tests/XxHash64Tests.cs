// SPDX-License-Identifier: Apache-2.0
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// Expected values are XXH64 with seed 0.
/// <list type="bullet">
/// <item>They were produced with python-xxhash 4.0.1, which wraps the reference C library (xxHash 0.8.3), run once offline.</item>
/// <item>The empty string, "a", "abc" and "Nobody inspects the spammish repetition" are the values that the
/// cespare/xxhash (Go) test suite also uses; that suite is checked against the reference implementation.</item>
/// <item>The sanity buffer follows the construction of the reference sanity test (Cyan4973/xxHash,
/// cli/xsum_sanity_check.c): byte i is the top byte of a generator that starts at PRIME32 = 2654435761 and is
/// multiplied by PRIME64 = 11400714785074694797 (mod 2^64) after each byte. The seed-PRIME32 empty-input value in
/// <see cref="XxHash64_Seed_ChangesTheHash"/> was also produced by python-xxhash.</item>
/// </list>
/// </summary>
public class XxHash64Tests
{
    private static byte[] SanityBuffer(int length)
    {
        const ulong prime32 = 2654435761UL;
        const ulong prime64 = 11400714785074694797UL;
        var generator = prime32;
        var buffer = new byte[length];
        for (var i = 0; i < length; i++)
        {
            buffer[i] = (byte)(generator >> 56);
            generator = unchecked(generator * prime64);
        }

        return buffer;
    }

    [Theory]
    [InlineData("", 0xEF46DB3751D8E999UL)]
    [InlineData("a", 0xD24EC4F1A98C6E5BUL)]
    [InlineData("abc", 0x44BC2CF5AD770999UL)]
    [InlineData("Nobody inspects the spammish repetition", 0xFBCEA83C8A378BF1UL)]
    public void XxHash64_PublishedVectors_Strings(string text, ulong expected)
    {
        Assert.Equal(expected, XxHash64.Hash(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(expected, XxHash64.HashString(text));
    }

    [Theory]
    [InlineData(1, 0xE934A84ADB052768UL)]
    [InlineData(4, 0x9136A0DCA57457EEUL)]
    [InlineData(8, 0xCDBCF538E71D1348UL)]
    [InlineData(14, 0x8282DCC4994E35C8UL)]
    [InlineData(31, 0x299B39A290E6D783UL)]
    [InlineData(32, 0x18B216492BB44B70UL)]
    [InlineData(33, 0x55C8DC3E578F5B59UL)]
    [InlineData(100, 0x4BFE019CD91D9EA4UL)]
    [InlineData(222, 0xB641AE8CB691C174UL)]
    public void XxHash64_PublishedVectors_SanityBuffer(int length, ulong expected)
    {
        Assert.Equal(expected, XxHash64.Hash(SanityBuffer(length)));
    }

    [Fact]
    public void XxHash64_HashString_UsesUtf8()
    {
        // U+00B5 is two bytes in UTF-8 (C2 B5) and one UTF-16 code unit.
        Assert.Equal(XxHash64.Hash(new byte[] { 0xC2, 0xB5 }), XxHash64.HashString("µ"));
    }

    [Fact]
    public void XxHash64_Seed_ChangesTheHash()
    {
        // Empty input with seed PRIME32 = 2654435761.
        Assert.Equal(0xAC75FDA2929B17EFUL, XxHash64.Hash(ReadOnlySpan<byte>.Empty, 2654435761UL));
    }

    [Theory]
    [InlineData(1, 2654435761UL, 0x5014607643A9B4C3UL)]
    [InlineData(7, 99UL, 0xF77BD0ABC7F05888UL)]
    [InlineData(33, 1UL, 0xDC168C777660DF5CUL)]
    [InlineData(100, 12345UL, 0x11D0082B083FC9F3UL)]
    [InlineData(222, 2654435761UL, 0x20CB8AB7AE10C14AUL)]
    public void XxHash64_SeededVectors_SanityBuffer(int length, ulong seed, ulong expected)
    {
        // Same provenance as above, with a non-zero seed. Lengths 33 and above cover the 32-byte block loop.
        Assert.Equal(expected, XxHash64.Hash(SanityBuffer(length), seed));
    }
}
