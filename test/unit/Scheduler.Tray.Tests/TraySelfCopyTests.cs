// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Tray.Tests.Fakes;

namespace Scheduler.Tray.Tests;

/// <summary>
/// Card 967-B: the tray runs from its own copy in <c>%LOCALAPPDATA%\ProjectToolkit\tray\</c>, so the
/// published folder holds no running image of the tray and a deploy can move every file in it. The copy is
/// the tray's own files only, and a tray that is already the copy does not copy and relaunch itself again.
/// </summary>
public class TraySelfCopyTests
{
    private const string Published = @"C:\kit\out\windows\scheduler";
    private const string Copy = @"C:\Users\dev\AppData\Local\ProjectToolkit\tray";

    // The scheduler's files must not be copied along: the copy is the tray, and the swap moves the rest.
    [Fact]
    public void Only_the_trays_own_files_are_copied()
    {
        var fs = Build();

        Assert.True(TraySelfCopy.Copy(fs, Published, Copy));
        Assert.Equal("tray exe", fs.Content(Path.Combine(Copy, "scheduler-tray.exe")));
        Assert.Equal("tray dll", fs.Content(Path.Combine(Copy, "Scheduler.Tray.dll")));
        Assert.Null(fs.Content(Path.Combine(Copy, "scheduler-next.exe")));
        Assert.Null(fs.Content(Path.Combine(Copy, "Scheduler.Host.dll")));
    }

    // 993-B: nothing the published tray carries is left behind, the icon included.
    [Fact]
    public void Every_tray_file_and_the_icon_are_copied()
    {
        var fs = Build()
            .WithFile(Path.Combine(Published, "scheduler-tray.deps.json"), "deps")
            .WithFile(Path.Combine(Published, "scheduler.ico"), "icon");

        Assert.True(TraySelfCopy.Copy(fs, Published, Copy));
        Assert.Equal("deps", fs.Content(Path.Combine(Copy, "scheduler-tray.deps.json")));
        Assert.Equal("icon", fs.Content(Path.Combine(Copy, "scheduler.ico")));
        Assert.Null(fs.Content(Path.Combine(Copy, "scheduler-next.exe")));
    }

    // The copy is what the swap needs: nothing in the published folder is a running image once the tray has
    // moved out of it, so the whole folder can be swapped.
    [Fact]
    public void The_copy_is_the_file_the_swap_needs_to_move()
    {
        var fs = Build();
        Assert.True(TraySelfCopy.Copy(fs, Published, Copy));

        var layout = DeployLayout.ForSchedulerDir(Published);
        var outcome = SchedulerSwap.Run(fs, layout, []);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.Equal("new tray", fs.Content(Path.Combine(Published, "scheduler-tray.exe")));
        Assert.Equal("new exe", fs.Content(Path.Combine(Published, "scheduler-next.exe")));
    }

    // The names a swap leaves alone are the tray's own, as they lie in the folder.
    [Fact]
    public void The_trays_own_files_are_named_by_name()
    {
        var fs = Build();

        Assert.Equal(
            ["scheduler-tray.exe", "Scheduler.Tray.dll"],
            TraySelfCopy.Names(fs, Published),
            StringComparer.OrdinalIgnoreCase);
    }

    // Started from the published folder the tray has to move to the copy; started from the copy, or already
    // relaunched by its own copy, it must not start another one (967-B).
    [Theory]
    [InlineData(Published, null, "", true)]
    [InlineData(Copy, Copy, "", false)]
    [InlineData(Published, null, "1", false)]
    [InlineData(Published + @"\", Copy, "", true)]
    public void Only_a_tray_outside_the_copy_needs_to_move_itself(string from, string? target, string already, bool expected) =>
        Assert.Equal(expected, TraySelfCopy.NeedsCopy(from, Published, target ?? Copy, already));

    private const string LocalAppData = @"C:\Users\dev\AppData\Local";

    // 1265-B: the copy folder is per kit folder, under the tray folder.
    [Fact]
    public void The_copy_lives_under_the_users_local_app_data_in_a_folder_of_its_kit()
    {
        var folder = TraySelfCopy.Folder(Published, LocalAppData);

        Assert.Equal(Path.Combine(LocalAppData, "ProjectToolkit", "tray", TraySelfCopy.KitKey(Published)), folder);
        Assert.Equal(Path.Combine(folder, "scheduler-tray.exe"), TraySelfCopy.Exe(folder));
    }

    [Fact]
    public void Two_kit_folders_have_two_copy_folders()
    {
        Assert.NotEqual(
            TraySelfCopy.Folder(Published, LocalAppData),
            TraySelfCopy.Folder(@"C:\other\scheduler", LocalAppData));
    }

    [Theory]
    [InlineData(@"C:\KIT\OUT\WINDOWS\SCHEDULER")]
    [InlineData(Published + @"\")]
    [InlineData(Published + "/")]
    public void The_same_folder_spelled_another_way_has_the_same_copy_folder(string spelling) =>
        Assert.Equal(TraySelfCopy.Folder(Published, LocalAppData), TraySelfCopy.Folder(spelling, LocalAppData));

    // The key is the end of the tray's exit-signal name, so both name the kit folder the same way.
    [Fact]
    public void The_kit_key_is_the_end_of_the_exit_signal_name()
    {
        var key = TraySelfCopy.KitKey(Published);

        Assert.Equal(16, key.Length);
        Assert.EndsWith(key, TrayExitSignal.NameFor(Published), StringComparison.Ordinal);
    }

    // -RemoveTrayCopy deletes its own kit's folder and leaves another kit's.
    [Fact]
    public void Removing_the_copy_deletes_only_its_own_kits_folder()
    {
        var mine = TraySelfCopy.Folder(Published, LocalAppData);
        var theirs = TraySelfCopy.Folder(@"C:\other\scheduler", LocalAppData);
        var fs = new FakeSwapFileSystem()
            .WithFile(Path.Combine(mine, "scheduler-tray.exe"), "mine")
            .WithFile(Path.Combine(theirs, "scheduler-tray.exe"), "theirs");

        var (code, line) = TraySelfCopy.RemoveCopy(fs, mine);

        Assert.Equal(0, code);
        Assert.Contains(mine, line, StringComparison.Ordinal);
        Assert.False(fs.DirectoryExists(mine));
        Assert.Equal("theirs", fs.Content(Path.Combine(theirs, "scheduler-tray.exe")));
    }

    [Fact]
    public void Removing_a_copy_that_was_never_there_is_not_an_error()
    {
        var (code, _) = TraySelfCopy.RemoveCopy(new FakeSwapFileSystem(), TraySelfCopy.Folder(Published, LocalAppData));

        Assert.Equal(0, code);
    }

    [Fact]
    public void Removing_a_copy_with_a_file_in_use_says_so_and_exits_1()
    {
        var mine = TraySelfCopy.Folder(Published, LocalAppData);
        var exe = Path.Combine(mine, "scheduler-tray.exe");
        var fs = new FakeSwapFileSystem().WithFile(exe, "mine").Lock(mine);

        var (code, line) = TraySelfCopy.RemoveCopy(fs, mine);

        Assert.Equal(1, code);
        Assert.Contains(mine, line, StringComparison.Ordinal);
    }

    private static FakeSwapFileSystem Build() =>
        new FakeSwapFileSystem()
            .WithFile(Path.Combine(Published, "scheduler-tray.exe"), "tray exe")
            .WithFile(Path.Combine(Published, "Scheduler.Tray.dll"), "tray dll")
            .WithFile(Path.Combine(Published, "scheduler-next.exe"), "old exe")
            .WithFile(Path.Combine(Published, "Scheduler.Host.dll"), "old host")
            .WithFile(Path.Combine(Published, @"themes\theme.default.json"), "old theme")
            .WithFile(Path.Combine(Published, "scheduler.prev", "scheduler-next.exe"), "older exe")
            .WithFile(Path.Combine(DeployLayout.ForSchedulerDir(Published).Next, "scheduler-tray.exe"), "new tray")
            .WithFile(Path.Combine(DeployLayout.ForSchedulerDir(Published).Next, "scheduler-next.exe"), "new exe")
            .WithFile(Path.Combine(DeployLayout.ForSchedulerDir(Published).Next, "Scheduler.Host.dll"), "new host");
}
