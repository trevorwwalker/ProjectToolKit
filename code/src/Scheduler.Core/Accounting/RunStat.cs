namespace Scheduler.Core.Accounting;

/// <summary>
/// One finished run as the control window's figures keep it in memory (ACCT-6, the
/// <c>runStats</c> list L861): what accounting.csv rows are read back into, and what a finished
/// run adds to them.
/// </summary>
public sealed record RunStat
{
    /// <summary>When the run finished; the figures cover the last 24 hours of these.</summary>
    public required DateTimeOffset Finished { get; init; }

    /// <summary>The result the worker ended with. Runs with no log, out of quota or a down server are left out.</summary>
    public required string Result { get; init; }

    /// <summary>Whole minutes it ran.</summary>
    public required int Minutes { get; init; }

    /// <summary>The model that ran, as the row's model column holds it.</summary>
    public required string Model { get; init; }

    /// <summary>1030-A: the card's type, "" for an old row or an untyped card.</summary>
    public string CardType { get; init; } = "";

    /// <summary>1030-A: the card's version header, "" when none.</summary>
    public string Version { get; init; } = "";

    /// <summary>The card's id, such as <c>010-A</c>. A run with no card is left out of the figures.</summary>
    public required string Card { get; init; }
}
