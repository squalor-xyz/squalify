// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using squalor.squalify;
using squalor.squalify.Cli;

namespace squalor.squalify.Cli.Tests;

public sealed class CliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "squalify-cli-" + Guid.NewGuid().ToString("N"));

    public CliTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private static async Task<(int Code, string Out, string Err)> RunAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CommandFactory.InvokeAsync(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private string WriteInput(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static string Expected(string path, bool includeValues = false)
    {
        var descriptor = Profiler.Profile(path, ProfileOptions.Create(includeValues, Array.Empty<string>()));
        return Encoding.UTF8.GetString(DescriptorWriter.ToBytes(descriptor));
    }

    [Fact]
    public async Task Cli_Version_PrintsToolVersion()
    {
        var (code, output, _) = await RunAsync("--version");
        Assert.Equal(0, code);
        Assert.Equal(ToolInfo.Version + Environment.NewLine, output);
    }

    [Fact]
    public async Task Cli_Profile_MissingPath_ExitsOne()
    {
        var (code, output, error) = await RunAsync("profile");
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task Cli_Profile_WritesDescriptorToStdout()
    {
        var path = WriteInput("sample.txt", "café one\nline two\n");
        var (code, output, error) = await RunAsync("profile", path);
        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error);
        Assert.Equal(Expected(path), output);
        Assert.EndsWith("}\n", output);
        Assert.DoesNotContain('\r', output);
        using var json = JsonDocument.Parse(output);
        Assert.Equal("sample.txt", json.RootElement.GetProperty("file").GetProperty("name").GetString());
        Assert.Equal("text", json.RootElement.GetProperty("format").GetProperty("family").GetString());
        Assert.DoesNotContain(_root, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_Profile_OutputFile_ReplacedAtomically()
    {
        var path = WriteInput("sample.txt", "a\nb\n");
        var target = Path.Combine(_root, "out.json");
        File.WriteAllText(target, "stale descriptor");
        File.WriteAllText(target + ".tmp", "stale partial write");

        var (code, output, error) = await RunAsync("profile", path, "-o", target);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, output);
        Assert.Equal(string.Empty, error);
        Assert.Equal(Expected(path), File.ReadAllText(target));
        Assert.Equal(DescriptorWriter.ToBytes(Profiler.Profile(path, ProfileOptions.Create(false, Array.Empty<string>()))), File.ReadAllBytes(target));
        Assert.False(File.Exists(target + ".tmp"), "the temporary file must be moved into place, not left behind");
    }

    [Fact]
    public async Task Cli_Profile_OutputFile_NewFileIsCreated()
    {
        var path = WriteInput("sample.txt", "a\n");
        var target = Path.Combine(_root, "new.json");
        var (code, _, _) = await RunAsync("profile", path, "--output", target);
        Assert.Equal(0, code);
        Assert.Equal(Expected(path), File.ReadAllText(target));
    }

    [Fact]
    public async Task Cli_Profile_OutputFile_FailedProfileLeavesTheExistingFileAlone()
    {
        var target = Path.Combine(_root, "out.json");
        File.WriteAllText(target, "previous good descriptor");
        var (code, output, error) = await RunAsync("profile", Path.Combine(_root, "missing.csv"), "-o", target);
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal("previous good descriptor", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".tmp"));
    }

    [Fact]
    public async Task Cli_Profile_MissingFile_ExitsOne()
    {
        var (code, output, error) = await RunAsync("profile", Path.Combine(_root, "missing.csv"));
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains("missing.csv", error);
    }

    [Fact]
    public async Task Cli_Profile_Directory_ExitsOne()
    {
        var (code, output, error) = await RunAsync("profile", _root);
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains("directory", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cli_Profile_EmptyPath_PrintsAPlainMessageWithoutTheParameterName()
    {
        var (code, output, error) = await RunAsync("profile", string.Empty);
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Equal("Cannot profile an empty path: give the path of a data file." + Environment.NewLine, error);
        Assert.DoesNotContain("Parameter", error, StringComparison.Ordinal);
        Assert.DoesNotContain("(path)", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_Profile_FileNameWithABackslash_ExitsOneAndNamesTheBackslash()
    {
        if (OperatingSystem.IsWindows())
        {
            // A backslash is a path separator there and cannot be in a file name.
            return;
        }

        var path = WriteInput("left\\right.csv", "a,b\n1,2\n");
        var (code, output, error) = await RunAsync("profile", path);
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains("backslash", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_root, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_Profile_IncludeValues_IsRecordedInOptions()
    {
        var path = WriteInput("sample.txt", "a\n");
        var (code, output, _) = await RunAsync("profile", path, "--include-values");
        Assert.Equal(0, code);
        Assert.Equal(Expected(path, includeValues: true), output);
        using var json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.GetProperty("options").GetProperty("includeValues").GetBoolean());
    }

    [Fact]
    public async Task Cli_Profile_IncludeValuesFor_IsRepeatable_SortedInOptions_AndOpensOnlyTheNamedField()
    {
        var path = WriteInput("sample.csv", "id,a,b\n1,CANARY-1,CANARY-2\n2,CANARY-1,CANARY-3\n3,CANARY-1,CANARY-2\n4,CANARY-1,CANARY-3\n");
        var (code, output, error) = await RunAsync("profile", path, "--include-values-for", "b", "--include-values-for", "B", "--include-values-for", "nothing");
        Assert.Equal(string.Empty, error);
        Assert.Equal(0, code);
        using var json = JsonDocument.Parse(output);
        var options = json.RootElement.GetProperty("options");
        Assert.False(options.GetProperty("includeValues").GetBoolean());
        Assert.Equal(new[] { "B", "b", "nothing" }, options.GetProperty("includeValuesFor").EnumerateArray().Select(static e => e.GetString()).ToArray());
        var fields = json.RootElement.GetProperty("table").GetProperty("fields");
        Assert.False(fields[1].TryGetProperty("top", out _));
        Assert.Equal("CANARY-2", fields[2].GetProperty("top").GetProperty("items")[0].GetProperty("value").GetString());
        var unmatched = json.RootElement.GetProperty("issues").EnumerateArray().Single(static i => i.GetProperty("code").GetString() == "includeValuesForUnmatched");
        Assert.Equal(new[] { "nothing" }, unmatched.GetProperty("names").EnumerateArray().Select(static e => e.GetString()).ToArray());
        Assert.Equal(1, unmatched.GetProperty("count").GetInt64());
        Assert.DoesNotContain("CANARY-1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_Profile_IncludeValuesFor_WithoutAName_ExitsOne()
    {
        var path = WriteInput("sample.txt", "a\n");
        var (code, output, error) = await RunAsync("profile", path, "--include-values-for");
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task Cli_Profile_MaxBytes_ProfilesAPrefix_AndWritesTheScope()
    {
        var path = WriteInput("rows.csv", "id,v\n" + string.Concat(Enumerable.Range(1, 400).Select(static i => i + "," + (i * 2) + "\n")));
        var (code, output, error) = await RunAsync("profile", path, "--max-bytes", "500");
        Assert.Equal(string.Empty, error);
        Assert.Equal(0, code);
        var expected = Profiler.Profile(path, ProfileOptions.Create(false, Array.Empty<string>(), 500));
        Assert.Equal(Encoding.UTF8.GetString(DescriptorWriter.ToBytes(expected)), output);
        using var json = JsonDocument.Parse(output);
        var scope = json.RootElement.GetProperty("scope");
        Assert.Equal(500, scope.GetProperty("maxBytes").GetInt64());
        Assert.InRange(scope.GetProperty("bytesRead").GetInt64(), 1, 500);
        Assert.False(scope.GetProperty("complete").GetBoolean());
        var file = json.RootElement.GetProperty("file");
        Assert.Equal(new FileInfo(path).Length, file.GetProperty("size").GetInt64());
        Assert.False(file.TryGetProperty("sha256", out _));
    }

    [Fact]
    public async Task Cli_Profile_WithoutMaxBytes_WritesNoScope()
    {
        var path = WriteInput("rows.csv", "id,v\n1,2\n");
        var (code, output, _) = await RunAsync("profile", path);
        Assert.Equal(0, code);
        Assert.DoesNotContain("\"scope\"", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0", "positive")]
    [InlineData("-5", "positive")]
    [InlineData("abc", "--max-bytes")]
    public async Task Cli_Profile_MaxBytes_NotAPositiveNumber_ExitsOne(string value, string expectedInMessage)
    {
        var path = WriteInput("rows.csv", "id,v\n1,2\n");
        var (code, output, error) = await RunAsync("profile", path, "--max-bytes", value);
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains(expectedInMessage, error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unrecognized", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_Profile_MaxFirstRecordBytes_IsParsed_AndAnAcceptedFileGivesTheSameDescriptor()
    {
        var path = WriteInput("rows.csv", "id,v\n" + string.Concat(Enumerable.Range(1, 400).Select(static i => i + "," + (i * 2) + "\n")));
        var (code, output, error) = await RunAsync("profile", path, "--max-bytes", "500", "--max-first-record-bytes", "5");
        Assert.Equal(string.Empty, error);
        Assert.Equal(0, code);
        var plain = await RunAsync("profile", path, "--max-bytes", "500");
        Assert.Equal(plain.Out, output);
        var expected = Profiler.Profile(path, ProfileOptions.Create(false, Array.Empty<string>(), 500, 5));
        Assert.Equal(Encoding.UTF8.GetString(DescriptorWriter.ToBytes(expected)), output);
    }

    [Fact]
    public async Task Cli_Profile_MaxFirstRecordBytes_FirstRecordTooLong_PrintsTheMessage_ExitsOne_WritesNothing()
    {
        var path = WriteInput("rows.csv", "id,value,label\n1,2,x\n");
        var target = Path.Combine(_root, "out.json");
        File.WriteAllText(target, "keep");
        var (code, output, error) = await RunAsync("profile", path, "--max-bytes", "100", "--max-first-record-bytes", "10", "-o", target);
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        var refused = Assert.Throws<SqualifyException>(() => Profiler.Profile(path, ProfileOptions.Create(false, Array.Empty<string>(), 100, 10)));
        Assert.Contains("--max-first-record-bytes", refused.Message, StringComparison.Ordinal);
        Assert.Equal(refused.Message + Environment.NewLine, error);
        Assert.Equal("keep", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".tmp"));

        var (code2, output2, error2) = await RunAsync("profile", path, "--max-bytes", "100", "--max-first-record-bytes", "10");
        Assert.Equal(1, code2);
        Assert.Equal(string.Empty, output2);
        Assert.Equal(error, error2);
    }

    [Theory]
    [InlineData("0", "positive")]
    [InlineData("-5", "positive")]
    [InlineData("abc", "--max-first-record-bytes")]
    public async Task Cli_Profile_MaxFirstRecordBytes_NotAPositiveNumber_ExitsOne(string value, string expectedInMessage)
    {
        var path = WriteInput("rows.csv", "id,v\n1,2\n");
        var (code, output, error) = await RunAsync("profile", path, "--max-bytes", "100", "--max-first-record-bytes", value);
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains(expectedInMessage, error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unrecognized", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_Profile_MaxFirstRecordBytes_WithoutMaxBytes_ExitsOne()
    {
        var path = WriteInput("rows.csv", "id,v\n1,2\n");
        var (code, output, error) = await RunAsync("profile", path, "--max-first-record-bytes", "100");
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains("--max-bytes", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unrecognized", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_Profile_LayoutOnly_IsParsed_AndWritesTheLayoutOnlyDescriptor()
    {
        var path = WriteInput("rows.csv", "id,v(V)\n1,2\n3,4\n5,6\n");
        var (code, output, error) = await RunAsync("profile", path, "--layout-only");
        Assert.Equal(string.Empty, error);
        Assert.Equal(0, code);
        var expected = Profiler.Profile(path, ProfileOptions.Create(false, Array.Empty<string>(), layoutOnly: true));
        Assert.Equal(Encoding.UTF8.GetString(DescriptorWriter.ToBytes(expected)), output);
        using var json = JsonDocument.Parse(output);
        Assert.True(json.RootElement.GetProperty("layoutOnly").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("hints").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("fingerprint").GetProperty("typed").ValueKind);
        Assert.Equal(3, json.RootElement.GetProperty("table").GetProperty("rows").GetInt32());
        Assert.False(json.RootElement.GetProperty("table").GetProperty("fields")[0].TryGetProperty("type", out _));
    }

    [Fact]
    public async Task Cli_Profile_WithoutLayoutOnly_WritesNoMarker()
    {
        var path = WriteInput("rows.csv", "id,v\n1,2\n");
        var (code, output, _) = await RunAsync("profile", path);
        Assert.Equal(0, code);
        Assert.DoesNotContain("layoutOnly", output, StringComparison.Ordinal);
        Assert.Equal(Expected(path), output);
    }

    [Fact]
    public async Task Cli_Profile_LayoutOnly_WorksWithTheOtherOptions()
    {
        var path = WriteInput("rows.csv", "# lab\nLot=A1\nid,v\n" + string.Concat(Enumerable.Range(1, 400).Select(static i => i + "," + (i * 2) + "\n")));
        var (code, output, error) = await RunAsync("profile", path, "--layout-only", "--max-bytes", "500", "--max-first-record-bytes", "50", "--include-values", "--include-values-for", "Lot");
        Assert.Equal(string.Empty, error);
        Assert.Equal(0, code);
        var expected = Profiler.Profile(path, ProfileOptions.Create(true, ["Lot"], 500, 50, layoutOnly: true));
        Assert.Equal(Encoding.UTF8.GetString(DescriptorWriter.ToBytes(expected)), output);
        using var json = JsonDocument.Parse(output);
        Assert.Equal(500, json.RootElement.GetProperty("scope").GetProperty("maxBytes").GetInt64());
        Assert.True(json.RootElement.GetProperty("layoutOnly").GetBoolean());
    }

    [Fact]
    public async Task Cli_Profile_Help_DocumentsLayoutOnly()
    {
        var (code, output, error) = await RunAsync("profile", "--help");
        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error);
        var flat = System.Text.RegularExpressions.Regex.Replace(output, @"\s+", " ");
        Assert.Contains("--layout-only", flat, StringComparison.Ordinal);
        foreach (var phrase in new[] { "encoding", "dialect", "layout regions", "header names", "row classes", "skip", "hints", "layoutOnly" })
        {
            Assert.Contains(phrase, flat, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Cli_Profile_TwiceGivesIdenticalOutput()
    {
        var path = WriteInput("sample.txt", "x\ny\n");
        var first = await RunAsync("profile", path);
        var second = await RunAsync("profile", path);
        Assert.Equal(first.Out, second.Out);
    }

    [Fact]
    public async Task CliApp_RunAsync_UnexpectedException_ExitsOneWithMessage()
    {
        var stderr = new StringWriter();
        var code = await CliApp.RunAsync(stderr, false, () => throw new InvalidOperationException("boom"));
        Assert.Equal(1, code);
        Assert.Contains("boom", stderr.ToString());
    }
}
