using Scheduler.Core.Contracts;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Categories;

/// <summary>Overlap warnings between categories (LANE-4, DISP-25, SC-8; <c>Category-Overlaps</c> L1659). Shown, never enforced.</summary>
public static class CategoryOverlaps
{
    /// <summary>
    /// Paths two categories both own, judged by the same test as scopes: <see cref="ScopeOverlap.Shared"/>
    /// asks whether one path can match both entries, so a glob is seen as the files it matches
    /// (<c>vm/68000.*</c> shares <c>vm/68000.cpp</c>, <c>vm/</c> shares <c>vm/*.h</c>) and two disjoint
    /// patterns share nothing. A path in categories.json's shared add-only list leaves the pair out (SC-5),
    /// and so does an <c>except</c> entry of either category that can be shown to cover one side of the pair,
    /// which is the same test the dispatch check makes an <c>except</c> pass before it can separate two
    /// cards (<see cref="PathGlob.Covers"/>, DISP-25, as the developer decided it for this card): a glob
    /// <c>except</c> that could name the disputed path but cannot be shown to cover it leaves the warning
    /// up, so the category has to write the <c>except</c> that does. This retires
    /// known gap C12, which treated a glob entry as no folder at all (LANE-4 as updated for card
    /// 977-A, 2026-11-19).
    /// </summary>
    public static IReadOnlyList<string> Find(CategoriesFile categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        var output = new List<string>();
        foreach (var a in categories.Categories.Values)
        {
            foreach (var b in categories.Categories.Values)
            {
                // One warning per pair of categories, whichever order the file lists them in.
                if (Text.SortComparer.Compare(a.Letter, b.Letter) >= 0)
                {
                    continue;
                }

                foreach (var pa in a.Owns)
                {
                    foreach (var pb in b.Owns)
                    {
                        // The scope test, one entry pair at a time so the warning can name both entries.
                        if (ScopeOverlap.Shared(Scope.Of(pa), Scope.Of(pb), categories.AddOnly) is null
                            || Excepted(a, pa, pb)
                            || Excepted(b, pa, pb))
                        {
                            continue;
                        }

                        if (Text.Same(pa, pb))
                        {
                            output.Add($"{pa} is owned by both {a.Letter} and {b.Letter}");
                        }
                        else if (PathGlob.Covers(pa, pb))
                        {
                            output.Add($"{a.Letter} owns {pa}, which holds {b.Letter}'s {pb} (add it to {a.Letter}'s except)");
                        }
                        else if (PathGlob.Covers(pb, pa))
                        {
                            output.Add($"{b.Letter} owns {pb}, which holds {a.Letter}'s {pa} (add it to {b.Letter}'s except)");
                        }
                        else
                        {
                            output.Add($"{a.Letter}'s {pa} and {b.Letter}'s {pb} can match the same path (give one of them an except)");
                        }
                    }
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Whether this category has an <c>except</c> entry shown to cover one side of the pair - the rule
    /// <see cref="ScopeOverlap"/> uses (DISP-25), so a glob <c>except</c> must name a side, not merely
    /// be able to match it.
    /// </summary>
    private static bool Excepted(CategoryDefinition category, string pa, string pb) =>
        category.Except.Any(e => PathGlob.Covers(e, pa) || PathGlob.Covers(e, pb));
}
