// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;

namespace Scheduler.Core.Scopes;

/// <summary>
/// The scheduler's scope check at dispatch (spec B2 SC-2, SC-3, SC-7): a candidate whose scope
/// overlaps a running card's, or an earlier candidate's, is not offered and gets a note naming
/// the card it waits for. Pure: the plan and the scopes come in, the gated plan goes out.
/// </summary>
public static class ScopeGate
{
    /// <summary>
    /// The plan with its ordered candidates checked in order. As with categories today (DISP-13, DISP-15),
    /// an earlier candidate reserves its scope whether or not a worker then takes it.
    /// </summary>
    /// <param name="plan">The project's dispatch plan.</param>
    /// <param name="scopeOf">Each candidate's scope (its own, or its area's: <see cref="AreaScope.Resolve"/>);
    /// null when it has neither, and then it is not offered (SC-1).</param>
    /// <param name="running">The scope of each running card, by card id.</param>
    /// <param name="addOnly">categories.json's shared add-only paths, where overlap is allowed (SC-5).</param>
    public static DispatchPlan Apply(
        DispatchPlan plan,
        Func<Card, Scope?> scopeOf,
        IReadOnlyDictionary<string, Scope> running,
        IReadOnlyList<string>? addOnly = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(scopeOf);
        ArgumentNullException.ThrowIfNull(running);

        var held = running.Select(r => (Id: r.Key, Scope: r.Value)).ToList();
        var ordered = new List<DispatchCandidate>();
        var notes = new List<DispatchNote>(plan.Notes);
        foreach (var candidate in plan.Ordered)
        {
            var scope = scopeOf(candidate.Card);
            if (scope is null || scope.Problem is not null)
            {
                notes.Add(new DispatchNote(candidate.Card.Name, scope is null
                    ? "not started: its scope is missing (no Scope line and no area)"
                    : $"not started: {scope.Problem}"));
                continue;
            }

            var clash = held
                .Select(h => (h.Id, Path: ScopeOverlap.Shared(scope, h.Scope, addOnly)))
                .FirstOrDefault(h => h.Path is not null);
            if (clash.Path is not null)
            {
                notes.Add(new DispatchNote(candidate.Card.Name, $"waits: overlaps {clash.Id} on {clash.Path}"));   // item 7 of job card 560-A
                continue;
            }

            ordered.Add(candidate);
            held.Add((candidate.Card.Id, scope));
        }

        var kept = ordered.Select(c => c.Card.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return plan with
        {
            Candidates = [.. plan.Candidates.Where(c => kept.Contains(c.Card.Id))],   // 560-A: a gated card is no candidate
            Ordered = ordered,
            Notes = notes,
        };
    }

    /// <summary>
    /// Dynamic locks (design <c>dynamic-locks.md</c>, DL-1, DL-10, DL-22): the plan gated again, with each
    /// running card's <em>held set</em> in place of its declared scope, every other card the repository's
    /// lock table holds (another project sharing the repository) counted as running, and each candidate's
    /// scope grown by its remembered set. With an empty table it gates as <see cref="Apply"/> did.
    /// </summary>
    /// <param name="plan">The plan <see cref="Apply"/> already gated on declared scopes.</param>
    /// <param name="scopeOf">Each candidate's declared scope, as for <see cref="Apply"/>.</param>
    /// <param name="running">The declared scope of each running or starting card, by card id.</param>
    /// <param name="names">Each running card's file name (the lock table's key), by card id.</param>
    /// <param name="table">The repository's lock table.</param>
    /// <param name="addOnly">categories.json's shared add-only paths (SC-5, DL-15).</param>
    public static DispatchPlan ApplyLocks(
        DispatchPlan plan,
        Func<Card, Scope?> scopeOf,
        IReadOnlyDictionary<string, Scope> running,
        IReadOnlyDictionary<string, string> names,
        Locks.LockTable table,
        IReadOnlyList<string>? addOnly = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(scopeOf);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(table);

        var held = new Dictionary<string, Scope>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, scope) in running)
        {
            var row = names.TryGetValue(id, out var name) ? table.Find(name) : null;
            held[id] = row is { Holds: true } ? Grown(scope, row.Held.Select(e => e.Path)) : scope;
        }

        var mine = names.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Holding.Where(r => !mine.Contains(r.Card)))
        {
            held[row.Card] = row.HeldScope;   // DL-22: a card of another project in the same repository
        }

        var gated = Apply(plan, c => WithRemembered(scopeOf(c), table.Find(c.Name)), held, addOnly);
        var kept = gated.Ordered.Select(c => c.Card.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gone = plan.Ordered.Select(c => c.Card.Id).Where(id => !kept.Contains(id)).ToList();
        if (gone.Count == 0)
        {
            return gated;
        }

        // A card gated out here no longer stands as its category's next card (DispatchPlanner.Plan's Busy).
        return gated with
        {
            Busy = plan.Busy
                .Where(b => !gone.Any(id => b.Value == $"next: {id}" || b.Value == $"orphan {id}"))
                .ToDictionary(b => b.Key, b => b.Value, Text.Comparer),
        };
    }

    /// <summary>DL-1, DL-10: a card's initial claim is its declared scope plus its remembered set.</summary>
    public static Scope? WithRemembered(Scope? scope, Locks.CardLocks? row) =>
        scope is null || row is null || row.Holds || row.Remembered.Count == 0 ? scope : Grown(scope, row.Remembered);

    private static Scope Grown(Scope scope, IEnumerable<string> paths)
    {
        var all = scope.Paths.Concat(paths).Distinct(StringComparer.Ordinal).ToList();
        return all.Count == scope.Paths.Count ? scope : scope with { Paths = all };
    }
}
