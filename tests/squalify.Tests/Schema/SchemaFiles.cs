// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify.Tests.Schema;

/// <summary>
/// Where the schema tests read the published schema and the descriptor document. They read the copies that the test project
/// puts in its output directory (<c>schema/descriptor-v1.schema.json</c> and <c>docs/descriptor.md</c>), never the source
/// tree, so the tests do not depend on the layout of the checkout (owner decision S12-9).
/// </summary>
internal static class SchemaFiles
{
    /// <summary>The path of the schema relative to a root.</summary>
    public const string SchemaRelativePath = "schema/descriptor-v1.schema.json";

    /// <summary>The path of the descriptor document relative to a root.</summary>
    public const string DocRelativePath = "docs/descriptor.md";

    private static readonly Lazy<SubsetValidator> Shared = new(() => SubsetValidator.Load(ReadSchema()));

    /// <summary>The directory that holds the copies: the test output directory.</summary>
    public static string Root => AppContext.BaseDirectory;

    /// <summary>The loaded schema of the output copy.</summary>
    public static SubsetValidator Validator => Shared.Value;

    /// <summary>Reads the schema text under a root (the output directory by default).</summary>
    /// <param name="root">A directory that holds <c>schema/descriptor-v1.schema.json</c>.</param>
    /// <returns>The text.</returns>
    /// <exception cref="FileNotFoundException">The file is not under the root.</exception>
    public static string ReadSchema(string? root = null)
    {
        return Read(root ?? Root, SchemaRelativePath);
    }

    /// <summary>Reads <c>docs/descriptor.md</c> under a root (the output directory by default).</summary>
    /// <param name="root">A directory that holds <c>docs/descriptor.md</c>.</param>
    /// <returns>The text, with LF line ends.</returns>
    /// <exception cref="FileNotFoundException">The file is not under the root.</exception>
    public static string ReadDoc(string? root = null)
    {
        return Read(root ?? Root, DocRelativePath).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Read(string root, string relative)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{relative} is not in the test output ({root}); the test project copies it there from the repository.", path);
        }

        return File.ReadAllText(path);
    }
}
