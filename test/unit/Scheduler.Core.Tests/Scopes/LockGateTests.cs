using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Locks;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Tests.Scopes;

/// <summary>
/// Dynamic locks at dispatch (design <c>dynamic-locks.md</c>, DL-1, DL-10, DL-22; test plan section 7,
/// "Resume"): <see cref="ScopeGate.ApplyLocks"/> gates on running cards' held sets and grows each
/// candidate's scope by its remembered set.
/// </summary>
public sealed class LockGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static Card Card(string name, string scope, CardFolder folder = CardFolder.Queue)
    {
        var parts = name.Split('-');
        return new Card
        {
            Folder = folder,
            Path = $"jobs/{folder.ToString().ToLowerInvariant()}/{name}.md",
            Name = name,
            Num = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            Category = parts[1],
            Id = $"{parts[0]}-{parts[1]}",
            Status = "ready",
            ScopeLine = scope,
        };
    }

    private static DispatchPlan Plan(params Card[] cards) =>
        DispatchPlanner.Plan(cards, [], new Dictionary<string, int>(), null, 2);

    private static DispatchPlan Gate(DispatchPlan plan, LockTable table, Dictionary<string, Scope>? running = null, Dictionary<string, string>? names = null) =>
        ScopeGate.ApplyLocks(plan, c => CardScope.Of(c, null), running ?? [], names ?? [], table);

    /// <summary>A table where 900-B runs holding <c>src/a/</c> (claim) and <c>src/shared.cs</c> (granted).</summary>
    private static LockTable Holder() =>
        LockTable.Empty("r")
            .Start("900-B-holder", new CardAge(1, 900), Scope.Of("src/a/"), Now, slot: 1)
            .Grant("900-B-holder", ["src/shared.cs"], Now);

    private static readonly Dictionary<string, Scope> RunningB = new() { ["900-B"] = Scope.Of("src/a/") };
    private static readonly Dictionary<string, string> NamesB = new() { ["900-B"] = "900-B-holder" };

    // DL-1: a granted entry keeps newcomers off, though the holder's declared scope does not cover it.
    [Fact]
    public void Gate_RunningCardsGrantedEntry_HoldsBackAQueuedCardOnIt()
    {
        var plan = Plan(Card("910-C-new", "src/shared.cs"));

        var gated = Gate(plan, Holder(), RunningB, NamesB);

        Assert.Empty(gated.Ordered);
        Assert.Contains(gated.Notes, n => n.Card == "910-C-new" && n.Note.Contains("900-B", StringComparison.Ordinal)
            && n.Note.Contains("src/shared.cs", StringComparison.Ordinal));
    }

    // Resume (DL-10, DL-1): a yielded card is not offered while its remembered entry is held; the note names the holder.
    [Fact]
    public void Resume_YieldedCard_NotOfferedWhileItsRememberedEntryIsHeld()
    {
        var yielded = Card("800-A-yielded", "src/mine/", CardFolder.Active);
        var table = Holder()
            .Start("800-A-yielded", new CardAge(1, 800), Scope.Of("src/mine/"), Now, slot: 2)
            .Release("800-A-yielded", LockRelease.Yielded, ["src/shared.cs"]);

        var gated = Gate(Plan(yielded), table, RunningB, NamesB);

        Assert.Empty(gated.Ordered);
        var note = Assert.Single(gated.Notes, n => n.Card == "800-A-yielded");
        Assert.Contains("900-B", note.Note, StringComparison.Ordinal);
    }

    // Resume (DL-10): once the holder is released, the yielded card is offered again.
    [Fact]
    public void Resume_YieldedCard_OfferedOnceTheHolderIsReleased()
    {
        var yielded = Card("800-A-yielded", "src/mine/", CardFolder.Active);
        var table = Holder()
            .Start("800-A-yielded", new CardAge(1, 800), Scope.Of("src/mine/"), Now, slot: 2)
            .Release("800-A-yielded", LockRelease.Yielded, ["src/shared.cs"])
            .Release("900-B-holder", LockRelease.Finished);

        var gated = Gate(Plan(yielded), table);

        Assert.Equal(["800-A"], gated.Ordered.Select(c => c.Card.Id));
    }

    // Resume (DL-1): the initial claim includes the remembered set, so it reserves those entries against later candidates.
    [Fact]
    public void Resume_InitialClaimIncludesTheRememberedSet()
    {
        var table = LockTable.Empty("r")
            .Start("800-A-yielded", new CardAge(1, 800), Scope.Of("src/mine/"), Now)
            .Grant("800-A-yielded", ["docs/notes.md"], Now)
            .Release("800-A-yielded", LockRelease.Ended);
        var row = table.Find("800-A-yielded");

        var claim = ScopeGate.WithRemembered(Scope.Of("src/mine/"), row);

        Assert.Equal(["src/mine/", "docs/notes.md"], claim!.Paths);
        var gated = Gate(Plan(Card("800-A-yielded", "src/mine/", CardFolder.Active), Card("810-C-other", "docs/notes.md")), table);
        Assert.Equal(["800-A"], gated.Ordered.Select(c => c.Card.Id));
        Assert.Contains(gated.Notes, n => n.Card == "810-C-other" && n.Note.Contains("800-A", StringComparison.Ordinal));
    }

    // DL-22: a card another project runs in the same repository holds its set against this project's candidates.
    [Fact]
    public void Gate_AnotherProjectsHolder_InTheSameTable_HoldsBackACandidate()
    {
        var gated = Gate(Plan(Card("910-C-new", "src/a/x.cs")), Holder());

        Assert.Empty(gated.Ordered);
        Assert.Contains(gated.Notes, n => n.Card == "910-C-new" && n.Note.Contains("900-B-holder", StringComparison.Ordinal));
    }

    // With an empty table, the lock gate changes nothing the declared-scope gate decided.
    [Fact]
    public void Gate_EmptyTable_IsTheDeclaredScopeGate()
    {
        var plan = Plan(Card("910-C-a", "src/x.cs"), Card("920-D-b", "src/y.cs"));

        var gated = Gate(plan, LockTable.Empty("r"));

        Assert.Equal(plan.Ordered.Select(c => c.Card.Id), gated.Ordered.Select(c => c.Card.Id));
        Assert.Equal(plan.Notes, gated.Notes);
        Assert.Same(plan.Busy, gated.Busy);
    }

    // A card gated out by a lock no longer stands as its category's next card.
    [Fact]
    public void Gate_GatedOutCard_LeavesTheBusyList()
    {
        var plan = Plan(Card("910-C-new", "src/shared.cs"));
        Assert.Equal("next: 910-C", plan.Busy["C"]);

        var gated = Gate(plan, Holder(), RunningB, NamesB);

        Assert.False(gated.Busy.ContainsKey("C"));
    }
}
