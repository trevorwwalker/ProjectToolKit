using System.Text.RegularExpressions;
using Scheduler.Core.WebContract;
using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 490-C: the developer's page follow-ups after 480-C. The worker log is a scroll box again
/// (PAGE-5) in every theme (PAGE-11); the worker's title bar carries no category text and names the
/// card right after the worker's name; and the window no longer repeats the model the title bar
/// already shows (PAGE-10's stats line ends at the tool count). Card 971-C took the model server's
/// address out of the window altogether - it is set and shown in the Worker types panel.
/// </summary>
public sealed class PageWorkerFollowupsTests
{
    private static string Render(PageSnapshot snapshot, PageThemes? themes = null) =>
        new PageRenderer(themes).Render(snapshot, remote: false, controlsAllowed: true);

    // The part of the page that is worker n's window (as PageParityTests reads it).
    private static string WindowOf(string html, int slot)
    {
        var start = html.IndexOf($"<a class='barlink' href='/log?slot={slot}'>", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no window for worker {slot}");
        start = html.LastIndexOf("<section class='win'", start, StringComparison.Ordinal);
        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        return html[start..end];
    }

    // The title bar of worker n's window: from its <div class='bar'> to the body below it.
    private static string BarOf(string window)
    {
        var start = window.IndexOf("<div class='bar'>", StringComparison.Ordinal);
        Assert.True(start >= 0, "no title bar");
        var end = window.IndexOf("<div class='inner'>", start, StringComparison.Ordinal);
        return window[start..end];
    }

    // The declarations of every pre.log rule in this CSS, in order.
    private static List<(string Property, string Value)> LogRules(string css)
    {
        var rules = new List<(string, string)>();
        foreach (Match rule in Regex.Matches(css, @"pre\.log\s*\{([^}]*)\}"))
        {
            foreach (var decl in rule.Groups[1].Value.Split(';'))
            {
                var bits = decl.Split(':', 2);
                if (bits.Length == 2 && bits[1].Trim().Length > 0)
                {
                    rules.Add((bits[0].Trim().ToLowerInvariant(), bits[1].Trim()));
                }
            }
        }

        return rules;
    }

    // The value a property ends with when these CSS blocks all apply, in order.
    private static string Effective(string css, string property) =>
        LogRules(css).LastOrDefault(r => r.Property == property).Value ?? "";

    // The log scrolls inside a fixed-height box rather than growing the window (card 490-C item 1,
    // as the old page's theme had it): max-height and overflow auto, in the theme that applies.
    // The rule is the page's own (CommonCss), so it holds in every theme, the built-in fallback
    // and every file in assets/themes included.
    [Fact]
    public void Page_WorkerWindow_LogScrollsInsideBox_InEveryTheme()
    {
        foreach (var themes in new[] { PageThemes.BuiltIn, RepoThemes() })
        {
            foreach (var entry in themes.Entries)
            {
                var css = entry.Css + "\n" + PageThemes.CommonCss;
                var overflow = Effective(css, "overflow");
                var maxHeight = Effective(css, "max-height");
                Assert.True(
                    overflow.Contains("auto", StringComparison.Ordinal),
                    $"theme {entry.Key}: pre.log overflow is '{overflow}', expected auto");
                Assert.True(
                    maxHeight.Length > 0,
                    $"theme {entry.Key}: pre.log has no max-height, so the log grows the window");
            }
        }

        // The page's own rule comes after every theme's block, so it wins over a theme's own pre.log.
        var html = Render(ViewsFixture.Full(), RepoThemes());
        var themesAt = html.LastIndexOf("<style data-theme=", StringComparison.Ordinal);
        var scrollAt = html.IndexOf(PageThemes.CommonCss, StringComparison.Ordinal);
        Assert.True(themesAt >= 0 && scrollAt > themesAt, "the common style must come after the theme blocks");
    }

    // The themes the exe really loads: assets/themes beside the repository (the build puts
    // theme.default.json beside the exe and the rest in themes/, so copy them that way).
    internal static PageThemes RepoThemes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "assets", "themes")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "no assets/themes above the test binaries");
        var assets = Path.Combine(dir!.FullName, "assets", "themes");

        var folder = new TempFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "themes"));
        foreach (var file in Directory.GetFiles(assets, "*.json"))
        {
            var name = Path.GetFileName(file);
            var target = name.Equals("theme.default.json", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(folder.Path, name)
                : Path.Combine(folder.Path, "themes", name);
            File.Copy(file, target, overwrite: true);
        }

        var themes = PageThemes.Load(folder.Path);
        Assert.True(themes.Entries.Count >= 2, "the repository's theme files did not load");
        return themes;
    }

    // The title bar: no category text, and the card link right after the worker's name (card 490-C
    // item 2). An idle worker shows nothing in that place.
    [Fact]
    public void Page_WorkerWindow_TitleCardLinkAfterName_NoCategoryText()
    {
        var html = Render(ViewsFixture.Full());
        var bar = BarOf(WindowOf(html, 1));

        Assert.False(Regex.IsMatch(bar, @"lane [A-Za-z]"), $"the title bar still names the lane: {bar}");
        Assert.Contains(
            "<a class='barlink' href='/log?slot=1'>worker - slot 1 (claude-opus-4)</a>"
            + "<a class='cardlink' href='/card?name=070-A-config-model.md'>070-A-config-model.md</a>",
            bar,
            StringComparison.Ordinal);

        // The card is no longer where it was (the right-hand barcard span).
        Assert.DoesNotContain("barcard", bar, StringComparison.Ordinal);

        // WT-P1: an idle folder has no window at all, so there is no card's place left to fill.
        var full = ViewsFixture.Full();
        var idle = full with { Slots = [full.Slots[0], full.Slots[1] with { Card = "" }] };
        Assert.False(
            Render(idle).Contains("<a class='barlink' href='/log?slot=2'>", StringComparison.Ordinal),
            "the idle folder drew a window");
    }

    // Inside the window the model is not repeated (card 490-C item 3) and the model server's address
    // is not shown at all (card 971-C): the stats line ends at the tool count, the title bar names
    // the model, and the address is only in the Worker types panel, where it is set.
    [Fact]
    public void Page_WorkerWindow_StatsEndAtTools_NoModelServerAddress()
    {
        var full = ViewsFixture.Full();
        var slot = full.Slots[0] with
        {
            Stats = "context ~1k  |  12 model calls  |  59 tools  |  claude-opus-4  |  http://127.0.0.1:8317/v1",
        };
        var win = WindowOf(Render(full with { Slots = [slot, full.Slots[1]] }), 1);

        Assert.Contains(
            "<div class='stats' data-slot='1' data-model='claude-opus-4'>context ~1k  |  12 model calls  |  59 tools</div>",
            win,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<div class='mute'>claude-opus-4</div>", win, StringComparison.Ordinal);
        Assert.DoesNotContain("http://127.0.0.1:8317/v1", win, StringComparison.Ordinal);
        Assert.DoesNotContain("://", win, StringComparison.Ordinal);

        // Before the worker has said anything the whole line is the model and its server: the stats
        // line is then empty, and the title bar is what still names the model.
        var quiet = full with { Slots = [full.Slots[0] with { Stats = "claude-opus-4  |  http://127.0.0.1:8317/v1" }, full.Slots[1]] };
        var quietWin = WindowOf(Render(quiet), 1);
        Assert.Contains("<div class='stats' data-slot='1' data-model='claude-opus-4'></div>", quietWin, StringComparison.Ordinal);
        Assert.DoesNotContain("http://127.0.0.1:8317/v1", quietWin, StringComparison.Ordinal);
        Assert.Contains("worker - slot 1 (claude-opus-4)", quietWin, StringComparison.Ordinal);

        // The live view (PAGE-3) trims the same way as it updates the stats line in place, and has no
        // separate server line left to fill.
        Assert.Contains("statsBits", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("data-model", PageScript.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("'.server'", PageScript.Html, StringComparison.Ordinal);
    }

    // The log stays pinned to the bottom while new lines arrive, and where the user left it when
    // they scrolled up (470-C's live refresh keeps both, and so does a page refresh now the log
    // scrolls inside its box).
    [Fact]
    public void Page_WorkerWindow_LogPinningKeptOnRefresh()
    {
        // The live update: pinned when at the bottom, kept when scrolled up.
        Assert.Contains("atEnd", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("p.scrollTop = p.scrollHeight", PageScript.Html, StringComparison.Ordinal);

        // A full refresh remembers each log's scroll place (and whether it was pinned) and puts it
        // back, instead of yanking every log to the bottom.
        Assert.Contains("pinLogScroll", PageScript.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("pinLogs();", PageScript.Html, StringComparison.Ordinal);
    }
}
