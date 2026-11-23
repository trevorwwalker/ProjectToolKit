namespace Scheduler.Host.Processes;

/// <summary>
/// 966-B: wakes the scheduler loop when a file appears in, or is renamed into, a folder - a worker's move
/// request (written as .tmp, renamed to .move) or a planner-outbox batch - instead of waiting for the poll.
/// A missing folder is created when <c>create</c> says so (the scheduler's own moves folder); otherwise
/// it, like one that cannot be watched, is left to the poll.
/// </summary>
public sealed class FolderWake : IDisposable
{
    private readonly FileSystemWatcher? _watcher;

    public FolderWake(string folder, Action wake, bool create)
    {
        try
        {
            if (create)
            {
                _ = Directory.CreateDirectory(folder);
            }
            else if (!Directory.Exists(folder))
            {
                return;
            }

            _watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            _watcher.Created += (_, _) => wake();
            _watcher.Renamed += (_, _) => wake();
            _watcher.Changed += (_, _) => wake();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    /// <summary>True when the folder is being watched.</summary>
    public bool Watching => _watcher is not null;

    public void Dispose() => _watcher?.Dispose();
}
