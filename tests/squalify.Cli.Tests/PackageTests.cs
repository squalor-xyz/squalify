// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace squalor.squalify.Cli.Tests;

/// <summary>
/// Tests the NuGet package. They run dotnet, so the class carries the Package category and CI runs it as its own step.
/// Nothing here reaches the network: the consumer restores from the local feed only, and the library has no package
/// dependencies to fetch.
/// </summary>
[Collection(PackageCollection.Name)]
[Trait("Category", "Package")]
public sealed class PackageTests(PackageFeed feed)
{
    private static readonly string[] SyntheticLines =
    [
        "Vcc(V),Freq(GHz),Gain(dB),Status",
        "1.8,2.40,15.2,PASS",
        "1.8,2.45,15.1,PASS",
        "1.9,2.40,14.8,PASS",
        "1.9,2.45,,FAIL",
        "2.0,2.40,14.6,PASS",
    ];

    [Fact]
    public async Task Pack_WritesOneNupkg_WhoseVersionIsTheToolVersion()
    {
        Assert.True(feed.PackExitCode == 0, $"dotnet pack exited with {feed.PackExitCode}.\n{feed.PackLog}");

        var expectedName = Path.GetFileName(feed.NupkgPath(ToolInfo.Version));
        var names = Directory.EnumerateFiles(feed.FolderPath).Select(file => Path.GetFileName(file)).ToArray();
        Assert.Equal(new[] { expectedName }, names);

        var nuspec = ReadNuspec(feed.NupkgPath(ToolInfo.Version));
        Assert.Equal(PackageFeed.PackageId, MetadataField(nuspec, "id"));
        Assert.Equal(ToolInfo.Version, MetadataField(nuspec, "version"));

        using var work = TempFolder.Create("squalify-pack-");
        var descriptor = await CliDescriptor(WriteSyntheticCsv(work.Path));
        Assert.Equal(ToolInfo.Version, ToolVersion(descriptor));
    }

    [Fact]
    public async Task Consumer_RestoredFromTheLocalFeed_WritesTheSameBytesAsTheCli()
    {
        Assert.True(feed.PackExitCode == 0, $"dotnet pack exited with {feed.PackExitCode}.\n{feed.PackLog}");
        Assert.True(File.Exists(feed.NupkgPath(ToolInfo.Version)), $"The feed has no {Path.GetFileName(feed.NupkgPath(ToolInfo.Version))}.");

        using var work = TempFolder.Create("squalify-consumer-");
        var input = WriteSyntheticCsv(work.Path);
        var expected = await CliDescriptor(input);

        var consumer = Directory.CreateDirectory(Path.Combine(work.Path, "consumer")).FullName;
        File.WriteAllText(Path.Combine(consumer, "consumer.csproj"), ConsumerProject(ToolInfo.Version), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(consumer, "Program.cs"), ConsumerProgram, new UTF8Encoding(false));
        WriteNuGetConfig(consumer, feed.FolderPath);

        // NuGet trusts a cached package of the same version and does not look at the feed. The cached copy of this
        // package id is removed before the restore, and again afterwards.
        DeleteCachedPackage();
        try
        {
            var restore = DotNetTool.Run(consumer, "restore", "consumer.csproj", "--configfile", "NuGet.Config");
            Assert.True(restore.ExitCode == 0, $"dotnet restore exited with {restore.ExitCode}.\n{restore.Log}");

            var build = DotNetTool.Run(consumer, "build", "consumer.csproj", "--configuration", "Release", "--no-restore", "-p:UseSharedCompilation=false");
            Assert.True(build.ExitCode == 0, $"dotnet build exited with {build.ExitCode}.\n{build.Log}");

            var dll = Path.Combine(consumer, "bin", "Release", "net10.0", "consumer.dll");
            var run = DotNetTool.Run(consumer, dll, input);
            Assert.True(run.ExitCode == 0, $"The consumer exited with {run.ExitCode}.\n{run.Log}");

            Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(run.Stdout));
            Assert.True(expected.AsSpan().SequenceEqual(run.Stdout), "The consumer's descriptor differs from the CLI's in bytes.");
            Assert.Equal(ToolInfo.Version, ToolVersion(run.Stdout));
        }
        finally
        {
            DeleteCachedPackage();
        }
    }

    /// <summary>Writes the synthetic lab-style table that both the CLI and the consumer profile.</summary>
    private static string WriteSyntheticCsv(string folder)
    {
        var path = Path.Combine(folder, "synthetic.csv");
        File.WriteAllText(path, string.Join('\n', SyntheticLines) + "\n", new UTF8Encoding(false));
        return path;
    }

    /// <summary>The descriptor bytes that the CLI writes for a file, in process.</summary>
    private static async Task<byte[]> CliDescriptor(string path)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CommandFactory.InvokeAsync(["profile", path], stdout, stderr);
        Assert.True(code == 0, $"squalify profile exited with {code}.\n{stderr}");
        return Encoding.UTF8.GetBytes(stdout.ToString());
    }

    private static string ToolVersion(byte[] descriptor)
    {
        using var json = JsonDocument.Parse(descriptor);
        return json.RootElement.GetProperty("tool").GetProperty("version").GetString()!;
    }

    /// <summary>Reads the root element of the nuspec inside a nupkg.</summary>
    private static XElement ReadNuspec(string nupkg)
    {
        Assert.True(File.Exists(nupkg), $"The package {Path.GetFileName(nupkg)} does not exist.");
        using var archive = ZipFile.OpenRead(nupkg);
        var entry = archive.GetEntry(PackageFeed.PackageId + ".nuspec")
            ?? throw new InvalidDataException($"{Path.GetFileName(nupkg)} has no {PackageFeed.PackageId}.nuspec.");
        using var stream = entry.Open();
        return XDocument.Load(stream).Root!;
    }

    private static string MetadataField(XElement nuspec, string name)
    {
        var ns = nuspec.Name.Namespace;
        return nuspec.Element(ns + "metadata")?.Element(ns + name)?.Value
            ?? throw new InvalidDataException($"The nuspec has no metadata/{name}.");
    }

    private static string ConsumerProject(string version) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <UseAppHost>false</UseAppHost>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="{PackageFeed.PackageId}" Version="{version}" />
          </ItemGroup>
        </Project>
        """;

    private const string ConsumerProgram = """
        using System;
        using squalor.squalify;

        var descriptor = Profiler.Profile(args[0], ProfileOptions.Create(false, Array.Empty<string>()));
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(DescriptorWriter.ToBytes(descriptor));
        """;

    /// <summary>
    /// Writes a NuGet.Config with the local feed as the only source. <c>clear</c> drops the user's sources, so the
    /// restore cannot reach nuget.org or any other feed. The library has no package dependencies yet. When it gains one
    /// that is not in the NuGet cache, this restore fails, and the source list must then be decided by the owner.
    /// </summary>
    private static void WriteNuGetConfig(string folder, string feedFolder)
    {
        var document = new XDocument(
            new XElement(
                "configuration",
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement("add", new XAttribute("key", "local"), new XAttribute("value", feedFolder)))));
        document.Save(Path.Combine(folder, "NuGet.Config"));
    }

    /// <summary>Removes this package id from the NuGet global packages folder, so the next restore reads the feed.</summary>
    private static void DeleteCachedPackage()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        var packages = string.IsNullOrEmpty(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            : configured;
        TempFolder.DeleteTree(Path.Combine(packages, PackageFeed.PackageId));
    }
}
