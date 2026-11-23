using Scheduler.Core.Contracts;
using Scheduler.Core.Locks;

namespace Scheduler.Host.Tests.Locks;

/// <summary>
/// 1020-B (design <c>dynamic-locks.md</c>, DL-8, DL-9, DL-10): the host half of Release - applied moves and
/// run ends release a card's held set, a finish not yet on main keeps it.
/// </summary>
public class LockReleasesTests
{
    [Fact]
    public void Release_FinishApplied_RowGoneAndWaitingRequestGranted_DL8a()
    {
        using var rig = new LockRig();
        var name = rig.Ask(LockRig.Old, 1, "src/b/x.cs");
        rig.RunStep();

        rig.Releases.MoveFiled(LockRig.Young, CardFolder.Done, applied: true);
        rig.Releases.ReleaseMoved();
        rig.RunStep();

        Assert.Null(rig.Table.Read().Find(LockRig.Young));
        Assert.Equal("granted", rig.Reply(name));
        Assert.Contains(rig.Log.Lines, l => l == "locks: 200-B-young released");
    }

    [Fact]
    public void Release_FinishNotOnMainYet_KeepsLocksUntilApplied_DL9()
    {
        using var rig = new LockRig();
        rig.Running.Remove(LockRig.Young);
        rig.PendingMoves.Add(LockRig.Young);

        rig.Releases.RunEnded(LockRig.Young, "done");
        Assert.True(rig.Row(LockRig.Young).Holds);
        Assert.Contains(rig.Log.Lines, l => l.Contains("200-B-young keeps its locks", StringComparison.Ordinal));

        rig.PendingMoves.Clear();
        rig.Releases.MoveFiled(LockRig.Young, CardFolder.Review, applied: true);
        rig.Releases.ReleaseMoved();
        Assert.Null(rig.Table.Read().Find(LockRig.Young));
    }

    [Fact]
    public void Release_ParkApplied_GrantedEntriesRemembered_DL8b_DL10()
    {
        using var rig = new LockRig();
        rig.Ask(LockRig.Young, 2, "docs/known_gaps.md");
        rig.RunStep();

        rig.Releases.MoveFiled(LockRig.Young, CardFolder.Active, applied: true);
        rig.Releases.ReleaseMoved();

        var row = rig.Row(LockRig.Young);
        Assert.False(row.Holds);
        Assert.Equal(CardLockState.Released, row.State);
        Assert.Equal(["docs/known_gaps.md"], row.Remembered);
    }

    [Fact]
    public void Release_SendBackApplied_Released_DL8b()
    {
        using var rig = new LockRig();
        rig.Releases.MoveFiled(LockRig.Young, CardFolder.Queue, applied: true);
        rig.Releases.ReleaseMoved();

        Assert.False(rig.Row(LockRig.Young).Holds);
    }

    [Theory]
    [InlineData("yielded")]
    [InlineData("failed")]
    [InlineData("time limit")]
    [InlineData("stopped")]
    public void Release_RunEndsWithoutAMove_Released_DL8c_DL8d(string result)
    {
        using var rig = new LockRig();
        rig.Running.Remove(LockRig.Young);

        rig.Releases.RunEnded(LockRig.Young, result);

        Assert.Equal(CardLockState.Released, rig.Row(LockRig.Young).State);
        Assert.True(rig.Row(LockRig.Old).Holds);
    }

    [Fact]
    public void Release_YieldedCard_RemembersWhatItAskedFor_DL12()
    {
        using var rig = new LockRig();
        rig.Ask(LockRig.Young, 2, "src/a/x.cs");
        rig.RunStep();   // yield

        rig.Running.Remove(LockRig.Young);
        rig.Releases.RunEnded(LockRig.Young, "yielded");

        Assert.Equal(["src/a/x.cs"], rig.Row(LockRig.Young).Remembered);
    }

    [Fact]
    public void Release_FailedMove_ReleasesOnlyACardNoWorkerRuns()
    {
        using var rig = new LockRig();
        rig.Releases.MoveFiled(LockRig.Young, CardFolder.Done, applied: false);
        rig.Releases.ReleaseMoved();
        Assert.True(rig.Row(LockRig.Young).Holds);   // still running: its run end releases it

        rig.Running.Remove(LockRig.Young);
        rig.Releases.MoveFiled(LockRig.Young, CardFolder.Done, applied: false);
        rig.Releases.ReleaseMoved();
        Assert.False(rig.Row(LockRig.Young).Holds);
    }
}
