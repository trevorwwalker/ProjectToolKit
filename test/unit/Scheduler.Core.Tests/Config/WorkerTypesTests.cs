// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;

namespace Scheduler.Core.Tests.Config;

/// <summary>
/// The worker types of the <c>pool</c> section and the migration that derives them from today's
/// <c>slots</c> and <c>workers</c> (WT-C1 to WT-C12).
/// Test plan: docs/design/tdd/scheduler/test-plans/worker-types.md, "Config and migration (Core)".
/// </summary>
public class WorkerTypesTests
{
    private const string Rel = "docs/jobs/workers.json";
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";

    /// <summary>This project's workers.json as WT-C12 describes it: the test's own copy, never the live jobs/workers.json.</summary>
    private const string ThisProjectsWorkersJson = """
        {
          "about": "The scheduler's workers: how many there are, and each one's name.",
          "slots": 4,
          "workers": {
            "1": { "name": "", "endpoint": "", "model": "", "metricsUrl": "", "lanes": "ABC", "premium": false },
            "2": { "name": "", "endpoint": "", "model": "", "metricsUrl": "", "lanes": "ABC", "premium": false },
            "3": { "name": "Sonnet", "agent": "claude", "endpoint": "", "model": "claude-sonnet-5-5", "metricsUrl": "", "lanes": "", "premium": true },
            "4": { "name": "Opus", "agent": "claude", "endpoint": "", "model": "claude-opus-5-5", "metricsUrl": "", "lanes": "", "premium": true }
          }
        }
        """;

    private sealed class FakeLog : ISchedulerLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string line) => Lines.Add(line);
    }

    private static SchedulerConfig Load(string json) => SchedulerConfigLoader.Load(json, Repo, LocalAppData);

    /// <summary>The pool a workers.json text gives: its <c>pool</c> when it has one, the migration otherwise (WT-C10).</summary>
    private static WorkerPool PoolOf(string text, SchedulerConfig cfg, IReadOnlyDictionary<int, bool>? enabled = null) =>
        WorkerTypesJson.HasPool(text)
            ? WorkerTypesJson.Parse(text, cfg, new FakeLog()) ?? throw new InvalidOperationException("no pool")
            : WorkerTypesJson.FromWorkersJson(cfg, text, enabled);

    /// <summary>Model-Short as a caller with a models.json would give it: "" for a model nothing is known about.</summary>
    private static Func<string, string, string> ShortFor(params (string Model, string Short)[] known)
    {
        var map = new Dictionary<string, string>(Text.Comparer);
        foreach (var (model, sh) in known)
        {
            map[model] = sh;
        }

        return (string model, string _) => map.GetValueOrDefault(model, "");
    }

    // WT-C1, WT-C3, WT-C5: every field is read, the types keep the file's order, the defaults are the rule's.
    [Fact]
    public void WorkerTypes_PoolSection_ParsedInOrder_DefaultsApplied()
    {
        var cfg = Load("""{"slots":3,"worker":{"model":"cm"}}""");
        const string text = """
            {
              "slots": 9,
              "workers": { "1": { "name": "old" } },
              "pool": {
                "maxWorkers": 4,
                "types": {
                  "spark":  { "max": 2, "agent": "", "model": "spark", "endpoint": "http://spark.local:18400/v1", "metricsUrl": "http://spark.local:18400/metrics", "areas": "abc" },
                  "sonnet": { "max": 1, "agent": "claude", "model": "claude-sonnet-5-5", "premium": true },
                  "opus":   { "max": 1, "agent": "claude", "model": "claude-opus-5-5", "premium": true }
                }
              }
            }
            """;

        var pool = WorkerTypesJson.Parse(text, cfg, new FakeLog());
        Assert.NotNull(pool);
        Assert.False(pool.Migrated);
        Assert.Equal(4, pool.MaxWorkers);
        Assert.Equal(["spark", "sonnet", "opus"], [.. pool.Types.Select(t => t.Name)]); // WT-C5: the file's order

        var spark = pool.Types[0];
        Assert.Equal(2, spark.Max);
        Assert.Equal("", spark.Agent);
        Assert.Equal("spark", spark.Model);
        Assert.Equal("http://spark.local:18400/v1", spark.Endpoint);
        Assert.Equal("http://spark.local:18400/metrics", spark.MetricsUrl);
        Assert.Equal("spark", pool.DefaultType); // 1146-A: no defaultWorker -> the first type premium left off
        Assert.Empty(spark.Projects);
        Assert.Empty(spark.Slots);

        var sonnet = pool.Types[1];
        Assert.Equal("claude", sonnet.Agent);
        Assert.Equal("claude-sonnet-5-5", sonnet.Model);
        Assert.False(pool.IsDefault("sonnet"));

        // WT-C3: a type with nothing on it takes the total for its cap and takes every area.
        var plain = WorkerTypesJson.Parse("""{"pool":{"types":{"plain":{}}}}""", cfg, new FakeLog());
        Assert.NotNull(plain);
        Assert.Equal(3, plain.MaxWorkers); // WT-C1: no maxWorkers -> scheduler.json's slots
        Assert.Equal(3, plain.Types[0].Max); // WT-C3: no max -> maxWorkers
        Assert.Equal("plain", plain.DefaultType);
        Assert.Empty(plain.Types[0].Slots); // WT-C10.5: only a migrated type has slot numbers

        // WT-C1: a maxWorkers that is 0 or not a number is "invalid" and gives scheduler.json's slots.
        Assert.Equal(3, WorkerTypesJson.Parse("""{"pool":{"maxWorkers":0,"types":{"a":{}}}}""", cfg)!.MaxWorkers);
        Assert.Equal(3, WorkerTypesJson.Parse("""{"pool":{"maxWorkers":"x","types":{"a":{}}}}""", cfg)!.MaxWorkers);
        Assert.Equal(3, WorkerTypesJson.Parse("""{"pool":{"maxWorkers":-2,"types":{"a":{}}}}""", cfg)!.MaxWorkers);

        // WT-C3: an invalid max is "missing", so it gives the total like WT-C1 says for the total.
        Assert.Equal(4, WorkerTypesJson.Parse("""{"pool":{"maxWorkers":4,"types":{"a":{"max":"x"},"b":{"max":-1}}}}""", cfg)!.Types[0].Max);

        // WT-C3: projects is a list of names; one string is a list of one; empty is every project.
        var serving = WorkerTypesJson.Parse("""{"pool":{"types":{"a":{"projects":["P1","P2"]},"b":{"projects":"P1"},"c":{}}}}""", cfg);
        Assert.NotNull(serving);
        Assert.Equal(["P1", "P2"], [.. serving.Types[0].Projects]);
        Assert.Equal(["P1"], [.. serving.Types[1].Projects]);
        Assert.Empty(serving.Types[2].Projects);

        // WT-C1: -Slots pins the total for one run whatever the file says (CFG-8).
        var pinned = SchedulerConfigLoader.Load("""{"slots":3}""", Repo, LocalAppData, new CommandLineOptions { Slots = 5 });
        Assert.Equal(5, WorkerTypesJson.Parse(text, pinned)!.MaxWorkers);
        Assert.Equal(5, WorkerTypesJson.Parse("""{"pool":{"maxWorkers":4,"types":{"a":{}}}}""", pinned)!.MaxWorkers);
    }

    // WT-C2: a name that is only digits is refused, and names compare case-insensitively. 1146-A:
    // `premium` is an ordinary name now.
    [Fact]
    public void WorkerTypes_NameDigits_Refused_PremiumIsAName()
    {
        var log = new FakeLog();
        var cfg = Load("{}");
        const string text = """
            {"pool":{"maxWorkers":9,"types":{
              "premium": {"max": 1}, "Premium": {"max": 1}, "12": {"max": 1}, "Spark": {"max": 1}, "SPARK": {"max": 1}}}}
            """;

        var pool = WorkerTypesJson.Parse(text, cfg, log);
        Assert.NotNull(pool);
        Assert.Equal(["premium", "Spark"], [.. pool.Types.Select(t => t.Name)]);
        Assert.DoesNotContain(pool.Types, t => Text.Same(t.Name, "12"));

        // Each refusal is logged: one by WT-C2 and two for the case-insensitive duplicates.
        Assert.Equal(3, log.Lines.Count);
        Assert.Contains(log.Lines, l => l.Contains("Premium") && l.Contains("duplicate"));
        Assert.Contains(log.Lines, l => l.Contains("12") && l.Contains("refused"));
        Assert.Contains(log.Lines, l => l.Contains("SPARK") && l.Contains("duplicate"));
    }

    // 1486-A: a worker's categories are read from "categories", else "lanes"; "categories" wins. The same
    // settings (the categories included) are one type, so three spellings of "ab" merge into one, "cd" does not.
    [Fact]
    public void WorkerTypes_EitherCategoriesKey()
    {
        var cfg = Load("""{"slots":2,"agents":{"ag":{"cli":"ag","args":["-p","{prompt}"]}}}""");
        const string text = """
            {"slots":4,"workers":{
              "1": {"agent":"ag","model":"m","categories":"ab"},
              "2": {"agent":"ag","model":"m","lanes":"AB"},
              "3": {"agent":"ag","model":"m","lanes":"cd","categories":"ab"},
              "4": {"agent":"ag","model":"m","categories":"cd"}}}
            """;

        var pool = WorkerTypesJson.FromWorkersJson(cfg, text, new Dictionary<int, bool>());
        Assert.Equal(2, pool.Types.Count);
        Assert.Equal(3, pool.Types.Max(t => t.Max));
    }

    // WT-C6: broken JSON, or a pool with no valid type, is logged and the previous settings are kept.
    [Fact]
    public void WorkerTypes_InvalidJson_PreviousKept()
    {
        var log = new FakeLog();
        var settings = new WorkerSettings(log, Load("""{"slots":3}"""), Rel);
        settings.Apply("""{"slots":4,"workers":{"1":{"name":"one","lanes":"ab"}}}""", []);
        var beforeMax = settings.Pool.MaxWorkers;
        var beforeTypes = Describe(settings.Pool.Types);
        Assert.True(settings.Pool.Migrated);
        Assert.Equal(4, beforeMax);

        // Broken JSON: logged, nothing changed.
        Assert.False(settings.Apply("{ nope", []));
        Assert.Equal(beforeMax, settings.Pool.MaxWorkers);
        Assert.Equal(beforeTypes, Describe(settings.Pool.Types));
        Assert.Contains(log.Lines, l => l.Contains("not valid JSON"));

        // A pool with no valid type: logged, the previous types kept.
        Assert.False(settings.Apply("""{"pool":{"maxWorkers":2,"types":{}}}""", []));
        Assert.Equal(beforeTypes, Describe(settings.Pool.Types));
        Assert.Contains(log.Lines, l => l.Contains("no valid worker type"));
        Assert.False(settings.Apply("""{"pool":{"maxWorkers":2,"types":{"12":{},"7":{}}}}""", []));
        Assert.Equal(beforeTypes, Describe(settings.Pool.Types));

        // WT-C6: caps may add up to more than the total: that is the point of them sharing it.
        Assert.True(settings.Apply("""{"pool":{"maxWorkers":1,"types":{"a":{"max":3},"b":{"max":2}}}}""", []));
        Assert.Equal(1, settings.Pool.MaxWorkers);
        Assert.Equal(2, settings.Pool.Types.Count);
        Assert.Equal(3, settings.Pool.Types[0].Max);

        // The last good text is still the one in force: sending it again is not a change.
        Assert.False(settings.Apply("""{"pool":{"maxWorkers":1,"types":{"a":{"max":3},"b":{"max":2}}}}""", []));
    }

    // WT-C10: workers with the same settings are one type whose cap is how many of them are enabled.
    [Fact]
    public void WorkerTypes_Migration_SameSettingsMerge_CapIsEnabledCount()
    {
        var cfg = Load("""{"slots":2,"agents":{"ag":{"cli":"ag","args":["-p","{prompt}"]}}}""");
        const string text = """
            {"slots":4,"workers":{
              "1": {"agent":"ag","model":"m1","lanes":"ab"},
              "2": {"agent":"ag","model":"m1","lanes":"AB"},
              "3": {"agent":"ag","model":"m2","lanes":"ab"}}}
            """;

        var pool = WorkerTypesJson.FromWorkersJson(cfg, text, new Dictionary<int, bool> { [2] = false });
        Assert.True(pool.Migrated);
        Assert.Equal(4, pool.MaxWorkers); // WT-C10.1: the total is the file's slots
        Assert.Equal(3, pool.Types.Count); // categories ab and AB are one setting; slot 4 has no entry of its own (WT-C10.2)

        var merged = pool.Types[0];
        Assert.Equal(1, merged.Max); // WT-C10.3: only slot 1 of the two is enabled
        Assert.Equal([1, 2], [.. merged.Slots]); // WT-C10.5: the type keeps the slots it was made from
        Assert.Equal("m1", merged.Model);

        var other = pool.Types[1];
        Assert.Equal(1, other.Max);
        Assert.Equal([3], [.. other.Slots]);

        // WT-C10.2: a slot with no entry of its own is still one worker, the worker section's.
        var bare = pool.Types[2];
        Assert.Equal([4], [.. bare.Slots]);
        Assert.Equal(1, bare.Max);
        Assert.Equal("", bare.Model);

        // Empty slots are the worker section, so slots 1..4 with no workers at all is one type.
        var plain = WorkerTypesJson.FromWorkersJson(cfg, """{"slots":4}""");
        Assert.Single(plain.Types);
        Assert.Equal(4, plain.Types[0].Max);
        Assert.Equal([1, 2, 3, 4], [.. plain.Types[0].Slots]);

        // WT-C10.3: all of a type's slots disabled is cap 0.
        var off = WorkerTypesJson.FromWorkersJson(cfg, text, new Dictionary<int, bool> { [1] = false, [2] = false });
        Assert.Equal(0, off.Types[0].Max);
        Assert.Equal(1, off.Types[1].Max);

        // Slots above the count are not workers; the pool makes nothing for them.
        var extra = WorkerTypesJson.FromWorkersJson(cfg, """{"slots":1,"workers":{"1":{"model":"m1"},"2":{"model":"m2"}}}""");
        Assert.Single(extra.Types);
        Assert.Equal([1], [.. extra.Types[0].Slots]);

        // WT-C10: -Slots is the total, and the migration is in memory only: no file is written.
        var pinned = SchedulerConfigLoader.Load("""{"slots":6}""", Repo, LocalAppData, new CommandLineOptions { Slots = 3 });
        Assert.Equal(3, WorkerTypesJson.FromWorkersJson(pinned, text).MaxWorkers);
    }

    // WT-C10, WT-C12: this project's workers.json (the test's own copy of it) migrates to worker,
    // Sonnet and Opus.
    [Fact]
    public void WorkerTypes_Migration_ThisProjectsWorkersJson_GivesWorkerSonnetOpus()
    {
        var pool = PoolOf(ThisProjectsWorkersJson, Load("""{"slots":4}"""));

        Assert.Equal(4, pool.MaxWorkers);
        Assert.Equal(["worker", "Sonnet", "Opus"], [.. pool.Types.Select(t => t.Name)]);

        var worker = pool.Types[0];
        Assert.Equal("worker", pool.DefaultType); // 1146-A: the slot the old premium flag left off
        Assert.Equal(2, worker.Max);
        Assert.Equal("", worker.Agent); // WT-C3: empty is the worker section

        var sonnet = pool.Types[1];
        Assert.Equal(1, sonnet.Max);
        Assert.Equal("claude", sonnet.Agent);
        Assert.Equal("claude-sonnet-5-5", sonnet.Model);

        var opus = pool.Types[2];
        Assert.Equal(1, opus.Max);
        Assert.Equal("claude", opus.Agent);
        Assert.Equal("claude-opus-5-5", opus.Model);

        // WT-C12: `Worker: Opus` on a card names the Opus type, as it named slot 4 by its name today.
        Assert.Single(pool.Types, t => Text.Same(t.Name, "Opus"));

        if (pool.Migrated)
        {
            Assert.Equal([1, 2], [.. worker.Slots]);
            Assert.Equal([3], [.. sonnet.Slots]);
            Assert.Equal([4], [.. opus.Slots]);
        }
    }

    // WT-C10.4: a name comes from the member, or Model-Short of the effective model, or `worker`;
    // a taken name is numbered and a refused name is prefixed.
    [Fact]
    public void WorkerTypes_Migration_NamesFromModelShort_ClashesNumbered()
    {
        var cfg = Load("{}");
        const string text = """
            {"slots":6,"workers":{
              "1": {"name":"Named","model":"a"},
              "2": {"model":"b"},
              "3": {},
              "4": {"model":"sonnet-a"},
              "5": {"model":"sonnet-b"},
              "6": {"name":"premium","model":"c"}}}
            """;

        var pool = WorkerTypesJson.FromWorkersJson(cfg, text, null, ShortFor(("b", "Beta"), ("sonnet-a", "Sonnet"), ("sonnet-b", "Sonnet")));
        Assert.Equal(["Named", "Beta", "worker", "Sonnet", "Sonnet-2", "premium"], [.. pool.Types.Select(t => t.Name)]);

        // A model nothing knows the short of is no name either: the type is `worker`, and two of
        // those are `worker` and `worker-2`.
        var unknown = WorkerTypesJson.FromWorkersJson(cfg, """{"slots":2,"workers":{"1":{"model":"x"},"2":{"model":"y"}}}""");
        Assert.Equal(["worker", "worker-2"], [.. unknown.Types.Select(t => t.Name)]);

        // The name comes from the first member, whatever the later members of the type are called.
        var first = WorkerTypesJson.FromWorkersJson(cfg, """{"slots":2,"workers":{"1":{"name":"First"},"2":{"name":"Second"}}}""");
        Assert.Single(first.Types);
        Assert.Equal("First", first.Types[0].Name);

        // A numeric effective model is refused as a name and gets `worker-` in front (WT-C10.4, WT-C2).
        var digits = WorkerTypesJson.FromWorkersJson(cfg, """{"slots":1,"workers":{"1":{"name":"7"}}}""");
        Assert.Equal("worker-7", digits.Types[0].Name);
    }

    // WT-C10: a workers.json with a `pool` uses it; `slots` and `workers` beside it are ignored, logged once.
    [Fact]
    public void WorkerTypes_PoolKeyPresent_SlotsAndWorkersIgnored()
    {
        const string text = """
            {"about":"old","slots":9,"workers":{"1":{"name":"ghost"},"2":{"name":"ghost2"}},
             "pool":{"maxWorkers":2,"types":{"only":{"max":2,"model":"m"}}}}
            """;
        Assert.True(WorkerTypesJson.HasPool(text));

        var log = new FakeLog();
        var settings = new WorkerSettings(log, Load("""{"slots":3}"""), Rel);
        settings.Apply(text, []);

        Assert.Equal(2, settings.Pool.MaxWorkers); // not the file's slots: 9
        Assert.Single(settings.Pool.Types);
        Assert.Equal("only", settings.Pool.Types[0].Name);
        Assert.False(settings.Pool.Migrated);
        Assert.Equal(2, settings.Slots); // today's slot count follows the total
        Assert.Empty(settings.Workers); // the workers section is ignored, ghost and ghost2 included

        // Logged once, not every tick: the same text applied again says nothing more.
        Assert.Single(log.Lines, l => l.Contains("ignored"));
        settings.Apply(text, []);
        Assert.Single(log.Lines, l => l.Contains("ignored"));

        // The config's worker count comes from the pool too, and not from the file's slots.
        Assert.Equal(2, SchedulerConfigLoader.ApplyWorkersJson(Load("""{"slots":7}"""), text).MaxWorkers);
        Assert.Equal(2, SchedulerConfigLoader.ApplyWorkersJson(Load("""{"slots":7}"""), text).Slots);

        // A pool whose text is broken leaves the previous types in place (WT-C6).
        Assert.False(settings.Apply("""{"pool":{"types":{1:1}}}""", []));
        Assert.Equal("only", settings.Pool.Types[0].Name);
    }

    // WT-C11: the first Save writes the pool form, drops slots and workers, and updates the about text.
    [Fact]
    public void WorkerTypes_Save_WritesPoolForm_DropsSlots()
    {
        var cfg = Load("""{"slots":3,"worker":{"model":"cm"}}""");
        const string old = """
            {"about":"The scheduler's workers: how many there are.","slots":3,
             "workers":{
               "1":{"name":"worker","agent":"","endpoint":"http://s:1/v1","model":"spark","metricsUrl":"http://s:1/metrics","lanes":"ab","premium":false},
               "2":{"name":"worker","agent":"","endpoint":"http://s:1/v1","model":"spark","metricsUrl":"http://s:1/metrics","lanes":"AB","premium":false},
               "3":{"name":"Opus","agent":"claude","endpoint":"","model":"opus-5","metricsUrl":"","lanes":"","premium":true}}}
            """;

        // Save from the panel with one type changed: Opus is off.
        var migrated = WorkerTypesJson.FromWorkersJson(cfg, old, null, ShortFor(("cm", "Spark")));
        Assert.Equal(["worker", "Opus"], [.. migrated.Types.Select(t => t.Name)]);
        var saved = migrated with
        {
            Types = [.. migrated.Types.Select(t => Text.Same(t.Name, "Opus") ? t with { Max = 0 } : t)],
        };

        var text = WorkerTypesJson.BuildPool(saved);

        Assert.Contains("\"pool\"", text);
        Assert.Contains("\"maxWorkers\": 3", text);
        Assert.DoesNotContain("\"slots\"", text); // WT-C11: slots and workers are gone
        Assert.DoesNotContain("\"workers\"", text);
        Assert.DoesNotContain("The scheduler's workers: how many there are.", text); // the about text is updated
        Assert.Contains("\"about\"", text);
        Assert.Contains("\"types\"", text);

        // Every field of a type is written, and a cap of 0 is written as 0 (WT-C3).
        Assert.Contains("\"worker\":", text);
        Assert.Contains("\"max\": 2", text);
        Assert.Contains("\"max\": 0", text);
        Assert.Contains("\"agent\": \"claude\"", text);
        Assert.Contains("\"model\": \"opus-5\"", text);
        Assert.Contains("\"endpoint\": \"http://s:1/v1\"", text);
        Assert.Contains("\"metricsUrl\": \"http://s:1/metrics\"", text);
        Assert.DoesNotContain("\"premium\"", text); // 1146-A: no premium flag; the default type is named instead
        Assert.Contains("\"defaultWorker\": \"worker\"", text);

        // What Save writes is what Parse reads back, field for field (the slot numbers are the
        // migration's own and are not written).
        var againLog = new FakeLog();
        var again = WorkerTypesJson.Parse(text, cfg, againLog);
        Assert.True(again is not null, $"Save wrote text Parse will not read ({string.Join(" | ", againLog.Lines)}):{Environment.NewLine}{text}");
        Assert.False(again.Migrated);
        Assert.Equal(saved.MaxWorkers, again.MaxWorkers);
        Assert.Equal(Describe([.. saved.Types.Select(t => t with { Slots = [] })]), Describe(again.Types));
        Assert.Equal("worker", again.DefaultType);

        // A type that serves named projects keeps them (WT-C3, Q1); a type without them says nothing.
        var withProjects = new WorkerPool
        {
            MaxWorkers = 2,
            Types =
            [
                new WorkerType { Name = "a", Max = 1, Projects = ["P1", "P2"] },
                new WorkerType { Name = "b", Max = 1 },
            ],
        };
        var projectsText = WorkerTypesJson.BuildPool(withProjects);
        Assert.Contains("\"projects\": [\"P1\", \"P2\"]", projectsText);
        Assert.Equal(1, projectsText.Split("\"projects\":", StringSplitOptions.None).Length - 1);
        var readBack = WorkerTypesJson.Parse(projectsText, cfg);
        Assert.NotNull(readBack);
        Assert.Equal(["P1", "P2"], [.. readBack.Types[0].Projects]);
        Assert.Empty(readBack.Types[1].Projects);
    }

    /// <summary>A type as one line, so a record holding lists can be compared as written.</summary>
    private static IReadOnlyList<string> Describe(IReadOnlyList<WorkerType> types) =>
        [.. types.Select(t => $"{t.Name}|{t.Max}|{t.Agent}|{t.Model}|{t.Endpoint}|{t.MetricsUrl}|{string.Join(",", t.Projects)}|{string.Join(",", t.Slots)}")];

    // 969-A: an old areas key is ignored with one log line; the type then takes any category.
    [Fact]
    public void WorkerTypes_OldAreasKey_IgnoredAndLoggedOnce()
    {
        var cfg = Load("""{"slots":3,"worker":{"model":"cm"}}""");
        const string text = """{ "pool": { "maxWorkers": 2, "types": { "a": { "max": 1, "areas": "AB" }, "b": { "max": 1, "areas": "C" } } } }""";
        var log = new FakeLog();

        var pool = WorkerTypesJson.Parse(text, cfg, log);

        Assert.NotNull(pool);
        Assert.Equal(2, pool.Types.Count);
        Assert.Single(log.Lines, l => l.Contains("areas", StringComparison.Ordinal));
        Assert.DoesNotContain("areas", WorkerTypesJson.BuildPool(pool), StringComparison.Ordinal);
    }
}
