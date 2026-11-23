using Scheduler.Core.Contracts;
using Scheduler.Host.Observe;

namespace Scheduler.Host.Tests.Observe;

/// <summary><c>-Observe</c> and <c>-Compare</c> on the command line, and the observe run's own folder, port and instance (card 390-B).</summary>
public sealed class ObserveCommandLineTests
{
    [Fact]
    public void Observe_is_taken_out_and_the_rest_is_left_for_the_config()
    {
        // CFG-7 (the other switches), roadmap item 1
        var parsed = ObserveCommandLine.Parse(["C:\\repo", "-observe", "-Paused"]);

        Assert.True(parsed.Observe);
        Assert.False(parsed.Compare);
        Assert.Equal(["C:\\repo", "-Paused"], parsed.Rest);
        Assert.Equal("", parsed.Error);
    }

    [Fact]
    public void Compare_takes_two_logs()
    {
        // roadmap item 1
        var parsed = ObserveCommandLine.Parse(["-Compare", "old.log", "new.log"]);

        Assert.True(parsed.Compare);
        Assert.Equal("old.log", parsed.CompareOld);
        Assert.Equal("new.log", parsed.CompareNew);
    }

    [Fact]
    public void Compare_with_one_log_or_with_observe_is_an_error()
    {
        // roadmap item 1
        Assert.NotEqual("", ObserveCommandLine.Parse(["-Compare", "old.log"]).Error);
        Assert.NotEqual("", ObserveCommandLine.Parse(["-Observe", "-Compare", "a", "b"]).Error);
    }

    [Fact]
    public void Observe_uses_its_own_log_folder_port_and_instance_and_keeps_ticking()
    {
        // CFG-14 (one instance per project), DISP-21 (dry run exits; observe does not), roadmap item 1
        var config = new SchedulerConfig { Project = "P", LogDir = "C:\\logs", Port = 8765, DryRun = true, HealthCommand = "pwsh x" };

        var (observed, settings) = ObserveSettings.Apply(config, portGiven: false);
        var (pinned, _) = ObserveSettings.Apply(config, portGiven: true);

        Assert.Equal("C:\\logs", settings.OldLogDir);
        Assert.Equal(Path.Combine("C:\\logs", "observe"), observed.LogDir);
        Assert.Equal(8766, observed.Port);
        Assert.Equal(8765, pinned.Port);
        Assert.Equal("P-observe", observed.Project);
        Assert.False(observed.DryRun);
        Assert.Equal("", observed.HealthCommand);
    }
}
