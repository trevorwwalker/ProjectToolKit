using Scheduler.Core.WebContract;
using Scheduler.Web.Server;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// PAGE-1, PAGE-3: the page keeps itself current without F5. It polls <c>/live.json</c>, updates
/// each running worker's log, stats and time in place, refreshes the rest of the page when the
/// snapshot's version changes, sends its buttons with fetch, and says so in the top bar when the
/// scheduler stops answering. The script itself is read, not run: these tests check that it is on
/// both pages and that every element it updates is there to be found.
/// </summary>
public sealed class PageLiveRefreshTests
{
    // PAGE-3 · the single page carries the polling script and the version it compares against.
    [Fact]
    public void Page_LiveRefresh_ScriptAndVersionOnThePage()
    {
        var snapshot = ViewsFixture.Full();
        var html = new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);

        AssertScript(html);
        Assert.Contains($"<meta name='page-version' content='{PageVersion.Of(snapshot)}'>", html, StringComparison.Ordinal);
    }

    // PAGE-3, PAGE-10 · each worker's window has the elements the script fills: its log, its
    // stats line, its minutes run and its time-limit bar, all keyed by slot number; and the Control
    // window's title bar has the place where "scheduler not responding" goes (520-A: the top bar is
    // gone, so the desk is the only part a whole-page refresh replaces).
    [Fact]
    public void Page_LiveRefresh_ElementsTheScriptUpdates()
    {
        var html = new PageRenderer().Render(ViewsFixture.Full(), remote: false, controlsAllowed: true);

        Assert.Contains("<pre class='log' data-slot='1'>", html, StringComparison.Ordinal);
        Assert.Contains("<div class='stats' data-slot='1' data-model='claude-opus-4'>1.2k tok/s</div>", html, StringComparison.Ordinal);
        Assert.Contains("<span class='tag' data-slot='1'>12 min</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class='prog' data-slot='1'></span>", html, StringComparison.Ordinal);
        Assert.Contains("id='live-state'", html, StringComparison.Ordinal);

        // The parts a refresh replaces.
        Assert.Contains("id='desk'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id='screen'", html, StringComparison.Ordinal);
    }

    // PAGE-3 · the version ignores what changes every tick (build time, host load, each worker's
    // log and stats, which the script updates in place) and changes with anything else.
    [Fact]
    public void Page_LiveRefresh_VersionChangesOnlyWithThePage()
    {
        var snapshot = ViewsFixture.Full();
        var version = PageVersion.Of(snapshot);

        var ticked = snapshot with
        {
            Built = snapshot.Built.AddSeconds(3),
            HostLoad = null,
            Slots = [.. snapshot.Slots.Select(slot => slot with { LogTail = ["something new"], Stats = "9.9k tok/s" })],
        };
        Assert.Equal(version, PageVersion.Of(ticked));

        var started = snapshot with
        {
            Slots = [.. snapshot.Slots.Select(slot => slot.Slot == 1 ? slot with { Card = "080-A-next.md" } : slot)],
        };
        Assert.NotEqual(version, PageVersion.Of(started));

        var queued = snapshot with { Queue = [.. snapshot.Queue.Skip(1)] };
        Assert.NotEqual(version, PageVersion.Of(queued));

        var paused = snapshot with { State = new PageStateView { State = Scheduler.Core.Contracts.SchedulerState.Paused, Text = "paused" } };
        Assert.NotEqual(version, PageVersion.Of(paused));
    }

    // PAGE-3 · the combined page behaves the same: the same script, and a version of its own.
    [Fact]
    public void Pool_CombinedPage_LiveRefreshScript()
    {
        var snapshots = new[] { ViewsFixture.Full(), ViewsFixture.Plain() };
        var html = new CombinedPageRenderer().Render(snapshots, "", remote: false, controlsAllowed: true);

        AssertScript(html);
        Assert.Contains($"<meta name='page-version' content='{PageVersion.Of(snapshots)}'>", html, StringComparison.Ordinal);
        Assert.Contains("id='live-state'", html, StringComparison.Ordinal);
    }

    private static void AssertScript(string html)
    {
        Assert.Contains(PageScript.Html, html, StringComparison.Ordinal);

        // What the script does, by reading it: polls every 2 s, compares the version, posts with
        // fetch and waits for pageDone, replaces the screen and desk, and says when it is down.
        Assert.Contains("'/live.json'", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("EVERY = 2000", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("meta[name=page-version]", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("'X-Requested-With': 'fetch'", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("j.pageDone", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("['screen', 'desk']", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("scheduler not responding", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("dialog[open]", PageScript.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("</script>", PageScript.Html[..^"</script>".Length], StringComparison.Ordinal);
    }
}
