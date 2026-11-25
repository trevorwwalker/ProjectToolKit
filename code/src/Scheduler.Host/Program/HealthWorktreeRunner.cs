// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Health;
using Scheduler.Core.Categories;
using Scheduler.Host.Git;
using Scheduler.Host.Slots;

namespace Scheduler.Host.Program;

/// <summary>
/// Runs the health check (HLTH-3, L1695-1719): in the worktree <c>&lt;workspaceRoot&gt;\health</c> reset to
/// main, through <c>health.cmd</c> (title, cd, set jobsEnvVar, no build-node reuse, the command),
/// output to <c>health-&lt;sha&gt;-&lt;ts&gt;.log</c>, in a job object.
/// <para>
/// One check at a time in that worktree (920-B): before a check starts, the previous check's whole
/// tree is ended and waited for, including a tree left behind by the scheduler that ran before this
/// one (a tray Restart ends only the scheduler, so a running check outlives it). The pid and job of
/// the running check are recorded in <c>health-running.json</c> the way <c>running.json</c> does for
/// workers; a leftover check is ended, never adopted. A check that fails only with MSB3026/MSB3027
/// file-lock errors gets one retry after the locker's tree is ended, and is then reported as "could
/// not run" rather than red, so it does not pause dispatching.
/// </para>
/// </summary>
public sealed partial class HealthWorktreeRunner(
    SchedulerConfig config,
    GitRunner git,
    IWorkerProcesses processes,
    IClock clock,
    ISchedulerLog log,
    int cores,
    Func<SchedulerState>? stateOf = null,
    Action<int>? startedPid = null) : IHealthRunner
{
    /// <summary>How long to wait for an ended check's tree to go, in 50 ms polls.</summary>
    private const int ExitPolls = 40;

    private static readonly System.Text.RegularExpressions.Regex[] OtherErrors =
    [
        new(@"error (?!MSB3026\b|MSB3027\b)[A-Za-z]+[0-9]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        new(@"Test Run Failed", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        new(@"\bFailed!", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        new(@"^\s*error\s*:", System.Text.RegularExpressions.RegexOptions.Multiline),
    ];

    private int _pid;
    private DateTimeOffset _start;
    private string _job = "";
    private string _sha = "";
    private string _logPath = "";
    private bool _lockRetried;
    private bool _lockReported;
    private string _locker = "";

    private string RefMain => $"{config.Remote}/{config.Branch}";

    private string Dir => Path.Combine(config.WorkspaceRoot, "health");

    private bool _notWorktreeLogged;

    private string CmdFile => Path.Combine(config.LogDir, "health.cmd");

    private string RecordPath => Path.Combine(config.LogDir, "health-running.json");

    /// <summary>"" unless the last check ended as "could not run", in which case the locker's name.</summary>
    public string CouldNotRun => _couldNotRun ? _locker : "";

    private bool _couldNotRun;

    private Lazy<Git.Objects.MainObjects?>? _objects;
    private bool? _shortAgrees;

    /// <inheritdoc />
    /// <remarks>
    /// 1065-B (MM-14): main's short sha is made in process (<see cref="ShortInProcess"/>). The first one of this run
    /// is checked against <c>git rev-parse --short</c>; only when they agree is git not asked again (a
    /// <c>core.abbrev</c> setting, for one, makes them differ, and then git is always asked).
    /// </remarks>
    public string? MainShortSha()
    {
        var mine = ShortInProcess();
        if (mine is not null && _shortAgrees == true)
        {
            return mine;
        }

        var r = git.Run(config.Repo, "rev-parse", "--short", RefMain);
        var line = r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        var theirs = r.ExitCode == 0 && !string.IsNullOrEmpty(line) ? line : null;
        if (mine is not null && theirs is not null)
        {
            _shortAgrees ??= mine == theirs;
        }

        return theirs;
    }

    /// <summary>
    /// What <c>git rev-parse --short</c> gives for main with git's default length: as many hex digits as git's
    /// rule for the packed object count asks (half the bits of the count, rounded up, at least 7; loose objects
    /// are not counted, as in git), more while another object starts the same. Null when git must be asked: no
    /// reader (MM-2), a multi-pack index (whose count this does not read), or a ref or pack it cannot read.
    /// </summary>
    public string? ShortInProcess()
    {
        _objects ??= new Lazy<Git.Objects.MainObjects?>(() => InProcessHistory.Open(config.Repo));
        if (_objects.Value is not { } objects)
        {
            return null;
        }

        try
        {
            var pack = Path.Combine(objects.Folder.ObjectsDir, "pack");
            if (File.Exists(Path.Combine(pack, "multi-pack-index")) || Directory.Exists(Path.Combine(pack, "multi-pack-index.d"))
                || objects.Resolve(RefMain) is not { } id)
            {
                return null;
            }

            var packs = objects.Packs(refresh: true);
            long count = packs.Sum(p => (long)p.Index.Count);
            var bits = 0;
            for (var x = count; (x >>= 1) > 0;)
            {
                bits++;
            }

            var len = Math.Max(7, (bits + 2) / 2);
            var full = id.ToString();
            var others = packs.SelectMany(p => p.Index.Ids()).Where(o => o.FirstByte == id.FirstByte && o != id)
                .Select(o => o.ToString())
                .Concat(Loose(objects.Folder.ObjectsDir, full))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            while (len < full.Length && others.Any(o => o.StartsWith(full[..len], StringComparison.Ordinal)))
            {
                len++;
            }

            return full[..len];
        }
        catch (Exception e) when (InProcessHistory.IsReadFailure(e))
        {
            return null;
        }
    }

    // The other loose objects whose id starts with the same two digits as `full`.
    private static IEnumerable<string> Loose(string objectsDir, string full)
    {
        var dir = Path.Combine(objectsDir, full[..2]);
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).Select(f => full[..2] + Path.GetFileName(f))
                .Where(o => o.Length == full.Length && o != full)
            : [];
    }

    /// <inheritdoc />
    public string? Start(string sha, string command)
    {
        // 920-B: never two checks in the health workspace; a leftover tree is ended first, not adopted.
        EndRecorded();

        var root = config.WorkspaceRoot;
        var dir = Dir;
        // 1119-B: git in the health worktree never walks up to an outer repository, and a folder there that is not
        // a worktree of the repository is set aside and the worktree added again; when that fails, no git runs there.
        GitRunner.Confine(dir);
        if (!GitRunner.IsWorktreeOf(dir, config.Repo))
        {
            Directory.CreateDirectory(root);
            var aside = GitRunner.SetAside(dir);
            git.Run(config.Repo, "worktree", "prune");
            var add = git.Run(config.Repo, "worktree", "add", "-f", "-B", "health", dir, RefMain);
            if (!GitRunner.IsWorktreeOf(dir, config.Repo))
            {
                if (!_notWorktreeLogged)
                {
                    _notWorktreeLogged = true;
                    log.Write($"health: {dir} is not a worktree of {config.Repo} and worktree add failed ({add.Stderr.Trim()}); the check does not run");
                }

                return null;
            }

            _notWorktreeLogged = false;
            if (aside is not null)
            {
                log.Write($"health: {dir} was not a worktree; it was moved to {aside} and the worktree added again");
            }
        }

        git.Run(dir, "fetch", "-q", config.Remote);
        // 1067-B (MM-17): one forced checkout moves the branch, the index and the files (was checkout, then reset --hard).
        git.Run(dir, "checkout", "-q", "-f", "-B", "health", RefMain);
        git.Run(dir, "clean", "-q", "-fd");

        var jobs = WorkerSelection.JobsPerWorker(cores, config.Slots);
        var stamp = clock.Now.ToLocalTime().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var logPath = Path.Combine(config.LogDir, $"health-{sha}-{stamp}.log");
        Directory.CreateDirectory(config.LogDir);
        File.WriteAllLines(CmdFile,
        [
            "@echo off",
            $"title {config.Project} health check of {sha}",
            $"cd /d \"{dir}\"",
            $"set {config.JobsEnvVar}={jobs}",
            // 920-B: no MSBuild node or compiler server may outlive the check and hold the build DLLs
            "set MSBUILDDISABLENODEREUSE=1",
            "set DOTNET_CLI_DISABLE_BUILD_SERVERS=1",
            Agents.Expand(config, command, jobs),
        ]);

        // call: a /c line that starts with a quote has its quotes stripped by cmd
        var started = StartProcess($"""call "{CmdFile}" > "{logPath}" 2>&1""", dir);
        if (started is null)
        {
            return null;
        }

        Track(started);
        _sha = sha;
        _logPath = logPath;
        _lockRetried = false;
        _lockReported = false;
        _couldNotRun = false;
        _locker = "";
        Save();
        return logPath;
    }

    /// <inheritdoc />
    public bool IsRunning()
    {
        if (_pid == 0)
        {
            return false;
        }

        if (processes.IsAlive(_pid, _start))
        {
            return true;
        }

        return RetryOnLock();
    }

    /// <inheritdoc />
    public int? ExitCode()
    {
        if (_pid == 0)
        {
            return null;
        }

        var code = processes.ExitCode(_pid);
        if (code is not 0 && _lockRetried && LockOnly(_logPath) is { } locker)
        {
            // A gate that fails only because the files were locked is not a red main (920-B): it is
            // reported as "could not run", which leaves dispatching alone. A scheduler already
            // paused-red keeps its verdict rather than being un-paused by an sha that was not checked.
            _couldNotRun = true;
            _locker = locker;
            if (!_lockReported)
            {
                _lockReported = true;
                log.Write($"health check of main {_sha} could not run (files locked by {locker}); not reported as red");
            }

            var state = stateOf is null ? SchedulerState.Running : stateOf();
            return state == SchedulerState.PausedRed ? code : 0;
        }

        return code;
    }

    /// <inheritdoc />
    public void Kill()
    {
        if (_pid != 0)
        {
            processes.EndJob(_job, _pid);
        }

        Delete();
    }

    private StartedProcess? StartProcess(string commandLine, string dir) =>
        processes.Start(commandLine, JobObjects.Template(config.Project, "health", 0), dir, new Dictionary<string, string?>());

    private void Track(StartedProcess started)
    {
        _pid = started.Pid;
        startedPid?.Invoke(started.Pid); // 966-B: its end wakes the loop
        _start = started.Start;
        _job = JobObjects.Name(config.Project, "health", 0, started.Pid);
    }

    /// <summary>
    /// The check ended, its log shows only file-lock errors, and it has not been retried yet: end the
    /// locker's tree and run the same check again into the same log (920-B). True when it runs again.
    /// </summary>
    private bool RetryOnLock()
    {
        if (_lockRetried || _sha.Length == 0 || LockOnly(_logPath) is null)
        {
            return false;
        }

        EndRecorded();
        if (!Path.Exists(CmdFile))
        {
            return false;
        }

        var again = StartProcess($"""call "{CmdFile}" >> "{_logPath}" 2>&1""", Dir);
        if (again is null)
        {
            return false;
        }

        Track(again);
        _lockRetried = true;
        Save();
        log.Write($"health check of main {_sha} hit locked files; ended the locker's tree and started it again");
        return true;
    }

    /// <summary>
    /// End the check this scheduler started and any check recorded by the one before it, waiting for
    /// each tree to go, then drop the record (920-B).
    /// </summary>
    private void EndRecorded()
    {
        var rec = Load();
        var ended = new HashSet<int>();
        if (_pid != 0 && ended.Add(_pid))
        {
            processes.EndJob(_job, _pid);
            WaitForExit(_pid, _start);
        }

        if (rec is not null && rec.Pid != 0 && ended.Add(rec.Pid))
        {
            processes.EndJob(rec.Job, rec.Pid);
            WaitForExit(rec.Pid, rec.Start);
            log.Write($"ended the health check tree left in the health worktree (pid {rec.Pid}, {rec.Sha})");
        }

        _pid = 0;
        _start = default;
        _job = "";
        Delete();
    }

    private void WaitForExit(int pid, DateTimeOffset start)
    {
        for (var polls = 0; polls < ExitPolls && processes.IsAlive(pid, start); polls++)
        {
            Thread.Sleep(50);
        }
    }

    /// <summary>
    /// The name of the process holding the files when the log's only errors are the MSBuild copy
    /// retries MSB3026 and MSB3027, else null (920-B).
    /// </summary>
    private static string? LockOnly(string path)
    {
        if (path.Length == 0 || !File.Exists(path))
        {
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;   // still held open: nothing to read, so it is not known to be lock-only
        }

        if (text.IndexOf("MSB3026", StringComparison.Ordinal) < 0 &&
            text.IndexOf("MSB3027", StringComparison.Ordinal) < 0)
        {
            return null;
        }

        foreach (var re in OtherErrors)
        {
            if (re.IsMatch(text))
            {
                return null;   // something else failed too: the check has a real verdict
            }
        }

        var m = LockedBy().Match(text);
        return m.Success ? m.Groups[1].Value.Trim() : "another process";
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(config.LogDir);
            File.WriteAllText(RecordPath, JsonSerializer.Serialize(new HealthRecord(_pid, _job, _start, _sha, _logPath)));
        }
        catch (IOException)
        {
            // the record only helps clear a leftover tree; losing it is not worth failing the check
        }
    }

    private HealthRecord? Load()
    {
        try
        {
            return File.Exists(RecordPath) ? JsonSerializer.Deserialize<HealthRecord>(File.ReadAllText(RecordPath)) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private void Delete()
    {
        try
        {
            if (File.Exists(RecordPath))
            {
                File.Delete(RecordPath);
            }
        }
        catch (IOException)
        {
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("locked by\\s*:?\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex LockedBy();

    /// <summary>health-running.json (920-B): the check the last scheduler started, so the next can end it.</summary>
    private sealed record HealthRecord(int Pid, string Job, DateTimeOffset Start, string Sha, string Log);
}
