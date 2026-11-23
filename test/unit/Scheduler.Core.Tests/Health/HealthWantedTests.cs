using Scheduler.Core.Contracts;
using Scheduler.Core.Health;

namespace Scheduler.Core.Tests.Health;

/// <summary>
/// When a health check is wanted and when it may start (HLTH-1, HLTH-2; schedule.ps1
/// <c>Start-Health</c> L1695-1719, L2843, L2918, L3856).
/// Test plan: scheduler-next/test-plans/health.md.
/// </summary>
public class HealthWantedTests
{
    private const string Sha = "abc1234";
    private const string Command = "dotnet build scheduler-next\\SchedulerNext.slnx -warnaserror -m:{jobs}";

    private static SchedulerConfig Config(string health = Command, bool dryRun = false, int maxMinutes = 240) =>
        new() { HealthCommand = health, DryRun = dryRun, MaxMinutes = maxMinutes };

    private static HealthCheck New(SchedulerConfig config, FakeHealthRunner runner, FakeLog? log = null) =>
        new(config, new FakeClock(), log ?? new FakeLog(), runner);

    /// <summary>HLTH-1: no check at start-up; wanted after a finished run and from Check main.</summary>
    [Fact]
    public void Health_WantedAfterFinishedRunAndCheckMain_NotAtStartup()
    {
        // HLTH-1 (L1693, L2843, L3856): a fresh scheduler never checks main by itself; a finished
        // run wants a check, and the page's Check main wants one that forgets the last checked sha.
        var runner = new FakeHealthRunner(Sha);
        var health = New(Config(), runner);

        Assert.False(health.Wanted);
        Assert.False(health.StartIfWanted());
        Assert.Empty(runner.Starts);

        health.RunFinished();
        Assert.True(health.Wanted);
        Assert.True(health.StartIfWanted());
        Assert.Equal([(Sha, Command)], runner.Starts);
        Assert.False(health.Wanted);
        Assert.Equal(Sha, health.Sha);

        runner.FinishWith(0);
        Assert.Equal(SchedulerState.Running, health.Reap(SchedulerState.Running));
        Assert.Equal(Sha, health.LastSha);

        health.CheckNow();
        Assert.True(health.Wanted);
        Assert.Equal("", health.LastSha);
        Assert.True(health.StartIfWanted());
        Assert.Equal(2, runner.Starts.Count);
    }

    /// <summary>HLTH-2: none of the four reasons starts a check; without them one starts.</summary>
    [Theory]
    [InlineData("no command")]
    [InlineData("dry run")]
    [InlineData("one already running")]
    [InlineData("main's short sha equals the last checked sha")]
    public void Health_NotStarted_Conditions(string condition)
    {
        // HLTH-2 (L1695-1698): no command, dry run, one already running, or main's short sha
        // equals the last checked sha - then wanted is cleared and nothing starts.
        var runner = new FakeHealthRunner(Sha);
        var config = Config();
        var health = New(config, runner);
        var state = SchedulerState.Running;

        switch (condition)
        {
            case "no command":
                config = config with { HealthCommand = "" };
                health = New(config, runner);
                health.RunFinished();
                break;
            case "dry run":
                config = config with { DryRun = true };
                health = New(config, runner);
                health.RunFinished();
                break;
            case "one already running":
                health.RunFinished();
                Assert.True(health.StartIfWanted());
                health.RunFinished();
                break;
            default:
                health.RunFinished();
                Assert.True(health.StartIfWanted());
                runner.FinishWith(0);
                Assert.Equal(SchedulerState.Running, health.Reap(state));
                Assert.Equal(Sha, health.LastSha);
                health.RunFinished();
                break;
        }

        var before = runner.Starts.Count;
        Assert.True(health.Wanted);
        Assert.False(health.StartIfWanted());
        Assert.Equal(before, runner.Starts.Count);

        if (condition == "no command" || condition == "dry run")
        {
            // The command is asked for before git, so a run that cannot happen asks nothing.
            Assert.Equal(0, runner.ShaRequests);
            Assert.True(health.Wanted);
            return;
        }

        if (condition == "main's short sha equals the last checked sha")
        {
            // The same sha is not checked twice, so wanted is cleared rather than kept (L1698).
            Assert.False(health.Wanted);

            // Otherwise a start: main has moved on.
            runner.Sha = "9f3c1ab";
            health.RunFinished();
            Assert.True(health.StartIfWanted());
            Assert.Equal(before + 1, runner.Starts.Count);
        }
        else
        {
            Assert.True(health.Wanted);
        }
    }

    /// <summary>HLTH-1, HLTH-2: Check main forgets the sha, so the same sha is checked again.</summary>
    [Fact]
    public void Health_CheckMainSameSha_RunsAgain()
    {
        // HLTH-1, HLTH-2 (L3856): last checked sha = main's sha; POST /health-now forgets it, so
        // the check starts again even though main has not moved.
        var runner = new FakeHealthRunner(Sha);
        var health = New(Config(), runner);
        health.RunFinished();
        Assert.True(health.StartIfWanted());
        runner.FinishWith(1);
        health.Reap(SchedulerState.Running);
        Assert.Equal(Sha, health.LastSha);

        health.CheckNow();
        Assert.True(health.StartIfWanted());
        Assert.Equal(2, runner.Starts.Count);
        Assert.Equal(Sha, runner.Starts[1].Sha);
    }

    /// <summary>HLTH-1: at start-up nobody checks main, though it has a new commit.</summary>
    [Fact]
    public void Health_NoStartupCheck_OthersPushesUnchecked()
    {
        // Spec C7: today's behaviour, kept until the developer decides (003-D review).
        // HLTH-1 (L1693): health is never run at start-up, and only a finished run or Check main
        // wants one, so a commit pushed by someone else stays unchecked while nothing finishes.
        var runner = new FakeHealthRunner("9f3c1ab");
        var health = New(Config(), runner);

        for (var tick = 0; tick < 5; tick++)
        {
            health.StartIfWanted();
            health.Reap(SchedulerState.Running);
        }

        Assert.Empty(runner.Starts);
        Assert.Equal(HealthResult.None, health.LastResult);
        Assert.Equal("", health.LastSha);
    }
}
