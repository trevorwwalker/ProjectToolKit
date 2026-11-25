// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;

namespace Scheduler.Core.Tests.Tick;

/// <summary>
/// 981-A: a woken tick does only the steps its wake needs. With main unchanged and nothing finished,
/// a move-request, planner-batch or health-check wake does not re-read the cards and does not
/// dispatch; a worker exit, a click and the normal poll do the full tick; anything unknown does the
/// full tick.
/// </summary>
public sealed class WokenTickTests
{
    private static readonly TickStep[] FullCalls =
    [
        TickStep.IndexLogs, TickStep.ReadCards, TickStep.FixDuplicates, TickStep.PlannerStep,
        TickStep.ReadCategories, TickStep.ReadWorkers, TickStep.ReadModels, TickStep.ReadRequests,
        TickStep.SaveRetries, TickStep.WriteStatus,
    ];

    private static readonly TickStep[] LightCalls = [TickStep.IndexLogs, TickStep.PlannerStep, TickStep.WriteStatus];

    private readonly TickClock _clock = new();
    private readonly TickProcesses _processes = new();
    private readonly TickHost _host;
    private readonly SchedulerTick _tick;

    public WokenTickTests()
    {
        _host = new TickHost(_processes, _clock) { Sha = "aaa" };
        _tick = new SchedulerTick(
            new SchedulerConfig(), _clock, new TickLog(), new TickLoad(), new TickProbe(), _processes, new TickFiles(), _host);
    }

    /// <summary>A first full tick, so the cards were read at main "aaa"; the host's calls are cleared after it.</summary>
    private void FirstTick()
    {
        _tick.Run();
        _host.Calls.Clear();
        _host.Starts.Clear();
    }

    private RunningWorker Running(int slot, int pid, Card card)
    {
        var w = TickHost.Worker(slot, pid, _clock.Now, card);
        _processes.Alive[pid] = w.Start;
        _tick.Adopt(w);
        return w;
    }

    [Theory]
    [InlineData(TickWake.MoveRequest)]
    [InlineData(TickWake.PlannerBatch)]
    [InlineData(TickWake.HealthCheck)]
    public void LightWake_MainUnchanged_NothingFinished_NoCardReadNoDispatch(TickWake wake)
    {
        FirstTick();
        _host.Cards.Add(TickHost.Card(CardFolder.Queue, 10, "A"));   // not on main as far as the tick knows: its sha is the same

        var steps = new List<TickStep>();
        _tick.StepStarting += steps.Add;
        _tick.Run(wake);

        Assert.Equal(LightCalls, _host.Calls);
        Assert.Empty(_host.Starts);
        Assert.DoesNotContain(TickStep.ReadCards, steps);
        Assert.DoesNotContain(TickStep.Dispatch, steps);
        Assert.Equal(TickStep.WriteStatus, steps[^1]);
    }

    [Fact]
    public void PlannerBatchWake_BatchMovesMain_ReReadsAfterTheBatchAndDispatches()
    {
        FirstTick();
        _host.OnPlannerStep = () =>
        {
            _host.Cards.Add(TickHost.Card(CardFolder.Queue, 10, "A"));
            _host.Sha = "bbb";
        };

        _tick.Run(TickWake.PlannerBatch);

        Assert.Equal(
            [
                TickStep.IndexLogs, TickStep.PlannerStep, TickStep.ReadCards, TickStep.FixDuplicates,
                TickStep.ReadCategories, TickStep.ReadWorkers, TickStep.ReadModels, TickStep.ReadRequests,
                TickStep.SaveRetries, TickStep.WriteStatus,
            ],
            _host.Calls);
        Assert.Equal([(1, "010-A", true)], _host.Starts);

        // The cards were read at "bbb": the next light wake with main unchanged reads nothing.
        _host.OnPlannerStep = null;
        _host.Calls.Clear();
        _tick.Run(TickWake.PlannerBatch);
        Assert.Equal(LightCalls, _host.Calls);
    }

    [Fact]
    public void MoveRequestWake_MainMovedSinceLastRead_FullTick()
    {
        FirstTick();
        _host.Cards.Add(TickHost.Card(CardFolder.Queue, 10, "A"));
        _host.Sha = "bbb";

        _tick.Run(TickWake.MoveRequest);

        Assert.Equal(FullCalls, _host.Calls);
        Assert.Equal([(1, "010-A", true)], _host.Starts);
    }

    [Fact]
    public void WorkerExitWake_MainUnchanged_StillDispatches()
    {
        var a = TickHost.Card(CardFolder.Active, 10, "A");
        _host.Cards.AddRange([a, TickHost.Card(CardFolder.Active, 20, "B"), TickHost.Card(CardFolder.Queue, 30, "C")]);
        _tick.Adopt(TickHost.Worker(1, 101, _clock.Now, a));
        _processes.Alive[101] = _clock.Now;
        _tick.Run();   // slot 2 takes the orphan 020-B; 030-C waits
        _host.Calls.Clear();
        _host.Starts.Clear();

        _processes.Alive.Remove(101);
        _host.Cards[0] = a with { Folder = CardFolder.Done };
        _tick.Run(TickWake.WorkerExit);

        Assert.Equal(FullCalls, _host.Calls);
        Assert.Equal(["010-A"], _host.Finished);
        Assert.Equal([(1, "030-C", true)], _host.Starts);
    }

    [Fact]
    public void LightWake_AWorkerFinished_FullTick()
    {
        var a = TickHost.Card(CardFolder.Active, 10, "A");
        _host.Cards.Add(a);
        Running(1, 101, a);
        FirstTick();

        _processes.Alive.Remove(101);
        _tick.Run(TickWake.MoveRequest);

        Assert.Equal(["010-A"], _host.Finished);
        Assert.Equal(FullCalls, _host.Calls);
    }

    [Theory]
    [InlineData(TickWake.Poll)]
    [InlineData(TickWake.Click)]
    public void PollAndClick_MainUnchanged_FullTick(TickWake wake)
    {
        FirstTick();
        _host.Cards.Add(TickHost.Card(CardFolder.Queue, 10, "A"));

        var steps = new List<TickStep>();
        _tick.StepStarting += steps.Add;
        _tick.Run(wake);

        Assert.Equal(FullCalls, _host.Calls);
        Assert.Equal(Enum.GetValues<TickStep>().Where(s => s != TickStep.Locks), steps);   // the lock step runs only with locks on (LockTickTests)
        Assert.Equal([(1, "010-A", true)], _host.Starts);
    }

    [Fact]
    public void Run_NoWakeGiven_IsThePoll()
    {
        FirstTick();
        _tick.Run();
        Assert.Equal(FullCalls, _host.Calls);
    }

    [Fact]
    public void LightWake_ShaNotKnown_FullTick()
    {
        _host.Sha = null;
        FirstTick();

        _tick.Run(TickWake.MoveRequest);

        Assert.Equal(FullCalls, _host.Calls);
    }

    [Fact]
    public void LightWake_FirstTick_FullTick()
    {
        _tick.Run(TickWake.HealthCheck);
        Assert.Equal(FullCalls, _host.Calls);
    }

    [Fact]
    public void LightWake_CardsCouldNotBeRead_NextWakeReadsThem()
    {
        // CARD-3: a failed read keeps the old list, and the sha it was meant for is not taken as read.
        _host.FailReadCards = true;
        FirstTick();
        _host.FailReadCards = false;

        _tick.Run(TickWake.MoveRequest);

        Assert.Equal(FullCalls, _host.Calls);
    }

    [Fact]
    public void LightWake_DuplicatesFixed_ReReadCountsAtTheNewSha()
    {
        // FixDuplicates commits to main, so the cards re-read after it are at the new sha, and a
        // light wake with main still there needs no read.
        _host.FixDuplicatesOnce = () => _host.Sha = "bbb";
        FirstTick();

        _tick.Run(TickWake.MoveRequest);

        Assert.Equal(LightCalls, _host.Calls);
    }
}
