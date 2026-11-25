// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Views;

namespace Scheduler.Core.Tests.Views;

/// <summary>Plans view model, rules VW-11 to VW-15 (docs/design/tdd/scheduler/views.md).</summary>
public class PlansViewModelTests
{
    private static HashSet<string> Paths(params string[] paths) => new(paths, StringComparer.Ordinal);

    private static ViewCard Card(string id, ViewFolder folder, string plan = "", string step = "", string status = "ready") =>
        new()
        {
            Id = id,
            Number = long.Parse(id.Split('-')[0], System.Globalization.CultureInfo.InvariantCulture),
            Category = id.Split('-')[1],
            Folder = folder,
            Status = status,
            PlanPath = plan,
            PlanStep = step,
        };

    // VW-11: only jobs/plans/ holds plans; the order is the plans by name, then No plan.
    [Fact]
    public void VW_11_Order_PlansByName_ThenNoPlan_AnyOtherPathIsNoPlan()
    {
        var onMain = Paths("jobs/plans/b_plan.md", "jobs/plans/a_plan.md", "docs/z.md", "docs/c.md", "jobs/archive/old_plan.md");
        var cards = new[]
        {
            Card("1-A", ViewFolder.Queue, "none (developer, 2026-11-22)"),
            Card("2-A", ViewFolder.Queue, "jobs/archive/old_plan.md"),
            Card("3-A", ViewFolder.Queue, "docs/design/z.md"),
            Card("4-A", ViewFolder.Queue, "jobs/plans/b_plan.md"),
            Card("5-A", ViewFolder.Queue, "docs/c.md"),
            Card("6-A", ViewFolder.Queue, "jobs/plans/a_plan.md"),
            Card("7-A", ViewFolder.Queue, "jobs/plans/gone_plan.md"),
        };

        var paths = PlanCounts.Of(cards, Paths(), onMain).Select(p => p.Plan.Path).ToList();

        Assert.Equal(["jobs/plans/a_plan.md", "jobs/plans/b_plan.md", "jobs/plans/gone_plan.md", ""], paths);
        Assert.Equal(PlanPaths.NoPlan, PlanPaths.Resolve("docs/design/z.md", onMain));
        Assert.Equal("docs/design/z.md", PlanPaths.Named("`docs/design/z.md`;"));
        Assert.Equal("", PlanPaths.Named("none (planner, 2026-11-01)"));
    }

    // VW-11: a card whose plan is in jobs/plans/ lands under it; one naming a design document lands under No plan.
    [Fact]
    public void VW_11_Tree_CardLandsUnderItsPlan_DesignDocumentIsNoPlan_RoadmapIsNotAPlan()
    {
        var onMain = Paths("jobs/plans/a_plan.md", "docs/design/gdd/scheduler-roadmap.md");
        var cards = new[]
        {
            Card("1-A", ViewFolder.Queue, "jobs/plans/a_plan.md"),
            Card("2-A", ViewFolder.Queue, "docs/design/tdd/x.md"),
        };
        var also = new[]
        {
            new PlanRef(PlanKind.Plans, "jobs/plans/a_plan.md"),
            new PlanRef(PlanKind.Elsewhere, "docs/design/gdd/scheduler-roadmap.md"),
            new PlanRef(PlanKind.Archived, "jobs/archive/b_plan.md"),
        };

        var tree = PlanTree.Of(cards, onMain, new Dictionary<string, string>(), also);

        Assert.Equal(["jobs/plans/a_plan.md", ""], tree.Select(n => n.Plan.Path).ToList());
        Assert.Equal(["1-A"], tree[0].Other!.Cards.Select(c => c.Id).ToList());
        Assert.Equal(["2-A"], tree[1].Other!.Cards.Select(c => c.Id).ToList());
    }

    // VW-12
    [Fact]
    public void VW_12_PlanPath_DocsJobsBecomesJobs_ArchiveResolvesToPlans_MissingMarked_NoneIsNoPlan()
    {
        var onMain = Paths("jobs/plans/ui_redesign_plan.md", "jobs/plans/a1000_plan.md", "docs/design/tdd/scheduler/multi-project.md");

        Assert.Equal(new PlanRef(PlanKind.Plans, "jobs/plans/ui_redesign_plan.md"), PlanPaths.Resolve("docs/jobs/plans/ui_redesign_plan.md", onMain));
        Assert.Equal(new PlanRef(PlanKind.Plans, "jobs/plans/a1000_plan.md"), PlanPaths.Resolve("`jobs/archive/a1000_plan.md`;", onMain));
        Assert.Equal(new PlanRef(PlanKind.Plans, "jobs/plans/a1000_plan.md"), PlanPaths.Resolve("docs/jobs/archive/a1000_plan.md,", onMain));
        Assert.Equal(PlanPaths.NoPlan, PlanPaths.Resolve("jobs/archive/other_plan.md", onMain));
        Assert.Equal(PlanPaths.NoPlan, PlanPaths.Resolve("`docs/design/tdd/scheduler/multi-project.md`", onMain));
        Assert.Equal(new PlanRef(PlanKind.Missing, "jobs/plans/nowhere_plan.md"), PlanPaths.Resolve("jobs/plans/nowhere_plan.md", onMain));
        Assert.Equal(PlanPaths.NoPlan, PlanPaths.Resolve("none (developer, 2026-11-22)", onMain));
        Assert.Equal(PlanPaths.NoPlan, PlanPaths.Resolve("", onMain));
        Assert.Equal(PlanPaths.NoPlan, PlanPaths.Resolve(null, onMain));
    }

    // VW-13
    [Fact]
    public void VW_13_StatusLine_FirstStatusOrItalicStatus_Cut200_NoneSaysSo()
    {
        Assert.Equal("Status: building", PlanStatus.Of("# Plan\n\nStatus: building\nStatus: later"));
        Assert.Equal("*Status 2026-11-01: half done*", PlanStatus.Of("# Plan\n*Status 2026-11-01: half done*"));
        Assert.Equal("**Status:** open", PlanStatus.Of("# Plan\r\n**Status:** open\r\n"));
        Assert.Equal(200, PlanStatus.Of("Status: " + new string('x', 500)).Length);
        Assert.Equal("no status line", PlanStatus.Of("# Plan\nStatuses are not this."));
        Assert.Equal("no status line", PlanStatus.Of(null));
    }

    // VW-14
    [Fact]
    public void VW_14_Counts_CreatedDonePendingByColumn_PerPlanAndStep()
    {
        var onMain = Paths("jobs/plans/p_plan.md");
        const string p = "jobs/plans/p_plan.md";
        var cards = new[]
        {
            Card("1-A", ViewFolder.Done, p, "step 1"),
            Card("2-A", ViewFolder.Archive, p, "step 1"),
            Card("3-A", ViewFolder.Queue, p, "step 2"),
            Card("4-A", ViewFolder.Active, p, "step 2"),
            Card("5-A", ViewFolder.Review, p, "step 2", "review: check it"),
            Card("6-A", ViewFolder.Active, p, "step 2", "needs-human: which?"),
            Card("7-A", ViewFolder.Queue, p, "step 2", "waiting: you"),
            Card("8-A", ViewFolder.Queue, p, "step 2", "Waiting: planner (request #3)"),
            Card("9-A", ViewFolder.Queue, "none (x)"),
        };

        var counts = PlanCounts.Of(cards, Paths("4-A"), onMain);

        var plan = counts[0];
        Assert.Equal(8, plan.Tally.Created);
        Assert.Equal(2, plan.Tally.Done);
        Assert.Equal(1, plan.Tally.Queue);
        Assert.Equal(1, plan.Tally.Running);
        Assert.Equal(1, plan.Tally.Review);
        Assert.Equal(2, plan.Tally.WaitingOnYou);
        Assert.Equal(1, plan.Tally.WaitingOnPlanner);
        Assert.Equal(6, plan.Tally.Pending);
        Assert.Equal(2, plan.Steps.Single(s => s.Step == "step 1").Tally.Done);
        Assert.Equal(6, plan.Steps.Single(s => s.Step == "step 2").Tally.Created);
        Assert.Equal(PlanKind.NoPlan, counts[1].Plan.Kind);
        Assert.Equal(1, counts[1].Tally.Created);
        Assert.Empty(counts[1].Steps);
    }

    // VW-15
    [Fact]
    public void VW_15_Steps_NormalisedAndOrderedByNumber()
    {
        var onMain = Paths("jobs/plans/d_plan.md");
        var cards = new[]
        {
            Card("1-A", ViewFolder.Queue, "jobs/plans/d_plan.md", "Step 10"),
            Card("2-A", ViewFolder.Queue, "jobs/plans/d_plan.md", "step 2."),
            Card("3-A", ViewFolder.Queue, "jobs/plans/d_plan.md", "(5.2)"),
            Card("4-A", ViewFolder.Queue, "jobs/plans/d_plan.md", "STEP 2"),
            Card("5-A", ViewFolder.Queue, "jobs/plans/d_plan.md"),
            Card("6-A", ViewFolder.Queue, "jobs/plans/d_plan.md", "Phase one"),
            Card("7-A", ViewFolder.Queue, "jobs/plans/d_plan.md", "[5.2]"),
        };

        var steps = PlanCounts.Of(cards, Paths(), onMain).Single().Steps;

        Assert.Equal(["step 2", "5.2", "step 10", "phase one", "(no step)"], steps.Select(s => s.Step));
        Assert.Equal(2, steps[0].Tally.Created);
        Assert.Equal(2, steps[1].Tally.Created);
        Assert.Equal("5.2", PlanCounts.NormaliseStep("(5.2)."));
    }
}
