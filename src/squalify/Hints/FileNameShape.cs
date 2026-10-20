// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;

namespace squalor.squalify;

/// <summary>
/// The shape mask of a file name (<c>hints.fileNameShape</c>), by the mask rules of <c>docs/design.md</c>:
/// <c>9</c> for an ASCII digit, <c>A</c> for an ASCII upper-case letter, <c>a</c> for an ASCII lower-case letter,
/// ASCII punctuation and space kept literally, and <c>?</c> for anything else. A run of two or more of the same
/// symbol is written <c>c{n}</c>. A mask of more than 64 tokens is cut after 64 and ends with <c>+</c>.
/// </summary>
internal static class FileNameShape
{
    private const int MaxTokens = 64;

    /// <summary>Maps a name to its shape mask.</summary>
    /// <param name="name">The file name.</param>
    /// <returns>The mask.</returns>
    public static string Of(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var mask = new StringBuilder();
        var tokens = 0;
        var i = 0;
        while (i < name.Length)
        {
            var symbol = Symbol(name[i]);
            var run = 1;
            while (i + run < name.Length && Symbol(name[i + run]) == symbol)
            {
                run++;
            }

            if (tokens == MaxTokens)
            {
                mask.Append('+');
                return mask.ToString();
            }

            mask.Append(symbol);
            if (run >= 2)
            {
                mask.Append('{').Append(run.ToString(CultureInfo.InvariantCulture)).Append('}');
            }

            tokens++;
            i += run;
        }

        return mask.ToString();
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

        return c is >= ' ' and <= '~' ? c : '?';
    }
}
