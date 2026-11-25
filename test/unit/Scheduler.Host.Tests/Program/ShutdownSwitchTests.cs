// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Program;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// <c>-ShutdownEvent:&lt;name&gt;</c> (670-B), the switch the tray adds so it can ask the scheduler to end
/// by itself instead of sending a Ctrl+C the workers would hear.
/// </summary>
public class ShutdownSwitchTests
{
    [Theory]
    [InlineData("-ShutdownEvent:ProjectToolkit-abc-scheduler-shutdown", "ProjectToolkit-abc-scheduler-shutdown")]
    [InlineData("-ShutdownEvent=ProjectToolkit-abc-scheduler-shutdown", "ProjectToolkit-abc-scheduler-shutdown")]
    [InlineData("-shutdownevent: name ", "name")]
    public void The_Name_Typed_Any_way_Is_Read(string given, string expected) =>
        Assert.Equal(expected, ShutdownSwitch.EventName(["C:\\repo", given]));

    [Fact]
    public void No_Switch_Is_No_Event()
    {
        Assert.Equal("", ShutdownSwitch.EventName(["C:\\repo", "-NoBrowser"]));
        Assert.Equal("", ShutdownSwitch.EventName([]));
        Assert.Equal("", ShutdownSwitch.EventName(["-ShutdownEvent", "orphaned-value"]));
    }
}
