// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Host.Processes;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.PageActions;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// 982-B: each wake reaches the tick as its kind, wakes before a tick merge (any full kind wins), and a
/// light wake with main unchanged skips the card read while a poll always reads.
/// </summary>
public class WakeKindTests
{
    private sealed class NoListener : IPageListener
    {
        public int Port => 1;

        public void Start() { }

        public void Stop() { }

        public void Dispose() { }
    }

    /// <summary>A runner whose waits run <see cref="Waits"/> in turn (each returns true), then end it.</summary>
    private sealed class Rig : IDisposable
    {
        public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 20, 1, 0, 0, TimeSpan.Zero));
        public ListLog Log { get; } = new();
        public SchedulerConfig Config { get; } = new() { Project = "test-" + Guid.NewGuid().ToString("N")[..10], PollSeconds = 15 };
        public WorkerExitWatcher Exits { get; } = new();
        public List<TickWake> Kinds { get; } = [];
        public Queue<Action<SchedulerRunner>> Waits { get; } = new();
        private bool _shutdown;

        public void Run(Action<TickWake>? tick = null)
        {
            SchedulerRunner? runner = null;
            runner = new SchedulerRunner(new RunnerParts
            {
                Config = Config,
                Clock = Clock,
                Log = Log,
                MutexWait = TimeSpan.FromSeconds(1),
                AdoptWorkers = () => { },
                SeedFromAccounting = () => { },
                StartListener = () => new NoListener(),
                OpenBrowser = _ => { },
                NoBrowser = true,
                ShutdownRequested = () => _shutdown,
                Tick = wake =>
                {
                    Kinds.Add(wake);
                    tick?.Invoke(wake);
                },
                Draining = () => false,
                WorkersRunning = () => 0,
                WriteStatusFile = () => { },
                WaitForTick = (_, _) =>
                {
                    if (Waits.Count == 0)
                    {
                        _shutdown = true;
                        return false;
                    }

                    Waits.Dequeue()(runner!);
                    return true;
                },
                Wake = () => Exits.Wake(TickWake.Click),
                TakeWake = Exits.TakeWake,
            });
            runner.Run();
        }

        public void Dispose() => Exits.Dispose();
    }

    [Theory]
    [InlineData(null, TickWake.MoveRequest, TickWake.MoveRequest)]
    [InlineData(TickWake.PlannerBatch, TickWake.PlannerBatch, TickWake.PlannerBatch)]
    [InlineData(TickWake.MoveRequest, TickWake.PlannerBatch, TickWake.MoveRequest)]
    [InlineData(TickWake.MoveRequest, TickWake.WorkerExit, TickWake.WorkerExit)]
    [InlineData(TickWake.Click, TickWake.HealthCheck, TickWake.Click)]
    [InlineData(TickWake.Click, TickWake.WorkerExit, TickWake.Poll)]
    [InlineData(TickWake.HealthCheck, TickWake.Poll, TickWake.Poll)]
    public void Merge_AnyFullKindWins(TickWake? pending, TickWake kind, TickWake merged)
    {
        Assert.Equal(merged, TickWakes.Merge(pending, kind));
        Assert.Equal(TickWakes.IsFull(merged), TickWakes.IsFull(kind) || (pending is { } p && TickWakes.IsFull(p)));
    }

    [Fact]
    public void Watcher_EachSourceSignalsItsKind_AndATakeForgetsIt()
    {
        using var exits = new WorkerExitWatcher();
        Assert.Null(exits.TakeWake());

        exits.Watch(int.MaxValue); // no such process: a worker that has already ended
        Assert.Equal(TickWake.WorkerExit, exits.TakeWake());
        Assert.Null(exits.TakeWake());

        exits.WatchOnce(int.MaxValue); // the health check, already ended
        Assert.Equal(TickWake.HealthCheck, exits.TakeWake());

        exits.Wake(); // a cause that is not known (the tray's exit)
        Assert.Equal(TickWake.Poll, exits.TakeWake());

        exits.Wake(TickWake.MoveRequest);
        exits.Wake(TickWake.PlannerBatch);
        Assert.Equal(TickWake.MoveRequest, exits.TakeWake());
        Assert.True(exits.WaitForTick(TimeSpan.Zero)); // and the wait still ends at once
    }

    [Theory]
    [InlineData(TickWake.MoveRequest)]
    [InlineData(TickWake.PlannerBatch)]
    [InlineData(TickWake.HealthCheck)]
    [InlineData(TickWake.WorkerExit)]
    [InlineData(TickWake.Click)]
    public void Runner_EachWakeKind_ReachesTheTickAsThatKind(TickWake kind)
    {
        using var rig = new Rig();
        rig.Waits.Enqueue(_ => rig.Exits.Wake(kind));

        rig.Run();

        Assert.Equal(new[] { TickWake.Poll, kind }, rig.Kinds); // the first tick is the poll
    }

    [Fact]
    public void Runner_PageAsks_IsAClick_AndThePollTimeout_IsAPoll()
    {
        using var rig = new Rig();
        rig.Waits.Enqueue(runner => runner.RequestTick());
        rig.Waits.Enqueue(_ => rig.Clock.Advance(TimeSpan.FromSeconds(15)));

        rig.Run();

        Assert.Equal(new[] { TickWake.Poll, TickWake.Click, TickWake.Poll }, rig.Kinds);
    }

    [Fact]
    public void Runner_SeveralWakesBeforeATick_MergeIntoOneTick()
    {
        using var rig = new Rig();
        rig.Waits.Enqueue(_ =>
        {
            rig.Exits.Wake(TickWake.MoveRequest);
            rig.Exits.Wake(TickWake.PlannerBatch);
        });
        rig.Waits.Enqueue(_ =>
        {
            rig.Exits.Wake(TickWake.MoveRequest);
            rig.Exits.Wake(TickWake.WorkerExit);
            rig.Exits.Wake(TickWake.HealthCheck);
        });
        rig.Waits.Enqueue(_ =>
        {
            rig.Exits.Wake(TickWake.PlannerBatch);
            rig.Clock.Advance(TimeSpan.FromSeconds(15)); // the poll is due as well
        });

        rig.Run();

        Assert.Equal(new[] { TickWake.Poll, TickWake.MoveRequest, TickWake.WorkerExit, TickWake.Poll }, rig.Kinds);
    }

    [Fact]
    public void Runner_ASignalWhoseKindWasAlreadyTaken_DoesNotTickAgain()
    {
        using var rig = new Rig();
        rig.Waits.Enqueue(_ => { }); // signalled, but nothing pending: an earlier tick took its kind

        rig.Run();

        Assert.Equal(new[] { TickWake.Poll }, rig.Kinds);
    }

    [Fact]
    public void Tick_PlannerBatchWithMainUnchanged_IsLight_PollAndMovedMainAreFull()
    {
        using var harness = new PageActionHarness();
        harness.Host.Sha = "aaa";
        using var rig = new Rig();
        var reads = new List<int>();
        rig.Waits.Enqueue(_ => rig.Exits.Wake(TickWake.PlannerBatch)); // main unchanged: light
        rig.Waits.Enqueue(_ => rig.Exits.Wake(TickWake.MoveRequest)); // still unchanged: light
        rig.Waits.Enqueue(_ =>
        {
            harness.Host.Sha = "bbb"; // a push moved main
            rig.Exits.Wake(TickWake.PlannerBatch);
        });
        rig.Waits.Enqueue(_ => rig.Exits.Wake()); // a poll wake, main unchanged: full all the same

        rig.Run(wake =>
        {
            harness.Tick.Run(wake);
            reads.Add(harness.Host.Reads);
        });

        Assert.Equal(new[] { TickWake.Poll, TickWake.PlannerBatch, TickWake.MoveRequest, TickWake.PlannerBatch, TickWake.Poll }, rig.Kinds);
        Assert.Equal(new[] { 1, 1, 1, 2, 3 }, reads);
    }

    [Fact]
    public void Cost_Line_SaysWhatWokeTheTicks()
    {
        using var rig = new Rig();
        rig.Waits.Enqueue(_ => rig.Exits.Wake(TickWake.MoveRequest));
        rig.Waits.Enqueue(_ =>
        {
            rig.Clock.Advance(TimeSpan.FromMinutes(1));
            rig.Exits.Wake(TickWake.MoveRequest);
        });

        rig.Run();

        var lines = rig.Log.Lines.Where(l => l.StartsWith("tick took", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Contains("woken by Poll 1.", lines[0], StringComparison.Ordinal);
        Assert.Contains("woken by Poll 1, MoveRequest 1.", lines[1], StringComparison.Ordinal);
    }
}
