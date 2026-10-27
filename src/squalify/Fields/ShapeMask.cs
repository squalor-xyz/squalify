// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace squalor.squalify;

/// <summary>
/// The shape mask of a text, one implementation for field values (<c>fields[].shapes</c>) and for file names
/// (<c>hints.fileNameShape</c>). Each UTF-16 code unit maps to a class: <c>9</c> for an ASCII digit, <c>A</c> for an
/// ASCII upper-case letter, <c>a</c> for an ASCII lower-case letter; ASCII punctuation and space are kept
/// literally, except <c>{</c> and <c>}</c>, which would collide with the run count; every other code unit (a non-ASCII letter, a control character, half of a surrogate pair) is
/// <c>?</c>. A run of two or more of the same symbol is written <c>c{n}</c>. A mask of more than
/// <see cref="MaxTokens"/> tokens (a token is a symbol with its optional run count) is cut after
/// <see cref="MaxTokens"/> and ends with <c>+</c>. The classes are plain ASCII ranges, never the runtime's Unicode
/// tables or the culture, so the mask is the same on every runtime and machine.
/// </summary>
internal static class ShapeMask
{
    /// <summary>The most tokens a mask keeps before it is cut and ends with <c>+</c>.</summary>
    public const int MaxTokens = 64;

    /// <summary>The longest mask <see cref="Write"/> can produce: 64 tokens of a symbol and <c>{n}</c> (up to ten digits), then <c>+</c>.</summary>
    public const int MaxLength = (MaxTokens * 13) + 1;

    /// <summary>Maps a text to its shape mask.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The mask.</returns>
    public static string Of(ReadOnlySpan<char> text)
    {
        Span<char> buffer = stackalloc char[MaxLength];
        return new string(buffer[..Write(text, buffer)]);
    }

    /// <summary>Writes the shape mask of a text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="destination">The buffer; it must hold <see cref="MaxLength"/> characters.</param>
    /// <returns>The number of characters written.</returns>
    public static int Write(ReadOnlySpan<char> text, Span<char> destination)
    {
        if (destination.Length < MaxLength)
        {
            throw new ArgumentException("The destination must hold MaxLength characters.", nameof(destination));
        }

        var length = 0;
        var tokens = 0;
        var i = 0;
        while (i < text.Length)
        {
            var symbol = Symbol(text[i]);
            var run = 1;
            while (i + run < text.Length && Symbol(text[i + run]) == symbol)
            {
                run++;
            }

            if (tokens == MaxTokens)
            {
                destination[length++] = '+';
                return length;
            }

            destination[length++] = symbol;
            if (run >= 2)
            {
                destination[length++] = '{';
                run.TryFormat(destination[length..], out var written, default, CultureInfo.InvariantCulture);
                length += written;
                destination[length++] = '}';
            }

            tokens++;
            i += run;
        }

        return length;
    }

    private static char Symbol(char c)
    {
        if (c is >= '0' and <= '9')
        {
            return '9';
        }

        if (c is >= 'A' and <= 'Z')
        {
            return 'A';
        }

        if (c is >= 'a' and <= 'z')
        {
            return 'a';
        }

        // A literal brace would read as part of a run count, so it is a question mark like any other character
        // that has no class of its own.
        return c is >= ' ' and <= '~' and not ('{' or '}') ? c : '?';
    }
}
