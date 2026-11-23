using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;

namespace Scheduler.Core.Pool;

/// <summary>1055-C: a worker running now, for the queue preview: its card id, its type, its card's category and when it started.</summary>
public sealed record PoolQueueWorker(string Id, string Type, string Category, DateTimeOffset Start);

/// <summary>
/// 1055-C: one project for the queue preview: the project as the dispatcher sees it, its queued cards in
/// its own order (the Queue window's, <see cref="PageModel.PageQueue.Order"/>) and its running workers.
/// </summary>
public sealed record PoolQueueLane(PoolProject Project, IReadOnlyList<Card> Queue)
{
    /// <summary>The project's workers running now.</summary>
    public IReadOnlyList<PoolQueueWorker> Running { get; init; } = [];
}

/// <summary>1055-C: one row of the pool's queue: the project and the card's file name.</summary>
public sealed record PoolQueueEntry(string Project, string Card);

/// <summary>
/// 1055-C: the order the pool will start the queued cards of all its projects. Each project's queue is
/// merged by the pool's own rule (<see cref="PoolDispatcher"/>, WT-7): while the total has room, every
/// eligible project with a free workspace (1029-B) offers its next card if a type fits it and has room
/// (<see cref="PoolDispatcher.Fits"/>, <see cref="TypeDispatcher.NotUsableBecause"/>), resumes first and then
/// the project furthest below its share (<see cref="PoolDispatcher.Choose"/>, MP-6). When nothing can start,
/// the oldest worker ends and frees its place, workspace and type. Each start counts a typical run's minutes
/// to its project's share. Within a project the order is its own: a card waits while another card of its
/// category runs, or while a card it depends on has not ended. Cards that never get a place (no type will
/// take them, the project is not running, a cycle) follow, each project's in its own order. Pure and linear
/// in the queued cards.
/// </summary>
public static class PoolQueueOrder
{
    /// <summary>The run a start is counted as when the window has no finished run to go by.</summary>
    public static readonly TimeSpan DefaultRun = TimeSpan.FromMinutes(30);

    /// <summary>The pool's queue in start order.</summary>
    /// <param name="lanes">The projects, in the pool file's order (ties go to the first).</param>
    /// <param name="states">Each project's state; a project not named counts as running (<see cref="PoolGate.Eligible"/>).</param>
    /// <param name="maxWorkers">The pool's total (WT-1).</param>
    /// <param name="types">The pool's types in the config's order; their running counts come from the lanes' workers.</param>
    /// <param name="migrated">The types came from slots.</param>
    /// <param name="quota">The agents out of quota.</param>
    /// <param name="servers">The model servers.</param>
    /// <param name="runs">The runs of the shares window (MP-6).</param>
    /// <param name="now">Now, for the shares.</param>
    public static IReadOnlyList<PoolQueueEntry> Order(
        IReadOnlyList<PoolQueueLane> lanes,
        IReadOnlyDictionary<string, SchedulerState> states,
        int maxWorkers,
        IReadOnlyList<DispatchType> types,
        bool migrated,
        AgentQuota quota,
        ServerCache servers,
        IEnumerable<PoolRun> runs,
        DateTimeOffset now)
    {
        var runList = runs.ToList();
        var run = TypicalRun(runList).TotalMinutes;
        var eligible = PoolGate.Eligible(lanes.Select(l => l.Project), states).Select(p => p.Name).ToHashSet(Text.Comparer);
        var all = new List<Lane>(lanes.Count);
        var byName = new Dictionary<string, Lane>(Text.Comparer);
        foreach (var l in lanes)
        {
            if (byName.ContainsKey(l.Project.Name))
            {
                continue;   // a repeated project is skipped, as the pool file's repeat is
            }

            var lane = new Lane(l, PoolShares.Minutes(runList, l.Project.Name, now), PoolDispatcher.WorkspacesOf(l.Project.Workspaces, maxWorkers));
            all.Add(lane);
            byName[l.Project.Name] = lane;
        }

        var running = new Dictionary<string, int>(Text.Comparer);
        foreach (var t in types)
        {
            running.TryAdd(t.Name, 0);
        }

        // The workers running now end oldest first; a simulated start ends after them.
        var fifo = new Queue<(Lane Lane, PoolQueueWorker Worker)>();
        foreach (var (lane, worker) in all.SelectMany(l => l.Source.Running.Select(w => (l, w))).OrderBy(x => x.w.Start))
        {
            fifo.Enqueue((lane, worker));
            lane.Begin(worker);
            running[worker.Type] = running.GetValueOrDefault(worker.Type) + 1;
        }

        var total = fifo.Count;
        var order = new List<PoolQueueEntry>();
        while (true)
        {
            if (total < maxWorkers)
            {
                var offers = new List<PoolOffer>();
                foreach (var lane in all)
                {
                    if (eligible.Contains(lane.Name) && lane.Busy < lane.Workspaces
                        && lane.Head(types, migrated, quota, servers) is { } card
                        && lane.MayStart(card)
                        && types.FirstOrDefault(t => PoolDispatcher.Fits(t, lane.Source.Project, card, migrated)
                            && TypeDispatcher.NotUsableBecause(t with { Running = running.GetValueOrDefault(t.Name) }, quota, servers) is null) is { } type)
                    {
                        offers.Add(new PoolOffer(lane.Name, new DispatchCandidate(card, card.Folder == CardFolder.Active)) { Type = type.Name });
                    }
                }

                if (PoolDispatcher.Choose(offers, o => FurthestBelowShare(o, byName)) is { } chosen)
                {
                    var lane = byName[chosen.Project];
                    var card = chosen.Candidate.Card;
                    var worker = new PoolQueueWorker(card.Id, chosen.Type, card.Category, now);
                    lane.Place(card, worker, run);
                    running[chosen.Type] = running.GetValueOrDefault(chosen.Type) + 1;
                    fifo.Enqueue((lane, worker));
                    total++;
                    order.Add(new PoolQueueEntry(lane.Name, card.Name));
                    continue;
                }
            }

            if (fifo.Count == 0)
            {
                break;
            }

            var (ended, done) = fifo.Dequeue();
            ended.End(done);
            running[done.Type] = Math.Max(0, running.GetValueOrDefault(done.Type) - 1);
            total--;
        }

        foreach (var lane in all)
        {
            order.AddRange(lane.Unplaced().Select(c => new PoolQueueEntry(lane.Name, c.Name)));
        }

        return order;
    }

    /// <summary>The mean length of the window's finished runs, at least a minute; <see cref="DefaultRun"/> when there are none.</summary>
    public static TimeSpan TypicalRun(IReadOnlyList<PoolRun> runs)
    {
        var finished = runs.Where(r => r.End is not null).Select(r => (r.End!.Value - r.Start).TotalMinutes).ToList();
        return finished.Count == 0 ? DefaultRun : TimeSpan.FromMinutes(Math.Max(1, finished.Average()));
    }

    // MP-6 as PoolShares.FurthestBelowShare reckons it, over the simulated minutes: weight share minus used
    // share among the offering projects; the first offer wins a tie.
    private static PoolOffer FurthestBelowShare(IReadOnlyList<PoolOffer> offers, Dictionary<string, Lane> lanes)
    {
        var weight = offers.Select(o => (double)Math.Max(0, lanes[o.Project].Source.Project.Weight)).ToList();
        var used = offers.Select(o => lanes[o.Project].Minutes).ToList();
        var totalWeight = weight.Sum();
        var totalUsed = used.Sum();
        PoolOffer? best = null;
        var bestGap = double.NegativeInfinity;
        for (var i = 0; i < offers.Count; i++)
        {
            var weightShare = totalWeight > 0 ? weight[i] / totalWeight : 1.0 / offers.Count;
            var usedShare = totalUsed > 0 ? used[i] / totalUsed : 0;
            var gap = weightShare - usedShare;
            if (best is null || gap > bestGap + 1e-9)
            {
                best = offers[i];
                bestGap = gap;
            }
        }

        return best!;
    }

    private sealed class Lane(PoolQueueLane source, double minutes, int workspaces)
    {
        private readonly Dictionary<string, int> _categories = new(Text.Comparer);
        private readonly HashSet<string> _open = new(source.Queue.Select(c => c.Id), Text.Comparer);
        private readonly HashSet<string> _placed = new(Text.Comparer);
        private int _next;

        public PoolQueueLane Source { get; } = source;

        public string Name => Source.Project.Name;

        public int Workspaces { get; } = workspaces;

        public int Busy { get; private set; }

        public double Minutes { get; private set; } = minutes;

        // The next card in the project's own order, past those no type would ever take (off, out of
        // quota, server down, or none fits), which the dispatcher would pass over too.
        public Card? Head(IReadOnlyList<DispatchType> types, bool migrated, AgentQuota quota, ServerCache servers)
        {
            while (_next < Source.Queue.Count)
            {
                var card = Source.Queue[_next];
                if (types.Any(t => PoolDispatcher.Fits(t, Source.Project, card, migrated)
                    && TypeDispatcher.NotUsableBecause(t with { Running = 0 }, quota, servers) is null))
                {
                    return card;
                }

                _next++;
            }

            return null;
        }

        // A card waits while another of its category runs, or while a card it depends on is still queued or running.
        public bool MayStart(Card card) =>
            _categories.GetValueOrDefault(card.Category) == 0 && !card.Depends.Any(_open.Contains);

        public void Begin(PoolQueueWorker worker)
        {
            Busy++;
            _open.Add(worker.Id);
            _categories[worker.Category] = _categories.GetValueOrDefault(worker.Category) + 1;
        }

        public void Place(Card card, PoolQueueWorker worker, double runMinutes)
        {
            _placed.Add(card.Id);
            _next++;
            Minutes += runMinutes;
            Begin(worker);
        }

        public void End(PoolQueueWorker worker)
        {
            Busy = Math.Max(0, Busy - 1);
            _open.Remove(worker.Id);
            _categories[worker.Category] = Math.Max(0, _categories.GetValueOrDefault(worker.Category) - 1);
        }

        public IEnumerable<Card> Unplaced() => Source.Queue.Where(c => !_placed.Contains(c.Id));
    }
}
