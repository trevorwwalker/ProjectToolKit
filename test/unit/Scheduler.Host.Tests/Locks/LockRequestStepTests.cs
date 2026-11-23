using Scheduler.Core.Locks;

namespace Scheduler.Host.Tests.Locks;

/// <summary>
/// 1020-B (design <c>dynamic-locks.md</c>, section 6): the host's lock step reads <c>.lock</c> files in name
/// order, answers each through the arbiter, writes the table then the reply, and files the request away.
/// </summary>
public class LockRequestStepTests
{
    [Fact]
    public void LockStep_FreePath_GrantedTableUpdatedRequestDone_DL4_DL5()
    {
        using var rig = new LockRig();
        var name = rig.Ask(LockRig.Old, 1, "docs/known_gaps.md");

        rig.RunStep();

        Assert.Equal("granted", rig.Reply(name));
        Assert.Contains(rig.Row(LockRig.Old).Held, e => e.Path == "docs/known_gaps.md" && e.Kind == LockKind.Granted);
        Assert.True(File.Exists(Path.Combine(rig.Moves, "done", name)));
        Assert.False(File.Exists(Path.Combine(rig.Moves, name)));
        Assert.Contains(rig.Log.Lines, l => l.Contains("locks: 100-A-old", StringComparison.Ordinal) && l.Contains("granted", StringComparison.Ordinal));
    }

    [Fact]
    public void LockStep_SlotRunsAnotherCard_RefusedAndFailed_DL22()
    {
        using var rig = new LockRig();
        var name = rig.Ask(LockRig.Old, 2, "docs/known_gaps.md");   // slot 2 runs the young card

        rig.RunStep();

        Assert.StartsWith("refused: unknown card: slot 2 runs 200-B-young", rig.Reply(name), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(rig.Moves, "failed", name)));
        Assert.True(File.Exists(Path.Combine(rig.Moves, "failed", Path.ChangeExtension(name, ".reason.txt"))));
        Assert.DoesNotContain(rig.Row(LockRig.Old).Held, e => e.Path == "docs/known_gaps.md");
    }

    [Fact]
    public void LockStep_YoungerAsksHeldPath_Yield_DL12()
    {
        using var rig = new LockRig();
        var name = rig.Ask(LockRig.Young, 2, "src/a/x.cs");

        rig.RunStep();

        Assert.Equal("yield: src/a/x.cs held by 100-A-old (older)", rig.Reply(name));
        Assert.Contains("src/a/x.cs", rig.Row(LockRig.Young).Remembered);
    }

    [Fact]
    public void LockStep_OlderWaits_GrantedOnTheSameReplyWhenTheHolderIsReleased_DL12_DL8()
    {
        using var rig = new LockRig();
        var name = rig.Ask(LockRig.Old, 1, "src/b/x.cs");

        rig.RunStep();
        Assert.StartsWith("waiting: held by 200-B-young, gives up at ", rig.Reply(name), StringComparison.Ordinal);
        Assert.NotNull(rig.Row(LockRig.Old).Open);

        rig.Running.Remove(LockRig.Young);
        rig.Releases.RunEnded(LockRig.Young, "done");
        rig.RunStep();

        Assert.Equal("granted", rig.Reply(name));
        Assert.Null(rig.Row(LockRig.Old).Open);
        Assert.Contains(rig.Row(LockRig.Old).Held, e => e.Path == "src/b/x.cs");
    }

    [Fact]
    public void LockStep_WaitLimitPasses_Yield_DL13()
    {
        using var rig = new LockRig();
        var name = rig.Ask(LockRig.Old, 1, "src/b/x.cs");
        rig.RunStep();

        rig.Clock.Advance(TimeSpan.FromMinutes(21));
        rig.RunStep();

        Assert.Equal("yield: src/b/x.cs held by 200-B-young (wait limit)", rig.Reply(name));
        Assert.Null(rig.Row(LockRig.Old).Open);
    }

    [Fact]
    public void LockStep_SecondRequestOfAWaitingCard_MergedAndBothReplied()
    {
        using var rig = new LockRig();
        var first = rig.Ask(LockRig.Old, 1, "src/b/x.cs");
        rig.RunStep();
        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        var second = rig.Ask(LockRig.Old, 1, "src/b/y.cs");

        rig.RunStep();

        Assert.StartsWith("waiting:", rig.Reply(first), StringComparison.Ordinal);
        Assert.StartsWith("waiting:", rig.Reply(second), StringComparison.Ordinal);
        Assert.Equal(first, rig.Row(LockRig.Old).Open!.FileName);
        Assert.Contains("src/b/y.cs", rig.Row(LockRig.Old).Open!.Paths);
    }

    [Fact]
    public void LockStep_NothingAsked_TableUntouched()
    {
        using var rig = new LockRig();
        var before = File.GetLastWriteTimeUtc(rig.Table.Path);

        Assert.Empty(rig.RunStep());
        Assert.Equal(before, File.GetLastWriteTimeUtc(rig.Table.Path));
        Assert.False(Directory.Exists(rig.Step.Replies));
    }
}
