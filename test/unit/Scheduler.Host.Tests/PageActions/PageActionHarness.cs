// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net.Http;
using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Core.WebContract;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;
using Scheduler.Host.PageActions;
using Scheduler.Host.Program;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Program;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;
using Scheduler.Web.Server;
using Scheduler.Web.Views;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// Host's page actions running where the page can reach them: the real Web server (050-C) on a free
/// port, the real views (090-C) rendering the page, and Host's <see cref="HostPageActions"/> behind
/// it, over a <see cref="SchedulerTick"/> whose host side, process table and git are faked.
/// Tests post to it as the page does and read what the scheduler kept.
/// </summary>
internal sealed class PageActionHarness : IDisposable
{
    private readonly TempDir _temp = new();

    public PageActionHarness(
        string? categoriesJson = null,
        string? workersJson = null,
        IMainRepository? main = null,
        IJobsCommitter? committer = null,
        string project = "ptk-280b",
        IFileLauncher? launcher = null,
        IReadOnlyDictionary<string, AgentDefinition>? agents = null,
        bool hostRenderer = false,
        bool? workersFromMain = null)
    {
        LogDir = Path.Combine(_temp.Path, "logs");
        Directory.CreateDirectory(LogDir);
        Log = new ListLog();
        Clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 11, 42, 0, TimeSpan.Zero));
        Files = new StateFiles(LogDir);
        Processes = new ActionProcesses();
        Host = new ActionTickHost { CategoriesJson = categoriesJson, WorkersJson = workersJson ?? """{"slots":2}""" };
        Notices = [];
        Config = new SchedulerConfig
        {
            Project = project,
            JobsDir = "docs/jobs",
            LogDir = LogDir,
            Repo = _temp.Path,
            WorkspaceRoot = Path.Combine(_temp.Path, "ws"),
            Slots = 2,
            PageStopGraceMinutes = 5,
            PollSeconds = 1,
            Agents = agents ?? new Dictionary<string, AgentDefinition>(),
        };
        Tick = new SchedulerTick(Config, Clock, Log, new TickLoad(), new TickProbe(), Processes, Files, Host);
        // 974-B: null means the host gates nothing, which is what every test but the start-up ones wants.
        Func<bool>? fromMain = workersFromMain is { } gate ? () => gate : null;
        Actions = new HostPageActions(new PageActionParts
        {
            Config = Config,
            Clock = Clock,
            Log = Log,
            Files = Files,
            Tick = Tick,
            Processes = Processes,
            Committer = committer ?? new RecordingCommitter(),
            Main = main ?? new NullMain(),
            TickNow = () => TickAsks++,
            Notice = message => Notices.Add(message),
            Succeeded = () => Successes++,
            // 974-B: null (every test but the ones about start-up) means the host gates nothing.
            WorkersFromMain = fromMain,
            // 680-B: what the composition gives the page actions for Open (MOVE-9), built the same way.
            Launcher = launcher,
            Opener = launcher is null
                ? null
                : new CardOpener(
                    new AnswersCommitter(new GitRunner(), Config.Repo, Path.Combine(Config.WorkspaceRoot, "answers"), Config.Remote, Config.Branch),
                    Config.Repo,
                    launcher),
        });

        Sources = fromMain is null ? null : new PageSnapshotSources { WorkersFromMain = fromMain };
        Data = new HarnessPageData(Config, Clock, Tick, Sources);
        // 938-B: hostRenderer asks for the renderer the way HostComposition builds it, so a test can
        // see what the host hands the Worker types panel; the plain harness keeps its own renderer.
        Renderer = hostRenderer
            ? HostComposition.CreateRenderer(Config, Tick, new PageNotice(Clock), Log)
            : new PageRenderer();
        Feed = new LiveFeed(Clock, Data);
        Server = new PageServer(new PageServerOptions
        {
            Port = 0,
            RemoteControl = true,
            Data = Data,
            Actions = Actions,
            Renderer = Renderer,
            Feed = Feed,
        });
        Server.Start();
        Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{Server.Port}/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public string LogDir { get; }

    public ListLog Log { get; }

    public FakeClock Clock { get; }

    public StateFiles Files { get; }

    public ActionProcesses Processes { get; }

    public ActionTickHost Host { get; }

    public SchedulerConfig Config { get; }

    public SchedulerTick Tick { get; }

    public HostPageActions Actions { get; }

    public List<string> Notices { get; }

    /// <summary>How many actions were accepted (MOVE-11 clears the notice on each).</summary>
    public int Successes { get; private set; }

    public HarnessPageData Data { get; }

    /// <summary>The sources the snapshot was built from, or null when the harness gates nothing (974-B).</summary>
    public PageSnapshotSources? Sources { get; }

    public PageRenderer Renderer { get; }

    public LiveFeed Feed { get; }

    public PageServer Server { get; }

    public HttpClient Client { get; }

    /// <summary>How many times an action asked the loop for a tick.</summary>
    public int TickAsks { get; private set; }

    /// <summary>The log folder's file, or null when it was never written.</summary>
    public string? Read(string name)
    {
        var path = Path.Combine(LogDir, name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>A worker on a slot, as the scheduler would have it after a start (WRK-12).</summary>
    public RunningWorker Running(int slot, string card = "010-A-x", string category = "A", bool msgs = true, string? log = null)
    {
        var worker = new RunningWorker
        {
            Slot = slot,
            Pid = 1000 + slot,
            Start = Clock.Now,
            Card = card,
            Category = category,
            Id = card[..Math.Min(card.Length, 5)],
            Msgs = msgs,
            Job = $"ProjectToolkit-{Config.Project}-{slot}-1000",
            Log = log ?? Path.Combine(LogDir, $"{card}-view.log"),
            RawLog = log ?? Path.Combine(LogDir, $"{card}-raw.jsonl"),
        };
        Processes.Alive[worker.Pid] = worker.Start;
        Tick.Adopt(worker);
        return worker;
    }

    /// <summary>The readable log of the worker on a slot, as WRK-9 gives it.</summary>
    public string WorkerLog(int slot) => Tick.Running[slot].Log;

    /// <summary>One tick, as the loop would run it.</summary>
    public void Tick_Once() => Tick.Run();

    /// <summary>
    /// A POST as the page's form makes it (303 back to the page). The work itself runs on the page
    /// action queue's worker thread (957-B), so a test that reads what the click did waits for the
    /// queue to empty; pass <paramref name="drain"/> false to read the reply before the work lands.
    /// </summary>
    public async Task<HttpResponseMessage> Post(string path, string? body = null, bool drain = true)
    {
        var response = await Client.PostAsync(path, new StringContent(body ?? string.Empty, Encoding.UTF8, "application/x-www-form-urlencoded"));
        if (drain)
        {
            Assert.True(Server.Queue.WaitIdle(TimeSpan.FromSeconds(10)), "the page's action queue never went idle");
        }

        return response;
    }

    public void Dispose()
    {
        Client.Dispose();
        Server.Dispose();
        _temp.Dispose();
    }

    /// <summary>
    /// What the page needs from the host: the snapshot Host builds (HostPageSnapshot, CFG-18) and the
    /// log folder the /log route reads. The texts no test here needs answer "none".
    /// </summary>
    internal sealed class HarnessPageData(SchedulerConfig config, IClock clock, SchedulerTick tick, PageSnapshotSources? sources = null) : IPageData
    {
        public PageSnapshot Snapshot() => HostPageSnapshot.Build(config, clock, tick, sources);

        public string? FontPath => null;

        public string LogDirectory => config.LogDir;

        public string? Card(string name) => null;

        public string? Request(string name) => null;

        public string? HealthText() => null;

        public string StatusJson() => "{}";

        public IReadOnlyList<LiveWorker> RunningWorkers() =>
            HostPageSnapshot.Live(config, clock, tick);
    }
}
