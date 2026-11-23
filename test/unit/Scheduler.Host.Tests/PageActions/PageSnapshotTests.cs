using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.Health;
using Scheduler.Core.PageModel;
using Scheduler.Core.WebContract;
using Scheduler.Host.PageActions;
using Scheduler.Host.State;
using Scheduler.Web.Views;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// 450-B: the page snapshot Host builds from the tick fills every window of PAGE-5, from Core's own
/// builders (PAGE-6 queue, the Waiting on you rows, ACCT-6 stats, HLTH-5/HLTH-6 health) and the
/// state Host keeps beside the tick (accounting, health check, notice, finished-card flags).
/// </summary>
public class PageSnapshotTests
{
    private const string CategoriesV1 = """
        {"lanes":{"A":{"name":"core","purpose":"logic","owns":["src/a/"],"except":[]},
                   "B":{"name":"host","purpose":"machine","owns":["src/b/"],"except":[]}}}
        """;

    // workers.json as this project runs it (jobs/workers.json): two plain slots and two premium
    // Claude workers, each taking any category (750-B).
    private const string WorkersFour = """
        {"slots":4,"workers":{
           "1":{"name":"","lanes":"AB","premium":false},
           "2":{"name":"","lanes":"AB","premium":false},
           "3":{"name":"Sonnet","model":"claude-sonnet-5-5","lanes":"","premium":true},
           "4":{"name":"Opus","model":"claude-opus-5-5","lanes":"","premium":true}}}
        """;

    // The same four workers as the pool form (WT-C11), which is what this project runs: the folder
    // number then says nothing about who works in it (WT-W1), so only the type can name the model (959-A).
    private const string WorkersPool = """
        {"pool":{"maxWorkers":4,"types":{
           "default":{"max":4,"agent":"","model":"","premium":false,"areas":"AB"},
           "Sonnet":{"max":4,"agent":"claude","model":"claude-sonnet-5-5","premium":true,"areas":""},
           "Opus":{"max":4,"agent":"claude","model":"claude-opus-5-5","premium":true,"areas":""}}}}
        """;

    // The areas a design card needs: its own scope, so it waits on no running card of another area.
    private const string CategoriesQueue = """
        {"lanes":{"A":{"name":"core","purpose":"logic","owns":["code/src/Scheduler.Core/"],"except":[]},
                   "B":{"name":"host","purpose":"machine","owns":["code/src/Scheduler.Host/"],"except":[]},
                   "D":{"name":"design","purpose":"design notes","owns":["docs/design/tdd/scheduler/"],"except":[]}}}
        """;

    private const string CategoriesV2 = """
        {"lanes":{"A":{"name":"core","purpose":"logic","owns":["src/a/","src/shared/"],"except":[]},
                   "B":{"name":"host","purpose":"machine","owns":["src/b/"],"except":[]}}}
        """;

    // PAGE-5, LANE-3 as changed by SC-4 (320-A Q5, 560-A): a redefined category is not held, so it is
    // no Waiting on you row and has no Accept button.
    [Fact]
    public void Snapshot_RedefinedCategory_NoWaitingRow()
    {
        using var harness = RedefinedCategoryHarness();

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick);

        Assert.DoesNotContain(snapshot.Waiting, w => w.Kind == PageWaitingKind.HeldCategory);
    }

    // PAGE-5, PAGE-6: every queued card is a Queue row in dispatch order, each with why it waits.
    [Fact]
    public void Snapshot_Queue_ListsEveryOpenCardWithItsWhy()
    {
        using var harness = new PageActionHarness(categoriesJson: CategoriesV1, project: "ptk-450b-queue");
        harness.Tick.State = SchedulerState.Paused;
        harness.Host.Cards.Add(Card(CardFolder.Queue, 30, "A") with { Depends = ["020-B"] });
        harness.Host.Cards.Add(Card(CardFolder.Queue, 10, "A"));
        harness.Host.Cards.Add(Card(CardFolder.Queue, 20, "B"));
        harness.Tick_Once();

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick);

        Assert.Equal(["010-A-x", "020-B-x", "030-A-x"], snapshot.Queue.Select(q => q.Name));
        Assert.All(snapshot.Queue, q => Assert.False(string.IsNullOrWhiteSpace(q.Note), $"{q.Name} has no note"));
        Assert.Contains("020-B", snapshot.Queue[2].Note);
    }

    // PAGE-6, DISP-19 (750-B): a queued card a dispatch could not place shows the dispatcher's note,
    // not just its Status - "ready" with an idle worker that does not fit it told the developer nothing.
    [Fact]
    public void Snapshot_Queue_ReadyCardNoFreeWorker_ShowsDispatcherNote()
    {
        using var harness = new PageActionHarness(workersJson: """{"slots":1}""", project: "ptk-750b-free");
        var running = Card(CardFolder.Active, 10, "B");
        harness.Host.Cards.Add(running);
        harness.Host.Cards.Add(Card(CardFolder.Queue, 20, "A"));
        harness.Running(1, running.Name, running.Category);

        harness.Tick_Once();

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick);

        var row = Assert.Single(snapshot.Queue, q => q.Name == "020-A-x");
        Assert.Equal("next up; waiting for worker (1 of 1 running: 010-B)", row.Note);
    }

    // PAGE-6, DISP-18, DISP-19 (750-B): 530-D's shape - a queued card naming a premium worker by
    // name. A free fitting slot takes it; when that slot is busy the Queue names the worker it waits for.
    [Fact]
    public void Snapshot_Queue_CardAskingNamedWorker_StartsOrNamesTheWorker()
    {
        using var harness = new PageActionHarness(workersJson: WorkersFour, categoriesJson: CategoriesQueue, project: "ptk-750b-opus");
        harness.Host.Cards.Add(Card(CardFolder.Queue, 530, "D") with { WorkerReq = "Opus" });

        harness.Tick_Once();

        Assert.Equal([(4, "530-D", false)], harness.Host.Starts);

        var busy = Card(CardFolder.Active, 500, "A");
        harness.Host.Cards.Add(busy);
        harness.Running(4, busy.Name, busy.Category);
        harness.Host.Starts.Clear();

        harness.Tick_Once();

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick);

        var row = Assert.Single(snapshot.Queue, q => q.Name == "530-D-x");
        Assert.Equal("next up; waiting for Opus (1 of 1 running: 500-A)", row.Note);
    }

    // HLTH-5, HLTH-6, PAGE-5: not checked, red (banner with the sha), then green again.
    [Fact]
    public void Snapshot_Health_GreenRedAndBanner()
    {
        using var harness = new PageActionHarness(project: "ptk-450b-health");
        var config = harness.Config with { HealthCommand = "dotnet test" };
        var runner = new GateRunner { Sha = "abc1234", Exit = 1 };
        var health = new HealthCheck(config, harness.Clock, harness.Log, runner);
        var sources = new PageSnapshotSources { Health = health };

        var before = HostPageSnapshot.Build(config, harness.Clock, harness.Tick, sources);
        Assert.True(before.Health.Green);
        Assert.Equal("main not checked yet", before.Health.Text);
        Assert.Equal("", before.Health.Sha);

        health.CheckNow();
        Assert.True(health.StartIfWanted());
        var during = HostPageSnapshot.Build(config, harness.Clock, harness.Tick, sources);
        Assert.Contains("checking abc1234", during.Health.Text);

        harness.Tick.State = health.Reap(harness.Tick.State);
        var red = HostPageSnapshot.Build(config, harness.Clock, harness.Tick, sources);
        Assert.False(red.Health.Green);
        Assert.Equal("abc1234", red.Health.Sha);
        Assert.Contains("main abc1234 RED at", red.Health.Text);
        Assert.Equal(SchedulerState.PausedRed, red.State.State);
        var page = harness.Renderer.Render(red, remote: false, controlsAllowed: true);
        Assert.Contains("<div class='health-red'>", page);
        Assert.Contains("abc1234", page);

        runner.Sha = "def5678";
        runner.Exit = 0;
        health.CheckNow();
        Assert.True(health.StartIfWanted());
        harness.Tick.State = health.Reap(harness.Tick.State);
        var green = HostPageSnapshot.Build(config, harness.Clock, harness.Tick, sources);
        Assert.True(green.Health.Green);
        Assert.Contains("main def5678 green at", green.Health.Text);
        Assert.DoesNotContain("<div class='health-red'>", harness.Renderer.Render(green, remote: false, controlsAllowed: true));
    }

    // PAGE-5, DISP-7: stuck, question, waiting, review and finished-without-update all reach Waiting on you.
    [Fact]
    public void Snapshot_WaitingKinds_AllPresent()
    {
        using var harness = new PageActionHarness(categoriesJson: CategoriesV1, project: "ptk-450b-waiting");
        harness.Tick.State = SchedulerState.Paused;
        harness.Host.Cards.Add(Card(CardFolder.Active, 10, "A"));
        harness.Host.Cards.Add(Card(CardFolder.Active, 20, "B") with
        {
            Status = "needs-human: which way?",
            Questions = [new CardQuestion { Number = 1, Text = "Which way?" }],
        });
        harness.Host.Cards.Add(Card(CardFolder.Queue, 30, "A") with { Status = "waiting: developer, see Q1" });
        harness.Host.Cards.Add(Card(CardFolder.Review, 40, "B") with { Status = "review: check the page" });
        harness.Host.Cards.Add(Card(CardFolder.Review, 50, "A") with { Status = "review: check it" });
        harness.Tick.Retries["010-A"] = harness.Config.MaxRetries;
        harness.Tick_Once();
        // A flag with only a name (Host has never had the folder or blob) still lists the card until
        // it leaves review/; with them it leaves as soon as main moves on (410-A, 949-B). A card in
        // done/ waits for nothing, flagged or not (1054-B).
        var sources = new PageSnapshotSources { FinishedWithoutUpdate = () => [new FinishedCardFlag { Card = "050-A-x" }] };

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, sources);

        Assert.Contains(snapshot.Waiting, w => w.Kind == PageWaitingKind.Stuck && w.Name == "010-A-x");
        Assert.Contains(snapshot.Waiting, w => w.Kind == PageWaitingKind.Question && w.Name == "020-B-x");
        Assert.Contains(snapshot.Waiting, w => w.Kind == PageWaitingKind.Waiting && w.Name == "030-A-x");
        Assert.Contains(snapshot.Waiting, w => w.Kind == PageWaitingKind.Review && w.Name == "040-B-x");
        Assert.Contains(snapshot.Waiting, w => w.Kind == PageWaitingKind.FinishedWithoutUpdate && w.Name == "050-A-x");
    }

    // PAGE-5, DISP-7, ACCT-5 (946-B): Host passes the stuck card's own run count and its newest
    // finished run to the Waiting on you row, so the row can say what the card did and which log to read.
    [Fact]
    public void Snapshot_StuckCard_WaitingRowNamesLastRunAndItsLog()
    {
        using var harness = new PageActionHarness(categoriesJson: CategoriesV1, project: "ptk-946b-stuck");
        harness.Tick.State = SchedulerState.Paused;
        harness.Host.Cards.Add(Card(CardFolder.Active, 10, "A"));
        harness.Tick.Retries["010-A"] = harness.Config.MaxRetries;
        harness.Tick_Once();
        // Newest first, as accounting's recent list is (ACCT-5): the row takes the newest run, not the oldest.
        var recent = new[]
        {
            new RecentEvent
            {
                When = harness.Clock.Now,
                Slot = 1,
                Card = "010-A-x",
                Result = "crashed (exit 1)",
                Minutes = 3,
                Log = @"logs\010-A-20261118-093012.log",
            },
            new RecentEvent
            {
                When = harness.Clock.Now.AddMinutes(-40),
                Slot = 1,
                Card = "010-A-x",
                Result = "stalled",
                Minutes = 40,
                Log = @"logs\010-A-20261118-080000.log",
            },
        };
        var sources = new PageSnapshotSources { Recent = () => recent };

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, sources);

        var row = Assert.Single(snapshot.Waiting, w => w.Kind == PageWaitingKind.Stuck);
        Assert.Equal("010-A-x", row.Name);
        Assert.Equal("crashed (exit 1)", row.LastResult);
        Assert.Equal(@"logs\010-A-20261118-093012.log", row.Log);
        Assert.Equal(
            "2 runs ended without a result (last: crashed (exit 1)). Read its logs, fix the card, then Retry.",
            row.Note);
    }

    // 946-B: a recent row may name the card by its id rather than its file name, and a card with no
    // finished run keeps the older wording with nothing to link.
    [Fact]
    public void Snapshot_StuckCard_RecentNamedByCardIdOrMissing_StillFillsWhatItKnows()
    {
        using var harness = new PageActionHarness(categoriesJson: CategoriesV1, project: "ptk-946b-stuck-id");
        harness.Tick.State = SchedulerState.Paused;
        harness.Host.Cards.Add(Card(CardFolder.Active, 10, "A"));
        harness.Host.Cards.Add(Card(CardFolder.Active, 20, "B"));
        harness.Tick.Retries["010-A"] = harness.Config.MaxRetries;
        harness.Tick.Retries["020-B"] = harness.Config.MaxRetries;
        harness.Tick_Once();
        var recent = new[]
        {
            new RecentEvent
            {
                When = harness.Clock.Now,
                Slot = 2,
                Card = "010-A",
                Result = "no log",
                Minutes = 1,
            },
        };
        var sources = new PageSnapshotSources { Recent = () => recent };

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, sources);

        var named = Assert.Single(snapshot.Waiting, w => w.Kind == PageWaitingKind.Stuck && w.Name == "010-A-x");
        Assert.Equal("no log", named.LastResult);
        Assert.Equal("", named.Log);
        Assert.Contains("(last: no log)", named.Note, StringComparison.Ordinal);

        var unknown = Assert.Single(snapshot.Waiting, w => w.Kind == PageWaitingKind.Stuck && w.Name == "020-B-x");
        Assert.Equal("", unknown.LastResult);
        Assert.Equal("", unknown.Log);
        Assert.Contains("(stalled or killed)", unknown.Note, StringComparison.Ordinal);
    }

    // PAGE-5, ACCT-5: Recent events are the last 10 finished runs, newest first, toned by result.
    [Fact]
    public void Snapshot_Events_LastTen()
    {
        using var harness = new PageActionHarness(project: "ptk-450b-events");
        var start = harness.Clock.Now;
        var recent = Enumerable.Range(0, 12)
            .Select(i => new RecentEvent
            {
                When = start.AddMinutes(-i),
                Slot = 1 + (i % 2),
                Card = $"{100 + i}-A-x",
                Result = i == 1 ? "failed" : "done",
                Minutes = i,
            })
            .ToList();
        var sources = new PageSnapshotSources { Recent = () => recent };

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, sources);

        Assert.Equal(10, snapshot.Events.Count);
        Assert.Equal(start, snapshot.Events[0].Time);
        Assert.Contains("100-A-x", snapshot.Events[0].Text);
        Assert.Contains("done", snapshot.Events[0].Text);
        Assert.Equal(PageTone.Ok, snapshot.Events[0].Tone);
        Assert.Equal(PageTone.Bad, snapshot.Events[1].Tone);
        Assert.DoesNotContain(snapshot.Events, e => e.Text.Contains("110-A-x"));
    }

    // ACCT-6: the Control window's stats come from accounting's runs of the last 24 hours.
    [Fact]
    public void Snapshot_Stats_FromAccounting()
    {
        using var harness = new PageActionHarness(project: "ptk-450b-stats");
        harness.Host.Cards.Add(Card(CardFolder.Done, 10, "A") with { Status = "done" });
        harness.Tick.State = SchedulerState.Paused;
        harness.Tick_Once();
        var now = harness.Clock.Now;
        IReadOnlyList<RunStat> runs =
        [
            new() { Finished = now.AddHours(-2), Result = "stopped", Minutes = 30, Model = "opus", Card = "010-A" },
            new() { Finished = now.AddHours(-1), Result = "done", Minutes = 15, Model = "opus", Card = "010-A" },
            new() { Finished = now.AddHours(-30), Result = "done", Minutes = 99, Model = "opus", Card = "090-A" },
        ];
        var sources = new PageSnapshotSources { Runs = () => runs };

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, sources);

        Assert.Equal(1, snapshot.Stats.Cards);
        Assert.Equal(45, snapshot.Stats.Minutes);
        Assert.StartsWith("Last 24 h (45 min): 1 cards (2 runs): done 1,", snapshot.Stats.Text);
        Assert.DoesNotContain("%", snapshot.Stats.Text, StringComparison.Ordinal);
    }

    // 1095-C: the summary and the Trends buckets for all eight ranges in one pass, from the 14 days of
    // rows the Trends window reads, so the page switches range without a round trip.
    [Fact]
    public void Snapshot_Stats_EightRanges_FromTheFourteenDayRows()
    {
        using var harness = new PageActionHarness(project: "ptk-1095c-ranges");
        harness.Host.Cards.Add(Card(CardFolder.Done, 10, "A") with { Status = "done" });
        harness.Host.Cards.Add(Card(CardFolder.Done, 20, "A") with { Status = "done" });
        harness.Tick.State = SchedulerState.Paused;
        harness.Tick_Once();
        var now = harness.Clock.Now;
        IReadOnlyList<RunStat> recent = [new() { Finished = now.AddMinutes(-20), Result = "done", Minutes = 5, Model = "opus", Card = "010-A" }];
        IReadOnlyList<RunStat> wide =
        [
            new() { Finished = now.AddMinutes(-20), Result = "done", Minutes = 5, Model = "opus", Card = "010-A" },
            new() { Finished = now.AddDays(-5), Result = "done", Minutes = 10, Model = "opus", Card = "020-A" },
        ];

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, new PageSnapshotSources { Runs = () => recent, TrendRuns = () => wide });

        Assert.Equal(new[] { "1h", "3h", "6h", "12h", "24h", "3d", "1w", "2w" }, snapshot.Stats.Ranges.Select(r => r.Key).ToArray());
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 1, 2, 2 }, snapshot.Stats.Ranges.Select(r => r.Cards).ToArray());
        Assert.StartsWith("Last 1 h (5 min): 1 cards (1 runs)", snapshot.Stats.Ranges[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("Last 1 week (15 min): 2 cards (2 runs)", snapshot.Stats.Ranges[6].Text, StringComparison.Ordinal);
        Assert.Equal(new[] { 12, 12, 12, 12, 24, 12, 7, 14 }, snapshot.Stats.Ranges.Select(r => r.Trend.Count).ToArray());
        Assert.Equal(1, snapshot.Stats.Ranges[0].Trend.Sum(b => b.Runs));
        Assert.Equal(2, snapshot.Stats.Ranges[7].Trend.Sum(b => b.Runs));
        // The 24 h figures every other caller reads are as they were.
        Assert.StartsWith("Last 24 h (5 min): 1 cards (1 runs)", snapshot.Stats.Text, StringComparison.Ordinal);
    }

    // TREND-1 and TREND-2 (770-B): the Trends window is built from accounting's own 14-day read, and
    // falls back to the Control figures when that source is not there. The grouping and the
    // less-than-one-day hiding are Core's, unchanged.
    [Fact]
    public void Snapshot_Trends_FromAccountingThenFigures()
    {
        using var harness = new PageActionHarness(project: "ptk-770b-trends");
        harness.Tick.State = SchedulerState.Paused;
        harness.Tick_Once();
        var now = harness.Clock.Now;
        IReadOnlyList<RunStat> fileRuns =
        [
            new() { Finished = now.AddDays(-1), Result = "done", Minutes = 15, Model = "opus", Card = "010-A" },
            new() { Finished = now.AddDays(-2), Result = "done", Minutes = 20, Model = "opus", Card = "020-B" },
        ];
        IReadOnlyList<RunStat> figures =
        [
            new() { Finished = now.AddDays(-1), Result = "done", Minutes = 15, Model = "sonnet", Card = "010-A" },
            new() { Finished = now.AddDays(-3), Result = "done", Minutes = 20, Model = "sonnet", Card = "020-B" },
        ];

        var fromFile = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, new PageSnapshotSources { TrendRuns = () => fileRuns, Runs = () => figures });
        Assert.Equal(Trends.WindowDays, fromFile.Trends.Count);
        Assert.Equal(2, fromFile.Trends.Count(d => d.HasData));
        Assert.Equal("opus", fromFile.Trends[^2].Segments.Single().Model);
        Assert.Equal(1, fromFile.Trends[^2].Segments.Single().CardsDone);

        var fromFigures = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, new PageSnapshotSources { Runs = () => figures });
        Assert.Equal(2, fromFigures.Trends.Count(d => d.HasData));
        Assert.Equal("sonnet", fromFigures.Trends[^2].Segments.Single().Model);

        // One day of bars is not a trend (630-A): an empty window hides the chart.
        var oneDay = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, new PageSnapshotSources
        {
            TrendRuns = () => [fileRuns[0]],
        });
        Assert.Empty(oneDay.Trends);
    }

    // PAGE-5, REQ: open requests, the cards waiting on the planner with "all done", and the planner line.
    [Fact]
    public void Snapshot_PlannerAndRequests()
    {
        using var harness = new PageActionHarness(project: "ptk-450b-planner");
        harness.Tick.State = SchedulerState.Paused;
        harness.Host.Cards.Add(Card(CardFolder.Done, 10, "A") with { Status = "done" });
        harness.Host.Cards.Add(Card(CardFolder.Queue, 20, "A") with { Status = "waiting: planner (request #3)", Depends = ["010-A"] });
        harness.Host.Requests.Add(new JobRequest { Path = "jobs/requests/003-x.md", Name = "003-x", Title = "Split it", Open = true });
        harness.Tick_Once();

        var without = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick);
        Assert.Equal("", without.PlannerLine);

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, new PageSnapshotSources { PlannerPush = () => true });

        Assert.Equal("003-x", Assert.Single(snapshot.Planner.OpenRequests).Name);
        Assert.Equal(["020-A-x"], snapshot.Planner.WaitingCards);
        Assert.True(snapshot.Planner.AllDone);
        Assert.Equal("Planner push: on", snapshot.PlannerLine);
        Assert.DoesNotContain(snapshot.Queue, q => q.Name == "020-A-x");
    }

    // MOVE-11: a failed page action is the notice at the top for 10 minutes, or until a later action succeeds.
    [Fact]
    public void Snapshot_Notice_FailedActionForTenMinutes()
    {
        using var harness = new PageActionHarness(project: "ptk-450b-notice");
        var notice = new PageNotice(harness.Clock);
        var sources = new PageSnapshotSources { Notice = notice };

        notice.Failed("open from the page failed: no editor");
        Assert.Contains("open from the page failed", HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, sources).Notice);

        notice.Succeeded();
        Assert.Equal("", HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, sources).Notice);

        notice.Failed("again");
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal("", HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, sources).Notice);
    }

    // MOVE-11: an accepted page action reports success, which is what clears the notice.
    [Fact]
    public async Task Actions_Accepted_ClearsNotice()
    {
        using var harness = new PageActionHarness(project: "ptk-450b-actions");

        await harness.Post("/pause");

        Assert.Equal(1, harness.Successes);
    }

    // PAGE-5 top bar: the host load the tick sampled.
    [Fact]
    public void Snapshot_HostLoad_FromTick()
    {
        using var harness = new PageActionHarness(project: "ptk-450b-load");
        harness.Tick.State = SchedulerState.Paused;
        harness.Tick_Once();

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick);

        Assert.Equal(harness.Tick.Load, snapshot.HostLoad);
    }

    // 959-A (WT-C4, WT-W1): a running folder's row names the model of the type the worker was started
    // as, not the model the folder number was given. A premium Opus worker sits in folder 2, which the
    // default type (Copilot on spark) last used; the window must say Opus, not spark.
    [Fact]
    public void Snapshot_RunningTypeWorker_SlotRowNamesTheTypesModel()
    {
        using var harness = new PageActionHarness(workersJson: WorkersPool, categoriesJson: CategoriesQueue, project: "ptk-959a-type");
        harness.Tick_Once();   // read workers.json, so the snapshot has the types
        var worker = harness.Running(2, "955-D-x", "D");
        harness.Tick.Adopt(worker with { Type = "Opus", Agent = "claude", Model = "claude-opus-5-5" });

        var row = Assert.Single(HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Slots, s => s.Slot == 2);

        Assert.Equal("Opus", row.Type);
        Assert.Equal("claude-opus-5-5", row.Model);
        Assert.True(row.Premium);
    }

    // PAGE-3, PAGE-10: the live feed of a running worker names its model and endpoint, and its time.
    [Fact]
    public void Live_RunningWorker_HasModelAndEndpoint()
    {
        using var harness = new PageActionHarness(project: "ptk-450b-live");
        var worker = harness.Running(1);
        harness.Tick.Adopt(worker with { Model = "claude-opus-5-5", Endpoint = "http://gpu:8080" });
        harness.Clock.Advance(TimeSpan.FromMinutes(7));

        var live = Assert.Single(HostPageSnapshot.Live(harness.Config, harness.Clock, harness.Tick));

        Assert.Equal(1, live.Slot);
        Assert.Equal(worker.Log, live.LogPath);
        Assert.Contains("claude-opus-5-5", live.Stats);
        Assert.Contains("http://gpu:8080", live.Stats);
        Assert.StartsWith("7/", live.Time);
    }

    // 1097-B (PAGE-10): the snapshot fills a running slot's stats line from the same WorkerStats the
    // live view uses, so a freshly drawn page shows the worker's numbers at once instead of "- T/s"
    // until /live.json answers. An idle slot still has none.
    [Fact]
    public void Snapshot_RunningWorker_SlotRowCarriesTheLiveStatsLine()
    {
        using var harness = new PageActionHarness(project: "ptk-1097-stats");
        var worker = harness.Running(1);
        harness.Tick.Adopt(worker with { Model = "claude-opus-5-5", Endpoint = "http://gpu:8080" });
        var stats = new WorkerStats
        {
            Calls = 7,
            Tools = 5,
            ToolFails = 1,
            CtxChars = 4000,
            Compactions = 2,
            GenTokens = 500,
            GenSeconds = 40,
            LastTps = 12.5,
            LastTtft = 0.5,
        };
        Func<int, WorkerStats?> bySlot = slot => slot == 1 ? stats : null;
        var sources = new PageSnapshotSources { Stats = bySlot };

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, sources);
        var row = Assert.Single(snapshot.Slots, s => s.Slot == 1);
        var live = Assert.Single(HostPageSnapshot.Live(harness.Config, harness.Clock, harness.Tick, bySlot));

        Assert.Equal(live.Stats, row.Stats);
        Assert.Contains("12.5 T/s now", row.Stats);
        Assert.Contains("claude-opus-5-5", row.Stats);
        Assert.Equal("", Assert.Single(snapshot.Slots, s => s.Slot == 2).Stats);
    }

    // PAGE-5 end to end through the real PageRenderer and server: no Accept for a redefined category
    // (560-A), a Queue row per card.
    [Fact]
    public async Task Page_RedefinedCategoryAndQueue_Rendered()
    {
        using var harness = RedefinedCategoryHarness();
        harness.Host.Cards.Add(Card(CardFolder.Queue, 20, "B"));
        harness.Host.Cards.Add(Card(CardFolder.Queue, 30, "B"));
        harness.Tick_Once();

        var page = await harness.Client.GetStringAsync("/");

        Assert.DoesNotContain("/accept-lane", page);
        foreach (var name in new[] { "010-A-x", "020-B-x", "030-B-x" })
        {
            Assert.Contains(name, page);
        }

        Assert.Contains(">3 cards<", page);
    }

    private static PageActionHarness RedefinedCategoryHarness()
    {
        var harness = new PageActionHarness(categoriesJson: CategoriesV1, project: "ptk-450b-held");
        harness.Tick.State = SchedulerState.Paused;
        harness.Tick_Once();
        harness.Host.Cards.Add(Card(CardFolder.Queue, 10, "A"));
        harness.Host.CategoriesJson = CategoriesV2;
        harness.Tick_Once();
        Assert.Empty(harness.Tick.Categories.Held);
        return harness;
    }

    private static Card Card(CardFolder folder, int num, string category) => new()
    {
        Folder = folder,
        Path = $"docs/jobs/{folder.ToString().ToLowerInvariant()}/{num:000}-{category}-x.md",
        Name = $"{num:000}-{category}-x",
        Num = num,
        Category = category,
        Id = $"{num:000}-{category}",
        Title = $"card {num}",
        Status = "ready",
    };

    private sealed class GateRunner : IHealthRunner
    {
        public string Sha { get; set; } = "";

        public int Exit { get; set; }

        public string? MainShortSha() => Sha;

        public string? Start(string sha, string command) => $"health-{sha}.log";

        public bool IsRunning() => false;

        public int? ExitCode() => Exit;

        public void Kill()
        {
        }
    }
}
