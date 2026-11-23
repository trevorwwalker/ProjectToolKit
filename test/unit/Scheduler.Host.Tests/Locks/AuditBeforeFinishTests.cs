using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Tick;

namespace Scheduler.Host.Tests.Locks;

/// <summary>
/// 1025-B, design <c>dynamic-locks.md</c> DL-19: a worker that skips the hook, pushes a file its card does not
/// hold and asks To: done in the same session lands in review/ in that finish, because the finish fetches and
/// audits main before it applies the moves. With locks off the finish is as before.
/// </summary>
[ParallelMethods]
public sealed class AuditBeforeFinishTests
{
    private const string CardText = "# 010-B: x\n\nLane: B\nStatus: ready\nScope: src/held/\n";

    // The fake worker: commits src/other/b.txt (outside its scope) past the hook, pushes it, then asks To: done.
    private const string Strays =
        "echo ARGS: %*\r\n"
        + "mkdir src\\other 2>nul\r\n"
        + "echo two> src\\other\\b.txt\r\n"
        + "git add src/other/b.txt\r\n"
        + "git -c user.name=w -c user.email=w@w -c commit.gpgsign=false commit -q --no-verify -m \"stray change\" -m \"Card: 010-B-x\"\r\n"
        + "git -c user.name=w -c user.email=w@w pull -q --rebase origin main\r\n"
        + "git push -q --no-verify origin HEAD:main\r\n"
        + "(echo Card: 010-B-x& echo To: done& echo Slot: 1& echo ---& type jobs\\active\\010-B-x.md& echo - all done by request) > \"{LOGDIR}\\moves\\20991231-235959-999-slot1.move.tmp\"\r\n"
        + "move /y \"{LOGDIR}\\moves\\20991231-235959-999-slot1.move.tmp\" \"{LOGDIR}\\moves\\20991231-235959-999-slot1.move\" >nul\r\n"
        + "echo LANE-RESULT: done\r\n";

    private static Dictionary<string, string> Cards => new()
    {
        ["jobs/queue/010-B-x.md"] = CardText,
        ["src/held/a.txt"] = "1\n",
    };

    [Fact]
    public void Finish_LocksOn_StrayPushedAndToDone_LandsInReviewInTheSameFinish() => LandsInReview(Strays);

    // The same worker, still running a few seconds after its To: done, so the request is applied before the
    // run's finish: by the card read's audit, or by the planner step's pass, which audits main first too when
    // the request appeared after that tick's card read.
    [Fact]
    public void PlannerStep_LocksOn_StrayPushedAndToDoneWhileRunning_LandsInReview() =>
        LandsInReview(Strays.Replace("echo LANE-RESULT: done\r\n", "ping -n 8 127.0.0.1 >nul\r\necho LANE-RESULT: done\r\n", StringComparison.Ordinal));

    private static void LandsInReview(string worker)
    {
        using var rig = new TickRig(worker, cards: Cards, locks: true);
        Assert.True(rig.TickUntilSettled(() => rig.Composition.Tick.Running.Count == 0
            && (rig.RemoteHas("jobs/review/010-B-x.md") || rig.RemoteHas("jobs/done/010-B-x.md"))), string.Join("\n", rig.Log.Lines));

        Assert.True(rig.RemoteHas("src/other/b.txt"), "the worker's stray commit is on main");
        Assert.True(rig.RemoteHas("jobs/review/010-B-x.md"), string.Join("\n", rig.Log.Lines));
        Assert.False(rig.RemoteHas("jobs/done/010-B-x.md"));
        var text = GitFixture.Git(rig.Fx.Bare, "show", "main:jobs/review/010-B-x.md");
        Assert.Contains("changed src/other/b.txt without its lock", text, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Lines, l => l.Contains("locks: stray ", StringComparison.Ordinal) && l.Contains("of 010-B-x", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Lines, l => l.Contains("card move from slot 1: 010-B-x active/ -> review/", StringComparison.Ordinal));
    }

    [Fact]
    public void Finish_LocksOff_SameWorker_FinishesDoneWithNoAudit()
    {
        using var rig = new TickRig(Strays, cards: Cards);
        Assert.True(rig.TickUntilSettled(() => rig.Composition.Tick.Running.Count == 0 && rig.RemoteHas("jobs/done/010-B-x.md")), string.Join("\n", rig.Log.Lines));

        Assert.False(rig.RemoteHas("jobs/review/010-B-x.md"));
        Assert.DoesNotContain(rig.Log.Lines, l => l.StartsWith("locks: ", StringComparison.Ordinal) || l.Contains(" locks: ", StringComparison.Ordinal));
        Assert.Null(rig.Composition.Host.Audit);
    }
}
