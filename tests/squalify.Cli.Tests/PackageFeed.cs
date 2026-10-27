// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify.Cli.Tests;

/// <summary>
/// Packs the library once for the Package tests, into a temporary local folder feed. Dispose deletes the feed.
/// </summary>
public sealed class PackageFeed : IDisposable
{
    /// <summary>The package id, which is also the assembly and namespace name.</summary>
    public const string PackageId = "squalor.squalify";

    private readonly TempFolder _folder = TempFolder.Create("squalify-feed-");

    /// <summary>Runs dotnet pack on the library project into the feed.</summary>
    public PackageFeed()
    {
        try
        {
            var root = DotNetTool.RepositoryRoot();
            var project = Path.Combine(root, "src", "squalify", "squalify.csproj");
            var result = DotNetTool.Run(root, "pack", project, "--configuration", "Release", "--no-restore", "--output", FolderPath);
            PackExitCode = result.ExitCode;
            PackLog = result.Log;
        }
        catch
        {
            // A failed constructor is never disposed, so the feed would leak.
            _folder.Dispose();
            throw;
        }
    }

    /// <summary>The folder that holds the packed files.</summary>
    public string FolderPath => _folder.Path;

    /// <summary>The exit code of dotnet pack.</summary>
    public int PackExitCode { get; }

    /// <summary>The output of dotnet pack, for failure messages.</summary>
    public string PackLog { get; }

    /// <summary>The path of the nupkg for a version, whether or not it exists.</summary>
    public string NupkgPath(string version) => Path.Combine(FolderPath, $"{PackageId}.{version}.nupkg");

    /// <summary>Deletes the feed.</summary>
    public void Dispose() => _folder.Dispose();
}

/// <summary>Shares one <see cref="PackageFeed"/> across the Package test classes.</summary>
[CollectionDefinition(Name)]
public sealed class PackageCollection : ICollectionFixture<PackageFeed>
{
    /// <summary>The collection name.</summary>
    public const string Name = "Package";
}

/// <summary>A temporary folder that Dispose deletes.</summary>
internal sealed class TempFolder : IDisposable
{
    private TempFolder(string path)
    {
        Path = path;
    }

    /// <summary>The folder's full path.</summary>
    public string Path { get; }

    /// <summary>Creates a new folder under the system temporary folder.</summary>
    public static TempFolder Create(string prefix) => new(Directory.CreateTempSubdirectory(prefix).FullName);

    /// <summary>Deletes the folder and everything in it.</summary>
    public void Dispose() => DeleteTree(Path);

    /// <summary>Deletes a folder tree if it exists. Read-only files, which NuGet writes, are made writable first.</summary>
    public static void DeleteTree(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(folder, recursive: true);
    }
}
