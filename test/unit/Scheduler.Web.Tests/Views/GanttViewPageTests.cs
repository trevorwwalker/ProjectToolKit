// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1132-C: the Gantt view (VW-18, gantt_plan.md section 4): the timeline window and the Details window.</summary>
public sealed class GanttViewPageTests
{
    // The page's now: the pool snapshot's build time. The 24 h window is then [now - 24 h, now + 3 h], 27 hours wide.
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private const string PlanPath = "jobs/plans/gantt_plan.md";

    private static PageSnapshot Page(string project) => new()
    {
        Built = Now,
        Project = project,
        Version = "3.0.99",
        State = new PageStateView { State = SchedulerState.Running, Text = "running" },
        Health = new PageHealthView { Green = true, Text = "main green" },
        Stats = new PageStats { Cards = 0, Minutes = 0, Text = "Last 24 h: none" },
        Waiting =
        [
            new PageWaitingItem
            {
                Kind = PageWaitingKind.Question,
                Name = "2-A",
                Note = "a question",
                Questions = [new CardQuestion { Number = 1, Text = "Which colour for review bars?", Choices = [new CardChoice { Label = "blue" }] }],
            },
        ],
    };

    private static ViewCard Card(string id, ViewFolder folder, string plan = "", string step = "", string type = "feature") => new()
    {
        Id = id,
        Number = long.Parse(id[..id.IndexOf('-')]),
        Category = id[(id.IndexOf('-') + 1)..],
        Title = "Title of " + id,
        Folder = folder,
        PlanPath = plan,
        PlanStep = step,
        CardType = type,
    };

    private static ViewRun Run(string card, DateTimeOffset start, DateTimeOffset? end, string result = "", string model = "claude-sonnet-5-5", DateTimeOffset? timeBoxEnd = null) => new()
    {
        CardId = card,
        Start = start,
        End = end,
        Result = result,
        Model = model,
        WorkerType = "Sonnet",
        TimeBoxEnd = timeBoxEnd,
    };

    private static PoolPageSnapshot Sample()
    {
        var cards = new[]
        {
            Card("1-A", ViewFolder.Done, PlanPath, "section 2"),
            Card("2-A", ViewFolder.Active, PlanPath, "section 2") with { DependsOn = ["1-A"], Worker = "Opus", TimeBoxMinutes = 120 },
            Card("3-B", ViewFolder.Queue, type: "bug"),
            Card("4-B", ViewFolder.Done, "none (planner, 2026-11-01)"),
            Card("5-A", ViewFolder.Queue, PlanPath, "section 1"),
        };
        var runs = new[]
        {
            Run("1-A", Now.AddHours(-6), Now.AddHours(-3), "done"),
            Run("2-A", Now.AddHours(-1), null, model: "claude-opus-5-5", timeBoxEnd: Now.AddHours(1)),
            Run("4-B", Now.AddHours(-2), Now.AddHours(-1.5), "failed"),
        };
        var views = new ProjectViews
        {
            Project = "AmigaX",
            Snapshot = ViewsSnapshot.Build(
                "AmigaX",
                cards,
                [new ViewPlan { Path = PlanPath, Kind = PlanKind.Plans, Title = "Gantt view plan" }],
                new Dictionary<string, string> { [PlanPath] = "# Gantt view plan\n\n## 1. View switch\n\n## 2. Rows and bars\n" },
                runs),
            Running = new HashSet<string>(["2-A"], StringComparer.Ordinal),
            OnMain = new HashSet<string>([PlanPath], StringComparer.Ordinal),
            DoneLoaded = true,
        };
        return new PoolPageSnapshot
        {
            Built = Now,
            MaxWorkers = 4,
            Running = 1,
            Projects = [new PoolProjectView { Name = "AmigaX", Weight = 1, WeightShare = 1, UsedShare = 1 }],
            Pages = [Page("AmigaX")],
            Views = [views],
        };
    }

    private static string Render(string view) =>
        new CombinedPageRenderer(theme: "slate").Render(Sample(), remote: false, controlsAllowed: true, view);

    // The hidden Details block of one row, by its key.
    private static string Details(string html, string key)
    {
        var at = html.IndexOf("data-gdet='" + key + "'", StringComparison.Ordinal);
        Assert.True(at >= 0, "a Details block for " + key);
        var end = html.IndexOf("class='gdet'", at, StringComparison.Ordinal);
        return end < 0 ? html[at..] : html[at..end];
    }

    // 1180-C: a card with no Time box and no typical is estimated at its own project's maxMinutes, not the default 240.
    [Fact]
    public void Estimate_UsesEachProjectsOwnMaxMinutes()
    {
        static ProjectViews Views(string project) => new()
        {
            Project = project,
            Snapshot = ViewsSnapshot.Build(project, [Card("9-A", ViewFolder.Queue) with { Status = "ready" }], [], new Dictionary<string, string>(), []),
            DoneLoaded = true,
        };
        var pool = new PoolPageSnapshot
        {
            Built = Now,
            MaxWorkers = 4,
            Projects = [new PoolProjectView { Name = "Short", Weight = 1 }, new PoolProjectView { Name = "Long", Weight = 1 }, new PoolProjectView { Name = "Bare", Weight = 1 }],
            Pages = [Page("Short") with { MaxMinutes = 90 }, Page("Long") with { MaxMinutes = 150 }, Page("Bare")],
            Views = [Views("Short"), Views("Long"), Views("Bare")],
        };

        var html = new CombinedPageRenderer(theme: "slate").Render(pool, remote: false, controlsAllowed: true, "gantt");

        Assert.Contains("90 min (max minutes)", html, StringComparison.Ordinal);
        Assert.Contains("150 min (max minutes)", html, StringComparison.Ordinal);
        Assert.Contains("240 min (max minutes)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Gantt_GroupRowMarkers_AreWholeEscapes_NotControlCharactersOrStrayHex()
    {
        var html = Render("gantt");

        // 1174-C: the fold markers are the CSS escapes of U+25B8 / U+25BE, a backslash and hex, never a raw control character.
        Assert.Contains("content: '\\25B8  '", html, StringComparison.Ordinal);
        Assert.Contains("details[open] > summary .glabel::before { content: '\\25BE  '", html, StringComparison.Ordinal);
        Assert.DoesNotContain("content: 'B8", html, StringComparison.Ordinal);
        Assert.DoesNotContain("content: 'BE", html, StringComparison.Ordinal);
        Assert.DoesNotContain(html, c => c < ' ' && c is not ('\n' or '\r' or '\t'));
    }

    [Fact]
    public void Gantt_IsAnEntryOfTheViewDropdown_AndShowsBothWindows()
    {
        var html = Render("gantt");

        Assert.Contains("<option value='gantt' selected", html, StringComparison.Ordinal);
        Assert.Contains("id='w-gantt'", html, StringComparison.Ordinal);
        Assert.Contains("id='w-gantt-details'", html, StringComparison.Ordinal);
        Assert.Contains("name='views-version'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<table class='projects'>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id='w-gantt'", Render("workers"), StringComparison.Ordinal);
        Assert.True(html.IndexOf("id='w-gantt'", StringComparison.Ordinal) < html.IndexOf("id='w-gantt-details'", StringComparison.Ordinal), "the timeline first, Details after it");
    }

    [Fact]
    public void Gantt_IsAlwaysGroupedByPlan_WithNoGroupByControl()
    {
        var html = Render("gantt");

        Assert.DoesNotContain("Group by", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ganttgroup", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-group=", html, StringComparison.Ordinal);
        Assert.Contains("id='rangesel'", html, StringComparison.Ordinal);
        Assert.Contains("data-key='g|AmigaX|plan|plan:" + PlanPath + "'", html, StringComparison.Ordinal);
        Assert.Contains("data-key='g|AmigaX|plan|requests'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("|category:", html, StringComparison.Ordinal);
        // Card rows: chip, the number and title as a link, and the plan tag.
        Assert.Contains("href='/card?project=AmigaX&id=1-A'>1-A: Title of 1-A</a>", html, StringComparison.Ordinal);
        Assert.Contains("class='gplan' href='/plan?project=AmigaX&path=jobs%2Fplans%2Fgantt_plan.md'", html, StringComparison.Ordinal);
        Assert.Contains("data-gd='AmigaX|plan|card:1-A'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void OldAddress_WithGroupCategory_StillRendersThePlanGrouping()
    {
        var html = Render("gantt?range=24h&group=category");

        Assert.Contains("data-key='g|AmigaX|plan|plan:" + PlanPath + "'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("|category:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-group=", html, StringComparison.Ordinal);
    }

    // The 24 h window runs from 12:00 the day before to 15:00: a run from 06:00 to 09:00 starts 18 of its 27 hours in.
    [Fact]
    public void Bar_PositionedInTheWindow_ColouredByResult()
    {
        var html = Render("gantt");

        Assert.Contains("<rect class='gbar gb-done' x='66.667' y='1.5' width='11.111' height='7' fill='var(--ok)'>", html, StringComparison.Ordinal);
        Assert.Contains("<rect class='gbar gb-failed' x='81.481' y='1.5' width='1.852' height='7' fill='var(--bad)'>", html, StringComparison.Ordinal);
        Assert.Contains("done, 06:00-09:00, 180 min, claude-sonnet-5-5", html, StringComparison.Ordinal);
    }

    // A running bar grows from its start to now, and its time box is outlined beyond it with its end marked.
    [Fact]
    public void RunningBar_ToNow_WithItsTimeBoxEnd()
    {
        var html = Render("gantt");

        Assert.Contains("<rect class='gbar gb-running' x='85.185' y='1.5' width='3.704'", html, StringComparison.Ordinal);
        Assert.Contains("running since 11:00, 60 min, claude-opus-5-5", html, StringComparison.Ordinal);
        Assert.Contains("<rect class='gtimebox' x='88.889' y='1.5' width='3.704'", html, StringComparison.Ordinal);
        Assert.Contains("<line class='gtbend' x1='92.593'", html, StringComparison.Ordinal);
    }

    // 1328-C: the now line is only in a row with something ahead of now (here the running card's time box), and the tag marks it.
    [Fact]
    public void NowLine_OnlyWhereSomethingIsAhead_AndTheTagMarksIt()
    {
        var html = Render("gantt");

        var lines = System.Text.RegularExpressions.Regex.Matches(html, "<line class='gnow' x1='88.889' x2='88.889'").Count;
        Assert.True(lines >= 1, "the running card's row has one");
        Assert.True(lines < System.Text.RegularExpressions.Regex.Matches(html, "class='grow").Count - 1, "not in every row");
        var past = html[html.IndexOf("data-gd='AmigaX|plan|card:1-A'", StringComparison.Ordinal)..];
        Assert.DoesNotContain("class='gnow'", past[..past.IndexOf("class='grow", 10, StringComparison.Ordinal)], StringComparison.Ordinal);
        var running = html[html.IndexOf("data-gd='AmigaX|plan|card:2-A'", StringComparison.Ordinal)..];
        Assert.Contains("<line class='gnow'", running[..running.IndexOf("</div></div>", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("<span class='gnowtag' style='left: 88.889%'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("more ahead", html, StringComparison.Ordinal);
    }

    // 1328-C: a row's depth indents its label only: no margin or padding on the row or its track, so the tracks line up.
    [Fact]
    public void Tracks_OfEveryDepth_StartAndEndTogether()
    {
        var html = Render("gantt");

        Assert.Contains("<span class='glabel' style='padding-left: 2em'>", html, StringComparison.Ordinal);
        Assert.DoesNotMatch("<div class='(grow|gtrack)[^']*' style=", html);
        Assert.DoesNotMatch("<summary class='grow'[^>]* style=", html);
        Assert.DoesNotContain("margin-left: 1em", html, StringComparison.Ordinal);
    }

    // The range zooms the calendar: hours for a day, ten minutes for an hour, days for two weeks.
    [Theory]
    [InlineData("24h", ">Sun 00:00</span>")]
    [InlineData("1h", ">11:10</span>")]
    [InlineData("2w", ">Sun 22 Nov</span>")]
    public void Range_ZoomsTheCalendar(string range, string tick)
    {
        var html = Render("gantt?range=" + range);

        Assert.Contains("data-range='" + range + "'", html, StringComparison.Ordinal);
        Assert.Contains("<option value='" + range + "' selected", html, StringComparison.Ordinal);
        Assert.Contains(tick, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Details_ForACard_RunsDependsPlanStepWorkerAndQuestions()
    {
        var block = Details(Render("gantt"), "AmigaX|plan|card:2-A");

        Assert.Contains("href='/card?project=AmigaX&id=2-A'>2-A: Title of 2-A</a>", block, StringComparison.Ordinal);
        Assert.Contains("path=jobs%2Fplans%2Fgantt_plan.md'>jobs/plans/gantt_plan.md</a>", block, StringComparison.Ordinal);
        Assert.Contains("#2-rows-and-bars'>2. Rows and bars</a>", block, StringComparison.Ordinal);
        Assert.Contains("Opus", block, StringComparison.Ordinal);
        Assert.Contains("claude-opus-5-5", block, StringComparison.Ordinal);
        Assert.Contains("href='/card?project=AmigaX&id=1-A'>1-A</a>", block, StringComparison.Ordinal);
        Assert.Contains("<td class='nw'>Sun 11:00</td><td class='nw'>running</td><td>running</td><td>60</td>", block, StringComparison.Ordinal);
        Assert.Contains("Which colour for review bars?", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Details_ForAGroup_DoneTotalRunningAndQueued()
    {
        var block = Details(Render("gantt"), "AmigaX|plan|plan:" + PlanPath);

        Assert.Contains("done 1 of 3", block, StringComparison.Ordinal);
        Assert.Contains("Running (1)", block, StringComparison.Ordinal);
        Assert.Contains("id=2-A'", block, StringComparison.Ordinal);
        Assert.Contains("Queued (1)", block, StringComparison.Ordinal);
        Assert.Contains("id=5-A'", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Router_HandsTheRangeAndGroupWithTheView()
    {
        PageRequest Get(string query) => new() { Method = "GET", Path = "/", QueryValues = PageRequest.ParseQuery(query) };

        Assert.Equal("gantt?range=3h&group=plan", PageRouter.ViewOf(Get("view=gantt&range=3h&group=plan")));
        Assert.Equal("gantt", PageRouter.ViewOf(Get("view=gantt")));
        Assert.Null(PageRouter.ViewOf(Get("range=3h")));
    }

    // VW-32: a step row's Details lists its child plans' cards at every depth, by GanttRows' keys.
    [Fact]
    public void VW32_GanttDetails_StepMembersIncludeChildPlans()
    {
        const string Root = "jobs/plans/p_plan.md";
        const string Mid = "jobs/plans/m_plan.md";
        const string Leaf = "jobs/plans/l_plan.md";
        var views = new ProjectViews
        {
            Project = "AmigaX",
            Snapshot = ViewsSnapshot.Build(
                "AmigaX",
                [
                    Card("1-A", ViewFolder.Done, Root, "1"),
                    Card("2-A", ViewFolder.Queue, Mid, "1"),
                    Card("3-A", ViewFolder.Queue, Leaf, "1"),
                ],
                [
                    new ViewPlan { Path = Root, Kind = PlanKind.Plans, Title = "Root" },
                    new ViewPlan { Path = Mid, Kind = PlanKind.Plans, Title = "Mid" },
                    new ViewPlan { Path = Leaf, Kind = PlanKind.Plans, Title = "Leaf" },
                ],
                new Dictionary<string, string>
                {
                    [Root] = "# Root\n\n## 1. One\n",
                    [Mid] = "# Mid\n\nParent: jobs/plans/p_plan.md, step 1\n\n## 1. Inner\n",
                    [Leaf] = "# Leaf\n\nParent: jobs/plans/m_plan.md, step 1\n\n## 1. Deep\n",
                },
                []),
            OnMain = new HashSet<string>([Root, Mid, Leaf], StringComparer.Ordinal),
            DoneLoaded = true,
        };
        var pool = new PoolPageSnapshot
        {
            Built = Now,
            MaxWorkers = 4,
            Projects = [new PoolProjectView { Name = "AmigaX", Weight = 1 }],
            Pages = [Page("AmigaX")],
            Views = [views],
        };

        var html = new CombinedPageRenderer(theme: "slate").Render(pool, remote: false, controlsAllowed: true, "gantt?group=plan");

        var step = Details(html, "AmigaX|plan|plan:" + Root + "/1");
        Assert.Contains("done 1 of 3", step, StringComparison.Ordinal);
        Assert.Contains("id=2-A'", step, StringComparison.Ordinal);
        Assert.Contains("id=3-A'", step, StringComparison.Ordinal);
        Assert.Contains("done 0 of 2", Details(html, "AmigaX|plan|plan:" + Mid), StringComparison.Ordinal);
    }

    // 1318-A: the page shows each step number and card id once, and a step's whole path.
    [Fact]
    public void Numbers_RowsAndDetails_ShowEachNumberAndIdOnce()
    {
        const string Root = "jobs/plans/p_plan.md";
        const string Mid = "jobs/plans/m_plan.md";
        var views = new ProjectViews
        {
            Project = "AmigaX",
            Snapshot = ViewsSnapshot.Build(
                "AmigaX",
                [
                    Card("1-A", ViewFolder.Active, Root, "2") with { Title = "1-A: Design the layout" },
                    Card("2-A", ViewFolder.Active, Mid, "1") with { Title = "2-A: Write the project" },
                ],
                [
                    new ViewPlan { Path = Root, Kind = PlanKind.Plans, Title = "Root" },
                    new ViewPlan { Path = Mid, Kind = PlanKind.Plans, Title = "Mid" },
                ],
                new Dictionary<string, string>
                {
                    [Root] = "# Root\n\n## 1. One\n\n## 2. Build the build system\n",
                    [Mid] = "# Mid\n\nParent: jobs/plans/p_plan.md, step 2\n\n## 1. Write the CMake project\n",
                },
                []),
            OnMain = new HashSet<string>([Root, Mid], StringComparer.Ordinal),
            DoneLoaded = true,
        };
        var pool = new PoolPageSnapshot
        {
            Built = Now,
            MaxWorkers = 4,
            Projects = [new PoolProjectView { Name = "AmigaX", Weight = 1 }],
            Pages = [Page("AmigaX")],
            Views = [views],
        };

        var html = new CombinedPageRenderer(theme: "slate").Render(pool, remote: false, controlsAllowed: true, "gantt?group=plan");

        Assert.Contains("2. Build the build system", html, StringComparison.Ordinal);
        Assert.Contains("2.1. Write the CMake project", html, StringComparison.Ordinal);
        Assert.Contains("1-A: Design the layout", html, StringComparison.Ordinal);
        Assert.DoesNotContain("2 2. ", html, StringComparison.Ordinal);
        Assert.DoesNotContain("1 1. ", html, StringComparison.Ordinal);
        Assert.DoesNotContain("1-A 1-A", html, StringComparison.Ordinal);
        Assert.DoesNotContain("2-A 2-A", html, StringComparison.Ordinal);
        var card = Details(html, "AmigaX|plan|card:2-A");
        Assert.Contains(">2-A: Write the project</a>", card, StringComparison.Ordinal);
        Assert.Contains(">2.1. Write the CMake project</a>", card, StringComparison.Ordinal);
        Assert.Contains(">2. Build the build system</a>", Details(html, "AmigaX|plan|card:1-A"), StringComparison.Ordinal);
    }
}
