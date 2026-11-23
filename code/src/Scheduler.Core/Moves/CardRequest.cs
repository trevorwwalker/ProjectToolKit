using Scheduler.Core.Contracts;

namespace Scheduler.Core.Moves;

/// <summary>
/// One request to move a card or change its Status, as a worker (or the scheduler's own claim)
/// wrote it into the moves folder (card 660-B; design in <c>card-moves.md</c> beside this file).
/// </summary>
public sealed record CardRequest
{
    /// <summary>The request file's name, which orders it.</summary>
    public string FileName { get; init; } = "";

    /// <summary>Why the request cannot be read, or empty.</summary>
    public string Error { get; init; } = "";

    /// <summary>True when the request could be read.</summary>
    public bool Ok => Error.Length == 0;

    /// <summary>The card's file name without <c>.md</c>.</summary>
    public string Card { get; init; } = "";

    /// <summary>Where the writer believes the card is.</summary>
    public CardFolder From { get; init; } = CardFolder.Active;

    /// <summary>Where the card goes.</summary>
    public CardFolder To { get; init; }

    /// <summary>The slot that asked, or null.</summary>
    public int? Slot { get; init; }

    /// <summary>The new Status for a request with no text, or empty.</summary>
    public string Status { get; init; } = "";

    /// <summary>True for the scheduler's own claim of a card it starts a worker on.</summary>
    public bool Claim { get; init; }

    /// <summary>The card's whole new text, or null to keep main's.</summary>
    public string? Text { get; init; }

    /// <summary>A request that cannot be read.</summary>
    public static CardRequest Failed(string fileName, string error) => new() { FileName = fileName, Error = error };
}
