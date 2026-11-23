using Scheduler.Core;
using Scheduler.Host.Program;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// Card 967-B: the tray or <c>bin\deploy-scheduler.ps1</c> starts a swapped-in build with
/// <c>-Replaces:&lt;version&gt;</c>, and the scheduler log says which version replaced which (VER rules).
/// Like <c>-ShutdownEvent</c> the switch is read at the entry point; the scheduler's own command line
/// leaves an unbound switch alone (CFG-7).
/// </summary>
public class ReplacesSwitchTests
{
    [Theory]
    [InlineData(new[] { @"C:\repo", "-Replaces:3.0.7" }, "3.0.7")]
    [InlineData(new[] { @"C:\repo", "-Replaces=3.0.7" }, "3.0.7")]
    [InlineData(new[] { @"C:\repo", "-replaces:3.0.7" }, "3.0.7")]
    [InlineData(new[] { @"C:\repo" }, "")]
    [InlineData(new[] { @"C:\repo", "-Replaces:" }, "")]
    [InlineData(new[] { @"C:\repo", "-Replaces:  " }, "")]
    public void The_switch_names_the_version_this_build_took_over_from(string[] args, string expected) =>
        Assert.Equal(expected, ReplacesSwitch.Version(args));

    // The line the scheduler log carries: the version that is running, and the one it replaced.
    [Fact]
    public void The_log_line_names_both_builds()
    {
        var line = ReplacesSwitch.ReplacedLine("3.0.8", "3.0.7");

        Assert.Equal("scheduler-next 3.0.8 replaced 3.0.7", line);
    }

    // The number shown is the build's own (VER-2): nothing here holds a copy of it.
    [Fact]
    public void The_replacing_version_is_the_one_this_build_carries() =>
        Assert.Contains(CoreInfo.Version, ReplacesSwitch.ReplacedLine(CoreInfo.Version, "1.0.0"), StringComparison.Ordinal);
}
