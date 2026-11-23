using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Health;

namespace Scheduler.Core.Tests.Health;

/// <summary>
/// A check that turns red in the middle of a run: the workers already running are left alone and
/// nothing new starts (HLTH-5, DISP-14; schedule.ps1 <c>Reap-Health</c> L1738-1742, <c>Tick</c>
/// L2918-2923).
/// Test plan: scheduler-next/test-plans/health.md.
/// </summary>
public class HealthMidRunTests
{
    private const string Jobs = "docs/jobs";
    private const string Sha = "abc1234";
    private const string Command = "dotnet build scheduler-next\\SchedulerNext.slnx";
    private const int MaxRetries = 2;
    private const string NextUp = "next up; dispatching is paused-red";

    private static Card Card(string folderAndName, string text = "Status: ready") =>
        CardFiles.Parse(Jobs, $"{Jobs}/{folderAndName}.md", text)
        ?? throw new InvalidOperationException($"not a card path: {folderAndName}");

    private static RunningWorker Worker(int slot, string name, string category) => new()
    {
        Slot = slot,
        Pid = 100 + slot,
        Start = DateTimeOffset.UnixEpoch,
        Card = name,
        Category = category,
        Id = $"{name[..3]}-{category}",
    };

    /// <summary>HLTH-5, DISP-14: red pauses new starts, a running worker keeps going, green starts it.</summary>
    [Fact]
    public void Health_RedMidRun_RunningWorkersContinueNoNewStarts()
    {
        // HLTH-5, WT-1: 010-A runs as one worker of the type, 020-B is ready in queue/ and the type
        // has room for a second. The health check finishes RED: 010-A keeps running (L1738-1742
        // never touch running workers), 020-B is not started and says why, and the state stays
        // paused-red until main is green.
        var runner = new FakeHealthRunner(Sha);
        var clock = new FakeClock();
        var log = new FakeLog();
        var health = new HealthCheck(new SchedulerConfig { HealthCommand = Command }, clock, log, runner);
        var servers = new ServerCache(new FakeProbe(), clock, log);
        var retries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var started = new List<string>();

        var cards = new[] { Card("queue/020-B-second") };
        var running = new List<RunningWorker> { Worker(1, "010-A-first", "A") };

        // One type that may run two workers (WT-2), as the tick gives it: the running count is
        // what the running list holds, so a finished worker frees a place.
        TypeDispatchResult Tick(SchedulerState state) => TypeDispatcher.Dispatch(
            DispatchPlanner.Plan(cards, running, retries, null, MaxRetries),
            state, maxWorkers: 2, totalRunning: running.Count,
            [new DispatchType { Name = "worker", Max = 2, Running = running.Count }],
            migrated: false, new AgentQuota(), servers, retries, Start);

        bool Start(TypeStart attempt)
        {
            started.Add(attempt.Candidate.Card.Id);
            return true;
        }

        // A check started earlier this run is the one that turns red.
        health.RunFinished();
        Assert.True(health.StartIfWanted());
        runner.FinishWith(1);
        var state = health.Reap(SchedulerState.Running);
        Assert.Equal(SchedulerState.PausedRed, state);
        Assert.Equal(0, runner.Kills);

        // The next tick: 010-A is still running, and 020-B is not started.
        var result = Tick(state);
        Assert.Empty(result.Starts);
        Assert.Empty(started);
        Assert.Single(running);
        Assert.Contains(result.Notes, n => n.Card == "020-B-second" && n.Note == NextUp);

        // The worker finishes its card, so health is wanted again (HLTH-1).
        running.Clear();
        health.RunFinished();
        Assert.True(health.Wanted);

        // main has not moved, so the check is not re-run (HLTH-2) and the red pause stays.
        Assert.False(health.StartIfWanted());
        Assert.False(health.Wanted);
        result = Tick(state);
        Assert.Empty(result.Starts);
        Assert.Empty(started);
        Assert.Contains(result.Notes, n => n.Note == NextUp);

        // A new commit lands. Nothing wants a check on its own (spec C7, flagged in
        // Health_NoStartupCheck_OthersPushesUnchecked), so the page's Check main is what asks for
        // it; it runs against the new sha and is green (HLTH-1, HLTH-5).
        runner.Sha = "9f3c1ab";
        Assert.False(health.StartIfWanted());
        health.CheckNow();
        Assert.True(health.StartIfWanted());
        Assert.Equal("9f3c1ab", runner.Starts[^1].Sha);
        runner.FinishWith(0);
        state = health.Reap(state);
        Assert.Equal(SchedulerState.Running, state);
        Assert.Equal(HealthResult.Green, health.LastResult);

        // Now the free place takes 020-B.
        result = Tick(state);
        Assert.Equal(["020-B"], started);
        Assert.Single(result.Starts);
    }
}
