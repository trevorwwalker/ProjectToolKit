// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Launch;

/// <summary>What adopting one running worker leaves the restarted scheduler with (ADOPT-2).</summary>
public sealed record AdoptionResult
{
    /// <summary>The worker as the restarted scheduler now runs it.</summary>
    public required RunningWorker Worker { get; init; }

    /// <summary>The slot count now: a worker on a slot above it raised it.</summary>
    public required int Slots { get; init; }

    /// <summary>The slots the adoption turned on because it raised the count.</summary>
    public IReadOnlyList<int> NewlyEnabled { get; init; } = [];
}
