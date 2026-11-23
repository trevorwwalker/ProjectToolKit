using Scheduler.Host.State;
using Scheduler.Core.Contracts;
using Scheduler.Core.Health;
using Scheduler.Core.Tests.MainModel;
using Scheduler.Core.Tick;
using Scheduler.Host.Git;
using Scheduler.Host.Processes;
using Scheduler.Host.Slots;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.PageActions;
using Scheduler.Host.Tests.Processes;
using Scheduler.Host.Tests.Program;

namespace Scheduler.Host.Tests.Tick;

/// <summary>
/// 1063-B (state-cache.md MM-14, MM-15): the tick reads the repository's model of main - cards, requests, settings
/// files, main's commit - so an idle tick starts no git process unless the fetch gate lets a fetch run.
/// </summary>
public sealed class TickReadsModelTests : IDisposable
{
    private static readonly Dictionary<string, string> Seed = new(StringComparer.Ordinal)
    {
        ["jobs/README.md"] = "# Jobs\n",
        ["jobs/workers.json"] = """{"slots":1}""",
        ["jobs/models.json"] = """{"claude-opus-5-5":{"short":"opus"}}""",
        ["jobs/categories.json"] = """{"categories":{"B":{"owns":["code/"]}}}""",
        ["jobs/queue/1-B-a.md"] = "# 1-B: a\n\nCategory: B\nStatus: waiting: planner\n",
        ["jobs/review/4-B-d.md"] = "# 4-B: d\n\nCategory: B\nStatus: review: look\n",
        ["jobs/done/3-B-c.md"] = "# 3-B: c\n\nCategory: B\nStatus: done\n",
        ["jobs/requests/001-a.md"] = "# A\n\nStatus: open\n",
    };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ptk-1063-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void MM_15_IdleTick_StartsNoGit_UnlessAFetchIsDue()
    {
        var (tick, feed, git, host) = Build();
        var clock = (FakeMain.FixedClock)feed.Main.Clock;

        Run(TickWake.Poll);   // the first tick fetches
        Assert.Equal(1, feed.Fetches);

        foreach (var wake in new[] { TickWake.Click, TickWake.Poll, TickWake.MoveRequest, TickWake.Click })
        {
            clock.Now += TimeSpan.FromSeconds(5);
            Run(wake);
        }

        Assert.Equal(1, feed.Fetches);   // within the poll: the fetch is shared, a click never fetches
        clock.Now += TimeSpan.FromSeconds(60);
        Run(TickWake.Poll);
        Assert.Equal(2, feed.Fetches);   // the poll's fetch is due again

        Assert.Empty(git.Calls);         // no rev-parse, ls-tree, cat-file or show: every read was the model's
        Assert.Contains(tick.Cards, c => c.Id == "4-B");

        void Run(TickWake wake)
        {
            host.Woke(wake);   // as the compositions do: the wake decides whether the read may fetch (1002-B)
            tick.Run(wake);
            Settled(feed);   // 1124-B: the fetch is the tick's no longer
        }
    }

    // 1124-B: the tick never waits for the fetch. A slow fetch leaves the tick reading the model as it stands; once
    // it completes and moves main, it wakes a tick (TickNow), whose read sees the new commit.
    [Fact]
    public void MM_9_SlowFetch_DoesNotDelayTheTick_AFetchThatMovesMainWakesATick()
    {
        var release = new ManualResetEventSlim();
        var woken = 0;
        var (tick, feed, _, host) = Build(() => Interlocked.Increment(ref woken));
        feed.RefFollowsFetch = true;
        feed.FetchStarted = () => release.Wait(TimeSpan.FromSeconds(30));
        var before = feed.Main.Commit;
        var pushed = feed.Main.Stage(new Dictionary<string, string?> { ["jobs/queue/6-B-f.md"] = "# 6-B: f\n\nCategory: B\nStatus: waiting: planner\n" });

        var took = System.Diagnostics.Stopwatch.StartNew();
        host.Woke(TickWake.Poll);
        tick.Run(TickWake.Poll);
        took.Stop();

        Assert.False(BackgroundFetch.For(feed).Running.IsCompleted);   // the fetch is still running
        Assert.True(took.Elapsed < TimeSpan.FromSeconds(5), $"tick took {took.Elapsed}");
        Assert.Equal(0, Volatile.Read(ref woken));
        Assert.DoesNotContain(tick.Cards, c => c.Id == "6-B");

        release.Set();
        Settled(feed);
        Assert.Equal(1, Volatile.Read(ref woken));
        Assert.Equal(pushed, feed.Main.Commit);
        Assert.NotEqual(before, pushed);

        host.Woke(TickWake.Click);   // the wake TickNow raises: a click's tick pays no fetch
        tick.Run(TickWake.Click);
        Assert.Contains(tick.Cards, c => c.Id == "6-B");
        Assert.Equal(1, feed.Fetches);
    }

    [Fact]
    public void MM_14_TickHost_SettingsAndCommit_FromTheModel()
    {
        var (_, feed, git, host) = BuildHost();

        Assert.Equal(Seed["jobs/workers.json"], host.ReadWorkers());
        Assert.True(host.WorkersFromMain);
        Assert.Equal(["opus"], host.ReadModels()["claude-opus-5-5"]);
        Assert.Equal(Seed["jobs/categories.json"], host.ReadCategories());
        Assert.Equal(feed.Main.Model.Commit, host.MainSha());

        feed.Main.Stage(new Dictionary<string, string?> { ["jobs/workers.json"] = """{"slots":2}""" });
        Assert.Equal(feed.Main.Head, host.MainSha());   // MM-8: the ref read in process moves the model
        Assert.Equal("""{"slots":2}""", host.ReadWorkers());
        Assert.Empty(git.Calls);
    }

    private static void Settled(IMainFeed feed) => Assert.True(BackgroundFetch.For(feed).Running.Wait(TimeSpan.FromSeconds(10)));

    private (SchedulerTick Tick, FakeFeed Feed, CountingMain Git, Scheduler.Host.Tick.TickHost Host) Build(Action? tickNow = null)
    {
        var (config, feed, git, host) = BuildHost(tickNow);
        var tick = new SchedulerTick(config, feed.Main.Clock, new ListLog(), new TickLoad(), new TickProbe(),
            new TickProcesses(), new TickFiles(), host);
        host.Tick = tick;
        return (tick, feed, git, host);
    }

    private (SchedulerConfig Config, FakeFeed Feed, CountingMain Git, Scheduler.Host.Tick.TickHost Host) BuildHost(Action? tickNow = null)
    {
        Directory.CreateDirectory(_dir);
        var feed = FakeFeed.Over(Seed, TimeSpan.FromSeconds(13));   // the tick's fetch interval: PollSeconds 15 less 2
        var git = new CountingMain();
        var clock = feed.Main.Clock;
        var log = new ListLog();
        var config = new SchedulerConfig
        {
            Repo = Path.Combine(_dir, "repo"),
            WorkspaceRoot = Path.Combine(_dir, "ws"),
            LogDir = Path.Combine(_dir, "logs"),
            Project = "Model",
        };
        var processes = new TickProcesses();
        var slotFiles = new SlotFiles(config.LogDir);
        var host = new Scheduler.Host.Tick.TickHost(new Scheduler.Host.Tick.TickHostParts
        {
            Config = config,
            Clock = clock,
            Log = log,
            Main = git,
            Reader = new CardReader(git, config.JobsDir, log) { ByCommit = true, Feed = feed },
            Committer = new RecordingCommitter(),
            Worktrees = new SlotWorktrees(config.Repo, config.WorkspaceRoot, config.Remote, config.Branch),
            Starter = new SlotWorkerStarter(slotFiles, processes),
            Finisher = new WorkerFinisher(clock, log, new AccountingCsv(config.LogDir, clock, log),
                new HealthCheck(config, clock, log, new NoHealthRunner()), processes, slotFiles),
            Processes = processes,
            Files = new TickFiles(),
            Probe = new TickProbe(),
            TickNow = tickNow,
        })
        {
            Feed = feed,
        };
        return (config, feed, git, host);
    }
}
