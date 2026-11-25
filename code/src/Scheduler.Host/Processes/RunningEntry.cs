// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;

namespace Scheduler.Host.Processes;

/// <summary>A worker as running.json keeps it: the worker and its figures (ADOPT-1).</summary>
public sealed record RunningEntry
{
    /// <summary>The worker.</summary>
    public required RunningWorker Worker { get; init; }

    /// <summary>The figures, or null when none were kept.</summary>
    public WorkerStats? Stats { get; init; }
}
