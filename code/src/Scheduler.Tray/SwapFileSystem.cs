namespace Scheduler.Tray;

/// <summary>
/// <see cref="ISwapFileSystem"/> over the real disk (967-B). A locked file throws, which is how the swap
/// learns the old build is still in use.
/// </summary>
public sealed class SwapFileSystem : ISwapFileSystem
{
    /// <inheritdoc/>
    public bool DirectoryExists(string dir) => Directory.Exists(dir);

    /// <inheritdoc/>
    public bool FileExists(string file) => File.Exists(file);

    /// <inheritdoc/>
    public bool IsDirectory(string path) => Directory.Exists(path);

    /// <inheritdoc/>
    public IReadOnlyList<string> Entries(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var all = new List<string>(Directory.GetFileSystemEntries(dir));
        return all;
    }

    /// <inheritdoc/>
    public void CreateDirectory(string dir) => System.IO.Directory.CreateDirectory(dir);

    /// <inheritdoc/>
    public void Move(string from, string to)
    {
        if (Directory.Exists(from))
        {
            Directory.Move(from, to);
            return;
        }

        File.Move(from, to, overwrite: true);
    }

    /// <inheritdoc/>
    public void Copy(string from, string to)
    {
        var dir = Path.GetDirectoryName(to);
        if (dir is { Length: > 0 })
        {
            System.IO.Directory.CreateDirectory(dir);
        }

        File.Copy(from, to, overwrite: true);
    }

    /// <inheritdoc/>
    public void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
