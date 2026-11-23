using System.Drawing;

namespace Scheduler.Tray.Tests;

public class TrayIconLoaderTests
{
    private static string RepoIcon()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "assets", "icons", "scheduler.ico")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(dir!, "assets", "icons", "scheduler.ico");
    }

    [Fact]
    public void The_copied_icon_file_is_loaded()
    {
        var folder = Scheduler.TestSupport.TestTmp.Create("ico");
        File.Copy(RepoIcon(), Path.Combine(folder, TrayIconLoader.FileName));
        var lines = new List<string>();

        using var icon = TrayIconLoader.Load(folder, lines.Add);

        Assert.NotSame(SystemIcons.Application, icon);
        Assert.Empty(lines);
    }

    [Fact]
    public void A_missing_file_gives_the_embedded_icon_without_a_log_line()
    {
        // 993-B: the tray's AppData copy has no .ico beside it; the icon is in the assembly.
        var folder = Scheduler.TestSupport.TestTmp.Create("ico");
        var lines = new List<string>();

        using var icon = TrayIconLoader.Load(folder, lines.Add);

        Assert.NotSame(SystemIcons.Application, icon);
        Assert.Empty(lines);
    }

    [Fact]
    public void An_unreadable_file_beside_the_exe_still_gives_the_embedded_icon()
    {
        var folder = Scheduler.TestSupport.TestTmp.Create("ico");
        File.WriteAllText(Path.Combine(folder, TrayIconLoader.FileName), "not an icon");
        var lines = new List<string>();

        using var icon = TrayIconLoader.Load(folder, lines.Add);

        Assert.NotSame(SystemIcons.Application, icon);
        Assert.Single(lines);
    }

    [Fact]
    public void The_build_copies_the_icon_beside_the_exe() =>
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, TrayIconLoader.FileName)));
}
