using Scheduler.Tray.Tests.Fakes;

namespace Scheduler.Tray.Tests;

/// <summary>
/// Card 967-B: the swap that puts a published <c>scheduler.next</c> into the running <c>scheduler</c>
/// folder, keeping the build it replaced in <c>scheduler.prev</c>. The path the page's firewall rule names
/// (BUILD-3) never moves, and a file that will not move - a running exe - leaves the old build in place.
/// </summary>
public class SchedulerSwapTests
{
    private const string Out = @"C:\kit\out\windows";
    private readonly FakeSwapFileSystem _fs = new();
    private readonly DeployLayout _layout = new(Out);

    public SchedulerSwapTests()
    {
        _fs.WithFile(At(_layout.Current, "scheduler-next.exe"), "old exe")
            .WithFile(At(_layout.Current, "Scheduler.Host.dll"), "old host")
            .WithFile(At(_layout.Current, "scheduler-tray.exe"), "old tray")
            .WithFile(At(_layout.Next, "scheduler-next.exe"), "new exe")
            .WithFile(At(_layout.Next, "Scheduler.Host.dll"), "new host")
            .WithFile(At(_layout.Next, "scheduler-tray.exe"), "new tray");
        _fs.WithDir(At(_layout.Current, "themes"));
        _fs.WithFile(At(_layout.Current, @"themes\theme.default.json"), "old theme");
        _fs.WithDir(At(_layout.Next, "themes"));
        _fs.WithFile(At(_layout.Next, @"themes\theme.default.json"), "new theme");
    }

    // 967-B: the new build is what runs afterwards, and the build it replaced is kept beside it.
    [Fact]
    public void The_published_build_becomes_the_running_one()
    {
        var outcome = SchedulerSwap.Run(_fs, _layout, ["scheduler-tray.exe"]);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.Equal("new exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
        Assert.Equal("new host", _fs.Content(At(_layout.Current, "Scheduler.Host.dll")));
        Assert.Equal("new theme", _fs.Content(At(_layout.Current, @"themes\theme.default.json")));
        Assert.Equal("old exe", _fs.Content(At(_layout.Prev, "scheduler-next.exe")));
        Assert.Equal("old theme", _fs.Content(At(_layout.Prev, @"themes\theme.default.json")));
        Assert.False(_fs.DirectoryExists(_layout.Next));
    }

    // 967-B: the tray's own files are the ones Windows will not release while it runs, so they stay put and
    // everything else in the folder swaps - which is what lets a deploy run from the tray's own menu.
    [Fact]
    public void The_running_trays_own_files_stay_where_they_are()
    {
        _fs.Lock(At(_layout.Current, "scheduler-tray.exe"));

        var outcome = SchedulerSwap.Run(_fs, _layout, TraySelfCopy.Names(_fs, _layout.Current));

        Assert.True(outcome.Ok, outcome.Message);
        Assert.Equal("old tray", _fs.Content(At(_layout.Current, "scheduler-tray.exe")));
        Assert.Equal("new exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
        Assert.Null(_fs.Content(At(_layout.Prev, "scheduler-tray.exe")));
    }

    // 967-B, BUILD-3: the firewall rule is bound to out\windows\scheduler\scheduler-next.exe. A deploy has
    // to leave the page answering from that same path.
    [Fact]
    public void The_path_the_firewall_rule_names_does_not_move()
    {
        var rule = _layout.SchedulerExe;

        var outcome = SchedulerSwap.Run(_fs, _layout, []);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.Equal(@"C:\kit\out\windows\scheduler\scheduler-next.exe", rule);
        Assert.Equal("new exe", _fs.Content(rule));
    }

    // A locked file the swap had to move (anything but the tray's own) fails the deploy and the running
    // folder ends up exactly as it started: the tray keeps talking to the build that was already running.
    [Fact]
    public void A_file_that_will_not_move_leaves_the_old_build_running()
    {
        _fs.Lock(At(_layout.Current, "Scheduler.Host.dll"));

        var outcome = SchedulerSwap.Run(_fs, _layout, []);

        Assert.False(outcome.Ok);
        Assert.Contains("the swap went wrong", outcome.Message, StringComparison.Ordinal);
        Assert.Contains("in place again", outcome.Message, StringComparison.Ordinal);
        Assert.Equal("old exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
        Assert.Equal("old host", _fs.Content(At(_layout.Current, "Scheduler.Host.dll")));
        Assert.Equal("old tray", _fs.Content(At(_layout.Current, "scheduler-tray.exe")));
    }

    // What an earlier deploy left in scheduler.prev must not survive into this one's rollback.
    [Fact]
    public void What_an_earlier_deploy_left_behind_is_cleared_first()
    {
        _fs.WithFile(At(_layout.Prev, "gone.dll"), "leftover");

        var outcome = SchedulerSwap.Run(_fs, _layout, []);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.Null(_fs.Content(At(_layout.Prev, "gone.dll")));
        Assert.Equal("old exe", _fs.Content(At(_layout.Prev, "scheduler-next.exe")));
    }

    // Nothing published, nothing swapped: the running scheduler is untouched and the log says what is missing.
    [Theory]
    [InlineData(false, false, "is not there")]
    [InlineData(true, false, "has no scheduler-next.exe in it")]
    public void Without_a_published_build_nothing_moves(bool nextIsThere, bool exeIsThere, string says)
    {
        if (!nextIsThere)
        {
            _fs.Delete(_layout.Next);
        }
        else if (!exeIsThere)
        {
            _fs.Delete(At(_layout.Next, "scheduler-next.exe"));
        }

        var outcome = SchedulerSwap.Run(_fs, _layout, []);

        Assert.False(outcome.Ok);
        Assert.Contains(says, outcome.Message, StringComparison.Ordinal);
        Assert.Equal("old exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
    }

    // 967-B: the new build would not start, so the build it replaced goes back and runs again.
    [Fact]
    public void Rolling_back_puts_the_replaced_build_back()
    {
        var swapped = SchedulerSwap.Run(_fs, _layout, ["scheduler-tray.exe"]);
        Assert.True(swapped.Ok, swapped.Message);

        var back = SchedulerSwap.RollBack(_fs, _layout, ["scheduler-tray.exe"]);

        Assert.True(back.Ok, back.Message);
        Assert.Equal("old exe", _fs.Content(At(_layout.Current, "scheduler-next.exe")));
        Assert.Equal("old host", _fs.Content(At(_layout.Current, "Scheduler.Host.dll")));
        Assert.Equal("old theme", _fs.Content(At(_layout.Current, @"themes\theme.default.json")));
        Assert.Equal("old tray", _fs.Content(At(_layout.Current, "scheduler-tray.exe")));
        Assert.False(_fs.DirectoryExists(_layout.Prev));
    }

    // The rollback is what the tray falls back on when the new scheduler never said its start line; it has
    // to work even with the new build's extra files sitting in the running folder.
    [Fact]
    public void Rolling_back_clears_what_the_failed_build_brought_in()
    {
        SchedulerSwap.Run(_fs, _layout, []);
        _fs.WithFile(At(_layout.Current, "only_in_the_new_build.dll"), "extra");

        var back = SchedulerSwap.RollBack(_fs, _layout, []);

        Assert.True(back.Ok, back.Message);
        Assert.DoesNotContain("only_in_the_new_build.dll", _fs.Names(_layout.Current), StringComparer.OrdinalIgnoreCase);
    }

    private static string At(string dir, string name) => Path.Combine(dir, name);
}
