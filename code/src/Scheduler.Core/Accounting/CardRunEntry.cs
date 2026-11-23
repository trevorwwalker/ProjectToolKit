namespace Scheduler.Core.Accounting;

/// <summary>
/// One card in the control window's figures (ACCT-6): its runs of the last 24 hours, the model of
/// its latest run, and where the card is now.
/// </summary>
public sealed record CardRunEntry
{
    /// <summary>The card's id, such as <c>010-A</c>.</summary>
    public required string Card { get; init; }

    /// <summary>How many runs of the window worked on it.</summary>
    public required int Runs { get; init; }

    /// <summary>The minutes of all those runs together.</summary>
    public required int Minutes { get; init; }

    /// <summary>The model of its latest run, which is the model the card is counted under.</summary>
    public required string Model { get; init; }

    /// <summary>Where the card is now.</summary>
    public required CardRunState State { get; init; }
}
