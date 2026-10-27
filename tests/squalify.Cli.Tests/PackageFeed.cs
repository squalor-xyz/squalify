// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify.Cli.Tests;

/// <summary>
/// Packs the library and the CLI tool once for the Package tests, each into its own temporary folder. The library
/// folder is the local feed that the consumer test restores from. Dispose deletes both folders.
/// </summary>
public sealed class PackageFeed : IDisposable
{
    /// <summary>The package id, which is also the assembly and namespace name.</summary>
    public const string PackageId = "squalor.squalify";

    /// <summary>The package id of the CLI tool.</summary>
    public const string CliPackageId = "squalor.squalify.Cli";

    private readonly TempFolder _folder = TempFolder.Create("squalify-feed-");
    private readonly TempFolder _cliFolder = TempFolder.Create("squalify-cli-feed-");

    /// <summary>Runs dotnet pack on the library project and on the CLI project, each into its own folder.</summary>
    public PackageFeed()
    {
        try
        {
            var root = DotNetTool.RepositoryRoot();
            var library = Pack(root, Path.Combine("src", "squalify", "squalify.csproj"), FolderPath);
            PackExitCode = library.ExitCode;
            PackLog = library.Log;
            var cli = Pack(root, Path.Combine("src", "squalify.Cli", "squalify.Cli.csproj"), CliFolderPath);
            CliPackExitCode = cli.ExitCode;
            CliPackLog = cli.Log;
        }
        catch
        {
            // A failed constructor is never disposed, so the folders would leak.
            _folder.Dispose();
            _cliFolder.Dispose();
            throw;
        }
    }

    /// <summary>The folder that holds the packed files.</summary>
    public string FolderPath => _folder.Path;

    /// <summary>The exit code of dotnet pack.</summary>
    public int PackExitCode { get; }

    /// <summary>The output of dotnet pack, for failure messages.</summary>
    public string PackLog { get; }

    /// <summary>The folder that holds the packed CLI tool.</summary>
    public string CliFolderPath => _cliFolder.Path;

    /// <summary>The exit code of dotnet pack on the CLI project.</summary>
    public int CliPackExitCode { get; }

    /// <summary>The output of dotnet pack on the CLI project, for failure messages.</summary>
    public string CliPackLog { get; }

    /// <summary>The path of the library nupkg for a version, whether or not it exists.</summary>
    public string NupkgPath(string version) => Path.Combine(FolderPath, $"{PackageId}.{version}.nupkg");

    /// <summary>The path of the library symbol package for a version, whether or not it exists.</summary>
    public string SnupkgPath(string version) => Path.Combine(FolderPath, $"{PackageId}.{version}.snupkg");

    /// <summary>The path of the CLI tool nupkg for a version, whether or not it exists.</summary>
    public string CliNupkgPath(string version) => Path.Combine(CliFolderPath, $"{CliPackageId}.{version}.nupkg");

    /// <summary>Deletes both folders.</summary>
    public void Dispose()
    {
        _folder.Dispose();
        _cliFolder.Dispose();
    }

    private static DotNetResult Pack(string root, string project, string output) =>
        DotNetTool.Run(root, "pack", Path.Combine(root, project), "--configuration", "Release", "--no-restore", "--output", output);
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
