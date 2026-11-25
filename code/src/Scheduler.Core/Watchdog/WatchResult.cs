// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Watchdog;

/// <summary>The outcome of a watchdog step: the worker's new state and what the host does.</summary>
public sealed record WatchResult
{
    /// <summary>The worker with its progress, warning, stop and kill records updated.</summary>
    public required RunningWorker Worker { get; init; }

    /// <summary>What the host must do.</summary>
    public WatchAction Action { get; init; }

    /// <summary>Why, for a stop request or a kill; empty for none.</summary>
    public string Reason { get; init; } = "";

    /// <summary>The text of the warning file to write (TIME-4), or null.</summary>
    public string? WarnText { get; init; }

    /// <summary>Minutes without progress at this watch (TIME-3).</summary>
    public double Quiet { get; init; }

    /// <summary>Lines for the scheduler's log.</summary>
    public IReadOnlyList<string> Log { get; init; } = [];
}
