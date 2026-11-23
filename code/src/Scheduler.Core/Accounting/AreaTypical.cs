namespace Scheduler.Core.Accounting;

/// <summary>
/// What finished cards typically take in one area on one model (roadmap item 4, card 640-A): the
/// median and the 90th percentile of the runs' minutes. The Queue window warns beside a card whose
/// <c>Time box:</c> is shorter than the percentile, and suggests it where the card has no time box.
/// </summary>
public sealed record AreaTypical
{
    /// <summary>The area (category) letter the runs were in.</summary>
    public required string Area { get; init; }

    /// <summary>The model that ran them.</summary>
    public required string Model { get; init; }

    /// <summary>How many runs the figures were taken from, at most <see cref="AreaDurations.WindowRuns"/>.</summary>
    public required int Runs { get; init; }

    /// <summary>The median minutes: the middle run, or the mean of the two middles, rounded.</summary>
    public required int Median { get; init; }

    /// <summary>The 90th percentile minutes by nearest rank: up to this long, 9 runs in 10 finish.</summary>
    public required int P90 { get; init; }
}
