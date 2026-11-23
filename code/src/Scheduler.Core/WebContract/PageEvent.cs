namespace Scheduler.Core.WebContract;

/// <summary>One line of Recent events (PAGE-5).</summary>
public sealed record PageEvent
{
    /// <summary>When it happened.</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>The text.</summary>
    public required string Text { get; init; }

    /// <summary>Its tone.</summary>
    public PageTone Tone { get; init; } = PageTone.Ok;

    /// <summary>The worker slot, or null when the event is not a run (510-B).</summary>
    public int? Slot { get; init; }

    /// <summary>
    /// The worker type's name as workers.json names it, or empty for a run from before the
    /// accounting held a type column (950-C, WT-P6).
    /// </summary>
    public string Type { get; init; } = "";

    /// <summary>The run's model, or empty when the row predates the model column (950-C, WT-P6).</summary>
    public string Model { get; init; } = "";

    /// <summary>The card, or empty (510-B).</summary>
    public string Card { get; init; } = "";

    /// <summary>How the run ended (done, review, killed, ...), or empty (510-B).</summary>
    public string Result { get; init; } = "";

    /// <summary>The minutes it ran, or null (510-B).</summary>
    public int? Minutes { get; init; }

    /// <summary>The run's log, or empty (510-B).</summary>
    public string Log { get; init; } = "";
}
