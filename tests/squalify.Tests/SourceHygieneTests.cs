// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify.Tests;

/// <summary>
/// sq-78: the library's source is ASCII. A literal non-breaking space reached <c>src/</c> once (a char literal, caught by hand at a gate), and
/// the Write tool decodes escape sequences into literal characters, so the rule is checked by a test rather than by a reader.
/// </summary>
public sealed class SourceHygieneTests
{
    [Fact]
    public void SourceFiles_AreAscii()
    {
        var src = Path.GetFullPath(Path.Combine(Golden.SourceFixturesRoot(), "..", "..", "..", "src"));
        Assert.True(Directory.Exists(src), $"The source folder {src} was not found.");

        // Every file under src, except build output (bin and obj folders).
        var files = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(src, file))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(files);

        var offenders = new List<string>();
        foreach (var file in files)
        {
            var first = FirstNonAscii(File.ReadAllBytes(file));
            if (first is { } at)
            {
                var name = Path.GetRelativePath(src, file).Replace(Path.DirectorySeparatorChar, '/');
                offenders.Add($"{name}: line {at.Line}, byte offset {at.Offset} (0-based), value 0x{at.Value:X2}");
            }
        }

        if (offenders.Count > 0)
        {
            Assert.Fail("Source under src/ must be ASCII. First non-ASCII byte of each file: " + string.Join("; ", offenders));
        }
    }

    // Whether a file lies under a bin or obj folder: build output, which is not source.
    private static bool IsBuildOutput(string src, string file)
    {
        var folders = Path.GetRelativePath(src, Path.GetDirectoryName(file)!).Split(Path.DirectorySeparatorChar);
        return folders.Any(static folder => folder is "bin" or "obj");
    }

    // The 1-based line and the 0-based byte offset of the first byte at or above 0x80, or null when every byte is ASCII.
    private static (int Line, int Offset, byte Value)? FirstNonAscii(byte[] bytes)
    {
        var at = Array.FindIndex(bytes, static b => b >= 0x80);
        if (at < 0)
        {
            return null;
        }

        var line = bytes.AsSpan(0, at).Count((byte)'\n') + 1;
        return (line, at, bytes[at]);
    }
}
