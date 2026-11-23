using System.Diagnostics;

namespace Scheduler.Tray;

/// <summary><c>scheduler-tray &lt;repository&gt; [the scheduler's own arguments]</c>.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var parsed = TrayArguments.Parse(args);
        if (!parsed.Ok)
        {
            MessageBox.Show($"{parsed.Error}\n\n{TrayArguments.Usage}", "scheduler-tray", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        var here = AppContext.BaseDirectory;
        var schedulerDir = parsed.SchedulerDir.Length > 0 ? parsed.SchedulerDir : here;

        // 967-B: the tray runs from its own copy in %LOCALAPPDATA%\ProjectToolkit\tray, so the published
        // folder holds no running image of the tray and "Update and restart" can move every file in it.
        // The copy is started with the same arguments and told where the scheduler is.
        if (parsed.SchedulerDir.Length == 0 && TraySelfCopy.NeedsCopy(here) && RunFromCopy(TraySelfCopy.Folder(), schedulerDir, args))
        {
            return 0;
        }

        var exe = Path.Combine(schedulerDir, "scheduler-next.exe");
        if (!File.Exists(exe))
        {
            MessageBox.Show($"scheduler-next.exe is not in {schedulerDir}", "scheduler-tray", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        using var app = new TrayApp(parsed, exe);
        Application.Run(app);
        return 0;
    }

    // The tray's own copy (967-B). True when it was started, in which case this process is done; when
    // anything about it failed the tray carries on where it was started, as it did before this existed.
    private static bool RunFromCopy(string target, string schedulerDir, IReadOnlyList<string> args)
    {
        var fs = new SwapFileSystem();
        var exe = TraySelfCopy.Exe(target);
        if (!TraySelfCopy.Copy(fs, AppContext.BaseDirectory, target) || !fs.FileExists(exe))
        {
            return false;
        }

        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        info.ArgumentList.Add($"{TrayArguments.SchedulerDirSwitch}:{schedulerDir}");
        info.Environment[TraySelfCopy.FromVariable] = "1";
        try
        {
            return Process.Start(info) is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
