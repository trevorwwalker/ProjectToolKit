namespace Scheduler.Web.Tests.Server.Fakes;

/// <summary>A throwaway log folder, deleted with the object.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = Scheduler.TestSupport.TestTmp.NewPath("web");
        Directory.CreateDirectory(Path);
    }

    /// <summary>The folder's full path.</summary>
    public string Path { get; }

    /// <summary>Writes a file into the folder and returns its full path.</summary>
    public string Write(string name, string text)
    {
        var file = System.IO.Path.Combine(Path, name);
        File.WriteAllText(file, text);
        return file;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
