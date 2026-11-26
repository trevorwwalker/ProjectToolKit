// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Dispatch;

/// <summary>
/// The first half of a dispatch decision (DISP-1 to DISP-14; schedule.ps1 L2867-2923): the
/// candidates, the note for every card that is not one, and the stuck cards.
/// </summary>
public sealed record DispatchPlan
{
    /// <summary>Ids of the cards in done/ or review/ (DISP-1).</summary>
    public required IReadOnlySet<string> Finished { get; init; }

    /// <summary>
    /// Every category taken this tick, with who took it: <c>worker N</c> (DISP-1), <c>orphan &lt;id&gt;</c>
    /// (DISP-8) or <c>next: &lt;id&gt;</c> (DISP-13). Case-insensitive (G-1).
    /// </summary>
    public required IReadOnlyDictionary<string, string> Busy { get; init; }

    /// <summary>The candidates as they were found: orphans by Num, then queued cards by Prio, Num, Category.</summary>
    public required IReadOnlyList<DispatchCandidate> Candidates { get; init; }

    /// <summary>The candidates in the order free workers take them: Prio, then orphans first, then Num (DISP-14).</summary>
    public required IReadOnlyList<DispatchCandidate> Ordered { get; init; }

    /// <summary>The notes, in the order they were made.</summary>
    public required IReadOnlyList<DispatchNote> Notes { get; init; }

    /// <summary>The stuck cards, for Waiting on you with Retry (DISP-7, DISP-11).</summary>
    public required IReadOnlyList<Card> Stuck { get; init; }

    /// <summary>
    /// 1472-A: the plan as a <c>paused-red</c> project offers it: only the candidates whose card says <c>Fixes-red: yes</c>.
    /// Every other rule was applied when the plan was made, so a held card (Depends on, scope, Locks) is not here to offer.
    /// </summary>
    public DispatchPlan OnlyFixesRed() => this with
    {
        Candidates = [.. Candidates.Where(c => c.Card.FixesRed)],
        Ordered = [.. Ordered.Where(c => c.Card.FixesRed)],
    };
}
