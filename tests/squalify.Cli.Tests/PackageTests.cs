// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace squalor.squalify.Cli.Tests;

/// <summary>
/// Tests the NuGet package. They run dotnet, so the class carries the Package category and CI runs it as its own step.
/// Nothing here reaches the network: the consumer restores from the local feed only, and the library has no package
/// dependencies to fetch. The one download is the baseline of package validation, which the solution restore
/// (before the tests) puts in the NuGet cache; the removal test restores a copy of the library from that cache.
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

        // One nupkg, plus the snupkg that carries its symbols.
        var expectedNames = new[] { feed.NupkgPath(ToolInfo.Version), feed.SnupkgPath(ToolInfo.Version) }.Select(Path.GetFileName).Order(StringComparer.Ordinal);
        var names = Directory.EnumerateFiles(feed.FolderPath).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal);
        Assert.Equal(expectedNames, names);

        var nuspec = ReadNuspec(feed.NupkgPath(ToolInfo.Version), PackageFeed.PackageId);
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
        // The consumer builds with the SDK that the repository pins.
        File.Copy(Path.Combine(DotNetTool.RepositoryRoot(), "global.json"), Path.Combine(consumer, "global.json"));

        // The restore extracts into a packages folder of this test's own. It never reads or writes the user's NuGet
        // cache, so a copy of this version left there by an earlier run cannot stand in for the feed.
        var packages = Path.Combine(work.Path, "packages");
        var restore = DotNetTool.Run(consumer, "restore", "consumer.csproj", "--configfile", "NuGet.Config", "--packages", packages);
        Assert.True(restore.ExitCode == 0, $"dotnet restore exited with {restore.ExitCode}.\n{restore.Log}");

        var packaged = Path.Combine(packages, PackageFeed.PackageId, ToolInfo.Version, "lib", "net10.0", $"{PackageFeed.PackageId}.dll");
        Assert.True(File.Exists(packaged), $"The restore did not extract the packaged library to {packaged}.");

        var build = DotNetTool.Run(consumer, "build", "consumer.csproj", "--configuration", "Release", "--no-restore", "-p:UseSharedCompilation=false");
        Assert.True(build.ExitCode == 0, $"dotnet build exited with {build.ExitCode}.\n{build.Log}");

        var dll = Path.Combine(consumer, "bin", "Release", "net10.0", "consumer.dll");
        var run = DotNetTool.Run(consumer, dll, input);
        Assert.True(run.ExitCode == 0, $"The consumer exited with {run.ExitCode}.\n{run.Log}");

        Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(run.Stdout));
        Assert.True(expected.AsSpan().SequenceEqual(run.Stdout), "The consumer's descriptor differs from the CLI's in bytes.");
        Assert.Equal(ToolInfo.Version, ToolVersion(run.Stdout));
    }

    [Fact]
    public void Package_CliToolId_IsPrefixed()
    {
        Assert.True(feed.CliPackExitCode == 0, $"dotnet pack exited with {feed.CliPackExitCode}.\n{feed.CliPackLog}");

        var nupkg = feed.CliNupkgPath(ToolInfo.Version);
        Assert.True(File.Exists(nupkg), $"The CLI pack did not write {Path.GetFileName(nupkg)}. It wrote: {string.Join(", ", Directory.EnumerateFiles(feed.CliFolderPath).Select(Path.GetFileName))}.");

        var nuspec = ReadNuspec(nupkg, PackageFeed.CliPackageId);
        Assert.Equal(PackageFeed.CliPackageId, MetadataField(nuspec, "id"));
        Assert.Equal(ToolInfo.Version, MetadataField(nuspec, "version"));

        using var archive = ZipFile.OpenRead(nupkg);
        var settings = archive.Entries.Single(entry => entry.Name == "DotnetToolSettings.xml");
        using var stream = settings.Open();
        var commands = XDocument.Load(stream).Descendants("Command").Select(command => (string?)command.Attribute("Name")).ToArray();
        Assert.Equal(new[] { "squalify" }, commands);
    }

    [Fact]
    public void Package_Library_HasSymbolsAndReadme()
    {
        Assert.True(feed.PackExitCode == 0, $"dotnet pack exited with {feed.PackExitCode}.\n{feed.PackLog}");
        Assert.True(File.Exists(feed.SnupkgPath(ToolInfo.Version)), $"The feed has no {Path.GetFileName(feed.SnupkgPath(ToolInfo.Version))}.");

        var nupkg = feed.NupkgPath(ToolInfo.Version);
        using (var archive = ZipFile.OpenRead(nupkg))
        {
            Assert.Contains(archive.Entries, entry => entry.FullName == "README.md");
        }

        var nuspec = ReadNuspec(nupkg, PackageFeed.PackageId);
        var ns = nuspec.Name.Namespace;
        var metadata = nuspec.Element(ns + "metadata")!;
        Assert.Equal("README.md", MetadataField(nuspec, "readme"));
        var license = metadata.Element(ns + "license");
        Assert.Equal("expression", (string?)license?.Attribute("type"));
        Assert.Equal("Apache-2.0", license?.Value);
        Assert.Equal("https://github.com/squalor-xyz/squalify", (string?)metadata.Element(ns + "repository")?.Attribute("url"));
    }

    [Fact]
    public void Package_BothNupkgs_ContainTheReadmeTheXmlDocFileAndTheLicense()
    {
        Assert.True(feed.PackExitCode == 0, $"dotnet pack exited with {feed.PackExitCode}.\n{feed.PackLog}");
        Assert.True(feed.CliPackExitCode == 0, $"dotnet pack exited with {feed.CliPackExitCode}.\n{feed.CliPackLog}");

        var readme = File.ReadAllBytes(Path.Combine(DotNetTool.RepositoryRoot(), "README.md"));
        var packages = new[]
        {
            (Nupkg: feed.NupkgPath(ToolInfo.Version), Id: PackageFeed.PackageId, XmlDoc: $"lib/net10.0/{PackageFeed.PackageId}.xml"),
            (Nupkg: feed.CliNupkgPath(ToolInfo.Version), Id: PackageFeed.CliPackageId, XmlDoc: $"tools/net10.0/any/{PackageFeed.PackageId}.xml"),
        };
        foreach (var (nupkg, id, xmlDoc) in packages)
        {
            var name = Path.GetFileName(nupkg);
            var nuspec = ReadNuspec(nupkg, id);
            var ns = nuspec.Name.Namespace;
            using var archive = ZipFile.OpenRead(nupkg);

            // The README: the file in the package is the repository's, and the nuspec points at it.
            Assert.Equal("README.md", MetadataField(nuspec, "readme"));
            Assert.True(EntryBytes(archive, "README.md", name).AsSpan().SequenceEqual(readme), $"The README.md in {name} differs in bytes from the repository's.");

            // The license: the nuspec carries the SPDX expression (NuGet packs no LICENSE file for an expression).
            var license = nuspec.Element(ns + "metadata")?.Element(ns + "license");
            Assert.Equal("expression", (string?)license?.Attribute("type"));
            Assert.Equal("Apache-2.0", license?.Value);

            // The XML doc file is a doc file with members, not an empty shell.
            var doc = XDocument.Parse(Encoding.UTF8.GetString(EntryBytes(archive, xmlDoc, name)));
            Assert.Equal("doc", doc.Root?.Name.LocalName);
            Assert.Equal(PackageFeed.PackageId, doc.Root!.Element("assembly")?.Element("name")?.Value);
            var members = doc.Root.Element("members")!.Elements("member").Select(member => (string?)member.Attribute("name")).ToArray();
            Assert.Contains("T:squalor.squalify.Profiler", members);
            Assert.Contains("T:squalor.squalify.Descriptor", members);
        }
    }

    private static byte[] EntryBytes(ZipArchive archive, string entryName, string packageName)
    {
        var entry = archive.GetEntry(entryName);
        Assert.True(entry is not null, $"{packageName} has no {entryName}.");
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    [Fact]
    public void Package_Library_HasEmbeddedIcon()
    {
        Assert.True(feed.PackExitCode == 0, $"dotnet pack exited with {feed.PackExitCode}.\n{feed.PackLog}");
        Assert.True(feed.CliPackExitCode == 0, $"dotnet pack exited with {feed.CliPackExitCode}.\n{feed.CliPackLog}");

        var expected = File.ReadAllBytes(Path.Combine(DotNetTool.RepositoryRoot(), "assets", "squalify-512.png"));
        foreach (var (nupkg, packageId) in new[] { (feed.NupkgPath(ToolInfo.Version), PackageFeed.PackageId), (feed.CliNupkgPath(ToolInfo.Version), PackageFeed.CliPackageId) })
        {
            var nuspec = ReadNuspec(nupkg, packageId);
            var ns = nuspec.Name.Namespace;
            Assert.Equal("squalify-512.png", MetadataField(nuspec, "icon"));
            Assert.Null(nuspec.Element(ns + "metadata")?.Element(ns + "iconUrl"));

            using var archive = ZipFile.OpenRead(nupkg);
            var entry = archive.GetEntry("squalify-512.png");
            Assert.True(entry is not null, $"{Path.GetFileName(nupkg)} has no squalify-512.png at the package root.");
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            Assert.True(expected.AsSpan().SequenceEqual(memory.ToArray()), $"The icon in {Path.GetFileName(nupkg)} differs in bytes from assets/squalify-512.png.");
        }
    }

    [Fact]
    public void Package_Readme_HasNoRelativeLinks()
    {
        // nuget.org renders the README outside the repository, so a relative target would not resolve there.
        var text = File.ReadAllText(Path.Combine(DotNetTool.RepositoryRoot(), "README.md"));
        text = Regex.Replace(text, @"^(```|~~~).*?^\1[^\n]*$", string.Empty, RegexOptions.Singleline | RegexOptions.Multiline);
        text = Regex.Replace(text, @"`[^`\n]*`", string.Empty);

        var targets = Regex.Matches(text, @"\]\(\s*<?([^)\s>]*)").Select(match => match.Groups[1].Value)
            .Concat(Regex.Matches(text, @"^ {0,3}\[[^\]\n]+\]:\s*<?(\S+)", RegexOptions.Multiline).Select(match => match.Groups[1].Value));
        var relative = targets.Where(target => !target.StartsWith('#') && !Regex.IsMatch(target, "^[A-Za-z][A-Za-z0-9+.-]*:")).ToArray();
        Assert.True(relative.Length == 0, $"README.md has relative link targets: {string.Join(", ", relative)}.");
    }

    /// <summary>
    /// Package validation is on for the library, against a baseline release from nuget.org, and the pack of main passes it:
    /// no 1.x change has broken the public API except through suppressions. The baseline is fetched at restore (a
    /// PackageDownload), so the project's assets file must list it: the pack runs with --no-restore and reads it from the
    /// NuGet cache. The CLI tool project is not validated; its properties are parsed from msbuild's JSON, so the check
    /// cannot pass on an empty or garbled read.
    /// </summary>
    [Fact]
    public void Pack_PassesAgainstBaseline_OnMain()
    {
        var root = DotNetTool.RepositoryRoot();
        var library = Path.Combine(root, "src", "squalify", "squalify.csproj");
        var properties = EvaluatedProperties(library, "EnablePackageValidation", "PackageValidationBaselineVersion");
        Assert.Equal("true", properties["EnablePackageValidation"]);
        var baseline = properties["PackageValidationBaselineVersion"];
        Assert.True(
            Version.TryParse(baseline, out var baselineVersion) && baselineVersion <= Version.Parse(ToolInfo.Version.Split('-')[0]),
            $"PackageValidationBaselineVersion '{baseline}' is not a release at or below the tool version {ToolInfo.Version}.");

        var cli = EvaluatedProperties(Path.Combine(root, "src", "squalify.Cli", "squalify.Cli.csproj"), "EnablePackageValidation", "PackageValidationBaselineVersion");
        Assert.True(cli.ContainsKey("EnablePackageValidation"), "The CLI project's properties were not read.");
        Assert.Equal("false", cli["EnablePackageValidation"]); // the SDK forces false for a PackAsTool project
        Assert.Equal(string.Empty, cli["PackageValidationBaselineVersion"]);

        var assets = File.ReadAllText(Path.Combine(root, "src", "squalify", "obj", "project.assets.json"));
        using var json = JsonDocument.Parse(assets);
        var downloads = json.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject()
            .SelectMany(framework => framework.Value.TryGetProperty("downloadDependencies", out var list) ? list.EnumerateArray() : [])
            .Select(entry => entry.GetProperty("name").GetString() + " " + entry.GetProperty("version").GetString())
            .ToArray();
        Assert.Contains($"{PackageFeed.PackageId} [{baseline}, {baseline}]", downloads);

        Assert.True(feed.PackExitCode == 0, $"dotnet pack exited with {feed.PackExitCode}.\n{feed.PackLog}");
    }

    /// <summary>
    /// A copy of the library project with one public member removed (ToolInfo.Name, which the profiler reads, so the
    /// copy swaps in the literal) fails dotnet pack with the API compatibility error CP0002: the baseline has the
    /// member and the package does not. The copy lives in a temporary folder, so the repository is untouched, and it
    /// restores from the NuGet cache that the solution restore filled with the baseline.
    /// </summary>
    [Fact]
    public void Pack_FailsWhenAPublicMemberIsRemoved()
    {
        var root = DotNetTool.RepositoryRoot();
        using var work = TempFolder.Create("squalify-removal-");
        var project = Path.Combine(work.Path, "src", "squalify");
        CopyTree(Path.Combine(root, "src", "squalify"), project);
        foreach (var file in new[] { "Directory.Build.props", "global.json", "README.md", Path.Combine("assets", "squalify-512.png") })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(work.Path, file))!);
            File.Copy(Path.Combine(root, file), Path.Combine(work.Path, file));
        }

        ReplaceOnce(Path.Combine(project, "ToolInfo.cs"), "    public const string Name = \"squalify\";", string.Empty);
        ReplaceOnce(Path.Combine(project, "Profiler.cs"), "new ToolSection(ToolInfo.Name,", "new ToolSection(\"squalify\",");

        var output = Path.Combine(work.Path, "out");
        var pack = DotNetTool.Run(work.Path, "pack", Path.Combine(project, "squalify.csproj"), "--configuration", "Release", "--output", output);
        Assert.NotEqual(0, pack.ExitCode);
        Assert.Contains("CP0002", pack.Log, StringComparison.Ordinal);
        Assert.Contains("squalor.squalify.ToolInfo.Name", pack.Log, StringComparison.Ordinal);
    }

    /// <summary>Evaluates MSBuild properties of a project (dotnet msbuild -getProperty), without building it.</summary>
    private static Dictionary<string, string> EvaluatedProperties(string project, params string[] names)
    {
        // With one property, msbuild prints a bare value and no JSON, so ask for at least two and parse the JSON.
        Assert.True(names.Length >= 2, "Ask for two properties or more.");
        var arguments = new[] { "msbuild", project }.Concat(names.Select(name => "-getProperty:" + name)).ToArray();
        var result = DotNetTool.Run(Path.GetDirectoryName(project)!, arguments);
        Assert.True(result.ExitCode == 0, $"dotnet msbuild exited with {result.ExitCode}.\n{result.Log}");
        using var json = JsonDocument.Parse(result.Stdout);
        var properties = json.RootElement.GetProperty("Properties");
        return names.ToDictionary(name => name, name => properties.GetProperty(name).GetString() ?? string.Empty);
    }

    /// <summary>Copies a folder tree, leaving out build output (bin and obj).</summary>
    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is not ("bin" or "obj"))
            {
                CopyTree(directory, Path.Combine(target, name));
            }
        }
    }

    /// <summary>Replaces one occurrence of a text in a file, and fails if the text is not there exactly once.</summary>
    private static void ReplaceOnce(string path, string oldText, string newText)
    {
        var text = File.ReadAllText(path);
        var index = text.IndexOf(oldText, StringComparison.Ordinal);
        Assert.True(index >= 0 && text.IndexOf(oldText, index + 1, StringComparison.Ordinal) < 0, $"{Path.GetFileName(path)} does not hold '{oldText}' exactly once.");
        File.WriteAllText(path, text.Remove(index, oldText.Length).Insert(index, newText), new UTF8Encoding(false));
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
    private static XElement ReadNuspec(string nupkg, string packageId)
    {
        Assert.True(File.Exists(nupkg), $"The package {Path.GetFileName(nupkg)} does not exist.");
        using var archive = ZipFile.OpenRead(nupkg);
        var entry = archive.GetEntry(packageId + ".nuspec")
            ?? throw new InvalidDataException($"{Path.GetFileName(nupkg)} has no {packageId}.nuspec.");
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
}
