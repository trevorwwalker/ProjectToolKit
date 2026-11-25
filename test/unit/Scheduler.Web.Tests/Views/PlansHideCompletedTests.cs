// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1256-C: the Plans page's Hide completed toggle (views.md VW-34) replaces the Completed plans window.</summary>
public sealed class PlansHideCompletedTests
{
    private const string Root = "jobs/plans/root_plan.md";
    private const string Child = "jobs/plans/child_plan.md";

    private static ViewCard Card(string id, ViewFolder folder, string plan, string step) => new()
    {
        Id = id,
        Number = long.Parse(id[..id.IndexOf('-')]),
        Category = id[(id.IndexOf('-') + 1)..],
        Title = "Title of " + id,
        Folder = folder,
        PlanPath = plan,
        PlanStep = step,
        CardType = "feature",
    };

    private static string Page(IEnumerable<ViewCard> cards)
    {
        var texts = new Dictionary<string, string>
        {
            [Root] = "# Root\n\n## 1. First\n\n## 2. Second\n\n## 3. Third\n",
            [Child] = "# Child\n\nParent: jobs/plans/root_plan.md, step 1\n\n## 1. Only\n",
        };
        var plans = texts.Keys.Select(p => new ViewPlan { Path = p, Kind = PlanKind.Plans, Title = p }).ToList();
        var view = new ProjectViews
        {
            Project = "P",
            Snapshot = ViewsSnapshot.Build("P", cards, plans, texts),
            Running = new HashSet<string>(StringComparer.Ordinal),
            OnMain = new HashSet<string>(texts.Keys, StringComparer.Ordinal),
            DoneLoaded = true,
        };
        var pool = new PoolPageSnapshot { Built = DateTimeOffset.UnixEpoch, MaxWorkers = 1, Running = 0, Projects = [], Pages = [], Views = [view] };
        return new CombinedPageRenderer(theme: "slate").Render(pool, remote: false, controlsAllowed: true, "plans");
    }

    private static List<ViewCard> Cards() =>
    [
        Card("10-A", ViewFolder.Queue, Root, "step 1"),
        Card("11-A", ViewFolder.Done, Child, "step 1"),
        Card("12-A", ViewFolder.Done, Root, "step 2"),
        Card("13-A", ViewFolder.Done, Root, "step 3"),
        Card("14-A", ViewFolder.Queue, Root, "step 3"),
    ];

    [Fact]
    public void VW34_CompleteChildPlan_HasDataComplete()
    {
        var html = Page(Cards());

        Assert.Contains("data-key='p|P|" + Child + "' data-complete='1'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW34_CompleteStep_HasDataComplete()
    {
        var html = Page(Cards());

        Assert.Matches(new Regex(@"data-key='s\|P\|" + Regex.Escape(Root) + @"\|2' data-complete='1'|<div class='plan-step' data-complete='1'>.*?2\. Second"), html);
    }

    [Fact]
    public void VW34_IncompleteStepWithDoneCards_HasNoDataComplete_AndItsDoneCardIsDrawn()
    {
        var html = Page(Cards());
        var at = html.IndexOf("data-key='s|P|" + Root + "|3'", StringComparison.Ordinal);

        Assert.True(at >= 0);
        var tag = html[at..html.IndexOf('>', at)];
        Assert.DoesNotContain("data-complete", tag, StringComparison.Ordinal);
        Assert.Contains("13-A", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW34_TopLevelPlan_NeverHasDataComplete_EvenWhenComplete()
    {
        var html = Page([Card("12-A", ViewFolder.Done, Root, "step 1"), Card("13-A", ViewFolder.Done, Root, "step 2"), Card("14-A", ViewFolder.Done, Root, "step 3")]);
        var at = html.IndexOf("data-key='p|P|" + Root + "'", StringComparison.Ordinal);

        Assert.True(at >= 0);
        Assert.DoesNotContain("data-complete", html[at..html.IndexOf('>', at)], StringComparison.Ordinal);
    }

    [Fact]
    public void VW34_Bar_HasTheToggle_AndPageHasNoCompletedWindow()
    {
        var html = Page(Cards());

        Assert.Contains("data-plans-hide-done='1' data-window='w-plans'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("w-plans-completed", html, StringComparison.Ordinal);
        Assert.Contains("#w-plans.plans-hide-done [data-complete] { display: none; }", html, StringComparison.Ordinal);
    }

    [Fact]
    public void VW34_Script_KeepsTheChoiceInLocalStorage_HiddenByDefault_AndReappliesAfterARefresh()
    {
        var html = Page(Cards());

        Assert.Contains("ptk-plans-hide-completed", html, StringComparison.Ordinal);
        Assert.Contains("!== '0'", html, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"function applyPlans\(\) \{\s+applyPlansHide\(\);"), html);
    }
}
