// SPDX-License-Identifier: Apache-2.0
using System.CommandLine;
using System.CommandLine.Invocation;

namespace squalor.squalify.Cli;

/// <summary>Builds and invokes the squalify command line.</summary>
public static class CommandFactory
{
    /// <summary>Builds the root command.</summary>
    /// <param name="stdout">Standard output writer.</param>
    /// <param name="stderr">Standard error writer.</param>
    /// <returns>The root command.</returns>
    public static RootCommand CreateRootCommand(TextWriter stdout, TextWriter stderr)
    {
        var root = new RootCommand("Profile one data file into a deterministic JSON descriptor");
        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Print full exception details",
            Recursive = true,
        };
        root.Options.Add(verboseOption);

        // Print ToolInfo.Version rather than the System.CommandLine default, so the descriptor and --version cannot disagree.
        foreach (var option in root.Options)
        {
            if (option is VersionOption versionOption)
                versionOption.Action = new VersionAction(stdout);
        }

        root.Subcommands.Add(CreateProfileCommand(stdout, stderr, verboseOption));
        return root;
    }

    /// <summary>Parses and runs the command line, returning the process exit code.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="stdout">Standard output writer.</param>
    /// <param name="stderr">Standard error writer.</param>
    /// <returns>0 on success, 1 on any error.</returns>
    public static async Task<int> InvokeAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var root = CreateRootCommand(stdout, stderr);
        var parseResult = root.Parse(args);
        parseResult.InvocationConfiguration.Error = stderr;
        parseResult.InvocationConfiguration.ProcessTerminationTimeout = null;
        parseResult.InvocationConfiguration.Output = parseResult.Errors.Count > 0 ? stderr : stdout;
        return await parseResult.InvokeAsync();
    }

    private static Command CreateProfileCommand(TextWriter stdout, TextWriter stderr, Option<bool> verboseOption)
    {
        var path = new Argument<string>("path") { Description = "Data file to profile" };
        var output = new Option<string?>("--output", "-o") { Description = "Write the descriptor to this file instead of stdout" };
        var includeValues = new Option<bool>("--include-values") { Description = "Include literal data values in the descriptor" };
        var command = new Command("profile", "Profile a data file and write its descriptor") { path, output, includeValues };
        command.SetAction(async (result, _) => await CliApp.ProfileAsync(
            result.GetValue(path)!,
            result.GetValue(output),
            result.GetValue(includeValues),
            stdout,
            stderr,
            result.GetValue(verboseOption)));
        return command;
    }

    private sealed class VersionAction(TextWriter stdout) : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            stdout.WriteLine(ToolInfo.Version);
            return 0;
        }
    }
}
