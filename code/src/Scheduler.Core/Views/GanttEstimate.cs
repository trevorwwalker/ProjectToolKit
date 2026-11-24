using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Views;

/// <summary>
/// One estimated bar of the Gantt (VW-19): when a card is expected to run, and why that long. A
/// waiting card (on the developer, the planner, or a dependency that never ends) has no times.
/// </summary>
public sealed record EstimatedBar
{
    public required string CardId { get; init; }

    /// <summary>Null when the card is waiting.</summary>
    public DateTimeOffset? Start { get; init; }

    /// <summary>Null when the card is waiting.</summary>
    public DateTimeOffset? End { get; init; }

    /// <summary><c>time box</c>, <c>typical &lt;model&gt;</c>, <c>max minutes</c>, or <c>waiting</c>.</summary>
    public required string Basis { get; init; }

    /// <summary>The cards it waited for: unfinished dependencies and the cards whose scope it shares.</summary>
    public IReadOnlyList<string> WaitedFor { get; init; } = [];

    public bool Waiting => Start is null;
}

/// <summary>
/// The schedule ahead (VW-19): a pure simulation of the free workers over the open cards. Queued
/// ready cards (and claimed orphans) are filled into <see cref="ViewsSnapshot.Workers"/> slots in
/// dispatch order (DISP-14), each starting at the latest of now, its dependencies' finish, the first
/// free slot and the end of every running or earlier-estimated card whose scope overlaps its own.
/// A running card past its typical gets the rest of its run. Cards waiting on the developer or the
/// planner, and in review, get no times, nor do their dependents. No I/O, no clock.
/// </summary>
public static class GanttEstimate
{
    public const string TimeBox = "time box";
    public const string MaxMinutes = "max minutes";
    public const string WaitingBasis = "waiting";

    /// <summary>The estimates, running cards first, then the candidates in dispatch order, then the waiting cards by number.</summary>
    /// <param name="snapshot">The project's views snapshot.</param>
    /// <param name="now">The time the schedule starts from.</param>
    /// <param name="maxMinutes">The scheduler's maxMinutes: the length when there is no time box and no typical.</param>
    /// <param name="addOnly">categories.json's shared add-only paths (SC-5), when known.</param>
    public static IReadOnlyList<EstimatedBar> Of(ViewsSnapshot snapshot, DateTimeOffset now, int maxMinutes, IReadOnlyList<string>? addOnly = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var cards = new Dictionary<string, ViewCard>(Text.Comparer);
        foreach (var c in snapshot.Cards)
        {
            // An open copy wins over a done one of the same id; queue/ loses to every other folder (DISP-9).
            if (!cards.TryGetValue(c.Id, out var had) || Rank(c.Folder) > Rank(had.Folder))
            {
                cards[c.Id] = c;
            }
        }

        var bars = new List<EstimatedBar>();
        var ends = new Dictionary<string, DateTimeOffset>(Text.Comparer);   // finish of every done, running or estimated card
        var waiting = new HashSet<string>(Text.Comparer);
        var busy = new List<(string Id, Scope Scope, DateTimeOffset End)>();
        var slotEnds = new List<DateTimeOffset>();

        foreach (var c in cards.Values.Where(c => c.Folder is ViewFolder.Done or ViewFolder.Archive))
        {
            var last = snapshot.Runs.Where(r => Text.Same(r.CardId, c.Id) && r.End is not null).Select(r => r.End!.Value);
            ends[c.Id] = last.DefaultIfEmpty(DateTimeOffset.MinValue).Max();
        }

        // The running cards hold their slots until their expected end.
        foreach (var run in snapshot.Runs.Where(r => r.Running).OrderBy(r => r.Start))
        {
            cards.TryGetValue(run.CardId, out var card);
            var category = card?.Category ?? "";
            var typical = Typical(snapshot.Typicals, category, run.Model) ?? Typical(snapshot.Typicals, category, card?.Worker ?? "");
            var limit = run.TimeBoxEnd ?? (card?.TimeBoxMinutes is int box ? run.Start.AddMinutes(box) : run.Start.AddMinutes(maxMinutes));
            var limitBasis = run.TimeBoxEnd is not null || card?.TimeBoxMinutes is not null ? TimeBox : MaxMinutes;
            DateTimeOffset end;
            if (typical is not null && run.Start.AddMinutes(typical.Median) <= now)
            {
                // Past its typical: the rest of the run, to the typical's end or the time box's when that is later.
                var typicalEnd = run.Start.AddMinutes(typical.Median);
                end = Max(now, Max(typicalEnd, limit));
                bars.Add(new EstimatedBar
                {
                    CardId = run.CardId,
                    Start = now,
                    End = end,
                    Basis = limit > typicalEnd ? limitBasis : "typical " + typical.Model,
                });
            }
            else
            {
                var (minutes, _) = card is null ? (maxMinutes, MaxMinutes) : Length(card, snapshot.Typicals, maxMinutes);
                end = Max(now, run.Start.AddMinutes(typical?.Median ?? minutes));
            }

            ends[run.CardId] = end;
            slotEnds.Add(end);
            if (card is not null)
            {
                busy.Add((card.Id, ScopeOf(card), end));
            }
        }

        // A cap below the running count frees a slot only when enough of them end: keep the latest.
        var workers = Math.Max(1, snapshot.Workers);
        slotEnds = [.. slotEnds.OrderByDescending(e => e).Take(workers)];
        while (slotEnds.Count < workers)
        {
            slotEnds.Add(now);
        }

        var open = cards.Values.Where(c => c.Folder is ViewFolder.Queue or ViewFolder.Active or ViewFolder.Review && !ends.ContainsKey(c.Id)).ToList();
        var candidates = new List<ViewCard>();
        var parked = new List<ViewCard>();
        foreach (var c in open)
        {
            if (c.Folder == ViewFolder.Review
                || Text.IsMatch(c.Status, "^(needs-human|waiting)")
                || (c.Folder == ViewFolder.Queue && !Text.IsMatch(c.Status, "^ready")))
            {
                parked.Add(c);
                waiting.Add(c.Id);
            }
            else
            {
                candidates.Add(c);
            }
        }

        var order = DispatchPlanner.InDispatchOrder(candidates, c => c.Priority, c => c.Folder == ViewFolder.Active, c => c.Number).ToList();
        var placed = new List<EstimatedBar>();
        var pending = new List<ViewCard>(order);
        while (pending.Count > 0)
        {
            // The first card in dispatch order whose dependencies are all settled goes next.
            var next = pending.FirstOrDefault(c => c.DependsOn.All(d => ends.ContainsKey(d) || waiting.Contains(d) || !IsPending(d)));
            if (next is null)
            {
                break;   // a dependency cycle: what is left waits.
            }

            pending.Remove(next);
            var blockedBy = next.DependsOn.Where(d => waiting.Contains(d) || (!ends.ContainsKey(d) && !cards.ContainsKey(d))).ToList();
            if (blockedBy.Count > 0)
            {
                waiting.Add(next.Id);
                placed.Add(new EstimatedBar { CardId = next.Id, Basis = WaitingBasis, WaitedFor = blockedBy });
                continue;
            }

            var waitedFor = next.DependsOn.Where(d => cards.TryGetValue(d, out var dc) && dc.Folder is not (ViewFolder.Done or ViewFolder.Archive)).ToList();
            var ready = next.DependsOn.Select(d => ends[d]).Append(now).Max();
            var scope = ScopeOf(next);
            foreach (var b in busy)
            {
                if (ScopeOverlap.Shared(scope, b.Scope, addOnly) is not null)
                {
                    ready = Max(ready, b.End);
                    if (!waitedFor.Contains(b.Id, Text.Comparer))
                    {
                        waitedFor.Add(b.Id);
                    }
                }
            }

            // Best fit: the latest slot already free by then, else the first to free up.
            var free = slotEnds.Where(e => e <= ready).ToList();
            var slot = free.Count > 0 ? slotEnds.IndexOf(free.Max()) : slotEnds.IndexOf(slotEnds.Min());
            var start = Max(ready, slotEnds[slot]);
            var (length, basis) = Length(next, snapshot.Typicals, maxMinutes);
            var end = start.AddMinutes(length);
            slotEnds[slot] = end;
            ends[next.Id] = end;
            busy.Add((next.Id, scope, end));
            placed.Add(new EstimatedBar { CardId = next.Id, Start = start, End = end, Basis = basis, WaitedFor = waitedFor });
        }

        foreach (var c in pending)
        {
            waiting.Add(c.Id);
            placed.Add(new EstimatedBar { CardId = c.Id, Basis = WaitingBasis, WaitedFor = [.. c.DependsOn.Where(d => !ends.ContainsKey(d))] });
        }

        bars.AddRange(placed.OrderBy(b => order.FindIndex(c => Text.Same(c.Id, b.CardId))));
        bars.AddRange(parked.OrderBy(c => c.Number).Select(c => new EstimatedBar { CardId = c.Id, Basis = WaitingBasis }));
        return bars;

        bool IsPending(string id) => pending.Any(p => Text.Same(p.Id, id));
    }

    /// <summary>The estimate's length (VW-19): the Time box, else the category's median for the model the Worker line picks, else maxMinutes.</summary>
    public static (int Minutes, string Basis) Length(ViewCard card, IReadOnlyList<AreaTypical> typicals, int maxMinutes)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(typicals);
        if (card.TimeBoxMinutes is int box)
        {
            return (box, TimeBox);
        }

        return Typical(typicals, card.Category, card.Worker) is { } t ? (t.Median, "typical " + t.Model) : (maxMinutes, MaxMinutes);
    }

    // The model a Worker line (or a run's model) picks among the category's typicals: the same name
    // (AreaDurations.For), else the one whose model name holds it (Worker: Opus picks claude-opus-5-5).
    private static AreaTypical? Typical(IReadOnlyList<AreaTypical> typicals, string category, string model)
    {
        model = model.Trim();
        return AreaDurations.For(typicals, category, model)
            ?? (model.Length == 0 ? null : typicals.FirstOrDefault(t => Text.Same(t.Area, category) && t.Model.Contains(model, StringComparison.OrdinalIgnoreCase)));
    }

    // A card's scope as dispatch reads it; with no Scope line, its area letter stands for the area (SC-4).
    private static Scope ScopeOf(ViewCard card) =>
        (card.Scope.Count == 0 ? null : ScopeLine.ParseValue(string.Join(", ", card.Scope))) ?? CardScope.AreaLetter(card.Category);

    private static int Rank(ViewFolder f) => f switch
    {
        ViewFolder.Queue => 0,
        ViewFolder.Done or ViewFolder.Archive => 1,
        _ => 2,
    };

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
