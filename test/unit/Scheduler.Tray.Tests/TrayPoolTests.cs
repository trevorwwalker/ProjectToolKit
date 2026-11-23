namespace Scheduler.Tray.Tests;

/// <summary>Card 1000-B: the tray in pool mode (design multi-project.md MP-H9).</summary>
public class TrayPoolTests
{
    // MP-H9: -Pool [<file>] starts the pool instead of a repository; the scheduler gets every argument.
    [Fact]
    public void MP_H9_TrayPoolArguments_PoolWithAndWithoutFile()
    {
        var withFile = TrayArguments.Parse(["-Pool", @"D:\p\pool.json", "-Slots", "3"]);
        var without = TrayArguments.Parse(["-pool"]);
        var beforeSwitch = TrayArguments.Parse(["-Pool", "-Paused"]);

        Assert.True(withFile.Ok);
        Assert.True(withFile.Pool);
        Assert.Equal(@"D:\p\pool.json", withFile.PoolFile);
        Assert.Equal("", withFile.Repository);
        Assert.Equal(["-Pool", @"D:\p\pool.json", "-Slots", "3", TrayArguments.NoBrowserSwitch], withFile.SchedulerArgs);
        Assert.Equal("pool", withFile.ProjectGuess());

        Assert.True(without.Ok);
        Assert.Equal("", without.PoolFile);
        Assert.Equal(["-pool", TrayArguments.NoBrowserSwitch], without.SchedulerArgs);

        Assert.Equal("", beforeSwitch.PoolFile); // a switch after -Pool is not the file
    }

    // MP-C9: a repository start is as it was, and the tray's own -SchedulerDir is taken out of a pool's arguments too.
    [Fact]
    public void MP_C9_RepositoryStart_NotAPool_AndSchedulerDirTakenOut()
    {
        var repo = TrayArguments.Parse([@"C:\Projects\Game"]);
        var pool = TrayArguments.Parse(["-Pool", @"-SchedulerDir:D:\out"]);

        Assert.False(repo.Pool);
        Assert.Equal("Game", repo.ProjectGuess());
        Assert.Equal(@"D:\out", pool.SchedulerDir);
        Assert.Equal(["-Pool", TrayArguments.NoBrowserSwitch], pool.SchedulerArgs);
    }

    // MP-H9: the tooltip names "pool" and each project's state.
    [Fact]
    public void MP_H9_TrayPoolTooltip_NamesEachProjectsState()
    {
        var text = TrayTooltip.ForPool(SchedulerState.Running, 2, "last", [("AmigaX", "running"), ("ProjectToolkit", "paused-red")]);

        Assert.Equal("pool scheduler: AmigaX running, ProjectToolkit paused-red; 2 workers", text);
        Assert.Equal("pool scheduler: AmigaX running; no workers busy", TrayTooltip.ForPool(SchedulerState.Running, 0, "", [("AmigaX", "running")]));
    }

    // MP-H9: without a status yet, or not running, it reads as a scheduler for the project "pool".
    [Fact]
    public void MP_H9_TrayPoolTooltip_NoStatusOrNotRunning_ReadsAsPool()
    {
        Assert.Equal("pool scheduler: running, 1 worker", TrayTooltip.ForPool(SchedulerState.Running, 1, "", []));
        Assert.Equal("pool scheduler: stopping, waiting for the workers", TrayTooltip.ForPool(SchedulerState.Stopping, 1, "", [("A", "running")]));
        Assert.True(TrayTooltip.ForPool(SchedulerState.Running, 1, "", [("A", new string('x', 300))]).Length <= TrayTooltip.MaxLength);
    }

    // MP-H3, MP-H9: the pool's status.txt gives each project's state; a project left out has no weight and is skipped.
    [Fact]
    public void MP_H9_PoolStatusFile_ProjectsAndStates()
    {
        const string status = "pool at 2026-11-20 10:00:00: 1 of 2 worker(s) running\n"
            + "AmigaX: running, weight 70%, used 10%, 1 running, 3 next up\n"
            + "ProjectToolkit: paused-red, weight 30%, used 90%, 0 running, 0 next up\n"
            + "Gone: not in the pool: its own scheduler is running\n"
            + "type w: 1 of 2 running (AmigaX 1)\n";

        Assert.Equal([("AmigaX", "running"), ("ProjectToolkit", "paused-red")], PoolStatusFile.Projects(status));
        Assert.Empty(PoolStatusFile.Projects(""));
    }

    // MP-C3: the status file is in the pool file's logDir, else the default pool folder.
    [Fact]
    public void MP_H9_PoolStatusFile_PathFollowsLogDir()
    {
        var temp = Scheduler.TestSupport.TestTmp.NewPath("tp");
        _ = Directory.CreateDirectory(temp);
        try
        {
            var custom = Path.Combine(temp, "custom.json");
            File.WriteAllText(custom, """{ "logDir": "D:/poollogs", "projects": [] }""");
            var plain = Path.Combine(temp, "plain.json");
            File.WriteAllText(plain, """{ "projects": [] }""");

            Assert.Equal(Path.Combine("D:/poollogs", "status.txt"), PoolStatusFile.PathFor(custom, temp));
            Assert.Equal(Path.Combine(temp, "ProjectToolkit", "pool", "status.txt"), PoolStatusFile.PathFor(plain, temp));
            Assert.Equal(Path.Combine(temp, "ProjectToolkit", "pool", "status.txt"), PoolStatusFile.PathFor(Path.Combine(temp, "missing.json"), temp));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
