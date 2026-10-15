// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;
using System.Text;
using Xunit.Sdk;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// Golden-file harness. <c>Verify("name.csv", descriptor)</c> writes the descriptor with the canonical writer and
/// compares the bytes with <c>fixtures/name.csv.squalify.json</c>. Set <c>SQUALIFY_UPDATE_GOLDEN=1</c> to rewrite
/// the golden from the current output instead (review the git diff before committing it).
/// </summary>
internal static class Golden
{
    /// <summary>Environment variable that switches <see cref="Verify(string, Descriptor)"/> to update mode.</summary>
    public const string UpdateVariable = "SQUALIFY_UPDATE_GOLDEN";

    private const string GoldenSuffix = ".squalify.json";
    private const int ContextLines = 3;
    private const long MaxDiffCells = 4_000_000;

    /// <summary>Verifies (or, with the update variable set, rewrites) the golden for a fixture.</summary>
    public static void Verify(string fixtureRelPath, Descriptor descriptor)
    {
        var update = Environment.GetEnvironmentVariable(UpdateVariable) == "1";
        Verify(fixtureRelPath, descriptor, FixturesRoot(update), update);
    }

    /// <summary>Verifies or rewrites a golden under an explicit root. Used by the harness's own tests.</summary>
    public static void Verify(string fixtureRelPath, Descriptor descriptor, string root, bool update)
    {
        ArgumentException.ThrowIfNullOrEmpty(fixtureRelPath);
        var path = Path.Combine(root, fixtureRelPath.Replace('/', Path.DirectorySeparatorChar) + GoldenSuffix);
        var actual = DescriptorWriter.ToBytes(descriptor);

        if (update)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, actual);
            return;
        }

        if (!File.Exists(path))
        {
            throw new XunitException($"Golden file is missing: {path}\nSet {UpdateVariable}=1 to create it, then review it.");
        }

        var expected = File.ReadAllBytes(path);
        if (expected.AsSpan().SequenceEqual(actual))
        {
            return;
        }

        var diff = UnifiedDiff(Decode(expected), Decode(actual));
        throw new XunitException($"Golden mismatch: {path}\nSet {UpdateVariable}=1 to rewrite it if the change is intended.\n{diff}");
    }

    /// <summary>Renders a unified line diff of two texts with three lines of context.</summary>
    public static string UnifiedDiff(string expected, string actual)
    {
        var a = SplitLines(expected);
        var b = SplitLines(actual);
        var builder = new StringBuilder();
        builder.Append("--- expected\n+++ actual\n");

        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
        {
            suffix++;
        }

        var middleA = a.Length - prefix - suffix;
        var middleB = b.Length - prefix - suffix;
        if (middleA == 0 && middleB == 0)
        {
            builder.Append("(texts are equal; the bytes differ, for example in line endings or a BOM)\n");
            return builder.ToString();
        }

        var ops = new List<(char Op, string Line)>();
        for (var i = 0; i < prefix; i++)
        {
            ops.Add((' ', a[i]));
        }

        if ((long)middleA * middleB > MaxDiffCells)
        {
            ops.AddRange(a.Skip(prefix).Take(middleA).Select(static l => ('-', l)));
            ops.AddRange(b.Skip(prefix).Take(middleB).Select(static l => ('+', l)));
        }
        else
        {
            ops.AddRange(DiffMiddle(a, b, prefix, middleA, middleB));
        }

        for (var i = 0; i < suffix; i++)
        {
            ops.Add((' ', a[a.Length - suffix + i]));
        }

        AppendHunks(builder, ops);
        return builder.ToString();
    }

    private static string Decode(byte[] bytes)
    {
        return new UTF8Encoding(false, false).GetString(bytes);
    }

    private static string[] SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }

        var lines = text.Split('\n');
        return text.EndsWith('\n') ? lines[..^1] : lines;
    }

    private static List<(char Op, string Line)> DiffMiddle(string[] a, string[] b, int offset, int n, int m)
    {
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = a[offset + i] == b[offset + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var ops = new List<(char Op, string Line)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[offset + x] == b[offset + y])
            {
                ops.Add((' ', a[offset + x]));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                ops.Add(('-', a[offset + x]));
                x++;
            }
            else
            {
                ops.Add(('+', b[offset + y]));
                y++;
            }
        }

        while (x < n)
        {
            ops.Add(('-', a[offset + x++]));
        }

        while (y < m)
        {
            ops.Add(('+', b[offset + y++]));
        }

        return ops;
    }

    private static void AppendHunks(StringBuilder builder, List<(char Op, string Line)> ops)
    {
        var index = 0;
        while (index < ops.Count)
        {
            while (index < ops.Count && ops[index].Op == ' ')
            {
                index++;
            }

            if (index >= ops.Count)
            {
                break;
            }

            var start = Math.Max(0, index - ContextLines);
            var end = index;
            while (true)
            {
                while (end < ops.Count && ops[end].Op != ' ')
                {
                    end++;
                }

                var next = end;
                while (next < ops.Count && ops[next].Op == ' ')
                {
                    next++;
                }

                if (next < ops.Count && next - end <= 2 * ContextLines)
                {
                    end = next;
                    continue;
                }

                end = Math.Min(ops.Count, end + ContextLines);
                break;
            }

            int oldStart = 1, newStart = 1;
            for (var i = 0; i < start; i++)
            {
                if (ops[i].Op != '+')
                {
                    oldStart++;
                }

                if (ops[i].Op != '-')
                {
                    newStart++;
                }
            }

            int oldCount = 0, newCount = 0;
            var body = new StringBuilder();
            for (var i = start; i < end; i++)
            {
                if (ops[i].Op != '+')
                {
                    oldCount++;
                }

                if (ops[i].Op != '-')
                {
                    newCount++;
                }

                body.Append(ops[i].Op).Append(ops[i].Line.Replace("\r", "\\r", StringComparison.Ordinal)).Append('\n');
            }

            builder.Append($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@\n").Append(body);
            index = end;
        }
    }

    // The compile-time path of this file, so the fixtures root is tests/squalify.Tests/fixtures
    // whichever folder or file the caller is in.
    private static string Here([CallerFilePath] string path = "")
    {
        return path;
    }

    /// <summary>The fixtures directory in the source tree, found from the compile-time path of this file.</summary>
    public static string SourceFixturesRoot()
    {
        return Path.Combine(Path.GetDirectoryName(Here()) ?? string.Empty, "fixtures");
    }

    private static string FixturesRoot(bool update)
    {
        var source = SourceFixturesRoot();
        if (Directory.Exists(source))
        {
            return source;
        }

        if (update)
        {
            throw new XunitException($"Cannot update goldens: the source fixtures directory was not found at {source}.");
        }

        return Path.Combine(AppContext.BaseDirectory, "fixtures");
    }
}
