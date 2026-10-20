// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>An <see cref="IProfileSource"/> over a file on disk.</summary>
public sealed class FileSource : IProfileSource
{
    private readonly string _path;

    /// <summary>Creates a source for a file.</summary>
    /// <param name="path">The path of an existing file.</param>
    /// <exception cref="SqualifyException">The path is a directory, or the file does not exist.</exception>
    public FileSource(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (Directory.Exists(path))
        {
            throw new SqualifyException($"Cannot profile '{path}': it is a directory, and directory input is not supported.");
        }

        if (!File.Exists(path))
        {
            throw new SqualifyException($"Cannot read '{path}': file not found.");
        }

        _path = path;
        Name = Path.GetFileName(path);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public long Length
    {
        get
        {
            try
            {
                return new FileInfo(_path).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SqualifyException($"Cannot read '{_path}': {ex.Message}", ex);
            }
        }
    }

    /// <inheritdoc />
    public Stream Open()
    {
        try
        {
            return new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SqualifyException($"Cannot read '{_path}': {ex.Message}", ex);
        }
    }
}
