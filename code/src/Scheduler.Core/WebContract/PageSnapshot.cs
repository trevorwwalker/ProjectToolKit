using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.WebContract;

/// <summary>
/// Everything the page windows of PAGE-5 show, built on the scheduler thread and read by the
/// page thread without locks (PAGE-1, PAGE-2). Immutable.
/// </summary>
public sealed record PageSnapshot
{
    /// <summary>When it was built (the last tick time in the top bar).</summary>
    public required DateTimeOffset Built { get; init; }

    /// <summary>The project name.</summary>
    public string Project { get; init; } = "";

    /// <summary>The scheduler version.</summary>
    public string Version { get; init; } = "";

    /// <summary>The project theme name (PAGE-11).</summary>
    public string Theme { get; init; } = "";

    /// <summary>The state (PAGE-8).</summary>
    public required PageStateView State { get; init; }

    /// <summary>A notice at the top (MOVE-11), or empty.</summary>
    public string Notice { get; init; } = "";

    /// <summary>The host load for the top bar, or null.</summary>
    public HostLoadSample? HostLoad { get; init; }

    /// <summary>Health and banner.</summary>
    public PageHealthView Health { get; init; } = new();

    /// <summary>Statistics.</summary>
    public PageStats Stats { get; init; } = new();

    /// <summary>The planner line of the Control window.</summary>
    public string PlannerLine { get; init; } = "";

    /// <summary>One window per worker slot (WT-P1: per running worker once the page moves to types).</summary>
    public IReadOnlyList<PageSlotView> Slots { get; init; } = [];

    /// <summary>The Worker types panel: one row per type, in config order (WT-P3).</summary>
    public IReadOnlyList<PageWorkerTypeView> WorkerTypes { get; init; } = [];

    /// <summary>Waiting on you.</summary>
    public IReadOnlyList<PageWaitingItem> Waiting { get; init; } = [];

    /// <summary>Requests to the planner.</summary>
    public PagePlannerView Planner { get; init; } = new();

    /// <summary>The categories (categories) in the order of the categories file, for the chips (970-C).</summary>
    public IReadOnlyList<PageCategory> Categories { get; init; } = [];

    /// <summary>The categories file's shared add-only list, for the Categories panel (992-B).</summary>
    public IReadOnlyList<string> SharedAddOnly { get; init; } = [];

    /// <summary>The queue in dispatch order, with notes (PAGE-6).</summary>
    public IReadOnlyList<PageQueueItem> Queue { get; init; } = [];

    /// <summary>The last 10 events, newest first (PAGE-5).</summary>
    public IReadOnlyList<PageEvent> Events { get; init; } = [];

    /// <summary>
    /// The daily figures the Trends window charts (roadmap item 3, 630-A): the last 14 local days,
    /// oldest first, one entry per model per day. Empty when there is less than one day of data,
    /// which is what hides the window.
    /// </summary>
    public IReadOnlyList<TrendDay> Trends { get; init; } = [];

    /// <summary>Whether planner push is on, or null when the project has no planner outbox (MP-9, 510-B).</summary>
    public bool? PlannerPush { get; init; }
}
