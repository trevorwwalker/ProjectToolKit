// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.WebContract;

/// <summary>The Requests to the planner window (PAGE-5).</summary>
public sealed record PagePlannerView
{
    /// <summary>The open requests.</summary>
    public IReadOnlyList<JobRequest> OpenRequests { get; init; } = [];

    /// <summary>Cards waiting on the planner, by name.</summary>
    public IReadOnlyList<string> WaitingCards { get; init; } = [];

    /// <summary>Whether every dependency of the planner cards is done: "all done: the planner can act now".</summary>
    public bool AllDone { get; init; }

    /// <summary>By waiting card, the cards it depends on (510-B).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> WaitingDepends { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>The waiting cards whose every dependency is finished (510-B).</summary>
    public IReadOnlyList<string> WaitingReady { get; init; } = [];
}
