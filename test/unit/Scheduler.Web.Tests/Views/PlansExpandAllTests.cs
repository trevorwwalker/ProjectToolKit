// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1212-C: Expand all and Collapse all in the title bars of the Plans window (1256-C: the Completed plans window is gone).</summary>
public sealed class PlansExpandAllTests
{
    private static string Page() =>
        new CombinedPageRenderer(theme: "slate").Render(new PoolPageSnapshot { Built = DateTimeOffset.UnixEpoch, MaxWorkers = 1, Running = 0, Projects = [], Pages = [] }, remote: false, controlsAllowed: true, "plans");

    [Fact]
    public void TitleBar_CarriesBothButtons_NamingTheirWindow()
    {
        const string window = "w-plans";
        var html = Page();
        var start = html.IndexOf("id='" + window + "'", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var bar = html[start..html.IndexOf("<div class='inner'>", start, StringComparison.Ordinal)];

        Assert.Contains("data-plans-all='expand' data-window='" + window + "'", bar, StringComparison.Ordinal);
        Assert.Contains("data-plans-all='collapse' data-window='" + window + "'", bar, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_OpensOrClosesEveryNodeOfTheWindow_AndRemembersEachKey()
    {
        var script = PageScript.Html;

        Assert.Contains("button[data-plans-all]", script, StringComparison.Ordinal);
        Assert.Contains("w.querySelectorAll('details[data-key]').forEach", script, StringComparison.Ordinal);
        Assert.Contains("d.open = open;", script, StringComparison.Ordinal);
        Assert.Contains("planSet(d.getAttribute('data-key'), open ? '1' : '0');", script, StringComparison.Ordinal);
    }
}
