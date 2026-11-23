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
    [InlineData(Copy, null, "", false)]
    [InlineData(Published, null, "1", false)]
    [InlineData(Published + @"\", Copy, "", true)]
    public void Only_a_tray_outside_the_copy_needs_to_move_itself(string from, string? target, string already, bool expected) =>
        Assert.Equal(expected, TraySelfCopy.NeedsCopy(from, target ?? Copy, already));

    // Where the tray runs from, and what it is called there.
    [Fact]
    public void The_copy_lives_under_the_users_local_app_data()
    {
        Assert.Equal(
            Path.Combine(@"C:\Users\dev\AppData\Local", "ProjectToolkit", "tray"),
            TraySelfCopy.Folder(@"C:\Users\dev\AppData\Local"));
        Assert.Equal(
            Path.Combine(@"C:\Users\dev\AppData\Local", "ProjectToolkit", "tray", "scheduler-tray.exe"),
            TraySelfCopy.Exe(TraySelfCopy.Folder(@"C:\Users\dev\AppData\Local")));
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
