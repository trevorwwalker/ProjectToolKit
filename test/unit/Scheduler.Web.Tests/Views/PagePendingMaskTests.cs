using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// 957-B item 2 and the client half of item 3: a click is applied to the page at once and kept
/// applied while its git work runs behind the reply. The page holds the change in a pending list,
/// re-applies it after every refresh (which rebuilds the desk from the scheduler's older view), and
/// replaces the marks from <c>/live.json</c>'s actions - dropping them when the work lands, and
/// putting the item back with the scheduler's reason in the notice line when it fails.
/// </summary>
public class PagePendingMaskTests
{
    // Item 2: the mask is kept in a list and put back over the page after each rebuild, so a refresh
    // from the scheduler's older view cannot bring the answered question or the Done card back.
    [Fact]
    public void PendingMask_IsKeptInAListAndReAppliedAfterEveryRefresh()
    {
        Assert.Contains("var pending = []", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("function applyPending()", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("pending.push(mask); applyPending();", PageScript.Html, StringComparison.Ordinal);

        // The re-apply happens after the refresh has replaced the screen and the desk.
        var atReplace = PageScript.Html.IndexOf("o.replaceWith(document.importNode(n, true))", StringComparison.Ordinal);
        var atApply = PageScript.Html.IndexOf("applyPending();", atReplace, StringComparison.Ordinal);
        Assert.True(atReplace >= 0 && atApply > atReplace, "the pending marks are not re-applied after the desk is rebuilt");
    }

    // Item 2: what each click does. An answered question row disappears and the card's row goes with
    // its last open question; Done leaves Waiting on you; Retry/Resume leave it and mark the Queue row
    // as resuming; a Stop greys the worker's own window.
    [Fact]
    public void PendingMask_AppliesEachClicksOwnResult()
    {
        // /answer: the question row goes by its card and number, ...
        Assert.Contains("#w-waiting tr.qrow", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("input[name=card]", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("String(q.value) === String(p.q)", PageScript.Html, StringComparison.Ordinal);

        // ... and the card's row follows its last open question.
        Assert.Contains("classList.contains('qrow') && r.style.display !== 'none'", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("rowsIn('#w-waiting', p.card, p.project).forEach(function (r) { r.style.display = 'none'; });", PageScript.Html, StringComparison.Ordinal);

        // /done and /retry: the card leaves Waiting on you, and a queued one shows as resuming.
        Assert.Contains("p.kind === 'done' || p.kind === 'queue'", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("rowsIn('#col-queue', p.card, p.project)", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("' resuming'", PageScript.Html, StringComparison.Ordinal);

        // /stop: the worker's window is greyed while the stop runs.
        Assert.Contains("closest('section.win')", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("classList.add('stopped')", PageScript.Html, StringComparison.Ordinal);
    }

    // Item 3: the live feed's actions decide when a mark is no longer needed. A landed action's mark
    // is dropped; a failed one is taken off the mask - so the next refresh brings the item back - and
    // its message is what the notice line says.
    [Fact]
    public void PendingMask_IsReplacedByTheLiveFeedAndAFailureSaysWhy()
    {
        Assert.Contains("st.state === 'done'", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("st.state === 'failed'", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("pending.splice(i, 1)", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("st.msg || 'that did not happen'", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("n.className = 'notice'", PageScript.Html, StringComparison.Ordinal);

        // The actions arrive with each live poll, and the click's id is what ties them together.
        Assert.Contains("settlePending(j.actions || [])", PageScript.Html, StringComparison.Ordinal);
        Assert.Contains("mask.id = j.id", PageScript.Html, StringComparison.Ordinal);
    }

    // 990-B: "landed" is not "shown". The scheduler's view trails a landed action until its next card
    // read, so a mark dropped on landed brought the answered or finished card back for a while. A
    // landed mark now stays until a page fetched after it landed no longer shows the item; a failed
    // action still brings the item back at once with the notice.
    [Fact]
    public void PendingMask_StaysUntilAPageFetchedAfterItLandedNoLongerShowsTheItem()
    {
        var html = PageScript.Html;

        // Landed only notes the time; the mark is not dropped there (a Stop's own window aside).
        var atDone = html.IndexOf("st.state === 'done'", StringComparison.Ordinal);
        var atFailed = html.IndexOf("st.state === 'failed'", atDone, StringComparison.Ordinal);
        Assert.True(atDone >= 0 && atFailed > atDone, "settlePending has no done and failed branches");
        var landed = html[atDone..atFailed];
        Assert.Contains("p.landed = Date.now()", landed, StringComparison.Ordinal);
        Assert.Contains("p.kind === 'stop'", landed, StringComparison.Ordinal);

        // The refresh notes when it asked, and drops the marks that page agrees with before it puts
        // the rest back.
        Assert.Contains("var fetchedAt = Date.now();", html, StringComparison.Ordinal);
        Assert.Contains("function agreePending(since)", html, StringComparison.Ordinal);
        Assert.Contains("p.landed && p.landed <= since && !showsPending(p)", html, StringComparison.Ordinal);
        var atAgree = html.IndexOf("agreePending(fetchedAt);", StringComparison.Ordinal);
        var atReplace = html.IndexOf("o.replaceWith(document.importNode(n, true))", StringComparison.Ordinal);
        var atApply = html.IndexOf("applyPending();", atAgree, StringComparison.Ordinal);
        Assert.True(atReplace >= 0 && atAgree > atReplace && atApply > atAgree, "the marks are not checked against the new page before they are put back");
        Assert.True(html.IndexOf("var fetchedAt = Date.now();", StringComparison.Ordinal) < html.IndexOf("get('/' + location.search, 15000)", StringComparison.Ordinal), "the refresh notes the time after it asked");

        // What "shows" means: the answered question's own row, or any of the card's rows in Waiting on you.
        Assert.Contains("function showsPending(p)", html, StringComparison.Ordinal);
        Assert.Contains("return waitingRows(p.card, p.project).length > 0;", html, StringComparison.Ordinal);

        // A failed action is still taken off the mask at once, with the scheduler's reason.
        var failed = html[atFailed..html.IndexOf("refresh(true);", atFailed, StringComparison.Ordinal)];
        Assert.Contains("pending.splice(i, 1)", failed, StringComparison.Ordinal);
        Assert.Contains("sayNotice(", failed, StringComparison.Ordinal);
    }

    // An action the queue never reports (a page left open through a restart) must not mask the page
    // for ever: it expires and the item comes back on its own.
    [Fact]
    public void PendingMask_DoesNotOutliveItsAction()
    {
        Assert.Contains("Date.now() - p.at > 120000", PageScript.Html, StringComparison.Ordinal);
    }
}
