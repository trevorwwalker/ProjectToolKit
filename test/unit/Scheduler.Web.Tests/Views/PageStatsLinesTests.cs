using System.Text.RegularExpressions;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// 520-A (the developer, 2026-11-18): the run stats show one line each and no percentage, and the
/// Control window's title bar is the scheduler's own line and carries the theme selector, the last
/// update and the not-responding state. The page has no top bar any more.
/// </summary>
public sealed class PageStatsLinesTests
{
    private static string Render(PageSnapshot snapshot) =>
        new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);

    // The part of the page that is the window holding the marker.
    private static string WindowOf(string html, string marker)
    {
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"no {marker}");
        var start = html.LastIndexOf("<section class='win'", at, StringComparison.Ordinal);
        var end = html.IndexOf("</section>", at, StringComparison.Ordinal);
        return html[start..end];
    }

    // ACCT-6 as 520-A shows it: the overall line and each model's line on a line of their own, each
    // model line keeping the two-space indent Summary writes.
    [Fact]
    public void Stats_Lines_RenderOneEach_KeepingTheIndent()
    {
        var html = Render(ViewsFixture.Full());
        var win = WindowOf(html, $"ProjectToolkit Scheduler v{ViewsFixture.ShownVersion}");

        var lines = Regex.Matches(win, "<div class='stats'([^>]*)>(.*?)</div>")
            .Select(m => (Attrs: m.Groups[1].Value, Text: m.Groups[2].Value))
            .ToList();
        Assert.Equal(3, lines.Count);
        Assert.StartsWith("Last 24 h (1 h 30 min): 4 cards (5 runs): done 3,", lines[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("  opus (1 h 0 min): 2 cards (3 runs): done 2,", lines[1].Text, StringComparison.Ordinal);
        Assert.StartsWith("  gpt (30 min): 2 cards (2 runs): done 1,", lines[2].Text, StringComparison.Ordinal);
        foreach (var line in lines)
        {
            // No percentage anywhere in a line (520-A), and pre-wrap is what keeps the indent.
            Assert.DoesNotContain("%", line.Text, StringComparison.Ordinal);
            Assert.Contains("white-space: pre-wrap", line.Attrs, StringComparison.Ordinal);
        }
    }

    // The Control window's title (the developer, 2026-11-18): project, version, state, worker count
    // and the host load, the state word in its colour.
    [Fact]
    public void Control_Title_IsTheSchedulersOwnLine()
    {
        var html = Render(ViewsFixture.Full());

        Assert.Contains(
            $"<b>ProjectToolkit Scheduler v{ViewsFixture.ShownVersion} - <span class='ok'>running</span>",
            html,
            StringComparison.Ordinal);
        Assert.Contains("2 of 3 workers", html, StringComparison.Ordinal);
        Assert.Contains("CPU 42%, 8G free of 32G, 8 threads", html, StringComparison.Ordinal);
    }

    // The Control window's title bar also carries what the top bar held on its right.
    [Fact]
    public void Control_TitleBar_CarriesTheSelectorAndLastUpdate()
    {
        var html = Render(ViewsFixture.Full());
        var win = WindowOf(html, $"ProjectToolkit Scheduler v{ViewsFixture.ShownVersion}");

        Assert.Contains("<select id='themesel'", win, StringComparison.Ordinal);
        Assert.Contains("last update " + ViewsFixture.Built.ToString("HH:mm:ss"), win, StringComparison.Ordinal);
        Assert.Contains("<span id='live-state'></span>", win, StringComparison.Ordinal);
    }

    // No top bar: nothing outside the desk, and the selector appears once. The ids the live-refresh
    // script needs (desk, themesel, live-state) all still exist.
    [Fact]
    public void Page_HasNoTopBar_ButKeepsTheScriptsIds()
    {
        var html = Render(ViewsFixture.Full());

        Assert.DoesNotContain("class='screen'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id='screen'", html, StringComparison.Ordinal);
        Assert.Contains("<div class='desk' id='desk'>", html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, "<select id='themesel'"));
        Assert.Equal(1, Count(html, "id='live-state'"));
    }

    // A paused scheduler keeps the tone of the state word in the title.
    [Fact]
    public void Control_Title_KeepsTheStateTone()
    {
        var html = Render(ViewsFixture.Plain(SchedulerState.Paused));

        Assert.Contains($"Scheduler v{ViewsFixture.ShownVersion} - <span class='warn'>paused</span>", html, StringComparison.Ordinal);
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
