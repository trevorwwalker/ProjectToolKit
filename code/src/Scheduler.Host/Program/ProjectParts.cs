using System.Text.Json;
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Health;
using Scheduler.Core.Planner;
using Scheduler.Core.Pool;
using Scheduler.Core.Tick;
using Scheduler.Core.WebContract;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.Observe;
using Scheduler.Host.PageActions;
using Scheduler.Host.Planner;
using Scheduler.Host.Processes;
using Scheduler.Host.Slots;
using Scheduler.Host.State;
using Scheduler.Host.Templates;
using Scheduler.Host.Tick;
using Scheduler.Host.Views;
using Scheduler.Web.Views;

namespace Scheduler.Host.Program;

/// <summary>
/// What one project's composition shares with the others in a pool (MP-H4), or keeps to itself as
/// one project: the exit watcher, the model-server probe, the worker processes, the host load and
/// the agents' quota and the server cache (null = the tick's own).
/// </summary>
public sealed class SharedParts
{
    /// <summary>The worker exits that wake the loop.</summary>
    public required WorkerExitWatcher Exits { get; init; }

    /// <summary>The model-server probe.</summary>
    public required ServerProbe Probe { get; init; }

    /// <summary>The worker processes and their job objects.</summary>
    public required WorkerProcesses Processes { get; init; }

    /// <summary>The PC's load.</summary>
    public required HostLoad Load { get; init; }

    /// <summary>The agents out of quota, one for a pool (MP-17); null = the tick's own.</summary>
    public AgentQuota? Quota { get; init; }

    /// <summary>The model servers, one cache for a pool (MP-H4); null = the tick's own.</summary>
    public ServerCache? Servers { get; init; }

    /// <summary>1062-B: the models of main, one per repository; null = the process's (<see cref="MainModels.Shared"/>).</summary>
    public MainModels? Models { get; init; }

    /// <summary>1111-B: the one counter of card numbers for every project here; null = the process's (<see cref="CardNumbering.Shared"/>).</summary>
    public CardNumbering? Numbering { get; init; }
}

/// <summary>
/// One project's parts, as <see cref="HostComposition"/> has always built them (MP-H2): its main
/// repository, committer, card reader, move requests, planner, template refresh, worktrees, slot
/// files, accounting, state files, health check, page data and actions, the adopter and its tick.
/// One project's composition and a pool's (<see cref="Pool.PoolComposition"/>) both build it.
/// </summary>
public sealed class ProjectParts : IDisposable
{
    private readonly GitWriter? _writer;
    private readonly List<FolderWake> _folders = [];
    private readonly WorkerAdopter _adopter;
    private readonly WorkerExitWatcher _exits;
    private readonly MainModels _models;

    /// <param name="config">The project's configuration.</param>
    /// <param name="configText">Its JSON: the planner's settings and <c>toolkit</c>.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="log">The project's log.</param>
    /// <param name="shared">What a pool shares (MP-H4).</param>
    /// <param name="requestTick">Asks the loop for a tick.</param>
    /// <param name="observe">The observe run's settings; null when this is not one.</param>
    /// <param name="exeDir">The exe's folder, above which ProjectToolkit is looked for (540-B); null looks nowhere.</param>
    /// <param name="poolProject">In a pool, the project's name: the dry run's lines name it (MP-22).</param>
    public ProjectParts(
        SchedulerConfig config,
        string configText,
        IClock clock,
        ISchedulerLog log,
        SharedParts shared,
        Action requestTick,
        ObserveSettings? observe = null,
        string? exeDir = null,
        string? poolProject = null)
    {
        Config = config;
        Log = log;
        _exits = shared.Exits;
        var git = new GitRunner();
        var main = new MainRepository(git, config.Repo, config.Remote, config.Branch);
        Main = main;
        var committer = new AnswersCommitter(git, config.Repo, Path.Combine(config.WorkspaceRoot, "answers"), config.Remote, config.Branch, log);
        var files = new StateFiles(config.LogDir);
        Files = files;
        var processes = shared.Processes;
        var slotFiles = new SlotFiles(config.LogDir);
        var cores = Environment.ProcessorCount;
        var accounting = new AccountingCsv(config.LogDir, clock, log);
        Accounting = accounting;
        // 920-B: the runner ends a leftover health tree before its first check, and keeps a check that
        // could not run from un-pausing a scheduler that is already paused-red.
        var health = new HealthCheck(config, clock, log,
            new HealthWorktreeRunner(config, git, processes, clock, log, cores, () => Tick is { } t ? t.State : SchedulerState.Running,
                pid => _exits.WatchOnce(pid)));
        var finishedCards = new FinishedCardCheck(main, config.JobsDir);
        var notice = new PageNotice(clock);
        Notice = notice;

        var reader = new CardReader(main, config.JobsDir, log) { ByCommit = true };
        Host = new TickHost(new TickHostParts
        {
            Config = config,
            Clock = clock,
            Log = log,
            Main = main,
            Reader = reader,   // 987-B
            Committer = committer,
            Worktrees = new SlotWorktrees(config.Repo, config.WorkspaceRoot, config.Remote, config.Branch),
            Starter = new SlotWorkerStarter(slotFiles, processes),
            Finisher = new WorkerFinisher(clock, log, accounting, health, processes, slotFiles),
            FinishedCards = finishedCards,
            Processes = processes,
            Files = files,
            Probe = shared.Probe,
            Cores = cores,
            Started = pid => _exits.Watch(pid),
            Ended = pid => _exits.Forget(pid),
            TickNow = requestTick,
        });
        ITickHost tickHost = observe is not null ? new ObserveTickHost(Host, log, clock)
            : config.DryRun ? new DryRunTickHost(Host, log, Host.TypeFor) { Project = poolProject }
            : Host;
        Tick = new SchedulerTick(config, clock, log, shared.Load, shared.Probe, processes, files, tickHost,
            config.HealthCommand.Length > 0 ? health : null, shared.Quota, shared.Servers);
        Host.Tick = Tick;
        Host.StoppedHold = new StoppedCardHold(committer, clock, log);   // WT-P2
        // WT-C10, WT-C11: the saved flags are read once, for the types made from the slots (a type's cap is
        // its enabled slots); nothing writes them any more, and a saved pool form does not look at them.
        var savedFlags = new WorkersEnabledState(files);
        foreach (var slot in Enumerable.Range(1, config.Slots))
        {
            Tick.Workers.Enabled[slot] = savedFlags.IsEnabled(slot);
        }

        // 974-B (WT-C7, WT-C10): main's workers.json is the only source of the worker types once it is
        // there, so it is read here - before the page is opened and before the workers are adopted.
        // Read only by the first tick, the Worker types panel and the adopter met the migration of the
        // slots instead (WT-C10.4), which is how every restart showed a "worker" where the developer
        // had saved a name, and how a Save in that first moment could write it back to main.
        // A pool's types come from the pool file instead (MP-C4): its projects' workers.json are not read.
        main.Fetch();
        if (poolProject is null && Host.ReadWorkers() is { } workersText)
        {
            Tick.Workers.Apply(workersText, []);
        }

        if (observe is not null)
        {
            Observer = new ObserveTicker(Tick, new OldSchedulerMirror(observe.OldLogDir, files, processes, clock, log), new ObserveRecorder(log, clock));
            log.Write(ObserveLine.Format(clock.Now, $"observing beside the scheduler whose log folder is {observe.OldLogDir}: starts nothing, commits nothing, no health check"));
        }

        if (config.StartPaused)
        {
            Tick.State = SchedulerState.Paused;
        }

        var planner = PlannerSettingsOf(configText);
        var pushFile = Path.Combine(config.LogDir, "planner-state.json");
        bool PushOn() => File.Exists(pushFile) && PlannerSwitch.Parse(File.ReadAllText(pushFile));
        if (planner is not null && observe is null)
        {
            Host.Planner = new PlannerStep(planner, git, committer, clock, log, PushOn, Tick.Retries,
                config.JobsDir, config.Remote, config.Branch) { Project = poolProject ?? "", Numbering = shared.Numbering ?? CardNumbering.Shared };
        }

        // 660-B: the scheduler as the only writer of card moves; never in an observe or dry run.
        if (observe is null && !config.DryRun)
        {
            Host.Moves = new CardRequestStep(committer, Scheduler.Core.Moves.CardRequests.Folder(config.LogDir), config.JobsDir, clock, log);
            Host.NewCards = new NewCardStep(committer, shared.Numbering ?? CardNumbering.Shared, config.JobsDir, poolProject ?? "", log);   // 1111-B

            // Dynamic locks (1020-B): off unless scheduler.json says "locks": true (section 8 card 9). On, the
            // host keeps the repository's table (DL-20, DL-22), answers .lock requests and releases on moves.
            Tick.Locks = Scheduler.Core.Config.LockSettings.Load(configText);
            if (Tick.Locks.On)
            {
                var table = new LockTableFile(config.LogDir, config.Repo, log);
                Host.LockFile = table;
                Host.LockRequests = new Scheduler.Host.Locks.LockRequestStep(
                    Scheduler.Core.Moves.CardRequests.Folder(config.LogDir), table.Read, table.Save, clock, log);
                Host.LockExe = Environment.ProcessPath ?? "";
                Host.Moves.Filed = Host.MoveFiled;

                // 1021-B: the hooks workers' git runs (DL-18), and the audit of main on every card read (DL-19).
                Scheduler.Host.Locks.HookFiles.Write(Scheduler.Core.Launch.WorkerFiles.HooksDir(config.LogDir), Host.LockExe, log);
                var tick = Tick;
                var audit = new Scheduler.Host.Locks.AuditRunner(git, config.Repo, table.Read, table.Save,
                    Scheduler.Core.Moves.CardRequests.Folder(config.LogDir), () => tick.Categories.Current?.AddOnly ?? [], clock, log);
                reader.MainSeen = head => audit.Run(head);
                Host.Audit = audit;   // 1025-B: and before a run's moves are applied
                log.Write($"locks: on, table {table.Path}, an older card waits up to {Tick.Locks.WaitMinutes} min");
            }
        }

        // 1062-B: the repository's model of main, shared with the other projects in it (DL-22), loaded in the
        // background (MM-5) from the first card read on, so it never competes with the page's start. 1063-B: the
        // tick reads it - cards, requests, settings files, main's commit, the finished-card check - with its ref read
        // in process each tick (MM-8), and every fetch of the tick is its gate's (MM-9), on the tick's interval.
        _models = shared.Models ?? MainModels.Shared;
        Model = _models.Join(new MainModelsJoin(config.Repo, config.Remote, config.Branch, config.JobsDir,
            TickHost.FetchInterval(config), ForceCliReader(configText), git, log, clock, LoadNow: false));
        Host.Feed = Model;
        reader.Feed = Model;
        finishedCards.Feed = Model;

        // 540-B: the generic files kept in step with ProjectToolkit's templates; never in an observe or dry run.
        var toolkit = ToolkitLocator.Find(StringSetting(configText, "toolkit"), config.Repo, exeDir);
        if (toolkit is not null && observe is null && !config.DryRun)
        {
            Host.Templates = new TemplateRefresh(git, toolkit, main, committer, config.JobsDir,
                Path.Combine(config.LogDir, TemplateState.FileName), log);
            // 1120-B: a paused project (its own pause or the pool's) refreshes no templates; resumed, the next tick does.
            Host.Templates.Paused = () => Tick is { } t && t.State == SchedulerState.Paused;
        }

        // 987-B: one background writer for every commit and push to main - the planner step, claims,
        // finishes and page actions - so the tick never waits on git.local; never in an observe or dry run.
        // One per repository in a pool.
        if (observe is null && !config.DryRun)
        {
            _writer = new GitWriter(log) { Moved = requestTick };
            committer.Writer = _writer;
            Host.Writer = _writer;
            if (Host.Planner is { } plannerStep)
            {
                plannerStep.OnTick = Host.OnTick;
            }
        }

        // 510-B: models.json for the worker titles, read at most once a minute rather than on every page rebuild.
        IReadOnlyDictionary<string, IReadOnlyList<string>>? models = null;
        var modelsRead = DateTimeOffset.MinValue;
        IReadOnlyDictionary<string, IReadOnlyList<string>> ModelsCached()
        {
            if (models is null || clock.Now - modelsRead >= TimeSpan.FromMinutes(1))
            {
                models = Host.ReadModels();
                modelsRead = clock.Now;
            }

            return models;
        }

        PageModels = ModelsCached;

        // 990-B: one overlay, fed by the page actions and laid over the tick's cards by the snapshot.
        var overlay = new PageCardOverlay(config.JobsDir, clock);
        // 450-B: everything the page shows that Host keeps beside the tick.
        var sources = new PageSnapshotSources
        {
            Overlay = overlay,
            Health = config.HealthCommand.Length > 0 && observe is null ? health : null,
            Recent = () => accounting.Recent,
            Runs = () => accounting.Runs,
            // 770-B: the Trends window's 14 days come from the same reader as the figures, not from
            // a second read of the file beside the page.
            TrendRuns = () => accounting.TrendRuns(clock.Now),
            FinishedWithoutUpdate = () => finishedCards.Flagged,
            // 580-A: the copies the project edited, which wait under Waiting on you for Overwrite or Keep.
            EditedTemplates = Host.Templates is { } tpl ? () => tpl.Edited.Select(e => new PageTemplateEdit(config.Project, e.Target)).ToList() : null,
            Notice = notice,
            PlannerPush = Host.Planner is not null ? PushOn : null,
            Stats = Host.Logs.Stats,
            // 964-B: each live build renders the new raw log text first, so a window grows between ticks.
            LiveRender = Host.Logs.CatchUp,
            Models = ModelsCached,
            // 974-B: until main's workers.json has been read, the Worker types panel has nothing to
            // show - the tick's types are then the migration of the slots, not main's (WT-C7).
            WorkersFromMain = () => Host.WorkersFromMain,
        };
        Data = new HostPageData(config, clock, Tick, main, files, () => HealthLog(health), sources);
        // 1090-B: the views data (VW-20 to VW-22), from the model of main and the page's slots only.
        Views = new ViewsSource(config.Project, config.JobsDir, () => Model.Model, () => Data.Snapshot().Slots);
        // 680-B: Open (MOVE-9) opens the card or file with this PC's default program. Opening belongs
        // to the PC running the scheduler (PAGE-7), and an observe run's page refuses it outright
        // (ObservePageActions), so an observe composition is given no launcher at all.
        var launcher = observe is null ? new ShellLauncher() : null;
        PageLauncher = launcher;
        Actions = new HostPageActions(new PageActionParts
        {
            Config = config,
            Clock = clock,
            Log = log,
            Files = files,
            Tick = Tick,
            Processes = processes,
            Committer = committer,
            Main = main,
            TickNow = requestTick,
            Health = health,
            Opener = launcher is null ? null : new CardOpener(committer, config.Repo, launcher),
            Launcher = launcher,
            // 580-A: /template-overwrite and /template-keep reach the same 540-B refresh.
            Templates = Host.Templates,
            PlannerPushGet = PushOn,
            PlannerPushSet = on => File.WriteAllText(pushFile, PlannerSwitch.Serialize(on)),
            Notice = notice.Failed,
            Succeeded = notice.Succeeded,
            // 974-B: and its Save is refused until the types have come from main (WT-C7).
            WorkersFromMain = () => Host.WorkersFromMain,
            Overlay = overlay,
        });
        _adopter = new WorkerAdopter(files, processes, clock, log);

        // 966-B: a worker's move request and a new planner batch wake the loop.
        if (Host.Moves is not null)
        {
            _folders.Add(new FolderWake(Scheduler.Core.Moves.CardRequests.Folder(config.LogDir), () => _exits.Wake(MovesWake.Kind(Scheduler.Core.Moves.CardRequests.Folder(config.LogDir))), create: true));
        }

        if (Host.Planner is not null && planner is not null)
        {
            _folders.Add(new FolderWake(planner.Outbox, () => _exits.Wake(TickWake.PlannerBatch), create: false));
        }
    }

    /// <summary>The project's configuration.</summary>
    public SchedulerConfig Config { get; }

    /// <summary>The project's log.</summary>
    public ISchedulerLog Log { get; }

    /// <summary>The project's main repository.</summary>
    public MainRepository Main { get; }

    /// <summary>1062-B: the repository's model of main (shared with the other projects in it); 1063-B: the tick reads it.</summary>
    public MainModelsRepository Model { get; }

    /// <summary>The project's state files (MP-H3: in its own logDir).</summary>
    public StateFiles Files { get; }

    /// <summary>The project's accounting.csv.</summary>
    public AccountingCsv Accounting { get; }

    /// <summary>The page's notices.</summary>
    public PageNotice Notice { get; }

    /// <summary>The tick host.</summary>
    public TickHost Host { get; }

    /// <summary>The project's tick.</summary>
    public SchedulerTick Tick { get; }

    /// <summary>The page's data.</summary>
    public HostPageData Data { get; }

    /// <summary>
    /// 1087-C: the same once-a-minute models.json read the page snapshot uses (510-B), exposed so the
    /// pool's Worker types panel resolves a type's effective model from it without a second reader.
    /// </summary>
    public Func<IReadOnlyDictionary<string, IReadOnlyList<string>>> PageModels { get; } = null!;

    /// <summary>1090-B: the project's views data, rebuilt only when something moved (VW-22).</summary>
    public ViewsSource Views { get; }

    /// <summary>The page's actions.</summary>
    public HostPageActions Actions { get; }

    /// <summary>The launcher the page's Open buttons use (MOVE-9); null in an observe run.</summary>
    public IFileLauncher? PageLauncher { get; }

    /// <summary>The observe run's tick (390-B); null when this is not an observe run.</summary>
    public ObserveTicker? Observer { get; }

    /// <summary>ADOPT-2, WT-W5 (MP-H6): adopt the workers in the project's running.json into its tick, against <paramref name="types"/> (a pool's) or the tick's own.</summary>
    public int AdoptWorkers(WorkerPool? types = null)
    {
        var count = 0;
        var adopted = new List<RunningWorker>();
        foreach (var entry in _adopter.Adopt(types ?? Tick.Workers.Pool).Entries)
        {
            Tick.Adopt(entry.Worker);
            _exits.Watch(entry.Worker.Pid);
            adopted.Add(entry.Worker);
            count++;
        }

        // DL-21 (1020-B): the adopted cards keep their held sets and open requests; every other card is released.
        if (Host.LocksOn)
        {
            var before = Host.ReadLocks();
            var after = Scheduler.Core.Launch.Adoption.KeepLocks(before, adopted);
            foreach (var gone in before.Holding.Where(c => after.Find(c.Card) is not { Holds: true }))
            {
                Log.Write($"locks: {gone.Card} released at start: its worker was not adopted");
            }

            Host.SaveLocks(after);
        }

        return count;
    }

    /// <summary>ACCT-6: read the earlier runs and the recent list from accounting.csv.</summary>
    public void SeedFromAccounting()
    {
        Log.Write($"accounting: {Accounting.SeedRunStats().Count} earlier run(s) found");
        Accounting.SeedRecent();
    }

    /// <summary>Write the project's status.txt (STAT-*): one line per slot, then the state.</summary>
    public void WriteStatus() => Host.WriteStatus(
        [.. Enumerable.Range(1, Math.Max(1, Tick.Workers.Slots)).Select(slot => new SlotState
        {
            Slot = slot,
            Enabled = Tick.Workers.Enabled.GetValueOrDefault(slot, true),
            Running = Tick.Running.GetValueOrDefault(slot),
        })],
        Tick.State);

    /// <summary>MP-15: the project's finished runs since <paramref name="from"/>, for the pool's shares.</summary>
    public IEnumerable<PoolRun> PoolRuns(string project, DateTimeOffset from) =>
        Accounting.Runs.Where(r => r.Finished > from).Select(r => new PoolRun(project, r.Finished - TimeSpan.FromMinutes(r.Minutes), r.Finished));

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var folder in _folders)
        {
            folder.Dispose();
        }

        _writer?.Dispose();
        _models.Leave(Model, Config.JobsDir);
    }

    // MM-2: "gitReader": "cli" in scheduler.json reads main through the git CLI instead of in process.
    private static bool ForceCliReader(string configText) =>
        string.Equals(StringSetting(configText, "gitReader"), "cli", StringComparison.OrdinalIgnoreCase);

    // HLTH-6: the /health page shows the last health check's log, or nothing before there is one.
    private static string? HealthLog(HealthCheck health)
    {
        var path = health.LogPath;
        try
        {
            return path.Length > 0 && File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? StringSetting(string configText, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(configText);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PlannerSettings? PlannerSettingsOf(string configText)
    {
        try
        {
            using var doc = JsonDocument.Parse(configText);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("planner", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                string? Get(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                return PlannerSettings.Create(Get("fetchRepo"), Get("outbox"));
            }
        }
        catch (JsonException)
        {
            // No readable config text: no planner.
        }

        return null;
    }
}
