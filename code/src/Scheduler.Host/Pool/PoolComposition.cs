using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Scheduler.Core;
using Scheduler.Core.Categories;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Pool;
using Scheduler.Core.Tick;
using Scheduler.Core.WebContract;
using Scheduler.Host.Config;
using Scheduler.Host.Git;
using Scheduler.Host.Live;
using Scheduler.Host.Moves;
using Scheduler.Host.PageActions;
using Scheduler.Host.Processes;
using Scheduler.Host.Program;
using Scheduler.Host.Slots;
using Scheduler.Host.State;
using Scheduler.Host.Tick;
using Scheduler.Web.Server;
using Scheduler.Web.Views;

namespace Scheduler.Host.Pool;

/// <summary>What <c>scheduler-next -Pool [&lt;file&gt;]</c> starts from (MP-C9).</summary>
public sealed class PoolStart
{
    /// <summary>The pool file, already read once (<see cref="PoolConfigFile.Current"/> set).</summary>
    public required PoolConfigFile File { get; init; }

    /// <summary>The command line: <c>-Slots</c>, <c>-NoHealthCheck</c>, <c>-Paused</c>, <c>-DryRun</c> apply to every project, <c>-Port</c> to the pool.</summary>
    public required CommandLineOptions Options { get; init; }

    /// <summary>The clock.</summary>
    public required IClock Clock { get; init; }

    /// <summary>The pool's log (MP-H3): every project's lines come here too, prefixed with its name.</summary>
    public required ISchedulerLog Log { get; init; }

    /// <summary>%LOCALAPPDATA%: the pool's default log folder and each project's (CFG-6).</summary>
    public required string LocalAppData { get; init; }

    /// <summary>Loads a project's scheduler.json from its repository; null = <see cref="ConfigLoader"/>.</summary>
    public Func<string, ConfigLoadResult>? LoadProject { get; init; }

    /// <summary>The project's own scheduler.log (MP-H3); null = a <see cref="FileSchedulerLog"/> in its logDir.</summary>
    public Func<SchedulerConfig, ISchedulerLog>? OwnLog { get; init; }

    /// <summary>The pool's name for its mutex (MP-H1): <c>pool</c>, so <c>GlobalProjectToolkit-pool-scheduler</c>.</summary>
    public string Name { get; init; } = "pool";

    /// <summary>How long to wait for the pool's own mutex (MP-H1).</summary>
    public TimeSpan MutexWait { get; init; } = SingleInstance.DefaultWait;

    /// <summary>How long to wait for a project's mutex; zero leaves a project whose own scheduler runs out at once.</summary>
    public TimeSpan ProjectMutexWait { get; init; } = TimeSpan.Zero;

    /// <summary>1050-B: the default <see cref="StartHold"/>.</summary>
    public static readonly TimeSpan DefaultStartHold = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 1050-B: at a start, how long the projects that have joined wait to tick for the others still joining: a slow
    /// first tick kept the loop from taking a finished join in. A join that runs longer holds up nobody after it.
    /// </summary>
    public TimeSpan StartHold { get; init; } = DefaultStartHold;

    /// <summary>Opens the page.</summary>
    public Action<string> OpenBrowser { get; init; } = _ => { };

    /// <summary><c>-NoBrowser</c>.</summary>
    public bool NoBrowser { get; init; }

    /// <summary>The exe's folder, above which ProjectToolkit is looked for (540-B); null looks nowhere.</summary>
    public string? ExeDir { get; init; }

    /// <summary>The exe's path, for each project's Claude worker settings (WRK-14); null writes none.</summary>
    public string? ExePath { get; init; }

    /// <summary>The tray's shutdown event (670-B); null when none.</summary>
    public string? ShutdownEvent { get; init; }

    /// <summary>The command line as given, for the startup block's options line.</summary>
    public IReadOnlyList<string> Args { get; init; } = [];
}

/// <summary>
/// The pool's composition root (MP-H1 to MP-H8; design section 6): per project the parts one
/// project has always had (<see cref="ProjectParts"/>), shared the clock, worker processes, host
/// load, model-server probe and cache, agents' quota and the loop (MP-H4), and one
/// <see cref="SchedulerRunner"/> over Core's <see cref="PoolTick"/>. The pool's page (MP-P1 to MP-P8) is
/// <see cref="PoolPageData"/> and <see cref="PoolPageActions"/> behind Web's pool routes, one page server on the
/// pool's port.
/// </summary>
public sealed class PoolComposition : IDisposable
{
    /// <summary>MP-H1: how often a project left out because its own scheduler runs is tried again.</summary>
    public static readonly TimeSpan RetryEvery = TimeSpan.FromMinutes(1);

    /// <summary>MP-H1: the note for a project left out.</summary>
    public const string OwnSchedulerRunning = "not in the pool: its own scheduler is running";

    // 1015-B: how long an exit waits for a join still under way.
    private static readonly TimeSpan JoinWaitOnExit = TimeSpan.FromSeconds(30);

    private readonly PoolStart _start;
    private readonly WorkerExitWatcher _exits = new();
    private readonly ServerProbe _probe = new();
    private readonly SharedParts _shared;
    // 1015-B: written on the loop thread only, read by the page's threads too.
    private readonly ConcurrentDictionary<string, Member> _members = new(Text.Comparer);
    private readonly Dictionary<string, (string Why, DateTimeOffset At)> _out = new(Text.Comparer);
    // 1015-B: the projects whose join runs on a task of its own, each harvested by the loop when done.
    private readonly Dictionary<string, Joining> _joining = new(Text.Comparer);
    private readonly Stopwatch _since = Stopwatch.StartNew();
    // 1053-B: wip branches of done cards, swept in the background once every project has joined, then daily.
    private readonly WipSweep _wipSweep;
    // 1078-B: how fast this PC starts processes, on a thread of its own.
    private readonly ProcessStartCheck _startRate;
    private readonly ISchedulerLog _log;
    private volatile string[] _starting = [];
    private volatile PageThemes _themes;
    private readonly HashSet<string> _skipped = new(Text.Comparer);
    private bool _startLogged;
    private bool _holdLogged;
    private readonly EventWaitHandle? _shutdown;
    private readonly RegisteredWaitHandle? _shutdownWake;
    private readonly SingleInstance _poolInstance;
    private readonly EventWaitHandle _poolShutdown;
    private readonly RegisteredWaitHandle _poolShutdownWake;
    private SchedulerRunner? _runner;
    private PoolPageData? _pageData;
    private IPageListener? _page;

    private PoolComposition(PoolStart start)
    {
        _start = start;
        _wipSweep = new WipSweep(start.Clock);
        _log = new LockedLog(start.Log); // 1015-B: projects join on tasks of their own, and log as they go
        var pool = start.File.Current ?? throw new SchedulerStartException($"The pool file {start.File.Path} names no valid project.");

        // 1161-B: the token leak workaround, once at the start (microseconds, in process) and again with each hourly check.
        ForegroundLockFix? foreground = null;
        ITokenCount? tokens = null;
        if (OperatingSystem.IsWindows())
        {
            foreground = new ForegroundLockFix(new Win32ForegroundLockApi(), _log, pool.ForegroundLockFix);
            tokens = new PoolTokenCount();
            foreground.Apply();
        }

        _startRate = new ProcessStartCheck(start.Clock, _log, null, foreground, tokens);

        // 1043-B, 1081-B: the one git, resolved once, before any project's git runner: the pool file's setting, else the
        // first project's scheduler.json (read from its working copy, as no runner exists yet to read it from main).
        GitExecutable.Configure(GitExecutable.Choose(GitExecutable.SettingIn(start.File.Path), start.File.Path,
            pool.Projects.Select(e => (e.Name, ProjectGitSetting(e)))));

        // 1013-B, MP-H1: the pool's own mutex before any project's. Taken in the runner, after the projects had
        // joined, a second pool started in between won the pool while the first held every project, and the
        // first then gave up on the pool mutex: one scheduler left, with no project in it.
        _poolInstance = SingleInstance.TryAcquire(start.Name, start.MutexWait)
            ?? throw new SchedulerStartException($"Another {start.Name} scheduler is still running");

        // 1013-B, 975-B: the pool's shutdown event, named from its pool file as a repository's is from its path,
        // so a tray started while this pool runs attaches to it instead of starting a second one.
        _poolShutdown = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownName(start.File.Path));
        _ = _poolShutdown.Reset();
        _poolShutdownWake = ThreadPool.RegisterWaitForSingleObject(_poolShutdown, (_, _) => _exits.Wake(), null, Timeout.Infinite, executeOnlyOnce: true);
        _shutdown = string.IsNullOrWhiteSpace(start.ShutdownEvent) ? null : new EventWaitHandle(false, EventResetMode.ManualReset, start.ShutdownEvent.Trim());
        var quota = new AgentQuota();
        var servers = new ServerCache(_probe, start.Clock, _log);
        _shared = new SharedParts
        {
            Exits = _exits,
            Probe = _probe,
            Processes = new WorkerProcesses(new JobObjects()),
            Load = new HostLoad(),
            Quota = quota,
            Servers = servers,
            Numbering = CardNumbering.Shared,   // 1111-B: one counter of card numbers for the whole pool, over its models of main
        };
        LogDir = PoolConfigFile.LogDirOf(pool, start.LocalAppData);
        Tick = new PoolTick(start.Clock, _log, quota, servers, FinishedRuns);
        // 1015-B: every project's join starts here on a task of its own; the page is served before any has joined.
        Apply(pool);

        var types = pool.Pool ?? new WorkerPool();
        Config = new SchedulerConfig
        {
            Project = start.Name, // MP-H1: SingleInstance names it Global\ProjectToolkit-pool-scheduler
            Slots = Math.Max(1, types.MaxWorkers),
            PollSeconds = _members.Count == 0 ? 15 : _members.Values.Min(m => m.Parts.Config.PollSeconds),
            DryRun = start.Options.DryRun,
            LogDir = LogDir,
            Port = start.Options.Port != 0 ? start.Options.Port : pool.Port,
            Listen = pool.Listen,
            RemoteControl = pool.RemoteControl,
        };

        var pageData = new PoolPageData(start.Clock, LogDir, () => ProjectsOf(m => (IPageData)m.Parts.Data), PoolPart);
        _pageData = pageData;
        var actions = Actions = new PoolPageActions(new PoolPageActionParts
        {
            Projects = () => ProjectsOf(m => (IPageActions)m.Parts.Actions),
            Wake = () => _exits.Wake(TickWake.Click),
            ClearAgent = quota.Clear,
            WritePool = start.File.WritePool,
            WriteWorkspaces = start.File.WriteWorkspaces,
            Pool = () => Tick.Types,
        });
        // 1015-B: the pool's and the scheduler's themes now; each project's own is added once all have joined.
        _themes = LoadThemes();
        var themes = _themes;
        var feed = new LiveFeed(start.Clock, pageData);
        // 1101-B: the live hub, on a thread of its own with its own sampler (HostLoad keeps state between samples).
        var versionOf = (object?)null;
        var version = "";
        Hub = new LiveHub(
            start.Clock,
            pageData.RunningWorkers,
            new HostLoad(),
            () =>
            {
                var snapshot = pageData.PoolSnapshot();
                if (!ReferenceEquals(snapshot, versionOf))
                {
                    version = PageVersion.Of(snapshot.Pages);
                    versionOf = snapshot;
                }

                return version;
            });
        Hub.Start();

        _runner = new SchedulerRunner(new RunnerParts
        {
            Config = Config,
            Clock = start.Clock,
            Log = _log,
            Version = SchedulerVersion.Text,
            MutexWait = start.MutexWait,
            PollSeconds = () => _members.IsEmpty ? Config.PollSeconds : _members.Values.Min(m => m.Parts.Config.PollSeconds),
            // 1015-B: each project adopts its workers and reads its accounting.csv as it joins (Harvest).
            AdoptWorkers = () => { },
            SeedFromAccounting = () => { },
            StartListener = () => _page = PageListening.Start(
                Config,
                PageServerListener.Factory((bind, port) => new PageServerOptions
                {
                    Port = port,
                    BindAddress = bind,
                    RemoteControl = Config.RemoteControl,
                    Data = pageData,
                    Actions = actions,
                    Renderer = new PageRenderer(themes, maxWorkers: () => Tick.Types.MaxWorkers, agents: Agents),
                    Feed = feed,
                    Pool = new PoolRouting(pageData, actions, new CombinedPageRenderer(() => _themes, pool.Theme, () => _starting)),
                }, Hub.Source),
                _log),
            OpenBrowser = start.OpenBrowser,
            NoBrowser = start.NoBrowser,
            Fast = () => feed.Fast,
            ShutdownRequested = () => _poolShutdown.WaitOne(0) || (_shutdown?.WaitOne(0) ?? false),
            Tick = RunTick,
            Draining = () => _members.Count > 0 && _members.Values.All(m => m.Parts.Tick.State == SchedulerState.Draining),
            WorkersRunning = () => Tick.Projects.Concat(Tick.Leaving).Sum(p => p.Tick.Busy().Count),
            WriteStatusFile = WriteStatus,
            WaitForTick = _exits.WaitForTick,
            Counters = () => (GitRunner.Total, MainModelsRepository.AdvanceMsTotal),   // MM-18
            Wake = () => _exits.Wake(TickWake.Click),
            TakeWake = _exits.TakeWake,
        });

        if (_shutdown is not null)
        {
            _shutdownWake = ThreadPool.RegisterWaitForSingleObject(_shutdown, (_, _) => _exits.Wake(), null, Timeout.Infinite, executeOnlyOnce: true);
        }
    }

    // 1081-B: a project's git.exe setting from the scheduler.json in its working copy; null when it has none.
    private static string? ProjectGitSetting(PoolProjectEntry entry)
    {
        foreach (var dir in new[] { entry.JobsDir, "jobs", "docs/jobs" })
        {
            var file = Path.Combine(entry.Repo, dir.Replace('/', Path.DirectorySeparatorChar), "scheduler.json");
            if (File.Exists(file))
            {
                return GitExecutable.SettingIn(file);
            }
        }

        return null;
    }

    /// <summary>
    /// Card 1012-B: write the <c>startup:</c> block before the first tick: tool, system, pool configuration and options
    /// to the pool's log, and each project's line to its own log (and the pool's, with its name).
    /// </summary>
    public void WriteStartup()
    {
        var pool = _start.File.Current;
        var log = _start.Log;
        var lines = new List<string>();
        lines.AddRange(StartupBlock.Tool(_start.ExePath));
        lines.AddRange(StartupBlock.Machine());
        lines.AddRange(GitExecutable.DescribeLines()); // 1043-B, 1081-B
        if (pool is not null)
        {
            var agents = _members.Values.SelectMany(m => m.Parts.Config.Agents.Values)
                .GroupBy(a => a.Name, Text.Comparer).Select(g => g.First());
            lines.AddRange(StartupBlock.Pool(_start.File.Path, pool, Tick.Types, LogDir, agents));
        }

        foreach (var line in lines)
        {
            log.Write(line);
        }

        foreach (var entry in pool?.Projects ?? [])
        {
            if (_members.TryGetValue(entry.Name, out var m))
            {
                m.Parts.Log.Write(StartupBlock.Project(
                    m.Parts.Config, entry.Weight, m.Parts.Host.Planner is not null, PoolDispatcher.WorkspacesOf(entry.Workspaces, Tick.Types.MaxWorkers)));
            }
        }

        log.Write(StartupBlock.Options(_start.Args));
    }

    /// <summary>The pool's own settings for the runner: project "pool", its total, the smallest poll, its log folder.</summary>
    public SchedulerConfig Config { get; }

    /// <summary>The pool's own log folder (MP-C3, MP-H3).</summary>
    public string LogDir { get; }

    /// <summary>The pool's tick.</summary>
    public PoolTick Tick { get; }

    /// <summary>The pool's page data (MP-P8).</summary>
    public PoolPageData PageData => _pageData!;

    /// <summary>The pool's page actions (MP-P8).</summary>
    public PoolPageActions Actions { get; private set; } = null!;

    /// <summary>The program's loop.</summary>
    public SchedulerRunner Runner => _runner!;

    /// <summary>The projects in the pool and their parts, in the pool file's order.</summary>
    public IReadOnlyList<ProjectParts> Projects => [.. Tick.Projects.Select(p => _members[p.Name].Parts)];

    /// <summary>MP-H1: the projects left out and why, for the page and status.txt.</summary>
    public IReadOnlyDictionary<string, string> LeftOut => _out.ToDictionary(o => o.Key, o => o.Value.Why, Text.Comparer);

    /// <summary>Compose the pool from <paramref name="start"/>.</summary>
    public static PoolComposition Create(PoolStart start) => new(start);

    /// <summary>MP-H3: the pool's status.txt: its projects, state and share, those left out, then each type's workers.</summary>
    public static string StatusText(PoolPageSnapshot pool, IReadOnlyDictionary<string, string> leftOut, DateTimeOffset now, string? processStarts = null)
    {
        var text = new StringBuilder();
        // 1108-B: while the last process-start check was slow, its warning rides on the first line (1078-B).
        var slow = string.IsNullOrWhiteSpace(processStarts) ? "" : $" - {processStarts}";
        text.Append(CultureInfo.InvariantCulture, $"pool at {now:yyyy-MM-dd HH:mm:ss}: {pool.Running} of {pool.MaxWorkers} worker(s) running{slow}\n");
        foreach (var p in pool.Projects)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{p.Name}: {p.State.ToString().ToLowerInvariant()}, weight {Percent(p.WeightShare)}%, used {Percent(p.UsedShare)}%, {p.Running} running, {p.Queued} next up\n");
        }

        foreach (var (name, why) in leftOut)
        {
            text.Append(CultureInfo.InvariantCulture, $"{name}: {why}\n");
        }

        foreach (var t in pool.Types)
        {
            var by = string.Join(", ", t.RunningByProject.Select(r => $"{r.Key} {r.Value}"));
            text.Append(CultureInfo.InvariantCulture, $"type {t.Name}: {t.RunningByProject.Values.Sum()} of {t.Max} running{(by.Length > 0 ? " (" + by + ")" : "")}\n");
        }

        return text.ToString();
    }

    /// <summary>1101-B: the live hub, building frames of what changed at up to 24 a second.</summary>
    public LiveHub Hub { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        // 1110-B: the page server goes before the hub, so no stream is reading the hub when it stops.
        _page?.Stop();
        Hub.Dispose();
        _ = _shutdownWake?.Unregister(null);
        // 1015-B: a join still running is waited for (a project's parts hold a background git writer), then let go.
        foreach (var j in _joining.Values)
        {
            if (j.Task.Wait(JoinWaitOnExit))
            {
                j.Task.Result.Parts?.Dispose();
            }

            j.Instance.Dispose();
        }

        _joining.Clear();
        foreach (var m in _members.Values)
        {
            m.Dispose();
        }

        _members.Clear();
        _ = _poolShutdownWake.Unregister(null);
        _exits.Dispose();
        _probe.Dispose();
        _shutdown?.Dispose();
        _poolShutdown.Dispose();
        _poolInstance.Dispose();
    }

    /// <summary>
    /// 1013-B: the name of the shutdown event of the pool run from <paramref name="poolFile"/>: the repository
    /// event's name (<see cref="RepoShutdown"/>) keyed by the pool file's path. The tray computes the same.
    /// </summary>
    public static string ShutdownName(string poolFile) => RepoShutdown.Name(poolFile);

    private static int Percent(double share) => (int)Math.Round(share * 100, MidpointRounding.AwayFromZero);

    /// <summary>
    /// One tick as the runner runs it, for the wake given (request 011): the pool file re-read (MP-C7), left-out
    /// projects tried again (MP-H1), each project told the wake (a click or a move request skips the fetch, 1002-B),
    /// the pool's tick, a project whose last worker ended after it left let go, each project's page data.
    /// </summary>
    public void RunTick(TickWake wake)
    {
        var now = _start.Clock.Now;
        var changed = _start.File.Read();
        // 1015-B: the projects whose join has finished since the last tick come in (or are left out) first.
        var joined = Harvest();
        // 1013-B: a project left out for its lock is tried at every tick (a mutex test costs nothing), so it
        // joins as soon as the other scheduler lets go; one left out for its config, each minute.
        var retry = _out.Values.Any(o => o.Why == OwnSchedulerRunning || now - o.At >= RetryEvery);
        if ((changed || retry || joined.Count > 0) && _start.File.Current is { } pool)
        {
            Apply(pool, changed);
        }

        Publish(joined);
        _startRate.Tick(); // 1078-B: on a thread of its own; returns at once
        if (_startLogged && _joining.Count == 0)
        {
            _ = _wipSweep.Tick(SweepRepos); // 1053-B: on a task of its own; returns at once
        }

        if (HoldingTicks())
        {
            return;
        }

        foreach (var m in _members.Values)
        {
            m.Parts.Host.Woke(wake); // 1002-B, request 011: the wake decides whether a project's read pays a fetch
        }

        Tick.Run(wake);
        foreach (var gone in _members.Keys.Where(n => !Tick.Projects.Concat(Tick.Leaving).Any(p => Text.Same(p.Name, n))).ToList())
        {
            if (_members.TryRemove(gone, out var member))
            {
                member.Dispose();
            }
        }

        // 1063-B: the page's data is a step of its own, not part of the tick's last (WriteStatus): on 2026-11-21 a
        // click tick's "WriteStatus 8.6-17 s" was this refresh.
        _runner?.StepStarting("PageRefresh");
        foreach (var m in _members.Values)
        {
            m.Parts.Data.Refresh();
        }

        // 1090-B: each project's views, rebuilt only when its model's commit or its running cards moved (VW-22).
        _runner?.StepStarting("ViewsRefresh");
        foreach (var m in _members.Values)
        {
            _ = m.Parts.Views.Refresh();
        }

        _pageData?.Refresh();
    }

    // 1053-B: each member's repository, once (two projects may share one).
    private List<SweepRepo> SweepRepos() =>
    [
        .. _members.Values
            .Select(m => m.Parts)
            .DistinctBy(p => p.Config.Repo, StringComparer.OrdinalIgnoreCase)
            .Select(p => new SweepRepo(p.Config.Project, p.Config.Repo, p.Config.Remote, p.Config.Branch,
                () => p.Main.ListTree(p.Config.JobsDir.TrimEnd('/') + "/done")?
                    .Where(e => e.Path.EndsWith(".md", StringComparison.Ordinal))
                    .Select(e => Path.GetFileNameWithoutExtension(e.Path)).ToList(),
                p.Log)),
    ];

    // 1050-B (MP-H1): at a start, the projects that joined do not tick while others are still joining (up to the
    // StartHold): the loop takes each finished join in only between ticks, and a joined project's first tick (its
    // cards read, thousands on a busy PC) kept finished joins waiting for it - once about 90 s.
    private bool HoldingTicks()
    {
        if (_startLogged || _joining.Count == 0 || _since.Elapsed >= _start.StartHold)
        {
            return false;
        }

        if (!_holdLogged && !_members.IsEmpty)
        {
            _holdLogged = true;
            _log.Write(string.Create(CultureInfo.InvariantCulture,
                $"pool: the projects that joined tick once every project has joined (at most {_start.StartHold.TotalSeconds:0} s from the start)"));
        }

        return true;
    }

    // The projects in the pool (not those only being watched to the end), in the pool file's order.
    // 1090-B: Core's pool part with each project's views data laid on (VW-20), in the pool file's order.
    // 1087-C (WT-P3): and each type's effective model, which needs a scheduler.json Core's pool tick
    // does not hold: the types are global to the pool (MP-C4), so the first project's config and
    // models.json answer for every one, as PoolTick already takes the first project's types (QueueOrder).
    private PoolPageSnapshot PoolPart()
    {
        var part = Tick.Snapshot() with { Views = [.. ProjectsOf(m => m.Parts.Views.Current).Select(p => p.Part)] };
        var members = ProjectsOf(m => m.Parts);
        if (members.Count == 0)
        {
            return part;
        }

        var parts = members[0].Part;
        var models = parts.PageModels();
        var byName = Tick.Types.Types.ToDictionary(t => t.Name, Text.Comparer);
        return part with
        {
            Types =
            [
                .. part.Types.Select(t => byName.TryGetValue(t.Name, out var wt)
                    ? t with { EffectiveModel = WorkerSelection.DisplayedModel(parts.Config, wt, models) }
                    : t),
            ],
        };
    }

    private IReadOnlyList<(string Name, T Part)> ProjectsOf<T>(Func<Member, T> pick) =>
        [.. Tick.Projects.Where(p => _members.ContainsKey(p.Name)).Select(p => (p.Name, pick(_members[p.Name])))];

    // The agents a type can be given in the panel: every project's scheduler.json agents (CFG-10), once each.
    private IReadOnlyList<string> Agents() =>
        [.. _members.Values.SelectMany(m => m.Parts.Config.Agents.Keys).Distinct(Text.Comparer)];

    // MP-C7: take the file's projects and types. A listed project already in keeps its parts; a new one
    // joins (its mutex, its scheduler.json); one no longer listed is left to PoolTick, which watches its
    // workers to the end (MP-H6) - its parts go when it is no longer watched.
    private void Apply(PoolConfig pool, bool all = true)
    {
        var types = pool.Pool ?? Tick.Types;
        var projects = new List<PoolTickProject>();
        foreach (var entry in pool.Projects)
        {
            if (!_members.TryGetValue(entry.Name, out var member))
            {
                if (!_joining.ContainsKey(entry.Name))
                {
                    Join(entry, types, all);
                }

                continue; // 1015-B: it comes in when its join has finished (Harvest)
            }

            projects.Add(new PoolTickProject(entry.Name, entry.Weight, member.Parts.Tick) { Agents = member.Agents, Workspaces = entry.Workspaces });
        }

        foreach (var name in _out.Keys.Where(n => !pool.Projects.Any(p => Text.Same(p.Name, n))).ToList())
        {
            _ = _out.Remove(name);
            _ = _skipped.Remove(name);
        }

        Tick.Apply(types, projects);
        _starting = [.. pool.Projects.Select(p => p.Name).Where(_joining.ContainsKey)];
    }

    // MP-H1, 1015-B: the project's mutex here, on the loop's thread (a mutex is let go by the thread that took it),
    // then the slow part - its scheduler.json from main, its parts (a fetch of main) - on a task of its own.
    private void Join(PoolProjectEntry entry, WorkerPool types, bool all)
    {
        var now = _start.Clock.Now;
        var log = _log;
        if (_out.TryGetValue(entry.Name, out var left) && left.Why != OwnSchedulerRunning && now - left.At < RetryEvery
            && !all)
        {
            return; // its config failed less than a minute ago: tried again at the minute
        }

        if (!all && _skipped.Contains(entry.Name))
        {
            return; // MP-C2, 1015-B: its scheduler.json names another project: tried again when the pool file changes
        }

        // An abandoned mutex (its scheduler died holding it) counts as free (SingleInstance.TryAcquire).
        var instance = SingleInstance.TryAcquire(entry.Name, _start.ProjectMutexWait);
        if (instance is null)
        {
            // 1013-B: every retry's outcome is logged, once a minute while the lock stays held.
            if (!_out.TryGetValue(entry.Name, out var was))
            {
                log.Write($"pool: {entry.Name} is {OwnSchedulerRunning}; tried again at each tick, logged each minute");
                _out[entry.Name] = (OwnSchedulerRunning, now);
            }
            else if (was.Why != OwnSchedulerRunning || now - was.At >= RetryEvery)
            {
                log.Write($"pool: {entry.Name} tried again: still {OwnSchedulerRunning}");
                _out[entry.Name] = (OwnSchedulerRunning, now);
            }

            return;
        }

        var watch = Stopwatch.StartNew();
        var task = Task.Run(() =>
        {
            try
            {
                var result = Build(entry, types);
                result.BuiltAt = watch.Elapsed;
                return result;
            }
#pragma warning disable CA1031 // 1015-B: any failure of one project's join leaves that project out, not the pool
            catch (Exception e)
#pragma warning restore CA1031
            {
                return new JoinResult { Why = $"not in the pool: it could not start ({e.Message})" };
            }
            finally
            {
                _exits.Wake(TickWake.Click); // the loop takes it in at once (a click's tick pays no fetch, 1002-B)
            }
        });
        _joining[entry.Name] = new Joining(entry, instance, task, watch);
    }

    // 1015-B: one project's slow join, on its own task: its scheduler.json, its log, its parts. It touches nothing
    // of the pool's but the shared parts' constructors and the log (locked).
    private JoinResult Build(PoolProjectEntry entry, WorkerPool types)
    {
        var step = Stopwatch.StartNew();
        var loaded = (_start.LoadProject ?? LoadProject)(entry.Repo);
        var configTook = step.Elapsed;
        if (!loaded.Ok || loaded.Config is not { } config)
        {
            return new JoinResult { Why = $"not in the pool: its config could not be read ({loaded.Error})" };
        }

        // MP-C2: the name names the log folder, mutex and themes, so it must be the project's own.
        if (!Text.Same(config.Project, entry.Name))
        {
            return new JoinResult { Skipped = $"pool file: project {entry.Name} is skipped: its scheduler.json names the project {config.Project}" };
        }

        // MP-19, MP-H2: threads per worker and the slot folders come from the pool's total, not the project's slots.
        if (!config.SlotsPinned)
        {
            config = config with { Slots = Math.Max(1, types.MaxWorkers) };
        }

        step.Restart();
        var own = (_start.OwnLog ?? OwnLog)(config);
        var projectLog = new PoolProjectLog(_log, own, entry.Name);
        foreach (var line in loaded.LogLines)
        {
            projectLog.Write(line);
        }

        if (_start.ExePath is { Length: > 0 } exe)
        {
            MessageDelivery.WriteClaudeSettings(config.LogDir, exe, projectLog);
        }

        // MP-C4, WT-C8: a pool's types come from the pool file.
        projectLog.Write("workers.json is not read in a pool: the worker types are the pool file's");
        var logTook = step.Elapsed;
        step.Restart();
        var parts = new ProjectParts(config, loaded.ConfigText, _start.Clock, projectLog, _shared, () => _runner?.RequestTick(),
            exeDir: _start.ExeDir, poolProject: entry.Name);
        var result = new JoinResult { Parts = parts, Agents = config.Agents.Count > 0 ? new HashSet<string>(config.Agents.Keys, Text.Comparer) : null };
        result.Steps.Add(("config", configTook));
        result.Steps.Add(("log", logTook));
        result.Steps.Add(("parts", step.Elapsed));
        return result;
    }

    /// <summary>
    /// 1015-B: wait up to <paramref name="timeout"/> for the joins under way, then take them in as a tick would.
    /// True when none is left running. On the loop's thread (or, before the loop runs, the one that composed the pool).
    /// </summary>
    public bool WaitForJoins(TimeSpan timeout)
    {
        var done = Task.WaitAll([.. _joining.Values.Select(j => (Task)j.Task)], timeout);
        var joined = Harvest();
        if (joined.Count > 0 && _start.File.Current is { } pool)
        {
            Apply(pool, all: false);
        }

        Publish(joined);
        _pageData?.Refresh();
        return done && _joining.Count == 0;
    }

    /// <summary>1015-B: the projects still joining, in the pool file's order (the page shows them as starting).</summary>
    public IReadOnlyList<string> Starting => _starting;

    // 1015-B: take in each finished join: a project that joined adopts its workers (MP-H6) and reads its
    // accounting.csv (ACCT-6) at once; one that could not is left out and tried again as before (1013-B).
    private List<Member> Harvest()
    {
        var joined = new List<Member>();
        var now = _start.Clock.Now;
        foreach (var (name, j) in _joining.Where(j => j.Value.Task.IsCompleted).ToList())
        {
            _ = _joining.Remove(name);
            var result = j.Task.Result;
            var took = j.Watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            if (result.Parts is not { } parts)
            {
                j.Instance.Dispose();
                if (result.Why is { } why)
                {
                    _log.Write(_out.ContainsKey(name)
                        ? $"pool: {name} tried again: still {why} ({took}s)"
                        : $"pool: {name} is {why}; tried again each minute ({took}s)");
                    _out[name] = (why, now);
                }
                else if (result.Skipped is { } skipped)
                {
                    _log.Write(skipped);
                    _ = _skipped.Add(name);
                }

                continue;
            }

            var member = new Member(parts, j.Instance, result.Agents);
            member.JoinSteps.AddRange(result.Steps);
            member.JoinSteps.Add(("waited for the loop", j.Watch.Elapsed - result.BuiltAt));
            _members[name] = member;
            parts.Tick.StepStarting += step => StepStarting(member, step);
            var adopt = Stopwatch.StartNew();
            _ = parts.AdoptWorkers(Tick.Types);
            member.JoinSteps.Add(("workers adopted", adopt.Elapsed));
            adopt.Restart();
            parts.SeedFromAccounting();
            member.JoinSteps.Add(("accounting", adopt.Elapsed));
            _ = _out.Remove(name);
            _ = _skipped.Remove(name);
            _log.Write($"pool: {name} joined ({j.Entry.Repo}, weight {j.Entry.Weight}) in {took}s");
            joined.Add(member);
        }

        if (joined.Count > 0 && _joining.Count == 0 && !_startLogged)
        {
            // 1015-B: once every project has joined, their own themes are offered too (staged as at a start).
            _startLogged = true;
            _themes = LoadThemes();
            _log.Write(string.Create(CultureInfo.InvariantCulture,
                $"pool: start took {_since.Elapsed.TotalSeconds:0.0}s: {_members.Count} project(s) joined, {_out.Count} left out"));
        }

        _starting = [.. _starting.Where(_joining.ContainsKey)];
        return joined;
    }

    // 1015-B: the page shows a project as soon as it has joined: its own snapshot, then the pool's.
    private void Publish(List<Member> joined)
    {
        if (joined.Count == 0)
        {
            return;
        }

        foreach (var m in joined)
        {
            var snap = Stopwatch.StartNew();
            m.Parts.Data.Refresh();
            m.JoinSteps.Add(("snapshot", snap.Elapsed));
            // 1050-B: where a join's time went, so a slow start shows which step (or the wait for the loop) took it.
            _log.Write($"pool: {m.Parts.Config.Project} join steps: "
                + string.Join(", ", m.JoinSteps.Select(s => s.Step + " " + s.Took.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "s")));
            m.JoinSteps.Clear();
        }

        _pageData?.Refresh();
    }

    // 966-B: each step is timed and is the tick's safe point. 1015-B: a project's first snapshot is published as soon
    // as its first tick has read the cards, not after the whole pool tick (the 00:09 start's first tick took 57 s,
    // its slowest step WriteStatus 26.5 s, and the page showed no data until it ended).
    private void StepStarting(Member member, TickStep step)
    {
        _runner?.StepStarting(step.ToString());
        if (!member.Published && step > TickStep.ReadCards)
        {
            member.Published = true;
            member.Parts.Data.Refresh();
            _pageData?.Refresh();
        }
    }

    private PageThemes LoadThemes()
    {
        var pool = _start.File.Current;
        return PoolThemeLoader.Load(pool?.Theme ?? "", Path.GetDirectoryName(_start.File.Path) ?? "", _members.Values.Select(m => m.Parts.Config), LogDir, _log);
    }

    private ConfigLoadResult LoadProject(string repo) =>
        new ConfigLoader(localAppData: _start.LocalAppData).Load(_start.Options with { Repo = repo });

    private static ISchedulerLog OwnLog(SchedulerConfig config) =>
        new FileSchedulerLog(config.LogDir, FileSchedulerLog.RollingFolder(config), new SystemClock());

    // MP-15: the window's finished runs of every project's accounting.csv.
    private IReadOnlyList<PoolRun> FinishedRuns()
    {
        var from = _start.Clock.Now - PoolShares.Window;
        return [.. _members.SelectMany(m => m.Value.Parts.PoolRuns(m.Key, from))];
    }

    // MP-H3: each project's status.txt in its own logDir, and the pool's in the pool's.
    private void WriteStatus()
    {
        foreach (var m in _members.Values)
        {
            m.Parts.WriteStatus();
        }

        try
        {
            Directory.CreateDirectory(LogDir);
            // 1015-B: a project still joining is listed as starting.
            var outs = LeftOut.Where(o => !_starting.Contains(o.Key, Text.Comparer)).Concat(_starting.Select(s => KeyValuePair.Create(s, "starting: joining the pool"))).ToDictionary(Text.Comparer);
            File.WriteAllText(Path.Combine(LogDir, "status.txt"), StatusText(Tick.Snapshot(), outs, _start.Clock.Now, _startRate.Warning));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Write($"pool: status.txt could not be written ({e.Message})");
        }
    }

    private sealed class Member(ProjectParts parts, SingleInstance instance, IReadOnlySet<string>? agents) : IDisposable
    {
        public ProjectParts Parts { get; } = parts;

        public IReadOnlySet<string>? Agents { get; } = agents;

        // 1015-B: whether its first snapshot after reading the cards has been published.
        public bool Published { get; set; }

        // 1050-B: its join's steps and their times, logged once its first snapshot is published.
        public List<(string Step, TimeSpan Took)> JoinSteps { get; } = [];

        public void Dispose()
        {
            Parts.Dispose();
            instance.Dispose();
        }
    }

    // 1015-B: a join under way: the project's mutex (taken on the loop's thread) and the task building its parts.
    private sealed record Joining(PoolProjectEntry Entry, SingleInstance Instance, Task<JoinResult> Task, Stopwatch Watch);

    // 1015-B: what a join came to: the parts, or why the project is left out (tried again), or skipped (MP-C2).
    private sealed class JoinResult
    {
        public ProjectParts? Parts { get; init; }

        public IReadOnlySet<string>? Agents { get; init; }

        public string? Why { get; init; }

        public string? Skipped { get; init; }

        // 1050-B: the steps of the join's own task and their times; and when, from the join's start, it finished.
        public List<(string Step, TimeSpan Took)> Steps { get; } = [];

        public TimeSpan BuiltAt { get; set; }
    }

    // 1015-B: the pool's log, written from the joins' tasks and the loop at once.
    private sealed class LockedLog(ISchedulerLog inner) : ISchedulerLog
    {
        private readonly Lock _lock = new();

        public void Write(string line)
        {
            lock (_lock)
            {
                inner.Write(line);
            }
        }
    }
}

/// <summary>
/// A pooled project's log (MP-H3): its own scheduler.log in its logDir, as one project has it, and
/// the pool's log with the project's name in front.
/// </summary>
public sealed class PoolProjectLog(ISchedulerLog pool, ISchedulerLog own, string project) : ISchedulerLog
{
    /// <inheritdoc />
    public void Write(string line)
    {
        own.Write(line);
        pool.Write($"{project}: {line}");
    }
}
