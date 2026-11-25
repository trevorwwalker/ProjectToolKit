// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Core.Contracts;
using Scheduler.Host.Processes;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// 966-B: the loop reacts at once - an exit ends a wait or a long tick at its next safe point, events
/// wake it without waiting for the poll, fast mode shortens its wait, and each tick's cost is logged.
/// </summary>
public class RunnerWakeTests
{
    private sealed class NoListener : IPageListener
    {
        public int Port => 1;

        public void Start() { }

        public void Stop() { }

        public void Dispose() { }
    }

    private sealed class Rig
    {
        public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        public ListLog Log { get; } = new();
        public SchedulerConfig Config { get; } = new() { Project = "test-" + Guid.NewGuid().ToString("N")[..10], PollSeconds = 15 };
        public volatile bool Shutdown;
        public int Ticks;
        public int Wakes;

        public RunnerParts Parts(Action tick, Func<TimeSpan, CancellationToken, bool> wait, Func<bool>? fast = null, Action? wake = null,
            Func<(long Git, double ModelMs)>? counters = null) => new()
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
            ShutdownRequested = () => Shutdown,
            Tick = _ =>
            {
                Ticks++;
                tick();
            },
            Draining = () => false,
            WorkersRunning = () => 0,
            WriteStatusFile = () => { },
            WaitForTick = wait,
            Fast = fast,
            Wake = wake ?? (() => Wakes++),
            Counters = counters,
        };
    }

    [Fact]
    public void Exit_DuringALongTick_EndsAtTheNextStepNotItsLast()
    {
        var rig = new Rig();
        var steps = new List<string>();
        SchedulerRunner? runner = null;
        runner = new SchedulerRunner(rig.Parts(
            () =>
            {
                foreach (var step in new[] { "SampleHost", "ReadCards", "Dispatch" })
                {
                    runner!.StepStarting(step);
                    steps.Add(step);
                    rig.Shutdown = true; // the tray's Exit, while the tick is in its first step
                }
            },
            (_, _) => false));

        runner.Run();

        Assert.Equal(new[] { "SampleHost" }, steps);
        Assert.Equal(1, rig.Ticks);
        Assert.Contains(rig.Log.Lines, l => l.Contains("stopped before its step ReadCards", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Lines, l => l.StartsWith("shutdown asked for", StringComparison.Ordinal));
    }

    [Fact]
    public void Exit_DuringTheWait_EndsWellWithinASecond()
    {
        var rig = new Rig();
        using var exits = new WorkerExitWatcher();
        var runner = new SchedulerRunner(rig.Parts(() => { }, exits.WaitForTick, wake: exits.Wake));
        var thread = new Thread(() => runner.Run());
        thread.Start();
        SpinWait.SpinUntil(() => Volatile.Read(ref rig.Ticks) >= 1, TimeSpan.FromSeconds(5));
        Thread.Sleep(50); // into the wait

        var clock = Stopwatch.StartNew();
        rig.Shutdown = true;
        exits.Wake(); // as the tray's shutdown event does

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        // 3 s, not 500 ms: a busy PC took 588 ms (1275-B); a broken wake takes up to the 15 s poll.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed.TotalMilliseconds} ms");
    }

    [Fact]
    public void Event_RequestTick_WakesATickWithoutWaitingForThePoll()
    {
        var rig = new Rig();
        SchedulerRunner? runner = null;
        var waits = 0;
        runner = new SchedulerRunner(rig.Parts(() => { }, (_, _) =>
        {
            waits++;
            if (waits == 1)
            {
                runner!.RequestTick(); // a page action, one second into a fifteen-second poll
            }
            else
            {
                rig.Shutdown = true;
            }

            rig.Clock.Advance(TimeSpan.FromSeconds(1));
            return false;
        }));

        runner.Run();

        Assert.Equal(2, rig.Ticks);
        Assert.Equal(1, rig.Wakes);
    }

    [Fact]
    public void Event_Signalled_WakesATickWithoutWaitingForThePoll()
    {
        var rig = new Rig();
        var waits = 0;
        var runner = new SchedulerRunner(rig.Parts(() => { }, (_, _) =>
        {
            waits++;
            rig.Shutdown = waits == 2;
            rig.Clock.Advance(TimeSpan.FromMilliseconds(100));
            return waits == 1; // a worker exit, a move request, a planner batch, a health check's end
        }));

        runner.Run();

        Assert.Equal(2, rig.Ticks);
    }

    [Fact]
    public void Idle_NoEvents_TicksEveryPollSeconds()
    {
        var rig = new Rig();
        var waited = new List<TimeSpan>();
        var runner = new SchedulerRunner(rig.Parts(() => { }, (step, _) =>
        {
            waited.Add(step);
            rig.Clock.Advance(step);
            rig.Shutdown = waited.Count >= 61; // one minute and a second
            return false;
        }));

        runner.Run();

        Assert.Equal(5, rig.Ticks); // at 0, 15, 30, 45 and 60 s
        Assert.All(waited, w => Assert.Equal(SchedulerRunner.Step, w));
    }

    [Fact]
    public void Fast_PageInFastMode_LoopWaits33msThenBack()
    {
        var rig = new Rig();
        var fast = true;
        var waited = new List<TimeSpan>();
        var runner = new SchedulerRunner(rig.Parts(() => { }, (step, _) =>
        {
            waited.Add(step);
            fast = waited.Count < 2;
            rig.Shutdown = waited.Count >= 4;
            return false;
        }, fast: () => fast));

        runner.Run();

        Assert.Equal(TimeSpan.FromMilliseconds(33), SchedulerRunner.FastStep);
        Assert.Equal(new[] { SchedulerRunner.FastStep, SchedulerRunner.FastStep, SchedulerRunner.Step, SchedulerRunner.Step }, waited);
        Assert.Equal(1, rig.Ticks); // a fast wait is light: no tick unless one is due or asked for
    }

    [Fact]
    public void Cost_SlowTick_LoggedWithItsSlowestStep()
    {
        var rig = new Rig();
        SchedulerRunner? runner = null;
        runner = new SchedulerRunner(rig.Parts(
            () =>
            {
                runner!.StepStarting("SampleHost");
                rig.Clock.Advance(TimeSpan.FromMilliseconds(500));
                runner.StepStarting("ReadCards");
                rig.Clock.Advance(TimeSpan.FromSeconds(2));
                runner.StepStarting("Dispatch");
                rig.Clock.Advance(TimeSpan.FromMilliseconds(100));
            },
            (_, _) =>
            {
                rig.Shutdown = true;
                return false;
            }));

        runner.Run();

        Assert.Contains(rig.Log.Lines, l => l.StartsWith("tick took 2.60s", StringComparison.Ordinal)
            && l.Contains("slowest step ReadCards 2.00s", StringComparison.Ordinal));
    }

    // MM-18 (1063-B): the line says what the last tick cost in git processes and in the model's advance; and the
    // page's data refresh after the tick's last step is a step of its own, not WriteStatus's.
    [Fact]
    public void Cost_LineSaysTheTicksGitProcessesAndModelTime()
    {
        var rig = new Rig();
        long git = 40;
        var model = 7.0;
        SchedulerRunner? runner = null;
        runner = new SchedulerRunner(rig.Parts(
            () =>
            {
                runner!.StepStarting("WriteStatus");
                rig.Clock.Advance(TimeSpan.FromMilliseconds(100));
                runner.StepStarting("PageRefresh");
                rig.Clock.Advance(TimeSpan.FromSeconds(3));
                git += 2;
                model += 1.25;
            },
            (_, _) =>
            {
                rig.Shutdown = true;
                return false;
            },
            counters: () => (git, model)));

        runner.Run();

        Assert.Contains(rig.Log.Lines, l => l.StartsWith("tick took 3.10s", StringComparison.Ordinal)
            && l.Contains("slowest step PageRefresh 3.00s", StringComparison.Ordinal)
            && l.Contains("; git 2, model 1.3ms", StringComparison.Ordinal));
    }

    [Fact]
    public void Cost_QuickTicks_LoggedOnceAMinute()
    {
        var rig = new Rig();
        var waits = 0;
        var runner = new SchedulerRunner(rig.Parts(() => { }, (_, _) =>
        {
            waits++;
            rig.Clock.Advance(TimeSpan.FromSeconds(15));
            rig.Shutdown = waits >= 9; // two minutes of quick ticks
            return false;
        }));

        runner.Run();

        var lines = rig.Log.Lines.Where(l => l.StartsWith("tick took", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, lines.Count); // the first, then one a minute
        Assert.Contains("4 tick(s) since the last such line", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Watcher_Wake_EndsTheWaitAtOnce()
    {
        using var exits = new WorkerExitWatcher();
        exits.Wake();

        var clock = Stopwatch.StartNew();
        Assert.True(exits.WaitForTick(TimeSpan.FromSeconds(5)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Folder_MoveRequestRenamedIn_Wakes()
    {
        var folder = Scheduler.TestSupport.TestTmp.NewPath("w966");
        try
        {
            using var woke = new ManualResetEventSlim();
            using var watch = new FolderWake(folder, woke.Set, create: true);
            Assert.True(watch.Watching);

            var tmp = Path.Combine(folder, "20261119-233335-000-slot1.tmp");
            File.WriteAllText(tmp, "Card: 1-B-x");
            woke.Reset();
            File.Move(tmp, Path.ChangeExtension(tmp, ".move"));

            Assert.True(woke.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Folder_MissingOutbox_NotCreatedAndNotWatched()
    {
        var folder = Scheduler.TestSupport.TestTmp.NewPath("w966");

        using var watch = new FolderWake(folder, () => { }, create: false);

        Assert.False(watch.Watching);
        Assert.False(Directory.Exists(folder));
    }
}
