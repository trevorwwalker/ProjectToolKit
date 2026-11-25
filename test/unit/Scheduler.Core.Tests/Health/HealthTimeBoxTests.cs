// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Health;

namespace Scheduler.Core.Tests.Health;

/// <summary>
/// 1100-B (HLTH-11): a health check that runs past <c>limits.healthWarnMinutes</c> is warned about once -
/// one log line, and the page's health line while it lasts - and every result line carries how long the
/// check took. The warning never stops or fails the check.
/// Test plan: scheduler-next/test-plans/health.md.
/// </summary>
public class HealthTimeBoxTests
{
    private const string Sha = "abc1234";
    private const string Command = "dotnet build scheduler-next\\SchedulerNext.slnx";
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>HLTH-11: past the time box the check is warned about, once, whatever the tick asks next.</summary>
    [Fact]
    public void Health_PastTimeBox_WarnsOnce()
    {
        var (health, runner, log, clock) = Check(5);
        Assert.True(health.StartIfWanted());

        clock.Now = T0.AddMinutes(6);
        Assert.Equal(SchedulerState.Running, health.Reap(SchedulerState.Running));
        Assert.Contains(
            log.Lines,
            l => l == $"health check of main {Sha}: still running after 6 min (warn at 5 min)");
        Assert.NotNull(health.Warning);

        // Later ticks, and the end, say nothing more about the time box.
        clock.Now = T0.AddMinutes(20);
        health.Reap(SchedulerState.Running);
        clock.Now = T0.AddMinutes(21);
        runner.FinishWith(0);
        health.Reap(SchedulerState.Running);
        Assert.Single(log.Lines, l => l.Contains("still running after", StringComparison.Ordinal));
        Assert.Null(health.Warning);
    }

    /// <summary>HLTH-11: a check inside the time box is not warned about at all.</summary>
    [Fact]
    public void Health_InsideTimeBox_DoesNotWarn()
    {
        var (health, runner, log, clock) = Check(5);
        Assert.True(health.StartIfWanted());

        clock.Now = T0.AddMinutes(4).AddSeconds(59);
        health.Reap(SchedulerState.Running);
        clock.Now = T0.AddMinutes(5).AddSeconds(-1);
        health.Reap(SchedulerState.Running);
        runner.FinishWith(0);
        clock.Now = T0.AddMinutes(5);
        health.Reap(SchedulerState.Running);

        Assert.DoesNotContain(log.Lines, l => l.Contains("still running after", StringComparison.Ordinal));
        Assert.Null(health.Warning);
    }

    /// <summary>HLTH-11: healthWarnMinutes 0 turns the warning off; the check still runs its course.</summary>
    [Fact]
    public void Health_WarnMinutesZero_TurnsWarningOff()
    {
        var (health, runner, log, clock) = Check(0);
        Assert.True(health.StartIfWanted());

        clock.Now = T0.AddHours(3);
        health.Reap(SchedulerState.Running);
        Assert.DoesNotContain(log.Lines, l => l.Contains("still running after", StringComparison.Ordinal));
        Assert.Null(health.Warning);

        runner.FinishWith(0);
        Assert.Equal(SchedulerState.Running, health.Reap(SchedulerState.Running));
        Assert.Equal(HealthResult.Green, health.LastResult);
    }

    /// <summary>1100-B: the result line always says how long the check took, green and red alike.</summary>
    [Theory]
    [InlineData(0, "green in 131 s")]
    [InlineData(1, "RED in 131 s")]
    public void Health_Ended_ResultLineCarriesDuration(int exitCode, string expected)
    {
        var (health, runner, log, clock) = Check(5);
        Assert.True(health.StartIfWanted());

        clock.Now = T0.AddSeconds(131);
        runner.FinishWith(exitCode);
        health.Reap(SchedulerState.Running);

        Assert.Contains(log.Lines, l => l.Contains($"health check of main {Sha}: ", StringComparison.Ordinal) && l.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>1100-B: a check the kill limit ends also reports its time, and a new check can warn again.</summary>
    [Fact]
    public void Health_KilledThenNewCheck_WarnsAgain()
    {
        var (health, runner, log, clock) = Check(1);
        Assert.True(health.StartIfWanted());

        clock.Now = T0.AddMinutes(240);
        Assert.Equal(SchedulerState.PausedRed, health.Reap(SchedulerState.Running));
        Assert.Contains(log.Lines, l => l.Contains("killed after 240 min", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("RED in 14400 s", StringComparison.Ordinal));

        // A new commit, and the page asks for a check: it is measured from its own start.
        runner.Sha = "9f3c1ab";
        health.CheckNow();
        clock.Now = T0.AddMinutes(250);
        Assert.True(health.StartIfWanted());
        Assert.Null(health.Warning);

        clock.Now = T0.AddMinutes(252);
        health.Reap(SchedulerState.Running);
        Assert.Contains(log.Lines, l => l == $"health check of main 9f3c1ab: still running after 2 min (warn at 1 min)");
    }

    private static (HealthCheck Health, FakeHealthRunner Runner, FakeLog Log, FakeClock Clock) Check(int warnMinutes)
    {
        var runner = new FakeHealthRunner(Sha);
        var log = new FakeLog();
        var clock = new FakeClock { Now = T0 };
        var health = new HealthCheck(
            new SchedulerConfig { HealthCommand = Command, HealthWarnMinutes = warnMinutes }, clock, log, runner);
        health.RunFinished();
        return (health, runner, log, clock);
    }
}
