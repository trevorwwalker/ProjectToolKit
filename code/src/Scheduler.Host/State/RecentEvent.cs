// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.State;

/// <summary>
/// One finished run in the page's Recent events after a restart (ACCT-5, <c>Seed-Recent</c> L812):
/// what accounting.csv says, plus the readable log that belongs to the run.
/// </summary>
public sealed record RecentEvent
{
    /// <summary>When the run finished, from the row's <c>finished</c>.</summary>
    public required DateTimeOffset When { get; init; }

    /// <summary>The slot it ran in.</summary>
    public required int Slot { get; init; }

    /// <summary>The worker type's name as workers.json names it, or empty for a run from before
    /// accounting had the <c>type</c> column (950-C, WT-P6).</summary>
    public string Type { get; init; } = "";

    /// <summary>The run's model from the row's <c>model</c>, or empty when it has none (950-C).</summary>
    public string Model { get; init; } = "";

    /// <summary>The card name the row holds.</summary>
    public required string Card { get; init; }

    /// <summary>The result the worker ended with.</summary>
    public required string Result { get; init; }

    /// <summary>Whole minutes it ran.</summary>
    public required int Minutes { get; init; }

    /// <summary>The readable log, or null when no log began in time to belong to it.</summary>
    public string? Log { get; init; }
}
