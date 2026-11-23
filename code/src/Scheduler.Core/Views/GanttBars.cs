namespace Scheduler.Core.Views;

/// <summary>One bar of the Gantt: a run (or a running card's run so far), or a group's summary span.</summary>
public sealed record GanttBar
{
    public required DateTimeOffset Start { get; init; }

    /// <summary>The run's end; for a running bar, now.</summary>
    public required DateTimeOffset End { get; init; }

    /// <summary>The colour key: done, review, needs-human, waiting, failed, stopped, yielded, running, other, or summary.</summary>
    public required string ColorKey { get; init; }

    public string Model { get; init; } = "";

    public bool Running { get; init; }

    /// <summary>For a running bar: where the card's time box ends, when it has one.</summary>
    public DateTimeOffset? TimeBoxEnd { get; init; }

    /// <summary>True for a group row's single summary span.</summary>
    public bool Summary { get; init; }
}

/// <summary>The bars of the Gantt (pure): one per run inside a window, and a summary span per group.</summary>
public static class GanttBars
{
    private static readonly HashSet<string> KnownResults = new(StringComparer.Ordinal)
    {
        "done", "review", "needs-human", "waiting", "failed", "stopped", "yielded",
    };

    /// <summary>A result word as a colour key; an unknown or empty result is "other".</summary>
    public static string ColorKey(string result)
    {
        var key = (result ?? "").Trim().ToLowerInvariant();
        return KnownResults.Contains(key) ? key : "other";
    }

    /// <summary>The bar of one run, or null when it lies wholly outside [from, to]. A running run ends at <paramref name="now"/>.</summary>
    public static GanttBar? Of(ViewRun run, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        var end = run.End ?? (now < run.Start ? run.Start : now);
        if (run.Start > to || end < from)
        {
            return null;
        }

        return new GanttBar
        {
            Start = run.Start,
            End = end,
            ColorKey = run.Running ? "running" : ColorKey(run.Result),
            Model = run.Model,
            Running = run.Running,
            TimeBoxEnd = run.Running ? run.TimeBoxEnd : null,
        };
    }

    /// <summary>The bars of one card inside the window, in start order.</summary>
    public static IReadOnlyList<GanttBar> ForCard(IEnumerable<ViewRun> runs, string cardId, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now) =>
        runs.Where(r => string.Equals(r.CardId, cardId, StringComparison.Ordinal))
            .OrderBy(r => r.Start)
            .Select(r => Of(r, from, to, now))
            .OfType<GanttBar>()
            .ToList();

    /// <summary>One summary span from the first start to the last end of the bars, or null when there are none.</summary>
    public static GanttBar? Summarise(IEnumerable<GanttBar> bars)
    {
        var list = bars.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        return new GanttBar
        {
            Start = list.Min(b => b.Start),
            End = list.Max(b => b.End),
            ColorKey = "summary",
            Summary = true,
        };
    }
}
