using System.Text.Json;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;

namespace Scheduler.Web.Tests.Server;

/// <summary>
/// Card 1092-C (PAGE-3): a build that reads a worker whose stats line has no numbers in it must not
/// publish a worse line than the one the page already had. The stats line is what the minimised title
/// bar reads its average T/s from, so an empty one showed a dash in every window for a poll; now the
/// last real line of the same run is published again, and the dash is left for a worker that has never
/// had stats - a new run in the slot.
/// </summary>
public sealed class LiveFeedStatsHoldTests
{
    // PAGE-3 (1092-C item 1) · the case that sent empty stats: a worker whose stats line lost its
    // numbers - the renderer had nothing for its slot this build - keeps the line the last build had,
    // so the page is never asked to blank a running worker's T/s.
    [Fact]
    public async Task LiveFeed_StatsHold_AnEmptyLineKeepsTheLastRealOneOfTheRun()
    {
        using var web = new WebHarness();
        var log = web.WriteLog("1092-C-20261122-140000.log", "a line\n");
        web.Data.Workers.Add(new LiveWorker(1, log, "context ~1.2k  |  30.1 T/s now, 12.4 avg  |  spark", "3/45 min"));

        Assert.Equal("context ~1.2k  |  30.1 T/s now, 12.4 avg  |  spark", await StatsOf(web));

        // The same run, read at a moment its stats line has no average in it (no stats scanned yet).
        web.Data.Workers[0] = new LiveWorker(1, log, "context ~1.2k  |  0 model calls", "3/45 min");
        Assert.Equal("context ~1.2k  |  30.1 T/s now, 12.4 avg  |  spark", await StatsOf(web));

        // And a real newer line still wins over the held one.
        web.Data.Workers[0] = new LiveWorker(1, log, "context ~9k  |  22.7 T/s now, 13.9 avg  |  spark", "4/45 min");
        Assert.Equal("context ~9k  |  22.7 T/s now, 13.9 avg  |  spark", await StatsOf(web));
    }

    // PAGE-3 (1092-C item 2) · a worker that never had stats shows none: the dash the page draws for it
    // is correct, so the held line is only used for a run that really had one.
    [Fact]
    public async Task LiveFeed_StatsHold_AWorkerThatNeverHadStatsSendsAnEmptyLine()
    {
        using var web = new WebHarness();
        var log = web.WriteLog("1093-C-20261122-140000.log", "a line\n");
        web.Data.Workers.Add(new LiveWorker(1, log, "spark  |  http://127.0.0.1:11434", "0/45 min"));

        Assert.Equal("spark  |  http://127.0.0.1:11434", await StatsOf(web));
    }

    // PAGE-3 (1092-C item 3) · a new run in the slot is a new worker, not a worker whose numbers went
    // missing: the line held for the run before it does not carry over, so its window starts with the
    // dash until its own stats are sampled.
    [Fact]
    public async Task LiveFeed_StatsHold_ANewRunInTheSlotStartsWithoutTheHeldLine()
    {
        using var web = new WebHarness();
        var first = web.WriteLog("1092-C-20261122-140000.log", "a line\n");
        web.Data.Workers.Add(new LiveWorker(1, first, "30.1 T/s now, 12.4 avg", "3/45 min"));
        Assert.Equal("30.1 T/s now, 12.4 avg", await StatsOf(web));

        // The slot now runs another card: its log file is another run.
        var second = web.WriteLog("1092-C-20261122-141500.log", "a line\n");
        web.Data.Workers[0] = new LiveWorker(1, second, "spark", "0/45 min");
        Assert.Equal("spark", await StatsOf(web));
    }

    // PAGE-3 (1092-C item 4) · in a pool each worker is held by its own (project, slot), so one
    // project's numbers never fill another's empty line (WT-W3).
    [Fact]
    public async Task LiveFeed_StatsHold_EachProjectKeepsItsOwnLine()
    {
        using var web = new WebHarness();
        var a = web.WriteLog("a-1092-C-20261122-140000.log", "a line\n");
        var b = web.WriteLog("b-1092-C-20261122-140000.log", "a line\n");
        web.Data.Workers.Add(new LiveWorker(1, a, "11.1 T/s now, 9.9 avg", "1/45 min", "Alpha"));
        web.Data.Workers.Add(new LiveWorker(1, b, "22.2 T/s now, 8.8 avg", "1/45 min", "Beta"));
        await AskLive(web);

        // Both lose their numbers this build; each keeps its own line.
        web.Data.Workers[0] = new LiveWorker(1, a, "spark", "2/45 min", "Alpha");
        web.Data.Workers[1] = new LiveWorker(1, b, "qwen", "2/45 min", "Beta");

        using var doc = JsonDocument.Parse(await AskLive(web));
        var slots = doc.RootElement.GetProperty("slots");
        Assert.Equal("11.1 T/s now, 9.9 avg", slots.GetProperty("Alpha:1").GetProperty("stats").GetString());
        Assert.Equal("22.2 T/s now, 8.8 avg", slots.GetProperty("Beta:1").GetProperty("stats").GetString());
    }

    /// <summary>Asks for the live view (a build, then the answer) and returns slot 1's stats line.</summary>
    private static async Task<string> StatsOf(WebHarness web)
    {
        using var doc = JsonDocument.Parse(await AskLive(web));
        return doc.RootElement.GetProperty("slots").GetProperty("1").GetProperty("stats").GetString() ?? "";
    }

    /// <summary>One <c>/live.json</c> ask, with the clock moved on so the build really happens.</summary>
    private static async Task<string> AskLive(WebHarness web)
    {
        web.Clock.Advance(TimeSpan.FromSeconds(1));
        var response = await web.Client.GetAsync("/live.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
