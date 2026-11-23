using Scheduler.Core.Accounting;

namespace Scheduler.Core.WebContract;

/// <summary>The statistics line of the Control window (PAGE-5, A8).</summary>
public sealed record PageStats
{
    /// <summary>Cards finished.</summary>
    public int Cards { get; init; }

    /// <summary>Total worker minutes.</summary>
    public double Minutes { get; init; }

    /// <summary>The text as shown.</summary>
    public string Text { get; init; } = "";

    /// <summary>
    /// 1095-C: the summary and the Trends buckets for every range the page offers
    /// (<see cref="CardStats.Ranges"/>), so a change of range needs no round trip. Empty on a
    /// snapshot built without them; <see cref="Text"/> is then the 24 h summary.
    /// </summary>
    public IReadOnlyList<PageStatsRange> Ranges { get; init; } = [];
}

/// <summary>One range's summary and chart (1095-C).</summary>
public sealed record PageStatsRange
{
    /// <summary>The range's key, as <see cref="StatsRange.Key"/>.</summary>
    public string Key { get; init; } = "";

    /// <summary>Cards in the range.</summary>
    public int Cards { get; init; }

    /// <summary>Worker minutes in the range.</summary>
    public double Minutes { get; init; }

    /// <summary>The summary lines, joined with newlines, as for <see cref="PageStats.Text"/>.</summary>
    public string Text { get; init; } = "";

    /// <summary>The Trends buckets over the range, oldest first.</summary>
    public IReadOnlyList<TrendDay> Trend { get; init; } = [];
}
