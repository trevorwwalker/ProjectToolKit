using System.Text;
using System.Text.Json;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

/// <summary>
/// PAGE-3 as card 610-C changed it: each worker's rendered lines are numbered from the start of its
/// run, and <c>/live.json</c> takes the number the page has reached per slot
/// (<c>?since=4:1234,1:87</c>) and answers with that slot's lines after it - at most 500 in one
/// answer - plus the line being written. With no <c>since</c> (a fresh page, or a slot the page has
/// seen nothing of yet) it answers with the last 500. A new run in a slot numbers its lines from 1
/// again and names itself in <c>run</c>, so the page can clear the window it had for the old run.
/// </summary>
public sealed class PageLiveHistoryTests
{
    // PAGE-3 (610-C) · a fresh page gets the last 500 rendered lines of the run, numbered: the
    // window starts where it starts and the cursor the page should hold next is the line count.
    [Fact]
    public async Task Page_LiveHistory_FreshPageGetsTheLast500Lines()
    {
        using var web = new WebHarness();
        web.Data.Workers.Add(new LiveWorker(1, web.WriteLog("610-C-20261118-231507.log", Lines(1200)), "1 card", "3 min"));

        using var doc = await LiveAsync(web, "/live.json");
        var slot = Slot(doc, "1");

        var log = Log(slot);
        Assert.Equal(500, log.Count);
        Assert.Equal("l701", log[0]);
        Assert.Equal("l1200", log[^1]);
        Assert.Equal(701, slot.GetProperty("from").GetInt32());
        Assert.Equal(1200, slot.GetProperty("next").GetInt32());
        Assert.Equal("610-C-20261118-231507.log", slot.GetProperty("run").GetString());
        Assert.Equal("", slot.GetProperty("partial").GetString());
    }

    // PAGE-3 (610-C) · with since the answer is only the lines after that number, so the page
    // appends what arrives instead of replacing the window; the line being written comes with it.
    [Fact]
    public async Task Page_LiveHistory_SinceSendsOnlyTheNewerLines()
    {
        using var web = new WebHarness();
        var logFile = web.WriteLog("610-C-20261118-231507.log", Lines(10));
        web.Data.Workers.Add(new LiveWorker(1, logFile, "1 card", "3 min"));

        using (var first = await LiveAsync(web, "/live.json"))
        {
            var element = Slot(first, "1");
            Assert.Equal(10, element.GetProperty("next").GetInt32());
            Assert.Equal("l1", Log(element)[0]);
        }

        File.AppendAllText(logFile, "l11\nl12\nl13\n  half a line more", Encoding.UTF8);
        web.Clock.Advance(TimeSpan.FromMilliseconds(100));

        using var second = await LiveAsync(web, "/live.json?since=1:10");
        var newer = Slot(second, "1");
        Assert.Equal(["l11", "l12", "l13"], Log(newer));
        Assert.Equal(11, newer.GetProperty("from").GetInt32());
        Assert.Equal(13, newer.GetProperty("next").GetInt32());
        Assert.Equal("  ~ half a line more", newer.GetProperty("partial").GetString());
    }

    // PAGE-3 (610-C) · a page caught up with the log gets nothing but the line being written, so an
    // idle worker costs the page no text every 2 s.
    [Fact]
    public async Task Page_LiveHistory_SinceAtTheEndSendsNothing()
    {
        using var web = new WebHarness();
        web.Data.Workers.Add(new LiveWorker(1, web.WriteLog("610-C-20261118-231507.log", Lines(1200)), "1 card", "3 min"));

        using var doc = await LiveAsync(web, "/live.json?since=1:1200");
        var slot = Slot(doc, "1");

        Assert.Empty(Log(slot));
        Assert.Equal(1200, slot.GetProperty("next").GetInt32());
    }

    // PAGE-3 (610-C) · one answer is capped at 500 lines: a page whose cursor is older than what the
    // feed still holds gets the whole window and a from past its cursor, which is how it knows it
    // missed lines and must replace rather than append.
    [Fact]
    public async Task Page_LiveHistory_AnswerIsCappedAt500AndSaysSo()
    {
        using var web = new WebHarness();
        web.Data.Workers.Add(new LiveWorker(1, web.WriteLog("610-C-20261118-231507.log", Lines(1200)), "1 card", "3 min"));

        using var doc = await LiveAsync(web, "/live.json?since=1:1");
        var slot = Slot(doc, "1");

        var log = Log(slot);
        var from = slot.GetProperty("from").GetInt32();
        Assert.Equal(500, log.Count);
        Assert.Equal("l701", log[0]);
        Assert.Equal(701, from);

        // The feed could not go back to the page's cursor, so it says where its window really
        // starts: past the line after the cursor, which is how the page knows to replace.
        Assert.True(from > 2, "the answer must show it missed lines");
    }

    // PAGE-3 (610-C) · a new run in the slot - another card, or the same card started again - writes
    // a new log file, so its lines number from 1 again and the answer names the new run.
    [Fact]
    public async Task Page_LiveHistory_NewRunNumbersFromOneAgain()
    {
        using var web = new WebHarness();
        var firstFile = web.WriteLog("610-C-20261118-231507.log", Lines(10));
        web.Data.Workers.Add(new LiveWorker(1, firstFile, "1 card", "3 min"));
        using (var old = await LiveAsync(web, "/live.json"))
        {
            Assert.Equal(10, Slot(old, "1").GetProperty("next").GetInt32());
        }

        var secondFile = web.WriteLog("620-C-20261118-233000.log", Lines(2));
        web.Data.Workers[0] = web.Data.Workers[0] with { LogPath = secondFile };
        web.Clock.Advance(TimeSpan.FromMilliseconds(100));

        // The page still holds the old run's cursor: the answer numbers the new run from 1 and
        // names it, which is what tells the page to clear the window.
        using var doc = await LiveAsync(web, "/live.json?since=1:10");
        var slot = Slot(doc, "1");
        Assert.Equal("620-C-20261118-233000.log", slot.GetProperty("run").GetString());
        Assert.Equal(["l1", "l2"], Log(slot));
        Assert.Equal(1, slot.GetProperty("from").GetInt32());
        Assert.Equal(2, slot.GetProperty("next").GetInt32());
    }

    // PAGE-3 (610-C) · each slot has its own cursor, so one worker's lines never hold another's back.
    [Fact]
    public async Task Page_LiveHistory_EverySlotHasItsOwnCursor()
    {
        using var web = new WebHarness();
        web.Data.Workers.Add(new LiveWorker(1, web.WriteLog("610-C-20261118-231507.log", Lines(10)), "1 card", "3 min"));
        web.Data.Workers.Add(new LiveWorker(2, web.WriteLog("620-C-20261118-233000.log", Lines(10)), "1 card", "3 min"));

        using var doc = await LiveAsync(web, "/live.json?since=1:10,2:3");
        var slots = doc.RootElement.GetProperty("slots");

        Assert.Empty(Log(Slot(doc, "1")));
        var two = Slot(doc, "2");
        Assert.Equal(["l4", "l5", "l6", "l7", "l8", "l9", "l10"], Log(two));
        Assert.Equal(4, two.GetProperty("from").GetInt32());
        Assert.Equal(10, two.GetProperty("next").GetInt32());
        Assert.Equal(2, slots.EnumerateObject().Count());
    }

    // PAGE-3 (610-C) · a since the page did not mean (garbage, or a number no line has) asks for the
    // whole window rather than failing: the page recovers by itself.
    [Fact]
    public async Task Page_LiveHistory_UnknownSinceAsksForTheWholeWindow()
    {
        using var web = new WebHarness();
        web.Data.Workers.Add(new LiveWorker(1, web.WriteLog("610-C-20261118-231507.log", Lines(6)), "1 card", "3 min"));

        using var doc = await LiveAsync(web, "/live.json?since=1:not-a-number");
        var slot = Slot(doc, "1");

        Assert.Equal(6, Log(slot).Count);
        Assert.Equal(1, slot.GetProperty("from").GetInt32());
        Assert.Equal(6, slot.GetProperty("next").GetInt32());
    }

    // PAGE-3 (610-C) · the ANSI marks come out of the lines the feed sends, the line being written is
    // collapsed and marked, and it is sent apart from the finished lines so the page keeps only the
    // finished ones in its 500-line window. Finished lines keep their own spacing, as they always did.
    [Fact]
    public async Task Page_LiveHistory_RendersTheLinesItSends()
    {
        using var web = new WebHarness();
        const string Raw = "coloured\x1b[31m text\x1b[0m\n   many    spaces   here  \n  no newline yet";
        web.Data.Workers.Add(new LiveWorker(1, web.WriteLog("610-C-20261118-231507.log", Raw), "1 card", "3 min"));

        using var doc = await LiveAsync(web, "/live.json");
        var slot = Slot(doc, "1");

        Assert.Equal(["coloured text", "   many    spaces   here  "], Log(slot));
        Assert.Equal("  ~ no newline yet", slot.GetProperty("partial").GetString());
    }

    private static string Lines(int count) =>
        string.Join("\n", Enumerable.Range(1, count).Select(i => $"l{i}")) + "\n";

    private static async Task<JsonDocument> LiveAsync(WebHarness web, string path) =>
        JsonDocument.Parse(await (await web.Client.GetAsync(path)).Content.ReadAsStringAsync());

    private static JsonElement Slot(JsonDocument doc, string slot) =>
        doc.RootElement.GetProperty("slots").GetProperty(slot);

    private static List<string?> Log(JsonElement slot) =>
        slot.GetProperty("log").EnumerateArray().Select(line => line.GetString()).ToList();
}
