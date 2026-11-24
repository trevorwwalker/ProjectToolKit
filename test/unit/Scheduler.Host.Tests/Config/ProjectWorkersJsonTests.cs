using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Host.Tests.PageActions;

namespace Scheduler.Host.Tests.Config;

/// <summary>
/// A project's <c>workers.json</c> in the pool form (WT-C11, WT-C12), and the Worker types panel as
/// the page shows it from that file (WT-P3). The file and scheduler.json are this test's own copies:
/// no test reads <c>jobs/*.json</c>, which the developer edits from the Worker types panel (940-A).
/// Test plan: docs/design/tdd/scheduler/worker-types.md, card 6 of section 6.
/// </summary>
public class ProjectWorkersJsonTests
{
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";

    /// <summary>scheduler.json as this project has it: four slots, and the worker section the plain type leaves empty (WT-C3).</summary>
    private const string SchedulerJson = """
        {"project":"ProjectToolkit","slots":4,"worker":{"cli":"copilot","model":"spark","endpoint":"http://spark.local:18400/v1","metricsUrl":"http://spark.local:18400/metrics"}}
        """;

    /// <summary>The file as it was before the pool form: four slots, two spark, Sonnet, Opus (WT-C12).</summary>
    private const string OldSlotForm = """
        {
          "slots": 4,
          "workers": {
            "1": { "name": "", "endpoint": "", "model": "", "metricsUrl": "", "lanes": "ABC", "premium": false },
            "2": { "name": "", "endpoint": "", "model": "", "metricsUrl": "", "lanes": "ABC", "premium": false },
            "3": { "name": "Sonnet", "agent": "claude", "endpoint": "", "model": "claude-sonnet-5-5", "metricsUrl": "", "lanes": "", "premium": true },
            "4": { "name": "Opus", "agent": "claude", "endpoint": "", "model": "claude-opus-5-5", "metricsUrl": "", "lanes": "", "premium": true }
          }
        }
        """;

    /// <summary>The same four workers as the pool form, exactly as the panel's Save writes it (WT-C11).</summary>
    private static string PoolForm => $$"""
        {
          "about": "{{WorkerTypesJson.About}}",
          "pool": {
            "maxWorkers": 4,
            "defaultWorker": "worker",
            "types": {
              "worker": { "max": 2, "agent": "", "model": "", "endpoint": "", "metricsUrl": "" },
              "Sonnet": { "max": 1, "agent": "claude", "model": "claude-sonnet-5-5", "endpoint": "", "metricsUrl": "" },
              "Opus": { "max": 1, "agent": "claude", "model": "claude-opus-5-5", "endpoint": "", "metricsUrl": "" }
            }
          }
        }
        """;

    // A workers.json read the way the tick reads it, with scheduler.json in front of it (WT-C10).
    private static (WorkerPool Pool, string Text) PoolFrom(string text)
    {
        var config = SchedulerConfigLoader.Load(SchedulerJson, Repo, LocalAppData);
        var pool = WorkerTypesJson.Parse(text, config, new NullLog())
            ?? throw new InvalidOperationException("the pool-form fixture has no usable pool (WT-C10)");
        return (pool, text);
    }

    private sealed class NullLog : ISchedulerLog
    {
        public void Write(string line)
        {
        }
    }

    // WT-C11: a file in the form the Worker types panel writes, so saving what it loads back writes
    // the same bytes; a pool form keeps no `slots` and no `workers`.
    [Fact]
    public void WorkerTypes_Save_WritesPoolForm_ThisProjectsFileIsWhatSaveWrites()
    {
        var (pool, text) = PoolFrom(PoolForm);

        Assert.False(pool.Migrated);
        Assert.DoesNotContain("\"slots\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"workers\"", text, StringComparison.Ordinal);

        var again = WorkerTypesJson.BuildPool(pool).Replace("\r\n", "\n").TrimEnd('\n');
        Assert.Equal(again, text.Replace("\r\n", "\n").TrimEnd('\n'));
    }

    // WT-C12: the pool says what the four slots said - worker (cap 2), Sonnet and Opus
    // (cap 1 each), worker the default - and the total is still four. Nothing the slots described changed.
    [Fact]
    public void WorkerTypes_Migration_ThisProjectsWorkersJson_GivesWorkerSonnetOpus()
    {
        var (pool, _) = PoolFrom(PoolForm);
        var config = SchedulerConfigLoader.Load(SchedulerJson, Repo, LocalAppData);
        var migrated = WorkerTypesJson.FromWorkersJson(config, OldSlotForm);

        Assert.Equal(migrated.MaxWorkers, pool.MaxWorkers);
        Assert.Equal(4, pool.MaxWorkers);
        Assert.Equal([.. migrated.Types.Select(t => t.Name)], [.. pool.Types.Select(t => t.Name)]);
        Assert.Equal(["worker", "Sonnet", "Opus"], [.. pool.Types.Select(t => t.Name)]);
        Assert.Equal([.. migrated.Types.Select(t => t.Max)], [.. pool.Types.Select(t => t.Max)]);
        Assert.Equal([2, 1, 1], [.. pool.Types.Select(t => t.Max)]);
        Assert.Equal([.. migrated.Types.Select(t => (t.Agent, t.Model, t.Endpoint, t.MetricsUrl))],
            [.. pool.Types.Select(t => (t.Agent, t.Model, t.Endpoint, t.MetricsUrl))]);
        Assert.Equal(migrated.DefaultType, pool.DefaultType); // 1146-A
        var worker = pool.Types[0];
        Assert.Equal("worker", pool.DefaultType);
        Assert.Equal("", worker.Agent); // WT-C3: empty is scheduler.json's worker section
        Assert.Equal("", worker.Model);  // so spark still comes from there, as the slots said

        foreach (var (name, model) in new[] { ("Sonnet", "claude-sonnet-5-5"), ("Opus", "claude-opus-5-5") })
        {
            var claude = Assert.Single(pool.Types, t => Text.Same(t.Name, name));
            Assert.False(pool.IsDefault(name));
            Assert.Equal("claude", claude.Agent);
            Assert.Equal(model, claude.Model);
        }
    }

    // WT-P3, WT-C12: the page's Worker types panel shows the three types in the file's order, each
    // running-of-cap, and the total the pool gives.
    [Fact]
    public async Task Page_WorkerTypes_ThisProjectsWorkersJson_RowsAndCaps()
    {
        var (_, text) = PoolFrom(PoolForm);
        using var harness = new PageActionHarness(workersJson: text, project: "ptk-900b-page");
        harness.Tick_Once();

        var page = await harness.Client.GetStringAsync("/");

        Assert.Contains("Worker types", page, StringComparison.Ordinal);
        Assert.Contains("No workers running (0 of 4)", page, StringComparison.Ordinal);
        var at = new List<string> { "'worker'", "'Sonnet'", "'Opus'" };
        var last = -1;
        foreach (var name in at)
        {
            var found = page.IndexOf(name, StringComparison.Ordinal);
            Assert.True(found > last, $"type {name} is not in the panel's order");
            last = found;
        }

        Assert.Contains("<span class='wrunning'>0 of 2</span>", page, StringComparison.Ordinal);
        Assert.Equal(2, page.Split("<span class='wrunning'>0 of 1</span>", StringSplitOptions.None).Length - 1);
    }
}
