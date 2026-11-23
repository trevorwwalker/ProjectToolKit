using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;
using Scheduler.Core.WebContract;
using Scheduler.Host.Tests.Tick;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// Where the Worker types panel's types come from at start-up (974-B). Once main has a pool-form
/// <c>workers.json</c> it is the only source of the types - names, caps, agents and models as saved -
/// so a restart shows <c>Spark</c> rather than the <c>worker</c> that WT-C10.4 gives a type with no
/// name; the slot migration is what a file with no <c>pool</c> still gets (WT-C10). Until main has
/// been read the panel shows nothing rather than migrated types, and its Save is refused (WT-C7).
/// Test plan: docs/design/tdd/scheduler/worker-types.md.
/// </summary>
[ParallelMethods]
public class WorkerTypesFromMainTests
{
    /// <summary>The developer's file on main (2026-11-19): three types, the first named Spark.</summary>
    private const string PoolSpark = """
        {
          "about": "the types",
          "pool": {
            "maxWorkers": 4,
            "types": {
              "Spark": { "max": 4, "agent": "", "model": "", "endpoint": "", "metricsUrl": "", "premium": false, "areas": "ABC" },
              "Sonnet": { "max": 4, "agent": "claude", "model": "claude-sonnet-5-5", "endpoint": "", "metricsUrl": "", "premium": true, "areas": "" },
              "Opus": { "max": 4, "agent": "claude", "model": "claude-opus-5-5", "endpoint": "", "metricsUrl": "", "premium": true, "areas": "" }
            }
          }
        }
        """;

    /// <summary>The same file before it was saved from the panel: four slots, none of them named (WT-C10).</summary>
    private const string SlotForm = """
        {
          "slots": 4,
          "workers": {
            "1": { "name": "", "agent": "", "model": "", "endpoint": "", "metricsUrl": "", "lanes": "ABC", "premium": false },
            "2": { "name": "", "agent": "", "model": "", "endpoint": "", "metricsUrl": "", "lanes": "ABC", "premium": false },
            "3": { "name": "Sonnet", "agent": "claude", "model": "claude-sonnet-5-5", "endpoint": "", "metricsUrl": "", "lanes": "", "premium": true },
            "4": { "name": "Opus", "agent": "claude", "model": "claude-opus-5-5", "endpoint": "", "metricsUrl": "", "lanes": "", "premium": true }
          }
        }
        """;

    // The flags the slots left behind (WRK-3), and 1.6's log-folder workers.json: neither is a type,
    // and with a pool on main neither may reach the page.
    private static Dictionary<string, string> SlotDayState => new()
    {
        ["workers-state.json"] = """{"enabled":{"1":true,"2":false,"3":false,"4":true}}""",
        ["workers.json"] = """
            {"slots":4,"workers":{
              "1":{"name":"","enabled":true},
              "2":{"name":"","enabled":false},
              "3":{"name":"Sonnet","enabled":false},
              "4":{"name":"Opus","enabled":true}}}
            """,
    };

    // WT-C10, WT-C7 (974-B): the program composed, the page rendered, and no tick run yet - and the
    // Worker types panel already says what main's pool says. The "worker" of WT-C10.4 was what the
    // panel showed here, so every restart reset the names the developer had saved.
    [Fact]
    public void StartUp_PoolFormOnMain_PanelShowsTheSavedNames_BeforeTheFirstTick()
    {
        using var rig = new TickRig("echo started\r\n", workersJson: PoolSpark, logFiles: SlotDayState, slots: 4, slotsPinned: false);

        var pool = rig.Composition.Tick.Workers.Pool;
        Assert.False(pool.Migrated);
        Assert.Equal(["Spark", "Sonnet", "Opus"], [.. pool.Types.Select(t => t.Name)]);
        Assert.Equal([4, 4, 4], [.. pool.Types.Select(t => t.Max)]);
        Assert.False(pool.Types[0].Premium);
        Assert.Equal("claude", pool.Types[1].Agent);

        // The page itself, not just the tick: this is the snapshot the first render is built from.
        Assert.Equal(["Spark", "Sonnet", "Opus"], [.. rig.Composition.Data.Snapshot().WorkerTypes.Select(t => t.Name)]);
    }

    // WT-W5, WT-P1 (974-B): an adopted worker whose entry names no type is not given the migrated
    // type of its folder, because at start-up the pool the adopter is handed is main's - whose types
    // belong to no folder at all. The worker window says what it is rather than "worker".
    [Fact]
    public void StartUp_PoolFormOnMain_AdopterSeesMainsPool_WithNoSlotsToNameAWorker()
    {
        using var rig = new TickRig("echo started\r\n", workersJson: PoolSpark, logFiles: SlotDayState, slots: 4, slotsPinned: false);

        var pool = rig.Composition.Tick.Workers.Pool;
        Assert.All(pool.Types, t => Assert.Empty(t.Slots));
        Assert.Equal("", Adoption.TypeOfSlot(pool, 1));
    }

    // WT-5, WT-C5 (974-B): the type a card that names Spark gets, asked before the first tick, is
    // Spark. A card naming a saved type must start as it right after a restart, not as "worker".
    [Fact]
    public void StartUp_PoolFormOnMain_CardNamingSparkIsTypedSpark_BeforeTheFirstTick()
    {
        using var rig = new TickRig("echo started\r\n", workersJson: PoolSpark, logFiles: SlotDayState, slots: 4, slotsPinned: false);
        var card = new Card
        {
            Folder = CardFolder.Queue,
            Path = "jobs/queue/974-B-x.md",
            Name = "974-B-x",
            Num = 974,
            Category = "B",
            Id = "974-B",
            WorkerReq = "Spark",
        };

        Assert.Equal("Spark", rig.Composition.Host.TypeFor(1, card));
    }

    // WT-C10 (974-B): a file with no pool still migrates, and the panel still shows the migrated
    // names - the fix takes away nothing from a project whose workers.json is in the old form.
    [Fact]
    public void StartUp_SlotFormOnMain_StillMigratesTheTypesOfTheSlots()
    {
        using var rig = new TickRig("echo started\r\n", workersJson: SlotForm, slots: 4, slotsPinned: false);

        var pool = rig.Composition.Tick.Workers.Pool;
        Assert.True(pool.Migrated);
        Assert.Equal(4, pool.MaxWorkers);
        Assert.Equal(["worker", "Sonnet", "Opus"], [.. pool.Types.Select(t => t.Name)]);
    }

    // WT-C7 (974-B): before main has been read there is nothing to show and nothing to save - the
    // panel shows no types rather than the migrated ones, so a Save cannot write them into main.
    [Fact]
    public async Task Page_BeforeMainIsRead_ShowsNoTypes_AndRefusesSave()
    {
        using var harness = new PageActionHarness(workersJson: PoolSpark, project: "ptk-974b-unread", workersFromMain: false);
        harness.Tick_Once(); // the types are there; it is that they have not come from main that matters

        Assert.Empty(harness.Data.Snapshot().WorkerTypes);
        var page = await harness.Client.GetStringAsync("/");
        Assert.Contains("Worker types", page, StringComparison.Ordinal);
        Assert.DoesNotContain("value='Spark'", page, StringComparison.Ordinal);

        var saved = harness.Actions.SaveWorkerTypes(4, [new PageWorkerTypeView { Name = "Spark", Max = 4 }]);
        Assert.False(saved.Ok);
        Assert.Equal("worker types not saved: the scheduler has not read workers.json from main yet", saved.Error);
        Assert.Equal(0, harness.Successes);
    }
}

