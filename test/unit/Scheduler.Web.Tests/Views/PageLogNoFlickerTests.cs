using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 965-C (PAGE-3): a worker's log window is only ever appended to, and a refresh never rebuilds
/// it. The box the server draws holds only what the snapshot's <c>LogTail</c> holds - and the host
/// fills none for a running worker - so replacing a section emptied every window and left the next
/// <c>/live.json</c> poll to refill it: the window collapsed to nothing and grew back on every
/// refresh. Now the box a worker already has is moved into the new markup, which keeps the element,
/// its lines, its height and its scroll. The script is read, as 470-C and 610-C read it.
/// </summary>
public sealed class PageLogNoFlickerTests
{
    // PAGE-3 (965-C item 1) · a refresh that swaps the screen and the desk keeps each existing
    // worker's log element: the one the page already has is moved into the new markup, and the box
    // the server drew is the one that goes. Nothing about the live box is copied or rebuilt.
    [Fact]
    public void Page_LogNoFlicker_RefreshMovesTheExistingBoxIntoTheNewMarkup()
    {
        var script = PageScript.Html;
        Assert.Contains("function liveBoxes() {", script, StringComparison.Ordinal);
        Assert.Contains("function keepBoxes(live) {", script, StringComparison.Ordinal);
        Assert.Contains("live[p.getAttribute('data-slot')] = p;", script, StringComparison.Ordinal);
        Assert.Contains("q.parentNode.replaceChild(old, q);", script, StringComparison.Ordinal);

        // The boxes are collected before the sections are replaced and put back after it.
        var had = script.IndexOf("var logs = liveBoxes();", StringComparison.Ordinal);
        var replace = script.IndexOf("o.replaceWith(", had, StringComparison.Ordinal);
        var kept = script.IndexOf("keepBoxes(logs);", replace, StringComparison.Ordinal);
        Assert.True(had >= 0 && replace > had && kept > replace, "the swap must go between taking the boxes and putting them back");

        // `boxes()` is the helper that finds the form fields a refresh carries across: a `var boxes`
        // in refresh would hide it from the whole of refresh (var is hoisted) and stop it refreshing.
        Assert.DoesNotContain("var boxes =", script, StringComparison.Ordinal);

        // Moved, not duplicated: the live element is put back as it is, never cloned or re-imported.
        Assert.DoesNotContain("cloneNode(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("importNode(old", script, StringComparison.Ordinal);
    }

    // PAGE-3 (965-C item 2) · a window that is new gets the box the server drew - empty, for a worker
    // whose tail the host does not fill - and it is painted from the lines this page already holds
    // for that run, so a new window is not left blank until the next poll.
    [Fact]
    public void Page_LogNoFlicker_ANewBoxIsPaintedFromTheLinesAlreadyHeld()
    {
        Assert.Contains("if (q.textContent) return;", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("var h = hist[q.getAttribute('data-slot')];", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("if (h && h.lines.length) q.textContent = h.lines.join('\\n');", PageScript.Html, StringComparison.Ordinal);
    }

    // PAGE-3 (965-C items 1 and 2) · nothing else rebuilds a box: the live feed is the only writer,
    // it writes only when its lines differ from what the box holds, and no path empties one.
    [Fact]
    public void Page_LogNoFlicker_TheLiveFeedIsTheOnlyWriterAndNeverClears()
    {
        Assert.Contains("if (p && p.textContent !== text) {", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("h.lines = h.lines.concat(got)", PageScript.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("textContent = ''", PageScript.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("textContent = \"\"", PageScript.Html, StringComparison.Ordinal);
    }

    // PAGE-3 (965-C item 2) · a slot taken by another card while the page was being fetched is
    // another run: its window is not filled with the lines of the worker that was there before, and
    // the next ask carries no cursor so the feed sends the new run from its first line (610-C).
    [Fact]
    public void Page_LogNoFlicker_ANewCardInASlotStartsItsWindowAfresh()
    {
        var script = PageScript.Html;
        var compare = script.IndexOf("if (windowOf(old) !== windowOf(q)) {", StringComparison.Ordinal);
        var cleared = script.IndexOf("clearSlot(s, windowOf(q));", compare, StringComparison.Ordinal);
        var moved = script.IndexOf("q.parentNode.replaceChild(old, q);", cleared, StringComparison.Ordinal);
        Assert.True(compare >= 0 && cleared > compare && moved > cleared,
            "a different card in the slot drops the old window; the same card keeps its box");

        // 1093-C: the window is dropped through the one clear both ways take, which lets go of the
        // lines, the cursor and the numbers of the card before.
        var start = script.IndexOf("function clearSlot(s, card) {", StringComparison.Ordinal);
        var body = script.Substring(start, script.IndexOf("          }", start, StringComparison.Ordinal) - start);
        Assert.Contains("h.run = ''; h.n = 0; h.lines = [];", body, StringComparison.Ordinal);
    }

    // PAGE-3 (965-C item 3) · the box keeps its height while it updates: the style every theme gets
    // gives a box that holds nothing a few lines of height, so the windows under a worker do not
    // jump when it has written nothing or has just started a new run.
    [Fact]
    public void Page_LogNoFlicker_AnEmptyBoxStillHoldsItsHeight()
    {
        Assert.Contains("pre.log { min-height:", PageThemes.CommonCss, StringComparison.Ordinal);
    }

    // PAGE-3 (965-C item 4) · where the reader had each log is noted at the moment of the swap, and
    // each box is pinned back after it, so a line that arrived during the fetch does not yank them.
    [Fact]
    public void Page_LogNoFlicker_ScrollIsNotedAtTheSwapAndPinnedAfterIt()
    {
        var script = PageScript.Html;
        var noted = script.IndexOf("noteLogs();", StringComparison.Ordinal);
        var replace = script.IndexOf("o.replaceWith(", noted, StringComparison.Ordinal);
        var pinned = script.IndexOf("pinLogScroll();", replace, StringComparison.Ordinal);
        Assert.True(noted >= 0 && replace > noted, "where each log was is noted before the sections are replaced");
        Assert.True(pinned > replace, "each box is pinned back where it was after the swap");
    }

    // PAGE-3 (965-C) · what the server puts in a worker's box is only the snapshot's LogTail, which
    // the host leaves empty for a running worker: this is the markup a refresh must not put back
    // over a live box.
    [Fact]
    public void Page_LogNoFlicker_TheServerDrawsAnEmptyBoxForAWorkerWithNoTail()
    {
        var snapshot = ViewsFixture.Full() with
        {
            Slots = [.. ViewsFixture.Full().Slots.Select(slot => slot with { LogTail = [] })],
        };
        var html = new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);

        Assert.Contains("<pre class='log' data-slot='1'></pre>", html, StringComparison.Ordinal);
    }
}
