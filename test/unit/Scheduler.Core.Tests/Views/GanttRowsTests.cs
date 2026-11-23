using Scheduler.Core.Views;

namespace Scheduler.Core.Tests.Views;

/// <summary>Gantt rows and bars (card 1130-A).</summary>
public class GanttRowsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = T0.AddHours(10);

    private static ViewCard Card(string id, ViewFolder folder = ViewFolder.Done, string plan = "", string step = "", string type = "feature", string status = "") =>
        new()
        {
            Id = id,
            Number = long.Parse(id.Split('-')[0], System.Globalization.CultureInfo.InvariantCulture),
            Category = id.Split('-')[1],
            Title = "t" + id,
            Folder = folder,
            PlanPath = plan,
            PlanStep = step,
            CardType = type,
            Status = status,
        };

    private static ViewRun Run(string id, double startH, double? endH, string result = "done", string model = "opus") =>
        new()
        {
            CardId = id,
            Start = T0.AddHours(startH),
            End = endH is { } e ? T0.AddHours(e) : null,
            Result = result,
            Model = model,
        };

    private static GanttRow Find(GanttRow row, string key) =>
        row.Key == key ? row : row.Children.Select(c => FindOrNull(c, key)).First(r => r is not null)!;

    private static GanttRow? FindOrNull(GanttRow row, string key) =>
        row.Key == key ? row : row.Children.Select(c => FindOrNull(c, key)).FirstOrDefault(r => r is not null);

    [Fact]
    public void ByCategory_GroupsCardsUnderTheirCategory_WithSpanAndCounts()
    {
        var snap = ViewsSnapshot.Build(
            "p",
            [Card("1-A-x"), Card("2-A-y", ViewFolder.Review), Card("3-B-z")],
            [],
            runs: [Run("1-A-x", 0, 1), Run("2-A-y", 2, 3, "review"), Run("3-B-z", 1, 4)]);

        var root = GanttRows.Of(snap, GanttGroupBy.Category, T0, T0.AddHours(8), Now);

        Assert.Equal(["A", "B"], root.Children.Select(c => c.Label));
        var a = root.Children[0];
        Assert.Equal(2, a.Total);
        Assert.Equal(1, a.Done);
        Assert.Equal(T0, a.Summary!.Start);
        Assert.Equal(T0.AddHours(3), a.Summary.End);
        Assert.Equal(["1-A-x t1-A-x", "2-A-y t2-A-y"], a.Children.Select(c => c.Label));
        Assert.Equal(T0, root.Summary!.Start);
        Assert.Equal(T0.AddHours(4), root.Summary.End);
    }

    [Fact]
    public void ByPlan_GroupsStepsAndCards_WithBugsAndRequestsForNoPlan()
    {
        const string plan = "jobs/plans/p_plan.md";
        var snap = ViewsSnapshot.Build(
            "p",
            [
                Card("1-A-x", plan: plan, step: "1"),
                Card("2-A-y", plan: plan, step: "2"),
                Card("3-A-z", plan: plan),
                Card("4-A-bug", type: "bug"),
                Card("5-A-req"),
            ],
            [new ViewPlan { Path = plan, Kind = PlanKind.Plans, Title = "The plan" }],
            new Dictionary<string, string> { [plan] = "# P\n\n## One\n\n## Two\n" },
            [Run("1-A-x", 0, 1), Run("2-A-y", 1, 2), Run("3-A-z", 2, 3), Run("4-A-bug", 3, 4), Run("5-A-req", 4, 5)]);

        var root = GanttRows.Of(snap, GanttGroupBy.Plan, T0, T0.AddHours(8), Now);

        Assert.Equal(["The plan", "Bugs", "Requests"], root.Children.Select(c => c.Label));
        var p = root.Children[0];
        Assert.Equal(["1 One", "2 Two", "Other cards"], p.Children.Select(c => c.Label));
        Assert.Equal("1-A-x t1-A-x", p.Children[0].Children[0].Label);
        Assert.Equal(["4-A-bug t4-A-bug"], root.Children[1].Children.Select(c => c.Label));
        Assert.Equal(["5-A-req t5-A-req"], root.Children[2].Children.Select(c => c.Label));
        Assert.Equal(3, p.Total);
    }

    [Fact]
    public void WindowCut_DropsCardsWithoutBarsExceptActive_AndEmptyGroups()
    {
        var snap = ViewsSnapshot.Build(
            "p",
            [Card("1-A-x"), Card("2-B-y"), Card("3-C-z", ViewFolder.Active)],
            [],
            runs: [Run("1-A-x", 0, 1), Run("2-B-y", 20, 21)]);

        var root = GanttRows.Of(snap, GanttGroupBy.Category, T0, T0.AddHours(5), Now);

        Assert.Equal(["A", "C"], root.Children.Select(c => c.Label));
        Assert.Empty(root.Children[1].Children[0].Bars);
        Assert.Null(root.Children[1].Summary);
    }

    [Fact]
    public void SeveralRuns_OnOneCard_GiveOneBarEachInOrder_AndOneSummary()
    {
        var snap = ViewsSnapshot.Build(
            "p",
            [Card("1-A-x")],
            [],
            runs: [Run("1-A-x", 3, 4, "review", "sonnet"), Run("1-A-x", 0, 1, "failed", "spark"), Run("1-A-x", 20, 21)]);

        var root = GanttRows.Of(snap, GanttGroupBy.Category, T0, T0.AddHours(8), Now);

        var card = root.Children[0].Children[0];
        Assert.Equal(["failed", "review"], card.Bars.Select(b => b.ColorKey));
        Assert.Equal(["spark", "sonnet"], card.Bars.Select(b => b.Model));
        Assert.Equal(T0, root.Children[0].Summary!.Start);
        Assert.Equal(T0.AddHours(4), root.Children[0].Summary!.End);
    }

    [Fact]
    public void RunningCard_HasABarToNow_WithItsTimeBoxEnd()
    {
        var run = Run("1-A-x", 8, null) with { TimeBoxEnd = T0.AddHours(11) };
        var snap = ViewsSnapshot.Build("p", [Card("1-A-x", ViewFolder.Active)], [], runs: [run]);

        var root = GanttRows.Of(snap, GanttGroupBy.Category, T0, T0.AddHours(12), Now);

        var bar = Assert.Single(root.Children[0].Children[0].Bars);
        Assert.True(bar.Running);
        Assert.Equal("running", bar.ColorKey);
        Assert.Equal(T0.AddHours(8), bar.Start);
        Assert.Equal(Now, bar.End);
        Assert.Equal(T0.AddHours(11), bar.TimeBoxEnd);
    }

    [Fact]
    public void ColorKey_MapsKnownResults_AndOtherwiseOther()
    {
        Assert.Equal("needs-human", GanttBars.ColorKey(" Needs-Human "));
        Assert.Equal("other", GanttBars.ColorKey("weird"));
        Assert.Equal("other", GanttBars.ColorKey(""));
    }

    [Fact]
    public void CancelledCards_AreListedButNotCounted()
    {
        var snap = ViewsSnapshot.Build(
            "p",
            [Card("1-A-x"), Card("2-A-y", status: "cancelled")],
            [],
            runs: [Run("1-A-x", 0, 1), Run("2-A-y", 1, 2)]);

        var root = GanttRows.Of(snap, GanttGroupBy.Category, T0, T0.AddHours(8), Now);

        Assert.Equal(1, Find(root, "category:A").Total);
        Assert.Equal(2, Find(root, "category:A").Children.Count);
    }
}
