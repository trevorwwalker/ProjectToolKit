// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Cards;

/// <summary>
/// Whether a card may run yet (CARD-17, <c>Deps-Done</c> L559) and which of its dependencies are
/// holding it (DISP-12, L2909-2910). A dependency is finished only when a card with that id is in
/// done/: a card in review/, active/ or queue/ keeps its dependents waiting (1059-B, developer
/// 2026-11-22 - a review card still has open questions, so work built on it may have to change).
/// The check looks each id up once and never follows a dependency of a dependency, so two cards
/// that depend on each other both wait forever instead of the scheduler looping - which is what
/// L559 does and what makes a cycle safe rather than fatal.
/// </summary>
public static class CardDependencies
{
    /// <summary>
    /// Cards indexed by Id, as the script's <c>$byIdS</c> hashtable does it (L3017): the last card
    /// with an id is the one kept, and ids compare as PowerShell compares hashtable keys (G-1).
    /// </summary>
    public static IReadOnlyDictionary<string, Card> ById(IEnumerable<Card> cards)
    {
        var byId = new Dictionary<string, Card>(Text.Comparer);
        foreach (var card in cards)
        {
            byId[card.Id] = card;
        }

        return byId;
    }

    /// <summary>
    /// <c>Deps-Done</c>: every id on the card's last <c>Depends on:</c> line is a card in done/.
    /// A card with no dependencies depends on nothing and is done to wait on.
    /// </summary>
    public static bool Done(Card card, IReadOnlyDictionary<string, Card> byId) =>
        Unfinished(card, byId).Count == 0;

    /// <summary>
    /// The dependency ids that are not finished, in the order the card names them - the list the
    /// page puts after "waits for " (DISP-12).
    /// </summary>
    public static IReadOnlyList<string> Unfinished(Card card, IReadOnlyDictionary<string, Card> byId) =>
        [.. card.Depends.Where(id => !IsFinished(id, byId))];

    private static bool IsFinished(string id, IReadOnlyDictionary<string, Card> byId) =>
        byId.TryGetValue(id, out var dependency) && dependency.Folder == CardFolder.Done;
}
