// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

/// <summary>
/// Card 1093-C (PAGE-3): a slot that has moved to the next card never shows the card before it. The
/// live feed numbers a run's lines and the page appends them, but the window it appends them to is the
/// slot's, and a build can trail the page by a poll: the worker that ran the last card is still listed
/// while the desk already names the next one. The entry then says what is starting (<c>starting</c>) and
/// carries no lines, so another card's text is never drawn under this card's title; the script clears
/// the window's log, stats and minutes with it.
/// </summary>
public sealed class LiveFeedCardSwitchTests
{
    // PAGE-3 (1093-C item 1) · the case the developer saw: the build still holds the finished worker
    // while the page's window names the next card. The answer for that slot has no lines at all.
    [Fact]
    public async Task LiveFeed_CardSwitch_ABuildBehindThePageSendsNoLines()
    {
        using var web = new WebHarness();
        var log = web.WriteLog("1093-C-before-card-20261122-150000.log", "the old card said this\n");
        web.Data.Workers.Add(new LiveWorker(1, log, "context ~3k  |  30.1 T/s now, 12.4 avg  |  spark", "3/45 min"));
        SetCard(web, 1, "1093-C-before-card.md");
        await AskLive(web);

        // The slot takes the next card while its old worker is still the one being listed.
        SetCard(web, 1, "1094-C-next-card.md");
        var json = await AskLive(web);
        Assert.DoesNotContain("the old card said this", json, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(json);
        var slot = doc.RootElement.GetProperty("slots").GetProperty("1");
        Assert.Equal("1094-C", slot.GetProperty("starting").GetString());
        Assert.Empty(slot.GetProperty("log").EnumerateArray());
        Assert.Equal("", slot.GetProperty("partial").GetString());
        Assert.Equal("", slot.GetProperty("run").GetString());
        Assert.Equal("", slot.GetProperty("stats").GetString());
        Assert.Equal("", slot.GetProperty("time").GetString());
        Assert.Equal(0, slot.GetProperty("next").GetInt32());

        // The page's own view of the slot says the same thing as its answer.
        Assert.Equal("(starting 1094-C...)", web.Feed.Snapshot.Slots[1].Log.Single());
        Assert.Equal("", web.Feed.Snapshot.Slots[1].Stats);
    }

    // PAGE-3 (1093-C item 2) · a worker whose card is the one its window shows is left alone: its
    // lines, its stats and its minutes go out as they always did.
    [Fact]
    public async Task LiveFeed_CardSwitch_TheSlotsOwnWorkerIsUntouched()
    {
        using var web = new WebHarness();
        var log = web.WriteLog("1093-C-own-card-20261122-150000.log", "what it is doing now\n");
        web.Data.Workers.Add(new LiveWorker(1, log, "context ~3k  |  30.1 T/s now, 12.4 avg  |  spark", "3/45 min"));
        SetCard(web, 1, "1093-C-own-card.md");

        using var doc = JsonDocument.Parse(await AskLive(web));
        var slot = doc.RootElement.GetProperty("slots").GetProperty("1");
        Assert.Equal("", slot.GetProperty("starting").GetString());
        Assert.Equal("what it is doing now", slot.GetProperty("log").EnumerateArray().Single().GetString());
        Assert.Contains("12.4 avg", slot.GetProperty("stats").GetString(), StringComparison.Ordinal);
    }

    // PAGE-3 (1093-C item 3) · the switch answers at once: the first build that sees the new card's
    // worker ends the starting line, and the whole window comes back with no cursor to catch up.
    [Fact]
    public async Task LiveFeed_CardSwitch_TheNewCardsWorkerComesBackAfterIt()
    {
        using var web = new WebHarness();
        var old = web.WriteLog("1093-C-before-card-20261122-150000.log", "the old card said this\n");
        web.Data.Workers.Add(new LiveWorker(1, old, "30.1 T/s now, 12.4 avg", "3/45 min"));
        SetCard(web, 1, "1094-C-next-card.md");
        await AskLive(web);

        // The new worker is running now, writing to its own log.
        var next = web.WriteLog("1094-C-next-card-20261122-151000.log", "the new card said this\n");
        web.Data.Workers[0] = new LiveWorker(1, next, "2.2 T/s now, 2.0 avg", "0/45 min");
        using var doc = JsonDocument.Parse(await AskLive(web));
        var slot = doc.RootElement.GetProperty("slots").GetProperty("1");
        Assert.Equal("", slot.GetProperty("starting").GetString());
        Assert.Equal("the new card said this", slot.GetProperty("log").EnumerateArray().Single().GetString());
        Assert.Equal("1094-C-next-card-20261122-151000.log", slot.GetProperty("run").GetString());
    }

    // PAGE-3 (1093-C item 4) · in a pool each project's window is read from that project's own page, so
    // another project's same slot number cannot make a running worker look stale (WT-W3).
    [Fact]
    public async Task LiveFeed_CardSwitch_EachProjectIsJudgedByItsOwnPage()
    {
        using var web = new WebHarness();
        var a = web.WriteLog("1093-C-alpha-20261122-150000.log", "alpha's line\n");
        var b = web.WriteLog("1095-C-beta-20261122-150000.log", "beta's line\n");
        web.Data.Workers.Add(new LiveWorker(1, a, "1.1 T/s now, 1.0 avg", "1/45 min", "Alpha"));
        web.Data.Workers.Add(new LiveWorker(1, b, "2.2 T/s now, 2.0 avg", "1/45 min", "Beta"));

        // The page holds no window for either: with nothing to compare with, both keep their lines.
        using var doc = JsonDocument.Parse(await AskLive(web));
        var slots = doc.RootElement.GetProperty("slots");
        Assert.Equal("alpha's line", slots.GetProperty("Alpha:1").GetProperty("log").EnumerateArray().Single().GetString());
        Assert.Equal("beta's line", slots.GetProperty("Beta:1").GetProperty("log").EnumerateArray().Single().GetString());
        Assert.Equal("", slots.GetProperty("Alpha:1").GetProperty("starting").GetString());
    }

    /// <summary>Says what the page's window for <paramref name="slot"/> shows now.</summary>
    private static void SetCard(WebHarness web, int slot, string card) =>
        web.Data.Latest = web.Data.Latest with
        {
            Slots = [.. web.Data.Latest.Slots.Where(s => s.Slot != slot), new PageSlotView { Slot = slot, Card = card }],
        };

    /// <summary>One <c>/live.json</c> ask, with the clock moved on so the build really happens.</summary>
    private static async Task<string> AskLive(WebHarness web)
    {
        web.Clock.Advance(TimeSpan.FromSeconds(1));
        var response = await web.Client.GetAsync("/live.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
