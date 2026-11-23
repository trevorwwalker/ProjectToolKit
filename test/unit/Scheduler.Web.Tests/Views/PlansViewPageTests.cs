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

    private static ViewCard Card(string id, ViewFolder folder, string plan, string step = "", string status = "", string type = "feature") => new()
    {
        Id = id,
        Number = long.Parse(id[..id.IndexOf('-')]),
        Category = id[(id.IndexOf('-') + 1)..],
        Title = "Title of " + id,
        Folder = folder,
        PlanPath = plan,
        PlanStep = step,
        Status = status,
        CardType = type,
    };

    private static ProjectViews Views(string project, IEnumerable<ViewCard> cards, IEnumerable<ViewPlan>? plans = null, params string[] running) => new()
    {
        Project = project,
        Snapshot = ViewsSnapshot.Build(project, cards, plans ?? [], new Dictionary<string, string>
        {
            ["jobs/plans/machines_plan.md"] = "# Machines\n\n## Build\n\n### Part\n\n## Two\n\n## Three\n\n## Four\n\n## Five\n",
            ["jobs/archive/old_plan.md"] = "# Old\n\n## Only\n",
        }),
        Running = new HashSet<string>(running, StringComparer.Ordinal),
        OnMain = new HashSet<string>(["jobs/plans/machines_plan.md", "jobs/archive/old_plan.md"], StringComparer.Ordinal),
        DoneLoaded = true,
    };

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
                Card("16-A", ViewFolder.Done, "jobs/archive/old_plan.md", "step 1"),
            ],
            [
                new ViewPlan { Path = "jobs/plans/machines_plan.md", Kind = PlanKind.Plans, Title = "Machines", Status = "Status: draft (planner)" },
                new ViewPlan { Path = "jobs/archive/old_plan.md", Kind = PlanKind.Archived, Title = "Old", Status = "done" },
            ],
            "13-C"),
        Views("Other", [Card("20-A", ViewFolder.Queue, "none")]));

    // The keys (1123-C) are asserted on their own; the tree tests read the markup without them.
    private static string Render(PoolPageSnapshot pool, string? view) =>
        System.Text.RegularExpressions.Regex.Replace(RenderKeyed(pool, view), " data-key='[^']*'", "");

    private static string RenderKeyed(PoolPageSnapshot pool, string? view) =>
        new CombinedPageRenderer(theme: "slate").Render(pool, remote: false, controlsAllowed: true, view);

    // VW-2: Workers and Plans sit in the title bar after the project dropdown and before the theme menu; the current one is pressed.
    [Theory]
    [InlineData(null, "workers")]
    [InlineData("", "workers")]
    [InlineData("nonsense", "workers")]
    [InlineData("board", "workers")]
    [InlineData("plans", "plans")]
    public void VW_2_Switch_PressedIsTheCurrentView(string? view, string current)
    {
        var html = Render(Sample(), view);

        Assert.Contains("href='/?view=workers' class='" + (current == "workers" ? "pressed" : "") + "'", html, StringComparison.Ordinal);
        Assert.Contains("href='/?view=plans' class='" + (current == "plans" ? "pressed" : "") + "'", html, StringComparison.Ordinal);
        Assert.Equal(current == "plans", html.Contains("id='w-plans'", StringComparison.Ordinal));
        var select = html.IndexOf("id='projsel'", StringComparison.Ordinal);
        var workers = html.IndexOf("class='vsw'", StringComparison.Ordinal);
        var theme = html.IndexOf("<span class='updated'>", StringComparison.Ordinal);
        Assert.True(select >= 0 && select < workers && workers < theme, "dropdown, then the switch, then the rest of the bar");
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

    // VW-23: the Plans HTML is built only when asked for.
    [Fact]
    public void VW_23_WorkersView_HasNoPlansHtml()
    {
        var html = Render(Sample(), null);

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

        var old = html.IndexOf("<details class='plan plan-step' data-project='AmigaX'><summary><span class='pm pm-ok' title='complete'>&#10003;</span> ", StringComparison.Ordinal);
        Assert.True(old > 0, "the complete plan is a collapsed details with a green check");
        Assert.Contains("jobs%2Farchive%2Fold_plan.md", html[old..], StringComparison.Ordinal);
    }

    // Steps are numbered links to their heading anchors; a checked step is collapsed, another opens; no cards is a plain line.
    [Fact]
    public void Tree_Steps_AreNumberedLinks_ToAnchors()
    {
        var html = Render(Sample(), "plans");

        var step1 = html.IndexOf("<a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#build'>1. Build</a>", StringComparison.Ordinal);
        var sub = html.IndexOf("#part'>1.1. Part</a>", StringComparison.Ordinal);
        var step5 = html.IndexOf("#five'>5. Five</a>", StringComparison.Ordinal);
        Assert.True(step1 >= 0 && step1 < sub && sub < step5, "1, 1.1, 5");
        Assert.Contains("<details class='plan-step' open><summary><span class='pm' title='not complete'>&#9744;</span> <a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#build'>", html, StringComparison.Ordinal);
        Assert.Contains("<div class='plan-step'><span class='pm' title='not complete'>&#9744;</span> <a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#two'>2. Two</a></div>", html, StringComparison.Ordinal);
        Assert.Contains("<details class='plan-step'><summary><span class='pm pm-ok' title='complete'>&#10003;</span> <a href='/plan?project=AmigaX&path=jobs%2Farchive%2Fold_plan.md#only'>", html, StringComparison.Ordinal);
    }

    // 1121-C: a step whose children are all complete (here its sub-step) is a green check and collapsed; an open child keeps it open.
    [Fact]
    public void Tree_ParentOfCompleteChildren_GreenCheck_Collapsed()
    {
        const string plan = "jobs/plans/machines_plan.md";
        var done = Render(Pool(Views("AmigaX", [Card("1-A", ViewFolder.Done, plan, "step 1.1")], [])), "plans");
        var open = Render(Pool(Views("AmigaX", [Card("1-A", ViewFolder.Done, plan, "step 1.1"), Card("2-A", ViewFolder.Queue, plan, "step 1.1")], [])), "plans");

        Assert.Contains("<details class='plan-step'><summary><span class='pm pm-ok' title='complete'>&#10003;</span> <a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#build'>", done, StringComparison.Ordinal);
        Assert.Contains("<details class='plan-step'><summary><span class='pm pm-ok' title='complete'>&#10003;</span> <a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#part'>", done, StringComparison.Ordinal);
        Assert.Contains("<details class='plan-step' open><summary><span class='pm' title='not complete'>&#9744;</span> <a href='/plan?project=AmigaX&path=jobs%2Fplans%2Fmachines_plan.md#build'>", open, StringComparison.Ordinal);
    }

    // A card line: mark, category chip, then number and title as one link (review to /review, others to /card, id=); no state tag.
    [Fact]
    public void Tree_CardLine_MarkChipLink_NoStateTag()
    {
        var html = Render(Sample(), "plans");

        Assert.Contains("<a href='/review?project=AmigaX&id=12-B'>12-B Title of 12-B</a></li>", html, StringComparison.Ordinal);
        Assert.Contains("<a href='/card?project=AmigaX&id=11-A'>11-A Title of 11-A</a></li>", html, StringComparison.Ordinal);
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
        Assert.Contains("#two'>2. Two</a> &nbsp;<span class='mute'>1/1</span>", html, StringComparison.Ordinal);
    }

    // Cards that match no heading are under "Other cards" of their plan.
    [Fact]
    public void Tree_OtherCards_AfterTheSteps()
    {
        var html = Render(Sample(), "plans");

        var five = html.IndexOf("#five'>", StringComparison.Ordinal);
        var other = html.IndexOf("Other cards", StringComparison.Ordinal);
        Assert.True(five > 0 && five < other, "Other cards follow the steps");
        Assert.Contains("14-A Title of 14-A", html[other..], StringComparison.Ordinal);
    }

    // VW-11: archived plans are folded under "Archived plans"; No plan is last.
    [Fact]
    public void VW_11_ArchivedPlansFolded_NoPlanLast()
    {
        var html = Render(Sample(), "plans");

        var plan = html.IndexOf("jobs/plans/machines_plan.md</b>", StringComparison.Ordinal);
        var archived = html.IndexOf("<details class='plan-archived'><summary>Archived plans (1)</summary>", StringComparison.Ordinal);
        var old = html.IndexOf("jobs/archive/old_plan.md</b>", StringComparison.Ordinal);
        var noPlan = html.IndexOf("<b>No plan</b>", StringComparison.Ordinal);
        Assert.True(plan >= 0 && plan < archived && archived < old && old < noPlan, "plans, archived (folded), No plan");
        Assert.Contains("15-A Title of 15-A", html[noPlan..], StringComparison.Ordinal);
    }

    // 1122-C: each level of the tree indents one step from its parent: the archived plan sits inside the
    // "Archived plans" node carrying the same step indent the plan's own steps use, its step inside it, its card inside that.
    [Fact]
    public void Tree_ArchivedPlan_NestedIndentedInsideTheGroup()
    {
        var html = Render(Sample(), "plans");

        var group = html.IndexOf("<details class='plan-archived'><summary>Archived plans (1)</summary>", StringComparison.Ordinal);
        var plan = html.IndexOf("<details class='plan plan-step' data-project='AmigaX'>", group, StringComparison.Ordinal);
        var step = html.IndexOf("<details class='plan-step'>", plan, StringComparison.Ordinal);
        var card = html.IndexOf("16-A Title of 16-A", step, StringComparison.Ordinal);
        Assert.True(group >= 0 && plan > group && step > plan && card > step, "group, then indented plan, then its step, then its card");

        // "Other cards" and a plain (unarchived) plan keep their existing nesting: the Other cards node is a step inside its plan.
        var machines = html.IndexOf("<details class='plan' data-project='AmigaX' open>", StringComparison.Ordinal);
        var other = html.IndexOf("<details class='plan-step' open><summary><span class='pm' title='not complete'>&#9744;</span> Other cards", machines, StringComparison.Ordinal);
        Assert.True(machines >= 0 && other > machines && other < group, "the plan's Other cards node is a step inside it, before the archived group");
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
\s*applyPlans\(\);").Count);
        Assert.Contains("window.scrollTo(sx, sy);", script, StringComparison.Ordinal);
    }
}
