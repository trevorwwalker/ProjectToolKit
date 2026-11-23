using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.PageModel;

/// <summary>
/// The rows of the Queue window, in the order the queue will run (PAGE-6): a faithful port of the
/// queue window of schedule.ps1 (L3651-3668) and <c>Order-Queue</c> (L3149). Orphans come first by
/// Num, then the queued cards by Prio, Num and Category, a card never placed before a queued card it
/// depends on - and when every card left waits on another, the cycle is broken by that order.
/// Cards waiting on the developer or the planner, a card a worker is running, and a <c>queue/</c>
/// copy of a card that is also in another folder are left out.
/// </summary>
public static class PageQueue
{
    /// <summary>The note beside an orphan the dispatch plan has no note for (L3655).</summary>
    public const string OrphanNote = "claimed earlier; resumes when its category is free";

    /// <summary>
    /// The window's rows. <paramref name="notes"/> is the dispatch plan's notes keyed by card file
    /// name; a card with no note shows <see cref="OrphanNote"/> if it is an orphan, or its own
    /// Status if it is still in the queue.
    /// </summary>
    public static IReadOnlyList<PageQueueItem> Order(
        IReadOnlyList<Card> cards,
        IReadOnlyCollection<RunningWorker> running,
        IReadOnlyDictionary<string, string>? notes = null)
    {
        var runningNames = new HashSet<string>(running.Select(w => w.Card), Text.Comparer);
        var runningIds = new HashSet<string>(running.Select(w => w.Id), Text.Comparer);

        // Orphans: claimed, not parked as needs-human, and no worker is on them (L3652). A card
        // waiting on the developer is NOT filtered here, which is what shows it twice (spec C15).
        var orphans = cards
            .Where(c => c.Folder == CardFolder.Active
                && !Text.IsMatch(c.Status, "^needs-human")
                && !runningNames.Contains(c.Name))
            .OrderBy(c => c.Num)
            .ToList();

        // A queue/ copy of a card that is also in another folder is stale (Queue-Copies L2749).
        var elsewhere = cards.Where(c => c.Folder != CardFolder.Queue).Select(c => c.Id).ToHashSet(Text.Comparer);
        var queued = cards
            .Where(c => c.Folder == CardFolder.Queue && !elsewhere.Contains(c.Id) && !runningIds.Contains(c.Id))
            .ToList();

        // Ordered first, then the cards that have their own window are dropped, as at L3656.
        var inOrder = OrderByDependencies(queued)
            .Where(c => !CardWaits.OnPlanner(c) && !CardWaits.OnDeveloper(c))
            .ToList();

        var items = new List<PageQueueItem>(orphans.Count + inOrder.Count);
        foreach (var c in orphans.Concat(inOrder))
        {
            var note = (notes is not null && notes.TryGetValue(c.Name, out var planned)) ? planned
                : c.Folder == CardFolder.Active ? OrphanNote
                : c.Status;
            items.Add(new PageQueueItem { Name = c.Name, Title = c.Title, Note = note, CardType = c.CardType, PlanPath = c.PlanPath, PlanStep = c.PlanStep });
        }

        return items;
    }

    /// <summary>
    /// <c>Order-Queue</c> (L3149): the cards by Prio, Num and Category, each taken as soon as every
    /// card it depends on that is also in this list has been placed. When none can be taken the
    /// cycle is broken by taking the first in that order.
    /// </summary>
    private static List<Card> OrderByDependencies(List<Card> queue)
    {
        var ids = queue.Select(c => c.Id).ToHashSet(Text.Comparer);
        var placed = new HashSet<string>(Text.Comparer);
        var left = queue
            .OrderBy(c => c.Prio)
            .ThenBy(c => c.Num)
            .ThenBy(c => c.Category, Text.SortComparer)
            .ToList();
        var outList = new List<Card>(left.Count);
        while (left.Count > 0)
        {
            var next = left.FirstOrDefault(c => !c.Depends.Any(d => ids.Contains(d) && !placed.Contains(d))) ?? left[0];
            outList.Add(next);
            placed.Add(next.Id);
            left.RemoveAll(c => Text.Same(c.Id, next.Id));
        }

        return outList;
    }
}
