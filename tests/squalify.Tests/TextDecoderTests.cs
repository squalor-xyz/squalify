// SPDX-License-Identifier: Apache-2.0
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

public class TextDecoderTests
{
    private static (string Text, long[] Offsets) Decode(byte[] bytes, string charset, int bomLength = 0, int chunk = 4096)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = new TextDecoder(stream, charset, bomLength);
        var text = new StringBuilder();
        var offsets = new List<long>();
        var chars = new char[chunk];
        var positions = new long[chunk];
        int n;
        while ((n = decoder.Read(chars, positions)) > 0)
        {
            text.Append(chars, 0, n);
            offsets.AddRange(positions.Take(n));
        }

        return (text.ToString(), offsets.ToArray());
    }

    [Fact]
    public void TextDecoder_Utf8_OffsetsAreByteOffsetsOfEachCharacter()
    {
        var (text, offsets) = Decode(Encoding.UTF8.GetBytes("aé€b"), "utf-8");
        Assert.Equal("aé€b", text);
        Assert.Equal(new long[] { 0, 1, 3, 6 }, offsets);
    }

    [Fact]
    public void TextDecoder_Utf8_SupplementaryCharacterIsTwoCharsWithOneOffset()
    {
        var (text, offsets) = Decode(Encoding.UTF8.GetBytes("a\U0001F600b"), "utf-8");
        Assert.Equal("a\U0001F600b", text);
        Assert.Equal(new long[] { 0, 1, 1, 5 }, offsets);
    }

    [Fact]
    public void TextDecoder_Utf8_InvalidSequencesBecomeOneReplacementEach()
    {
        // E2 82 is one truncated sequence; FF is another; the trailing lone 80 is a third.
        var (text, offsets) = Decode(TestBytes.Concat(TestBytes.Ascii("a"), TestBytes.Hex("E2 82"), TestBytes.Ascii("b"), TestBytes.Hex("FF"), TestBytes.Ascii("c"), TestBytes.Hex("80")), "ascii");
        Assert.Equal("a�b�c�", text);
        Assert.Equal(new long[] { 0, 1, 3, 4, 5, 6 }, offsets);
    }

    [Fact]
    public void TextDecoder_Utf8_TruncatedAtEndOfFile_IsOneReplacement()
    {
        var (text, offsets) = Decode(TestBytes.Concat(TestBytes.Ascii("a"), TestBytes.Hex("E2 82")), "utf-8");
        Assert.Equal("a�", text);
        Assert.Equal(new long[] { 0, 1 }, offsets);
    }

    [Fact]
    public void TextDecoder_Utf8WithBom_SkipsTheBomButKeepsFileOffsets()
    {
        var (text, offsets) = Decode(TestBytes.Concat(TestBytes.Hex("EF BB BF"), TestBytes.Ascii("hi")), "utf-8", bomLength: 3);
        Assert.Equal("hi", text);
        Assert.Equal(new long[] { 3, 4 }, offsets);
    }

    [Fact]
    public void TextDecoder_Latin1_MapsEveryByte()
    {
        var (text, offsets) = Decode(TestBytes.Hex("63 61 66 E9 93"), "latin-1");
        Assert.Equal("café\u0093", text);
        Assert.Equal(new long[] { 0, 1, 2, 3, 4 }, offsets);
    }

    [Fact]
    public void TextDecoder_Utf16_TwoBytesPerUnitAndPairs()
    {
        var le = TestBytes.Concat(TestBytes.Hex("FF FE"), new UnicodeEncoding(false, false).GetBytes("a\U0001F600b"));
        var (text, offsets) = Decode(le, "utf-16le", bomLength: 2);
        Assert.Equal("a\U0001F600b", text);
        Assert.Equal(new long[] { 2, 4, 4, 8 }, offsets);

        var be = new UnicodeEncoding(true, false).GetBytes("a€");
        var (textBe, offsetsBe) = Decode(be, "utf-16be");
        Assert.Equal("a€", textBe);
        Assert.Equal(new long[] { 0, 2 }, offsetsBe);
    }

    [Fact]
    public void TextDecoder_Utf16_LoneSurrogatesAndOddTrailingByteBecomeReplacements()
    {
        // 'a', a lone high surrogate, 'b', a lone low surrogate, then one stray byte.
        var (text, offsets) = Decode(TestBytes.Hex("61 00 00 D8 62 00 00 DC 41"), "utf-16le");
        Assert.Equal("a�b��", text);
        Assert.Equal(new long[] { 0, 2, 4, 6, 8 }, offsets);
    }

    [Fact]
    public void TextDecoder_Utf32_FourBytesPerCharacter()
    {
        var (text, offsets) = Decode(TestBytes.Hex("00 00 FE FF 00 00 00 68 00 01 F6 00 00 11 00 00"), "utf-32be", bomLength: 4);
        Assert.Equal("h\U0001F600�", text);
        Assert.Equal(new long[] { 4, 8, 8, 12 }, offsets);
    }

    [Fact]
    public void TextDecoder_ChunkSizeDoesNotChangeTheResult()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 30000; i++)
        {
            sb.Append(i % 7 == 0 ? "€" : i % 11 == 0 ? "\U0001F600" : "x");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var whole = Decode(bytes, "utf-8", chunk: 100000);
        var tiny = Decode(bytes, "utf-8", chunk: 1);
        var odd = Decode(bytes, "utf-8", chunk: 4097);
        Assert.Equal(sb.ToString(), whole.Text);
        Assert.Equal(whole.Text, tiny.Text);
        Assert.Equal(whole.Offsets, tiny.Offsets);
        Assert.Equal(whole.Offsets, odd.Offsets);
        Assert.Equal(bytes.Length - 1L, whole.Offsets[^1]);
    }

    [Fact]
    public void TextDecoder_CharacterSplitAcrossTheInternalBuffer_IsDecoded()
    {
        var bytes = TestBytes.Concat(Enumerable.Repeat((byte)'a', 65534).ToArray(), Encoding.UTF8.GetBytes("€\U0001F600z"));
        var (text, offsets) = Decode(bytes, "utf-8");
        Assert.Equal(65534 + 4, text.Length);
        Assert.DoesNotContain('�', text);
        Assert.Equal(65534L, offsets[65534]);
        Assert.Equal(65537L, offsets[65535]);
        Assert.Equal(65537L, offsets[65536]);
        Assert.Equal(65541L, offsets[65537]);
    }
}
