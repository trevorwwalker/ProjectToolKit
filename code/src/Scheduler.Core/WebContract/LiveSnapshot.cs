namespace Scheduler.Core.WebContract;

/// <summary>The live event the page polls (PAGE-3): t, pageDone and the slots by number.</summary>
public sealed record LiveSnapshot
{
    /// <summary>When it was built.</summary>
    public required DateTimeOffset T { get; init; }

    /// <summary>Whether the page snapshot is up to date, so the page need not reload.</summary>
    public bool PageDone { get; init; }

    /// <summary>The slots by number.</summary>
    public IReadOnlyDictionary<int, LiveSlot> Slots { get; init; } = new Dictionary<int, LiveSlot>();

    /// <summary>
    /// 1090-B, VW-20: the views version of every project (<see cref="ProjectViews.VersionOf"/>), "" without a pool.
    /// A page on a view other than Workers fetches its address again when it changes.
    /// </summary>
    public string Views { get; init; } = "";
}
