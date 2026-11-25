// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Accounting;

/// <summary>
/// One model's share of one day of the Trends chart (roadmap item 3, card 630-A): the cards that
/// finished that day under that model, and how many runs it took.
/// </summary>
public sealed record TrendSegment
{
    /// <summary>The model's key, as accounting.csv's model column holds it (ACCT-2).</summary>
    public required string Model { get; init; }

    /// <summary>What the chart calls it: the model's <c>short</c> name, or the key when models.json has none.</summary>
    public required string Label { get; init; }

    /// <summary>How many distinct cards finished (done or review) that day under this model.</summary>
    public required int CardsDone { get; init; }

    /// <summary>How many runs that day were this model's.</summary>
    public required int Runs { get; init; }
}
