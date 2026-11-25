// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.WebContract;

/// <summary>The state as the page shows it (PAGE-8): the state, its text and its tone.</summary>
public sealed record PageStateView
{
    /// <summary>The dispatching state.</summary>
    public required SchedulerState State { get; init; }

    /// <summary>The text, for example "paused: main is red" (PAGE-8).</summary>
    public required string Text { get; init; }

    /// <summary>Its class: ok, warn or bad.</summary>
    public PageTone Tone { get; init; } = PageTone.Ok;
}
