// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Accounting;

/// <summary>
/// One day of the Trends chart (roadmap item 3, card 630-A): a local date and what each model did
/// on it. A day nothing ran on is still in the window, empty, so the days line up along the
/// bottom of the chart.
/// </summary>
public sealed record TrendDay
{
    /// <summary>The local date the runs' finish times fall on.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>
    /// 1095-C: the local time the bucket starts at, for a range shorter than a day's buckets; midnight
    /// for a day.
    /// </summary>
    public TimeOnly Time { get; init; }

    /// <summary>1095-C: the axis label the bucket was given; empty means the chart writes the date.</summary>
    public string Label { get; init; } = "";

    /// <summary>One entry per model that ran that day, in name order.</summary>
    public required IReadOnlyList<TrendSegment> Segments { get; init; }

    /// <summary>Cards that finished that day, over every model.</summary>
    public int CardsDone
    {
        get
        {
            var total = 0;
            foreach (var segment in Segments)
            {
                total += segment.CardsDone;
            }

            return total;
        }
    }

    /// <summary>Runs that ended that day, over every model.</summary>
    public int Runs
    {
        get
        {
            var total = 0;
            foreach (var segment in Segments)
            {
                total += segment.Runs;
            }

            return total;
        }
    }

    /// <summary>Whether anything that counts ran on this day.</summary>
    public bool HasData => Segments.Count > 0;
}
