using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 1093-C (PAGE-3): a worker window that changes card clears at once. The live feed numbers one
/// run's lines and the page appends them to its slot's window, so a window could keep the text of the
/// card that ran before the one in its title for a few seconds - and, between two cards, the slot has
/// no entry to answer at all and was simply left as it was. The script now takes a window back to the
/// starting line whenever the card it shows is not the card the lines came from: on the feed's
/// <c>starting</c>, on a desk swap to another card, and on a worker window with nothing in the answer.
/// The script is read, as 610-C, 965-C and 1092-C read it.
/// </summary>
public sealed class PageStaleLogTests
{
    // PAGE-3 (1093-C item 1) · what a window is cleared to: the starting line, and no memory of the
    // card before - not its lines and cursor, and not its stats, average or minutes.
    [Fact]
    public void Page_StaleLog_AWindowClearedForANewCardKeepsNothingOfTheLast()
    {
        var script = PageScript.Html;
        Assert.Contains("function startingText(card) { return '(starting ' + card + '...)'; }", script, StringComparison.Ordinal);

        var clear = script.IndexOf("function clearSlot(s, card) {", StringComparison.Ordinal);
        Assert.True(clear >= 0, "one function is what clears a window between two cards");
        var body = script.Substring(clear, script.IndexOf("          }", clear, StringComparison.Ordinal) - clear);
        Assert.Contains("if (h) { h.run = ''; h.n = 0; h.lines = []; }", body, StringComparison.Ordinal);
        Assert.Contains("if (k) { k.run = ''; k.stats = ''; k.tps = ''; k.time = ''; }", body, StringComparison.Ordinal);
        Assert.Contains("setText(bySlot('pre.log', s), startingText(card));", body, StringComparison.Ordinal);
        Assert.Contains("setText(bySlot('.stats', s), '');", body, StringComparison.Ordinal);
        Assert.Contains("setText(bySlot('.tag', s), '');", body, StringComparison.Ordinal);
        Assert.Contains("'- T/s'", body, StringComparison.Ordinal);
    }

    // PAGE-3 (1093-C item 2) · an entry the feed says belongs to another card is never drawn: the
    // window is cleared before its lines, stats or time are read.
    [Fact]
    public void Page_StaleLog_AnEntryForAnotherCardIsNotDrawn()
    {
        var script = PageScript.Html;
        var apply = script.IndexOf("function applySlot(s, v) {", StringComparison.Ordinal);
        Assert.True(apply >= 0, "the live answer is drawn by applySlot");
        var guard = script.IndexOf("if (v.starting) {", apply, StringComparison.Ordinal);
        var clears = script.IndexOf("clearSlot(s, v.starting);", guard, StringComparison.Ordinal);
        var returns = script.IndexOf("return;", clears, StringComparison.Ordinal);
        var draws = script.IndexOf("var p = bySlot('pre.log', s), text = logText(s, v);", returns, StringComparison.Ordinal);
        Assert.True(guard > apply && clears > guard && returns > clears && draws > returns,
            "the starting answer is dealt with before any of the entry's own lines are read");
    }

    // PAGE-3 (1093-C item 3) · a desk swap to another card takes the numbers with it: the old code
    // dropped the lines only, so the card before kept its stats, average and minutes under the new
    // title until the next poll, and the box waited empty.
    [Fact]
    public void Page_StaleLog_TheSwapToAnotherCardClearsStatsToo()
    {
        var script = PageScript.Html;
        var keep = script.IndexOf("function keepBoxes(live) {", StringComparison.Ordinal);
        Assert.True(keep >= 0, "the desk swap is what keeps a window's boxes");
        var changed = script.IndexOf("if (windowOf(old) !== windowOf(q)) {", keep, StringComparison.Ordinal);
        var clears = script.IndexOf("clearSlot(s, windowOf(q));", changed, StringComparison.Ordinal);
        var branchEnd = script.IndexOf("}", clears, StringComparison.Ordinal);
        Assert.True(changed > keep && clears > changed && branchEnd > clears,
            "the window of a slot that took another card is cleared, with the card's own name in it");

        // The old box is not carried across for another card: it goes, and the starting line shows.
        Assert.DoesNotContain("var gone = hist[s];", script, StringComparison.Ordinal);
    }

    // PAGE-3 (1093-C item 4) · a worker window with nothing in this answer is a slot between two cards:
    // it is cleared at once instead of waiting for a worker that has not started.
    [Fact]
    public void Page_StaleLog_AWindowWithNoEntryIsCleared()
    {
        var script = PageScript.Html;
        var live = script.IndexOf("Object.keys(slots).forEach(function (s) { applySlot(s, slots[s]); });", StringComparison.Ordinal);
        Assert.True(live >= 0, "each entry of the answer is drawn first");
        var sweep = script.IndexOf("document.querySelectorAll('pre.log[data-slot]').forEach(function (p) {", live, StringComparison.Ordinal);
        var skip = script.IndexOf("if (slots[s]) return;", sweep, StringComparison.Ordinal);
        var card = script.IndexOf("var card = windowOf(p);", skip, StringComparison.Ordinal);
        var clears = script.IndexOf("if (card && p.textContent !== startingText(card)) clearSlot(s, card);", card, StringComparison.Ordinal);
        Assert.True(sweep > live && skip > sweep && card > skip && clears > card,
            "a window whose slot is not in the answer goes to the starting line for the card it shows");

        // A window with no card in it is an idle folder's: it is left alone.
        Assert.True(clears > card, "only a window that shows a card is cleared");
    }
}
