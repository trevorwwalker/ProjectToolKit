using Scheduler.Core.Launch;
using Scheduler.Host.Slots;

namespace Scheduler.Host.Tests.Slots;

/// <summary>Worktree folders under worker types (WT-W1 to WT-W3). Test plan: worker-types.md.</summary>
public class FolderTests
{
    // WT-W1, WT-W2: the lowest folder no worker holds is taken, whatever type is starting.
    [Fact]
    public void Folders_LowestFreeFolderTaken_AnyType()
    {
        Assert.Equal(1, SlotWorktrees.FreeFolder([], 4));
        Assert.Equal(2, SlotWorktrees.FreeFolder([1, 3], 4));
        Assert.Equal(4, SlotWorktrees.FreeFolder([1, 2, 3], 4));
        Assert.Null(SlotWorktrees.FreeFolder([1, 2, 3, 4], 4));
        // WT-W5: a worker kept on a folder above maxWorkers does not take one of the others.
        Assert.Equal(1, SlotWorktrees.FreeFolder([5], 4));
        Assert.Equal(3, SlotWorktrees.FreeFolder([1, 2, 5], 4));
    }

    // WT-W2: an abandoned worker (TIME-11) leaves the running set, and its folder is the next one taken.
    [Fact]
    public void Folders_AbandonedWorkerFreesFolder()
    {
        var running = new HashSet<int> { 1, 2, 3 };
        Assert.Equal(4, SlotWorktrees.FreeFolder(running, 4));

        running.Remove(2);

        Assert.Equal(2, SlotWorktrees.FreeFolder(running, 4));
    }

    // WT-W3: the folder number is the worker's number, so its flag files keep the names they have today.
    [Fact]
    public void Folders_StopAndWarnFilesKeepSlotNumber()
    {
        var logs = Path.Combine("logs", "x");

        Assert.Equal(Path.Combine(logs, "stop-slot3"), WorkerFiles.StopFlag(logs, 3));
        Assert.Equal(Path.Combine(logs, "warn-slot3"), WorkerFiles.WarnFlag(logs, 3));
        Assert.Equal(Path.Combine("worktrees", "slot3"), new SlotWorktrees("repo", "worktrees", "origin", "main").SlotDirectory(3));
    }
}
