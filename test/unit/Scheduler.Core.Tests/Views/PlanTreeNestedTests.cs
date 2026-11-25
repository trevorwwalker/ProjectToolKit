// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Views;

namespace Scheduler.Core.Tests.Views;

/// <summary>VW-27 to VW-33: the nested Plans tree (card 1232-A).</summary>
public class PlanTreeNestedTests
{
    private const string A = "jobs/plans/a_plan.md";
    private const string B = "jobs/plans/b_plan.md";
    private const string C = "jobs/plans/c_plan.md";
    private const string D = "jobs/plans/d_plan.md";

    private static string Doc(string? parent, string body = "## 1. One\n\n### 1.1 Sub\n\n## 2. Two\n") =>
        "# Plan\n" + (parent is null ? "" : "Parent: " + parent + "\n") + "\n" + body;

    private static ViewCard Card(long n, ViewFolder folder, string plan, string step, string status = "") =>
        new() { Id = n + "-A-x", Number = n, Category = "A", Folder = folder, PlanPath = plan, PlanStep = step, Status = status };

    private static PlanForest Nest(Dictionary<string, string> texts, IEnumerable<ViewCard>? cards = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>>? marks = null)
    {
        var onMain = texts.Keys.ToHashSet();
        return PlanTree.Nest(cards ?? [], onMain, texts, onMain.Select(p => new PlanRef(PlanKind.Plans, p)), marks);
    }

    private static PlanNode Find(PlanForest f, string path) =>
        f.Top.SelectMany(PlanTree.Walk).First(n => n.Plan.Path == path);

    [Fact]
    public void VW27_Child_UnderParentStep_AfterCards_BeforeSubSteps()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc($"{A}, step 1") };
        var forest = Nest(texts, [Card(1, ViewFolder.Done, A, "step 1")]);

        var step = Assert.Single(forest.Top).Steps[0];
        Assert.Single(step.Cards);
        Assert.Equal(B, Assert.Single(step.Plans).Plan.Path);
        Assert.Single(step.SubSteps);
    }

    [Fact]
    public void VW27_Child_UnderSubStep()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc($"{A}, step 1.1") };

        var step = Assert.Single(Nest(texts).Top).Steps[0];

        Assert.Empty(step.Plans);
        Assert.Equal(B, Assert.Single(step.SubSteps[0].Plans).Plan.Path);
    }

    [Fact]
    public void VW27_Siblings_OrderedByPath()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [D] = Doc($"{A}, step 2"), [B] = Doc($"{A}, step 2"), [C] = Doc($"{A}, step 2") };

        var kids = Assert.Single(Nest(texts).Top).Steps[1].Plans;

        Assert.Equal([B, C, D], kids.Select(k => k.Plan.Path));
    }

    [Fact]
    public void VW27_ThreeLevels_Nest()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc($"{A}, step 1"), [C] = Doc($"{B}, step 2") };

        var grand = Assert.Single(Assert.Single(Assert.Single(Nest(texts).Top).Steps[0].Plans).Steps[1].Plans);

        Assert.Equal(C, grand.Plan.Path);
    }

    [Fact]
    public void VW27_EveryPlanPlacedOnce()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc($"{A}, step 1"), [C] = Doc($"{B}, step 9"), [D] = Doc($"{C}, step 1") };

        var all = Nest(texts).Top.SelectMany(PlanTree.Walk).Select(n => n.Plan.Path).ToList();

        Assert.Equal([A, B, C, D], all.Order());
    }

    [Fact]
    public void VW27_TopLevel_RootsThenUnplacedThenMissingThenNoPlan()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc($"{A}, step 9"), [D] = Doc(null) };
        var cards = new[]
        {
            Card(1, ViewFolder.Done, "jobs/plans/gone_plan.md", "1"),
            Card(2, ViewFolder.Done, "none", ""),
        };
        var onMain = texts.Keys.ToHashSet();

        var top = PlanTree.Nest(cards, onMain, texts, onMain.Select(p => new PlanRef(PlanKind.Plans, p))).Top;

        Assert.Equal([A, D, B, "jobs/plans/gone_plan.md", ""], top.Select(n => n.Plan.Path));
        Assert.Equal(PlanKind.NoPlan, top[^1].Plan.Kind);
    }

    [Fact]
    public void VW27_OtherCards_StayWithTheirPlan()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc($"{A}, step 1") };

        var forest = Nest(texts, [Card(1, ViewFolder.Queue, B, "step 77")]);

        Assert.Single(Find(forest, B).Other!.Cards);
        Assert.Null(Find(forest, A).Other);
    }

    [Theory]
    [InlineData("jobs/plans/zzz_plan.md, step 1", PlanFlag.ParentNotFound, "parent not found: jobs/plans/zzz_plan.md")]
    [InlineData("docs/design/x.md, step 1", PlanFlag.ParentNotAPlan, "parent is not a plan: docs/design/x.md")]
    [InlineData("jobs/plans/a_plan.md, step 9", PlanFlag.StepNotFound, "step not found: jobs/plans/a_plan.md, step 9")]
    [InlineData("jobs/plans/a_plan.md", PlanFlag.StepNotFound, "step not found: jobs/plans/a_plan.md, no step given")]
    public void VW28_Unplaced_TopLevelFlagged(string parent, PlanFlag flag, string words)
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc(parent) };

        var forest = Nest(texts);

        Assert.Equal([A, B], forest.Top.Select(n => n.Plan.Path));
        Assert.Equal(flag, forest.Top[1].Flag);
        Assert.Equal(words, forest.Top[1].FlagText);
        Assert.Empty(forest.Top[0].Steps.SelectMany(s => s.Plans));
    }

    [Fact]
    public void VW28_ParentNotFound_TopLevelFlagged() =>
        Assert.Equal(PlanFlag.ParentNotFound, Nest(new() { [B] = Doc("jobs/plans/q_plan.md, step 1") }).Top[0].Flag);

    [Fact]
    public void VW28_SelfParent_IsCycle()
    {
        var node = Assert.Single(Nest(new() { [A] = Doc($"{A}, step 1") }).Top);

        Assert.Equal(PlanFlag.Cycle, node.Flag);
        Assert.Equal("in a cycle: its parents lead back to it", node.FlagText);
        Assert.Empty(node.Steps[0].Plans);
    }

    [Fact]
    public void VW28_Cycle_BrokenAtFirstByPath()
    {
        // a -> b -> c -> a: a is flagged at the top, c under a, b under c.
        var texts = new Dictionary<string, string> { [A] = Doc($"{B}, step 1"), [B] = Doc($"{C}, step 1"), [C] = Doc($"{A}, step 1") };

        var top = Assert.Single(Nest(texts).Top);

        Assert.Equal(A, top.Plan.Path);
        Assert.Equal(PlanFlag.Cycle, top.Flag);
        var c = Assert.Single(top.Steps[0].Plans);
        Assert.Equal(C, c.Plan.Path);
        Assert.Equal(B, Assert.Single(c.Steps[0].Plans).Plan.Path);
    }

    [Fact]
    public void VW28_LeadingIntoCycle_PlacedNormally()
    {
        var texts = new Dictionary<string, string> { [A] = Doc($"{B}, step 1"), [B] = Doc($"{A}, step 1"), [C] = Doc($"{B}, step 2") };

        var top = Assert.Single(Nest(texts).Top);

        Assert.Equal(A, top.Plan.Path);
        var b = Assert.Single(top.Steps[0].Plans);
        Assert.Equal(C, Assert.Single(b.Steps[1].Plans).Plan.Path);
        Assert.Equal(PlanFlag.None, b.Flag);
    }

    [Fact]
    public void VW28_UnplacedKeepsItsChildren()
    {
        var texts = new Dictionary<string, string> { [A] = Doc("jobs/plans/zz_plan.md, step 1"), [B] = Doc($"{A}, step 2") };

        var top = Assert.Single(Nest(texts).Top);

        Assert.Equal(PlanFlag.ParentNotFound, top.Flag);
        Assert.Equal(B, Assert.Single(top.Steps[1].Plans).Plan.Path);
    }

    [Fact]
    public void VW29_StepCounts_IncludeChildPlans()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc($"{A}, step 1") };
        var cards = new[]
        {
            Card(1, ViewFolder.Done, A, "1"),
            Card(2, ViewFolder.Done, B, "1"),
            Card(3, ViewFolder.Queue, B, "1"),
            Card(4, ViewFolder.Done, B, "2", "cancelled"),
        };

        var step = Assert.Single(Nest(texts, cards).Top).Steps[0];

        Assert.Equal((2, 3, 1), (step.Done, step.Total, step.Cancelled));
    }

    [Fact]
    public void VW29_RootCounts_EqualSumOfEverythingUnder()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc($"{A}, step 1"), [C] = Doc($"{B}, step 2") };
        var cards = new[]
        {
            Card(1, ViewFolder.Done, A, "2"),
            Card(2, ViewFolder.Queue, B, "1"),
            Card(3, ViewFolder.Done, C, "1"),
            Card(4, ViewFolder.Queue, C, "x"),
        };

        var forest = Nest(texts, cards);
        var root = Assert.Single(forest.Top);

        Assert.Equal((2, 4, 0), (root.Done, root.Total, root.Cancelled));
        Assert.Equal(forest.Top.SelectMany(PlanTree.Walk).Sum(n => n.Steps.Sum(s => s.Cards.Count) + (n.Other?.Cards.Count ?? 0)), root.Total);
    }

    [Fact]
    public void VW29_StepMark_FromChildPlanMark()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null, "## 1. One\n"), [B] = Doc($"{A}, step 1", "## 1. Do\n") };

        var open = Assert.Single(Nest(texts, [Card(1, ViewFolder.Queue, B, "1")]).Top).Steps[0];
        var done = Assert.Single(Nest(texts, [Card(1, ViewFolder.Done, B, "1")]).Top).Steps[0];

        Assert.Equal(PlanMark.Open, open.Mark);
        Assert.Equal(PlanMark.Complete, done.Mark);
    }

    private static Dictionary<string, IReadOnlyDictionary<string, ManualMark>> Marks(string plan, string key) =>
        new() { [plan] = new Dictionary<string, ManualMark> { [key] = ManualMark.Checked } };

    [Fact]
    public void VW29_ManualCheckOnChild_CountsCompleteInParent()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null, "## 1. One\n"), [B] = Doc($"{A}, step 1", "## 1. Do\n") };

        var forest = Nest(texts, [Card(1, ViewFolder.Queue, B, "1")], Marks(B, PlanMarkFile.PlanKey));

        Assert.Equal(PlanMark.Complete, Assert.Single(forest.Top).Steps[0].Mark);
    }

    [Fact]
    public void VW29_ManualMarkOnNestedStep_Applies()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null, "## 1. One\n"), [B] = Doc($"{A}, step 1", "## 1. Do\n") };

        var forest = Nest(texts, [Card(1, ViewFolder.Queue, B, "1")], Marks(B, "1"));

        Assert.Equal(PlanMark.Complete, Find(forest, B).Steps[0].Mark);
        Assert.Equal(PlanMark.Complete, Find(forest, B).Mark);
    }

    [Fact]
    public void VW29_ManualMarks_DoNotChangeCounts()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null, "## 1. One\n"), [B] = Doc($"{A}, step 1", "## 1. Do\n") };
        var cards = new[] { Card(1, ViewFolder.Queue, B, "1") };

        var plain = Assert.Single(Nest(texts, cards).Top);
        var marked = Assert.Single(Nest(texts, cards, Marks(B, "1")).Top);

        Assert.Equal((plain.Done, plain.Total), (marked.Done, marked.Total));
        Assert.Equal((0, 1), (marked.Done, marked.Total));
    }

    [Fact]
    public void VW29_UnplacedPlan_AddsToNoParent()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc($"{A}, step 9") };

        var forest = Nest(texts, [Card(1, ViewFolder.Done, B, "1")]);

        Assert.Equal(0, forest.Top[0].Total);
        Assert.Equal(1, forest.Top[1].Total);
    }

    [Fact]
    public void VW30_Roots_CountsOnlyPlansWithNoParent()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc($"{A}, step 9"), [C] = Doc($"{A}, step 1") };

        Assert.Equal(1, Nest(texts).Roots);
    }

    [Fact]
    public void VW30_TwoRoots_BothOnTop()
    {
        var forest = Nest(new() { [A] = Doc(null), [B] = Doc(null) });

        Assert.Equal(2, forest.Roots);
        Assert.Equal([A, B], forest.Top.Select(n => n.Plan.Path));
    }

    [Fact]
    public void VW31_Roadmap_IsAnOrdinaryPlan()
    {
        var roadmap = "# Roadmap\n\n## Order of the remaining work\n\n## 1. Items\n\n### 1.1 One\n\n## 2. Next\n";
        var texts = new Dictionary<string, string> { [A] = roadmap, [B] = Doc($"{A}, step 1.1") };

        var root = Assert.Single(Nest(texts).Top);

        Assert.Equal(["1", "2"], root.Steps.Select(s => s.Step!.Label));
        Assert.Equal(B, Assert.Single(root.Steps[0].SubSteps[0].Plans).Plan.Path);
    }

    [Fact]
    public void VW33_Forest_BuiltOncePerSnapshot()
    {
        var snapshot = new ViewsSnapshot
        {
            Project = "p",
            Plans = [new ViewPlan { Path = A, Kind = PlanKind.Plans }],
            PlanTexts = new Dictionary<string, string> { [A] = Doc(null) },
        };

        var first = PlanTree.Of(snapshot);

        Assert.Same(first, PlanTree.Of(snapshot));
        Assert.Equal(A, Assert.Single(first.Top).Plan.Path);
        Assert.NotSame(first, PlanTree.Of(snapshot with { Project = "q" }));
    }

    [Fact]
    public void VW33_FlatOf_Unchanged()
    {
        var texts = new Dictionary<string, string> { [A] = Doc(null), [B] = Doc($"{A}, step 1") };
        var onMain = texts.Keys.ToHashSet();

        var flat = PlanTree.Of([Card(1, ViewFolder.Done, B, "1")], onMain, texts, onMain.Select(p => new PlanRef(PlanKind.Plans, p)));

        Assert.Equal([A, B], flat.Select(n => n.Plan.Path));
        Assert.All(flat, n => Assert.All(n.Steps, s => Assert.Empty(s.Plans)));
        Assert.All(flat, n => Assert.Equal(PlanFlag.None, n.Flag));
        Assert.Equal(1, flat[1].Done);
        Assert.Equal(0, flat[0].Done);
    }
}
