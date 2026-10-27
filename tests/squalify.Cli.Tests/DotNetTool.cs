// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace squalor.squalify.Cli.Tests;

/// <summary>Runs the dotnet host for the Package tests.</summary>
internal static class DotNetTool
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The dotnet host that runs this test. <c>dotnet test</c> sets DOTNET_HOST_PATH to the host it runs under. Without
    /// it, the host is found under the root of the runtime that is running this test.
    /// </summary>
    public static string HostPath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        // RuntimeEnvironment.GetRuntimeDirectory() is <root>/shared/Microsoft.NETCore.App/<version>/.
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new InvalidOperationException(
            $"Cannot find the dotnet host. DOTNET_HOST_PATH is '{fromEnvironment}', and '{candidate}' does not exist.");
    }

    /// <summary>The repository root: the nearest folder above the test assembly that holds squalify.sln.</summary>
    public static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "squalify.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"squalify.sln was not found above {AppContext.BaseDirectory}.");
    }

    /// <summary>Runs dotnet with the given arguments and returns its exit code, its raw standard output and a log.</summary>
    public static DotNetResult Run(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(HostPath())
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        // Stop MSBuild worker nodes from outliving the command, so they do not hold files in the temporary folders.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start dotnet.");
        var stdout = new MemoryStream();
        var stdoutCopy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderrRead = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} did not finish within {Timeout}.");
        }

        // The parameterless wait makes sure the redirected output has been read to the end.
        process.WaitForExit();
        Task.WaitAll(stdoutCopy, stderrRead);

        var bytes = stdout.ToArray();
        var log = Encoding.UTF8.GetString(bytes) + stderrRead.Result;
        return new DotNetResult(process.ExitCode, bytes, log);
    }
}

/// <summary>The outcome of one dotnet command.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="Stdout">The standard output, byte for byte.</param>
/// <param name="Log">The standard output and standard error as text, for failure messages.</param>
internal sealed record DotNetResult(int ExitCode, byte[] Stdout, string Log);
