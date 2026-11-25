// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>PAGE-5: every window and button the page carries, from a snapshot with one of everything.</summary>
public sealed class PageWindowTests
{
    // PAGE-5
    [Fact]
    public void Page_Windows_Present()
    {
        var html = new PageRenderer().Render(ViewsFixture.Full(), remote: false, controlsAllowed: true);

        // The Control window's title bar carries what the top bar used to (520-A): the scheduler's
        // own line on the left, the theme selector and the last update on the right.
        Assert.Contains("ProjectToolkit", html, StringComparison.Ordinal);
        Assert.Contains("<span class='ok'>running</span>", html, StringComparison.Ordinal);
        Assert.Contains("workers", html, StringComparison.Ordinal);
        Assert.Contains("42%", html, StringComparison.Ordinal);
        Assert.Contains("<select id='themesel'", html, StringComparison.Ordinal);
        Assert.Contains(ViewsFixture.Built.ToString("HH:mm:ss"), html, StringComparison.Ordinal);

        // The Control window and its buttons.
        Assert.Contains($"ProjectToolkit Scheduler v{ViewsFixture.ShownVersion} -", html, StringComparison.Ordinal);
        Assert.Contains("main 1a2b3c4 green at 12:20", html, StringComparison.Ordinal);
        Assert.Contains("model server", html, StringComparison.Ordinal);
        Assert.Contains("lane A and lane B both own src/Shared/", html, StringComparison.Ordinal);
        Assert.Contains("4 cards (5 runs): done 3, 1 in review", html, StringComparison.Ordinal);
        // The developer (2026-11-18, card 510-B): no planner push line; the button shows the state.
        Assert.DoesNotContain("planner push: on, 2 waiting", html, StringComparison.Ordinal);
        // 992-B: the Lanes button (labelled Categories) is gone; the Categories panel replaced it.
        foreach (var label in new[] { "Check main", "Planner push: on", "Pause", "Drain and exit", "Stop all and exit" })
        {
            Assert.Contains(">" + label + "</button>", html, StringComparison.Ordinal);
        }

        // Waiting on you: each kind, and the running worker's review entry hidden.
        Assert.Contains("Waiting on you", html, StringComparison.Ordinal);
        Assert.Contains("stuck", html, StringComparison.Ordinal);
        Assert.Contains("question", html, StringComparison.Ordinal);
        Assert.Contains("waiting", html, StringComparison.Ordinal);
        Assert.Contains("the git retry needs a look", html, StringComparison.Ordinal);
        Assert.DoesNotContain("check the banner reads well", html, StringComparison.Ordinal);

        // Requests to the planner: the open request, and the card whose deps are all done.
        Assert.Contains("Requests to the planner", html, StringComparison.Ordinal);
        Assert.Contains("/request?name=12.md", html, StringComparison.Ordinal);
        Assert.Contains("Ask for a scope plan", html, StringComparison.Ordinal);
        Assert.Contains(">060-E<", html, StringComparison.Ordinal);
        Assert.Contains("all done: the planner can act now", html, StringComparison.Ordinal);

        // One window per worker, and the queue.
        // Worker windows (WT-P1): one per running worker, named by its type and slot.
        Assert.Contains("worker - slot 1", html, StringComparison.Ordinal);
        Assert.Contains("Sonnet - slot 2", html, StringComparison.Ordinal);
        Assert.Contains("thinking about the config", html, StringComparison.Ordinal);
        Assert.Contains("asked to stop at a safe point", html, StringComparison.Ordinal);
        Assert.Contains(">Queue<", html, StringComparison.Ordinal);
        Assert.Contains("100-A-lanes-and-workers-json.md", html, StringComparison.Ordinal);
        Assert.Contains("next up", html, StringComparison.Ordinal);

        // Recent events: the last 10 of the 12.
        Assert.Contains(">Recent events<", html, StringComparison.Ordinal);
        Assert.Contains("finished 10-A-card-10 done", html, StringComparison.Ordinal);
        Assert.DoesNotContain("finished 11-A-card-11 done", html, StringComparison.Ordinal);
        Assert.DoesNotContain("finished 12-A-card-12 done", html, StringComparison.Ordinal);

        // The Worker types panel, which replaced the settings dialogs (WT-P3).
        Assert.Contains("<form method='post' action='/worker-types' class='wtypes wthidden'>", html, StringComparison.Ordinal);
    }
}
