// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Health;

namespace Scheduler.Core.Tests.Health;

/// <summary>
/// What green and red do to the scheduler's state (HLTH-5; schedule.ps1 <c>Reap-Health</c>
/// L1733-1743), and what the page's resume does to a red main (MOVE-8, L3855).
/// Test plan: scheduler-next/test-plans/health.md.
/// </summary>
public class HealthStateTests
{
    private const string Sha = "abc1234";
    private const string Command = "dotnet build scheduler-next\\SchedulerNext.slnx";

    private static (HealthCheck Health, FakeHealthRunner Runner, FakeLog Log) RunningCheck()
    {
        var runner = new FakeHealthRunner(Sha);
        var log = new FakeLog();
        var health = new HealthCheck(
            new SchedulerConfig { HealthCommand = Command, Project = "Proj" }, new FakeClock(), log, runner);
        health.RunFinished();
        Assert.True(health.StartIfWanted());
        return (health, runner, log);
    }

    /// <summary>HLTH-5: exit 0 is green, and only a red pause is lifted by it.</summary>
    [Theory]
    [InlineData(SchedulerState.PausedRed, SchedulerState.Running)]
    [InlineData(SchedulerState.Paused, SchedulerState.Paused)]
    [InlineData(SchedulerState.Draining, SchedulerState.Draining)]
    [InlineData(SchedulerState.Running, SchedulerState.Running)]
    public void Health_Green_ResumesFromPausedRed(SchedulerState before, SchedulerState after)
    {
        // HLTH-5 (L1733-1738): exit 0 -> green, and paused-red -> running, logged; other states keep.
        var (health, runner, log) = RunningCheck();
        runner.FinishWith(0);

        Assert.Equal(after, health.Reap(before));
        Assert.Equal(HealthResult.Green, health.LastResult);
        Assert.Equal(Sha, health.LastSha);
        Assert.False(health.Running);

        // The resume is logged; a green check in any other state says nothing about dispatching.
        var said = log.Lines.Any(l => l == "dispatching resumed: Proj");
        Assert.Equal(before == SchedulerState.PausedRed, said);
    }

    /// <summary>HLTH-5: a non-zero exit pauses only a scheduler that was dispatching.</summary>
    [Theory]
    [InlineData(SchedulerState.Running, SchedulerState.PausedRed)]
    [InlineData(SchedulerState.Paused, SchedulerState.Paused)]
    [InlineData(SchedulerState.Draining, SchedulerState.Draining)]
    [InlineData(SchedulerState.PausedRed, SchedulerState.PausedRed)]
    public void Health_Red_PausesOnlyFromRunning(SchedulerState before, SchedulerState after)
    {
        // HLTH-5 (L1739-1742): exit 1 -> RED, and running -> paused-red, logged; a developer's
        // pause or a drain is left as it is.
        var (health, runner, log) = RunningCheck();
        runner.FinishWith(1);

        Assert.Equal(after, health.Reap(before));
        Assert.Equal(HealthResult.Red, health.LastResult);
        Assert.Equal(Sha, health.LastSha);
        Assert.False(health.Running);
        Assert.Contains(log.Lines, l => l.Contains($"health check of main {Sha}: RED", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("dispatching paused", StringComparison.Ordinal));

        // 1472-A: the pause line is written once, on running -> paused-red only.
        var said = log.Lines.Count(l => l.StartsWith("dispatching paused: Proj main red at ", StringComparison.Ordinal));
        Assert.Equal(before == SchedulerState.Running ? 1 : 0, said);
    }

    /// <summary>1472-A: the pause line names the project, the sha and the command's first word with its exit code.</summary>
    [Fact]
    public void Health_Red_PauseLineNamesProjectShaAndFailingCheck()
    {
        var (health, runner, log) = RunningCheck();
        runner.FinishWith(3);
        health.Reap(SchedulerState.Running);
        Assert.Contains($"dispatching paused: Proj main red at {Sha} (dotnet exit 3)", log.Lines);
    }

    /// <summary>1472-A: a second red while paused writes no new pause line; green writes one resume line; green after green none.</summary>
    [Fact]
    public void Health_PauseAndResumeLines_OncePerPause()
    {
        var (health, runner, log) = RunningCheck();
        runner.FinishWith(1);
        var state = health.Reap(SchedulerState.Running);

        state = NextCheck(health, runner, "def5678", 1, state);
        Assert.Equal(SchedulerState.PausedRed, state);
        Assert.Equal(1, log.Lines.Count(l => l.StartsWith("dispatching paused:", StringComparison.Ordinal)));
        Assert.Equal(2, log.Lines.Count(l => l.Contains(": RED", StringComparison.Ordinal)));

        state = NextCheck(health, runner, "fed9876", 0, state);
        Assert.Equal(SchedulerState.Running, state);
        Assert.Equal(1, log.Lines.Count(l => l == "dispatching resumed: Proj"));

        NextCheck(health, runner, "0001111", 0, state);
        Assert.Equal(1, log.Lines.Count(l => l == "dispatching resumed: Proj"));
        Assert.Equal(1, log.Lines.Count(l => l.StartsWith("dispatching paused:", StringComparison.Ordinal)));
    }

    private static SchedulerState NextCheck(HealthCheck health, FakeHealthRunner runner, string sha, int exit, SchedulerState state)
    {
        health.RunFinished();
        runner.Sha = sha;
        Assert.True(health.StartIfWanted());
        runner.FinishWith(exit);
        return health.Reap(state);
    }

    /// <summary>MOVE-8, HLTH-5: resume lifts a red pause with no green check and clears retries.</summary>
    [Fact]
    public void Health_ResumeOverridesRed()
    {
        // Spec C5: today's behaviour, kept until the developer decides (003-D review).
        // HLTH-5, MOVE-8 (L3855): POST /resume takes paused-red to running without a green check,
        // and clears every card's retry entry, not only the stuck ones.
        var (health, runner, _) = RunningCheck();
        runner.FinishWith(1);
        var state = health.Reap(SchedulerState.Running);
        Assert.Equal(SchedulerState.PausedRed, state);
        Assert.Equal(HealthResult.Red, health.LastResult);

        var retries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["010-A"] = 2,
            ["020-B"] = 1,
        };

        state = health.Resume(state, retries);

        Assert.Equal(SchedulerState.Running, state);
        Assert.Equal(HealthResult.Red, health.LastResult);
        Assert.Empty(retries);
    }
}
