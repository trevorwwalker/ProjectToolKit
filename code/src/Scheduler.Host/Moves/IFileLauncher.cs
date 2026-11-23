namespace Scheduler.Host.Moves;

/// <summary>Opens a file with the program this PC opens it with (MOVE-9: the .md handler).</summary>
public interface IFileLauncher
{
    /// <summary>Open <paramref name="file"/>; throws when it cannot.</summary>
    void Open(string file);
}
