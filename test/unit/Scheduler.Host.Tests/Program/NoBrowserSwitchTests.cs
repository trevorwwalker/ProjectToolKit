// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Program;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// <c>-NoBrowser</c> (670-B): the page is opened only when it is not given (CFG-15). The tray starts the
/// scheduler with it and opens the page itself, so a start does not spawn a browser window.
/// </summary>
public class NoBrowserSwitchTests
{
    [Theory]
    [InlineData(new string[] { }, false)]
    [InlineData(new[] { @"C:\repo" }, false)]
    [InlineData(new[] { @"C:\repo", "-NoBrowser" }, true)]
    [InlineData(new[] { @"C:\repo", "-nobrowser" }, true)]
    [InlineData(new[] { @"C:\repo", "-NoBrowser:true" }, true)]
    [InlineData(new[] { @"C:\repo", "-NoBrowser:false" }, false)]
    [InlineData(new[] { @"C:\repo", "-NoBrowser=$false" }, false)]
    [InlineData(new[] { @"C:\repo", "-NoBrowserish" }, false)]
    [InlineData(new[] { @"C:\repo", "NoBrowser" }, false)]
    public void The_switch_is_read_from_the_command_line(string[] args, bool expected) =>
        Assert.Equal(expected, NoBrowserSwitch.IsSet(args));
}
