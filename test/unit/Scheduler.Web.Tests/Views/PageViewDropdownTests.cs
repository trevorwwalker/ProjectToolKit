// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// 1129-C (VW-2): the pool page's title bar carries one views dropdown instead of the Workers and Plans
/// buttons. The views are named in the renderer's one list, which the markup and the browser's memory
/// of the last view are both made from, so a further view (the Gantt) is one more entry there.
/// </summary>
public sealed class PageViewDropdownTests
{
    private static PoolPageSnapshot Pool() => new()
    {
        Built = ViewsFixture.Built,
        MaxWorkers = 2,
        Running = 0,
        Projects =
        [
            new PoolProjectView { Name = "ProjectToolkit", Weight = 1, WeightShare = 1, UsedShare = 1 },
        ],
        Pages = [ViewsFixture.Plain()],
        Views = [ProjectViews.Empty("ProjectToolkit", "")],
    };

    private static string Render(string? view) =>
        new CombinedPageRenderer(theme: "slate").Render(Pool(), remote: false, controlsAllowed: true, view);

    // The options of the #viewsel select, in order: (value, selected).
    private static List<(string Value, bool Selected)> Options(string html)
    {
        var start = html.IndexOf("id='viewsel'", StringComparison.Ordinal);
        Assert.True(start >= 0, "the title bar has a views dropdown");
        var end = html.IndexOf("</select>", start, StringComparison.Ordinal);
        var body = html[(start..end)];
        return [.. System.Text.RegularExpressions.Regex.Matches(body, @"<option value='([^']*)'([^>]*)>")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value.Contains("selected", StringComparison.Ordinal)))];
    }

    // VW-2: the dropdown lists every view, and the view being shown is the selected one.
    // 1301-C: the order is Plans, Workers, Gantt - Plans first, the developer (2026-11-24 11:52).
    [Theory]
    [InlineData(null, "plans")]
    [InlineData("", "plans")]
    [InlineData("board", "plans")]
    [InlineData("plans", "plans")]
    [InlineData("  PLANS  ", "plans")]
    [InlineData("gantt", "gantt")]
    [InlineData("workers", "workers")]
    public void VW_2_Dropdown_ListsEveryViewWithTheCurrentSelected(string? view, string current)
    {
        var options = Options(Render(view));

        Assert.Equal(new[] { "plans", "workers", "gantt" }, options.Select(o => o.Value).ToArray());
        Assert.Single(options, o => o.Selected);
        Assert.Equal(current, options.Single(o => o.Selected).Value);
    }

    // 1129-C: the buttons are gone; nothing in the page links to a view address any more.
    [Fact]
    public void VW_2_Dropdown_NoViewButtonsRemain()
    {
        var html = Render("plans");

        Assert.DoesNotContain("<span class='vsw'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<a href='/?view=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-current='page'", html, StringComparison.Ordinal);
    }

    // VW-2: choosing a view goes to /?view=<name>, as the buttons did.
    [Fact]
    public void VW_2_Dropdown_ChoiceNavigates()
    {
        var script = PageScript.Html;

        Assert.Contains("s.id !== 'viewsel'", script, StringComparison.Ordinal);
        Assert.Contains("location.href = '/?view='", script, StringComparison.Ordinal);
        // An open dropdown is not rebuilt under the reader by the live refresh, as the project and theme menus are not.
        Assert.Contains("#viewsel:focus", script, StringComparison.Ordinal);
    }

    // VW-2, 1129-C: the dropdown and what the browser remembers of a bare "/" are made from one list of views.
    [Fact]
    public void VW_2_Dropdown_AndRememberedViewShareOneListOfViews()
    {
        var html = Render(null);
        var options = Options(html).Select(o => o.Value).ToArray();
        var remembered = Regex.Match(html, @"var viewNames = \[([^\]]*)\];");

        Assert.True(remembered.Success, "the page carries the list of view names the browser remembers by");
        Assert.Equal(
            options,
            remembered.Groups[1].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim('\'')).ToArray());

        // The bare "/" keeps the last view it was shown: remembered by the page, then asked for by name.
        Assert.Contains("localStorage.setItem('ptk-view', knownView(", html, StringComparison.Ordinal);
        Assert.Contains("location.replace('/?view=' + p)", html, StringComparison.Ordinal);
        Assert.Contains("localStorage.getItem('ptk-view')", html, StringComparison.Ordinal);
    }

    // 1269-C: the default is Plans, named by the script rather than read off the list (which 1301-C now opens on Plans).
    [Fact]
    public void VW_2_DefaultView_IsPlans_AndTheScriptKnowsItByName()
    {
        var html = Render(null);

        Assert.Contains("var defaultView = 'plans';", html, StringComparison.Ordinal);
        Assert.Contains("return defaultView; }", html, StringComparison.Ordinal);
        Assert.Contains("if (p !== defaultView) location.replace", html, StringComparison.Ordinal);
        Assert.DoesNotContain("viewNames[0]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("viewNames[0]", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("defaultView", PageScript.Html, StringComparison.Ordinal);
    }
}
