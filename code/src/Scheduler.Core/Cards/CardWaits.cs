// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Cards;

/// <summary>
/// The two kinds of card that wait for a person rather than for a worker (CARD-15, CARD-16): a
/// faithful port of <c>Waits-On-Dev</c> and <c>Waits-On-Planner</c> (schedule.ps1 L552, L558).
/// Only cards in queue/ and active/ wait - a review card is finished and a done card is gone.
/// </summary>
public static class CardWaits
{
    /// <summary>
    /// The Status of a card waiting on the developer (L551): <c>waiting:</c> then developer, dev,
    /// you or human as a whole word, with an optional "the". The agent CLI's own matching is
    /// case-insensitive (G-1), so <c>Waiting: YOU</c> counts.
    /// </summary>
    public const string DeveloperPattern = @"^waiting:\s*(the\s+)?(developer|dev|you|human)\b";

    /// <summary>The Status of a card waiting on the planner (L557).</summary>
    public const string PlannerPattern = @"^waiting:\s*(the\s+)?planner\b";

    /// <summary><c>Waits-On-Dev</c>: queue/ or active/ and <see cref="DeveloperPattern"/>.</summary>
    public static bool OnDeveloper(Card card) =>
        IsQueuedOrClaimed(card) && Text.IsMatch(card.Status, DeveloperPattern);

    /// <summary><c>Waits-On-Planner</c>: queue/ or active/ and <see cref="PlannerPattern"/>.</summary>
    public static bool OnPlanner(Card card) =>
        IsQueuedOrClaimed(card) && Text.IsMatch(card.Status, PlannerPattern);

    private static bool IsQueuedOrClaimed(Card card) =>
        card.Folder is CardFolder.Queue or CardFolder.Active;
}
