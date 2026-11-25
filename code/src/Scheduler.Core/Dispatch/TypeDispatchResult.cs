// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Dispatch;

/// <summary>The outcome of a dispatch by type (WT-6, WT-12): the starts tried and the Queue window's notes.</summary>
public sealed record TypeDispatchResult
{
    /// <summary>The starts tried, in order.</summary>
    public required IReadOnlyList<TypeStart> Starts { get; init; }

    /// <summary>The notes: one for every candidate not tried (WT-12), then the plan's notes.</summary>
    public required IReadOnlyList<DispatchNote> Notes { get; init; }
}
