// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Scopes;

/// <summary>The scope's part of the worker's prompt (spec B2 SC-5).</summary>
public static class ScopeBrief
{
    /// <summary>
    /// SC-4 (job card 560-A item 5): the worker's brief of what it may change: the card's scope
    /// (<see cref="CardScope.Of"/>), the shared add-only files, the add-only rule, and the card's
    /// area as a label; "" when the card has no scope.
    /// </summary>
    public static string For(Card card, CategoriesFile? categories, string categoriesRel)
    {
        ArgumentNullException.ThrowIfNull(card);
        var scope = CardScope.Of(card, categories);
        if (scope is null)
        {
            return "";
        }

        var shared = categories?.AddOnly ?? [];
        var t = $"You may change: {string.Join(", ", scope.Paths)}";
        if (scope.Except.Count > 0)
        {
            t += $"; but not: {string.Join(", ", scope.Except)}";
        }

        t += ".";
        if (shared.Count > 0)
        {
            t += $" Any card may add to (not rewrite): {string.Join(", ", shared)}.";
        }

        var rule = AddOnly(scope, shared);
        if (rule.Length > 0)
        {
            t += $" {rule}";
        }

        var name = categories is not null && categories.Categories.TryGetValue(card.Category, out var area) && area.Name.Length > 0
            ? $" ({area.Name})"
            : "";
        return $"{t} The card is in area {card.Category}{name}, a label for it, not a limit; areas are defined in {categoriesRel}.";
    }

    /// <summary>
    /// SC-5: the rule for the add-only paths in the scope (marked on the card, or in categories.json's
    /// shared add-only list), which other cards may change at the same time; empty when it has none.
    /// </summary>
    public static string AddOnly(Scope scope, IReadOnlyList<string> sharedAddOnly)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(sharedAddOnly);
        var paths = scope.Paths
            .Where(p => scope.AddOnly.Contains(p, StringComparer.Ordinal) || sharedAddOnly.Contains(p, StringComparer.Ordinal))
            .ToList();
        return paths.Count == 0
            ? ""
            : $"Other cards may change {string.Join(", ", paths)} at the same time: add to it, never rewrite it;"
                + " before each push, pull and rebase, resolve any conflict, and re-run the gates.";
    }
}
