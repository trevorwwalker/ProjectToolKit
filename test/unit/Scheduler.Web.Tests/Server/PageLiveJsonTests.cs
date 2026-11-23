using System.Net;
using System.Text.Json;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

public class PageLiveJsonTests
{
    // PAGE-3: /live.json is {t, pageDone, page, checking, slots:{n:{log, partial, from, next, run,
    // stats, time}}}; the log is the last 500 rendered lines of the run numbered from its start
    // (610-C) and the line being written comes in partial; builds stop 5 s after the last ask and
    // the gap between builds is at least twice the last build's time.
    [Fact]
    public async Task Page_LiveJson_RateAndShape()
    {
        using var web = new WebHarness();

        var raw = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"line {i}")) + "\n" + "  half a line more";
        web.Data.Workers.Add(new LiveWorker(1, web.WriteLog("050-C-20261118-120000.log", raw), "3 cards, 42 min", "0:12:34"));
        web.Data.Workers.Add(new LiveWorker(2, web.WriteLog("051-C-20261118-120001.log", raw), "1 card, 3 min", "0:01:02"));

        // Reading the logs costs 200 ms on the fake clock, so the next build must wait 400 ms.
        web.Data.BeforeWorkers = () => web.Clock.Advance(TimeSpan.FromMilliseconds(200));

        var response = await web.Client.GetAsync("/live.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.String, root.GetProperty("t").ValueKind);
        Assert.Equal(JsonValueKind.True, root.GetProperty("pageDone").ValueKind);
        Assert.Equal("", root.GetProperty("checking").GetString());
        var slots = root.GetProperty("slots");
        Assert.Equal(2, slots.EnumerateObject().Count());

        foreach (var name in new[] { "1", "2" })
        {
            var slot = slots.GetProperty(name);
            Assert.Equal(JsonValueKind.Array, slot.GetProperty("log").ValueKind);
            Assert.Equal(JsonValueKind.String, slot.GetProperty("stats").ValueKind);
            Assert.Equal(JsonValueKind.String, slot.GetProperty("time").ValueKind);
        }

        var log = slots.GetProperty("1").GetProperty("log").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(50, log.Count);
        Assert.Equal("line 1", log[0]);
        Assert.Equal("line 50", log[49]);
        var one = slots.GetProperty("1");
        Assert.Equal("  ~ half a line more", one.GetProperty("partial").GetString());
        Assert.Equal(1, one.GetProperty("from").GetInt32());
        Assert.Equal(50, one.GetProperty("next").GetInt32());
        Assert.Equal("050-C-20261118-120000.log", one.GetProperty("run").GetString());
        Assert.Equal("3 cards, 42 min", slots.GetProperty("1").GetProperty("stats").GetString());
        Assert.Equal("0:01:02", slots.GetProperty("2").GetProperty("time").GetString());

        // The build was one, it took 200 ms of the fake clock, and a second ask straight away
        // does not build again: the gap must reach twice the build's own time.
        Assert.Equal(1, web.Feed.Builds);
        Assert.Equal(TimeSpan.FromMilliseconds(200), web.Feed.LastBuild);

        var again = await web.Client.GetAsync("/live.json");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, web.Feed.Builds);

        web.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.False(web.Feed.BuildDue());
        web.Clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.True(web.Feed.BuildDue());

        // Five seconds after the last ask the builds stop, however long the page keeps waiting.
        web.Clock.Advance(TimeSpan.FromSeconds(5).Add(TimeSpan.FromMilliseconds(1)));
        Assert.False(web.Feed.Active);
        Assert.False(web.Feed.BuildDue());
        Assert.Equal(1, web.Feed.Builds);
    }

    // PAGE-3 (470-C) · /live.json also carries the page snapshot's version, so the page knows
    // when anything beyond the workers' live lines has changed and refreshes itself.
    [Fact]
    public async Task Page_LiveJson_CarriesPageVersion()
    {
        using var web = new WebHarness();
        var before = PageVersion.Of(web.Data.Latest);

        using (var first = JsonDocument.Parse(await (await web.Client.GetAsync("/live.json")).Content.ReadAsStringAsync()))
        {
            Assert.Equal(before, first.RootElement.GetProperty("page").GetString());
        }

        web.Data.Latest = web.Data.Latest with { Notice = "a card finished" };
        web.Clock.Advance(TimeSpan.FromSeconds(1));

        using var second = JsonDocument.Parse(await (await web.Client.GetAsync("/live.json")).Content.ReadAsStringAsync());
        var page = second.RootElement.GetProperty("page").GetString();
        Assert.NotEqual(before, page);
        Assert.Equal(PageVersion.Of(web.Data.Latest), page);
    }

    // 930-C (the developer, 2026-11-19) · /live.json also carries the health check that is running,
    // as the sha it started on ("" when none is): the page's Check main button shows the press from
    // this, so the reader gets feedback without a reload.
    [Fact]
    public async Task Page_LiveJson_CarriesTheRunningHealthCheck()
    {
        using var web = new WebHarness();
        web.Data.Latest = web.Data.Latest with
        {
            Health = new PageHealthView { Green = true, Sha = "1a2b3c4", Text = "main 1a2b3c4 green at 12:20; checking 9f8e7d6c (2 min)" },
        };

        using (var first = JsonDocument.Parse(await (await web.Client.GetAsync("/live.json")).Content.ReadAsStringAsync()))
        {
            Assert.Equal("9f8e7d6c", first.RootElement.GetProperty("checking").GetString());
        }

        // The check finished: the same field goes empty, and the page script puts the button back.
        web.Data.Latest = web.Data.Latest with
        {
            Health = new PageHealthView { Green = true, Sha = "9f8e7d6c", Text = "main 9f8e7d6c green at 12:24" },
        };
        web.Clock.Advance(TimeSpan.FromSeconds(1));

        using var second = JsonDocument.Parse(await (await web.Client.GetAsync("/live.json")).Content.ReadAsStringAsync());
        Assert.Equal("", second.RootElement.GetProperty("checking").GetString());
    }
}
