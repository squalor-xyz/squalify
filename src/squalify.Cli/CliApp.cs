// SPDX-License-Identifier: Apache-2.0
using System.Text;

namespace squalor.squalify.Cli;

internal static class CliApp
{
    /// <summary>The exit gate: 0 on success, 1 on any error, with the message on stderr.</summary>
    internal static async Task<int> RunAsync(TextWriter stderr, bool verbose, Func<Task<int>> action)
    {
        try
        {
            return await action();
        }
        catch (SqualifyException ex)
        {
            stderr.WriteLine(ex.Message);
            if (ex.InnerException is not null)
            {
                if (verbose)
                    stderr.WriteLine(ex.InnerException.ToString());
                else if (!ex.Message.Contains(ex.InnerException.Message, StringComparison.Ordinal))
                    stderr.WriteLine(ex.InnerException.Message);
            }
            return 1;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stderr.WriteLine(verbose ? ex.ToString() : ex.Message);
            return 1;
        }
    }

    internal static Task<int> ProfileAsync(string path, string? output, bool includeValues, IReadOnlyList<string> includeValuesFor, TextWriter stdout, TextWriter stderr, bool verbose)
    {
        return RunAsync(stderr, verbose, () =>
        {
            // Profile completely before writing anything, so a failure leaves stdout and any existing output file untouched.
            var descriptor = Profiler.Profile(path, ProfileOptions.Create(includeValues, includeValuesFor));
            var bytes = DescriptorWriter.ToBytes(descriptor);
            if (output is null)
            {
                stdout.Write(new UTF8Encoding(false, true).GetString(bytes));
                stdout.Flush();
            }
            else
            {
                WriteReplacing(output, bytes);
            }

            return Task.FromResult(0);
        });
    }

    // Writes <output>.tmp, then moves it over the output file, so a reader sees the old file or the new one, never half of it.
    private static void WriteReplacing(string output, byte[] bytes)
    {
        var temporary = output + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, output, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            throw new SqualifyException($"Cannot write '{output}': {ex.Message}", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the original error is the one to report.
        }
    }
}
