// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray.Tests;

public class TrayArgumentsTests
{
    // 670-B: every argument is passed on as it came, and -NoBrowser is added, because the tray opens the
    // page itself when its icon is double-clicked (CFG-15's browser opening is the tray's, not the scheduler's).
    [Fact]
    public void Every_argument_goes_to_the_scheduler_and_the_page_is_left_in_the_tray()
    {
        var args = TrayArguments.Parse([@"C:\Projects\Game", "-Slots", "3", "-Paused"]);

        Assert.True(args.Ok);
        Assert.Equal(@"C:\Projects\Game", args.Repository);
        Assert.Equal([@"C:\Projects\Game", "-Slots", "3", "-Paused", TrayArguments.NoBrowserSwitch], args.SchedulerArgs);
    }

    // Whoever starts the tray can say it themselves, either way round; the tray does not say it twice.
    [Theory]
    [InlineData(new[] { @"C:\Projects\Game" }, new[] { @"C:\Projects\Game", "-NoBrowser" })]
    [InlineData(new[] { @"C:\Projects\Game", "-Slots", "2" }, new[] { @"C:\Projects\Game", "-Slots", "2", "-NoBrowser" })]
    [InlineData(new[] { @"C:\Projects\Game", "-NoBrowser" }, new[] { @"C:\Projects\Game", "-NoBrowser" })]
    [InlineData(new[] { @"C:\Projects\Game", "-nobrowser" }, new[] { @"C:\Projects\Game", "-nobrowser" })]
    [InlineData(new[] { @"C:\Projects\Game", "-NoBrowser:$false" }, new[] { @"C:\Projects\Game", "-NoBrowser:$false" })]
    public void The_page_switch_is_added_once(string[] given, string[] expected) =>
        Assert.Equal(expected, TrayArguments.Parse(given).SchedulerArgs);

    [Fact]
    public void No_arguments_is_an_error()
    {
        Assert.False(TrayArguments.Parse([]).Ok);
        Assert.False(TrayArguments.Parse(["  "]).Ok);
    }

    [Fact]
    public void An_option_first_is_an_error_because_the_repository_must_come_first()
    {
        var args = TrayArguments.Parse(["-Slots", "3"]);

        Assert.False(args.Ok);
        Assert.Contains("-Slots", args.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Projects\Game", "Game")]
    [InlineData(@"C:\Projects\Game\", "Game")]
    [InlineData("C:/Projects/Game/", "Game")]
    public void The_project_is_guessed_from_the_repository_folder(string repo, string expected) =>
        Assert.Equal(expected, TrayArguments.Parse([repo]).ProjectGuess());

    // 1014-B: -Quiet is the tray's own switch: it is read, and not handed to the scheduler.
    [Theory]
    [InlineData(new[] { @"C:\Projects\Game", "-Quiet" }, true)]
    [InlineData(new[] { @"C:\Projects\Game", "-quiet" }, true)]
    [InlineData(new[] { @"C:\Projects\Game" }, false)]
    [InlineData(new[] { "-Pool", "-Quiet" }, true)]
    [InlineData(new[] { "-Pool" }, false)]
    public void Quiet_is_read_and_kept_from_the_scheduler(string[] given, bool quiet)
    {
        var args = TrayArguments.Parse(given);

        Assert.Equal(quiet, args.Quiet);
        Assert.DoesNotContain(args.SchedulerArgs, a => a.Equals("-Quiet", StringComparison.OrdinalIgnoreCase));
    }

    // 1265-B: -RemoveTrayCopy is the tray's own switch, taken before any repository.
    [Fact]
    public void Remove_tray_copy_parses_with_the_kit_folder_when_given()
    {
        var plain = TrayArguments.Parse(["-RemoveTrayCopy"]);
        var kit = TrayArguments.Parse(["-RemoveTrayCopy", @"-SchedulerDir:C:\kit\scheduler"]);

        Assert.True(plain.Ok);
        Assert.True(plain.RemoveTrayCopy);
        Assert.Equal("", plain.SchedulerDir);
        Assert.True(kit.RemoveTrayCopy);
        Assert.Equal(@"C:\kit\scheduler", kit.SchedulerDir);
        Assert.False(TrayArguments.Parse([@"C:\Projects\Game"]).RemoveTrayCopy);
    }
}
