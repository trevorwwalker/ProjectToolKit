// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
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

    /// <summary>The file of refused pushes beside the table (DL-26): one UTC time per line.</summary>
    public string PushRefusalsPath => PushRefusalsOf(Path);

    /// <summary>The refused-pushes file that goes with the table file <paramref name="tablePath"/>.</summary>
    public static string PushRefusalsOf(string tablePath) =>
        System.IO.Path.ChangeExtension(tablePath, ".pushes.log");

    /// <summary>
    /// DL-26: <c>check-push</c> (another process) records one refused push at <paramref name="at"/> by appending a line;
    /// the scheduler counts these lines on the page. Never throws: a push is refused all the same.
    /// </summary>
    public static void AppendPushRefused(string tablePath, DateTimeOffset at)
    {
        try
        {
            File.AppendAllText(PushRefusalsOf(tablePath), at.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine, Utf8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the count is lost, the refusal is not
        }
    }

    /// <summary>The times of the pushes <c>check-push</c> refused, oldest first; none when the file is missing or unreadable.</summary>
    public IReadOnlyList<DateTimeOffset> ReadPushRefusals()
    {
        var times = new List<DateTimeOffset>();
        try
        {
            if (File.Exists(PushRefusalsPath))
            {
                foreach (var line in File.ReadAllLines(PushRefusalsPath, Utf8))
                {
                    if (DateTimeOffset.TryParse(line, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
                    {
                        times.Add(at);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // being appended to: the next read has it
        }

        return times;
    }

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
