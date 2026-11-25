// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;
using Scheduler.Core.Views;

namespace Scheduler.Core.Tests.Views;

/// <summary>The schedule-ahead simulation, rule VW-19 (card 1142-A). A fixed now, no clock.</summary>
public class GanttEstimateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private const int MaxMinutes = 90;

    private static ViewCard Card(long n, string scope, ViewFolder folder = ViewFolder.Queue, string status = "ready", int? box = 60,
        string[]? depends = null, string worker = "", int priority = 1, string category = "A") => new()
    {
        Id = $"{n}-{category}",
        Number = n,
        Category = category,
        Folder = folder,
        Status = status,
        TimeBoxMinutes = box,
        Scope = ViewCard.SplitScope(scope),
        DependsOn = depends ?? [],
        Worker = worker,
        Priority = priority,
    };

    private static IReadOnlyList<EstimatedBar> Run(int workers, IEnumerable<ViewCard> cards, IEnumerable<ViewRun>? runs = null, IReadOnlyList<AreaTypical>? typicals = null) =>
        GanttEstimate.Of(ViewsSnapshot.Build("p", cards, [], runs: runs, workers: workers, typicals: typicals), Now, MaxMinutes);

    private static EstimatedBar Bar(IReadOnlyList<EstimatedBar> bars, string id) => Assert.Single(bars, b => b.CardId == id);

    private static AreaTypical Typical(string area, string model, int median) => new() { Area = area, Model = model, Runs = 5, Median = median, P90 = median * 2 };

    [Fact]
    public void VW_19_TwoIndependentCards_TwoSlots_StartTogether()
    {
        var bars = Run(2, [Card(1, "a/"), Card(2, "b/")]);
        Assert.Equal(Now, Bar(bars, "1-A").Start);
        Assert.Equal(Now, Bar(bars, "2-A").Start);
        Assert.Equal(Now.AddMinutes(60), Bar(bars, "2-A").End);
    }

    [Fact]
    public void VW_19_TwoIndependentCards_OneSlot_RunBackToBack()
    {
        var bars = Run(1, [Card(2, "b/"), Card(1, "a/")]);
        Assert.Equal(Now, Bar(bars, "1-A").Start);
        Assert.Equal(Now.AddMinutes(60), Bar(bars, "2-A").Start);
        Assert.Equal(Now.AddMinutes(120), Bar(bars, "2-A").End);
    }

    [Fact]
    public void VW_19_PriorityHigh_GoesFirst_InDispatchOrder()
    {
        var bars = Run(1, [Card(1, "a/"), Card(2, "b/", priority: 0)]);
        Assert.Equal(Now, Bar(bars, "2-A").Start);
        Assert.Equal(Now.AddMinutes(60), Bar(bars, "1-A").Start);
        Assert.Equal("2-A", bars[0].CardId);
    }

    [Fact]
    public void VW_19_DependencyChain_StartsEachAfterTheLast()
    {
        // 1 <- 2 <- 3, with three free slots: they still run one after another.
        var bars = Run(3, [Card(3, "c/", depends: ["2-A"]), Card(2, "b/", depends: ["1-A"]), Card(1, "a/", box: 30)]);
        Assert.Equal(Now.AddMinutes(30), Bar(bars, "2-A").Start);
        Assert.Equal(Now.AddMinutes(90), Bar(bars, "3-A").Start);
        Assert.Equal(["1-A"], Bar(bars, "2-A").WaitedFor);
    }

    [Fact]
    public void VW_19_DoneDependency_DoesNotHoldTheCard()
    {
        var done = Card(1, "a/", ViewFolder.Done, "done");
        var bars = Run(1, [done, Card(2, "b/", depends: ["1-A"])]);
        Assert.Equal(Now, Bar(bars, "2-A").Start);
        Assert.Empty(Bar(bars, "2-A").WaitedFor);
        Assert.DoesNotContain(bars, b => b.CardId == "1-A");
    }

    [Fact]
    public void VW_19_ScopeOverlap_SerialisesTwoCards_EvenWithFreeSlots()
    {
        var bars = Run(4, [Card(1, "src/x/"), Card(2, "src/x/y.cs")]);
        Assert.Equal(Now, Bar(bars, "1-A").Start);
        Assert.Equal(Now.AddMinutes(60), Bar(bars, "2-A").Start);
        Assert.Equal(["1-A"], Bar(bars, "2-A").WaitedFor);
    }

    [Fact]
    public void VW_19_ScopeOverlap_WithARunningCard_WaitsForItsEnd()
    {
        var running = Card(1, "src/", ViewFolder.Active, box: 60);
        var run = new ViewRun { CardId = "1-A", Start = Now.AddMinutes(-20), Model = "m" };
        var bars = Run(2, [running, Card(2, "src/a.cs")], [run]);
        Assert.Equal(Now.AddMinutes(40), Bar(bars, "2-A").Start);
        Assert.DoesNotContain(bars, b => b.CardId == "1-A");
    }

    [Fact]
    public void VW_19_WaitingCard_AndItsDependent_GetNoTimes()
    {
        var parked = Card(1, "a/", ViewFolder.Active, "needs-human: which way?");
        var bars = Run(2, [parked, Card(2, "b/", depends: ["1-A"]), Card(3, "c/")]);
        Assert.True(Bar(bars, "1-A").Waiting);
        Assert.Equal(GanttEstimate.WaitingBasis, Bar(bars, "1-A").Basis);
        var dependent = Bar(bars, "2-A");
        Assert.Null(dependent.Start);
        Assert.Null(dependent.End);
        Assert.Equal(GanttEstimate.WaitingBasis, dependent.Basis);
        Assert.Equal(["1-A"], dependent.WaitedFor);
        Assert.Equal(Now, Bar(bars, "3-A").Start);
    }

    [Theory]
    [InlineData(ViewFolder.Review, "review: check it")]
    [InlineData(ViewFolder.Queue, "waiting: planner (request #4)")]
    public void VW_19_ReviewAndPlannerCards_AreWaiting(ViewFolder folder, string status)
    {
        var bars = Run(1, [Card(1, "a/", folder, status)]);
        Assert.True(Bar(bars, "1-A").Waiting);
    }

    [Fact]
    public void VW_19_UnknownDependency_IsUnbounded()
    {
        var bars = Run(1, [Card(2, "b/", depends: ["999-Z"])]);
        Assert.True(Bar(bars, "2-A").Waiting);
        Assert.Equal(["999-Z"], Bar(bars, "2-A").WaitedFor);
    }

    [Fact]
    public void VW_19_Length_TimeBoxFirst()
    {
        var bars = Run(1, [Card(1, "a/", box: 45, worker: "Opus")], typicals: [Typical("A", "claude-opus-5-5", 20)]);
        Assert.Equal(GanttEstimate.TimeBox, Bar(bars, "1-A").Basis);
        Assert.Equal(Now.AddMinutes(45), Bar(bars, "1-A").End);
    }

    [Fact]
    public void VW_19_Length_TypicalOfTheWorkerLinesModel()
    {
        var typicals = new[] { Typical("A", "claude-sonnet-5-5", 10), Typical("A", "claude-opus-5-5", 20), Typical("B", "claude-opus-5-5", 70) };
        var bars = Run(1, [Card(1, "a/", box: null, worker: "Opus")], typicals: typicals);
        Assert.Equal("typical claude-opus-5-5", Bar(bars, "1-A").Basis);
        Assert.Equal(Now.AddMinutes(20), Bar(bars, "1-A").End);
    }

    [Fact]
    public void VW_19_Length_MaxMinutesWhenNothingElse()
    {
        var bars = Run(1, [Card(1, "a/", box: null, worker: "Opus")], typicals: [Typical("B", "claude-opus-5-5", 70)]);
        Assert.Equal(GanttEstimate.MaxMinutes, Bar(bars, "1-A").Basis);
        Assert.Equal(Now.AddMinutes(MaxMinutes), Bar(bars, "1-A").End);
    }

    [Fact]
    public void VW_19_RunningCardPastItsTypical_GetsTheRestOfItsRun()
    {
        var card = Card(1, "a/", ViewFolder.Active, box: 120);
        var run = new ViewRun { CardId = "1-A", Start = Now.AddMinutes(-50), Model = "claude-opus-5-5", TimeBoxEnd = Now.AddMinutes(70) };
        var bars = Run(1, [card, Card(2, "b/")], [run], [Typical("A", "claude-opus-5-5", 30)]);
        var rest = Bar(bars, "1-A");
        Assert.Equal(Now, rest.Start);
        Assert.Equal(Now.AddMinutes(70), rest.End);
        Assert.Equal(GanttEstimate.TimeBox, rest.Basis);
        Assert.Equal(Now.AddMinutes(70), Bar(bars, "2-A").Start);   // the one slot is held until then
    }

    [Fact]
    public void VW_19_RunningCardWithinItsTypical_HoldsItsSlotToTheTypicalsEnd()
    {
        var card = Card(1, "a/", ViewFolder.Active, box: 120);
        var run = new ViewRun { CardId = "1-A", Start = Now.AddMinutes(-10), Model = "claude-opus-5-5" };
        var bars = Run(1, [card, Card(2, "b/")], [run], [Typical("A", "claude-opus-5-5", 30)]);
        Assert.DoesNotContain(bars, b => b.CardId == "1-A");
        Assert.Equal(Now.AddMinutes(20), Bar(bars, "2-A").Start);
    }

    [Fact]
    public void VW_19_DependencyCycle_Waits()
    {
        var bars = Run(1, [Card(1, "a/", depends: ["2-A"]), Card(2, "b/", depends: ["1-A"])]);
        Assert.All(bars, b => Assert.True(b.Waiting));
        Assert.Equal(2, bars.Count);
    }
}
