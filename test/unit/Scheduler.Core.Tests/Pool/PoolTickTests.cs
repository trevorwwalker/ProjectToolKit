using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Pool;
using Scheduler.Core.Tests.Tick;
using Scheduler.Core.Tick;

namespace Scheduler.Core.Tests.Pool;

/// <summary>
/// The pool tick (multi-project design section 2, MP-10 to MP-22, MP-C9): real project ticks over
/// fake hosts, one shared quota and server cache, a fake clock; no git or processes.
/// </summary>
public sealed class PoolTickTests
{
    private readonly TickClock _clock = new();
    private readonly TickProcesses _processes = new();
    private readonly TickLog _log = new();
    private readonly AgentQuota _quota = new();
    private readonly ServerCache _servers;
    private readonly List<PoolRun> _finished = [];
    private readonly PoolTick _pool;
    private readonly Dictionary<string, TickHost> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public PoolTickTests()
    {
        _servers = new ServerCache(new TickProbe(), _clock, _log);
        _pool = new PoolTick(_clock, _log, _quota, _servers, () => _finished);
    }

    private static WorkerPool Types(string json) =>
        WorkerTypesJson.Parse($$"""{"pool":{{json}}}""", new SchedulerConfig())!;

    private static readonly WorkerPool Default4 = Types("""{"maxWorkers":4,"types":{"default":{"max":4}}}""");

    private PoolTickProject Project(string name, int weight, SchedulerConfig? config = null)
    {
        var host = _hosts.TryGetValue(name, out var h) ? h : new TickHost(_processes, _clock);
        _hosts[name] = host;
        var tick = new SchedulerTick(
            config ?? new SchedulerConfig(), _clock, _log, new TickLoad(), new TickProbe(), _processes, new TickFiles(), host,
            quota: _quota, servers: _servers);
        return new PoolTickProject(name, weight, tick);
    }

    // MP-P9: the snapshot carries each project's repository folder, "" when the project has none.
    [Fact]
    public void MP_P9_Snapshot_CarriesEachProjectsRepo()
    {
        var a = Project("A", 50) with { Repo = @"C:\repos\A" };
        _pool.Apply(Default4, [a, Project("B", 50)]);

        var snap = _pool.Snapshot();

        Assert.Equal(@"C:\repos\A", snap.Projects[0].Repo);
        Assert.Equal("", snap.Projects[1].Repo);
    }

    // 1055-C: the snapshot carries the pool's queue in start order: every card still queued, each
    // project's in its own order, the started one left out.
    [Fact]
    public void Card1055_Snapshot_QueueOrder_MergesTheProjectsQueues()
    {
        _pool.Apply(Types("""{"maxWorkers":1,"types":{"default":{"max":1}}}"""), [Project("A", 75), Project("B", 25)]);
        Queue("A", (1, "A"), (2, "B"), (3, "C"));
        Queue("B", (4, "A"), (5, "B"));

        _pool.Run();
        var order = _pool.Snapshot().QueueOrder;

        Assert.Equal(4, order.Count);
        Assert.Equal(2, order.Count(e => e.Project == "A"));
        Assert.Equal(["004-A", "005-B"], order.Where(e => e.Project == "B").Select(e => e.Card[..5]));
        Assert.Equal(["002-B", "003-C"], order.Where(e => e.Project == "A").Select(e => e.Card[..5]));
    }

    private TickHost Host(string name) => _hosts[name];

    private PoolTickProject P(string name) => _pool.Projects.Concat(_pool.Leaving).First(p => p.Name == name);

    private void Queue(string project, params (int Num, string Category)[] cards)
    {
        foreach (var (num, category) in cards)
        {
            Host(project).Cards.Add(TickHost.Card(CardFolder.Queue, num, category));
        }
    }

    private void Adopt(string project, int slot, int pid, Card card, string type = "default", DateTimeOffset? start = null)
    {
        Host(project).Cards.Add(card);
        var w = TickHost.Worker(slot, pid, start ?? _clock.Now, card) with { Type = type };
        _processes.Alive[pid] = w.Start;
        P(project).Tick.Adopt(w);
    }

    private List<string> Started(string project) => [.. Host(project).Starts.Where(s => s.Started).Select(s => s.Card)];

    private string Note(string project, string card) =>
        P(project).Tick.LastDispatch!.Notes.First(n => n.Card == card).Note;

    [Fact]
    public void MP_C9_OneProject_Unchanged()
    {
        // Run is Prepare, Dispatch and Complete in a row: the host sees today's steps and starts.
        var host = new TickHost(_processes, _clock);
        host.Cards.AddRange([TickHost.Card(CardFolder.Queue, 10, "A"), TickHost.Card(CardFolder.Queue, 20, "B")]);
        var tick = new SchedulerTick(new SchedulerConfig(), _clock, _log, new TickLoad(), new TickProbe(), _processes, new TickFiles(), host);

        tick.Run();

        Assert.Equal([(1, "010-A", true), (2, "020-B", true)], host.Starts);
        Assert.Contains(TickStep.ReadWorkers, host.Calls);
        Assert.Equal([TickStep.SaveRetries, TickStep.WriteStatus], host.Calls.TakeLast(2));
        Assert.NotSame(_quota, tick.Quota);   // one project gets its own quota and servers
    }

    [Fact]
    public void MP_10_OneDispatchAcrossProjects()
    {
        _pool.Apply(Default4, [Project("A", 50), Project("B", 50)]);
        Queue("A", (10, "A"), (20, "B"));
        Queue("B", (10, "A"), (20, "B"));

        _pool.Run();

        Assert.Equal(4, _pool.LastStarts.Count);
        Assert.Equal(2, Started("A").Count);
        Assert.Equal(2, Started("B").Count);
        foreach (var name in new[] { "A", "B" })
        {
            Assert.DoesNotContain(TickStep.ReadWorkers, Host(name).Calls);   // MP-C4: the project's workers.json is not read
            Assert.Equal([TickStep.SaveRetries, TickStep.WriteStatus], Host(name).Calls.TakeLast(2));
        }
    }

    [Fact]
    public void MP_11_TotalAndCapsCountEveryProject()
    {
        _pool.Apply(Types("""{"maxWorkers":3,"types":{"default":{"max":2},"spare":{"max":3,"agent":"other"}}}"""), [Project("A", 50), Project("B", 50)]);
        Adopt("A", 1, 501, TickHost.Card(CardFolder.Active, 10, "A"));
        Queue("B", (10, "A"), (20, "B"), (30, "C"));

        _pool.Run();

        // default is at 1 of 2 from A's worker: B gets one default, then one of spare; the total of 3 stops the third.
        Assert.Equal(["010-A", "020-B"], Started("B"));
        Assert.Equal(["default", "spare"], Host("B").TypeStarts.Select(t => t.Type));
        Assert.StartsWith("next up; waiting for default (2 of 2 running: 010-A, 010-A)", Note("B", "030-C-x"));   // A's worker counts toward the cap
    }

    [Fact]
    public void MP_12_RedProject_OthersRunOn()
    {
        _pool.Apply(Default4, [Project("A", 50), Project("B", 50)]);
        P("A").Tick.State = SchedulerState.PausedRed;
        Queue("A", (10, "A"));
        Queue("B", (10, "A"));

        _pool.Run();

        Assert.Empty(Started("A"));
        Assert.Equal(["010-A"], Started("B"));
        Assert.Equal("next up; dispatching is paused-red", Note("A", "010-A-x"));
    }

    [Fact]
    public void MP_13_ResumesFirst_AcrossProjects()
    {
        _pool.Apply(Types("""{"maxWorkers":1,"types":{"default":{"max":1}}}"""), [Project("A", 90), Project("B", 10)]);
        Queue("A", (10, "A"));
        Host("B").Cards.Add(TickHost.Card(CardFolder.Active, 20, "B"));   // an orphan: in active/, no worker

        _pool.Run();

        Assert.Empty(Started("A"));
        Assert.Equal(["020-B"], Started("B"));
    }

    [Fact]
    public void MP_14_FurthestBelowShare_Wins()
    {
        _pool.Apply(Types("""{"maxWorkers":1,"types":{"default":{"max":1}}}"""), [Project("A", 70), Project("B", 30)]);
        _finished.Add(new PoolRun("A", _clock.Now.AddMinutes(-90), _clock.Now.AddMinutes(-10)));
        Queue("A", (10, "A"));
        Queue("B", (10, "A"));

        _pool.Run();

        Assert.Equal(["010-A"], Started("B"));   // A has used everything; B's gap is larger
    }

    [Fact]
    public void MP_14_HighPriority_DoesNotJumpWeights()
    {
        _pool.Apply(Types("""{"maxWorkers":1,"types":{"default":{"max":1}}}"""), [Project("A", 70), Project("B", 30)]);
        Queue("A", (10, "A"));
        Host("B").Cards.Add(TickHost.Card(CardFolder.Queue, 5, "B") with { Prio = 0 });

        _pool.Run();

        Assert.Equal(["010-A"], Started("A"));
        Assert.Empty(Started("B"));
    }

    [Fact]
    public void MP_14_TieToFirstListed()
    {
        _pool.Apply(Types("""{"maxWorkers":1,"types":{"default":{"max":1}}}"""), [Project("B", 50), Project("A", 50)]);
        Queue("A", (10, "A"));
        Queue("B", (10, "A"));

        _pool.Run();

        Assert.Equal(["010-A"], Started("B"));
        Assert.Empty(Started("A"));
    }

    [Fact]
    public void MP_15_RunsFromAccountingAndRunning_ClippedToWindow()
    {
        _pool.Apply(Default4, [Project("A", 50), Project("B", 50)]);
        _finished.Add(new PoolRun("A", _clock.Now.AddHours(-3), _clock.Now.AddHours(-1)));      // half inside the window
        _finished.Add(new PoolRun("A", _clock.Now.AddHours(-5), _clock.Now.AddHours(-3)));      // ended before it
        Adopt("B", 1, 601, TickHost.Card(CardFolder.Active, 10, "A"), start: _clock.Now.AddMinutes(-30));

        var runs = _pool.Runs();

        Assert.Equal(2, runs.Count);
        Assert.Equal(60, PoolShares.Minutes(runs, "A", _clock.Now), 3);
        Assert.Equal(30, PoolShares.Minutes(runs, "B", _clock.Now), 3);
        var snap = _pool.Snapshot();
        Assert.Equal(2.0 / 3, snap.Projects[0].UsedShare, 3);
        Assert.Equal(0.5, snap.Projects[1].WeightShare, 3);
        Assert.Equal(1, snap.Running);
        Assert.Equal(1, snap.Types[0].RunningByProject["B"]);
    }

    [Fact]
    public void MP_16_OrphanOfRemovedProject_NotResumed()
    {
        _pool.Apply(Default4, [Project("A", 50), Project("B", 50)]);
        Adopt("B", 1, 701, TickHost.Card(CardFolder.Active, 10, "A"));
        Host("B").Cards.Add(TickHost.Card(CardFolder.Active, 20, "B"));   // an orphan

        _pool.Apply(Default4, [P("A")]);
        _pool.Run();

        Assert.Equal(["B"], _pool.Leaving.Select(p => p.Name));
        Assert.Empty(Host("B").Starts);

        _processes.Alive.Remove(701);   // its last worker ends
        _clock.Now = _clock.Now.AddSeconds(1);
        _pool.Run();

        Assert.Equal(["010-A"], Host("B").Finished);
        Assert.Empty(_pool.Leaving);
        Assert.Empty(Host("B").Starts);
    }

    [Fact]
    public void MP_17_QuotaByAgentName_AllProjects()
    {
        _pool.Apply(Types("""{"maxWorkers":4,"types":{"Opus":{"max":4,"agent":"claude"}}}"""), [Project("A", 50), Project("B", 50)]);
        Queue("A", (10, "A"));
        Queue("B", (10, "A"));
        _quota.Mark("claude", _clock.Now);

        _pool.Run();

        Assert.Same(_quota, P("A").Tick.Quota);
        Assert.Same(_quota, P("B").Tick.Quota);
        Assert.Same(_servers, P("B").Tick.Servers);
        Assert.Empty(Host("A").Starts);
        Assert.Empty(Host("B").Starts);
    }

    [Fact]
    public void MP_18_RetriesAndLimits_PerProject()
    {
        _pool.Apply(Default4, [Project("A", 50), Project("B", 50, new SchedulerConfig { MaxRetries = 0 })]);
        Host("A").Cards.Add(TickHost.Card(CardFolder.Active, 10, "A"));   // orphans, both with one retry used
        Host("B").Cards.Add(TickHost.Card(CardFolder.Active, 10, "A"));
        P("A").Tick.Retries["010-A"] = 1;
        P("B").Tick.Retries["010-A"] = 1;

        _pool.Run();

        Assert.Equal(["010-A"], Started("A"));    // A allows 2 retries
        Assert.Equal(2, P("A").Tick.Retries["010-A"]);
        Assert.Empty(Started("B"));               // B allows none: its own limit
        Assert.Equal(1, P("B").Tick.Retries["010-A"]);
    }

    [Fact]
    public void MP_19_Threads_FromPoolMaxWorkers()
    {
        _pool.Apply(Default4, [Project("A", 50)]);

        Assert.Equal(4, _pool.ThreadsPerWorker(16));
        Assert.Equal(1, _pool.ThreadsPerWorker(2));
    }

    [Fact]
    public void MP_20_SeparateRepositories_NeverOverlap()
    {
        _pool.Apply(Default4, [Project("A", 50), Project("B", 50)]);
        Adopt("A", 1, 801, TickHost.Card(CardFolder.Active, 10, "A"));
        Queue("B", (30, "A"));   // the same category letter in another project

        _pool.Run();

        Assert.Equal(["030-A"], Started("B"));
    }

    [Fact]
    public void MP_21_AboveShareNote()
    {
        _pool.Apply(Types("""{"maxWorkers":1,"types":{"default":{"max":1}}}"""), [Project("A", 50), Project("B", 50)]);
        _finished.Add(new PoolRun("B", _clock.Now.AddMinutes(-70), _clock.Now.AddMinutes(-10)));
        Queue("A", (10, "A"));
        Queue("B", (10, "A"), (20, "B"));

        _pool.Run();

        Assert.Equal(["010-A"], Started("A"));
        Assert.Equal("next up; B is above its share (100% of 50%)", Note("B", "010-A-x"));
        Assert.StartsWith("next up; waiting for default (1 of 1 running: 010-A)", Note("B", "020-B-x"));
    }

    [Fact]
    public void MP_21_NotServedNote()
    {
        _pool.Apply(Types("""{"maxWorkers":2,"types":{"default":{"max":2,"projects":["A"]}}}"""), [Project("A", 50), Project("B", 50)]);
        Queue("A", (10, "A"));
        Queue("B", (10, "A"));

        _pool.Run();

        Assert.Equal(["010-A"], Started("A"));
        Assert.Empty(Started("B"));
        Assert.Equal("next up; no worker type serves project B", Note("B", "010-A-x"));
    }

    [Fact]
    public void MP_22_DryRun_NamesProject()
    {
        var card = TickHost.Card(CardFolder.Queue, 10, "A");

        Assert.Equal("DRY RUN AmigaX spark in slot 2 <- 010-A-x", PoolDispatcher.DryRunLine("AmigaX", "spark", 2, card, resume: false));
        Assert.Equal("DRY RUN AmigaX spark in slot 2 <- 010-A-x (resume)", PoolDispatcher.DryRunLine("AmigaX", "spark", 2, card, resume: true));
    }

    // 1029-B: A has 2 workspaces, one held by an adopted worker on slot1: one more card starts, in slot2 (the lowest
    // free), the rest wait for a workspace while B's cards fill the pool; the snapshot shows 2 of 2 busy.
    [Fact]
    public void Card1029_Workspaces_CapAProject_LowestFreeSlot_NoteAndSnapshot()
    {
        _pool.Apply(Types("""{"maxWorkers":6,"types":{"default":{"max":6}}}"""),
            [Project("A", 90) with { Workspaces = 2 }, Project("B", 10)]);
        Adopt("A", 1, 601, TickHost.Card(CardFolder.Active, 5, "E"));
        Queue("A", (10, "A"), (20, "B"), (30, "C"));
        Queue("B", (10, "A"), (20, "B"));

        _pool.Run();

        Assert.Equal([(2, "010-A", true)], Host("A").Starts);
        Assert.Equal(2, Started("B").Count);
        Assert.Equal("next up; waits for a workspace (2 of 2 busy)", Note("A", "020-B-x"));
        var a = _pool.Snapshot().Projects.First(p => p.Name == "A");
        Assert.Equal((2, 2), (a.Running, a.Workspaces));
        Assert.Equal(6, _pool.Snapshot().Projects.First(p => p.Name == "B").Workspaces);   // missing = maxWorkers
    }

    // 1029-B: workspaces lowered to 1 while A's worker runs on slot3: it keeps its folder and runs on, and no new
    // worker of A starts until it has ended; then the next one takes slot1.
    [Fact]
    public void Card1029_Workspaces_Lowered_RunningWorkerFinishesInPlace()
    {
        _pool.Apply(Default4, [Project("A", 50) with { Workspaces = 1 }]);
        Adopt("A", 3, 701, TickHost.Card(CardFolder.Active, 5, "E"));
        Queue("A", (10, "A"));

        _pool.Run();

        Assert.Empty(Host("A").Starts);
        Assert.Contains(P("A").Tick.Busy(), w => w.Slot == 3);
        Assert.Equal("next up; waits for a workspace (1 of 1 busy)", Note("A", "010-A-x"));

        _processes.Alive.Remove(701);   // it ends; its card (or the queued one) then starts in slot1, the only workspace
        _clock.Now = _clock.Now.AddSeconds(1);
        _pool.Run();
        _clock.Now = _clock.Now.AddSeconds(1);
        _pool.Run();

        Assert.NotEmpty(Host("A").Starts);
        Assert.All(Host("A").Starts, s => Assert.Equal(1, s.Item1));
    }
}
