using Scheduler.Core.Contracts;

namespace Scheduler.Core.PageModel;

/// <summary>
/// One card that 395-B's <c>FinishedCardCheck</c> flagged: its run ended <c>done</c> or <c>review</c>
/// and the card moved, but its text on main was the one last seen in <c>active/</c>, so the Result was
/// never committed (job card 410-A). Host passes one per entry of <c>FinishedCardCheck.Flagged</c>.
/// <see cref="Stamp"/> and <see cref="Folder"/> say what main showed at the moment it was flagged, so
/// the row leaves once the developer's Done or a fresh commit moves on from them; both may be left out,
/// in which case the row shows while the card still sits in <c>review/</c>. A card in <c>done/</c> never
/// shows the row (1054-B): it waits for nothing.
/// </summary>
public sealed record FinishedCardFlag
{
    /// <summary>The card file name without <c>.md</c>, as <see cref="Card.Name"/>.</summary>
    public required string Card { get; init; }

    /// <summary>The card's blob id on main when it was flagged (CARD-1), or empty when unknown.</summary>
    public string Stamp { get; init; } = "";

    /// <summary>The folder it was in when flagged, or null when Host has only the name.</summary>
    public CardFolder? Folder { get; init; }
}
