// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Accounting;

/// <summary>
/// One run of a finished card as the time-limit history keeps it (roadmap item 4, card 640-A):
/// the card's area letter, the model that ran it and the whole minutes it ran, read back from an
/// accounting.csv row (ACCT-1). What <see cref="AreaDurations"/> measures a typical card on.
/// </summary>
public sealed record AreaRun
{
    /// <summary>The area (category) letter the card was in.</summary>
    public required string Area { get; init; }

    /// <summary>The model that ran it, as the row's model column holds it.</summary>
    public required string Model { get; init; }

    /// <summary>Whole minutes it ran.</summary>
    public required int Minutes { get; init; }

    /// <summary>When the run finished; the window and the newest-30 cut are taken on this.</summary>
    public required DateTimeOffset Finished { get; init; }
}
