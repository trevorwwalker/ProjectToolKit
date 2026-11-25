// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.Estimates;

namespace Scheduler.Host.State;

/// <summary>
/// EST-27: reads a project's <c>accounting.csv</c> and <c>review-stays.csv</c> from its log folder, and only again when
/// either file's last write moves (the <c>TrendRuns</c> cache of 770-B). A folder that cannot be read gives what it gave
/// last, or nothing. Each folder is named once, whatever the spelling of its path.
/// </summary>
public sealed class EstimateLogFolders
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime Accounting, DateTime Stays, EstimateSource Source)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times a folder's files were read (not served from the cache): EST-27's "read once".</summary>
    public int Reads { get; private set; }

    /// <summary>The folder's full path, the key of the cache (compared ignoring case).</summary>
    public static string KeyOf(string logDir) => System.IO.Path.GetFullPath(logDir).TrimEnd('\\', '/');

    /// <summary>The source of <paramref name="logDir"/>: its rows, the lines skipped, and its closed review stays.</summary>
    public EstimateSource Read(string logDir)
    {
        var key = KeyOf(logDir);
        var accountingPath = System.IO.Path.Combine(key, "accounting.csv");
        var staysPath = System.IO.Path.Combine(key, ReviewStaysFile.FileName);
        lock (_gate)
        {
            var accountingTime = File.GetLastWriteTimeUtc(accountingPath);
            var staysTime = File.GetLastWriteTimeUtc(staysPath);
            if (_cache.TryGetValue(key, out var cached) && cached.Accounting == accountingTime && cached.Stays == staysTime)
            {
                return cached.Source;
            }

            try
            {
                var rows = File.Exists(accountingPath) ? AccountingRows.Parse(File.ReadAllLines(accountingPath)) : new AccountingRowsResult([], 0);
                var stays = File.Exists(staysPath)
                    ? File.ReadAllLines(staysPath).Where(l => l.Length > 0 && l != ReviewStays.Header)
                        .Select(ReviewStays.Parse).OfType<ReviewStay>().ToList()
                    : [];
                var source = new EstimateSource(rows.Rows, rows.Skipped, new ReviewStaySource(stays, ReviewStays.KindsOf(rows.Rows)));
                _cache[key] = (accountingTime, staysTime, source);
                Reads++;
                return source;
            }
            catch (IOException) when (cached.Source is not null)
            {
                return cached.Source;
            }
            catch (IOException)
            {
                return new EstimateSource([], 0, new ReviewStaySource([], new Dictionary<string, string>()));
            }
        }
    }
}

/// <summary>
/// The projects of a pool as the estimates see them (EST-27, EST-29, EST-31): every member's <see cref="EstimatesFile"/>,
/// the pool's own report held in memory, and the one place where a change in any member's rows rebuilds every member's
/// file, because each may borrow from the pool.
/// </summary>
public sealed class EstimatesPool(IClock clock, string scheduler, ISchedulerLog? log = null)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, EstimatesFile> _members = new(StringComparer.OrdinalIgnoreCase);
    private volatile string? _json;

    /// <summary>The cached reader of the members' log folders.</summary>
    public EstimateLogFolders Folders { get; } = new();

    /// <summary>The pool's report as last built, or null before the first.</summary>
    public string? Json => _json;

    /// <summary>The member of that name, or null.</summary>
    public EstimatesFile? For(string project)
    {
        lock (_gate)
        {
            return _members.GetValueOrDefault(project);
        }
    }

    /// <summary>The members' log folders, each once even when two members share one (EST-27).</summary>
    public IReadOnlyList<string> LogFolders()
    {
        lock (_gate)
        {
            return [.. _members.Values.Select(m => EstimateLogFolders.KeyOf(m.LogDir)).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }

    internal void Add(EstimatesFile file)
    {
        lock (_gate)
        {
            _members[file.Project] = file;
        }
    }

    /// <summary>A member left the pool; the others are rebuilt.</summary>
    public void Remove(string project)
    {
        bool removed;
        lock (_gate)
        {
            removed = _members.Remove(project);
        }

        if (removed)
        {
            Changed();
        }
    }

    /// <summary>EST-29: some member's rows or stays changed. Rebuilds the pool's report and every member's file.</summary>
    public void Changed()
    {
        List<EstimatesFile> members;
        lock (_gate)
        {
            members = [.. _members.Values];
        }

        try
        {
            var now = clock.Now;
            var sources = LogFolders().Select(Folders.Read).ToList();
            var open = members.SelectMany(m => m.Open()).ToList();
            _json = EstimatesReport.ForPool(sources, open, now, scheduler).ToJson();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            log?.Write($"estimates: the pool's report was not built: {e.Message}");
        }

        foreach (var member in members)
        {
            member.Write();
        }
    }

    /// <summary>The pool's report for the members' sources, to a test or the route, built now.</summary>
    internal IReadOnlyList<EstimateSource> Sources() => [.. LogFolders().Select(Folders.Read)];
}

/// <summary>
/// One project's <c>jobs/.logs/estimates.json</c> (EST-28 to EST-31): builds the report from the project's and the pool's
/// cached rows and its review stays, holds the last one in memory for the route, and writes it atomically, through
/// <c>estimates.json.tmp</c> and a move. It writes only when asked: <see cref="Changed"/> after a run finishes or a review
/// stay opens or closes (EST-29), never from a tick.
/// </summary>
public sealed class EstimatesFile
{
    private readonly IClock _clock;
    private readonly string _scheduler;
    private readonly ISchedulerLog? _log;
    private readonly EstimatesPool? _pool;
    private readonly EstimateLogFolders _folders;
    private readonly Func<IReadOnlyList<ReviewStay>> _open;
    private readonly object _gate = new();
    private volatile string? _json;
    private string? _lastError;

    /// <summary>
    /// The report of <paramref name="project"/>, whose accounting and review stays are in <paramref name="logDir"/>, written to
    /// <paramref name="path"/>. In a <paramref name="pool"/> the figures fall back to the pool's (EST-25) and the file joins it;
    /// with none, the project's own figures are its only ones (EST-27). <paramref name="open"/> gives the open review stays.
    /// </summary>
    public EstimatesFile(string project, string logDir, string path, IClock clock, string scheduler, ISchedulerLog? log = null,
        EstimatesPool? pool = null, Func<IReadOnlyList<ReviewStay>>? open = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(logDir);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(clock);
        Project = project;
        LogDir = logDir;
        Path = path;
        _clock = clock;
        _scheduler = scheduler;
        _log = log;
        _pool = pool;
        _folders = pool?.Folders ?? new EstimateLogFolders();
        _open = open ?? (() => []);
        pool?.Add(this);
    }

    /// <summary>The project's name.</summary>
    public string Project { get; }

    /// <summary>The project's log folder: where <c>accounting.csv</c> and <c>review-stays.csv</c> are.</summary>
    public string LogDir { get; }

    /// <summary>Where the report is written (EST-28).</summary>
    public string Path { get; }

    /// <summary>The report as last built, as JSON, or null before the first (EST-31: the route reads this, not the file).</summary>
    public string? Json => _json;

    /// <summary>How many times the file was written.</summary>
    public int Writes { get; private set; }

    /// <summary>The project's open review stays.</summary>
    public IReadOnlyList<ReviewStay> Open() => _open();

    /// <summary>
    /// EST-29: something that changes a figure happened here (a run finished, a review stay opened or closed), or this is the
    /// start-up. A pool rebuilds every member's file, because each may borrow from the pool's rows.
    /// </summary>
    public void Changed()
    {
        if (_pool is not null)
        {
            _pool.Changed();
        }
        else
        {
            Write();
        }
    }

    /// <summary>Builds the report from the cached files, keeps it, and writes the file (EST-29, EST-30). Never throws.</summary>
    public void Write()
    {
        lock (_gate)
        {
            try
            {
                var own = _folders.Read(LogDir);
                var pool = _pool?.Sources();
                var json = EstimatesReport.ForProject(Project, own, pool, _open(), _clock.Now, _scheduler).ToJson();
                _json = json;
                Save(json);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                Told(e);
            }
        }
    }

    // EST-30: the whole old file or the whole new one. A move that fails leaves the temporary file for the next trigger.
    private void Save(string json)
    {
        try
        {
            var folder = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(folder))
            {
                _ = Directory.CreateDirectory(folder);
            }

            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, json, new System.Text.UTF8Encoding(false));
            File.Move(tmp, Path, overwrite: true);
            Writes++;
            _lastError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Told(e);
        }
    }

    // 1118-B: a failure that repeats is told once.
    private void Told(Exception e)
    {
        if (_lastError == e.Message)
        {
            return;
        }

        _lastError = e.Message;
        _log?.Write($"estimates: {Path} was not written: {e.Message}");
    }
}
