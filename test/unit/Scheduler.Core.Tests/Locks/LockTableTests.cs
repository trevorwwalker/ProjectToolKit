// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Locks;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Tests.Locks;

/// <summary>
/// Dynamic locks, the table (design <c>docs/design/tdd/scheduler/dynamic-locks.md</c>, card 1016-A):
/// section 7's Release, table half (DL-1, DL-8, DL-9, DL-10), Table (DL-20, DL-21, with 1018-D's
/// amendment for a claim that had not landed) and the lock request format (section 3). Pure.
/// </summary>
public class LockTableTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static readonly LockRules Rules = new() { WaitLimit = TimeSpan.FromMinutes(20) };

    private static LockRequest Ask(string card, params string[] paths) =>
        new() { FileName = "20261121-100000-000-slot1.lock", Card = card, Paths = paths, Why = "test" };

    private static LockTable TwoCards() =>
        LockTable.Empty("C:/repo")
            .Start("990-B-x", new CardAge(1, 990), Scope.Of("docs/a.md"), T0)
            .Start("991-A-y", new CardAge(1, 991), Scope.Of("docs/b.md"), T0);

    // ---- Release (DL-8, DL-9, DL-10)

    [Fact]
    public void FinishApplied_Releases_AndWaitingRequestIsGrantedInTheSameTick()
    {
        var t = LockArbiter.Ask(TwoCards(), Ask("990-B-x", "docs/b.md"), T0, Rules).Table;
        t = t.OnMove("991-A-y", CardFolder.Done, applied: true);
        Assert.Null(t.Find("991-A-y"));
        var (after, decisions) = LockArbiter.Recheck(t, T0.AddMinutes(3), Rules);
        var d = Assert.Single(decisions);
        Assert.Equal(("990-B-x", LockAnswer.Granted), (d.Card, d.Answer));
        Assert.Equal("20261121-100000-000-slot1.lock", d.FileName);
        Assert.Null(after.Find("990-B-x")!.Open);
        Assert.Contains(after.Find("990-B-x")!.Held, e => e.Path == "docs/b.md");
    }

    [Fact]
    public void FinishNotOnMain_KeepsLocks()
    {
        var t = TwoCards();
        Assert.Same(t, t.OnMove("991-A-y", CardFolder.Done, applied: false));
        Assert.Same(t, t.OnMove("991-A-y", CardFolder.Review, applied: false));
    }

    [Theory]
    [InlineData(CardFolder.Active)]
    [InlineData(CardFolder.Queue)]
    public void ParkOrSendBack_Releases_AndRemembersGrantedEntries(CardFolder to)
    {
        var t = LockArbiter.Ask(TwoCards(), Ask("991-A-y", "docs/c.md"), T0, Rules).Table;
        var c = t.OnMove("991-A-y", to, applied: true).Find("991-A-y")!;
        Assert.Equal(CardLockState.Released, c.State);
        Assert.Empty(c.Held);
        Assert.Equal(["docs/c.md"], c.Remembered);
    }

    [Theory]
    [InlineData(LockRelease.Ended)]
    [InlineData(LockRelease.Yielded)]
    public void FailOrKill_Releases_AndRemembersGrantedEntries(LockRelease why)
    {
        var t = LockArbiter.Ask(TwoCards(), Ask("991-A-y", "docs/c.md"), T0, Rules).Table;
        var c = t.Release("991-A-y", why).Find("991-A-y")!;
        Assert.False(c.Holds);
        Assert.Null(c.Open);
        Assert.Equal(["docs/c.md"], c.Remembered);
    }

    [Fact]
    public void ReleasedCard_DoesNotBlockOthers()
    {
        var t = TwoCards().Release("991-A-y", LockRelease.Ended);
        Assert.Equal(LockAnswer.Granted, LockArbiter.Ask(t, Ask("990-B-x", "docs/b.md"), T0, Rules).Answer);
    }

    [Fact]
    public void Resume_InitialClaimIncludesRememberedSet_AndKeepsItAcrossAnotherRun()
    {
        var t = LockArbiter.Ask(TwoCards(), Ask("991-A-y", "docs/c.md"), T0, Rules).Table.Release("991-A-y", LockRelease.Ended);
        t = t.Start("991-A-y", new CardAge(1, 991), Scope.Of("docs/b.md"), T0.AddHours(1));
        var c = t.Find("991-A-y")!;
        Assert.Equal(CardLockState.Running, c.State);
        Assert.Empty(c.Remembered);
        Assert.Contains(c.Held, e => e is { Path: "docs/c.md", Kind: LockKind.Remembered });
        Assert.Contains(c.Held, e => e is { Path: "docs/b.md", Kind: LockKind.Claim });
        Assert.Equal(["docs/c.md"], t.Release("991-A-y", LockRelease.Ended).Find("991-A-y")!.Remembered);
    }

    [Fact]
    public void FinishedCard_DropsRememberedSet()
    {
        var t = LockArbiter.Ask(TwoCards(), Ask("991-A-y", "docs/c.md"), T0, Rules).Table;
        Assert.Null(t.OnMove("991-A-y", CardFolder.Review, applied: true).Find("991-A-y"));
    }

    [Fact]
    public void Held_IsTheHeldSetAsAScope()
    {
        var t = LockTable.Empty("C:/repo").Start("990-B-x", new CardAge(1, 990), new Scope(["code/"], ["code/x/"]) { AddOnly = ["code/a.md"] }, T0);
        var s = t.Find("990-B-x")!.HeldScope;
        Assert.Equal(["code/"], s.Paths);
        Assert.Equal(["code/x/"], s.Except);
        Assert.Equal(["code/a.md"], s.AddOnly);
    }

    // ---- Table (DL-20, DL-21)

    private static LockTable Busy()
    {
        var t = LockTable.Empty("C:/Projects/Repo")
            .Start("990-B-x", new CardAge(0, 990), new Scope(["code/"], ["code/x/"]) { AddOnly = ["code/a.md"] }, T0, project: "P1", slot: 2)
            .Start("991-A-y", new CardAge(1, 991), Scope.Of("docs/b.md"), T0, project: "P2", slot: 3)
            .Start("993-C-w", new CardAge(2, 993), Scope.Of("site/"), T0)
            .Starting("992-C-z", slot: 4);
        t = t.Grant("993-C-w", ["site2/"], T0).Release("993-C-w", LockRelease.Ended).Starting("993-C-w", slot: 5);
        t = LockArbiter.Ask(t, Ask("991-A-y", "docs/c.md"), T0.AddMinutes(1), Rules).Table;
        t = LockArbiter.Ask(t, Ask("990-B-x", "docs/b.md"), T0.AddMinutes(2), Rules).Table;
        return t.Release("991-A-y", LockRelease.Ended).Start("991-A-y", new CardAge(1, 991), Scope.Of("docs/b.md"), T0.AddMinutes(3)) with { AuditedCommit = "abc123" };
    }

    [Fact]
    public void RoundTrip()
    {
        var t = Busy();
        var json = LockTableText.Write(t);
        var (back, error) = LockTableText.Read(json);
        Assert.Equal("", error);
        Assert.Equal(json, LockTableText.Write(back));
        Assert.Equal("abc123", back.AuditedCommit);
        Assert.Equal("C:/Projects/Repo", back.Repo);
        var x = back.Find("990-B-x")!;
        Assert.Equal(new CardAge(0, 990), x.Age);
        Assert.Equal(("P1", 2), (x.Project, x.Slot));
        Assert.Equal(T0.AddMinutes(22), x.Open!.Deadline);
        Assert.Equal(["docs/b.md"], x.Open.Paths);
        Assert.Equal(["code/x/"], x.Except);
        Assert.Equal(CardLockState.Starting, back.Find("992-C-z")!.State);
        Assert.Contains(back.Find("991-A-y")!.Held, e => e is { Path: "docs/c.md", Kind: LockKind.Remembered });
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"cards\":[{\"card\":\"\"}]}")]
    [InlineData("{\"cards\":[{\"card\":\"990-B-x\",\"state\":\"flying\"}]}")]
    public void Unreadable_StartsEmpty_WithALogLine(string json)
    {
        var (t, error) = LockTableText.Read(json, "C:/repo");
        Assert.Empty(t.Cards);
        Assert.Equal("C:/repo", t.Repo);
        Assert.Contains("starting empty", error);
    }

    [Fact]
    public void Missing_StartsEmpty_WithALogLine()
    {
        var (t, error) = LockTableText.Read(null, "C:/repo");
        Assert.Empty(t.Cards);
        Assert.Contains("starting empty", error);
    }

    [Fact]
    public void Restart_KeepsAdoptedCardsLocksAndDeadlines_ReleasesTheRest()
    {
        var t = Busy();
        var (after, log) = LockTableText.Restart(t, ["990-B-x", "992-C-z"]);
        var x = after.Find("990-B-x")!;
        Assert.Equal(CardLockState.Running, x.State);
        Assert.Equal(T0.AddMinutes(22), x.Open!.Deadline);
        var y = after.Find("991-A-y")!;
        Assert.False(y.Holds);
        Assert.Equal(["docs/c.md"], y.Remembered);
        Assert.Null(after.Find("992-C-z"));
        Assert.Contains(log, l => l.Contains("992-C-z", StringComparison.Ordinal) && l.Contains("not landed", StringComparison.Ordinal));
        var w = after.Find("993-C-w")!;
        Assert.Equal((CardLockState.Released, (CardAge?)new CardAge(2, 993)), (w.State, w.Age));
        Assert.Equal(["site2/"], w.Remembered);
        Assert.Equal("abc123", after.AuditedCommit);
    }

    [Fact]
    public void FileName_IsPerRepository_AndStable()
    {
        Assert.Equal(LockTableText.FileName(@"C:\Projects\Repo\"), LockTableText.FileName("c:/projects/repo"));
        Assert.EndsWith(".json", LockTableText.FileName("C:/Projects/Repo"), StringComparison.Ordinal);
        Assert.DoesNotContain(':', LockTableText.FileName("C:/Projects/Repo"));
        Assert.Equal(System.IO.Path.Combine("L", "locks", LockTableText.FileName("C:/r")), LockTableText.PathIn("L", "C:/r"));
    }

    // ---- Lock requests (section 3)

    [Fact]
    public void Request_RoundTrip()
    {
        var name = LockRequests.FileName(T0, 3);
        Assert.Equal("20261121-100000-000-slot3.lock", name);
        var r = LockRequests.Parse(name, LockRequests.Text("1016-A-dl-lock-table", 3, ["docs/a.md", "code/x/"], "need the gap list"));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(("1016-A-dl-lock-table", (int?)3, "need the gap list"), (r.Card, r.Slot, r.Why));
        Assert.Equal(["docs/a.md", "code/x/"], r.Paths);
        Assert.Equal("20261121-100000-000-slot3.reply", LockRequests.ReplyName(name));
    }

    [Fact]
    public void Request_ParsesLooseForms()
    {
        var r = LockRequests.Parse("n.lock", "\uFEFFCard: 990-B-x.md\r\nSlot: slot2\r\nPaths: docs\\a.md\r\n  - docs/b.md\r\n\r\nWhy: because\r\n");
        Assert.True(r.Ok, r.Error);
        Assert.Equal("990-B-x", r.Card);
        Assert.Equal(2, r.Slot);
        Assert.Equal(["docs/a.md", "docs/b.md"], r.Paths);
    }

    [Theory]
    [InlineData("Slot: 1\nPaths:\n  docs/a.md\n", "no Card: line")]
    [InlineData("Card: 990-B-x\nWhy: x\n", "no paths")]
    public void Request_Unreadable(string text, string error)
    {
        var r = LockRequests.Parse("n.lock", text);
        Assert.False(r.Ok);
        Assert.Contains(error, r.Error);
        var d = LockArbiter.Ask(TwoCards(), r, T0, Rules);
        Assert.Equal(LockAnswer.Refused, d.Answer);
    }

    [Fact]
    public void Request_Order_AndSlotCheck()
    {
        Assert.Equal(["a-slot1.lock", "b-slot2.lock"], LockRequests.Order(["b-slot2.lock", "x.move", "a-slot1.lock"]));
        var r = LockRequests.Parse("n.lock", LockRequests.Text("990-B-x", 2, ["docs/a.md"], "w"));
        Assert.True(LockRequests.Verify(r, "990-B-x").Ok);
        var bad = LockRequests.Verify(r, "991-A-y");
        Assert.False(bad.Ok);
        Assert.Contains("991-A-y", bad.Error);
        Assert.False(LockRequests.Verify(r, null).Ok);
    }
}
