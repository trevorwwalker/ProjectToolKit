// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Scopes;

/// <summary>
/// Whether two scopes overlap (spec B2 SC-2, SC-3, SC-5; B3 Q5-Q7 as answered on card 320-A, and
/// Q7 as the developer reversed it on card 961-A). A folder (ending in <c>/</c>) covers every path
/// inside it; a file only itself; a glob every path it matches (<see cref="PathGlob"/>), so
/// <c>vm/68000.*</c> shares <c>vm/68000.cpp</c>. The shared path is the narrower of the two; it is
/// no overlap when either scope's except list covers it, or when it is add-only (in categories.json's
/// shared list, or marked add-only on both scopes). Scopes in different repositories never overlap;
/// projects sharing one compare their paths in it.
/// </summary>
public static class ScopeOverlap
{
    /// <summary>
    /// The first path the two scopes both change, or null when they share none.
    /// </summary>
    /// <param name="a">One scope.</param>
    /// <param name="b">The other.</param>
    /// <param name="addOnly">categories.json's shared add-only paths (SC-5), which any card may add to.</param>
    public static string? Shared(Scope a, Scope b, IReadOnlyList<string>? addOnly = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (!SameRepo(a.Repo, b.Repo))
        {
            return null;
        }

        foreach (var pa in a.Paths)
        {
            foreach (var pb in b.Paths)
            {
                if (!PathGlob.MayOverlap(pa, pb))
                {
                    continue;
                }

                var shared = Narrower(pa, pb);
                if (a.Except.Any(e => LeavesOut(e, pa, pb))
                    || b.Except.Any(e => LeavesOut(e, pa, pb))
                    || (addOnly ?? []).Contains(shared, StringComparer.Ordinal)
                    || (a.AddOnly.Contains(shared, StringComparer.Ordinal) && b.AddOnly.Contains(shared, StringComparer.Ordinal)))
                {
                    continue;
                }

                return shared;
            }
        }

        return null;
    }

    /// <summary>
    /// The entry to name in the note: the concrete one of the two, or, of two globs, the one with the
    /// fewest wildcards.
    /// </summary>
    private static string Narrower(string pa, string pb)
    {
        var ga = PathGlob.IsGlob(pa);
        var gb = PathGlob.IsGlob(pb);
        if (ga != gb)
        {
            return ga ? pb : pa;
        }

        if (!ga)
        {
            return PathGlob.Covers(pa, pb) ? pb : pa;
        }

        var wa = pa.Count(c => c is '*' or '?');
        var wb = pb.Count(c => c is '*' or '?');
        return wa != wb ? (wa < wb ? pa : pb) : (string.CompareOrdinal(pa, pb) <= 0 ? pa : pb);
    }

    /// <summary>An <c>except</c> entry leaves a pair out when it covers either side of it.</summary>
    private static bool LeavesOut(string except, string pa, string pb) =>
        PathGlob.Covers(except, pa) || PathGlob.Covers(except, pb);

    private static bool SameRepo(string a, string b) =>
        a.Length == 0 || b.Length == 0 || string.Equals(Normal(a), Normal(b), StringComparison.OrdinalIgnoreCase);

    private static string Normal(string repo) => repo.Replace('\\', '/').TrimEnd('/');
}
