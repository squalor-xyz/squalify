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
        var includeValuesFor = new Option<string[]>("--include-values-for")
        {
            Description = "Include literal values only for the named preamble key, annotation label, key=value key or column (repeatable; matched whole, ASCII case-folded)",
            AllowMultipleArgumentsPerToken = false,
            Arity = ArgumentArity.OneOrMore,
        };
        var maxBytes = new Option<long?>("--max-bytes")
        {
            Description = "Read at most this many bytes: a text file is cut after its last whole line (past the limit when the first line is longer), a binary file at the limit. The descriptor gets a scope section, and no file.sha256 unless the whole file was read",
        };
        var maxFirstRecordBytes = new Option<long?>("--max-first-record-bytes")
        {
            Description = "With --max-bytes: fail, profiling nothing, when the first record (line terminator included) is longer than this many bytes, instead of reading it to its end. Off by default",
        };
        var layoutOnly = new Option<bool>("--layout-only")
        {
            Description = "Compute only the encoding, dialect, layout regions, header names and row classes, and skip the per-field types, statistics and sketches, the hints and the typed fingerprint. The descriptor says so with layoutOnly: true, and writes null for hints and fingerprint.typed and only the names for each field. --include-values then opens only the preamble and annotation values",
        };
        var command = new Command("profile", "Profile a data file and write its descriptor") { path, output, includeValues, includeValuesFor, maxBytes, maxFirstRecordBytes, layoutOnly };
        command.SetAction(async (result, _) => await CliApp.ProfileAsync(
            result.GetValue(path)!,
            result.GetValue(output),
            result.GetValue(includeValues),
            result.GetValue(includeValuesFor) ?? [],
            result.GetValue(maxBytes),
            result.GetValue(maxFirstRecordBytes),
            result.GetValue(layoutOnly),
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
