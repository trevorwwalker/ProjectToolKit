// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Moves;

/// <summary>
/// A card change from the page (A10), worked out from main's copy of the card and ready for Host
/// (250-B) to commit through the answers worktree: the card's new text, where it ends up, and the
/// commit message. When <see cref="Error"/> is set nothing is to be committed.
/// </summary>
public sealed record CardEdit
{
    /// <summary>Why the change cannot be made, as the page shows it; empty when it can.</summary>
    public string Error { get; init; } = "";

    /// <summary>True when there is a change to commit.</summary>
    public bool Ok => Error.Length == 0;

    /// <summary>The card's id (<c>010-A</c>), for the retry count.</summary>
    public string CardId { get; init; } = "";

    /// <summary>The card's path on main now.</summary>
    public string Path { get; init; } = "";

    /// <summary>The card's path after the change: <see cref="Path"/> unless it moves.</summary>
    public string NewPath { get; init; } = "";

    /// <summary>The folder the card is in after the change.</summary>
    public CardFolder Folder { get; init; }

    /// <summary>True when the card moves to another folder.</summary>
    public bool Moves => Path != NewPath;

    /// <summary>The card's whole new text, with main's line endings.</summary>
    public string Text { get; init; } = "";

    /// <summary>The Status the change sets, or empty when the Status line is left as it was.</summary>
    public string Status { get; init; } = "";

    /// <summary>The commit message.</summary>
    public string Message { get; init; } = "";

    /// <summary>The scheduler log's line once the commit is pushed.</summary>
    public string Log { get; init; } = "";

    /// <summary>
    /// True when a pushed commit clears the card's retry count (MOVE-7: resume and send back of
    /// active and review cards); <see cref="CardMoves.Committed"/> applies it.
    /// </summary>
    public bool ClearsRetries { get; init; }

    /// <summary>The change as one commit for <see cref="IJobsCommitter"/>: the move, then the new text.</summary>
    public JobsChange Change => new()
    {
        Moves = Moves ? [new FileMove { From = Path, To = NewPath }] : [],
        Writes = new Dictionary<string, string>(StringComparer.Ordinal) { [NewPath] = Text },
        Message = Message,
    };

    /// <summary>A change that cannot be made.</summary>
    public static CardEdit Failed(string error) => new() { Error = error };
}
