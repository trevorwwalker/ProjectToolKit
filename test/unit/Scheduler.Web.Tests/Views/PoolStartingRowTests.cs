// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// 1015-B: the pool's page is served before its projects have joined. A project still joining has its row in
/// the Projects table at once: its chip and "starting..." in grey, its buttons there but disabled.
/// </summary>
public sealed class PoolStartingRowTests
{
    // 1015-B: one project joined, one still starting: the joined one has its own row, the other a starting row.
    [Fact]
    public void Pool_StartingProject_ChipStartingGreyButtonsDisabled()
    {
        var renderer = new CombinedPageRenderer(() => PageThemes.BuiltIn, "slate", () => ["Joined", "Later"]);
        var html = renderer.Render([new PageSnapshot { Project = "Joined", Built = DateTimeOffset.UnixEpoch, State = new PageStateView { State = SchedulerState.Running, Text = "running" } }], "slate", remote: false, controlsAllowed: true);

        var start = html.IndexOf("<tr data-project='Later' data-starting='1'>", StringComparison.Ordinal);
        Assert.True(start >= 0, "the starting project has a row");
        var row = html[start..html.IndexOf("</tr>", start, StringComparison.Ordinal)];
        Assert.Contains("<span class='mute'>starting...</span>", row, StringComparison.Ordinal);
        Assert.Contains("title='Later'", row, StringComparison.Ordinal); // its chip
        foreach (var label in new[] { "Check main", "Categories", "Planner push", "Pause", "Drain", "Stop" })
        {
            Assert.Contains($"disabled>{label}</button>", row, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("<form", row, StringComparison.Ordinal);
        Assert.DoesNotContain("data-project='Joined' data-starting", html, StringComparison.Ordinal);
    }

    // 1015-B: before any project has joined the page still renders, every project as starting.
    [Fact]
    public void Pool_NoProjectJoinedYet_EveryOneStarting()
    {
        var renderer = new CombinedPageRenderer(() => PageThemes.BuiltIn, "", () => ["A", "B"]);
        var html = renderer.Render([], "", remote: false, controlsAllowed: true);

        Assert.Contains("<tr data-project='A' data-starting='1'>", html, StringComparison.Ordinal);
        Assert.Contains("<tr data-project='B' data-starting='1'>", html, StringComparison.Ordinal);
    }
}
