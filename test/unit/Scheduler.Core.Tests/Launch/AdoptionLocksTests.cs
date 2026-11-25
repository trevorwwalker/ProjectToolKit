// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;
using Scheduler.Core.Locks;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Tests.Launch;

/// <summary>Dynamic locks at a restart (design <c>dynamic-locks.md</c>, DL-21): adopted cards keep their locks.</summary>
public sealed class AdoptionLocksTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static RunningWorker Worker(int slot, string card) => new()
    {
        Slot = slot,
        Pid = 10 + slot,
        Start = Now,
        Card = card,
        Category = "A",
        Id = card[..card.IndexOf('-', card.IndexOf('-') + 1)],
    };

    private static LockTable Table()
    {
        var open = new OpenLockRequest("20261121-100000-000-slot1.lock", ["docs/x.md"], "why", Now, Now.AddMinutes(20));
        var t = LockTable.Empty("r")
            .Start("100-A-kept", new CardAge(1, 100), Scope.Of("src/a/"), Now, slot: 1)
            .Grant("100-A-kept", ["src/g.cs"], Now)
            .Start("200-B-gone", new CardAge(1, 200), Scope.Of("src/b/"), Now, slot: 2)
            .Grant("200-B-gone", ["src/h.cs"], Now)
            .Starting("300-C-starting", slot: 3);
        return t.With(t.Find("100-A-kept")! with { Open = open });
    }

    // DL-21: the adopted card keeps its held set and its open request with the old deadline, in its new slot.
    [Fact]
    public void KeepLocks_AdoptedCard_KeepsHeldSetAndOpenRequest()
    {
        var before = Table();

        var after = Adoption.KeepLocks(before, [Worker(4, "100-A-kept")]);

        var row = after.Find("100-A-kept")!;
        Assert.True(row.Holds);
        Assert.Equal(4, row.Slot);
        Assert.Equal(before.Find("100-A-kept")!.Held, row.Held);
        Assert.Equal(Now.AddMinutes(20), row.Open!.Deadline);
    }

    // DL-21: a card with no adopted worker is released; its granted entries become its remembered set.
    [Fact]
    public void KeepLocks_NotAdopted_IsReleasedToItsRememberedSet()
    {
        var row = Adoption.KeepLocks(Table(), [Worker(4, "100-A-kept")]).Find("200-B-gone")!;

        Assert.Equal(CardLockState.Released, row.State);
        Assert.Empty(row.Held);
        Assert.Equal(["src/h.cs"], row.Remembered);
    }

    // DL-21 (1018-D): a starting card's entry is dropped, with nothing to remember.
    [Fact]
    public void KeepLocks_StartingCard_IsDropped()
    {
        Assert.Null(Adoption.KeepLocks(Table(), []).Find("300-C-starting"));
    }
}
