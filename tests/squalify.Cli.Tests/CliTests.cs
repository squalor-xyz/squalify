// SPDX-License-Identifier: Apache-2.0
using squalor.squalify;
using squalor.squalify.Cli;

namespace squalor.squalify.Cli.Tests;

public class CliTests
{
    private static async Task<(int Code, string Out, string Err)> RunAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CommandFactory.InvokeAsync(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
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
    public async Task Cli_Profile_NotImplemented_ExitsOneWithMessage()
    {
        var (code, output, error) = await RunAsync("profile", "some-file.csv");
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains("profiling is not implemented yet", error);
    }

    [Fact]
    public async Task Cli_Profile_AllOptions_ReachesNotImplemented()
    {
        var (code, output, error) = await RunAsync("profile", "x", "-o", "y", "--include-values");
        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains("profiling is not implemented yet", error);
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
