// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>PAGE-8: the state text and its colour class in the top bar and the Control window.</summary>
public sealed class PageStateTextTests
{
    // PAGE-8
    [Fact]
    public void Page_StateText()
    {
        var renderer = new PageRenderer();

        var running = renderer.Render(ViewsFixture.Plain(SchedulerState.Running), remote: false, controlsAllowed: true);
        Assert.Contains("<span class='ok'>running</span>", running, StringComparison.Ordinal);

        var paused = renderer.Render(ViewsFixture.Plain(SchedulerState.Paused), remote: false, controlsAllowed: true);
        Assert.Contains("<span class='warn'>paused</span>", paused, StringComparison.Ordinal);

        var red = renderer.Render(ViewsFixture.Plain(SchedulerState.PausedRed), remote: false, controlsAllowed: true);
        Assert.Contains("<span class='bad'>paused: main is red</span>", red, StringComparison.Ordinal);

        var quota = renderer.Render(ViewsFixture.Plain(SchedulerState.PausedQuota), remote: false, controlsAllowed: true);
        Assert.Contains("<span class='bad'>paused: quota used up</span>", quota, StringComparison.Ordinal);

        // test-plans/page.md asks for warn while draining; schedule.ps1's default case colours it bad. See Q1.
        var draining = renderer.Render(ViewsFixture.Plain(SchedulerState.Draining), remote: false, controlsAllowed: true);
        Assert.Contains("<span class='warn'>draining</span>", draining, StringComparison.Ordinal);
    }
}
