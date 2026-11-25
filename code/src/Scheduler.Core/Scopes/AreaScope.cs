// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;

namespace Scheduler.Core.Scopes;

/// <summary>
/// Areas, today's categories, as named default scopes and as the worker's brief of what it owns
/// (spec B2 SC-4, A4 LANE-5).
/// </summary>
public static class AreaScope
{
    /// <summary>
    /// SC-4: the card's own scope when it names one; otherwise its area's (the category of its file
    /// name): the area's owns minus its excepts. Null when it has neither.
    /// </summary>
    public static Scope? For(Card card, Scope? own, CategoriesFile? categories)
    {
        if (own is not null)
        {
            return own;
        }

        return categories is not null && categories.Categories.TryGetValue(card.Category, out var area)
            ? new Scope(area.Owns, area.Except)
            : null;
    }

    /// <summary>
    /// SC-1, SC-4: a card's scope from its text: its Scope line, or else its area's in the
    /// categories.json read now (Q10: a redefined area's default simply applies, nothing is held).
    /// </summary>
    public static Scope? Resolve(Card card, string cardText, CategoriesFile? categories) =>
        For(card, ScopeLine.Parse(cardText), categories);

    /// <summary>SC-4, LANE-5: the area's brief for the worker's prompt, as today's category brief.</summary>
    public static string Brief(CategoriesFile? categories, string area, string categoriesRel) => CategoryBrief.For(categories, area, categoriesRel);
}
