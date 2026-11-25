// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Locks;
using Scheduler.Core.PageModel;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Tests.PageModel;

/// <summary>The pool page's lock lines (design <c>dynamic-locks.md</c>, DL-24 to DL-26), from the lock table.</summary>
public sealed class PageLocksTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static LockTable Table() => LockTable.Empty("r")
        .Start("100-A-old", new CardAge(1, 100), Scope.Of("src/a/"), T0, slot: 1)
        .Grant("100-A-old", ["docs/known_gaps.md"], T0.AddMinutes(5))
        .Start("200-B-young", new CardAge(1, 200), Scope.Of("src/b/"), T0, slot: 2);

    // DL-24: the claim on one line, what else it holds with each entry's age.
    [Fact]
    public void Holds_ClaimAndGrantedEntriesWithTheirAges()
    {
        var h = PageLocks.Holds(Table(), "100-A-old", T0.AddMinutes(12))!;

        Assert.Equal(["src/a/"], h.Claim);
        Assert.Equal(12, h.ClaimMinutes);
        Assert.Equal([new Scheduler.Core.WebContract.PageHeldEntry("docs/known_gaps.md", 7)], h.Also);
        Assert.Equal("", h.Wait);
    }

    [Fact]
    public void Holds_NotRunning_IsNull() => Assert.Null(PageLocks.Holds(Table(), "999-Z-none", T0));

    // DL-25: an open wait names the path, its holder, since when and when it gives up.
    [Fact]
    public void Wait_NamesPathHolderAndTimes()
    {
        var t = Table();
        var open = new OpenLockRequest("f.lock", ["src/b/x.cs"], "need it", T0.AddMinutes(22), T0.AddMinutes(42));
        t = t.With(t.Find("100-A-old")! with { Open = open });

        Assert.Equal("waits for lock on `src/b/x.cs` held by `200-B-young` since 10:22, gives up at 10:42",
            PageLocks.Holds(t, "100-A-old", T0.AddMinutes(30))!.Wait);
    }

    // DL-25: a yielded card's Queue note names what it waits for.
    [Fact]
    public void YieldedNote_NamesThePathAndTheHolder()
    {
        var t = Table().Release("200-B-young", LockRelease.Yielded, ["docs/known_gaps.md"]);

        Assert.Equal("yielded: waits for `docs/known_gaps.md` held by `100-A-old`", PageLocks.YieldedNote(t, "200-B-young"));
        Assert.Equal("", PageLocks.YieldedNote(t.Release("100-A-old", LockRelease.Finished), "200-B-young"));
    }

    // DL-26: one day's events counted by kind.
    [Fact]
    public void Counts_OneDayByKind()
    {
        var day = DateOnly.FromDateTime(T0.DateTime);
        var c = PageLocks.Counts(
            [(T0, LockEventKind.Grant), (T0, LockEventKind.Grant), (T0, LockEventKind.Yield), (T0.AddDays(-1), LockEventKind.Grant), (T0, LockEventKind.Stray)],
            day);

        Assert.Equal((2, 0, 1, 0, 0, 1), (c.Grants, c.Waits, c.Yields, c.Refusals, c.PushesRefused, c.Strays));
        Assert.False(c.None);
        Assert.True(PageLocks.Counts([], day).None);
    }
}
