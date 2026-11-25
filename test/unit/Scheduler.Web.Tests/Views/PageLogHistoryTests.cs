// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 610-C item 2: each worker's window on the page keeps its own history - it appends what
/// <c>/live.json</c> sends and holds at most 500 lines, dropping the oldest. The lines are numbered
/// from the start of the run, so the page asks for what it has not seen yet
/// (<c>?since=4:1234,1:87</c>); a new run in the slot clears the window, and a page opened later
/// gets the last 500 lines from the feed. 490-C's scroll box stays as it was. The script is read,
/// as 470-C read it, and run against a fake DOM (PageScriptNodeTests).
/// </summary>
public sealed class PageLogHistoryTests
{
    // PAGE-3 (610-C) · the window keeps 500 lines and drops the oldest from the front.
    [Fact]
    public void Page_LogHistory_Keeps500AndDropsTheOldest()
    {
        Assert.Contains("KEEP = 500", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("h.lines.length > KEEP", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("h.lines.slice(-KEEP)", PageScript.Html, StringComparison.Ordinal);
    }

    // PAGE-3 (610-C) · what arrives is appended, not written over: the page sends the number it has
    // reached per slot and adds the lines after it.
    [Fact]
    public void Page_LogHistory_AppendsWhatArrivesAndAsksForTheRest()
    {
        Assert.Contains("h.lines = h.lines.concat(got)", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("'?since=' + ", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("'/live.json' + sinceParam()", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("h.n = v.next", PageScript.Html, StringComparison.Ordinal);
    }

    // PAGE-3 (610-C) · a new run in the slot - another card, or the same card started again - clears
    // the window; and when the feed could not go back as far as the page's cursor, the answer
    // replaces the window instead of leaving a hole in it.
    [Fact]
    public void Page_LogHistory_NewRunClearsAndAGapReplaces()
    {
        Assert.Contains("h.run !== run", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("h.n = 0; h.lines = []", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("v.from > h.n + 1", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("h.lines = got.slice()", PageScript.Html, StringComparison.Ordinal);
    }

    // PAGE-3 (610-C) · the line being written is shown after the kept lines but is not kept, so it
    // never takes a place in the 500; a worker that has written nothing says so.
    [Fact]
    public void Page_LogHistory_PartialLineIsShownButNotKept()
    {
        Assert.Contains("if (v.partial) text.push(v.partial)", PageScript.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("h.lines.push(v.partial)", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("(no output yet)", PageScript.Html, StringComparison.Ordinal);
    }

    // PAGE-3 (610-C) · when the run changed, the sliced answer is dropped and the next ask carries no
    // cursor; the number it carried must not be taken either, or the new run's first lines are missed
    // (the fake-DOM run of the script found this).
    [Fact]
    public void Page_LogHistory_AnIgnoredReplyDoesNotMoveTheCursor()
    {
        Assert.Contains("cleared = true;", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("if (!cleared && typeof v.next === 'number' && v.next > h.n) h.n = v.next;", PageScript.Html, StringComparison.Ordinal);
    }

    // PAGE-3, 490-C · the scroll box is untouched: the window still follows the bottom while the
    // reader has not scrolled up, and a whole-page refresh puts each log back where it was.
    [Fact]
    public void Page_LogHistory_KeepsTheScrollBoxBehaviour()
    {
        Assert.Contains("p.scrollTop + p.clientHeight >= p.scrollHeight - 4", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("p.scrollTop = p.scrollHeight", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("function pinLogScroll()", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("pinLogScroll();", PageScript.Html, StringComparison.Ordinal);
    }
}
