// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Core.Contracts;
using Scheduler.Host.Pool;
using Scheduler.Host.Processes;
using Scheduler.Host.Slots;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.Tick;
using Xunit;
using static Scheduler.Host.Tests.Pool.PoolCompositionTests;

namespace Scheduler.Host.Tests.Pool;

/// <summary>
/// Card 1029-B: each project of the pool file has its own <c>workspaces</c> (slot1..slotN); at most that many of
/// its workers run at once, and the pool's maxWorkers still bounds the total.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[ParallelMethods]
public sealed class PoolWorkspacesTests
{
    private static PoolConfig Parse(string workspaces) =>
        PoolConfig.Parse($$"""{ "projects": [ { "name": "A", "repo": "C:/a", "weight": 70{{workspaces}} } ] }""");

    // Read and validated like weight: 1..64, a number in quotes counts; missing is null (the pool's maxWorkers);
    // anything else is logged and null.
    [Fact]
    public void Workspaces_ReadAndValidated()
    {
        Assert.Null(Parse("").Projects[0].Workspaces);
        Assert.Empty(Parse("").Skipped);
        Assert.Equal(3, Parse(""", "workspaces": 3""").Projects[0].Workspaces);
        Assert.Equal(4, Parse(""", "workspaces": "4" """).Projects[0].Workspaces);
        Assert.Equal(64, Parse(""", "workspaces": 64""").Projects[0].Workspaces);
        Assert.Equal(1, Parse(""", "workspaces": 1""").Projects[0].Workspaces);
        foreach (var bad in new[] { "0", "65", "-2", "2.5", "\"many\"", "true" })
        {
            var config = Parse($", \"workspaces\": {bad}");
            Assert.Null(config.Projects[0].Workspaces);
            Assert.Equal(["A"], config.Projects.Select(p => p.Name));   // the project is kept
            Assert.Contains(config.Skipped, l => l.Contains("project A has workspaces", StringComparison.Ordinal)
                && l.Contains("the pool's maxWorkers is used", StringComparison.Ordinal));
        }
    }

    // MP-C7: the Worker types panel's save keeps each project's workspaces, as it keeps its weight.
    [Fact]
    public void Workspaces_RoundTripThroughWritePool()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "pool.json");
        File.WriteAllText(path, """{ "projects": [ { "name": "A", "repo": "C:/a", "weight": 70, "workspaces": 2 }, { "name": "B", "repo": "C:/b" } ] }""");
        var file = new PoolConfigFile(path, new ListLog());
        Assert.True(file.Read());
        Assert.Equal(2, file.Current!.Projects[0].Workspaces);

        Assert.True(file.WritePool("""{ "maxWorkers": 3, "types": { "w": { "max": 3 } } }"""));
        var again = new PoolConfigFile(path, new ListLog());
        Assert.True(again.Read());
        Assert.Equal([(70, (int?)2), (70, null)], again.Current!.Projects.Select(p => (p.Weight, p.Workspaces)));
    }

    // The lowest free workspace is taken, whatever the type; a folder held above the count is not in the way.
    [Fact]
    public void Workspaces_LowestFreeChosen()
    {
        Assert.Equal(1, SlotWorktrees.FreeFolder([], 2));
        Assert.Equal(2, SlotWorktrees.FreeFolder([1, 5], 2));
        Assert.Null(SlotWorktrees.FreeFolder([1, 2], 2));
    }

    // MP-H6 with workspaces lowered to 1: A's adopted worker keeps its workspace (slot2, above the count) and runs
    // on in place; A starts nothing more while it runs, and B, with workspaces left, still gets a place.
    [Fact]
    public void Workspaces_Adopted_KeepsItsWorkspace_LoweredCountLetsItFinish()
    {
        using var rig = new PoolRig();
        rig.WritePoolFile(workspacesA: "1", maxWorkers: 3);
        using var worker = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            var running = new RunningWorker
            {
                Slot = 2,
                Pid = worker.Id,
                Start = new DateTimeOffset(worker.StartTime),
                Card = "005-E-other",
                Category = "E",
                Id = "005-E",
                Type = "w",
            };
            Directory.CreateDirectory(rig.LogDirA);
            File.WriteAllText(Path.Combine(rig.LogDirA, "running.json"), RunningJson.Write([running]));
            using var pool = rig.Compose();
            pool.Runner.Run();

            var a = pool.Tick.Projects.Single(p => p.Name == rig.A);
            Assert.Equal(1, a.Workspaces);
            Assert.Equal([(worker.Id, 2)], a.Tick.Busy().Select(w => (w.Pid, w.Slot)));
            Assert.DoesNotContain(rig.Log.Lines, l => l.Contains($"DRY RUN {rig.A}", StringComparison.Ordinal));
            Assert.Contains(rig.Log.Lines, l => l.Contains($"DRY RUN {rig.B}", StringComparison.Ordinal));
            var view = pool.Tick.Snapshot().Projects.Single(p => p.Name == rig.A);
            Assert.Equal((1, 1), (view.Running, view.Workspaces));
            Assert.Equal(3, pool.Tick.Snapshot().Projects.Single(p => p.Name == rig.B).Workspaces);   // missing = maxWorkers
        }
        finally
        {
            worker.Kill(entireProcessTree: true);
        }
    }

    // 1012-B: the startup block lists the project's workspaces.
    [Fact]
    public void Workspaces_InTheStartupLine()
    {
        var line = StartupBlock.Project(new SchedulerConfig { Project = "A", Repo = "C:/a", WorkspaceRoot = @"C:\ws\A" }, 70, planner: false, workspaces: 3);
        Assert.Contains(@"workspaces 3 (slot1..slot3) in C:\ws\A,", line, StringComparison.Ordinal);
    }
}
