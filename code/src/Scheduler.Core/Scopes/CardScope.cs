// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Scopes;

/// <summary>
/// A card's whole scope for dispatch (spec B2 SC-1, SC-4; job card 560-A): its Scope line, else its
/// area's default (the category's owns minus excepts in categories.json), plus the paths on its Locks line.
/// </summary>
public static class CardScope
{
    /// <summary>
    /// The card's scope, or null when it has none (no Scope line, no area in categories.json and no
    /// Locks paths): such a card is not started (SC-1). When categories.json has not been read at all,
    /// the area letter itself is the default scope, so one card per area runs, as categories did.
    /// </summary>
    public static Scope? Of(Card card, CategoriesFile? categories)
    {
        ArgumentNullException.ThrowIfNull(card);
        var own = ScopeLine.ParseValue(card.ScopeLine);
        var scope = own is null && categories is null ? AreaLetter(card.Category) : AreaScope.For(card, own, categories);
        var locks = ScopeLine.ParseValue(card.LocksLine);
        if (locks is null)
        {
            return scope;
        }

        if (scope is null)
        {
            return locks;
        }

        var paths = scope.Paths.Concat(locks.Paths.Where(p => !scope.Paths.Contains(p, StringComparer.Ordinal))).ToList();

        // A locked path is changed even where the area's except list leaves it out: a lock that
        // could reach into an except entry, glob or not, drops that entry.
        var except = scope.Except.Where(e => !locks.Paths.Any(l => Related(e, l))).ToList();
        var problems = new[] { scope.Problem, locks.Problem }.Where(p => p is not null).ToList();
        return new Scope(paths, except)
        {
            AddOnly = [.. scope.AddOnly, .. locks.AddOnly],
            Repo = scope.Repo,
            Problem = problems.Count == 0 ? null : string.Join("; ", problems),
        };
    }

    /// <summary>The scope that stands for a whole area when there is no categories.json (SC-4).</summary>
    public static Scope AreaLetter(string category) => Scope.Of($"area {category}");

    private static bool Related(string a, string b) => PathGlob.MayOverlap(a, b);
}
