using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 1092-C (PAGE-3): a number on screen is only ever replaced by a newer real one. Every running
/// worker's minimised window used to show <c>- T/s</c> for about a second, all at once, because the
/// page the server draws carries no stats for a running worker (the host fills none) and the desk swap
/// put those empty values on screen until the next <c>/live.json</c> poll refilled them; an update with
/// no stats for a slot did the same. The script now remembers what each slot last really showed, by run,
/// keeps it through an empty update, and puts it back after a swap. The script is read, as 470-C,
/// 610-C and 965-C read it.
/// </summary>
public sealed class PageTpsBlinkTests
{
    // PAGE-3 (1092-C item 1) · what each slot showed is remembered per slot, with the run it belongs to.
    [Fact]
    public void Page_TpsBlink_WhatEachSlotShowedIsHeldByRun()
    {
        var script = PageScript.Html;
        Assert.Contains("var held = {};", script, StringComparison.Ordinal);
        Assert.Contains(
            "function holdOf(s) { return held[s] || (held[s] = { run: '', stats: '', tps: '', time: '' }); }",
            script,
            StringComparison.Ordinal);
        Assert.Contains("var k = holdOf(s), run = v.run || '';", script, StringComparison.Ordinal);
    }

    // PAGE-3 (1092-C item 2) · an update with no stats for a running slot leaves the shown line alone:
    // the held text is what is written, and only a real line replaces it.
    [Fact]
    public void Page_TpsBlink_AnUpdateWithNoStatsKeepsTheShownLine()
    {
        var script = PageScript.Html;
        var read = script.IndexOf("var bits = statsBits(v.stats || ''", StringComparison.Ordinal);
        var holds = script.IndexOf("if (bits.stats) k.stats = bits.stats;", read, StringComparison.Ordinal);
        var writes = script.IndexOf("setText(st, k.stats);", holds, StringComparison.Ordinal);
        Assert.True(read >= 0 && holds > read && writes > holds,
            "the stats line on screen is replaced only by a newer real one");

        // The minutes keep the same rule, so a time that arrived empty cannot clear the tag either.
        Assert.Contains("if (v.time) k.time = v.time;", script, StringComparison.Ordinal);
        Assert.Contains("setText(bySlot('.tag', s), k.time);", script, StringComparison.Ordinal);
    }

    // PAGE-3 (1092-C item 3) · the mini title bar's T/s: the average this page last really saw for the
    // run stays up, and the dash is only ever drawn for a worker that has had no stats at all.
    [Fact]
    public void Page_TpsBlink_TheMiniKeepsItsNumberAndBlanksOnlyForANewWorker()
    {
        var script = PageScript.Html;
        var mini = script.IndexOf("function applyMini(s, k) {", StringComparison.Ordinal);
        Assert.True(mini >= 0, "the title bar's number is read from what the slot holds, not from one update");
        var body = script.Substring(mini, script.IndexOf("          }", mini, StringComparison.Ordinal) - mini);
        Assert.Contains("if (m) k.tps = m[1] + ' T/s avg';", body, StringComparison.Ordinal);
        Assert.Contains("setText(t, k.tps || '- T/s');", body, StringComparison.Ordinal);

        // It is called with the slot's own memory, so an update cannot blank it.
        Assert.Contains("applyMini(s, k);", script, StringComparison.Ordinal);
        Assert.DoesNotContain("applyMini(s, bits.stats)", script, StringComparison.Ordinal);
    }

    // PAGE-3 (1092-C item 4) · the desk swap is what blanked every window at once: the values this page
    // held are put back after the new markup arrives, the way the log boxes are.
    [Fact]
    public void Page_TpsBlink_ASwapRefillsTheWindowsFromWhatWasHeld()
    {
        var script = PageScript.Html;
        Assert.Contains("function keepStats() {", script, StringComparison.Ordinal);
        var boxes = script.IndexOf("keepBoxes(logs);", StringComparison.Ordinal);
        var stats = script.IndexOf("keepStats();", boxes, StringComparison.Ordinal);
        Assert.True(boxes >= 0 && stats > boxes, "the held values go back after the boxes, in the same swap");

        // All three live parts of a worker's title bar and body are carried across.
        var body = script.Substring(script.IndexOf("function keepStats() {", StringComparison.Ordinal));
        body = body.Substring(0, body.IndexOf("          }", StringComparison.Ordinal));
        Assert.Contains("setText(bySlot('.stats', s), k.stats);", body, StringComparison.Ordinal);
        Assert.Contains("setText(document.querySelector('.mini[data-slot=\"' + s + '\"] .tps'), k.tps);", body, StringComparison.Ordinal);
        Assert.Contains("setText(bySlot('.tag', s), k.time);", body, StringComparison.Ordinal);

        // Nothing is written from an empty memory: a slot this page never saw keeps what the server drew.
        Assert.Contains("if (k.stats) setText(", body, StringComparison.Ordinal);
        Assert.Contains("if (k.tps) setText(", body, StringComparison.Ordinal);
        Assert.Contains("if (k.time) setText(", body, StringComparison.Ordinal);
    }

    // PAGE-3 (1092-C item 5) · a worker that has never had stats still shows the dash: another run in
    // the slot (its own log file) drops everything held for the worker before it.
    [Fact]
    public void Page_TpsBlink_ANewRunInTheSlotStartsFromNothing()
    {
        var script = PageScript.Html;
        var isNew = script.IndexOf("if (run && run !== k.run) {", StringComparison.Ordinal);
        var cleared = script.IndexOf("k.run = run; k.stats = ''; k.tps = ''; k.time = '';", isNew, StringComparison.Ordinal);
        var stats = script.IndexOf("var bits = statsBits(", cleared, StringComparison.Ordinal);
        Assert.True(isNew >= 0 && cleared > isNew && stats > cleared,
            "a new run clears what the last worker showed before its own numbers arrive");
    }

    // PAGE-3 (1092-C) · what the server puts in a running worker's window is empty: the host fills no
    // stats for a slot, so the markup a swap brings in is dashes and blank lines. This is what the
    // carried-across values above cover.
    [Fact]
    public void Page_TpsBlink_TheServerDrawsNoStatsForARunningWorker()
    {
        var snapshot = ViewsFixture.Full() with
        {
            Slots = [.. ViewsFixture.Full().Slots.Select(slot => slot with { Stats = "" })],
        };
        var html = new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);

        Assert.Contains("<div class='stats' data-slot='1' data-model='claude-opus-4'></div>", html, StringComparison.Ordinal);
        Assert.Contains("<span class='tps'>- T/s</span>", html, StringComparison.Ordinal);
    }
}
