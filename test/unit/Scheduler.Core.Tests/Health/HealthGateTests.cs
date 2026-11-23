using Scheduler.Core.Contracts;
using Scheduler.Core.Health;

namespace Scheduler.Core.Tests.Health;

/// <summary>
/// The scheduler never runs a card's gates (HLTH-7; schedule.ps1 L1753, L1761): hand-over is the
/// worker's own job, and the only command the scheduler asks for is the configured health command.
/// Test plan: scheduler-next/test-plans/health.md.
/// </summary>
public class HealthGateTests
{
    private const string Command = "dotnet build scheduler-next\\SchedulerNext.slnx -warnaserror -m:{jobs}";

    /// <summary>HLTH-7: after a finished run the scheduler asks for the health command and nothing else.</summary>
    [Fact]
    public void Health_SchedulerDoesNotRunGates()
    {
        // HLTH-7 (L1753, L1761): the worker's commit is not re-checked by the scheduler. The gate
        // rules reach the worker only through the prompt (asserted by
        // Workers_Prompt_ContainsEveryPart in test-plans/workers.md), so no card ever appears in a
        // command the scheduler asks the machine to run.
        var runner = new FakeHealthRunner("9f3c1ab");
        var health = new HealthCheck(
            new SchedulerConfig { HealthCommand = Command }, new FakeClock(), new FakeLog(), runner);

        // The worker finished done, having pushed its commit to main.
        health.RunFinished();
        Assert.True(health.StartIfWanted());

        var run = Assert.Single(runner.Starts);
        Assert.Equal("9f3c1ab", run.Sha);
        Assert.Equal(Command, run.Command);
        Assert.DoesNotContain("010-A", run.Command, StringComparison.Ordinal);
        Assert.DoesNotContain("010-A-first", run.Command, StringComparison.Ordinal);
        Assert.All(runner.Starts, s => Assert.Equal(Command, s.Command));
    }
}
