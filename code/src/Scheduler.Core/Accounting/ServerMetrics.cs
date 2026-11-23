using System.Globalization;
using System.Text.RegularExpressions;

namespace Scheduler.Core.Accounting;

/// <summary>
/// vLLM's Prometheus text, the row it adds to metrics-&lt;host&gt;.csv and the line the page shows
/// (ACCT-7, <c>Sample-One</c> L1030).
/// </summary>
public static class ServerMetrics
{
    private static readonly string[] Gauges =
    [
        "num_requests_running",
        "num_requests_waiting",
        "kv_cache_usage_perc",
        "prompt_tokens_total",
        "generation_tokens_total",
        "prefix_cache_hits_total",
        "prefix_cache_queries_total",
        "num_preemptions_total",
    ];

    /// <summary>The line a new metrics CSV starts with.</summary>
    public const string Header =
        "time,slots,workers,running,waiting,kv_usage,prompt_tokens,generation_tokens,prefix_hits,prefix_queries,preemptions,gen_tokens_per_min";

    /// <summary>The eight gauges and counters of one read, NaN for whatever the server did not publish.</summary>
    public static ServerSample Parse(string? text, DateTimeOffset when)
    {
        var body = text ?? "";
        return new ServerSample
        {
            When = when,
            Running = Read(body, Gauges[0]),
            Waiting = Read(body, Gauges[1]),
            KvUsage = Read(body, Gauges[2]),
            PromptTokens = Read(body, Gauges[3]),
            GenerationTokens = Read(body, Gauges[4]),
            PrefixHits = Read(body, Gauges[5]),
            PrefixQueries = Read(body, Gauges[6]),
            Preemptions = Read(body, Gauges[7]),
        };
    }

    /// <summary>
    /// The CSV row for a sample, with the tokens a minute since <paramref name="previous"/> in
    /// the last column (empty, as <c>Sample-One</c> leaves it, when there is no previous sample
    /// or none worth comparing).
    /// </summary>
    public static string Row(ServerSample sample, int slots, int workers, ServerSample? previous)
    {
        var perMinute = TokensPerMinute(sample, previous);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4},{5:0.000},{6},{7},{8},{9},{10},{11}",
            sample.When.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            slots,
            workers,
            Number(sample.Running),
            Number(sample.Waiting),
            sample.KvUsage,
            Number(sample.PromptTokens),
            Number(sample.GenerationTokens),
            Number(sample.PrefixHits),
            Number(sample.PrefixQueries),
            Number(sample.Preemptions),
            perMinute is null ? "" : perMinute.Value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// What the page shows for a server (ACCT-7): "server 2 running, 1 waiting, KV 43 %", with
    /// the speed and the preemptions added when the sample can be compared with the last one.
    /// </summary>
    public static string Line(ServerSample sample, ServerSample? previous)
    {
        var kv = double.IsNaN(sample.KvUsage)
            ? "?"
            : ((long)Math.Round(100 * sample.KvUsage, MidpointRounding.ToEven)).ToString(CultureInfo.InvariantCulture);
        var line = $"server {Number(sample.Running)} running, {Number(sample.Waiting)} waiting, KV {kv} %";
        if (TokensPerMinute(sample, previous) is not { } perMinute)
        {
            return line;
        }

        var preempted = double.IsNaN(sample.Preemptions) || double.IsNaN(previous!.Preemptions)
            ? "?"
            : ((long)Math.Round(sample.Preemptions - previous.Preemptions, MidpointRounding.ToEven)).ToString(CultureInfo.InvariantCulture);
        var perSecond = Math.Round(perMinute / 60.0, 1, MidpointRounding.ToEven).ToString("0.#", CultureInfo.InvariantCulture);
        return $"{line}, {perSecond} T/s total, {preempted} preempted";
    }

    /// <summary>Generated tokens a minute since <paramref name="previous"/>, or null when it cannot be worked out.</summary>
    private static long? TokensPerMinute(ServerSample sample, ServerSample? previous)
    {
        if (previous is null)
        {
            return null;
        }

        var minutes = (sample.When - previous.When).TotalMinutes;
        if (minutes <= 0 || double.IsNaN(previous.GenerationTokens) || double.IsNaN(sample.GenerationTokens))
        {
            return null;
        }

        // [int] of the rate: to the nearest, halves to even, as PowerShell casts it (L1044).
        return (long)Math.Round((sample.GenerationTokens - previous.GenerationTokens) / minutes, MidpointRounding.ToEven);
    }

    private static double Read(string text, string gauge)
    {
        var hit = Regex.Match(text, @"(?m)^vllm:" + Regex.Escape(gauge) + @"(\{[^}]*\})?\s+(\S+)");
        return hit.Success
            && double.TryParse(hit.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : double.NaN;
    }

    // How PowerShell prints a double it is told to write: 2, not 2.0.
    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
}
