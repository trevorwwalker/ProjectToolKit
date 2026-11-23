using System.Diagnostics;
using System.Security.Cryptography;
using Scheduler.Core.Contracts;
using Scheduler.Host.Observe;
using Scheduler.Host.Processes;
using Scheduler.Host.Program;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.Tick;

namespace Scheduler.Host.Tests.Observe;

/// <summary>
/// The observe run on a temporary repository with a bare remote (card 390-B, roadmap item 1): it
/// keeps ticking and logging what it would do, mirrors the old scheduler's workers, and nothing in
/// the repository, the remote, the slots or the old scheduler's log folder changes (its fetch, the
/// same as the old scheduler's, may only add what the remote already has).
/// </summary>
[ParallelMethods]
public sealed class ObserveRunTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly GitFixture _fx = new();
    private readonly string _oldLogs;
    private readonly ListLog _log = new();
    private readonly HostComposition _composition;
    private readonly SchedulerConfig _config;
    private Process? _worker;

    public ObserveRunTests()
    {
        _fx.Push("seed", new Dictionary<string, string?> { [$"jobs/queue/{TickRig.CardName}.md"] = TickRig.CardText });
        GitFixture.Git(_fx.Repo, "pull", "-q", "origin", "main");
        _oldLogs = Path.Combine(_temp.Path, "logs");
        Directory.CreateDirectory(_oldLogs);
        var old = new StateFiles(_oldLogs);
        RetriesFile.Save(old, new Dictionary<string, int> { ["100-A"] = 2 });
        File.WriteAllText(Path.Combine(_oldLogs, "scheduler.log"), "[2026-11-18 10:00:00] old\r\n");
        var config = new SchedulerConfigBuilder
        {
            Repo = _fx.Repo,
            Project = "Obs" + Guid.NewGuid().ToString("N")[..8],
            WorkspaceRoot = Path.Combine(_temp.Path, "ws"),
            LogDir = _oldLogs,
            WorkerCli = Path.Combine(_temp.Path, "no-agent.cmd"),
            Port = TickRig.FreePort(),
        }.Build();
        var (observed, settings) = ObserveSettings.Apply(config, portGiven: true);
        _config = observed;
        _composition = HostComposition.Create(observed, "{}", new SkewClock(), _log, _ => { }, TimeSpan.FromSeconds(5), settings);
    }

    public void Dispose()
    {
        try { _worker?.Kill(true); } catch (InvalidOperationException) { }
        _worker?.Dispose();
        _composition.Dispose();
        _fx.Dispose();
        _temp.Dispose();
    }

    private ObserveTicker Observer => _composition.Observer ?? throw new InvalidOperationException("not an observe run");

    private string Snapshot() => string.Join("\n",
        GitFixture.Git(_fx.Bare, "rev-parse", "main"),
        GitFixture.Git(_fx.Repo, "rev-parse", "HEAD"),
        GitFixture.Git(_fx.Repo, "status", "--porcelain"),
        GitFixture.Git(_fx.Repo, "worktree", "list", "--porcelain"),
        GitFixture.Git(_fx.Repo, "branch"),
        GitFixture.Git(_fx.Repo, "rev-parse", "origin/main"),
        Directory.Exists(_config.WorkspaceRoot) ? "slots exist" : "no slots",
        Directory.Exists(Path.Combine(_temp.Path, "ws")) ? "old slots exist" : "no old slots",
        string.Join("\n", Directory.GetFiles(_oldLogs).Order(StringComparer.Ordinal)
            .Select(f => Path.GetFileName(f) + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))))));

    /// <summary>True when an observe line holding <paramref name="fragment"/> has been logged.</summary>
    private bool ObserveLogged(string fragment) => _log.Lines.Any(
        l => l.StartsWith(ObserveLine.Mark, StringComparison.Ordinal) && l.Contains(fragment, StringComparison.Ordinal));

    private string Tail() => string.Join(" | ", _log.Lines.TakeLast(8));

    [Fact]
    public void It_keeps_ticking_logs_what_it_would_start_and_changes_nothing()
    {
        // DISP-21 (logged instead of started; observe does not stop after one tick), CFG-14, roadmap item 1
        var before = Snapshot();

        for (var i = 0; i < 3; i++)
        {
            Observer.Run();
        }

        var observeLines = _log.Lines.Where(l => l.StartsWith(ObserveLine.Mark, StringComparison.Ordinal)).ToList();
        Assert.Equal(3, observeLines.Count(l => l.EndsWith($" slot 1 <- {TickRig.CardName}", StringComparison.Ordinal)));
        Assert.Contains(observeLines, l => l.Contains(" tick 3: state running, running none", StringComparison.Ordinal));
        Assert.Empty(_composition.Tick.Running);
        Assert.Equal(2, _composition.Tick.Retries["100-A"]);
        Assert.Equal(before, Snapshot());
        Assert.True(File.Exists(Path.Combine(_oldLogs, "observe", "status.txt")));
    }

    [Fact]
    public void The_program_loop_keeps_ticking_under_its_own_instance_and_port()
    {
        // DISP-21 (dry run stops after the first tick; observe does not), CFG-14 (its own instance), roadmap item 1
        var before = Snapshot();
        using var oldInstance = new Mutex(false, SingleInstance.MutexName(_config.Project[..^ObserveSettings.ProjectSuffix.Length]));
        Assert.True(oldInstance.WaitOne(0));

        // 934-B: the loop paces itself on the real clock - each turn waits a second in
        // WorkerExitWatcher.WaitForTick (SchedulerRunner.Step) and every tick runs real git - so the
        // run is driven by what it logs, not by a budget in seconds. The 4 s this used to allow had
        // to cover three loop turns with nothing in the test controlling that pacing, so a loaded PC
        // could lose it and tick 2 never be logged. 60 s is only a hang guard: the test ends as soon
        // as the second tick is there.
        using var stop = new CancellationTokenSource();
        Exception? startFailure = null;
        var run = Task.Run(() =>
        {
            try
            {
                _composition.Runner.Run(stop.Token);
            }
            catch (OperationCanceledException)
            {
                // The runner's wait ends a cancelled run this way.
            }
            catch (Exception e)
            {
                startFailure = e; // a start failure (mutex, port) fails the assertions below with its message
            }
        });

        var sinceStart = Stopwatch.StartNew();
        var until = DateTime.UtcNow.AddMinutes(10); // 1051-B: waits for tick 2; the cap only turns a hang into a failure
        while (DateTime.UtcNow < until && !run.IsCompleted && !ObserveLogged(" tick 2: "))
        {
            Thread.Sleep(50);
        }

        var tickedAfter = sinceStart.ElapsedMilliseconds;
        stop.Cancel();
        var ended = DateTime.UtcNow.AddMinutes(5); // 1051-B
        while (DateTime.UtcNow < ended && !run.IsCompleted)
        {
            Thread.Sleep(50);
        }

        // The loop's own thread still has to stop the page listener and write the status file before
        // the snapshot below can be trusted, so it is waited for, not abandoned.
        Assert.True(run.IsCompleted, "the loop did not end within 5 min of the cancel");
        oldInstance.ReleaseMutex();

        Assert.True(startFailure is null, $"the loop did not start: {startFailure?.Message}\nLOG: {Tail()}");
        Assert.True(ObserveLogged(" tick 2: "), $"tick 2 was not logged within {tickedAfter} ms\nLOG: {Tail()}");
        Assert.Contains(_log.Lines, l => l.Contains($"page http://localhost:{_config.Port}/", StringComparison.Ordinal));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void The_old_schedulers_workers_are_mirrored_and_their_end_is_logged()
    {
        // ADOPT-1 (running.json adoption), TIME-13 (alive by pid and start time), TIME-20, roadmap item 1
        _worker = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3600 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        var running = new RunningWorker { Slot = 1, Pid = _worker.Id, Start = _worker.StartTime, Card = TickRig.CardName, Category = "B", Id = "010-B" };
        new StateFiles(_oldLogs).Write(StateFile.Running, RunningJson.Write([running]));
        var before = Snapshot();

        Observer.Run();
        Observer.Run();
        _worker.Kill(true);
        _worker.WaitForExit(10000);
        Observer.Run();

        var observeLines = _log.Lines.Where(l => l.StartsWith(ObserveLine.Mark, StringComparison.Ordinal)).ToList();
        Assert.Single(observeLines, l => l.EndsWith($" mirror slot 1: {TickRig.CardName} (pid {_worker.Id})", StringComparison.Ordinal));
        var finished = observeLines.FindIndex(l => l.EndsWith($" slot 1 finished: {TickRig.CardName}", StringComparison.Ordinal));
        Assert.True(finished > 0);
        Assert.DoesNotContain(observeLines.Take(finished), l => l.Contains(" <- ", StringComparison.Ordinal));
        Assert.Equal(before, Snapshot());
    }
}
