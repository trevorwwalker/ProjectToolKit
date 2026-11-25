// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
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

        var root = GanttRows.Of(snap, T0, T0.AddHours(8), Now);

        Assert.Equal(["The plan", "Bugs", "Requests"], root.Children.Select(c => c.Label));
        var p = root.Children[0];
        Assert.Equal(["1. One", "2. Two", "Other cards"], p.Children.Select(c => c.Label));
        Assert.Equal("1-A-x: t1-A-x", p.Children[0].Children[0].Label);
        Assert.Equal(["4-A-bug: t4-A-bug"], root.Children[1].Children.Select(c => c.Label));
        Assert.Equal(["5-A-req: t5-A-req"], root.Children[2].Children.Select(c => c.Label));
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

        var root = GanttRows.Of(snap, T0, T0.AddHours(5), Now);

        Assert.Equal(["Requests"], root.Children.Select(c => c.Label));
        Assert.Equal(["card:1-A-x", "card:3-C-z"], root.Children[0].Children.Select(c => c.Key));
        Assert.Empty(root.Children[0].Children[1].Bars);
    }

    [Fact]
    public void Listed_QueuedCardsWithAnEstimate_GetARowWithoutABar()
    {
        var snap = ViewsSnapshot.Build(
            "p",
            [Card("1-A-x"), Card("4-D-q", ViewFolder.Queue), Card("5-D-r", ViewFolder.Queue)],
            [],
            runs: [Run("1-A-x", 0, 1)]);

        var root = GanttRows.Of(snap, T0, T0.AddHours(5), Now, new HashSet<string>(["4-D-q"], StringComparer.Ordinal));

        Assert.Equal(["card:1-A-x", "card:4-D-q"], root.Children[0].Children.Select(c => c.Key));
        Assert.Empty(root.Children[0].Children[1].Bars);
    }

    [Fact]
    public void SeveralRuns_OnOneCard_GiveOneBarEachInOrder_AndOneSummary()
    {
        var snap = ViewsSnapshot.Build(
            "p",
            [Card("1-A-x")],
            [],
            runs: [Run("1-A-x", 3, 4, "review", "sonnet"), Run("1-A-x", 0, 1, "failed", "spark"), Run("1-A-x", 20, 21)]);

        var root = GanttRows.Of(snap, T0, T0.AddHours(8), Now);

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

        var root = GanttRows.Of(snap, T0, T0.AddHours(12), Now);

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

        var root = GanttRows.Of(snap, T0, T0.AddHours(8), Now);

        Assert.Equal(1, Find(root, "requests").Total);
        Assert.Equal(2, Find(root, "requests").Children.Count);
    }

    private const string Root = "jobs/plans/p_plan.md";
    private const string Child = "jobs/plans/c_plan.md";

    private static ViewsSnapshot Nested(params ViewCard[] cards) =>
        ViewsSnapshot.Build(
            "p",
            cards,
            [
                new ViewPlan { Path = Root, Kind = PlanKind.Plans, Title = "Root" },
                new ViewPlan { Path = Child, Kind = PlanKind.Plans, Title = "Kid" },
            ],
            new Dictionary<string, string>
            {
                [Root] = "# Root\n\n## 1. One\n\n## 2. Two\n",
                [Child] = "# Kid\n\nParent: jobs/plans/p_plan.md, step 1\n\n## 1. Inner\n",
            },
            cards.Select((c, n) => Run(c.Id, n, n + 1)).ToList());

    private static GanttRow NestedRows(ViewsSnapshot snap) => GanttRows.Of(snap, T0, T0.AddHours(8), Now);

    private static IEnumerable<GanttRow> All(GanttRow row) => new[] { row }.Concat(row.Children.SelectMany(All));

    [Fact]
    public void VW32_Gantt_ChildPlanUnderParentStep()
    {
        var root = NestedRows(Nested(Card("1-A-x", plan: Child, step: "1"), Card("2-A-y", plan: Root, step: "1")));

        var step = Find(root, "plan:" + Root + "/1");
        Assert.Equal(["2-A-y: t2-A-y", "Kid"], step.Children.Select(c => c.Label));
        Assert.Equal(GanttRowKind.Plan, step.Children[1].Kind);
        Assert.Equal("1-A-x: t1-A-x", Find(root, "plan:" + Child + "/1").Children[0].Label);
        Assert.Equal(["Root"], root.Children.Select(c => c.Label));
    }

    [Fact]
    public void VW32_Gantt_KeysUniqueAndPlanKeysUnchanged()
    {
        var root = NestedRows(Nested(Card("1-A-x", plan: Child, step: "1"), Card("2-A-y", plan: Root, step: "2"), Card("3-A-z", plan: Child)));

        var keys = AllRows(root).Select(r => r.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("plan:" + Child, keys);
        Assert.Contains("plan:" + Child + "/1", keys);
        Assert.Contains("plan:" + Child + "/other", keys);
        Assert.Contains("plan:" + Root + "/2", keys);
    }

    [Fact]
    public void VW32_Gantt_StepWithOnlyChildPlan_Kept()
    {
        var root = NestedRows(Nested(Card("1-A-x", plan: Child, step: "1")));

        var step = Find(root, "plan:" + Root + "/1");
        Assert.Equal(["plan:" + Child], step.Children.Select(c => c.Key));
        Assert.Null(FindOrNull(root, "plan:" + Root + "/2"));
    }

    [Fact]
    public void VW32_Gantt_PlanWithNoOwnCards_KeptForItsChildren()
    {
        var root = NestedRows(Nested(Card("1-A-x", plan: Child, step: "1")));

        Assert.Equal(["Root"], root.Children.Select(c => c.Label));
        Assert.Equal(1, root.Children[0].Total);
    }

    [Fact]
    public void VW32_Gantt_UnplacedPlan_TopLevel()
    {
        var snap = ViewsSnapshot.Build(
            "p",
            [Card("1-A-x", plan: Child, step: "1"), Card("2-A-y", plan: Root, step: "1")],
            [new ViewPlan { Path = Root, Kind = PlanKind.Plans, Title = "Root" }, new ViewPlan { Path = Child, Kind = PlanKind.Plans, Title = "Kid" }],
            new Dictionary<string, string>
            {
                [Root] = "# Root\n\n## 1. One\n",
                [Child] = "# Kid\n\nParent: jobs/plans/gone_plan.md, step 1\n\n## 1. Inner\n",
            },
            [Run("1-A-x", 0, 1), Run("2-A-y", 1, 2)]);

        var root = NestedRows(snap);

        Assert.Equal(2, root.Children.Count);
        Assert.Equal("plan:" + Root, root.Children[0].Key);
        Assert.Equal("plan:" + Child, root.Children[1].Key);
        Assert.Contains("parent not found", root.Children[1].Label);
    }

    [Fact]
    public void VW32_Gantt_SummaryCoversChildPlans()
    {
        var root = NestedRows(Nested(Card("1-A-x", plan: Child, step: "1"), Card("2-A-y", plan: Root, step: "2")));

        var p = Find(root, "plan:" + Root);
        Assert.Equal(2, p.Total);
        Assert.Equal(2, p.Done);
        Assert.Equal(1, Find(root, "plan:" + Root + "/1").Total);
        Assert.NotNull(Find(root, "plan:" + Root + "/1").Summary);
        Assert.Equal(T0, p.Summary!.Start);
        Assert.Equal(T0.AddHours(2), p.Summary.End);
    }

    // 1318-A: the Gantt rows number steps and cards as the Plans view does.
    private const string PA = "jobs/plans/a_plan.md";
    private const string PB = "jobs/plans/b_plan.md";
    private const string PC = "jobs/plans/c_plan.md";
    private const string PD = "jobs/plans/d_plan.md";

    private static GanttRow Numbered(Dictionary<string, string> texts, params ViewCard[] cards)
    {
        var snap = ViewsSnapshot.Build(
            "p",
            cards,
            texts.Keys.Select(k => new ViewPlan { Path = k, Kind = PlanKind.Plans, Title = k }).ToList(),
            texts,
            cards.Select(c => Run(c.Id, 0, 1)).ToList());
        return GanttRows.Of(snap, T0, T0.AddHours(8), Now);
    }

    private static IEnumerable<GanttRow> AllRows(GanttRow r) => new[] { r }.Concat(r.Children.SelectMany(AllRows));

    [Fact]
    public void Numbered_RootStep_ShowsItsNumberOnce_AndKeepsItsKey()
    {
        var root = Numbered(new() { [PA] = "# A\n\n## 1. Design\n\n## 2. Build the job system\n" }, Card("1-A-x", plan: PA, step: "2"));

        var step = Find(root, "plan:" + PA + "/2");
        Assert.Equal("2. Build the job system", step.Label);
    }

    [Fact]
    public void Numbered_ChildPlanStep_ShowsThePath_AndKeepsItsKey()
    {
        var texts = new Dictionary<string, string>
        {
            [PA] = "# A\n\n## 1. One\n\n## 2. Two\n",
            [PB] = "# B\nParent: " + PA + ", step 2\n\n## 7. Seven\n\n### 7.1 Sub\n",
        };
        var root = Numbered(texts, Card("1-A-x", plan: PB, step: "7"), Card("2-A-y", plan: PB, step: "7.1"));

        Assert.Equal("2.7. Seven", Find(root, "plan:" + PB + "/7").Label);
        var keys = AllRows(root).Select(r => r.Key).ToList();
        Assert.Contains("plan:" + PA + "/2", keys);
        Assert.Contains("plan:" + PB, keys);
        Assert.Contains("plan:" + PB + "/7/7.1", keys);
        Assert.Equal("2.7.1. Sub", Find(root, "plan:" + PB + "/7/7.1").Label);
    }

    [Fact]
    public void Numbered_ThirdLevel_ShowsTheWholePath()
    {
        var texts = new Dictionary<string, string>
        {
            [PA] = "# A\n\n## 2. Two\n",
            [PB] = "# B\nParent: " + PA + ", step 2\n\n## 10. Ten\n",
            [PC] = "# C\nParent: " + PB + ", step 10\n\n## 8. Eight\n",
        };
        var root = Numbered(texts, Card("1-A-x", plan: PC, step: "8"));

        Assert.Equal("2.10.8. Eight", Find(root, "plan:" + PC + "/8").Label);
    }

    [Fact]
    public void Numbered_TwoChildPlansUnderOneStep_BothNumberFromTheParent()
    {
        var texts = new Dictionary<string, string>
        {
            [PA] = "# A\n\n## 2. Two\n",
            [PB] = "# B\nParent: " + PA + ", step 2\n\n## 1. First\n",
            [PD] = "# D\nParent: " + PA + ", step 2\n\n## 1. Other first\n",
        };
        var root = Numbered(texts, Card("1-A-x", plan: PB, step: "1"), Card("2-A-y", plan: PD, step: "1"));

        Assert.Equal("2.1. First", Find(root, "plan:" + PB + "/1").Label);
        Assert.Equal("2.1. Other first", Find(root, "plan:" + PD + "/1").Label);
    }

    [Fact]
    public void Numbered_CardRow_ShowsItsIdOnce()
    {
        var plain = Card("1-A-x");
        var titled = Card("1312-D", ViewFolder.Done) with { Title = "1312-D: Design the build layout" };
        var bare = Card("1313-B", ViewFolder.Done) with { Title = "1313-B" };
        var root = Numbered([], plain, titled, bare);

        Assert.Equal("1312-D: Design the build layout", Find(root, "card:1312-D").Label);
        Assert.Equal("1313-B", Find(root, "card:1313-B").Label);
        Assert.Equal("1-A-x: t1-A-x", Find(root, "card:1-A-x").Label);
    }

    [Fact]
    public void ShownNumbers_GivesEachStepItsPath()
    {
        var texts = new Dictionary<string, string>
        {
            [PA] = "# A\n\n## 2. Two\n",
            [PB] = "# B\nParent: " + PA + ", step 2\n\n## 7. Seven\n",
        };
        var snap = ViewsSnapshot.Build("p", [], texts.Keys.Select(k => new ViewPlan { Path = k, Kind = PlanKind.Plans, Title = k }).ToList(), texts, []);

        var shown = GanttRows.ShownNumbers(snap);

        Assert.Equal("2", shown[(PA, "2")]);
        Assert.Equal("2.7", shown[(PB, "7")]);
    }
}
