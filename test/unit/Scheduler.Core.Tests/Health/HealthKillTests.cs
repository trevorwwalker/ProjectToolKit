using Scheduler.Core.Contracts;
using Scheduler.Core.Health;

namespace Scheduler.Core.Tests.Health;

/// <summary>
/// A check that hangs is killed after maxMinutes and treated as red (HLTH-4; schedule.ps1
/// <c>Reap-Health</c> L1721-1731).
/// Test plan: scheduler-next/test-plans/health.md.
/// </summary>
public class HealthKillTests
{
    private const string Sha = "abc1234";
    private const string Command = "dotnet build scheduler-next\\SchedulerNext.slnx";

    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.FromHours(-8));

    private static (HealthCheck Health, FakeHealthRunner Runner, FakeClock Clock, FakeLog Log) Running(int maxMinutes)
    {
        var runner = new FakeHealthRunner(Sha);
        var clock = new FakeClock { Now = T0 };
        var log = new FakeLog();
        var health = new HealthCheck(
            new SchedulerConfig { HealthCommand = Command, MaxMinutes = maxMinutes }, clock, log, runner);
        health.RunFinished();
        Assert.True(health.StartIfWanted());
        return (health, runner, clock, log);
    }

    /// <summary>HLTH-4: killed exactly at maxMinutes, logged, RED, and running becomes paused-red.</summary>
    [Fact]
    public void Health_HangingCheck_KilledAfterMaxMinutesTreatedRed()
    {
        // HLTH-4 (L1725-1728): still running just before the limit; at it the tree is killed, the
        // check is logged as treated as red, the result is RED and the scheduler pauses red.
        var (health, runner, clock, log) = Running(maxMinutes: 240);
        Assert.True(health.Running);

        clock.Now = T0.AddMinutes(239.99);
        Assert.Equal(SchedulerState.Running, health.Reap(SchedulerState.Running));
        Assert.Equal(0, runner.Kills);
        Assert.True(health.Running);
        Assert.Equal(HealthResult.None, health.LastResult);

        clock.Now = T0.AddMinutes(240);
        Assert.Equal(SchedulerState.PausedRed, health.Reap(SchedulerState.Running));
        Assert.Equal(1, runner.Kills);
        Assert.False(health.Running);
        Assert.Equal(HealthResult.Red, health.LastResult);
        Assert.Equal(Sha, health.LastSha);
        Assert.Contains(log.Lines, l => l.Contains("killed after 240 min; treated as red", StringComparison.Ordinal));
    }

    /// <summary>HLTH-4: the limit that kills the check is maxMinutes, whatever it is set to.</summary>
    [Fact]
    public void Health_KillLimit_UsesMaxMinutes()
    {
        // Spec C16: today's behaviour, kept until the developer decides (003-D review).
        // HLTH-4 (L1725): the kill limit is limits.maxMinutes, not a health-specific limit, so a
        // config that changes maxMinutes moves the kill with it.
        var (health, runner, clock, log) = Running(maxMinutes: 45);

        clock.Now = T0.AddMinutes(44.99);
        health.Reap(SchedulerState.Running);
        Assert.Equal(0, runner.Kills);
        Assert.True(health.Running);

        clock.Now = T0.AddMinutes(45);
        Assert.Equal(SchedulerState.PausedRed, health.Reap(SchedulerState.Running));
        Assert.Equal(1, runner.Kills);
        Assert.Equal(HealthResult.Red, health.LastResult);
        Assert.Contains(log.Lines, l => l.Contains("killed after 45 min; treated as red", StringComparison.Ordinal));
    }
}
