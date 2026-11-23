namespace Scheduler.Host.Tests.Slots;

/// <summary>A temporary folder removed (read-only files included) on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = Scheduler.TestSupport.TestTmp.NewPath("t");

    public TempDir() => Directory.CreateDirectory(Path);

    /// <summary>A folder in %TEMP%, outside every repository and toolkit: only for tests that need that and write no repositories (card 1042-B).</summary>
    public static TempDir InSystemTemp() => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ptk-out-" + Guid.NewGuid().ToString("N")[..10]));

    private TempDir(string path)
    {
        Path = path;
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(f, FileAttributes.Normal);
            }
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
