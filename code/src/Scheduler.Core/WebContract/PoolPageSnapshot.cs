using Scheduler.Core.Contracts;

namespace Scheduler.Core.WebContract;

/// <summary>
/// The pool's page (section 4 of the multi-project design): one <see cref="PageSnapshot"/> per
/// project plus the pool part - each project's weight, share and state (MP-P4), the worker types
/// with their running workers per project (MP-P7), and the total. Immutable.
/// </summary>
public sealed record PoolPageSnapshot
{
    /// <summary>When it was built.</summary>
    public required DateTimeOffset Built { get; init; }

    /// <summary>The pool's total (WT-C1, MP-11).</summary>
    public int MaxWorkers { get; init; }

    /// <summary>Workers running now in every project, adopted ones included (MP-11).</summary>
    public int Running { get; init; }

    /// <summary>The projects in the pool file's order (MP-C2).</summary>
    public IReadOnlyList<PoolProjectView> Projects { get; init; } = [];

    /// <summary>The worker types in the pool file's order (MP-C4).</summary>
    public IReadOnlyList<PoolTypeView> Types { get; init; } = [];

    /// <summary>
    /// 1055-C: every project's queued cards in the order the pool will start them
    /// (<see cref="Pool.PoolQueueOrder"/>), computed once per snapshot; empty when there is no pool part.
    /// </summary>
    public IReadOnlyList<Pool.PoolQueueEntry> QueueOrder { get; init; } = [];

    /// <summary>Each project's own page, in the same order as <see cref="Projects"/> (the Host adds them).</summary>
    public IReadOnlyList<PageSnapshot> Pages { get; init; } = [];

    /// <summary>
    /// 1090-B: each project's views data (VW-20 to VW-22), in the order of <see cref="Projects"/>; the Host
    /// adds them. Never part of the page version: <see cref="ProjectViews.VersionOf"/> is its own.
    /// </summary>
    public IReadOnlyList<ProjectViews> Views { get; init; } = [];
}

/// <summary>One project in the Projects window (MP-P4).</summary>
public sealed record PoolProjectView
{
    /// <summary>The project's name.</summary>
    public required string Name { get; init; }

    /// <summary>The project's repository folder ("" when unknown); shown on the chip's hover (MP-P9).</summary>
    public string Repo { get; init; } = "";

    /// <summary>Its weight (MP-4).</summary>
    public int Weight { get; init; }

    /// <summary>Its weight over every project's, 0 to 1.</summary>
    public double WeightShare { get; init; }

    /// <summary>Its worker-minutes in the 2 h window over every project's, 0 to 1 (MP-14).</summary>
    public double UsedShare { get; init; }

    /// <summary>Its state; red, paused or draining stops only this project (MP-12).</summary>
    public SchedulerState State { get; init; } = SchedulerState.Running;

    /// <summary>Its running workers.</summary>
    public int Running { get; init; }

    /// <summary>1029-B: its workspaces (the pool file's setting, else the pool's <c>maxWorkers</c>); 0 = not shown.</summary>
    public int Workspaces { get; init; }

    /// <summary>Its cards waiting to start (the last plan's candidates).</summary>
    public int Queued { get; init; }

    /// <summary>Dynamic locks (DL-26): today's lock events; null when locks are off.</summary>
    public PageLockCounts? LockCounts { get; init; }
}

/// <summary>One worker type in the Worker types panel of a pool (MP-P7): no categories (969-A, MP-C5).</summary>
public sealed record PoolTypeView
{
    /// <summary>The type's name.</summary>
    public required string Name { get; init; }

    /// <summary>Its cap (WT-2).</summary>
    public int Max { get; init; }

    /// <summary>Its agent; "" is each project's <c>worker</c> section (MP-C8).</summary>
    public string Agent { get; init; } = "";

    /// <summary>1087-C: the model as the pool file sets it, or "" (WT-C4).</summary>
    public string Model { get; init; } = "";

    /// <summary>1087-C: the model server endpoint, or "" for none of its own (WT-C4).</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>1087-C: the metrics URL, or "" (WT-C4).</summary>
    public string MetricsUrl { get; init; } = "";

    /// <summary>1087-C: premium, as the pool file sets it (WT-3).</summary>
    public bool Premium { get; init; }

    /// <summary>
    /// 1087-C: the model the type really runs when its own is blank - the agent's default or the
    /// served name of a server alias, as the worker windows show it (WT-P3). The Host fills it
    /// (<c>PoolComposition.PoolPart</c>); "" until then. The panel shows it greyed as the Model
    /// box's placeholder, so a type never reads as having no model.
    /// </summary>
    public string EffectiveModel { get; init; } = "";

    /// <summary>The projects it serves; empty is every project (WT-C8).</summary>
    public IReadOnlyList<string> Projects { get; init; } = [];

    /// <summary>Its running workers by project; a project with none is left out.</summary>
    public IReadOnlyDictionary<string, int> RunningByProject { get; init; } = new Dictionary<string, int>();
}
