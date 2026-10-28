// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// Lays out the canonical JSON of a descriptor (sq-32). The input is the descriptor written without any white space;
/// the output has two-space indentation and LF line ends, except that an object or an array whose one-line form is
/// <see cref="MaxInlineCharacters"/> characters or fewer is written on one line, spaced: <c>{ "a": 1, "b": [1, 2] }</c>.
/// The one-line form is the container alone (no indentation, key or trailing comma) and holds every nested container
/// inline too. The length counts code points of the written text, an escape at its written length. That equals the
/// count of UTF-16 code units, because the writer escapes everything outside the Basic Multilingual Plane, so a
/// literal character is one code unit and one UTF-8 lead byte. Scalar tokens (numbers and strings) are copied byte
/// for byte from the input, so the numbers and the escapes are the canonical ones.
/// </summary>
internal static class CompactLayout
{
    /// <summary>The longest one-line form, in characters.</summary>
    public const int MaxInlineCharacters = 120;

    /// <summary>Lays out compact canonical JSON (an object or array, no white space outside strings).</summary>
    /// <param name="compact">The UTF-8 bytes of the compact JSON.</param>
    /// <returns>The UTF-8 bytes of the laid-out JSON, without a trailing line end.</returns>
    public static byte[] Apply(byte[] compact)
    {
        ArgumentNullException.ThrowIfNull(compact);
        var lengths = new List<int>();
        var pos = 0;
        Measure(compact, ref pos, lengths);

        var output = new MemoryStream(compact.Length + (compact.Length / 2));
        pos = 0;
        var ordinal = 0;
        Emit(compact, ref pos, lengths, ref ordinal, output, level: 0);
        return output.ToArray();
    }

    // Pass 1: the one-line length of every container, indexed by the order in which the containers open.
    private static int Measure(byte[] src, ref int pos, List<int> lengths)
    {
        var c = src[pos];
        if (c is (byte)'{' or (byte)'[')
        {
            var isObject = c == (byte)'{';
            var close = isObject ? (byte)'}' : (byte)']';
            var ordinal = lengths.Count;
            lengths.Add(0);
            pos++;
            if (src[pos] == close)
            {
                pos++;
                lengths[ordinal] = 2;
                return 2;
            }

            var total = isObject ? 4 : 2;
            var count = 0;
            while (true)
            {
                if (count > 0)
                {
                    total += 2;
                }

                if (isObject)
                {
                    total += Characters(src, ScanScalar(src, ref pos)) + 2;
                    pos++; // ':'
                }

                total += Measure(src, ref pos, lengths);
                count++;
                if (src[pos] == (byte)',')
                {
                    pos++;
                    continue;
                }

                pos++; // closer
                break;
            }

            lengths[ordinal] = total;
            return total;
        }

        return Characters(src, ScanScalar(src, ref pos));
    }

    // Pass 2: write the layout.
    private static void Emit(byte[] src, ref int pos, List<int> lengths, ref int ordinal, MemoryStream output, int level)
    {
        var c = src[pos];
        if (c is not ((byte)'{' or (byte)'['))
        {
            var token = ScanScalar(src, ref pos);
            output.Write(src, token.Start, token.Length);
            return;
        }

        var isObject = c == (byte)'{';
        var close = isObject ? (byte)'}' : (byte)']';
        var mine = ordinal++;
        pos++;
        if (src[pos] == close)
        {
            pos++;
            output.WriteByte(c);
            output.WriteByte(close);
            return;
        }

        // A container inside a one-line container is shorter than it, so it is one line too.
        var oneLine = lengths[mine] <= MaxInlineCharacters;
        output.WriteByte(c);
        if (!oneLine)
        {
            output.WriteByte((byte)'\n');
        }
        else if (isObject)
        {
            output.WriteByte((byte)' ');
        }

        var first = true;
        while (true)
        {
            if (!first)
            {
                output.WriteByte((byte)',');
                output.WriteByte(oneLine ? (byte)' ' : (byte)'\n');
            }

            first = false;
            if (!oneLine)
            {
                Indent(output, level + 1);
            }

            if (isObject)
            {
                var key = ScanScalar(src, ref pos);
                output.Write(src, key.Start, key.Length);
                output.WriteByte((byte)':');
                output.WriteByte((byte)' ');
                pos++; // ':'
            }

            Emit(src, ref pos, lengths, ref ordinal, output, level + 1);
            if (src[pos] == (byte)',')
            {
                pos++;
                continue;
            }

            pos++; // closer
            break;
        }

        if (!oneLine)
        {
            output.WriteByte((byte)'\n');
            Indent(output, level);
        }
        else if (isObject)
        {
            output.WriteByte((byte)' ');
        }

        output.WriteByte(close);
    }

    private static void Indent(MemoryStream output, int level)
    {
        for (var i = 0; i < level; i++)
        {
            output.WriteByte((byte)' ');
            output.WriteByte((byte)' ');
        }
    }

    private readonly record struct Token(int Start, int Length);

    private static Token ScanScalar(byte[] src, ref int pos)
    {
        var start = pos;
        if (src[pos] == (byte)'"')
        {
            pos++;
            while (src[pos] != (byte)'"')
            {
                pos += src[pos] == (byte)'\\' ? 2 : 1;
            }

            pos++;
        }
        else
        {
            while (src[pos] is not ((byte)',' or (byte)']' or (byte)'}'))
            {
                pos++;
            }
        }

        return new Token(start, pos - start);
    }

    // The characters of a token: its bytes less the UTF-8 continuation bytes.
    private static int Characters(byte[] src, Token token)
    {
        var n = 0;
        for (var i = token.Start; i < token.Start + token.Length; i++)
        {
            if ((src[i] & 0xC0) != 0x80)
            {
                n++;
            }
        }

        return n;
    }
}
