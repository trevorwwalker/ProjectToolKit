using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Program;

public class RunnerTests
{
    private sealed class FakeListener(List<string> calls) : IPageListener
    {
        public int Port => 8123;

        public void Start() { }

        public void Stop() => calls.Add("listener stopped");

        public void Dispose() { }
    }

    private sealed class OrderLog(ListLog inner, List<string> calls) : ISchedulerLog
    {
        public void Write(string line)
        {
            inner.Write(line);
            calls.Add("log " + line.Split(' ')[0]);
        }
    }

    private sealed class Rig
    {
        public List<string> Calls { get; } = [];
        public ListLog Log { get; } = new();
        public int Ticks { get; private set; }
        public bool Draining { get; set; }
        public int Running { get; set; }
        public bool NoBrowser { get; set; }
        public Func<bool>? Shutdown { get; set; }
        public Action<int>? OnTick { get; set; }
        public SchedulerConfig Config { get; }

        public Rig(bool dryRun) =>
            Config = new SchedulerConfig { Project = "test-" + Guid.NewGuid().ToString("N")[..10], DryRun = dryRun };

        public RunnerParts Parts(Action? tick = null) => new()
        {
            Config = Config,
            Clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero)),
            Log = new OrderLog(Log, Calls),
            Version = "1.2.3",
            MutexWait = TimeSpan.FromSeconds(1),
            AdoptWorkers = () => Calls.Add("adopt"),
            SeedFromAccounting = () => Calls.Add("seed"),
            StartListener = () =>
            {
                Calls.Add("listen");
                return new FakeListener(Calls);
            },
            OpenBrowser = url => Calls.Add("browser " + url),
            NoBrowser = NoBrowser,
            ShutdownRequested = Shutdown,
            Tick = tick is { } run ? _ => run() : _ =>
            {
                Ticks++;
                OnTick?.Invoke(Ticks);
            },
            Draining = () => Draining,
            WorkersRunning = () => Running,
            WriteStatusFile = () => Calls.Add("status file"),
            WaitForTick = (_, _) => true,
        };
    }

    // CFG-15: adopt workers, seed recent events and stats, start the listener, log the start line, open
    // the browser once; in a dry run the browser is not opened.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Config_StartupOrder_AdoptSeedListenLog(bool dryRun)
    {
        var rig = new Rig(dryRun);
        using var stopped = new CancellationTokenSource();
        stopped.Cancel(); // start and stop before the first tick

        new SchedulerRunner(rig.Parts()).Run(stopped.Token);

        var expected = new List<string> { "adopt", "seed", "listen", "log scheduler-next" };
        if (!dryRun)
        {
            expected.Add("browser http://localhost:8123/");
        }

        expected.AddRange(["listener stopped", "status file"]);
        Assert.Equal(expected, rig.Calls);
        Assert.Contains("1.2.3", rig.Log.Lines[0]);
        Assert.Equal(0, rig.Ticks);
    }

    // CFG-15 (670-B): -NoBrowser, which is how the tray starts it - everything else about the start is
    // the same, only the browser is left alone.
    [Fact]
    public void Config_NoBrowser_PageIsNotOpenedAtStart()
    {
        var rig = new Rig(dryRun: false) { NoBrowser = true };
        using var stopped = new CancellationTokenSource();
        stopped.Cancel();

        new SchedulerRunner(rig.Parts()).Run(stopped.Token);

        Assert.DoesNotContain(rig.Calls, c => c.StartsWith("browser", StringComparison.Ordinal));
        Assert.Contains("log scheduler-next", rig.Calls);
        Assert.Contains("listener stopped", rig.Calls);
    }

    // PAGE-12: draining with one worker running; it finishes. The loop ends after that tick, not before;
    // the listener stops, the status file is written, the mutex is released.
    [Fact]
    public void Page_Exit_DrainingWithNoWorkers()
    {
        var rig = new Rig(dryRun: false) { Draining = true, Running = 1 };
        rig.OnTick = n =>
        {
            if (n == 2)
            {
                rig.Running = 0; // the worker finished
            }
        };

        new SchedulerRunner(rig.Parts()).Run();

        Assert.Equal(2, rig.Ticks);
        Assert.Contains("listener stopped", rig.Calls);
        Assert.Equal("status file", rig.Calls[^1]);
        Assert.Contains(rig.Log.Lines, l => l.StartsWith("drained", StringComparison.Ordinal));
        using var second = SingleInstance.Acquire(rig.Config.Project, TimeSpan.Zero); // free at once
    }

    // 670-B: the tray's Restart and "Exit, leave workers running" ask the scheduler to end by itself
    // through its shutdown event, rather than send a Ctrl+C that the workers would hear too. The loop ends
    // the same clean way PAGE-12 does: listener stopped, status file written, mutex free again.
    [Fact]
    public void Tray_ShutdownEvent_EndsWithoutTouchingWorkers()
    {
        var rig = new Rig(dryRun: false) { Running = 1 };
        rig.Shutdown = () => rig.Ticks >= 2;

        new SchedulerRunner(rig.Parts()).Run();

        Assert.Equal(2, rig.Ticks);
        Assert.Contains("listener stopped", rig.Calls);
        Assert.Equal("status file", rig.Calls[^1]);
        Assert.Contains(rig.Log.Lines, l => l.StartsWith("shutdown asked for", StringComparison.Ordinal));
        using var second = SingleInstance.Acquire(rig.Config.Project, TimeSpan.Zero); // free at once
    }

    // DISP-21: dry run with one ready card: "DRY RUN slot 1 <- 010-A-x", nothing started, one tick.
    [Fact]
    public void Dispatch_DryRun_LogsAndExitsAfterFirstTick()
    {
        var rig = new Rig(dryRun: true);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var inner = new TickHost { Cards = { TickHost.Card(CardFolder.Queue, 10, "A") } };
        var host = new DryRunTickHost(inner, rig.Log);
        var tick = new SchedulerTick(
            rig.Config, clock, rig.Log, new TickLoad(), new TickProbe(), new TickProcesses(), new TickFiles(), host);
        var ticks = 0;
        var parts = rig.Parts(() =>
        {
            ticks++;
            tick.Run();
        });

        new SchedulerRunner(parts).Run();

        Assert.Equal(1, ticks);
        Assert.Contains("DRY RUN slot 1 <- 010-A-x", rig.Log.Lines);
        Assert.Empty(inner.Starts);
        Assert.Empty(tick.Running);
        Assert.DoesNotContain(rig.Calls, c => c.StartsWith("browser", StringComparison.Ordinal));
    }
}
