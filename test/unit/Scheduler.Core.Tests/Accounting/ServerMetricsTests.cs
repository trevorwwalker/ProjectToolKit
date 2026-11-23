using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Accounting;

public class ServerMetricsTests
{
    private const string Url = "http://s:8000/metrics";

    private static string Metrics(long generation) => string.Join('\n',
        "# HELP vllm:num_requests_running Number of requests running",
        "# TYPE vllm:num_requests_running gauge",
        "vllm:num_requests_running{model_name=\"Qwen3\"} 2",
        "vllm:num_requests_waiting{model_name=\"Qwen3\"} 1",
        "vllm:kv_cache_usage_perc{model_name=\"Qwen3\"} 0.43",
        "vllm:prompt_tokens_total{model_name=\"Qwen3\"} 1000",
        "vllm:generation_tokens_total{model_name=\"Qwen3\"} " + generation,
        "vllm:prefix_cache_hits_total{model_name=\"Qwen3\"} 20",
        "vllm:prefix_cache_queries_total{model_name=\"Qwen3\"} 80",
        "vllm:num_preemptions_total{model_name=\"Qwen3\"} 0");

    private static RunningWorker Worker(int slot, string metricsUrl) => new()
    {
        Slot = slot,
        Pid = 1000 + slot,
        Start = new DateTimeOffset(2026, 11, 18, 11, 0, 0, TimeSpan.FromHours(-8)),
        Card = "010-A-slug",
        Category = "A",
        Id = "010-A",
        MetricsUrl = metricsUrl,
    };

    // ACCT-7 (Sample-Server L1021, Sample-One L1030): vLLM's gauges parsed, one row per sample
    // per distinct URL, and the line the page shows.
    [Fact]
    public void Acct_ServerMetrics_SampledAndLine()
    {
        var text = Metrics(500);
        var sample = ServerMetrics.Parse(text, new DateTimeOffset(2026, 11, 18, 11, 48, 35, TimeSpan.FromHours(-8)));
        Assert.Equal(2, sample.Running);
        Assert.Equal(1, sample.Waiting);
        Assert.Equal(0.43, sample.KvUsage);
        Assert.Equal(1000, sample.PromptTokens);
        Assert.Equal(500, sample.GenerationTokens);
        Assert.Equal(20, sample.PrefixHits);
        Assert.Equal(80, sample.PrefixQueries);
        Assert.Equal(0, sample.Preemptions);

        // A gauge the server does not publish is NaN, not zero.
        var sparse = ServerMetrics.Parse("vllm:num_requests_running 2\nvllm:num_requests_waiting 1", sample.When);
        Assert.Equal(2, sparse.Running);
        Assert.True(double.IsNaN(sparse.KvUsage));
        Assert.True(double.IsNaN(sparse.Preemptions));

        Assert.Equal(
            "time,slots,workers,running,waiting,kv_usage,prompt_tokens,generation_tokens,prefix_hits,prefix_queries,preemptions,gen_tokens_per_min",
            ServerMetrics.Header);

        // Two slots share the URL, and so do two running workers: it is sampled once.
        var clock = new FakeClock(sample.When);
        var probe = new FakeServerProbe { MetricsText = text };
        var rows = new List<string>();
        var sampler = new ServerMetricsSampler(clock, probe, rows.Add);
        var workers = new[] { Worker(1, Url), Worker(2, Url) };

        Assert.Equal(1, sampler.Sample(3, [Url, Url, ""], workers));
        Assert.Equal(1, probe.MetricsFetches);
        Assert.Single(rows);

        // The first sample has no rate yet: an empty last column and no T/s in the line.
        Assert.Equal("2026-11-18T11:48:35,3,2,2,1,0.430,1000,500,20,80,0,", rows[0]);
        Assert.Equal("server 2 running, 1 waiting, KV 43 %", sampler.LoadLine(Url));

        // 1200 tokens in the 30 s since: 2400 a minute, 40 T/s, nothing preempted.
        probe.MetricsText = Metrics(1700);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, sampler.Sample(3, [Url], workers));
        Assert.Equal(2, rows.Count);
        Assert.Equal("2026-11-18T11:49:05,3,2,2,1,0.430,1000,1700,20,80,0,2400", rows[1]);
        Assert.Equal("server 2 running, 1 waiting, KV 43 %, 40 T/s total, 0 preempted", sampler.LoadLine(Url));

        // The 30 s cadence: a second call inside the window asks for nothing.
        Assert.Equal(0, sampler.Sample(3, [Url], workers));
        Assert.Equal(2, probe.MetricsFetches);
        Assert.Equal(2, rows.Count);

        // A URL no slot or worker uses is not asked for, and a server that does not answer
        // writes no row.
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, sampler.Sample(3, [null, "  "], workers));
        Assert.Equal(3, probe.MetricsFetches);
        Assert.Equal(3, rows.Count);

        clock.Advance(TimeSpan.FromSeconds(30));
        probe.MetricsText = null;
        Assert.Equal(0, sampler.Sample(3, [Url], workers));
        Assert.Equal(3, rows.Count);
    }
}
