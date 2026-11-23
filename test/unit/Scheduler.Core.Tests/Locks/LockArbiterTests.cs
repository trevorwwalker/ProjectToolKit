using Scheduler.Core.Locks;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Tests.Locks;

/// <summary>
/// Dynamic locks, the arbiter (design <c>docs/design/tdd/scheduler/dynamic-locks.md</c>, card 1016-A):
/// section 7's Grant (DL-4, DL-5, DL-15, DL-22), Narrowness (DL-6), Wait-die (DL-11, DL-12) and
/// Wait limit (DL-13) tests. Pure: no disk, no git.
/// </summary>
public class LockArbiterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static readonly LockRules Rules = new()
    {
        AddOnly = ["docs/known_gaps.md"],
        CategoryDefaults = [["code/src/Scheduler.Core/"], ["code/src/Scheduler.Web/", "test/unit/Scheduler.Web.Tests/"]],
        TrackedFiles = [.. Enumerable.Range(0, 250).Select(i => $"big/f{i}.cs"), "small/a.cs", "small/b.cs", "vm/68000.cpp"],
        WaitLimit = TimeSpan.FromMinutes(20),
    };

    private static LockTable Running(params (string Card, int Prio, long Num, string[] Paths)[] cards)
    {
        var t = LockTable.Empty("C:/repo");
        foreach (var (card, prio, num, paths) in cards)
        {
            t = t.Start(card, new CardAge(prio, num), Scope.Of(paths), T0);
        }

        return t;
    }

    private static LockRequest Ask(string card, params string[] paths) =>
        new() { FileName = "20261121-100000-000-slot1.lock", Card = card, Paths = paths, Why = "test" };

    // ---- Grant (DL-4, DL-5)

    [Fact]
    public void FreePath_IsGranted_AndAddedToHeldSet()
    {
        var t = Running(("990-B-x", 1, 990, ["code/src/Scheduler.Host/"]), ("991-A-y", 1, 991, ["code/src/Scheduler.Core/"]));
        var d = LockArbiter.Ask(t, Ask("990-B-x", "docs/notes.md"), T0.AddMinutes(1), Rules);
        Assert.Equal(LockAnswer.Granted, d.Answer);
        var held = d.Table.Find("990-B-x")!.Held;
        Assert.Contains(held, e => e.Path == "docs/notes.md" && e.Kind == LockKind.Granted && e.Granted == T0.AddMinutes(1));
    }

    [Fact]
    public void PathInsideOwnHeldSet_IsGranted_WithoutTableChange()
    {
        var t = Running(("990-B-x", 1, 990, ["code/src/Scheduler.Host/"]));
        var d = LockArbiter.Ask(t, Ask("990-B-x", "code/src/Scheduler.Host/Git/GitWriter.cs"), T0, Rules);
        Assert.Equal(LockAnswer.Granted, d.Answer);
        Assert.Same(t, d.Table);
    }

    [Fact]
    public void OneOfTwoEntriesHeldByOther_NeitherIsGranted()
    {
        var t = Running(("990-B-x", 1, 990, ["code/src/Scheduler.Host/"]), ("991-A-y", 1, 991, ["docs/a.md"]));
        var d = LockArbiter.Ask(t, Ask("991-A-y", "docs/free.md", "code/src/Scheduler.Host/Program.cs"), T0, Rules);
        Assert.NotEqual(LockAnswer.Granted, d.Answer);
        Assert.DoesNotContain(d.Table.Find("991-A-y")!.Held, e => e.Path == "docs/free.md");
    }

    [Fact]
    public void GlobAgainstFile_Conflicts()
    {
        var t = Running(("990-B-x", 1, 990, ["vm/68000.cpp"]), ("991-A-y", 1, 991, ["docs/a.md"]));
        var d = LockArbiter.Ask(t, Ask("991-A-y", "vm/68000.*"), T0, Rules);
        Assert.Equal(LockAnswer.Yield, d.Answer);
        Assert.Equal("990-B-x", d.Holder);
    }

    [Fact]
    public void DifferentRepositories_NeverConflict()
    {
        var mine = LockTable.Empty("C:/repo").Start("991-A-y", new CardAge(1, 991), Scope.Of("docs/a.md"), T0);
        var other = LockTable.Empty("C:/other").Start("990-B-x", new CardAge(1, 990), Scope.Of("vm/"), T0);
        var d = LockArbiter.Ask(mine, Ask("991-A-y", "vm/68000.cpp"), T0, Rules);
        Assert.Equal(LockAnswer.Granted, d.Answer);
        Assert.Equal(LockAnswer.Granted, LockArbiter.Ask(other, Ask("990-B-x", "docs/a.md"), T0, Rules).Answer);
        Assert.NotEqual(LockTableText.FileName("C:/repo"), LockTableText.FileName("C:/other"));
    }

    [Fact]
    public void AddOnlyPaths_NeverConflict()
    {
        var t = Running(("990-B-x", 1, 990, ["docs/known_gaps.md"]), ("991-A-y", 1, 991, ["docs/a.md"]));
        Assert.Equal(LockAnswer.Granted, LockArbiter.Ask(t, Ask("991-A-y", "docs/known_gaps.md"), T0, Rules).Answer);
    }

    [Fact]
    public void HolderExceptList_LeavesPathFree()
    {
        var t = LockTable.Empty()
            .Start("990-B-x", new CardAge(1, 990), new Scope(["code/src/"], ["code/src/Scheduler.Core/"]), T0)
            .Start("991-A-y", new CardAge(1, 991), Scope.Of("docs/a.md"), T0);
        Assert.Equal(LockAnswer.Granted, LockArbiter.Ask(t, Ask("991-A-y", "code/src/Scheduler.Core/Locks/LockTable.cs"), T0, Rules).Answer);
    }

    [Fact]
    public void CardNotRunning_IsRefused()
    {
        var t = Running(("990-B-x", 1, 990, ["docs/a.md"]));
        var d = LockArbiter.Ask(t, Ask("999-A-z", "docs/b.md"), T0, Rules);
        Assert.Equal(LockAnswer.Refused, d.Answer);
        Assert.Contains("999-A-z", d.Reason);
    }

    [Fact]
    public void StartingCard_HoldsNothing_HasNoAge_AndCannotAsk()
    {
        // 1018-D: until its claim lands a card is in the table as starting, with no held set and no
        // age; its scope counts at dispatch through the slot's stand-in, not here.
        var t = LockTable.Empty()
            .Starting("990-B-x", slot: 2)
            .Start("995-A-y", new CardAge(1, 995), Scope.Of("docs/b.md"), T0);
        var s = t.Find("990-B-x")!;
        Assert.Equal((CardLockState.Starting, (CardAge?)null, 2), (s.State, s.Age, s.Slot));
        Assert.Empty(s.Held);
        Assert.False(s.Holds);
        Assert.Equal(LockAnswer.Granted, LockArbiter.Ask(t, Ask("995-A-y", "docs/a.md"), T0, Rules).Answer);
        Assert.Equal(LockAnswer.Refused, LockArbiter.Ask(t, Ask("990-B-x", "docs/c.md"), T0, Rules).Answer);
    }

    [Fact]
    public void ClaimLanding_RecordsHeldSetAndAge()
    {
        var t = LockTable.Empty().Starting("990-B-x").Start("990-B-x", new CardAge(1, 990), Scope.Of("docs/a.md"), T0.AddSeconds(30));
        var c = t.Find("990-B-x")!;
        Assert.Equal((CardLockState.Running, (CardAge?)new CardAge(1, 990)), (c.State, c.Age));
        Assert.Contains(c.Held, e => e is { Path: "docs/a.md", Kind: LockKind.Claim });
    }

    [Fact]
    public void ClaimFailed_DropsTheStartingEntry_AndKeepsAnEarlierRun()
    {
        Assert.Null(LockTable.Empty().Starting("990-B-x").ClaimFailed("990-B-x").Find("990-B-x"));
        var t = LockTable.Empty().Start("990-B-x", new CardAge(0, 990), Scope.Of("docs/a.md"), T0)
            .Grant("990-B-x", ["docs/b.md"], T0).Release("990-B-x", LockRelease.Ended)
            .Starting("990-B-x");
        Assert.Equal(new CardAge(0, 990), t.Find("990-B-x")!.Age);
        var back = t.ClaimFailed("990-B-x").Find("990-B-x")!;
        Assert.Equal((CardLockState.Released, (CardAge?)new CardAge(0, 990)), (back.State, back.Age));
        Assert.Equal(["docs/b.md"], back.Remembered);
    }

    // ---- Narrowness (DL-6)

    [Theory]
    [InlineData("**")]
    [InlineData("/")]
    [InlineData("./")]
    [InlineData("**/*")]
    [InlineData("code/src/Scheduler.Core/")]
    [InlineData("code/src/")]
    [InlineData("big/")]
    [InlineData("big/*.cs")]
    public void TooBroad_IsRefused(string path)
    {
        var t = Running(("991-A-y", 1, 991, ["docs/a.md"]));
        var d = LockArbiter.Ask(t, Ask("991-A-y", path), T0, Rules);
        Assert.Equal(LockAnswer.Refused, d.Answer);
        Assert.StartsWith("too broad: ask for the files or the folder you need", d.Reason);
        Assert.Same(t, d.Table);
    }

    [Theory]
    [InlineData("small/")]
    [InlineData("code/src/Scheduler.Web/")]
    [InlineData("code/src/Scheduler.Core/Locks/")]
    public void NarrowEnough_IsGranted(string path)
    {
        var t = Running(("991-A-y", 1, 991, ["docs/a.md"]));
        Assert.Equal(LockAnswer.Granted, LockArbiter.Ask(t, Ask("991-A-y", path), T0, Rules).Answer);
    }

    // ---- Wait-die (DL-11, DL-12)

    [Fact]
    public void OlderRequester_Waits()
    {
        var t = Running(("990-B-x", 1, 990, ["docs/a.md"]), ("991-A-y", 1, 991, ["docs/b.md"]));
        var d = LockArbiter.Ask(t, Ask("990-B-x", "docs/b.md"), T0, Rules);
        Assert.Equal(LockAnswer.Waiting, d.Answer);
        Assert.Equal("991-A-y", d.Holder);
        Assert.Equal(T0.AddMinutes(20), d.GivesUpAt);
        var open = d.Table.Find("990-B-x")!.Open!;
        Assert.Equal(["docs/b.md"], open.Paths);
        Assert.Equal(T0.AddMinutes(20), open.Deadline);
        Assert.Equal("waiting: held by 991-A-y, gives up at 10:20", LockReply.Text(d));
        Assert.Equal(2, LockReply.ExitCode(d.Answer));
    }

    [Fact]
    public void YoungerRequester_Yields_NamingPathAndHolder()
    {
        var t = Running(("990-B-x", 1, 990, ["docs/a.md"]), ("991-A-y", 1, 991, ["docs/b.md"]));
        var d = LockArbiter.Ask(t, Ask("991-A-y", "docs/a.md"), T0, Rules);
        Assert.Equal(LockAnswer.Yield, d.Answer);
        Assert.Equal("docs/a.md", d.Path);
        Assert.Equal("990-B-x", d.Holder);
        Assert.Equal("yield: docs/a.md held by 990-B-x (older)", LockReply.Text(d));
        Assert.Equal(3, LockReply.ExitCode(d.Answer));
    }

    [Fact]
    public void YieldedCard_RemembersWhatItHeldAndAskedFor()
    {
        var t = Running(("990-B-x", 1, 990, ["docs/a.md"]), ("991-A-y", 1, 991, ["docs/b.md"]));
        t = LockArbiter.Ask(t, Ask("991-A-y", "docs/c.md"), T0, Rules).Table;
        var d = LockArbiter.Ask(t, Ask("991-A-y", "docs/a.md"), T0, Rules);
        var released = d.Table.Release("991-A-y", LockRelease.Yielded).Find("991-A-y")!;
        Assert.Equal(CardLockState.Released, released.State);
        Assert.Empty(released.Held);
        Assert.Equal(["docs/c.md", "docs/a.md"], released.Remembered);
    }

    [Fact]
    public void HighPriorityHigherNumber_IsOlderThanNormalLowerNumber()
    {
        Assert.True(new CardAge(0, 1020).IsOlderThan(new CardAge(1, 990)));
        Assert.False(new CardAge(1, 990).IsOlderThan(new CardAge(0, 1020)));
        Assert.True(new CardAge(1, 990).IsOlderThan(new CardAge(1, 991)));
        Assert.True(new CardAge(1, 990).IsOlderThan(new CardAge(2, 10)));
        var t = Running(("990-B-x", 1, 990, ["docs/a.md"]), ("1020-A-y", 0, 1020, ["docs/b.md"]));
        Assert.Equal(LockAnswer.Waiting, LockArbiter.Ask(t, Ask("1020-A-y", "docs/a.md"), T0, Rules).Answer);
    }

    [Fact]
    public void AgeFixedAtFirstClaim_SurvivesPriorityChange()
    {
        var t = LockTable.Empty().Start("990-B-x", new CardAge(0, 990), Scope.Of("docs/a.md"), T0);
        t = t.Release("990-B-x", LockRelease.Yielded);
        t = t.Start("990-B-x", new CardAge(2, 990), Scope.Of("docs/a.md"), T0.AddHours(1));
        Assert.Equal(new CardAge(0, 990), t.Find("990-B-x")!.Age);
    }

    [Fact]
    public void CardAge_OfCard_UsesPrioAndNumber()
    {
        var card = new Scheduler.Core.Contracts.Card
        {
            Folder = Scheduler.Core.Contracts.CardFolder.Active,
            Path = "jobs/active/1016-A-x.md",
            Name = "1016-A-x",
            Num = 1016,
            Category = "A",
            Id = "1016-A",
            Prio = 0,
        };
        Assert.Equal(new CardAge(0, 1016), CardAge.Of(card));
    }

    [Fact]
    public void SecondRequest_IsMergedIntoTheOpenOne()
    {
        var t = Running(("990-B-x", 1, 990, ["docs/a.md"]), ("991-A-y", 1, 991, ["docs/b.md", "docs/c.md"]));
        t = LockArbiter.Ask(t, Ask("990-B-x", "docs/b.md"), T0, Rules).Table;
        var d = LockArbiter.Ask(t, Ask("990-B-x", "docs/c.md"), T0.AddMinutes(5), Rules);
        Assert.Equal(LockAnswer.Waiting, d.Answer);
        var open = d.Table.Find("990-B-x")!.Open!;
        Assert.Equal(["docs/b.md", "docs/c.md"], open.Paths);
        Assert.Equal(T0.AddMinutes(20), open.Deadline);
    }

    // ---- Wait limit (DL-13)

    [Fact]
    public void Waiting_TurnsToYield_AtWaitLimit()
    {
        var t = Running(("990-B-x", 1, 990, ["docs/a.md"]), ("991-A-y", 1, 991, ["docs/b.md"]));
        t = LockArbiter.Ask(t, Ask("990-B-x", "docs/b.md"), T0, Rules).Table;
        Assert.Equal(LockAnswer.Waiting, LockArbiter.Ask(t, Ask("990-B-x", "docs/b.md"), T0.AddMinutes(19), Rules).Answer);
        var d = LockArbiter.Ask(t, Ask("990-B-x", "docs/b.md"), T0.AddMinutes(20), Rules);
        Assert.Equal(LockAnswer.Yield, d.Answer);
        Assert.Equal("991-A-y", d.Holder);
        Assert.Null(d.Table.Find("990-B-x")!.Open);
        Assert.Equal(["docs/b.md"], d.Table.Find("990-B-x")!.Remembered);
    }

    [Fact]
    public void Waiting_TurnsToYield_AtWarnTime_WhenSooner()
    {
        var t = Running(("990-B-x", 1, 990, ["docs/a.md"]), ("991-A-y", 1, 991, ["docs/b.md"]));
        var d = LockArbiter.Ask(t, Ask("990-B-x", "docs/b.md"), T0, Rules, warnAt: T0.AddMinutes(5));
        Assert.Equal(T0.AddMinutes(5), d.GivesUpAt);
        var (_, decisions) = LockArbiter.Recheck(d.Table, T0.AddMinutes(5), Rules);
        Assert.Equal(LockAnswer.Yield, Assert.Single(decisions).Answer);
    }

    [Fact]
    public void Yield_IsNeverAnsweredByStoppingTheYoungerHolder()
    {
        var t = Running(("990-B-x", 1, 990, ["docs/a.md"]), ("991-A-y", 1, 991, ["docs/b.md"]));
        t = LockArbiter.Ask(t, Ask("990-B-x", "docs/b.md"), T0, Rules).Table;
        var (after, _) = LockArbiter.Recheck(t, T0.AddMinutes(30), Rules);
        Assert.Equal(CardLockState.Running, after.Find("991-A-y")!.State);
        Assert.Contains(after.Find("991-A-y")!.Held, e => e.Path == "docs/b.md");
    }

    // ---- Refused text

    [Fact]
    public void Refused_ReplyText_AndExitCodes()
    {
        var t = Running(("991-A-y", 1, 991, ["docs/a.md"]));
        var d = LockArbiter.Ask(t, Ask("991-A-y", "**"), T0, Rules);
        Assert.StartsWith("refused: too broad", LockReply.Text(d));
        Assert.Equal(4, LockReply.ExitCode(LockAnswer.Refused));
        Assert.Equal(0, LockReply.ExitCode(LockAnswer.Granted));
        Assert.Equal("granted", LockReply.Text(LockArbiter.Ask(t, Ask("991-A-y", "docs/z.md"), T0, Rules)));
    }

    [Theory]
    [InlineData("granted", LockAnswer.Granted)]
    [InlineData("waiting: held by 990-B, gives up at 10:42", LockAnswer.Waiting)]
    [InlineData("yield: docs/a.md held by 990-B (older)", LockAnswer.Yield)]
    [InlineData("refused: too broad", LockAnswer.Refused)]
    public void ReplyText_ParsesBackToItsAnswer(string text, LockAnswer answer) =>
        Assert.Equal(answer, LockReply.Parse(text + "\r\n"));

    [Fact]
    public void ReplyText_Unknown_IsNull() => Assert.Null(LockReply.Parse("what"));
}
