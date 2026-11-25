// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1091-C: the view switch (VW-2, VW-3) and the Plans view (VW-4, VW-11 to VW-15, VW-23) on the pool page.</summary>
public sealed class PlansViewPageTests
{
    private static readonly DateTimeOffset Built = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static PageSnapshot Page(string project) => new()
    {
        Built = Built,
        Project = project,
        Version = "3.0.99",
        State = new PageStateView { State = SchedulerState.Running, Text = "running" },
        Health = new PageHealthView { Green = true, Text = "main green" },
        Stats = new PageStats { Cards = 0, Minutes = 0, Text = "Last 24 h: none" },
    };

    private static ViewCard Card(string id, ViewFolder folder, string plan, string step = "", string status = "", string type = "feature", string? title = null) => new()
    {
        Id = id,
        Number = long.Parse(id[..id.IndexOf('-')]),
        Category = id[(id.IndexOf('-') + 1)..],
        Title = title ?? "Title of " + id,
        Folder = folder,
        PlanPath = plan,
        PlanStep = step,
        Status = status,
        CardType = type,
    };

    // The plan documents every tree here is drawn from: a one-step plan, and a five-step plan with a sub-step.
    private static readonly Dictionary<string, string> PlanTexts = new(StringComparer.Ordinal)
    {
        ["jobs/plans/old_plan.md"] = "# Old\n\n## Only\n",
        ["jobs/plans/machines_plan.md"] = "# Machines\n\n## Build\n\n### Part\n\n## Two\n\n## Three\n\n## Four\n\n## Five\n",
    };

    private static ProjectViews Views(string project, IEnumerable<ViewCard> cards, IEnumerable<ViewPlan>? plans = null, params string[] running) =>
        Views(project, cards, plans, new Dictionary<string, string>(StringComparer.Ordinal), running);

    // 1289-C: the same, with plan documents of a test's own added (here a plan written with numbered headings).
    private static ProjectViews Views(string project, IEnumerable<ViewCard> cards, IEnumerable<ViewPlan>? plans, IReadOnlyDictionary<string, string> extra, params string[] running)
    {
        var texts = new Dictionary<string, string>(PlanTexts, StringComparer.Ordinal);
        foreach (var (path, text) in extra)
        {
            texts[path] = text;
        }

        return new ProjectViews
        {
            Project = project,
            Snapshot = ViewsSnapshot.Build(project, cards, plans ?? [], texts),
            Running = new HashSet<string>(running, StringComparer.Ordinal),
            OnMain = new HashSet<string>(["jobs/plans/machines_plan.md", "jobs/plans/old_plan.md"], StringComparer.Ordinal),
            DoneLoaded = true,
        };
    }

    private static PoolPageSnapshot Pool(params ProjectViews[] views) => new()
    {
        Built = Built,
        MaxWorkers = 4,
        Running = 0,
        Projects = views.Select(v => new PoolProjectView { Name = v.Project, Weight = 1, WeightShare = 0.5, UsedShare = 0.5 }).ToList(),
        Pages = views.Select(v => Page(v.Project) with
        {
            Waiting = v.Project == "AmigaX" ? [new PageWaitingItem { Kind = PageWaitingKind.Question, Name = "120-A-dispatch.md", Note = "which?" }] : [],
        }).ToList(),
        Views = views,
    };

    private static PoolPageSnapshot Sample() => Pool(
        Views(
            "AmigaX",
            [
                Card("10-A", ViewFolder.Done, "jobs/plans/machines_plan.md", "step 1"),
                Card("11-A", ViewFolder.Queue, "jobs/plans/machines_plan.md", "Step 1."),
                Card("12-B", ViewFolder.Review, "jobs/plans/machines_plan.md", "step 5"),
                Card("13-C", ViewFolder.Active, "jobs/plans/machines_plan.md", "step 5"),
                Card("14-A", ViewFolder.Queue, "jobs/plans/machines_plan.md"),
                Card("15-A", ViewFolder.Done, "none (planner, 2026-11-01)", type: ""),
                Card("16-A", ViewFolder.Done, "jobs/plans/old_plan.md", "step 1"),
                Card("17-A", ViewFolder.Done, "docs/design/old.md", "step 1"),
            ],
            [
                new ViewPlan { Path = "jobs/plans/machines_plan.md", Kind = PlanKind.Plans, Title = "Machines", Status = "Status: draft (planner)" },
                new ViewPlan { Path = "jobs/plans/old_plan.md", Kind = PlanKind.Plans, Title = "Old", Status = "done" },
            ],
            "13-C"),
        Views("Other", [Card("20-A", ViewFolder.Queue, "none")]));

    // The keys (1123-C) are asserted on their own; the tree tests read the markup without them.
    private static string Render(PoolPageSnapshot pool, string? view) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(RenderKeyed(pool, view), " data-key='[^']*'", ""),
            "<span class='pm-click'[^>]*>(<span class='pm[^']*' title='[^']*'>[^<]*</span>)(<span class='pm-set'>[^<]*</span>)?</span>", "$1");

    private static string RenderKeyed(PoolPageSnapshot pool, string? view) =>
        new CombinedPageRenderer(theme: "slate").Render(pool, remote: false, controlsAllowed: true, view);

    // VW-2 (1129-C): the views dropdown sits in the title bar after the project dropdown and before the
    // theme menu, and lists every view with the current one selected.
    [Theory]
    [InlineData(null, "plans")]
    [InlineData("", "plans")]
    [InlineData("nonsense", "plans")]
    [InlineData("board", "plans")]
    [InlineData("plans", "plans")]
    [InlineData("workers", "workers")]
    public void VW_2_Switch_SelectedIsTheCurrentView(string? view, string current)
    {
        var html = Render(Sample(), view);

        Assert.Contains("<option value='" + current + "' selected", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<option value='" + (current == "plans" ? "workers" : "plans") + "' selected", html, StringComparison.Ordinal);
        Assert.Equal(current == "plans", html.Contains("id='w-plans'", StringComparison.Ordinal));
        var select = html.IndexOf("id='projsel'", StringComparison.Ordinal);
        var views = html.IndexOf("id='viewsel'", StringComparison.Ordinal);
        var theme = html.IndexOf("<span class='updated'>", StringComparison.Ordinal);
        Assert.True(select >= 0 && select < views && views < theme, "project dropdown, then the views dropdown, then the rest of the bar");
    }

    // 1114-C: the Plans view keeps the title bar and shows only the Plans window; Workers keeps its table and Waiting on you.
    [Fact]
    public void VW_3_PlansView_OnlyThePlansWindowUnderTheTitleBar()
    {
        var plans = Render(Sample(), "plans");
        var workers = Render(Sample(), "workers");

        Assert.Contains("Pause all", plans, StringComparison.Ordinal);
        Assert.Contains("Drain and exit", plans, StringComparison.Ordinal);
        Assert.DoesNotContain("id='w-waiting'", plans, StringComparison.Ordinal);
        Assert.DoesNotContain("<table class='projects'>", plans, StringComparison.Ordinal);
        Assert.Contains("id='w-waiting'", workers, StringComparison.Ordinal);
        Assert.Contains("<table class='projects'>", workers, StringComparison.Ordinal);
        Assert.Contains("id='w-plans'", plans, StringComparison.Ordinal);
        Assert.DoesNotContain("id='w-plans'", workers, StringComparison.Ordinal);
        Assert.Contains("name='views-version'", plans, StringComparison.Ordinal);
    }

    // 1256-C (VW-34), replacing the 1127-C test of the Completed plans window: every top-level plan is drawn in the one window, complete or not.
    [Fact]
    public void CompletedPlans_WindowIsGone_EveryTopLevelPlanIsInThePlansWindow()
    {
        var html = Render(Sample(), "plans");

        Assert.DoesNotContain("w-plans-completed", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Completed plans", html, StringComparison.Ordinal);
        Assert.Contains("jobs/plans/machines_plan.md", html, StringComparison.Ordinal);
        Assert.Contains("jobs/plans/old_plan.md", html, StringComparison.Ordinal);
        Assert.Contains("<b>Requests</b>", html, StringComparison.Ordinal);
    }

    // 1256-C (VW-34), replacing the 1127-C test of a plan checked by a click: it stays in the tree, a top-level plan never has data-complete.
    [Fact]
    public void CompletedPlans_APlanCheckedByAClick_StaysInTheTree_WithTheClickableMark()
    {
        const string plan = "jobs/plans/machines_plan.md";
        var marks = new Dictionary<string, IReadOnlyDictionary<string, ManualMark>> { [plan] = new Dictionary<string, ManualMark> { ["plan"] = ManualMark.Checked } };
        var views = new ProjectViews
        {
            Project = "P",
            Snapshot = ViewsSnapshot.Build("P", [Card("1-A", ViewFolder.Queue, plan, "step 1")], [new ViewPlan { Path = plan, Kind = PlanKind.Plans }],
                new Dictionary<string, string> { [plan] = "# M\n\n## Build\n" }, marks: marks),
            OnMain = new HashSet<string>([plan], StringComparer.Ordinal),
            DoneLoaded = true,
        };
        var html = RenderKeyed(Pool(views), "plans");

        Assert.Contains(plan, html, StringComparison.Ordinal);
        Assert.Contains("class='pm-click'", html, StringComparison.Ordinal);
        Assert.Contains("data-key='p|P|" + plan + "'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-key='p|P|" + plan + "' data-complete", html, StringComparison.Ordinal);
    }

    // VW-23: the Plans HTML is built only when asked for.
    [Fact]
    public void VW_23_WorkersView_HasNoPlansHtml()
    {
        var html = Render(Sample(), "workers");

        Assert.DoesNotContain("machines_plan.md", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class='plan'", html, StringComparison.Ordinal);
    }

    // The plan is a mark, a link to its document and done/total; an incomplete plan is open.
    [Fact]
    public void Tree_IncompletePlan_IsOpen_WithMarkLinkAndCount()
    {
        var html = Render(Sample(), "plans");

        Assert.Contains("<details class='plan' data-project='AmigaX' open><summary><span class='pm' title='not complete'>&#9744;</span> ", html, StringComparison.Ordinal);
        Assert.Contains("<a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md'><b>jobs/plans/machines_plan.md</b></a>", html, StringComparison.Ordinal);
        Assert.Contains("<span class='mute'>1/5</span></summary>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class='plan-bar'", html, StringComparison.Ordinal);
    }

    // A complete plan shows a green check and is collapsed (no open attribute).
    [Fact]
    public void Tree_CompletePlan_GreenCheck_Collapsed()
    {
        var html = Render(Sample(), "plans");

        var old = html.IndexOf("<details class='plan' data-project='AmigaX'><summary><span class='pm pm-ok' title='complete'>&#10003;</span> ", StringComparison.Ordinal);
        Assert.True(old > 0, "the complete plan is a collapsed details with a green check");
        Assert.Contains("jobs%2Fplans%2Fold_plan.md", html[old..], StringComparison.Ordinal);
    }

    // Steps are numbered links to their heading anchors; a checked step is collapsed, another opens; no cards is a plain line.
    [Fact]
    public void Tree_Steps_AreNumberedLinks_ToAnchors()
    {
        var html = Render(Sample(), "plans");

        var step1 = html.IndexOf("<a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#build' title='step 1 of jobs/plans/machines_plan.md'>1. Build</a>", StringComparison.Ordinal);
        var sub = html.IndexOf("#part' title='step 1.1 of jobs/plans/machines_plan.md'>1.1. Part</a>", StringComparison.Ordinal);
        var step5 = html.IndexOf("#five' title='step 5 of jobs/plans/machines_plan.md'>5. Five</a>", StringComparison.Ordinal);
        Assert.True(step1 >= 0 && step1 < sub && sub < step5, "1, 1.1, 5");
        Assert.Contains("<details class='plan-step' open><summary><span class='pm' title='not complete'>&#9744;</span> <a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#build'", html, StringComparison.Ordinal);
        Assert.Contains("<div class='plan-step'><span class='pm' title='not complete'>&#9744;</span> <a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#two' title='step 2 of jobs/plans/machines_plan.md'>2. Two</a></div>", html, StringComparison.Ordinal);
        Assert.Contains("<details class='plan-step' data-complete='1'><summary><span class='pm pm-ok' title='complete'>&#10003;</span> <a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fold_plan.md#only'", html, StringComparison.Ordinal);
    }

    // 1289-C: a plan written with numbered headings ("## 2. Build the job system") already carries the number in its
    // title, so the Plans view must not draw the step's label again: "2. Build ...", never "2. 2. Build ...".
    [Fact]
    public void Tree_ANumberedStepTitle_ShowsItsNumberOnce()
    {
        var html = RenderedNumberedPlan("# Numbered\n\n## 2. Build the job system: cards\n\n## 7. Bring the other projects in the pool up\n");

        Assert.Contains(">2. Build the job system: cards</a>", html, StringComparison.Ordinal);
        Assert.Contains(">7. Bring the other projects in the pool up</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("2. 2. Build", html, StringComparison.Ordinal);
        Assert.DoesNotContain("7. 7. Bring", html, StringComparison.Ordinal);
    }

    // A plan written the old way (headings with no number) is unchanged: the view's own number, then the title.
    [Fact]
    public void Tree_AnUnnumberedStepTitle_StillGetsTheViewsNumber()
    {
        var html = RenderedNumberedPlan("# Plain\n\n## One\n\n## Two\n\n## Trends\n");

        Assert.Contains(">3. Trends</a>", html, StringComparison.Ordinal);
    }

    // A sub-step of a numbered plan is "n.m" once, too (VW-24 numbers it; the heading may repeat it).
    [Fact]
    public void Tree_ANumberedSubStepTitle_ShowsItsNumberOnce()
    {
        var html = RenderedNumberedPlan("# Numbered\n\n## 1. Main\n\n### 1.2 Sub thing\n");

        Assert.Contains(">1.2. Sub thing</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("1.2. 1.2", html, StringComparison.Ordinal);
    }

    // A heading whose words merely begin with the label's digits keeps them: only the label itself is dropped.
    [Fact]
    public void Tree_AStepTitleThatOnlyLooksLikeANumber_IsLeftAlone()
    {
        Assert.Contains(">4. 404 pages</a>", RenderedNumberedPlan("# A\n\n## 4. 404 pages\n"), StringComparison.Ordinal);
        Assert.Contains(">2. 2x Speed</a>", RenderedNumberedPlan("# B\n\n## One\n\n## 2x Speed\n"), StringComparison.Ordinal);
    }

    private static string RenderedNumberedPlan(string text)
    {
        const string path = "jobs/plans/numbered_plan.md";
        var views = Views("AmigaX", [], [new ViewPlan { Path = path, Kind = PlanKind.Plans, Title = "Numbered" }], new Dictionary<string, string>(StringComparer.Ordinal) { [path] = text });
        return Render(Pool(views), "plans");
    }

    // 1289-C: a card's title line is "# 1274-B: After a restart ...", so the id was drawn twice on its row.
    [Fact]
    public void Tree_ACardTitleBeginningWithItsId_ShowsTheIdOnce()
    {
        var html = RenderedCard("1274-B", "1274-B: After a restart");

        Assert.Contains("<a href='/card?project=AmigaX&id=1274-B'>1274-B: After a restart</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("1274-B 1274-B", html, StringComparison.Ordinal);
    }

    // The card's id is shown as "id: title" when the title does not repeat it.
    [Fact]
    public void Tree_ACardTitleWithoutItsId_IsDrawnAfterAColon()
    {
        var html = RenderedCard("31-A", "Trends panel");

        Assert.Contains(">31-A: Trends panel</a>", html, StringComparison.Ordinal);
    }

    // A card with no title at all is its id alone.
    [Fact]
    public void Tree_ACardWithNoTitle_IsItsIdAlone()
    {
        var html = RenderedCard("32-A", "");

        Assert.Contains(">32-A</a>", html, StringComparison.Ordinal);
    }

    private static string RenderedCard(string id, string title)
    {
        var views = Views("AmigaX", [Card(id, ViewFolder.Queue, "jobs/plans/machines_plan.md", title: title)]);
        return Render(Pool(views), "plans");
    }

    // 1121-C: a step whose children are all complete (here its sub-step) is a green check and collapsed; an open child keeps it open.
    [Fact]
    public void Tree_ParentOfCompleteChildren_GreenCheck_Collapsed()
    {
        const string plan = "jobs/plans/machines_plan.md";
        var done = Render(Pool(Views("AmigaX", [Card("1-A", ViewFolder.Done, plan, "step 1.1")], [])), "plans");
        var open = Render(Pool(Views("AmigaX", [Card("1-A", ViewFolder.Done, plan, "step 1.1"), Card("2-A", ViewFolder.Queue, plan, "step 1.1")], [])), "plans");

        Assert.Contains("<details class='plan-step' data-complete='1'><summary><span class='pm pm-ok' title='complete'>&#10003;</span> <a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#build'", done, StringComparison.Ordinal);
        Assert.Contains("<details class='plan-step' data-complete='1'><summary><span class='pm pm-ok' title='complete'>&#10003;</span> <a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#part'", done, StringComparison.Ordinal);
        Assert.Contains("<details class='plan-step' open><summary><span class='pm' title='not complete'>&#9744;</span> <a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#build'", open, StringComparison.Ordinal);
    }

    // A card line: mark, category chip, then id and title once each as one link (review to /review, others to /card, id=); no state tag.
    [Fact]
    public void Tree_CardLine_MarkChipLink_NoStateTag()
    {
        var html = Render(Sample(), "plans");

        Assert.Contains("<a href='/review?project=AmigaX&id=12-B'>12-B: Title of 12-B</a></li>", html, StringComparison.Ordinal);
        Assert.Contains("<a href='/card?project=AmigaX&id=11-A'>11-A: Title of 11-A</a></li>", html, StringComparison.Ordinal);
        Assert.Contains("<li class='plan-card'><span class='pm pm-ok' title='complete'>&#10003;</span> ", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<span class='mute'>queue</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("running</span>", html, StringComparison.Ordinal);
    }

    // A cancelled card has a red X and is left out of the step's count.
    [Fact]
    public void Tree_CancelledCard_RedX_NotCounted()
    {
        var views = Views(
            "AmigaX",
            [
                Card("10-A", ViewFolder.Done, "jobs/plans/machines_plan.md", "step 2"),
                Card("11-A", ViewFolder.Archive, "jobs/plans/machines_plan.md", "step 2", status: "closed: replaced by 12-A"),
            ]);

        var html = Render(Pool(views), "plans");

        Assert.Contains("<span class='pm pm-bad' title='cancelled or errored'>&#10007;</span> ", html, StringComparison.Ordinal);
        Assert.Contains("#two' title='step 2 of jobs/plans/machines_plan.md'>2. Two</a> &nbsp;<span class='mute'>1/1</span>", html, StringComparison.Ordinal);
    }

    // Cards that match no heading are under "Other cards" of their plan.
    [Fact]
    public void Tree_OtherCards_AfterTheSteps()
    {
        var html = Render(Sample(), "plans");

        var five = html.IndexOf("#five'", StringComparison.Ordinal);
        var other = html.IndexOf("Other cards", StringComparison.Ordinal);
        Assert.True(five > 0 && five < other, "Other cards follow the steps");
        Assert.Contains("14-A: Title of 14-A", html[other..], StringComparison.Ordinal);
    }

    // VW-11: only jobs/plans/ files are plans; a card naming another path sits under No plan, last, with its path shown.
    [Fact]
    public void VW_11_OtherPathIsNoPlan_ShowsItsPath_NoArchivedGroup()
    {
        var html = Render(Sample(), "plans");

        var plan = html.IndexOf("jobs/plans/machines_plan.md</b>", StringComparison.Ordinal);
        var noPlan = html.IndexOf("<b>Plan not found</b>", StringComparison.Ordinal);
        Assert.True(plan >= 0 && plan < noPlan, "the plan, then Plan not found");
        Assert.DoesNotContain("Archived plans", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>docs/design/old.md</b>", html, StringComparison.Ordinal);
        var card = html.IndexOf("17-A: Title of 17-A</a> <span class='mute'>Plan: docs/design/old.md</span>", noPlan, StringComparison.Ordinal);
        Assert.True(card > noPlan, "the card lists the path it names under No plan");
    }

    // 1128-C: Bugs and Requests under the plans: category, then month newest first; a finished month is collapsed, an open one shows done/total;
    // both stay in the Plans window; a card naming a non-plan path is in "Plan not found".
    [Fact]
    public void BugsAndRequests_ByCategoryAndMonth_FinishedMonthFolded_StayLeft_PlanNotFoundLast()
    {
        var run = (string id, int month) => new ViewRun { CardId = id, Start = new DateTimeOffset(2026, month, 5, 9, 0, 0, TimeSpan.Zero), End = new DateTimeOffset(2026, month, 5, 10, 0, 0, TimeSpan.Zero) };
        var views = new ProjectViews
        {
            Project = "P",
            Snapshot = ViewsSnapshot.Build(
                "P",
                [
                    Card("20-A", ViewFolder.Done, "none", type: "bug"),
                    Card("21-A", ViewFolder.Queue, "", type: "bug"),
                    Card("22-A", ViewFolder.Done, "none", type: "feature"),
                    Card("23-A", ViewFolder.Done, "docs/design/gone.md", type: "feature"),
                ],
                [],
                runs: [run("20-A", 10), run("21-A", 11), run("22-A", 10)]),
            OnMain = new HashSet<string>(StringComparer.Ordinal),
            DoneLoaded = true,
        };
        var html = RenderKeyed(Pool(views), "plans");
        var left = html;

        Assert.Contains("<b>Bugs</b>", left, StringComparison.Ordinal);
        Assert.Contains("<b>Requests</b>", left, StringComparison.Ordinal);
        Assert.True(left.IndexOf("<b>Bugs</b>", StringComparison.Ordinal) < left.IndexOf("<b>Requests</b>", StringComparison.Ordinal));
        Assert.True(left.IndexOf("<b>Requests</b>", StringComparison.Ordinal) < left.IndexOf("<b>Plan not found</b>", StringComparison.Ordinal));
        // Bugs: November (open, 0/1) before October (done, folded).
        Assert.True(left.IndexOf("2026-11", StringComparison.Ordinal) < left.IndexOf("2026-10", StringComparison.Ordinal));
        Assert.Contains("<details class='plan-step' data-key='g|P|Bugs|A|2026-11' open>", left, StringComparison.Ordinal);
        Assert.Contains("<details class='plan-step' data-key='g|P|Bugs|A|2026-10'>", left, StringComparison.Ordinal);
        Assert.Contains("<details class='plan' data-project='P' data-key='g|P|Bugs' open>", left, StringComparison.Ordinal);
        // Requests: its only month is finished, so the whole group is folded.
        Assert.Contains("<details class='plan' data-project='P' data-key='g|P|Requests'>", left, StringComparison.Ordinal);
        Assert.Contains("23-A: Title of 23-A</a> <span class='mute'>Plan: docs/design/gone.md</span>", left, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>No plan</b>", html, StringComparison.Ordinal);
    }

    // VW-4: every plan carries data-project so the dropdown filters it; under two projects each shows its chip.
    [Fact]
    public void VW_4_EveryPlanCarriesDataProject()
    {
        var html = Render(Sample(), "plans");

        Assert.Contains("<div class='plans-project' data-project='AmigaX'>", html, StringComparison.Ordinal);
        Assert.Contains("<div class='plans-project' data-project='Other'>", html, StringComparison.Ordinal);
        Assert.Contains("<details class='plan' data-project='Other' open>", html, StringComparison.Ordinal);
        Assert.Contains("projchip", html, StringComparison.Ordinal);
    }

    // VW-21: a project whose done cards are still being read says so.
    [Fact]
    public void VW_21_ReadingNote_IsShown()
    {
        var views = Views("AmigaX", [Card("10-A", ViewFolder.Queue, "none")]) with { Note = ProjectViews.ReadingDone, DoneLoaded = false };

        var html = Render(Pool(views), "plans");

        Assert.Contains("reading done cards...", html, StringComparison.Ordinal);
    }

    // 1123-C: every details of the tree has a stable key, so a refresh can put back what the developer toggled.
    [Fact]
    public void Tree_EveryDetails_HasAKey_AndKeysAreUnique()
    {
        var html = RenderKeyed(Sample(), "plans");

        var tags = System.Text.RegularExpressions.Regex.Matches(html, "<details [^>]*>").Select(m => m.Value).ToList();
        Assert.NotEmpty(tags);
        Assert.All(tags, t => Assert.Contains(" data-key='", t, StringComparison.Ordinal));
        var keys = tags.Select(t => System.Text.RegularExpressions.Regex.Match(t, "data-key='([^']*)'").Groups[1].Value).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // 1140-C: main was red because the plan-level node lost the key 1123-C's script remembers a toggle by.
    // The key is the project and the plan's own path, so a plan keeps its open state across a refresh like every other node.
    [Fact]
    public void Tree_PlanNode_KeyIsTheProjectAndThePlanPath()
    {
        var html = RenderKeyed(Sample(), "plans");

        Assert.Contains("<details class='plan' data-project='AmigaX' data-key='p|AmigaX|jobs/plans/machines_plan.md'", html, StringComparison.Ordinal);
        // The same path under two projects must not share a key, so each node carries its own project.
        Assert.Contains("<details class='plan' data-project='Other' data-key='g|Other|Requests'", html, StringComparison.Ordinal);
    }

    // 1123-C: the page script remembers a toggle in sessionStorage by key and applies it after a swap; an untouched node is left to the server.
    [Fact]
    public void Script_RemembersToggles_AndAppliesThemAfterTheSwapAndTheLoad()
    {
        var script = PageScript.Html;

        Assert.Contains("sessionStorage.setItem('plans-open:' + k, v)", script, StringComparison.Ordinal);
        Assert.Contains("details[data-key] > summary", script, StringComparison.Ordinal);
        Assert.Contains("if (v === '1' && !d.open) d.open = true;", script, StringComparison.Ordinal);
        Assert.Contains("else if (v === '0' && d.open) d.open = false;", script, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(script, @"
?
\s*applyPlans\(\);").Count);
        Assert.Contains("window.scrollTo(sx, sy);", script, StringComparison.Ordinal);
    }

    // 1162-C (VW-3): the Plans view sits on the full-width desk the Gantt view had, so the title bar stretches
    // across the window and the two plan panels ride beneath it in the pair. Workers keeps the two-column desk.
    [Fact]
    public void WideDesk_PlansAndGantt_GetIt_WorkersDoesNot()
    {
        var plans = Render(Sample(), "plans");
        var gantt = Render(Sample(), "gantt");
        var workers = Render(Sample(), "workers");

        Assert.Contains("<div class='desk wide-desk' id='desk'>", plans, StringComparison.Ordinal);
        Assert.Contains("<div class='desk wide-desk' id='desk'>", gantt, StringComparison.Ordinal);
        Assert.Contains("<div class='desk' id='desk'>", workers, StringComparison.Ordinal);
        Assert.DoesNotContain("<div class='desk wide-desk'", workers, StringComparison.Ordinal);
        Assert.DoesNotContain("gantt-desk", plans, StringComparison.Ordinal);
        Assert.DoesNotContain("gantt-desk", gantt, StringComparison.Ordinal);
    }

    // 1162-C, 1256-C: the Plans window is inside the pair, alone (the Completed plans window is gone).
    [Fact]
    public void WideDesk_PlansPair_HoldsThePlansWindow()
    {
        var plans = Render(Sample(), "plans");

        var pair = plans.IndexOf("<div class='plans-pair'>", StringComparison.Ordinal);
        var wide = plans.IndexOf("id='w-plans'", StringComparison.Ordinal);
        Assert.True(pair >= 0, "the pair div is drawn");
        Assert.True(pair < wide, "Plans is inside the pair");
        Assert.DoesNotContain("w-plans-completed", plans, StringComparison.Ordinal);
        Assert.True(wide < plans.IndexOf("</body>", StringComparison.Ordinal), "and the pair is closed after it");
    }

    // 1162-C: the pair rules are shared, not copied: one rule lays out both pairs, and the plans windows take
    // the Gantt window proportions (the wide panel 3, the narrow one 1).
    [Fact]
    public void WideDesk_PairCss_IsSharedWithTheGantt()
    {
        var plans = Render(Sample(), "plans");

        Assert.Contains(".plans-pair, .gantt-pair { display: flex; flex-wrap: wrap; gap: 12px; align-items: flex-start; }", plans, StringComparison.Ordinal);
        Assert.Contains("#w-gantt { flex: 3 1 640px; min-width: 0; } #w-plans { flex: 1 1 100%; min-width: 0; } #w-gantt-details { flex: 1 1 300px; min-width: 0; }", plans, StringComparison.Ordinal);
        Assert.Contains(".desk.wide-desk { grid-template-columns: minmax(0, 1fr); grid-template-areas: \"top\" \"main\"; }", plans, StringComparison.Ordinal);
    }
}
