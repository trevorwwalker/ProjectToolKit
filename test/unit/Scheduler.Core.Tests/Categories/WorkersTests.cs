using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;

namespace Scheduler.Core.Tests.Categories;

/// <summary>
/// workers.json from main and what it makes of a worker (WRK-1, WRK-2, WRK-4, WRK-7;
/// <c>Apply-Workers</c> L2557, <c>Effective-Worker</c> L2695, <c>Jobs-Per-Worker</c> L703).
/// Test plan: scheduler-next/test-plans/workers.md.
/// </summary>
public class WorkersTests
{
    private const string Rel = "docs/jobs/workers.json";
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";

    private sealed class FakeLog : ISchedulerLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string line) => Lines.Add(line);
    }

    private static SchedulerConfig Load(string json) => SchedulerConfigLoader.Load(json, Repo, LocalAppData);

    private static WorkerSettings Settings(SchedulerConfig cfg, FakeLog? log = null) =>
        new(log ?? new FakeLog(), cfg, Rel);

    // WRK-1: every field is read, categories upper-cased.
    [Fact]
    public void Workers_JsonFromMain_ParsedAndCategoriesUpperCased()
    {
        var settings = Settings(Load("{}"));
        settings.Apply(
            """{"slots":3,"workers":{"1":{"name":"big","agent":"claude","endpoint":"http://w:8000/v1","model":"m1","metricsUrl":"http://w:9/m","lanes":"ab","premium":true}}}""",
            []);

        var w = settings.Workers[1];
        Assert.Equal(1, w.Slot);
        Assert.Equal("big", w.Name);
        Assert.Equal("claude", w.Agent);
        Assert.Equal("http://w:8000/v1", w.Endpoint);
        Assert.Equal("m1", w.Model);
        Assert.Equal("http://w:9/m", w.MetricsUrl);
        Assert.Equal("AB", w.Categories);
        Assert.Equal(3, settings.Slots);
    }

    // WRK-1: the same text is applied once; invalid JSON is logged and the previous settings kept.
    [Fact]
    public void Workers_JsonUnchangedText_NotReapplied_InvalidKeepsPrevious()
    {
        var log = new FakeLog();
        var settings = Settings(Load("{}"), log);
        var text = """{"slots":3,"workers":{"1":{"name":"one"}}}""";

        Assert.True(settings.Apply(text, []));
        Assert.False(settings.Apply(text, []));
        Assert.Single(log.Lines, l => l.StartsWith("workers: 3"));

        // The same text no longer applies, even after the in-memory state was disturbed.
        settings.Workers.Clear();
        Assert.False(settings.Apply(text, []));
        Assert.Empty(settings.Workers);

        Assert.False(settings.Apply("{ nope", []));
        Assert.Contains(log.Lines, l => l.Contains("not valid JSON"));
        Assert.Equal(3, settings.Slots);

        // Previous text was kept as seen: sending it again is still not a change.
        Assert.False(settings.Apply(text, []));
    }

    // WRK-2: slots from workers.json, never below the highest running slot; new slots start enabled.
    [Fact]
    public void Workers_SlotsFromJson_NeverBelowHighestRunning()
    {
        var settings = Settings(Load("""{"slots":4}"""));
        Assert.Equal(4, settings.Slots);

        settings.Apply("""{"slots":2}""", [4]);
        Assert.Equal(4, settings.Slots);

        settings.Apply("""{"slots":6}""", []);
        Assert.Equal(6, settings.Slots);
        Assert.True(settings.Enabled[5]);
        Assert.True(settings.Enabled[6]);

        // A slot someone disabled stays disabled when the count grows past it.
        var pinned = Settings(Load("""{"slots":2}"""));
        pinned.Enabled[3] = false;
        pinned.Apply("""{"slots":4}""", []);
        Assert.False(pinned.Enabled[3]);
        Assert.True(pinned.Enabled[4]);
    }

    // WRK-4: the endpoint, model and metrics a worker really has.
    [Fact]
    public void Workers_EffectiveWorker_EndpointModelMetrics()
    {
        const string cfgJson = """
            {"worker":{"endpoint":"http://c:1/v1","model":"cm","metricsUrl":""},
             "agents":{
               "cl":{"cli":"claude","args":["--output-format","stream-json"]},
               "own":{"cli":"x","args":["-p","{prompt}"],"endpoint":""},
               "ag":{"cli":"x","args":["-p","{prompt}"],"endpoint":"http://a/v1","model":"am"}
             }}
            """;
        var cfg = Load(cfgJson);
        EffectiveWorker Of(string workerJson)
        {
            var s = Settings(cfg);
            s.Apply("{\"workers\":{\"1\":" + workerJson + "}}", []);
            return WorkerSelection.Effective(cfg, s.Workers, 1);
        }

        // Agent format claude: no endpoint, no metrics, even with a config endpoint.
        var claude = Of("""{"agent":"cl"}""");
        Assert.Equal("", claude.Endpoint);
        Assert.Equal("", claude.MetricsUrl);

        // Agent with endpoint "": its own service, same.
        var own = Of("""{"agent":"own"}""");
        Assert.Equal("", own.Endpoint);
        Assert.Equal("", own.MetricsUrl);

        // Worker endpoint: that, with metrics at its authority.
        var withEndpoint = Of("""{"endpoint":"http://w:8000/v1"}""");
        Assert.Equal("http://w:8000/v1", withEndpoint.Endpoint);
        Assert.Equal("http://w:8000/metrics", withEndpoint.MetricsUrl);

        // Worker metricsUrl is used as is.
        Assert.Equal("http://m:1/x", Of("""{"endpoint":"http://w:8000/v1","metricsUrl":"http://m:1/x"}""").MetricsUrl);

        // No worker endpoint, agent endpoint: from the agent.
        var fromAgent = Of("""{"agent":"ag"}""");
        Assert.Equal("http://a/v1", fromAgent.Endpoint);
        Assert.Equal("http://a/metrics", fromAgent.MetricsUrl);

        // Nothing on worker or agent: the config's endpoint, metrics derived from it.
        var plain = Of("{}");
        Assert.Equal("http://c:1/v1", plain.Endpoint);
        Assert.Equal("http://c:1/metrics", plain.MetricsUrl);

        // ...or the config's metricsUrl when it has one.
        var cfg2 = Load("""{"worker":{"endpoint":"http://c:1/v1","metricsUrl":"http://c:2/mm"}}""");
        Assert.Equal("http://c:2/mm", WorkerSelection.Effective(cfg2, new Dictionary<int, WorkerDefinition>(), 1).MetricsUrl);

        // Model: the worker's, else the agent's, else the config's.
        Assert.Equal("wm", Of("""{"agent":"ag","model":"wm"}""").Model);
        Assert.Equal("am", Of("""{"agent":"ag"}""").Model);
        Assert.Equal("cm", Of("{}").Model);
    }

    // WRK-7: floor(cores / slots), at least 1; no slots counts as one; {jobs} carries it.
    [Fact]
    public void Workers_ThreadsPerWorker()
    {
        Assert.Equal(8, WorkerSelection.JobsPerWorker(16, 2));
        Assert.Equal(5, WorkerSelection.JobsPerWorker(16, 3));
        Assert.Equal(1, WorkerSelection.JobsPerWorker(2, 4));
        Assert.Equal(16, WorkerSelection.JobsPerWorker(16, 0));

        // The value goes into {jobs} (the env var named by jobsEnvVar is set when the worker starts, 160-A).
        var cfg = Load("{}");
        Assert.Equal("-j 5", Agents.Expand(cfg, "-j {jobs}", WorkerSelection.JobsPerWorker(16, 3)));
        Assert.Equal("AGENT_JOBS", cfg.JobsEnvVar);
    }
}
