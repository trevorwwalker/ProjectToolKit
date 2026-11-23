using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Core.Locks;

namespace Scheduler.Host.Tick;

/// <summary>
/// Dynamic locks (design <c>dynamic-locks.md</c>, DL-20, DL-21, DL-22): the lock table's file,
/// <c>&lt;log&gt;\locks\&lt;repository key&gt;.json</c>, one per repository. Only the scheduler writes it (tmp then
/// rename, as CM-1). A missing or unreadable file reads as an empty table, and the log says so once.
/// </summary>
public sealed class LockTableFile
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly string _repo;
    private readonly ISchedulerLog? _log;
    private string _logged = "";

    /// <summary>The table of <paramref name="repo"/> under <paramref name="logDir"/>.</summary>
    public LockTableFile(string logDir, string repo, ISchedulerLog? log)
    {
        _repo = repo;
        _log = log;
        Path = LockTableText.PathIn(logDir, repo);
    }

    /// <summary>The file's path.</summary>
    public string Path { get; }

    /// <summary>The table as the file has it; an empty one for the repository when it has none (DL-21).</summary>
    public LockTable Read()
    {
        string? json = null;
        try
        {
            json = File.Exists(Path) ? File.ReadAllText(Path, Utf8) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            json = "";   // held or unreadable: read as an unreadable table
        }

        var (table, error) = LockTableText.Read(json, _repo);
        if (error.Length > 0 && error != _logged)
        {
            _log?.Write($"locks: {error}");
        }

        _logged = error;
        return table;
    }

    /// <summary>Writes the table: tmp then rename, so a reader never sees half of it.</summary>
    public void Save(LockTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, LockTableText.Write(table), Utf8);
            File.Move(tmp, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log?.Write($"locks: the lock table could not be written: {e.Message}");
        }
    }
}
