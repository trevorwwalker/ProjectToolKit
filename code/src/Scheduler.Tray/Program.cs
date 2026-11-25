// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
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

        // INS-19 (1245-B): an installer's stop, before the tray copies itself or starts anything.
        if (parsed.StopForUpdate)
        {
            return RunStopForUpdate(parsed, schedulerDir);
        }

        // INS-22 (1265-B): an uninstall removes this kit folder's copy of the tray, and no other kit's.
        if (parsed.RemoveTrayCopy)
        {
            KitProcesses.UseParentConsole();
            var (removeCode, removeLine) = TraySelfCopy.RemoveCopy(new SwapFileSystem(), TraySelfCopy.Folder(schedulerDir));
            Console.WriteLine(removeLine);
            return removeCode;
        }

        // INS-13 (1247-C): a pool with no pool file has nothing to run, and the scheduler's own error says
        // only that. Say what to do about it - the guide's create-a-project step, which writes the file -
        // in one message box, before the tray copies itself or starts anything.
        var localAppDataForPool = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (TrayApp.NoPoolFileMessage(parsed, localAppDataForPool, File.Exists) is { } noPool)
        {
            MessageBox.Show(noPool, "Switchyard", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        // INS-22 (1264-B): a pool or repository run by another kit folder's Switchyard is not this tray's to attach
        // to, stop or start: say whose it is in one message box, before the tray copies itself or starts anything.
        if (TrayApp.ForeignSchedulerMessage(parsed, localAppDataForPool, schedulerDir, AttachedScheduler.EventExists, AttachedScheduler.RunningExes("scheduler-next")) is { } foreign)
        {
            MessageBox.Show(foreign, "Switchyard", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        // 967-B: the tray runs from its own copy in %LOCALAPPDATA%\ProjectToolkit\tray, so the published
        // folder holds no running image of the tray and "Update and restart" can move every file in it.
        // The copy is started with the same arguments and told where the scheduler is.
        if (parsed.SchedulerDir.Length == 0 && TraySelfCopy.NeedsCopy(here, schedulerDir) && RunFromCopy(TraySelfCopy.Folder(schedulerDir), schedulerDir, args))
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

    // INS-19: end the trays and the pool's scheduler, wait for workers' exe calls, say it in one line.
    private static int RunStopForUpdate(TrayArguments parsed, string schedulerDir)
    {
        KitProcesses.UseParentConsole();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        using var kit = new KitProcesses(Path.Combine(schedulerDir, "scheduler-next.exe"), AttachedScheduler.PoolFileOf(parsed, localAppData));
        var (code, line) = new StopForUpdate(kit).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine(line);
        return code;
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
