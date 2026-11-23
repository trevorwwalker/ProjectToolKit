namespace Scheduler.Core.Accounting;

/// <summary>
/// 1030-A: the runs of one card type (<c>feature</c>, <c>bug</c> or "" untyped), the figure a later
/// page card draws; here it is only exposed.
/// </summary>
public sealed record TypeTotal
{
    /// <summary>The card type; "" for untyped cards.</summary>
    public required string CardType { get; init; }

    /// <summary>How many runs.</summary>
    public required int Runs { get; init; }

    /// <summary>Their minutes together.</summary>
    public required int Minutes { get; init; }

    /// <summary>The distinct cards (by id) that ran.</summary>
    public required int Cards { get; init; }
}
