// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray.Tests;

/// <summary>
/// Card 967-B: when the tray relaunches itself from its own copy it is told where the scheduler is with
/// <c>-SchedulerDir:</c>. That switch is the tray's own - the scheduler never sees it - and everything else
/// still goes to the scheduler as it came.
/// </summary>
public class TraySchedulerDirArgumentTests
{
    [Fact]
    public void The_scheduler_folder_is_the_trays_own_switch()
    {
        var args = TrayArguments.Parse([@"C:\Projects\Game", @"-SchedulerDir:C:\kit\out\windows\scheduler", "-Slots", "3"]);

        Assert.True(args.Ok);
        Assert.Equal(@"C:\kit\out\windows\scheduler", args.SchedulerDir);
        Assert.Equal([@"C:\Projects\Game", "-Slots", "3", TrayArguments.NoBrowserSwitch], args.SchedulerArgs);
    }

    // A tray started the old way still finds the scheduler beside itself (TRAY-1).
    [Fact]
    public void Without_the_switch_the_scheduler_stays_beside_the_tray() =>
        Assert.Equal("", TrayArguments.Parse([@"C:\Projects\Game"]).SchedulerDir);

    [Theory]
    [InlineData(new[] { "-SchedulerDir:C:\\a\\b" }, @"C:\a\b")]
    [InlineData(new[] { "-SchedulerDir=C:\\a\\b" }, @"C:\a\b")]
    [InlineData(new[] { "-schedulerdir:C:\\a\\b" }, @"C:\a\b")]
    [InlineData(new[] { "other" }, "")]
    public void The_switch_is_read_however_it_is_written(string[] given, string expected) =>
        Assert.Equal(expected, TrayArguments.ValueOf(given, TrayArguments.SchedulerDirSwitch));
}
