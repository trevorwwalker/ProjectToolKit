using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Observe;
using Scheduler.Host.PageActions;
using Scheduler.Host.Processes;
using Scheduler.Host.Slots;
using Scheduler.Host.State;
using Scheduler.Host.Tick;
using Scheduler.Web.Server;
using Scheduler.Web.Views;

namespace Scheduler.Host.Program;

/// <summary>
/// The real ports put together (285-B): the tick over <see cref="TickHost"/> (or the dry-run host),
/// the page's data and actions, the health runner, the single instance and the listener, as
/// <see cref="RunnerParts"/> for <see cref="SchedulerRunner"/> (CFG-15's order is the runner's).
/// The project's own parts are <see cref="ProjectParts"/>, which a pool builds once per project (MP-H2).
/// </summary>
public sealed class HostComposition : IDisposable
{
    private readonly WorkerExitWatcher _exits = new();
    private readonly ServerProbe _probe = new();
    private readonly ProjectParts _parts;
    private readonly EventWaitHandle? _shutdown;
    private readonly RegisteredWaitHandle? _shutdownWake;
    private readonly EventWaitHandle? _repoShutdown;
    private readonly RegisteredWaitHandle? _repoShutdownWake;

    private HostComposition(SchedulerConfig config, string configText, IClock clock, ISchedulerLog log, Action<string> openBrowser, bool noBrowser, TimeSpan? mutexWait, ObserveSettings? observe, string? exeDir, string? shutdownEvent)
    {
        Config = config;
        // 670-B: the tray's way to ask this scheduler to end by itself, which - unlike a console Ctrl+C -
        // reaches no other process, so the workers carry on and the next scheduler adopts them.
        _shutdown = string.IsNullOrWhiteSpace(shutdownEvent) ? null : new EventWaitHandle(false, EventResetMode.ManualReset, shutdownEvent.Trim());
        // 975-B: and the repository's own, which a tray that did not start this scheduler finds and sets
        // (it attaches instead of starting a second). An observing run is not the repository's scheduler.
        // It starts clear: a request left set was for the scheduler before this one.
        if (observe is null)
        {
            _repoShutdown = new EventWaitHandle(false, EventResetMode.ManualReset, RepoShutdown.Name(config.Repo));
            _repoShutdown.Reset();
        }

        var shared = new SharedParts
        {
            Exits = _exits,
            Probe = _probe,
            Processes = new WorkerProcesses(new JobObjects()),
            Load = new HostLoad(),
        };
        _parts = new ProjectParts(config, configText, clock, log, shared, () => Runner?.RequestTick(), observe, exeDir);
        Host = _parts.Host;
        Tick = _parts.Tick;
        Data = _parts.Data;
        PageLauncher = _parts.PageLauncher;
        Observer = _parts.Observer;
        var actions = _parts.Actions;

        // PAGE-11 (420-B): the themes come from the files beside the exe, not the built-in ones.
        var renderer = CreateRenderer(config, Tick, _parts.Notice, log);
        var feed = new LiveFeed(clock, Data);

        Runner = new SchedulerRunner(new RunnerParts
        {
            Config = config,
            Clock = clock,
            Log = log,
            Version = SchedulerVersion.Text,
            MutexWait = mutexWait ?? SingleInstance.DefaultWait,
            AdoptWorkers = () => _parts.AdoptWorkers(),
            SeedFromAccounting = _parts.SeedFromAccounting,
            StartListener = () => PageListening.Start(
                config,
                PageServerListener.Factory((bind, port) => new PageServerOptions
                {
                    Port = port,
                    BindAddress = bind,
                    RemoteControl = config.RemoteControl,
                    Data = Data,
                    Actions = observe is null ? actions : new ObservePageActions(actions),
                    Renderer = renderer,
                    Feed = feed,
                }),
                log),
            OpenBrowser = openBrowser,
            NoBrowser = noBrowser,
            ShutdownRequested = _shutdown is null && _repoShutdown is null
                ? null
                : () => (_shutdown?.WaitOne(0) ?? false) || (_repoShutdown?.WaitOne(0) ?? false),
            Tick = wake =>
            {
                if (Observer is { } observer)
                {
                    observer.Run();
                }
                else
                {
                    Host.Woke(wake); // 1002-B, request 011: the wake decides whether this read pays a fetch
                    Tick.Run(wake); // 982-B: a light wake skips the card read when main has not moved
                }

                // 1063-B: the page's data is a step of its own, not part of the tick's last (WriteStatus).
                Runner?.StepStarting("PageRefresh");
                Data.Refresh();
            },
            Draining = () => Tick.State == SchedulerState.Draining,
            WorkersRunning = () => Tick.Running.Count,
            WriteStatusFile = _parts.WriteStatus,
            WaitForTick = _exits.WaitForTick,
            Counters = () => (GitRunner.Total, MainModelsRepository.AdvanceMsTotal),   // MM-18
            Wake = () => _exits.Wake(TickWake.Click),
            TakeWake = _exits.TakeWake,
            Fast = () => feed.Fast,
        });

        // 966-B: the tick's steps are timed, and the gap before each is where an exit ends a tick.
        var runner = Runner;
        Tick.StepStarting += step => runner.StepStarting(step.ToString());
        // 966-B: the tray's Exit and Restart wake the loop at once instead of at its next look.
        if (_shutdown is not null)
        {
            _shutdownWake = ThreadPool.RegisterWaitForSingleObject(_shutdown, (_, _) => _exits.Wake(), null, Timeout.Infinite, executeOnlyOnce: true);
        }

        if (_repoShutdown is not null)
        {
            _repoShutdownWake = ThreadPool.RegisterWaitForSingleObject(_repoShutdown, (_, _) => _exits.Wake(), null, Timeout.Infinite, executeOnlyOnce: true);
        }
    }

    /// <summary>
    /// The page's renderer as this host builds it (938-B). The Worker types panel's total is the
    /// pool's, read on every render (WT-C1): the count moves when workers.json is saved or a worker
    /// is adopted above it (WT-W5), and the panel writes back what it shows. Its agent choice is
    /// scheduler.json's agents in config order (CFG-10, WT-P3), so a type can be given an agent no
    /// saved type uses.
    /// </summary>
    public static PageRenderer CreateRenderer(SchedulerConfig config, SchedulerTick tick, PageNotice? notice, ISchedulerLog log)
    {
        var agentNames = (IReadOnlyList<string>)[.. config.Agents.Keys];

        return new PageRenderer(PageThemeLoader.Load(config, log), notice, maxWorkers: () => tick.Workers.Slots, agents: () => agentNames);
    }

    /// <summary>The configuration.</summary>
    public SchedulerConfig Config { get; }

    /// <summary>The tick host.</summary>
    public TickHost Host { get; }

    /// <summary>The tick.</summary>
    public SchedulerTick Tick { get; }

    /// <summary>The page's data.</summary>
    public HostPageData Data { get; }

    /// <summary>
    /// The launcher the page's Open buttons use (MOVE-9); null when this composition opens no file,
    /// which is the observe run, whose page refuses Open (PAGE-7 keeps opening local).
    /// </summary>
    public IFileLauncher? PageLauncher { get; }

    /// <summary>The program's loop.</summary>
    public SchedulerRunner Runner { get; }

    /// <summary>The observe run's tick (roadmap item 1, card 390-B); null when this is not an observe run.</summary>
    public ObserveTicker? Observer { get; }

    /// <summary>
    /// Compose the program for <paramref name="config"/>; <paramref name="configText"/> is its JSON,
    /// which holds the planner's settings and the <c>toolkit</c> setting. <paramref name="exeDir"/> is
    /// the exe's folder, above which ProjectToolkit is looked for when <c>toolkit</c> is not set
    /// (card 540-B); null (tests) looks nowhere. <paramref name="noBrowser"/> is <c>-NoBrowser</c>
    /// (670-B): the page is not opened at start.
    /// </summary>
    public static HostComposition Create(
        SchedulerConfig config,
        string configText,
        IClock clock,
        ISchedulerLog log,
        Action<string> openBrowser,
        TimeSpan? mutexWait = null,
        ObserveSettings? observe = null,
        string? exeDir = null,
        bool noBrowser = false,
        string? shutdownEvent = null) => new(config, configText, clock, log, openBrowser, noBrowser, mutexWait, observe, exeDir, shutdownEvent);

    /// <inheritdoc />
    public void Dispose()
    {
        _ = _shutdownWake?.Unregister(null);
        _ = _repoShutdownWake?.Unregister(null);
        _parts.Dispose();
        _exits.Dispose();
        _probe.Dispose();
        _shutdown?.Dispose();
        _repoShutdown?.Dispose();
    }
}
