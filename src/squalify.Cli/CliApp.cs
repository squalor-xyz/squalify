// SPDX-License-Identifier: Apache-2.0
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

    internal static Task<int> ProfileAsync(string path, string? output, bool includeValues, TextWriter stderr, bool verbose)
    {
        return RunAsync(stderr, verbose, () =>
            throw new SqualifyException("profiling is not implemented yet"));
    }
}
