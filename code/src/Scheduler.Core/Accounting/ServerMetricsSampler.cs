// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Accounting;

/// <summary>
/// The 30-second read of the model servers (ACCT-7, <c>Sample-Server</c> L1021): every distinct
/// metrics URL of the slots and the running workers, once each, each writing one CSV row and one
/// line the page shows. Host hands it the probe and the CSV append; the tick's cadence is the
/// 30 seconds here, so a second call inside the window asks for nothing.
/// </summary>
public sealed class ServerMetricsSampler(IClock clock, IServerProbe probe, Action<string>? appendCsvRow = null)
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, ServerSample> _last = new(Text.Comparer);
    private readonly Dictionary<string, string> _lines = new(Text.Comparer);
    private DateTimeOffset _askedAt = DateTimeOffset.MinValue;

    /// <summary>The last line worked out for a URL, or null when it has not been sampled.</summary>
    public string? LoadLine(string metricsUrl) => _lines.TryGetValue(metricsUrl, out var line) ? line : null;

    /// <summary>The last sample of each URL, for the page and for the next sample's rate.</summary>
    public IReadOnlyDictionary<string, ServerSample> Samples => _last;

    /// <summary>
    /// Samples the URLs of these slots and running workers. Returns how many rows were written,
    /// which is 0 when the 30 seconds have not passed or no server answered.
    /// </summary>
    public int Sample(int slots, IEnumerable<string?> slotMetricsUrls, IEnumerable<RunningWorker> running)
    {
        if ((clock.Now - _askedAt).TotalSeconds < Every.TotalSeconds)
        {
            return 0;
        }

        _askedAt = clock.Now;
        var workers = running.ToList();

        var urls = new List<string>();
        foreach (var url in slotMetricsUrls.Concat(workers.Select(w => w.MetricsUrl)))
        {
            if (!string.IsNullOrWhiteSpace(url) && !urls.Contains(url, Text.Comparer))
            {
                urls.Add(url);
            }
        }

        var rows = 0;
        foreach (var url in urls)
        {
            var text = probe.ReadMetrics(url);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue; // no answer: nothing is recorded, as Sample-One leaves it
            }

            var sample = ServerMetrics.Parse(text, clock.Now);
            _last.TryGetValue(url, out var previous);
            appendCsvRow?.Invoke(ServerMetrics.Row(
                sample, slots, workers.Count(w => Text.Same(w.MetricsUrl, url)), previous));
            _last[url] = sample;
            _lines[url] = ServerMetrics.Line(sample, previous);
            rows++;
        }

        return rows;
    }
}
