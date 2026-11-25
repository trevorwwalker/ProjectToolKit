// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// 520-A item 5 (the developer, 2026-11-18): the windows read Control, Waiting on you, Requests to
/// the planner, then the rest, in every layout, and a Waiting on you or Requests window with
/// nothing in it is not rendered at all.
/// </summary>
public sealed class PageWindowOrderTests
{
    private static string Render(PageSnapshot snapshot) =>
        new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);

    // The Control window leads the main column by the order its own tag carries (520-A item 5); the
    // common style no longer reorders the windows (520-A Q2, the developer's answer, done by 590-C).
    [Fact]
    public void Page_ControlLeadsTheMainColumn()
    {
        var html = Render(ViewsFixture.Full());

        var control = html.IndexOf("id='w-control'", StringComparison.Ordinal);
        var waiting = html.IndexOf("id='w-waiting'", StringComparison.Ordinal);
        var requests = html.IndexOf("id='w-requests'", StringComparison.Ordinal);
        Assert.True(control >= 0 && waiting > control && requests > waiting, "Control, Waiting, Requests order");
        Assert.Contains("id='w-control' style='order: -2'", html, StringComparison.Ordinal);
    }

    // PAGE-5 with nothing waiting and nothing asked: neither window is drawn, so there is no
    // "Nothing." or "None open." box.
    [Fact]
    public void Page_EmptyWaitingAndRequests_AreNotRendered()
    {
        var html = Render(ViewsFixture.Plain());

        Assert.DoesNotContain("Waiting on you", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Requests to the planner", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("None open.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id='w-waiting'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id='w-requests'", html, StringComparison.Ordinal);

        // The rest of the page is unaffected: the Control window is still there.
        Assert.Contains("id='w-control'", html, StringComparison.Ordinal);
    }

    // Both windows come back when something arrives in them.
    [Fact]
    public void Page_WindowsAppear_WhenTheyHaveSomething()
    {
        var html = Render(ViewsFixture.Full());

        Assert.Contains(">Waiting on you<", html, StringComparison.Ordinal);
        Assert.Contains(">Requests to the planner<", html, StringComparison.Ordinal);
    }

    // Requests to the planner is still no window when only the request list is empty and no card
    // waits on the planner; the planner view may carry other facts that are not this window's.
    [Fact]
    public void Page_PlannerViewWithNothingOpen_IsNotRendered()
    {
        var html = Render(ViewsFixture.Plain() with
        {
            Planner = new PagePlannerView { AllDone = true },
        });

        Assert.DoesNotContain("Requests to the planner", html, StringComparison.Ordinal);
    }

    // The combined page: an empty Waiting on you is no window, and the pool's own line leads.
    [Fact]
    public void CombinedPage_EmptyWaitingIsNotRendered_AndPoolLeads()
    {
        var snapshot = new PageSnapshot
        {
            Built = ViewsFixture.Built,
            Project = "ProjectToolkit",
            Version = ViewsFixture.ShownVersion,
            State = new PageStateView { State = Scheduler.Core.Contracts.SchedulerState.Running, Text = "running" },
        };
        var html = new CombinedPageRenderer().Render([snapshot], "slate", remote: false, controlsAllowed: true);

        Assert.DoesNotContain("Waiting on you", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing.", html, StringComparison.Ordinal);
        Assert.Contains("id='w-projects' data-view='projects' style='order: -2'", html, StringComparison.Ordinal);
    }
}
