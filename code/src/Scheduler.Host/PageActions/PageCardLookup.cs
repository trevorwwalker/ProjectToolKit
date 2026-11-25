// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;

namespace Scheduler.Host.PageActions;

/// <summary>
/// 1113-C: finds the card a page link names: by its file name without <c>.md</c> or by its short id
/// (<c>1091-C</c>), among the open cards first and then the done/ and archive/ cards of the model of main.
/// </summary>
public static class PageCardLookup
{
    /// <summary>A card a link found.</summary>
    public sealed record Found(string Name, string Id, string Path, string Title, string Status, IReadOnlyList<CardQuestion> Questions);

    /// <summary>The card named <paramref name="name"/> (no case), or null when there is none.</summary>
    public static Found? Find(IEnumerable<Card> open, IEnumerable<DoneCard> done, string name)
    {
        var key = (name ?? "").Trim();
        if (key.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            key = key[..^3];
        }

        if (key.Length == 0)
        {
            return null;
        }

        var card = open.FirstOrDefault(c => Same(c.Name, key)) ?? open.FirstOrDefault(c => Same(c.Id, key));
        if (card is not null)
        {
            return new Found(card.Name, card.Id, card.Path, card.Title, card.Status, card.Questions);
        }

        var finished = done.FirstOrDefault(c => Same(c.Name, key)) ?? done.FirstOrDefault(c => Same(c.Id, key));
        return finished is null ? null : new Found(finished.Name, finished.Id, finished.Path, finished.Title, finished.Status, []);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
