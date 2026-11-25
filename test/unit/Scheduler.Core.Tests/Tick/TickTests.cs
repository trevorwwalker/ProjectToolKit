// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;

namespace Scheduler.Core.Tests.Tick;

public sealed class TickTests
{
    private readonly TickClock _clock = new();
    private readonly TickProcesses _processes = new();
    private readonly TickHost _host;
    private readonly SchedulerTick _tick;

    public TickTests()
    {
        _host = new TickHost(_processes, _clock);
        _tick = new SchedulerTick(
            new SchedulerConfig(), _clock, new TickLog(), new TickLoad(), new TickProbe(), _processes, new TickFiles(), _host);
    }

    private RunningWorker Running(int slot, int pid, Card card)
    {
        var w = TickHost.Worker(slot, pid, _clock.Now, card);
        _processes.Alive[pid] = w.Start;
        _tick.Adopt(w);
        return w;
    }

    [Fact]
    public void Dispatch_AllWorkersBusy_ThenOneFinishes_StartsNextTick()
    {
        // DISP-15, TIME-21: both slots busy (categories A and B), 030-C waits; slot 2's worker
        // finishes, and the next tick starts 030-C on slot 2.
        var a = TickHost.Card(CardFolder.Active, 10, "A");
        var b = TickHost.Card(CardFolder.Active, 20, "B");
        _host.Cards.AddRange([a, b, TickHost.Card(CardFolder.Queue, 30, "C")]);
        Running(2, 102, b);      // adopted out of slot order: the note still lists the cards by slot
        Running(1, 101, a);

        _tick.Run();
        Assert.Empty(_host.Starts);
        // WT-12 replaces DISP-19's note: the type that fits says it is full, and names the cards it holds.
        Assert.Contains(_tick.LastDispatch!.Notes, n => n.Card == "030-C-x" && n.Note == "next up; waiting for worker (2 of 2 running: 010-A, 020-B)");

        // Slot 2's worker exits; its card has gone to done/.
        _processes.Alive.Remove(102);
        _host.Cards[1] = b with { Folder = CardFolder.Done };
        _clock.Now = _clock.Now.AddSeconds(1);

        _tick.Run();
        Assert.Equal(["020-B"], _host.Finished);
        Assert.Equal([(2, "030-C", true)], _host.Starts);
        Assert.Equal("030-C", _tick.Running[2].Id);
        Assert.Equal("010-A", _tick.Running[1].Id);
    }

    // 1082-A: a worker that ends leaves its card in active/ with Status: ready; the tick that reaps it
    // resumes the card (the watchdog's "its card stays in active/ and will be resumed").
    [Fact]
    public void Tick_WorkerEnds_CardStillActiveReady_Resumed()
    {
        var a = TickHost.Card(CardFolder.Active, 10, "A");
        _host.Cards.Add(a);
        Running(1, 101, a);

        _tick.Run();
        Assert.Empty(_host.Starts);

        _processes.Alive.Remove(101);
        _clock.Now = _clock.Now.AddSeconds(1);
        _tick.Run();

        Assert.Equal(["010-A"], _host.Finished);
        Assert.Equal([(1, "010-A", true)], _host.Starts);
        Assert.True(_tick.Running[1].Resume);
        Assert.Equal(1, _tick.Retries["010-A"]);
    }

    // 1082-A: an orphan that cannot start for want of a free worker is not silent: the Queue says why.
    [Fact]
    public void Tick_OrphanWithNoFreeWorker_QueueNoteSaysWhy()
    {
        var a = TickHost.Card(CardFolder.Active, 10, "A");
        var b = TickHost.Card(CardFolder.Active, 20, "B");
        _host.Cards.AddRange([a, b, TickHost.Card(CardFolder.Active, 30, "C")]);
        Running(1, 101, a);
        Running(2, 102, b);

        _tick.Run();

        Assert.Empty(_host.Starts);
        Assert.Contains(_tick.LastDispatch!.Notes, n => n.Card == "030-C-x"
            && n.Note == "orphan to resume; next up; waiting for worker (2 of 2 running: 010-A, 020-B)");
    }

    // 1082-A (DISP-6, DISP-9): a stale, higher-priority queue/ copy of the orphan does not hold it back.
    [Fact]
    public void Tick_OrphanWithStaleHigherPriorityQueueCopy_Resumed()
    {
        _host.Cards.Add(TickHost.Card(CardFolder.Active, 10, "A") with { Prio = 2 });
        _host.Cards.Add(TickHost.Card(CardFolder.Queue, 10, "A") with { Prio = 1 });

        _tick.Run();

        Assert.Equal([(1, "010-A", true)], _host.Starts);
        Assert.True(_tick.Running[1].Resume);
    }

    [Fact]
    public void Dispatch_StartWorkerFails_CardNotRestartedSameTick()
    {
        // DISP-11, DISP-15: Start-Worker fails for 010-A with two free slots; it is not tried on
        // the second slot in the same tick, and the slots stay idle.
        _host.Cards.Add(TickHost.Card(CardFolder.Queue, 10, "A"));
        _host.FailToStart.Add("010-A");

        _tick.Run();

        Assert.Equal([(1, "010-A", false)], _host.Starts);
        Assert.Empty(_tick.Running);
    }

    // WT-6, WT-C4, 959-A: the type WT-5 chose goes with the start, so what the host starts is that
    // type's worker - not the settings of the folder it happens to sit in. A queued card that asks for
    // the premium Claude type by name and a resumed orphan of no worker line at all.
    [Fact]
    public void Dispatch_TypeChosen_StartCarriesThatTypeToTheHost()
    {
        _host.Workers = """
            {"pool":{"maxWorkers":3,"types":{
              "default":{"max":3,"areas":"AB"},
              "Opus":{"max":3,"agent":"claude","model":"claude-opus-5-5","premium":true}}}}
            """;
        _host.Cards.Add(TickHost.Card(CardFolder.Queue, 10, "A") with { WorkerReq = "Opus" });
        _host.Cards.Add(TickHost.Card(CardFolder.Active, 20, "B"));

        _tick.Run();

        // The orphan goes first (DISP-14) and takes folder 1; the premium card takes folder 2, and each
        // start names the type its worker runs as.
        Assert.Equal([(1, "020-B", "default"), (2, "010-A", "Opus")], _host.TypeStarts);
        Assert.Equal("default", _tick.Running[1].Type);
        Assert.Equal("Opus", _tick.Running[2].Type);
    }

    [Fact]
    public void Tick_StepsInSpecOrder()    {
        // A3 (Tick L2795): sample host, sample model servers, reap health check, reap/watch
        // workers, index logs, read cards, fix duplicates, planner step, read categories, workers,
        // models, requests, plan, start health check if wanted, dispatch, save retries, write
        // status files.
        var steps = new List<TickStep>();
        _tick.StepStarting += steps.Add;

        _tick.Run();

        Assert.Equal(Enum.GetValues<TickStep>().Where(s => s != TickStep.Locks), steps);   // the lock step runs only with locks on (LockTickTests)
        Assert.Equal(
            [
                TickStep.IndexLogs, TickStep.ReadCards, TickStep.FixDuplicates, TickStep.PlannerStep,
                TickStep.ReadCategories, TickStep.ReadWorkers, TickStep.ReadModels, TickStep.ReadRequests,
                TickStep.SaveRetries, TickStep.WriteStatus,
            ],
            _host.Calls);
    }

    [Fact]
    public void TypeDispatch_QuotaResult_MarksAgent_StateStaysRunning_OtherTypeRuns()
    {
        // WT-9, TIME-15: the out-of-quota run's agent is marked and the state stays running, so a
        // card for the default agent's type still starts in the same tick.
        var a = TickHost.Card(CardFolder.Active, 10, "A");
        var w = Running(1, 101, a) with { Agent = "claude" };
        _tick.Adopt(w);
        _processes.Alive.Remove(101);
        _host.FinishState = SchedulerState.PausedQuota;
        _host.Cards.AddRange([a with { Folder = CardFolder.Done }, TickHost.Card(CardFolder.Queue, 20, "B")]);

        _tick.Run();

        Assert.Equal(SchedulerState.Running, _tick.State);
        Assert.True(_tick.Quota.IsOut("claude"));
        Assert.Equal([(1, "020-B", true)], _host.Starts);
        Assert.Equal("worker", _tick.Running[1].Type);
    }

    [Fact]
    public void Adoption_FolderAboveMaxWorkers_KeptAndCounted()
    {
        // WT-W5, WT-1: a worker adopted on folder 4 with maxWorkers 2 keeps its number and counts in
        // the total, so only one more worker starts, not two.
        var adopted = TickHost.Card(CardFolder.Active, 10, "A");
        Running(4, 104, adopted);
        _host.Cards.AddRange([adopted, TickHost.Card(CardFolder.Queue, 20, "B"), TickHost.Card(CardFolder.Queue, 30, "C")]);

        _tick.Run();

        Assert.Equal([(1, "020-B", true)], _host.Starts);
        Assert.Contains(4, _tick.Running.Keys);
        Assert.Equal("", _tick.Running[4].Type);   // no type was migrated from slot 4: it counts toward the total only
        Assert.Contains(_tick.LastDispatch!.Notes, n => n.Card == "030-C-x" && n.Note == "next up; waiting for a free worker (2 of 2 running)");
    }
}
