// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Cards 500-C and 590-C: the page uses wide screens. The desk holds the main column on the left
/// (the page's own windows and the workers) and a side column on the right (Queue, then Recent
/// events); the common style lays them out as one column below 1400 px and two from 1400 px,
/// whatever the theme.
/// </summary>
public sealed class PageLayoutTests
{
    private const string Medium = "@media (min-width: 1400px)";
    private const string Wide = "@media (min-width: 2000px)";

    // The page: the columns in the desk, and the windows in today's order inside them, so the
    // narrow layout reads exactly as before.
    [Fact]
    public void Page_Desk_HasColumns_WindowsInOrder()
    {
        var html = new PageRenderer().Render(ViewsFixture.Full(), remote: false, controlsAllowed: true);

        AssertInOrder(html,
            "id='desk'", "id='col-main'",
            "id='w-control'", "id='w-waiting'", "id='w-requests'", "id='w-slots'",
            "id='col-side'", "id='col-queue'", "id='w-queue'", "id='col-events'", "id='w-events'");
        // The notice and the banner stay above the columns, across the whole width.
        var desk = html.IndexOf("id='desk'", StringComparison.Ordinal);
        var main = html.IndexOf("id='col-main'", StringComparison.Ordinal);
        var top = html.IndexOf("id='col-top'", StringComparison.Ordinal);
        Assert.True(desk < top && top < main, "col-top must come first in the desk");
    }

    // The combined page, the same way: Projects (1004-C: with the statistics and Worker types in
    // it), Waiting, then the running workers' own windows in the middle.
    [Fact]
    public void CombinedPage_Desk_HasColumns_WindowsInOrder()
    {
        var html = new CombinedPageRenderer().Render([ViewsFixture.Full()], "", remote: false, controlsAllowed: true);

        AssertInOrder(html,
            "id='desk'", "id='col-main'",
            "id='w-projects'", "id='w-waiting'", "<section class='win' data-project=",
            "id='col-side'", "id='col-queue'", "id='w-queue'", "id='col-events'", "id='w-events'");
        Assert.DoesNotContain("id='w-statistics'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id='w-workers'", html, StringComparison.Ordinal);
    }

    // The common style has the two layouts: nothing below 1400 px (one column, as before), and from
    // 1400 px two - the main column on the left at the width the single column used to have, and the
    // side column on the right taking whatever is left. No layout splits Queue from Recent events.
    [Fact]
    public void CommonCss_HasTwoLayouts()
    {
        var css = PageThemes.CommonCss;
        var medium = MediaBlock(css, Medium);
        Assert.Contains("display: grid", medium, StringComparison.Ordinal);
        Assert.Contains("\"main side\"", medium, StringComparison.Ordinal);
        // 958-C: the two columns share the width in fixed proportions, whatever the zoom; the side
        // never drops below the width at which Queue and Recent events are still readable.
        // 958-C (the developer's reply): the split is proportional - the fractions are today's
        // proportions, and no pixel cap can leave the left behind while the right grows.
        Assert.Contains("grid-template-columns: minmax(0, 60fr) minmax(440px, 40fr)", medium, StringComparison.Ordinal);
        Assert.Contains("#col-main { grid-area: main;", medium, StringComparison.Ordinal);
        Assert.Contains("#col-side { grid-area: side;", medium, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", medium, StringComparison.Ordinal);
        // 520-A Q2 (the developer, 23:07): the common style no longer reorders the main column's
        // windows - the renderer leads with the Control window's own order.
        Assert.DoesNotContain("#w-waiting", medium, StringComparison.Ordinal);

        // 590-C: there is no wider layout that pulls Queue and Recent events apart - the side
        // column stays one column at every width, Queue above Recent events.
        Assert.DoesNotContain(Wide, css, StringComparison.Ordinal);
        Assert.DoesNotContain("queue main events", css, StringComparison.Ordinal);
        Assert.DoesNotContain("display: contents", css, StringComparison.Ordinal);

        // Outside the media query the desk is not a grid: small screens keep one column.
        var outside = css.Replace(medium, "", StringComparison.Ordinal);
        Assert.DoesNotContain("grid-template-areas", outside, StringComparison.Ordinal);
    }

    // A theme that sets its own desk width and display does not break it: the common style comes
    // after every theme's block and, in the wide layout, sets the desk's width and display itself.
    [Fact]
    public void Theme_WithOwnDeskWidth_DoesNotBreakLayout()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "themes"));
        folder.Write("theme.default.json", """
            { "name": "Default", "label": "Modern", "order": 10,
              "css": [".desk { max-width: 900px; margin: 0 40px; display: block; }"] }
            """);
        var themes = PageThemes.Load(folder.Path);

        var html = new PageRenderer(themes).Render(ViewsFixture.Full(), remote: false, controlsAllowed: true);
        var themeAt = html.IndexOf(".desk { max-width: 900px;", StringComparison.Ordinal);
        var commonAt = html.IndexOf(PageThemes.CommonCss, StringComparison.Ordinal);
        Assert.True(themeAt >= 0 && commonAt > themeAt, "the common style must come after the theme's");

        var desk = Regex.Match(MediaBlock(PageThemes.CommonCss, Medium), @"\.desk\s*\{([^}]*)\}").Groups[1].Value;
        Assert.Contains("max-width: none", desk, StringComparison.Ordinal);
        Assert.Contains("display: grid", desk, StringComparison.Ordinal);
    }

    // The live refresh replaces the desk (470-C); each column that scrolls on its own keeps its place.
    [Fact]
    public void LiveRefresh_KeepsEachColumnsScroll()
    {
        var script = PageScript.Html;
        var save = script.IndexOf("document.querySelectorAll('#desk .col[id]')", StringComparison.Ordinal);
        var replace = script.IndexOf("o.replaceWith(", StringComparison.Ordinal);
        var restore = script.IndexOf("c.scrollTop = cols[id]", StringComparison.Ordinal);
        Assert.True(save >= 0 && save < replace && replace < restore, "column scroll must be saved before the desk is replaced and put back after");
    }

    // The shipped themes keep only their looks: none sets the desk's width any more.
    [Fact]
    public void ShippedThemes_DoNotSetDeskWidth()
    {
        foreach (var entry in PageWorkerFollowupsTests.RepoThemes().Entries)
        {
            foreach (Match rule in Regex.Matches(entry.Css, @"\.desk\s*\{([^}]*)\}"))
            {
                Assert.DoesNotContain("max-width", rule.Groups[1].Value, StringComparison.Ordinal);
            }
        }
    }

    private static void AssertInOrder(string html, params string[] needles)
    {
        var at = -1;
        foreach (var needle in needles)
        {
            var next = html.IndexOf(needle, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, $"'{needle}' missing or out of order");
            at = next;
        }
    }

    // The text of a media query's block, up to its closing brace.
    private static string MediaBlock(string css, string query)
    {
        var start = css.IndexOf(query, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no '{query}' in the common style");
        var open = css.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            depth += css[i] == '{' ? 1 : css[i] == '}' ? -1 : 0;
            if (depth == 0)
            {
                return css[start..(i + 1)];
            }
        }

        return css[start..];
    }
}
