// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.PageModel;
using Scheduler.Core.Tick;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Pool;

/// <summary>
/// One project of a running pool: its name and weight from the pool file (MP-C2), the agents its
/// scheduler.json defines (WT-C9; null = not restricted) and its own tick (MP-H2).
/// </summary>
/// <param name="Name">The project's name.</param>
/// <param name="Weight">Its weight (MP-4).</param>
/// <param name="Tick">Its tick, built as for one project, with the pool's shared quota and servers.</param>
public sealed record PoolTickProject(string Name, int Weight, SchedulerTick Tick)
{
    /// <summary>The agents the project defines (WT-C9); null = not restricted.</summary>
    public IReadOnlySet<string>? Agents { get; init; }

    /// <summary>1029-B: the project's workspaces from the pool file; null = the pool's <c>maxWorkers</c>.</summary>
    public int? Workspaces { get; init; }

    /// <summary>The project's repository folder ("" when unknown), shown on its chip's hover (MP-P9).</summary>
    public string Repo { get; init; } = "";
}

/// <summary>
/// The pool's tick (MP-10): every project's steps up to its plan, in pool order; then one dispatch
/// across all of them (<see cref="PoolDispatcher.Dispatch"/>, WT-7) with pool-wide counts (MP-11) and
/// the shares' runs (MP-15); each start routed to its project's tick; then each project's
/// retries and status. One project without a pool keeps <see cref="SchedulerTick.Run()"/> (MP-C9).
/// </summary>
public sealed class PoolTick
{
    // 1055-C: the plan of a project that has not planned yet, for the queue preview's PoolProject.
    private static readonly DispatchPlan EmptyPlan = new()
    {
        Finished = new HashSet<string>(),
        Busy = new Dictionary<string, string>(),
        Candidates = [],
        Ordered = [],
        Notes = [],
        Stuck = [],
    };

    private readonly IClock _clock;
    private readonly ISchedulerLog _log;
    private readonly Func<IReadOnlyList<PoolRun>> _finishedRuns;
    private readonly List<PoolTickProject> _projects = [];
    private readonly List<PoolTickProject> _leaving = [];
    private readonly Dictionary<string, (double Used, double Weight)> _shares = new(Text.Comparer);

    /// <summary>A pool tick; <paramref name="finishedRuns"/> gives the finished runs of every project's accounting.csv (MP-15).</summary>
    public PoolTick(IClock clock, ISchedulerLog log, AgentQuota quota, ServerCache servers, Func<IReadOnlyList<PoolRun>> finishedRuns)
    {
        _clock = clock;
        _log = log;
        Quota = quota;
        Servers = servers;
        _finishedRuns = finishedRuns;
    }

    /// <summary>The agents out of quota, one for the pool (MP-17, WT-9).</summary>
    public AgentQuota Quota { get; }

    /// <summary>The model servers, one cache for the pool (MP-H4).</summary>
    public ServerCache Servers { get; }

    /// <summary>The pool's worker types and total (MP-C4).</summary>
    public WorkerPool Types { get; private set; } = new();

    /// <summary>The projects in the pool, in the pool file's order (MP-C2).</summary>
    public IReadOnlyList<PoolTickProject> Projects => _projects;

    /// <summary>Projects removed from the pool file whose workers still run (MP-H6): watched, never offered.</summary>
    public IReadOnlyList<PoolTickProject> Leaving => _leaving;

    /// <summary>The starts of the last dispatch, in order.</summary>
    public IReadOnlyList<PoolOffer> LastStarts { get; private set; } = [];

    /// <summary>MP-19: threads per worker, from the pool's <c>maxWorkers</c>.</summary>
    public int ThreadsPerWorker(int cores) => PoolDispatcher.ThreadsPerWorker(cores, Types.MaxWorkers);

    /// <summary>
    /// MP-C7: take the pool file's types and projects (at start, and each time the file changes).
    /// Every project's tick uses the pool's types (MP-C4). A project no longer listed whose workers
    /// still run is kept to be watched until they end (MP-H6); its orphans are not resumed (MP-16).
    /// </summary>
    public void Apply(WorkerPool types, IReadOnlyList<PoolTickProject> projects)
    {
        Types = types;
        var gone = _projects.Concat(_leaving)
            .Where(p => !projects.Any(n => Text.Same(n.Name, p.Name)))
            .ToList();
        _projects.Clear();
        _leaving.Clear();
        foreach (var p in projects)
        {
            if (_projects.Any(q => Text.Same(q.Name, p.Name)))
            {
                _log.Write($"pool: project {p.Name} is listed twice; the repeat is skipped");
                continue;
            }

            _projects.Add(p);
        }

        foreach (var p in gone)
        {
            if (p.Tick.Busy().Count > 0)
            {
                _leaving.Add(p);
                _log.Write($"pool: {p.Name} left the pool; its running workers are watched until they end, its cards are not started");
            }
        }

        foreach (var p in _projects.Concat(_leaving))
        {
            p.Tick.UsePoolTypes(types);
        }
    }

    /// <summary>One pool tick (MP-10) for <paramref name="wake"/>.</summary>
    public void Run(TickWake wake = TickWake.Poll)
    {
        var all = _projects.Concat(_leaving).ToList();
        var full = all.Where(p => p.Tick.Prepare(wake)).ToList();
        var offering = _projects.Where(full.Contains).ToList();

        if (offering.Count > 0)
        {
            Dispatch(offering);
        }

        foreach (var p in all)
        {
            p.Tick.Complete();
        }

        _leaving.RemoveAll(p =>
        {
            var done = p.Tick.Busy().Count == 0;
            if (done)
            {
                _log.Write($"pool: {p.Name}'s last worker ended; it is no longer watched");
            }

            return done;
        });
    }

    /// <summary>
    /// The pool part of the page (MP-P4, MP-P7): each project's state, weight and share in the
    /// window, its running workers and queue; each type's running workers per project; the total.
    /// The projects' own page snapshots are the Host's to add (<see cref="PoolPageSnapshot.Pages"/>).
    /// </summary>
    public PoolPageSnapshot Snapshot()
    {
        var now = _clock.Now;
        var runs = Runs();
        var totalWeight = _projects.Sum(p => Math.Max(0, p.Weight));
        var minutes = _projects.ToDictionary(p => p.Name, p => PoolShares.Minutes(runs, p.Name, now), Text.Comparer);
        var totalMinutes = minutes.Values.Sum();
        return new PoolPageSnapshot
        {
            Built = now,
            MaxWorkers = Types.MaxWorkers,
            Running = _projects.Concat(_leaving).Sum(p => p.Tick.Busy().Count),
            QueueOrder = QueueOrder(runs, now),
            Projects =
            [
                .. _projects.Select(p => new PoolProjectView
                {
                    Name = p.Name,
                    Repo = p.Repo,
                    Weight = p.Weight,
                    WeightShare = totalWeight > 0 ? (double)Math.Max(0, p.Weight) / totalWeight : 0,
                    UsedShare = totalMinutes > 0 ? minutes[p.Name] / totalMinutes : 0,
                    State = p.Tick.State,
                    Running = p.Tick.Busy().Count,
                    Workspaces = PoolDispatcher.WorkspacesOf(p.Workspaces, Types.MaxWorkers),
                    Queued = p.Tick.LastPlan?.Candidates.Count ?? 0,
                }),
            ],
            Types =
            [
                .. Types.Types.Select(t => new PoolTypeView
                {
                    Name = t.Name,
                    Max = t.Max,
                    Agent = t.Agent,
                    // 1087-C: the pool file's own settings travel with the type, so the Worker
                    // types panel's boxes are not empty on the pool page; EffectiveModel is the
                    // Host's to fill, as resolving it needs the project's scheduler.json.
                    Model = t.Model,
                    Endpoint = t.Endpoint,
                    MetricsUrl = t.MetricsUrl,
                    IsDefault = Types.IsDefault(t.Name),
                    Projects = t.Projects,
                    RunningByProject = _projects.Concat(_leaving)
                        .Select(p => (p.Name, Count: p.Tick.Busy().Count(w => Text.Same(w.Type, t.Name))))
                        .Where(x => x.Count > 0)
                        .ToDictionary(x => x.Name, x => x.Count, Text.Comparer),
                }),
            ],
        };
    }

    /// <summary>
    /// 1055-C: the pool's queue in start order (<see cref="PoolQueueOrder"/>): each project's Queue window
    /// order (<see cref="PageQueue.Order"/>, as its page shows it), merged by the pool's dispatch rule.
    /// A project leaving the pool queues nothing; its workers still end and free their places.
    /// </summary>
    private IReadOnlyList<PoolQueueEntry> QueueOrder(IReadOnlyList<PoolRun> runs, DateTimeOffset now)
    {
        if (_projects.Count == 0)
        {
            return [];
        }

        var all = _projects.Concat(_leaving).ToList();
        var lanes = all.Select(p =>
        {
            var busy = p.Tick.Busy();
            var cards = p.Tick.Cards;
            var project = new PoolProject(p.Name, p.Weight, p.Tick.LastPlan ?? EmptyPlan)
            {
                Agents = p.Agents,
                Workspaces = p.Workspaces,
                Busy = busy.Count,
            };
            return new PoolQueueLane(project, _leaving.Contains(p) ? [] : QueuedCards(cards, busy))
            {
                Running = [.. busy.Select(w => new PoolQueueWorker(w.Id, w.Type, CategoryOf(cards, w.Id), w.Start))],
            };
        }).ToList();
        var states = all.ToDictionary(p => p.Name, p => p.Tick.State, Text.Comparer);
        return PoolQueueOrder.Order(lanes, states, Types.MaxWorkers, PoolTypes(all, _projects[0]), Types.Migrated, Quota, Servers, runs, now);
    }

    // The Queue window's cards (PageQueue.Order) as cards: an orphan in active/ before a queue/ card of the same name.
    private static List<Card> QueuedCards(IReadOnlyList<Card> cards, IReadOnlyList<RunningWorker> busy)
    {
        var byName = new Dictionary<string, Card>(Text.Comparer);
        foreach (var c in cards.Where(c => c.Folder is CardFolder.Active or CardFolder.Queue).OrderBy(c => c.Folder == CardFolder.Active ? 0 : 1))
        {
            byName.TryAdd(c.Name, c);
        }

        return [.. PageQueue.Order(cards, busy).Select(q => byName.GetValueOrDefault(q.Name)).OfType<Card>()];
    }

    private static string CategoryOf(IReadOnlyList<Card> cards, string id) =>
        cards.FirstOrDefault(c => Text.Same(c.Id, id))?.Category
        ?? (id.LastIndexOf('-') is var dash and >= 0 ? id[(dash + 1)..] : "");

    /// <summary>MP-15: the finished runs of the window and every running worker of every project, up to now.</summary>
    public IReadOnlyList<PoolRun> Runs()
    {
        var from = _clock.Now - PoolShares.Window;
        return
        [
            .. _finishedRuns().Where(r => r.End is null || r.End > from),
            .. _projects.Concat(_leaving).SelectMany(p => p.Tick.Busy().Select(w => new PoolRun(p.Name, w.Start, null))),
        ];
    }

    private void Dispatch(List<PoolTickProject> offering)
    {
        var all = _projects.Concat(_leaving).ToList();
        var types = PoolTypes(all, offering[0]);
        var attempts = new Dictionary<string, List<TypeStart>>(Text.Comparer);
        var lost = new List<PoolOffer>();
        var runs = Runs();
        var projects = offering.Select(p => new PoolProject(p.Name, p.Weight, p.Tick.LastPlan!)
        {
            Agents = p.Agents,
            Workspaces = p.Workspaces,
            Busy = p.Tick.Busy().Count,
        }).ToList();
        var states = offering.ToDictionary(p => p.Name, p => p.Tick.State, Text.Comparer);

        LastStarts = PoolDispatcher.Dispatch(
            projects, states, Types.MaxWorkers, all.Sum(p => p.Tick.Busy().Count), types, Types.Migrated, Quota, Servers, runs, _clock,
            name => Find(name).Tick.Retries,
            offer =>
            {
                var attempt = new TypeStart(offer.Type, offer.Candidate, Started: false);
                var ok = Find(offer.Project).Tick.Start(attempt);
                if (!attempts.TryGetValue(offer.Project, out var list))
                {
                    attempts[offer.Project] = list = [];
                }

                list.Add(attempt with { Started = ok });
                return ok;
            },
            lost);

        Shares(lost, runs);
        var after = PoolTypes(all, offering[0]);
        var running = after.ToDictionary(t => t.Name, t => t.Running, Text.Comparer);
        var total = all.Sum(p => p.Tick.Busy().Count);
        foreach (var p in offering)
        {
            var mine = attempts.GetValueOrDefault(p.Name) ?? [];
            var tried = new HashSet<string>(mine.Select(a => a.Candidate.Card.Id), Text.Comparer);
            var served = Types.Types.Any(t => t.Serves(p.Name));
            var lostCard = lost.FirstOrDefault(l => Text.Same(l.Project, p.Name))?.Candidate.Card.Id;
            var busy = p.Tick.Busy().Count;
            var workspaces = PoolDispatcher.WorkspacesOf(p.Workspaces, Types.MaxWorkers);
            var notes = TypeDispatcher.Notes(
                p.Tick.LastPlan!, tried, p.Tick.State, Types.MaxWorkers, total, after, Types.Migrated, running, Quota, Servers,
                card => p.Tick.State != SchedulerState.Running ? null
                    : !served ? PoolDispatcher.NotServedNote(p.Name)
                    : busy >= workspaces ? PoolDispatcher.WaitsForWorkspaceNote(busy, workspaces)
                    : Text.Same(card.Id, lostCard ?? "") ? PoolDispatcher.AboveShareNote(p.Name, _shares[p.Name].Used, _shares[p.Name].Weight)
                    : null);
            p.Tick.Dispatched(new TypeDispatchResult { Starts = mine, Notes = notes });
        }
    }

    /// <summary>MP-21: the used and weight shares of each project that lost, among the projects that started or lost.</summary>
    private void Shares(List<PoolOffer> lost, IReadOnlyList<PoolRun> runs)
    {
        _shares.Clear();
        if (lost.Count == 0)
        {
            return;
        }

        var now = _clock.Now;
        var names = LastStarts.Select(s => s.Project).Concat(lost.Select(l => l.Project)).Distinct(Text.Comparer).ToList();
        var weights = names.ToDictionary(n => n, n => (double)Math.Max(0, Find(n).Weight), Text.Comparer);
        var minutes = names.ToDictionary(n => n, n => PoolShares.Minutes(runs, n, now), Text.Comparer);
        var totalWeight = weights.Values.Sum();
        var totalMinutes = minutes.Values.Sum();
        foreach (var n in names)
        {
            _shares[n] = (
                totalMinutes > 0 ? minutes[n] / totalMinutes : 0,
                totalWeight > 0 ? weights[n] / totalWeight : 1.0 / names.Count);
        }
    }

    /// <summary>
    /// MP-11: the pool's types with their running workers and cards counted in every project
    /// (leaving ones included); model and endpoint as <paramref name="first"/>'s scheduler.json gives them.
    /// </summary>
    private static List<DispatchType> PoolTypes(List<PoolTickProject> all, PoolTickProject first) =>
    [
        .. first.Tick.DispatchTypes().Select(t =>
        {
            var busy = all.SelectMany(p => p.Tick.Busy()).Where(w => Text.Same(w.Type, t.Name)).ToList();
            return t with { Running = busy.Count, RunningCards = [.. busy.Select(w => w.Id)] };
        }),
    ];

    private PoolTickProject Find(string name) =>
        _projects.Concat(_leaving).First(p => Text.Same(p.Name, name));
}
