namespace Scheduler.Core.WebContract;

/// <summary>One slot in a live snapshot (PAGE-3): the log, stats and time.</summary>
public sealed record LiveSlot
{
    /// <summary>The last 20 rendered lines plus the line being written (PAGE-3).</summary>
    public IReadOnlyList<string> Log { get; init; } = [];

    /// <summary>The stats line (PAGE-10).</summary>
    public string Stats { get; init; } = "";

    /// <summary>The elapsed time text.</summary>
    public string Time { get; init; } = "";
}
