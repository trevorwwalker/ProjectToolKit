using Scheduler.Core.Locks;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Tests.Locks;

/// <summary>
/// Dynamic locks, the audit of main (design <c>docs/design/tdd/scheduler/dynamic-locks.md</c>, card 1017-A):
/// section 7's Audit (DL-19), the audit half; the stray-to-review half is in Moves/MovesStrayTests. Pure.
/// </summary>
public class MainAuditTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] AddOnly = ["docs/known_gaps.md"];

    private static LockTable Table() =>
        LockTable.Empty("C:/repo")
            .Start("990-B-x", new CardAge(1, 990), Scope.Of("code/src/Scheduler.Host/"), T0)
            .Grant("990-B-x", ["docs/design/x.md"], T0.AddMinutes(30));

    private static MainAuditCommit Commit(string sha, string card, params string[] paths) =>
        new() { Sha = sha, Card = card, Paths = [.. paths.Select(p => new PushCheckPath(p))] };

    [Fact]
    public void DL19_TrailerAndHeldPaths_IsClean_AndTheAuditMovesOn()
    {
        var r = MainAudit.Check(Table(), [Commit("a1", "990-B-x", "code/src/Scheduler.Host/Program.cs", "jobs/active/990-B-x.md", "docs/known_gaps.md")], AddOnly);
        Assert.Empty(r.Strays);
        Assert.Equal("a1", r.Table.AuditedCommit);
    }

    [Fact]
    public void DL19_UnheldPath_IsAStray_NamingCardAndPath()
    {
        var r = MainAudit.Check(Table(), [Commit("a1", "990-B-x", "code/src/Scheduler.Host/Program.cs", "code/src/Scheduler.Core/Tick/SchedulerTick.cs")], AddOnly);
        var s = Assert.Single(r.Strays);
        Assert.Equal(("a1", "990-B-x", "code/src/Scheduler.Core/Tick/SchedulerTick.cs"), (s.Sha, s.Card, s.Path));
        Assert.Equal(["code/src/Scheduler.Core/Tick/SchedulerTick.cs"], r.StrayPaths("990-B-x"));
    }

    [Fact]
    public void DL19_HeldSetAsItWasWhenTheCommitLanded()
    {
        var before = Commit("a1", "990-B-x", "docs/design/x.md") with { Landed = T0.AddMinutes(10) };
        var after = Commit("a2", "990-B-x", "docs/design/x.md") with { Landed = T0.AddMinutes(40) };
        var r = MainAudit.Check(Table(), [before, after], AddOnly);
        var s = Assert.Single(r.Strays);
        Assert.Equal("a1", s.Sha);
        Assert.Equal("a2", r.Table.AuditedCommit);
    }

    [Fact]
    public void DL19_NoTrailerWhileWorkersRun_IsAStray()
    {
        var r = MainAudit.Check(Table(), [Commit("a1", "", "README.md")], AddOnly);
        var s = Assert.Single(r.Strays);
        Assert.Equal(("a1", ""), (s.Sha, s.Card));
        Assert.Contains("no Card trailer", s.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DL19_NoTrailerWithNoWorkerRunning_IsClean()
    {
        Assert.Empty(MainAudit.Check(LockTable.Empty("C:/repo"), [Commit("a1", "", "README.md")], AddOnly).Strays);
        var landedBefore = Commit("a2", "", "README.md") with { Landed = T0.AddMinutes(-5) };
        Assert.Empty(MainAudit.Check(Table(), [landedBefore], AddOnly).Strays);
    }

    [Fact]
    public void DL19_SchedulersOwnCommits_AreIgnored()
    {
        var own = Commit("a1", "", "jobs/done/990-B-x.md", "jobs/active/990-B-x.md") with { Own = true };
        var r = MainAudit.Check(Table(), [own], AddOnly);
        Assert.Empty(r.Strays);
        Assert.Equal("a1", r.Table.AuditedCommit);
    }

    [Fact]
    public void DL19_CardNotHoldingAnything_ChangesAreStrays()
    {
        var r = MainAudit.Check(Table(), [Commit("a1", "991-A-y", "code/src/a.cs", "jobs/active/991-A-y.md")], AddOnly);
        var s = Assert.Single(r.Strays);
        Assert.Equal("code/src/a.cs", s.Path);
    }

    [Fact]
    public void DL19_JobsPaths_HeldOrdinaryIsClean_ProtectedIsAStray()
    {
        var table = LockTable.Empty("C:/repo").Start("990-B-x", new CardAge(1, 990), Scope.Of("jobs/card-template.md", "jobs/attachments/", "jobs/plans/"), T0);
        var r = MainAudit.Check(table, [Commit("a1", "990-B-x", "jobs/card-template.md", "jobs/attachments/x.md", "jobs/plans/x.md", "jobs/.logs/x", "jobs/queue/991-A-y.md", "jobs/active/990-B-x.md", "jobs/README.md")], AddOnly);
        Assert.Equal(["jobs/plans/x.md", "jobs/.logs/x", "jobs/queue/991-A-y.md", "jobs/README.md"], r.Strays.Select(s => s.Path).ToArray());
    }

    [Fact]
    public void DL19_NoCommits_KeepsTheAuditedCommit()
    {
        var t = Table() with { AuditedCommit = "zz" };
        Assert.Equal("zz", MainAudit.Check(t, [], AddOnly).Table.AuditedCommit);
    }
}
