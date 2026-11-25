// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1125-C: the mark of a plan or step is clickable and says when the developer set it; a card's mark is not.</summary>
public sealed class PlanMarkClickTests
{
    private const string Plan = "jobs/plans/m_plan.md";

    private static string Html(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ManualMark>>? marks)
    {
        var card = new ViewCard { Id = "1-C", Number = 1, Category = "C", Title = "T", Folder = ViewFolder.Done, PlanPath = Plan, PlanStep = "step 1" };
        var views = new ProjectViews
        {
            Project = "P",
            Snapshot = ViewsSnapshot.Build("P", [card], [new ViewPlan { Path = Plan, Kind = PlanKind.Plans }], new Dictionary<string, string> { [Plan] = "# M\n\n## A\n\n### a1\n\n## B\n" }, marks: marks),
            OnMain = new HashSet<string>([Plan], StringComparer.Ordinal),
            DoneLoaded = true,
        };
        var page = new PageSnapshot
        {
            Built = DateTimeOffset.UnixEpoch,
            Project = "P",
            Version = "3.0.99",
            State = new PageStateView { State = SchedulerState.Running, Text = "running" },
            Health = new PageHealthView { Green = true, Text = "ok" },
            Stats = new PageStats { Cards = 0, Minutes = 0, Text = "" },
        };
        var pool = new PoolPageSnapshot
        {
            Built = DateTimeOffset.UnixEpoch,
            MaxWorkers = 1,
            Projects = [new PoolProjectView { Name = "P", Weight = 1, WeightShare = 1, UsedShare = 1 }],
            Pages = [page],
            Views = [views],
        };
        return new CombinedPageRenderer(theme: "slate").Render(pool, remote: false, controlsAllowed: true, "plans");
    }

    [Fact]
    public void PlanAndSteps_HaveAClickableMark_WithTheirKey_ButNotTheCards()
    {
        var html = Html(null);

        Assert.Contains("class='pm-click'", html, StringComparison.Ordinal);
        Assert.Contains("data-plan='" + Plan + "' data-step='plan'", html, StringComparison.Ordinal);
        Assert.Contains("data-step='1'", html, StringComparison.Ordinal);
        Assert.Contains("data-step='1.1'", html, StringComparison.Ordinal);
        Assert.Contains("data-step='2'", html, StringComparison.Ordinal);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "class='pm-click'").Count);
        Assert.DoesNotContain("pm-set'>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AManualMark_ShowsTheSetByYouMarker()
    {
        var html = Html(new Dictionary<string, IReadOnlyDictionary<string, ManualMark>> { [Plan] = new Dictionary<string, ManualMark> { ["2"] = ManualMark.Checked } });

        Assert.Contains("set by you: checked", html, StringComparison.Ordinal);
        Assert.Contains("<span class='pm-set'>", html, StringComparison.Ordinal);
    }

    // 1215-C: two states; the tooltip says what a click will do.
    [Fact]
    public void Tooltip_SaysWhatAClickDoes_AndTheScriptFlipsBetweenTwoStates()
    {
        var html = Html(new Dictionary<string, IReadOnlyDictionary<string, ManualMark>> { [Plan] = new Dictionary<string, ManualMark> { ["2"] = ManualMark.Checked } });

        Assert.Contains("set by you: checked; Click to return to automatic", html, StringComparison.Ordinal);
        Assert.Contains("worked out from its cards and steps; Click to check", html, StringComparison.Ordinal);
        Assert.DoesNotContain("'cleared'", PageScript.Html, StringComparison.Ordinal);
    }

    // 1171-C: a pool of two projects, each with the plan; its page as served at /?view=plans.
    private static string PoolHtml()
    {
        ProjectViews View(string project)
        {
            var card = new ViewCard { Id = "1-C", Number = 1, Category = "C", Title = "T", Folder = ViewFolder.Done, PlanPath = Plan, PlanStep = "step 1" };
            return new ProjectViews
            {
                Project = project,
                Snapshot = ViewsSnapshot.Build(project, [card], [new ViewPlan { Path = Plan, Kind = PlanKind.Plans }], new Dictionary<string, string> { [Plan] = "# M\n\n## A\n\n### a1\n\n## B\n" }),
                OnMain = new HashSet<string>([Plan], StringComparer.Ordinal),
                DoneLoaded = true,
            };
        }

        PageSnapshot Page(string project) => new()
        {
            Built = DateTimeOffset.UnixEpoch,
            Project = project,
            Version = "3.0.99",
            State = new PageStateView { State = SchedulerState.Running, Text = "running" },
            Health = new PageHealthView { Green = true, Text = "ok" },
            Stats = new PageStats { Cards = 0, Minutes = 0, Text = "" },
        };

        var pool = new PoolPageSnapshot
        {
            Built = DateTimeOffset.UnixEpoch,
            MaxWorkers = 2,
            Projects = [new PoolProjectView { Name = "AmigaX", Weight = 1, WeightShare = 0.5, UsedShare = 0.5 }, new PoolProjectView { Name = "ProjectToolkit", Weight = 1, WeightShare = 0.5, UsedShare = 0.5 }],
            Pages = [Page("AmigaX"), Page("ProjectToolkit")],
            Views = [View("AmigaX"), View("ProjectToolkit")],
        };
        return new CombinedPageRenderer(theme: "slate").Render(pool, remote: false, controlsAllowed: true, "plans");
    }

    // The statement of the script that starts at `from` (a listener), up to the end of its call.
    private static string Statement(string script, string from, string end)
    {
        var at = script.IndexOf(from, StringComparison.Ordinal);
        Assert.True(at >= 0, "the script has no " + from);
        var stop = script.IndexOf(end, at, StringComparison.Ordinal);
        Assert.True(stop > at, "the statement at " + from + " does not end with " + end);
        return script[at..(stop + end.Length)];
    }

    // The listener whose selector is the mark's class: from its addEventListener to the end of the call.
    private static string MarkHandler(string html)
    {
        var sel = html.IndexOf("closest('.pm-click')", StringComparison.Ordinal);
        Assert.True(sel >= 0, "the script never looks for a .pm-click");
        var at = html.LastIndexOf("document.addEventListener(", sel, StringComparison.Ordinal);
        var stop = html.IndexOf("}, true);", sel, StringComparison.Ordinal);
        Assert.True(at >= 0 && stop > sel, "the .pm-click lookup is not in a listener");
        return html[at..(stop + "}, true);".Length)];
    }

    [Fact]
    public void Card1171_PoolPage_EachProjectsMarks_CarryWhatTheHandlerPosts()
    {
        var html = PoolHtml();
        var handler = MarkHandler(html);

        // The handler's selector is the markup's class, and it reads the three fields the markup carries.
        foreach (var project in new[] { "AmigaX", "ProjectToolkit" })
        {
            Assert.Contains("<span class='pm-click' role='button' tabindex='0' data-project='" + project + "' data-plan='" + Plan + "' data-step='plan'", html, StringComparison.Ordinal);
            Assert.Contains("data-project='" + project + "' data-plan='" + Plan + "' data-step='1.1'", html, StringComparison.Ordinal);
        }

        Assert.Equal(8, System.Text.RegularExpressions.Regex.Matches(html, "<span class='pm-click'").Count);
        foreach (var field in new[] { "project", "plan", "step" })
        {
            Assert.Contains("body.set('" + field + "', m.getAttribute('data-" + field + "')", handler, StringComparison.Ordinal);
        }

        // Delegated on the document (the desk is rebuilt by every refresh), in the capture phase, and the
        // summary's own toggle is prevented; the post is the pool route's.
        Assert.StartsWith("document.addEventListener('click'", handler, StringComparison.Ordinal);
        Assert.EndsWith("}, true);", handler, StringComparison.Ordinal);
        Assert.Contains("e.preventDefault(); e.stopPropagation();", handler, StringComparison.Ordinal);
        Assert.Contains("fetch('/plan-mark', { method: 'POST', body: body", handler, StringComparison.Ordinal);
        Assert.Contains("'X-Requested-With': 'fetch'", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Card1171_ARefusedMark_SaysWhyOnTheNoticeLine_NotASilentReload()
    {
        var handler = MarkHandler(PoolHtml());

        Assert.DoesNotContain("location.reload()", handler, StringComparison.Ordinal);
        Assert.Contains("sayNotice('plan mark not set: '", handler, StringComparison.Ordinal);
        // A refusal by the action comes back through the queue: the click is a pending one, which settles it.
        Assert.Contains("kind: 'plan-mark'", handler, StringComparison.Ordinal);
        Assert.Contains("pending.push(mark);", handler, StringComparison.Ordinal);
        Assert.Contains("if (p.kind === 'plan-mark') return false;", PoolHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void Card1171_ARefresh_WaitsForThePressToEnd_SoTheClickReachesTheMark()
    {
        var html = PoolHtml();

        // The press asks for the live view at once; the desk it brings must not replace the mark under the pointer.
        Assert.Contains("document.addEventListener('pointerdown', interact, true);", html, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('pointerdown', function () { pressAt = Date.now(); }, true);", html, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('pointerup', released, true);", html, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('pointercancel', released, true);", html, StringComparison.Ordinal);
        var refresh = Statement(html, "function refresh(force) {", "var d = new DOMParser()");
        Assert.Contains(".then(afterPress).then(function (t) {", refresh, StringComparison.Ordinal);
        // The waiting desk goes in after the click is delivered (a task later), and never waits past HOLD.
        var released = Statement(html, "function released() {", "}, 0);");
        Assert.Contains("pressAt = 0;", released, StringComparison.Ordinal);
        Assert.EndsWith("setTimeout(function () { w.forEach(function (go) { go(); }); }, 0);", released, StringComparison.Ordinal);
        Assert.Contains("setTimeout(once, HOLD);", Statement(html, "function afterPress(t) {", "});"), StringComparison.Ordinal);
    }
    // 1171-C: the developer clicked marks that are not clickable (a card's, a group's) and saw a text cursor. A
    // clickable mark is boxed under the pointer or focus and has the hand; every other mark of the tree has the arrow.
    [Fact]
    public void Card1171_OnlyTheClickableMarks_LookClickable_InThePlansWindow()
    {
        var html = PoolHtml();
        var desk = html.IndexOf("id='desk'", StringComparison.Ordinal);
        Assert.True(desk >= 0);

        // The rules ride inside the desk (a refresh swaps the desk), once in each window.
        const string open = "<style>.plans-project .pm { cursor: default; }";
        var at = html.IndexOf(open, StringComparison.Ordinal);
        Assert.True(at > desk, "the mark rules are not inside the desk");
        var style = html[at..(html.IndexOf("</style>", at, StringComparison.Ordinal) + "</style>".Length)];
        var left = html.IndexOf("id='w-plans'", StringComparison.Ordinal);
        Assert.DoesNotContain("w-plans-completed", html, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, System.Text.RegularExpressions.Regex.Escape(style)));
        Assert.True(at > left);

        Assert.Contains(".plans-project .pm-click, .plans-project .pm-click .pm { cursor: pointer; }", style, StringComparison.Ordinal);
        Assert.Contains(".pm-click:hover, .pm-click:focus-visible { outline-color: currentColor;", style, StringComparison.Ordinal);

        // A card's mark is a plain mark (not a button), so it gets the arrow; the plan's and steps' are buttons.
        Assert.Contains("<li class='plan-card'><span class='pm pm-ok' title='complete'>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<li class='plan-card'><span class='pm-click'", html, StringComparison.Ordinal);
    }

    // 1213-B: each mark carries its state and the mark its children give, and the handler shows the next state before it
    // posts (so before the fetch resolves); a refusal puts the mark back.
    [Fact]
    public void Card1213_TheMarkFlipsOnClick_BeforeTheFetch_AndARefusalPutsItBack()
    {
        var html = Html(new Dictionary<string, IReadOnlyDictionary<string, ManualMark>> { [Plan] = new Dictionary<string, ManualMark> { ["2"] = ManualMark.Checked } });

        Assert.Contains("data-step='2' data-manual='checked' data-auto='open'", html, StringComparison.Ordinal);
        Assert.Contains("data-step='plan' data-manual='automatic' data-auto=", html, StringComparison.Ordinal);
        var handler = MarkHandler(html);
        var flip = handler.IndexOf("markShow(m, markNext(was));", StringComparison.Ordinal);
        Assert.True(flip >= 0, "the handler does not show the next state");
        Assert.True(flip < handler.IndexOf("fetch('/plan-mark'", StringComparison.Ordinal), "the mark changes only after the fetch");
        Assert.Contains("markShow(m, was);", handler[handler.IndexOf(".catch(", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("function markNext(manual) { return manual === 'automatic' ? 'checked' : 'automatic'; }", html, StringComparison.Ordinal);
    }
}
