// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Contracts;
using Scheduler.Core.Estimates;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git;
using Scheduler.Host.Git.Objects;

namespace Scheduler.Host.State;

/// <summary>
/// The review stays of one project (EST-16 to EST-19): <c>review-stays.csv</c> and <c>review-stays.head</c> in its log
/// folder, and the open stays in memory. <see cref="Advance(string, Func{string, IReadOnlyList{MainCommit}?})"/> is
/// called with the model of main's snapshot: when its commit is the head, nothing is read and nothing runs (EST-17).
/// Otherwise the new commits come from the model, in memory; only a gap the model's log does not reach is walked, in
/// process, else with exactly one <c>git log</c> (EST-18). The first call walks main's first-parent line from its root
/// once (EST-19): it writes the whole file when there is none, and otherwise only rebuilds the open stays. It never fetches.
/// </summary>
public sealed class ReviewStaysFile
{
    /// <summary>EST-16: the file of closed stays.</summary>
    public const string FileName = "review-stays.csv";

    /// <summary>EST-16: the last main commit the file covers.</summary>
    public const string HeadName = "review-stays.head";

    private readonly string _repo;
    private readonly string _jobsDir;
    private readonly GitRunner _git;
    private readonly ISchedulerLog? _log;
    private readonly Func<string?, string, IReadOnlyList<MainCommit>?> _inProcess;
    private readonly object _gate = new();
    private readonly List<ReviewStay> _closed = [];
    private ReviewStays _stays;
    private bool _loaded;
    private string? _lastError;

    /// <summary>
    /// The stays of the repository <paramref name="repo"/>'s jobs folder <paramref name="jobsDir"/>, kept in
    /// <paramref name="logDir"/>. <paramref name="inProcess"/> replaces the in-process walk (tests); it answers the
    /// commits after <c>from</c> (null: from the root) up to <c>to</c>, oldest first, or null for git to answer.
    /// </summary>
    public ReviewStaysFile(string logDir, string repo, string jobsDir, GitRunner git, ISchedulerLog? log = null,
        Func<string?, string, IReadOnlyList<MainCommit>?>? inProcess = null)
    {
        ArgumentNullException.ThrowIfNull(logDir);
        ArgumentNullException.ThrowIfNull(jobsDir);
        Path = System.IO.Path.Combine(logDir, FileName);
        HeadPath = System.IO.Path.Combine(logDir, HeadName);
        _repo = repo;
        _jobsDir = jobsDir.Replace('\\', '/').Trim('/');
        _git = git;
        _log = log;
        _inProcess = inProcess ?? WalkInProcess;
        _stays = new ReviewStays(_jobsDir);
    }

    /// <summary>The file of closed stays.</summary>
    public string Path { get; }

    /// <summary>The head file.</summary>
    public string HeadPath { get; }

    /// <summary>The last main commit read, or null before the first walk.</summary>
    public string? Head { get; private set; }

    /// <summary>How many walks of history this instance made (in process or with git): EST-18's "once per gap".</summary>
    public int Walks { get; private set; }

    /// <summary>The closed stays, oldest first, as the file holds them.</summary>
    public IReadOnlyList<ReviewStay> Closed
    {
        get
        {
            lock (_gate)
            {
                return [.. _closed];
            }
        }
    }

    /// <summary>The open stays, oldest first.</summary>
    public IReadOnlyList<ReviewStay> Open
    {
        get
        {
            lock (_gate)
            {
                return _stays.Open;
            }
        }
    }

    /// <summary>EST-17: advances to the model's snapshot. Returns whether any stay opened or closed.</summary>
    public bool Advance(MainSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Advance(snapshot.Commit, snapshot.Commits);
    }

    /// <summary>
    /// EST-17 to EST-19: advances to <paramref name="commit"/>, the model's snapshot commit, reading its new commits
    /// with <paramref name="commitsSince"/> (the model's <c>Commits(since)</c>). Returns whether any stay opened or closed.
    /// </summary>
    public bool Advance(string commit, Func<string, IReadOnlyList<MainCommit>?> commitsSince)
    {
        var changed = AdvanceCore(commit, commitsSince);
        if (changed)
        {
            Changed?.Invoke();   // EST-29: a stay opened or closed
        }

        return changed;
    }

    /// <summary>EST-29: raised, outside the lock, when an advance opened or closed a stay; never on one that changed nothing.</summary>
    public event Action? Changed;

    private bool AdvanceCore(string commit, Func<string, IReadOnlyList<MainCommit>?> commitsSince)
    {
        ArgumentException.ThrowIfNullOrEmpty(commit);
        ArgumentNullException.ThrowIfNull(commitsSince);

        lock (_gate)
        {
            if (!_loaded)
            {
                return Load(commit);
            }

            if (commit == Head)
            {
                return false;
            }

            var commits = Head is null ? null : commitsSince(Head);
            if (commits is null)
            {
                var walked = Walk(Head, commit);
                if (walked is null)
                {
                    return false;
                }

                if (!walked.Value.FromFound)
                {
                    return Rebuild(commit, walked.Value.Commits, null);
                }

                commits = walked.Value.Commits;
            }

            var openBefore = _stays.Open.Select(s => s.EnteredCommit).ToHashSet(StringComparer.Ordinal);
            var closed = _stays.Apply(commits);
            Append(closed);
            WriteHead(commit);
            return closed.Count > 0 || !openBefore.SetEquals(_stays.Open.Select(s => s.EnteredCommit));
        }
    }

    // EST-16, EST-19: the first call. Reads the file and the head, walks main from its root to `commit` once, and either
    // writes the whole file (none, no head, or a head not on the line) or appends the stays closed after the head.
    private bool Load(string commit)
    {
        _loaded = true;
        var file = ReadFile();
        Head = ReadHead();
        _closed.Clear();
        _closed.AddRange(file ?? []);

        var walked = Walk(null, commit);
        if (walked is null)
        {
            return false;
        }

        var commits = walked.Value.Commits;
        return Rebuild(commit, commits, file is null || Head is null ? null : Head);
    }

    // Replays `commits` (main from its root) into fresh stays. With a head on the line, the file already holds every stay
    // closed up to it, so only the later ones are appended; otherwise the whole file is written again.
    private bool Rebuild(string commit, IReadOnlyList<MainCommit> commits, string? head)
    {
        _stays = new ReviewStays(_jobsDir);
        var at = head is null ? -1 : IndexOf(commits, head);
        if (head is not null && at < 0 && head != commit)
        {
            head = null; // EST-18: a head not on the line is no head
        }

        var all = new List<ReviewStay>();
        var after = new List<ReviewStay>();
        for (var i = 0; i < commits.Count; i++)
        {
            var closed = _stays.Apply(commits[i]);
            all.AddRange(closed);
            if (i > at)
            {
                after.AddRange(closed);
            }
        }

        if (head is null)
        {
            _closed.Clear();
            _closed.AddRange(all);
            WriteAll();
        }
        else
        {
            Append(after);
        }

        WriteHead(commit);
        return true;
    }

    private static int IndexOf(IReadOnlyList<MainCommit> commits, string sha)
    {
        for (var i = 0; i < commits.Count; i++)
        {
            if (commits[i].Sha == sha)
            {
                return i;
            }
        }

        return -1;
    }

    // EST-18: one walk of main's first-parent line after `from` (null: from the root) up to `to`, in process, else one
    // git log. FromFound false: `from` is not on the line, and the commits are the whole line from its root.
    private (IReadOnlyList<MainCommit> Commits, bool FromFound)? Walk(string? from, string to)
    {
        Walks++;
        var inProcess = _inProcess(from, to);
        if (inProcess is not null)
        {
            var found = from is null || inProcess.Count == 0 || inProcess[0].Parent == from;
            return (inProcess, found);
        }

        var fromGit = WalkWithGit(from, to);
        return fromGit is null ? null : (fromGit, true);
    }

    // EST-18's fallback: `git log --first-parent --reverse --format=%H %P %ct --name-status --no-renames <from>..<to> -- <jobs folders>`.
    private List<MainCommit>? WalkWithGit(string? from, string to)
    {
        var range = from is null ? to : from + ".." + to;
        string[] folders = [.. new[] { "review", "done", "queue", "active" }.Select(f => (_jobsDir.Length == 0 ? "" : _jobsDir + "/") + f)];
        var result = _git.Run(_repo, ["-c", "core.quotePath=false", "log", "--first-parent", "--reverse", "--format=%x01%H %P %ct", "--name-status", "--no-renames", range, "--", .. folders]);
        if (!result.Ok)
        {
            LogOnce("review stays: git log failed: " + result.Stderr.Trim());
            return null;
        }

        return ParseLog(result.Stdout);
    }

    /// <summary>Reads the output of EST-18's <c>git log</c> (each commit headed by <c>\x01&lt;sha&gt; &lt;parents&gt; &lt;time&gt;</c>).</summary>
    internal static List<MainCommit> ParseLog(string stdout)
    {
        var commits = new List<MainCommit>();
        foreach (var block in stdout.Split('\x01', StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Replace("\r", "", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var head = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (head.Length < 2 || !long.TryParse(head[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                continue;
            }

            var changes = new List<PathChange>();
            foreach (var line in lines.Skip(1))
            {
                var tab = line.IndexOf('\t', StringComparison.Ordinal);
                if (tab < 1)
                {
                    continue;
                }

                var path = line[(tab + 1)..];
                changes.Add(line[0] switch
                {
                    'A' => new PathChange(path, null, "+"),
                    'D' => new PathChange(path, "-", null),
                    _ => new PathChange(path, "-", "+"),
                });
            }

            commits.Add(new MainCommit(head[0], head.Length > 2 ? head[1] : null, DateTimeOffset.FromUnixTimeSeconds(seconds), "", changes));
        }

        return commits;
    }

    // EST-18 in process: the first-parent line from `to` back to `from` (or the root), each commit's changes under the
    // jobs folder only. Null when git must answer: no reader (MM-2), an unreadable object, more than WalkLimit commits.
    private IReadOnlyList<MainCommit>? WalkInProcess(string? from, string to)
    {
        var objects = InProcessHistory.Open(_repo);
        if (objects is null || !ObjectId.TryParse(to, out var head))
        {
            return null;
        }

        try
        {
            using var s = objects.Begin();
            var line = new List<(ObjectId Id, Commit Commit)>();
            ObjectId? at = head;
            while (at is { } id && id.ToString() != from)
            {
                if (line.Count >= InProcessHistory.WalkLimit)
                {
                    return null;
                }

                var c = s.ReadCommit(id);
                line.Add((id, c));
                at = c.Parents.Count > 0 ? c.Parents[0] : null;
            }

            line.Reverse();
            var commits = new List<MainCommit>(line.Count);
            ObjectId? parentJobs = line.Count > 0 && line[0].Commit.Parents.Count > 0 ? JobsTree(s, s.ReadCommit(line[0].Commit.Parents[0]).Tree) : null;
            foreach (var (id, c) in line)
            {
                var jobs = JobsTree(s, c.Tree);
                var changes = jobs == parentJobs
                    ? []
                    : InProcessHistory.Diff(s, parentJobs, jobs)
                        .Select(n => new PathChange(Prefixed(n.Path), n.Old?.ToString(), n.New?.ToString()))
                        .ToList();
                commits.Add(new MainCommit(id.ToString(), c.Parents.Count > 0 ? c.Parents[0].ToString() : null, c.CommitTime, c.Subject, changes));
                parentJobs = jobs;
            }

            return commits;
        }
        catch (Exception e) when (InProcessHistory.IsReadFailure(e))
        {
            return null;
        }
    }

    private ObjectId? JobsTree(ObjectReadSession s, ObjectId root) => _jobsDir.Length == 0 ? root : s.FindPath(root, _jobsDir)?.Id;

    private string Prefixed(string path) => _jobsDir.Length == 0 ? path : _jobsDir + "/" + path;

    private List<ReviewStay>? ReadFile()
    {
        try
        {
            return File.Exists(Path) ? [.. File.ReadAllLines(Path).Select(ReviewStays.Parse).OfType<ReviewStay>()] : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogOnce("review stays: cannot read " + Path + ": " + e.Message);
            return [];
        }
    }

    private string? ReadHead()
    {
        try
        {
            var text = File.Exists(HeadPath) ? File.ReadAllText(HeadPath).Trim() : "";
            return text.Length > 0 ? text : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogOnce("review stays: cannot read " + HeadPath + ": " + e.Message);
            return null;
        }
    }

    private void Append(IReadOnlyList<ReviewStay> closed)
    {
        if (closed.Count == 0)
        {
            return;
        }

        _closed.AddRange(closed);
        Try(() =>
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var header = File.Exists(Path) && new FileInfo(Path).Length > 0 ? "" : ReviewStays.Header + "\n";
            File.AppendAllText(Path, header + string.Concat(closed.Select(s => ReviewStays.Format(s) + "\n")));
        });
    }

    private void WriteAll() => Try(() =>
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, ReviewStays.Header + "\n" + string.Concat(_closed.Select(s => ReviewStays.Format(s) + "\n")));
        File.Move(tmp, Path, true);
    });

    private void WriteHead(string commit)
    {
        Head = commit;
        Try(() =>
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(HeadPath)!);
            File.WriteAllText(HeadPath, commit + "\n");
        });
    }

    private void Try(Action write)
    {
        try
        {
            write();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogOnce("review stays: cannot write " + Path + ": " + e.Message);
        }
    }

    private void LogOnce(string line)
    {
        if (line != _lastError)
        {
            _lastError = line;
            _log?.Write(line);
        }
    }
}
