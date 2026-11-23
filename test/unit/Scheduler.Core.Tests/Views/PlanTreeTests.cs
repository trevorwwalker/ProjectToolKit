using Scheduler.Core.Views;

namespace Scheduler.Core.Tests.Views;

/// <summary>Plans view, second pass: plan steps from headings, card matching, checks (card 1112-A).</summary>
public class PlanTreeTests
{
    private const string Doc = "# Title\n\nintro\n\n## Setup\n\n### Build it\n\n### Test it\n\n## Run: the (thing)!\n\n## Setup\n\n### Build it\n\n```\n## not a heading\n```\n";

    private static ViewCard Card(string id, ViewFolder folder, string plan, string step) =>
        new()
        {
            Id = id,
            Number = long.Parse(id.Split('-')[0], System.Globalization.CultureInfo.InvariantCulture),
            Category = id.Split('-')[1],
            Folder = folder,
            PlanPath = plan,
            PlanStep = step,
        };

    [Fact]
    public void Steps_AreH2InOrder_WithH3AsSubSteps()
    {
        var steps = PlanSteps.Of(Doc);

        Assert.Equal(["1", "2", "3"], steps.Select(s => s.Label));
        Assert.Equal(["Setup", "Run: the (thing)!", "Setup"], steps.Select(s => s.Title));
        Assert.Equal(["1.1", "1.2"], steps[0].SubSteps.Select(s => s.Label));
        Assert.Empty(steps[1].SubSteps);
        Assert.Equal(["3.1"], steps[2].SubSteps.Select(s => s.Label));
    }

    [Fact]
    public void Anchors_AreGithubSlugs_WithSuffixForRepeats()
    {
        var steps = PlanSteps.Of(Doc);

        Assert.Equal("setup", steps[0].Anchor);
        Assert.Equal("build-it", steps[0].SubSteps[0].Anchor);
        Assert.Equal("run-the-thing", steps[1].Anchor);
        Assert.Equal("setup-2", steps[2].Anchor);
        Assert.Equal("build-it-2", steps[2].SubSteps[0].Anchor);
    }

    [Fact]
    public void Headings_InsideCodeFences_AreIgnored()
    {
        Assert.DoesNotContain(PlanSteps.Headings(Doc), h => h.Title == "not a heading");
    }

    [Fact]
    public void Roadmap_NumberedItems_KeepTheirOwnNumber()
    {
        var steps = PlanSteps.Of("# Roadmap\n\n## 1. First\n\n## 3. Third\n\n### 3.2 Sub\n\n## 4. Fourth\n");

        Assert.Equal(["1", "3", "4"], steps.Select(s => s.Label));
        Assert.Equal(["3.2"], steps[1].SubSteps.Select(s => s.Label));
        Assert.Equal("3-third", steps[1].Anchor);
    }

    [Theory]
    [InlineData("step 3", "3")]
    [InlineData("section 9 item 4", "9.4")]
    [InlineData("(5.2)", "5.2")]
    [InlineData("part 2", "2")]
    [InlineData("section 2 item 9", "2")]
    public void Match_FirstNumberToStep_SecondToSubStep(string text, string label)
    {
        var steps = PlanSteps.Of(string.Join("\n", Enumerable.Range(1, 9).Select(i => "## S" + i + "\n### a\n### b\n### c\n### d\n")));
        steps = steps.Select(s => s.Number is 2 ? s with { SubSteps = [] } : s).ToList();

        Assert.Equal(label, PlanStepMatch.Of(text, steps)?.Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("the whole plan")]
    [InlineData("step 12")]
    public void Match_NoNumberOrNoSuchStep_IsNull(string text)
    {
        Assert.Null(PlanStepMatch.Of(text, PlanSteps.Of("## A\n## B\n")));
    }

    [Fact]
    public void Tree_StepChecked_WhenItHasCards_AndAllAreDone()
    {
        const string plan = "jobs/plans/p.md";
        var cards = new[]
        {
            Card("1-A", ViewFolder.Done, plan, "step 1"),
            Card("2-A", ViewFolder.Archive, plan, "step 1"),
            Card("3-A", ViewFolder.Done, plan, "step 2"),
            Card("4-A", ViewFolder.Queue, plan, "step 2"),
        };

        var tree = PlanTree.Of(cards, new HashSet<string> { plan }, new Dictionary<string, string> { [plan] = "## A\n## B\n## C\n" });

        var node = Assert.Single(tree);
        Assert.True(node.Steps[0].Checked);
        Assert.Equal((2, 2), (node.Steps[0].Done, node.Steps[0].Total));
        Assert.False(node.Steps[1].Checked);
        Assert.Equal((1, 2), (node.Steps[1].Done, node.Steps[1].Total));
        Assert.False(node.Steps[2].Checked);
        Assert.Equal(0, node.Steps[2].Total);
        Assert.False(node.Complete);
        Assert.Null(node.Other);
    }

    [Fact]
    public void Tree_PlanComplete_WhenEveryStepIsChecked_AnEmptyStepKeepsItOpen()
    {
        const string plan = "jobs/plans/p.md";
        var cards = new[] { Card("1-A", ViewFolder.Done, plan, "step 1"), Card("2-A", ViewFolder.Done, plan, "step 3") };
        var onMain = new HashSet<string> { plan };

        Assert.False(PlanTree.Of(cards, onMain, new Dictionary<string, string> { [plan] = "## A\n## B\n## C\n" })[0].Complete);
        Assert.True(PlanTree.Of([cards[0], Card("2-A", ViewFolder.Done, plan, "step 2")], onMain, new Dictionary<string, string> { [plan] = "## A\n## C\n" })[0].Complete);
    }

    [Fact]
    public void Marks_BottomUp_CardToSubStepToStepToPlan()
    {
        const string plan = "jobs/plans/p.md";
        var doc = new Dictionary<string, string> { [plan] = "## A\n### A1\n### A2\n" };
        var onMain = new HashSet<string> { plan };
        var allDone = PlanTree.Of([Card("1-A", ViewFolder.Done, plan, "step 1.1"), Card("2-A", ViewFolder.Done, plan, "step 1.2")], onMain, doc)[0];
        var oneOpen = PlanTree.Of([Card("1-A", ViewFolder.Done, plan, "step 1.1"), Card("2-A", ViewFolder.Queue, plan, "step 1.2")], onMain, doc)[0];
        var emptySub = PlanTree.Of([Card("1-A", ViewFolder.Done, plan, "step 1.1")], onMain, doc)[0];

        Assert.All(allDone.Steps[0].SubSteps, s => Assert.Equal(PlanMark.Complete, s.Mark));
        Assert.Equal(PlanMark.Complete, allDone.Steps[0].Mark);
        Assert.Equal(PlanMark.Complete, allDone.Mark);
        Assert.Equal(PlanMark.Open, oneOpen.Steps[0].Mark);
        Assert.Equal(PlanMark.Open, oneOpen.Mark);
        Assert.Equal(PlanMark.Open, emptySub.Steps[0].SubSteps[1].Mark);
        Assert.Equal(PlanMark.Open, emptySub.Steps[0].Mark);
        Assert.False(emptySub.Complete);
    }

    [Fact]
    public void Marks_CancelledChildren_DoNotBlockAParent_AllCancelledIsRedX()
    {
        const string plan = "jobs/plans/p.md";
        var doc = new Dictionary<string, string> { [plan] = "## A\n" };
        var onMain = new HashSet<string> { plan };
        var mixed = PlanTree.Of([Card("1-A", ViewFolder.Done, plan, "step 1"), WithStatus(Card("2-A", ViewFolder.Archive, plan, "step 1"), "failed")], onMain, doc)[0];
        var none = PlanTree.Of([WithStatus(Card("1-A", ViewFolder.Archive, plan, "step 1"), "failed")], onMain, doc)[0];

        Assert.Equal(PlanMark.Complete, mixed.Steps[0].Mark);
        Assert.Equal(PlanMark.Cancelled, none.Steps[0].Mark);
        Assert.Equal(PlanMark.Cancelled, none.Mark);
    }

    [Fact]
    public void Tree_PlanWithNoCardsInAnyStep_IsNotComplete()
    {
        const string plan = "jobs/plans/p.md";
        var cards = new[] { Card("1-A", ViewFolder.Done, plan, "") };

        var tree = PlanTree.Of(cards, new HashSet<string> { plan }, new Dictionary<string, string> { [plan] = "## A\n" });

        Assert.False(tree[0].Complete);
        Assert.Equal(["1-A"], tree[0].Other!.Cards.Select(c => c.Id));
    }

    [Fact]
    public void Tree_SubStepCards_CountInTheirStep_AndUnmatchedGoToOther()
    {
        const string plan = "jobs/plans/p.md";
        var cards = new[]
        {
            Card("1-A", ViewFolder.Done, plan, "(1.1)"),
            Card("2-A", ViewFolder.Queue, plan, "step 1"),
            Card("3-A", ViewFolder.Queue, plan, "step 7"),
        };

        var tree = PlanTree.Of(cards, new HashSet<string> { plan }, new Dictionary<string, string> { [plan] = "## A\n### x\n" });

        var step = tree[0].Steps[0];
        Assert.Equal((1, 2), (step.Done, step.Total));
        Assert.Equal(["2-A"], step.Cards.Select(c => c.Id));
        Assert.True(step.SubSteps[0].Checked);
        Assert.Equal(["3-A"], tree[0].Other!.Cards.Select(c => c.Id));
    }

    [Fact]
    public void Tree_OrdersPlans_AndPutsNoPlanLast()
    {
        var onMain = new HashSet<string> { "jobs/plans/b.md", "jobs/plans/a.md" };
        var cards = new[]
        {
            Card("1-A", ViewFolder.Queue, "none", ""),
            Card("2-A", ViewFolder.Queue, "jobs/plans/b.md", "step 1"),
            Card("3-A", ViewFolder.Queue, "jobs/plans/a.md", "step 1"),
        };

        var tree = PlanTree.Of(cards, onMain, new Dictionary<string, string>());

        Assert.Equal(["jobs/plans/a.md", "jobs/plans/b.md", ""], tree.Select(n => n.Plan.Path));
        Assert.Equal(PlanKind.NoPlan, tree[^1].Plan.Kind);
        Assert.Equal(["1-A"], tree[^1].Other!.Cards.Select(c => c.Id));
    }

    private static ViewCard WithStatus(ViewCard c, string status) => c with { Status = status };

    [Fact]
    public void Marks_CardsByFolderAndStatus()
    {
        Assert.Equal(PlanMark.Complete, CardMark.Of(Card("1-A", ViewFolder.Done, "", "")));
        Assert.Equal(PlanMark.Complete, CardMark.Of(WithStatus(Card("1-A", ViewFolder.Archive, "", ""), "done")));
        Assert.Equal(PlanMark.Open, CardMark.Of(Card("1-A", ViewFolder.Queue, "", "")));
        Assert.Equal(PlanMark.Cancelled, CardMark.Of(WithStatus(Card("1-A", ViewFolder.Archive, "", ""), "closed: replaced by 9-A")));
        Assert.Equal(PlanMark.Cancelled, CardMark.Of(WithStatus(Card("1-A", ViewFolder.Queue, "", ""), "Cancelled")));
        Assert.Equal(PlanMark.Cancelled, CardMark.Of(WithStatus(Card("1-A", ViewFolder.Done, "", ""), "failed")));
    }

    [Fact]
    public void Marks_CancelledCardLeftOutOfTotal_RedXOnlyWhenAllCancelled()
    {
        const string plan = "jobs/plans/p.md";
        var cards = new[]
        {
            Card("1-A", ViewFolder.Done, plan, "step 1"),
            WithStatus(Card("2-A", ViewFolder.Archive, plan, "step 1"), "replaced"),
            WithStatus(Card("3-A", ViewFolder.Archive, plan, "step 2"), "dropped"),
            Card("4-A", ViewFolder.Queue, plan, "step 3"),
        };

        var tree = PlanTree.Of(cards, new HashSet<string> { plan }, new Dictionary<string, string> { [plan] = "## A\n## B\n## C\n## D\n" })[0];

        Assert.Equal((1, 1, 1), (tree.Steps[0].Done, tree.Steps[0].Total, tree.Steps[0].Cancelled));
        Assert.Equal(PlanMark.Complete, tree.Steps[0].Mark);
        Assert.Equal(PlanMark.Cancelled, tree.Steps[1].Mark);
        Assert.Equal(PlanMark.Open, tree.Steps[2].Mark);
        Assert.Equal(PlanMark.Open, tree.Steps[3].Mark);
        Assert.Equal(PlanMark.Open, tree.Mark);
    }

    [Fact]
    public void Marks_PlanCompleteAndPlanCancelled()
    {
        const string plan = "jobs/plans/p.md";
        var doc = new Dictionary<string, string> { [plan] = "## A\n## B\n" };
        var onMain = new HashSet<string> { plan };
        var docOne = new Dictionary<string, string> { [plan] = "## A\n" };

        var complete = PlanTree.Of([Card("1-A", ViewFolder.Done, plan, "step 1"), WithStatus(Card("2-A", ViewFolder.Archive, plan, "step 2"), "cancelled")], onMain, doc)[0];
        var cancelled = PlanTree.Of([WithStatus(Card("1-A", ViewFolder.Archive, plan, "step 1"), "closed"), WithStatus(Card("2-A", ViewFolder.Archive, plan, "step 9"), "failed")], onMain, docOne)[0];

        Assert.Equal(PlanMark.Complete, complete.Mark);
        Assert.Equal(PlanMark.Cancelled, cancelled.Mark);
    }
}
