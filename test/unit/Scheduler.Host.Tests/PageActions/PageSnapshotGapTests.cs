using System.Globalization;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Host.PageActions;
using Scheduler.Host.State;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// 510-B: the facts the page lacked after 480-C (its "Host gap" list), each filled from the tick or
/// from what Host keeps beside it: the time limit and kill time of a running worker, the model's
/// short name, the card's title and category name, planner push, the queue's depends and logs,
/// the events as fields, the waiting cards' titles and where their reasons came from.
/// </summary>
public class PageSnapshotGapTests
{
    private const string Categories = """
        {"lanes":{"A":{"name":"core","purpose":"logic","owns":["src/a/"],"except":[]},
                   "B":{"name":"host","purpose":"machine","owns":["src/b/"],"except":[]}}}
        """;

    private const string Workers = """
        {"slots":2,"workers":{"1":{"name":"w1","model":"claude-opus-5-5","premium":true},"2":{"name":"w2","model":"qwen"}}}
        """;

    // PAGE-5, TIME-9, 510-B: a running worker's window knows its card's title, its category's name, its
    // time limit, and, once stopping, when it is killed (the stop time plus the grace minutes).
    [Fact]
    public void Slot_Running_HasTimeLimitKillTimeTitleAndCategoryName()
    {
        using var harness = Paused(new PageActionHarness(categoriesJson: Categories, workersJson: Workers, project: "ptk-510b-slot"));
        harness.Host.Cards.Add(Card(CardFolder.Active, 10, "A") with { Title = "Make the thing" });
        harness.Tick_Once();
        var stopAt = harness.Clock.Now.AddMinutes(3);
        harness.Tick.Adopt(harness.Running(1) with { TimeBox = 45, StopAt = stopAt, StopGraceMinutes = 5, StopWhy = "asked" });

        var slot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Slots[0];

        Assert.Equal(45, slot.TimeLimit);
        Assert.Equal(stopAt.AddMinutes(5), slot.KillAt);
        Assert.Equal("Make the thing", slot.CardTitle);
        Assert.Equal("core", slot.CategoryName);
    }

    // 510-B: an idle worker has no limit, no kill time, no card title and no category name.
    [Fact]
    public void Slot_Idle_HasNoRunFacts()
    {
        using var harness = Paused(new PageActionHarness(categoriesJson: Categories, workersJson: Workers, project: "ptk-510b-idle"));

        var slot = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Slots[1];

        Assert.Null(slot.TimeLimit);
        Assert.Null(slot.KillAt);
        Assert.Equal("", slot.CardTitle);
        Assert.Equal("", slot.CategoryName);
    }

    // DISP-18, 510-B: the worker title's extras: the model's short name from models.json.
    [Fact]
    public void Slot_ModelShortName()
    {
        using var harness = Paused(new PageActionHarness(categoriesJson: Categories, workersJson: Workers, project: "ptk-510b-model"));
        var models = new Dictionary<string, IReadOnlyList<string>> { ["claude-opus-5-5"] = ["Opus"] };
        var sources = new PageSnapshotSources { Models = () => models };

        var slots = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, sources).Slots;

        Assert.Equal("Opus", slots[0].ModelShort);
        Assert.Equal("", slots[1].ModelShort);
    }

    // PAGE-3, TIME-9, 510-B: the live time is "minutes run/time limit min", as status.json writes it.
    [Fact]
    public void Live_Time_MinutesOfLimit()
    {
        using var harness = new PageActionHarness(project: "ptk-510b-live");
        // Its log grew a minute ago, so it is not quiet.
        harness.Tick.Adopt(harness.Running(1) with { TimeBox = 45, LastProgress = harness.Clock.Now.AddMinutes(6) });
        harness.Clock.Advance(TimeSpan.FromMinutes(7));

        var live = Assert.Single(HostPageSnapshot.Live(harness.Config, harness.Clock, harness.Tick));

        Assert.Equal("7/45 min", live.Time);
    }

    // MP-9, 510-B: planner push as a flag: on, off, or null with no planner outbox.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlannerPush_IsAFlag(bool on)
    {
        using var harness = new PageActionHarness(project: "ptk-510b-push");

        Assert.Equal(on, HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, new PageSnapshotSources { PlannerPush = () => on }).PlannerPush);
        Assert.Null(HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).PlannerPush);
    }

    // PAGE-5, PAGE-6, 510-B: a queue row carries its Depends on list and the log of its newest run.
    [Fact]
    public void Queue_DependsAndNewestLog()
    {
        using var harness = Paused(new PageActionHarness(categoriesJson: Categories, project: "ptk-510b-queue"));
        harness.Host.Cards.Add(Card(CardFolder.Queue, 10, "A"));
        harness.Host.Cards.Add(Card(CardFolder.Queue, 20, "B") with { Depends = ["010-A"] });
        harness.Tick_Once();
        IReadOnlyList<RecentEvent> recent =
        [
            Event(20, "B", "stopped", "new.log"),
            Event(20, "B", "killed", "old.log"),
        ];

        var queue = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, new PageSnapshotSources { Recent = () => recent }).Queue;

        var row = Assert.Single(queue, q => q.Name == "020-B-x");
        Assert.Equal(["010-A"], row.Depends);
        Assert.Equal("new.log", row.Log);
        var first = Assert.Single(queue, q => q.Name == "010-A-x");
        Assert.Empty(first.Depends);
        Assert.Equal("", first.Log);
    }

    // PAGE-5, ACCT-5, 510-B: a Recent event as fields: worker, card, result, minutes and log.
    [Fact]
    public void Events_AsFields()
    {
        using var harness = new PageActionHarness(project: "ptk-510b-events");
        IReadOnlyList<RecentEvent> recent = [Event(30, "A", "review", "run.log") with { Slot = 2, Minutes = 17 }];

        var e = Assert.Single(HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick, new PageSnapshotSources { Recent = () => recent }).Events);

        Assert.Equal(2, e.Slot);
        Assert.Equal("030-A-x", e.Card);
        Assert.Equal("review", e.Result);
        Assert.Equal(17, e.Minutes);
        Assert.Equal("run.log", e.Log);
    }

    // PAGE-5, CARD-13, 510-B: a Waiting on you row has its card's title, and says where its reason
    // came from (the Result journal here, so the page can mark it).
    [Fact]
    public void Waiting_TitleAndReasonSource()
    {
        using var harness = Paused(new PageActionHarness(categoriesJson: Categories, project: "ptk-510b-waiting"));
        harness.Host.Cards.Add(Card(CardFolder.Review, 40, "A") with { Title = "Check it", Status = "review: look", Why = "look at it", WhyFrom = "result" });
        harness.Tick_Once();

        var row = Assert.Single(HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Waiting, w => w.Name == "040-A-x");

        Assert.Equal("Check it", row.Title);
        Assert.Equal("result", row.NoteFrom);
    }

    // REQ, 510-B: the cards waiting on the planner with their Depends on lists, and which of them
    // have every dependency finished.
    [Fact]
    public void Planner_WaitingDependsAndReady()
    {
        using var harness = Paused(new PageActionHarness(categoriesJson: Categories, project: "ptk-510b-planner"));
        harness.Host.Cards.Add(Card(CardFolder.Done, 10, "A") with { Status = "done" });
        harness.Host.Cards.Add(Card(CardFolder.Queue, 20, "B") with { Status = "waiting: planner", Depends = ["010-A"] });
        harness.Host.Cards.Add(Card(CardFolder.Queue, 30, "B") with { Status = "waiting: planner", Depends = ["099-A"] });
        harness.Tick_Once();

        var planner = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Planner;

        Assert.Equal(["010-A"], planner.WaitingDepends["020-B-x"]);
        Assert.Equal(["099-A"], planner.WaitingDepends["030-B-x"]);
        Assert.Equal(["020-B-x"], planner.WaitingReady);
    }

    // TIME-8, 510-B: a worker whose log has not grown for 5 minutes or more is quiet: its window
    // knows for how long, and its live time says so, which stops the page's spinner.
    [Fact]
    public void Slot_Quiet_FromFiveMinutes()
    {
        using var harness = new PageActionHarness(project: "ptk-510b-quiet");
        harness.Tick.Adopt(harness.Running(1) with { TimeBox = 45, LastProgress = harness.Clock.Now });
        harness.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Null(HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Slots[0].QuietMinutes);
        Assert.Equal("4/45 min", Assert.Single(HostPageSnapshot.Live(harness.Config, harness.Clock, harness.Tick)).Time);

        harness.Clock.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal(7, HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Slots[0].QuietMinutes);
        Assert.Equal("7/45 min, quiet 7 min", Assert.Single(HostPageSnapshot.Live(harness.Config, harness.Clock, harness.Tick)).Time);
    }

    // 510-B: a message sent from the page and not read yet (msg-slotN.txt still there).
    [Fact]
    public void Slot_MessagePending_WhileTheFileIsThere()
    {
        using var harness = new PageActionHarness(project: "ptk-510b-msg");
        harness.Tick.Adopt(harness.Running(1));
        Assert.False(HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Slots[0].MessagePending);

        File.WriteAllText(Path.Combine(harness.Config.LogDir, "msg-slot1.txt"), "hello");

        Assert.True(HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Slots[0].MessagePending);
    }

    // WRK-14, 700-B: the worker window carries the time the hook handed its worker the message: the
    // stamp on msg-slotN.read.txt. None while that file is not there, and none for an idle slot.
    [Fact]
    public void Slot_MessageReadAt_FromTheReadFileStamp()
    {
        using var harness = new PageActionHarness(project: "ptk-700b-read");
        harness.Running(1);
        Assert.Null(HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Slots[0].MessageReadAt);

        var when = new DateTime(2026, 11, 19, 14, 5, 9, DateTimeKind.Local);
        var read = Path.Combine(harness.Config.LogDir, "msg-slot1.read.txt");
        File.WriteAllText(read, "hello");
        File.SetLastWriteTime(read, when);
        File.WriteAllText(Path.Combine(harness.Config.LogDir, "msg-slot2.read.txt"), "hello"); // slot 2 is idle

        var at = HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Slots[0].MessageReadAt;

        Assert.NotNull(at);
        Assert.Equal(when, at.Value.LocalDateTime);
        Assert.Equal("14:05:09", at.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        Assert.Null(HostPageSnapshot.Build(harness.Config, harness.Clock, harness.Tick).Slots[1].MessageReadAt);
    }

    // DISP, 510-B: an idle worker whose model server does not answer says so, and the health line
    // names the server.
    [Fact]
    public void Slot_IdleServerDown_AndNamed()
    {
        using var harness = new PageActionHarness(workersJson: """{"slots":2,"workers":{"1":{"endpoint":"http://gpu:8080/v1"}}}""", project: "ptk-510b-down");
        var tick = new Scheduler.Core.Tick.SchedulerTick(harness.Config, harness.Clock, harness.Log, new Program.TickLoad(), new DownProbe(), harness.Processes, harness.Files, harness.Host)
        {
            State = SchedulerState.Paused,
        };
        tick.Run();
        Assert.False(tick.Servers.IsUp("http://gpu:8080/v1"));

        var snapshot = HostPageSnapshot.Build(harness.Config, harness.Clock, tick);

        Assert.True(snapshot.Slots[0].ServerDown);
        Assert.False(snapshot.Slots[1].ServerDown);
        Assert.Contains("http://gpu:8080/v1", snapshot.Health.DownServers);
    }

    // HLTH-5, 510-B: the health line has a log once a check has written one.
    [Fact]
    public void Health_HasLog_WhenTheFileExists()
    {
        using var harness = new PageActionHarness(project: "ptk-510b-hlog");
        var config = harness.Config with { HealthCommand = "dotnet test" };
        var runner = new LogRunner(Path.Combine(harness.Config.LogDir, "health-abc.log"));
        var health = new Scheduler.Core.Health.HealthCheck(config, harness.Clock, harness.Log, runner);
        var sources = new PageSnapshotSources { Health = health };
        Assert.False(HostPageSnapshot.Build(config, harness.Clock, harness.Tick, sources).Health.HasLog);

        health.CheckNow();
        Assert.True(health.StartIfWanted());
        File.WriteAllText(runner.Path, "ok");

        Assert.True(HostPageSnapshot.Build(config, harness.Clock, harness.Tick, sources).Health.HasLog);
    }

    private sealed class DownProbe : IServerProbe
    {
        public bool IsUp(string endpoint) => false;

        public string? ReadModels(string endpoint) => null;

        public string? ReadMetrics(string metricsUrl) => null;
    }

    private sealed class LogRunner(string path) : Scheduler.Core.Health.IHealthRunner
    {
        public string Path { get; } = path;

        public string? MainShortSha() => "abc";

        public string? Start(string sha, string command) => Path;

        public bool IsRunning() => true;

        public int? ExitCode() => null;

        public void Kill()
        {
        }
    }

    private static PageActionHarness Paused(PageActionHarness harness)
    {
        harness.Tick.State = SchedulerState.Paused;
        harness.Tick_Once();
        return harness;
    }

    private static RecentEvent Event(int num, string category, string result, string log) => new()
    {
        When = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero),
        Slot = 1,
        Card = $"{num:000}-{category}-x",
        Result = result,
        Minutes = 5,
        Log = log,
    };

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
}
