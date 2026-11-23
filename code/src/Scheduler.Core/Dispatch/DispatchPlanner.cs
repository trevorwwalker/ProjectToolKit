using Scheduler.Core.Contracts;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Dispatch;

/// <summary>
/// Builds a <see cref="DispatchPlan"/> from the cards, the running workers, the retry counts,
/// categories.json and maxRetries (DISP-1 to DISP-14, L2867-2923), with categories replaced by per-card
/// scopes (spec B2 SC-1 to SC-5, job card 560-A): a card starts when its scope overlaps no running
/// card's and no earlier candidate's (<see cref="ScopeGate"/>). Which slot each candidate goes to is
/// DISP-15.
/// </summary>
public static class DispatchPlanner
{
    /// <summary>The ids of the cards in done/ (DISP-1, L2867). A card in review/ does not release
    /// its dependents: it still waits for the developer (1059-B).</summary>
    public static IReadOnlySet<string> Finished(IEnumerable<Card> cards) =>
        cards.Where(c => c.Folder == CardFolder.Done)
            .Select(c => c.Id)
            .ToHashSet(Text.Comparer);

    /// <summary>The category of every running worker's card, with <c>worker N</c> (DISP-1, L2870).</summary>
    public static Dictionary<string, string> BusyCategories(IEnumerable<RunningWorker> running)
    {
        var busy = new Dictionary<string, string>(Text.Comparer);
        foreach (var w in running)
        {
            busy[w.Category] = $"worker {w.Slot}";
        }

        return busy;
    }

    /// <summary>
    /// The scope of each running worker's card, by card id (SC-2): its card's scope when the card
    /// is still in the list, else its area's default.
    /// </summary>
    public static Dictionary<string, Scope> RunningScopes(
        IReadOnlyList<Card> cards, IEnumerable<RunningWorker> running, CategoriesFile? categories)
    {
        var scopes = new Dictionary<string, Scope>(Text.Comparer);
        foreach (var w in running)
        {
            var card = cards.FirstOrDefault(c => Text.Same(c.Id, w.Id) && c.Folder == CardFolder.Active)
                ?? cards.FirstOrDefault(c => Text.Same(c.Id, w.Id));
            var scope = card is null ? null : CardScope.Of(card, categories);
            scope ??= categories is not null && categories.Categories.TryGetValue(w.Category, out var area)
                ? new Scope(area.Owns, area.Except)
                : CardScope.AreaLetter(w.Category);
            scopes[w.Id] = scope with { Problem = null };
        }

        return scopes;
    }

    /// <summary>
    /// Orphans first (DISP-2 to DISP-8), then queued cards (DISP-9 to DISP-13); the candidates
    /// also in the order free workers take them (DISP-14), each started only when its scope is
    /// free (SC-2, SC-3). There are no held categories: a redefined area's default simply applies
    /// (320-A Q5).
    /// </summary>
    /// <param name="cards">Every card.</param>
    /// <param name="running">The running workers.</param>
    /// <param name="retries">The retry count of each card, by id.</param>
    /// <param name="categories">categories.json as read now, for the areas' default scopes and the shared
    /// add-only paths; null when it has not been read (then each area letter is its own scope).</param>
    /// <param name="maxRetries">The config's maxRetries.</param>
    public static DispatchPlan Plan(
        IReadOnlyList<Card> cards,
        IReadOnlyCollection<RunningWorker> running,
        IReadOnlyDictionary<string, int> retries,
        CategoriesFile? categories,
        int maxRetries)
    {
        var finished = Finished(cards);
        var busy = BusyCategories(running);
        var runningIds = running.Select(w => w.Id).ToHashSet(Text.Comparer);
        var tries = new Dictionary<string, int>(Text.Comparer);
        foreach (var pair in retries)
        {
            tries[pair.Key] = pair.Value;
        }

        int Retries(Card c) => tries.GetValueOrDefault(c.Id);
        List<string> Missing(Card c) => [.. c.Depends.Where(d => !finished.Contains(d))];

        var candidates = new List<DispatchCandidate>();
        var notes = new List<DispatchNote>();
        var stuck = new List<Card>();

        // DISP-9: a queue/ copy of a card that is also in another folder is stale (Queue-Copies).
        var elsewhere = cards.Where(c => c.Folder != CardFolder.Queue).Select(c => c.Id).ToHashSet(Text.Comparer);

        // Orphans first: claimed, not parked, nobody working on them (L2874).
        foreach (var c in cards.Where(c => c.Folder == CardFolder.Active && !Text.IsMatch(c.Status, "^needs-human")).OrderBy(c => c.Num))
        {
            if (runningIds.Contains(c.Id))
            {
                continue;   // DISP-3: a worker has it.
            }

            // DISP-4: sent back as waiting, or blocked: not an orphan, does not hold its scope.
            var missing = Missing(c);
            if (Text.IsMatch(c.Status, "^waiting") || missing.Count > 0)
            {
                notes.Add(new(c.Name, missing.Count > 0 ? $"claimed; waits for {string.Join(", ", missing)}" : $"claimed; {c.Status}"));
                continue;
            }

            // DISP-7, checked before DISP-6 (1082-A): stuck; treated as parked, so it does not hold its
            // scope. A stuck orphan behind a higher-priority card still needs the developer, so it is
            // listed under Waiting on you rather than only noted as waiting behind that card.
            if (Retries(c) >= maxRetries)
            {
                notes.Add(new(c.Name, StuckNote(Retries(c))));
                stuck.Add(c);
                continue;
            }

            // DISP-5 (held categories) is gone (560-A, 320-A Q5).
            // DISP-6: a ready, unblocked queued card of higher priority whose scope overlaps goes
            // first (was: of the same category). Spec C3: stuck or held queued cards are not excluded
            // (L2886). A stale queue/ copy (DISP-9) is: it is never a candidate, so an orphan behind it,
            // or behind a stale copy of itself, would wait for ever (1082-A).
            var scope = CardScope.Of(c, categories);
            var ahead = scope is null ? null : cards.FirstOrDefault(q => q.Folder == CardFolder.Queue && q.Prio < c.Prio
                && !elsewhere.Contains(q.Id)
                && Text.IsMatch(q.Status, "^ready") && Missing(q).Count == 0
                && CardScope.Of(q, categories) is { } qs && ScopeOverlap.Shared(scope, qs, categories?.AddOnly) is not null);
            if (ahead is not null)
            {
                notes.Add(new(c.Name, $"orphan; waits behind higher-priority {ahead.Id}"));
                continue;
            }

            candidates.Add(new(c, true));   // DISP-8
        }

        // DISP-9: stale queue/ copies are skipped.
        var queued = cards.Where(c => c.Folder == CardFolder.Queue)
            .OrderBy(c => c.Prio).ThenBy(c => c.Num).ThenBy(c => c.Category, Text.SortComparer);
        foreach (var c in queued)
        {
            if (elsewhere.Contains(c.Id))
            {
                continue;
            }

            if (!Text.IsMatch(c.Status, "^ready"))
            {
                notes.Add(new(c.Name, c.Status));   // DISP-10
                continue;
            }

            if (Retries(c) >= maxRetries)
            {
                notes.Add(new(c.Name, StuckNote(Retries(c))));   // DISP-11
                stuck.Add(c);
                continue;
            }

            // DISP-12: dependencies; a busy scope is the gate's (SC-2).
            var missing = Missing(c);
            if (missing.Count > 0)
            {
                notes.Add(new(c.Name, $"waits for {string.Join(", ", missing)}"));
                continue;
            }

            candidates.Add(new(c, false));   // DISP-13
        }

        // DISP-14: highest priority first; within a priority, orphans before new cards (L2923).
        var ordered = candidates.OrderBy(x => x.Card.Prio).ThenBy(x => !x.Resume).ThenBy(x => x.Card.Num).ToList();
        var plan = new DispatchPlan
        {
            Finished = finished,
            Busy = busy,
            Candidates = candidates,
            Ordered = ordered,
            Notes = notes,
            Stuck = stuck,
        };

        // SC-2, SC-3, SC-5: in that order, each candidate reserves its scope; one that overlaps a
        // running or earlier card's waits, with a note naming that card (560-A item 7).
        var gated = ScopeGate.Apply(plan, c => CardScope.Of(c, categories), RunningScopes(cards, running, categories), categories?.AddOnly);
        foreach (var c in gated.Ordered)
        {
            busy.TryAdd(c.Card.Category, c.Resume ? $"orphan {c.Card.Id}" : $"next: {c.Card.Id}");
        }

        return gated;
    }

    /// <summary>
    /// A resume counts against the card's retries before the worker starts, so a failed start
    /// still counts (DISP-16, L2927). A new card's start does not count here.
    /// </summary>
    public static void CountResume(IDictionary<string, int> retries, DispatchCandidate candidate)
    {
        if (!candidate.Resume)
        {
            return;
        }

        // A count carried from before the clamp (spec C8) cannot go on being negative (940-A).
        retries[candidate.Card.Id] = Math.Max(0, retries.TryGetValue(candidate.Card.Id, out var n) ? n : 0) + 1;
    }

    /// <summary>
    /// The words that say a card is stuck, for the Queue and for Waiting on you (DISP-7, DISP-11):
    /// the runs counted against it are its runs that ended without a result (940-A, spec C2).
    /// </summary>
    public static string StuckNote(int runs) => $"stuck: {runs} runs ended without a result: needs you";
}
