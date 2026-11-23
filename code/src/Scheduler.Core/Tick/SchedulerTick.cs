using Scheduler.Core.Accounting;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Health;
using Scheduler.Core.Launch;
using Scheduler.Core.Categories;
using Scheduler.Core.Locks;
using Scheduler.Core.Results;
using Scheduler.Core.Scopes;

namespace Scheduler.Core.Tick;

/// <summary>
/// One scheduler tick (spec A3, <c>Tick</c> L2795): every Core area called in the spec's order,
/// with all I/O through the ports of <c>Core/Contracts</c>. Host's loop (270-B) calls
/// <see cref="Run"/> once a second and when a worker exits (TIME-21).
/// </summary>
public sealed class SchedulerTick
{
    private readonly SchedulerConfig _config;
    private readonly IClock _clock;
    private readonly IHostLoad _load;
    private readonly IWorkerProcesses _processes;
    private readonly ITickHost _host;
    private readonly HealthCheck? _health;
    private readonly ServerMetricsSampler _metrics;
    private readonly Dictionary<int, RunningWorker> _running = [];
    private readonly Dictionary<int, PendingStart> _starting = [];   // 995-B: slots whose claim is on its way to main
    private readonly ISchedulerLog _log;
    private readonly Dictionary<string, int> _retries = new(Text.Comparer);
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _models = new Dictionary<string, IReadOnlyList<string>>();
    private string? _readSha;   // 981-A: main's sha when the cards were last read, or null
    private WorkerPool? _poolTypes;   // MP-C4: the pool file's types, which replace workers.json in a pool
    private bool _full;   // the tick prepared in full: it dispatches and saves its retries

    /// <summary>
    /// A tick over the given ports; <paramref name="health"/> is null when no health check is set.
    /// <paramref name="quota"/> and <paramref name="servers"/> are shared by every project of a pool
    /// (MP-H4, MP-17); one project passes none and gets its own.
    /// </summary>
    public SchedulerTick(
        SchedulerConfig config,
        IClock clock,
        ISchedulerLog log,
        IHostLoad load,
        IServerProbe probe,
        IWorkerProcesses processes,
        IStateFiles files,
        ITickHost host,
        HealthCheck? health = null,
        AgentQuota? quota = null,
        ServerCache? servers = null)
    {
        _config = config;
        _clock = clock;
        _log = log;
        _load = load;
        _processes = processes;
        _host = host;
        _health = health;
        _metrics = new ServerMetricsSampler(clock, probe);
        Servers = servers ?? new ServerCache(probe, clock, log);
        Quota = quota ?? new AgentQuota();
        Categories = new CategoryRegistry(log, files, $"{config.JobsDir}/{CategoriesLocation.Name}");
        Workers = new WorkerSettings(log, config, $"{config.JobsDir}/workers.json");
    }

    /// <summary>Raised as each step begins, in order.</summary>
    public event Action<TickStep>? StepStarting;

    /// <summary>The scheduler's state.</summary>
    public SchedulerState State { get; set; } = SchedulerState.Running;

    /// <summary>The running workers by slot.</summary>
    public IReadOnlyDictionary<int, RunningWorker> Running => _running;

    /// <summary>
    /// 995-B: the slots starting a card whose claim is not on main yet, each held by a stand-in worker
    /// with no process (pid 0); its worker starts once the claim lands.
    /// </summary>
    public IReadOnlyDictionary<int, RunningWorker> Starting => _starting.ToDictionary(p => p.Key, p => p.Value.Holder);

    /// <summary>The cards last read.</summary>
    public IReadOnlyList<Card> Cards { get; private set; } = [];

    /// <summary>The requests last read.</summary>
    public IReadOnlyList<JobRequest> Requests { get; private set; } = [];

    /// <summary>The retry counts by card id (TIME-22).</summary>
    public IDictionary<string, int> Retries => _retries;

    /// <summary>The model servers, cached 20 s (DISP-15).</summary>
    public ServerCache Servers { get; }

    /// <summary>categories.json and the held categories (A4).</summary>
    public CategoryRegistry Categories { get; }

    /// <summary>workers.json: slots, workers and which are enabled.</summary>
    public WorkerSettings Workers { get; }

    /// <summary>The agents out of quota (WT-9). It replaces the project-wide <c>paused-quota</c> state: the state stays running.</summary>
    public AgentQuota Quota { get; }

    /// <summary>The host's last cpu and memory sample.</summary>
    public HostLoadSample? Load { get; private set; }

    /// <summary>The last plan.</summary>
    public DispatchPlan? LastPlan { get; private set; }

    /// <summary>The last dispatch: starts tried and notes.</summary>
    public TypeDispatchResult? LastDispatch { get; private set; }

    /// <summary>
    /// The dynamic-lock settings (design <c>dynamic-locks.md</c>): off by default. On, with a host that
    /// keeps the lock table (<see cref="ILockingHost"/>), the tick records held sets, runs the lock step
    /// and gates dispatch on held and remembered sets; off, it dispatches exactly as before.
    /// </summary>
    public LockSettings Locks { get; set; } = LockSettings.Off;

    /// <summary>
    /// Take a worker that is already running (adopted at start-up, WT-W5) into its folder. It keeps
    /// the folder number even above <c>maxWorkers</c>; an entry with no type gets the type its slot
    /// was migrated from, and otherwise counts toward the total only.
    /// </summary>
    public void Adopt(RunningWorker worker) =>
        _running[worker.Slot] = worker.Type.Length > 0 ? worker : worker with { Type = Adoption.TypeOfSlot(Pool(), worker.Slot) };

    /// <summary>Run one full tick, its steps in the order of spec A3: the poll.</summary>
    public void Run() => Run(TickWake.Poll);

    /// <summary>
    /// The worker types in use now: the pool's (<see cref="UsePoolTypes"/>), else workers.json's
    /// <c>pool</c>, else those its slots make.
    /// </summary>
    public WorkerPool Types => Pool();

    /// <summary>
    /// MP-C4, MP-C7: in a pool the pool file's types replace the project's workers.json, which is
    /// then no longer read; null goes back to workers.json.
    /// </summary>
    public void UsePoolTypes(WorkerPool? pool) => _poolTypes = pool;

    /// <summary>The running workers and the starting slots' stand-ins: both hold a slot, a card and its scope (MP-11).</summary>
    public IReadOnlyList<RunningWorker> Busy() => [.. _running.Values.Concat(_starting.Values.Select(s => s.Holder))];

    /// <summary>
    /// The types as dispatch sees them (WT-2, WT-12): this project's running count and cards per
    /// type, with the model, endpoint and premium its own scheduler.json gives the type (MP-C8).
    /// </summary>
    public IReadOnlyList<DispatchType> DispatchTypes() => DispatchTypes(Pool());

    /// <summary>
    /// Run one tick for <paramref name="wake"/> (981-A). The poll, a click and a worker exit run every
    /// step of spec A3. A move-request, planner-batch or health-check wake is light when no worker
    /// finished, the state did not change and main is at the sha the cards were last read at: it
    /// skips the card read before the planner step, re-reads after it only if the step moved main, and
    /// when main has still not moved it skips the reads, plan, dispatch and retry save as well. When
    /// any of that is not known (no sha, no read yet, a failed read) the tick is full.
    /// </summary>
    public void Run(TickWake wake)
    {
        if (Prepare(wake))
        {
            Dispatch();
        }

        Complete();
    }

    /// <summary>
    /// MP-10: the tick's steps up to and including the plan and the health start (see
    /// <see cref="Run(TickWake)"/>). False when the tick is light and main has not moved: then it
    /// neither dispatches nor saves its retries, and <see cref="Complete"/> only writes the status.
    /// </summary>
    public bool Prepare(TickWake wake)
    {
        _full = false;
        var before = State;

        Step(TickStep.SampleHost);
        Load = _load.Sample();

        Step(TickStep.SampleServers);
        var pool = Pool();
        _metrics.Sample(pool.MaxWorkers, pool.Types.Select(t => WorkerSelection.Effective(_config, t).MetricsUrl), _running.Values);

        Step(TickStep.ReapHealthCheck);
        if (_health is not null)
        {
            State = _health.Reap(State);
        }

        Step(TickStep.ReapWorkers);
        var finished = ReapWorkers();
        finished |= StartClaimed();

        Step(TickStep.IndexLogs);
        _host.IndexLogs();

        var light = wake is TickWake.MoveRequest or TickWake.LockRequest or TickWake.PlannerBatch or TickWake.HealthCheck
            && !finished && State == before && MainUnchanged();
        if (!light)
        {
            ReadCardsAndFix();
        }

        Step(TickStep.PlannerStep);
        _host.PlannerStep();
        LockStep();   // dynamic locks: after the move step, so releases from finishes come first

        if (light)
        {
            if (MainUnchanged())
            {
                Step(TickStep.StartHealthCheck);
                _health?.StartIfWanted();
                return false;
            }

            ReadCardsAndFix();   // the planner step moved main: its cards are read after it
        }

        Step(TickStep.ReadCategories);
        Categories.Read(_host.ReadCategories(), Cards);

        Step(TickStep.ReadWorkers);
        var workers = _poolTypes is null ? _host.ReadWorkers() : null;   // MP-C4: not read in a pool
        if (workers is not null)
        {
            Workers.Apply(workers, []);   // WT-W5: a worker above maxWorkers no longer raises it; WT-1 and WT-2 count it
        }

        Step(TickStep.ReadModels);
        _models = _host.ReadModels();

        Step(TickStep.ReadRequests);
        Requests = _host.ReadRequests();

        Step(TickStep.Plan);
        var plan = DispatchPlanner.Plan(Cards, [.. Busy()],_retries, Categories.Current, _config.MaxRetries);   // SC-2, 560-A: no held categories
        plan = GateOnLocks(plan);   // DL-1, DL-10: held and remembered sets, when locks are on
        LastPlan = plan;

        Step(TickStep.StartHealthCheck);
        _health?.StartIfWanted();
        _full = true;
        return true;
    }

    /// <summary>The one-project dispatch (WT-7 in one project) over the last plan, as today.</summary>
    public void Dispatch()
    {
        Step(TickStep.Dispatch);
        var pool = Pool();   // ReadWorkers may have changed it since the metrics step
        LastDispatch = TypeDispatcher.Dispatch(
            LastPlan!, State, pool.MaxWorkers, _running.Count + _starting.Count, DispatchTypes(pool), pool.Migrated, Quota, Servers, _retries,
            attempt => Start(pool, attempt));
    }

    /// <summary>MP-10: a pool records the result of its one dispatch here, in place of <see cref="Dispatch"/>.</summary>
    public void Dispatched(TypeDispatchResult result)
    {
        Step(TickStep.Dispatch);
        LastDispatch = result;
    }

    /// <summary>The steps after dispatch: save the retries (a full tick only) and write the status.</summary>
    public void Complete()
    {
        if (_full)
        {
            Step(TickStep.SaveRetries);
            _host.SaveRetries(_retries);
        }

        Step(TickStep.WriteStatus);
        _host.WriteStatus(SlotStates(), State);
    }

    /// <summary>MP-10: start the card a pool's dispatch gave this project, on the type it chose; true when it started.</summary>
    public bool Start(TypeStart attempt) => Start(Pool(), attempt);

    private void Step(TickStep step) => StepStarting?.Invoke(step);

    /// <summary>
    /// TIME-13 to TIME-20: a worker that is no longer alive is finished and leaves its slot, and a
    /// health check is wanted; a live one is watched.
    /// </summary>
    private bool ReapWorkers()
    {
        var finished = false;
        foreach (var w in _running.Values.OrderBy(w => w.Slot).ToList())
        {
            if (RunResults.Alive(w.Abandoned, _processes.CmdStartTime(w.Pid), w.Start))
            {
                _running[w.Slot] = _host.Watch(w);
                continue;
            }

            // WT-9: an out-of-quota result marks the run's agent instead of pausing the project.
            var before = State;
            State = _host.Finish(w, State, _retries);
            if (State == SchedulerState.PausedQuota && before != SchedulerState.PausedQuota)
            {
                Quota.Mark(w.Agent, _clock.Now);
                State = before;
            }

            _running.Remove(w.Slot);
            _health?.RunFinished();
            finished = true;
        }

        return finished;
    }

    /// <summary>
    /// 995-B: each starting slot whose claim landed starts its worker (CM-9: its card is in active/ on
    /// main now); one whose claim failed is freed. True when a slot changed.
    /// </summary>
    private bool StartClaimed()
    {
        if (_starting.Count == 0 || _host is not IClaimingHost claims)
        {
            return false;
        }

        var changed = false;
        foreach (var (slot, s) in _starting.OrderBy(p => p.Key).ToList())
        {
            var state = claims.ClaimOf(slot);
            if (state == ClaimState.Pending)
            {
                continue;
            }

            _starting.Remove(slot);
            changed = true;
            if (state == ClaimState.Failed)
            {
                _log.Write($"slot {slot}: the claim of {s.Card.Name} failed; the slot is free");
                UpdateLocks(t => t.ClaimFailed(s.Card.Name));   // 1018-D: the card was never held
                continue;
            }

            Hold(s.Card, slot);   // 1018-D: the initial held set is recorded when the claim lands
            var w = _host.StartWorker(slot, s.Card, s.Resume, s.Type);
            if (w is not null)
            {
                _running[slot] = w with { Type = s.Type };
            }
            else
            {
                UpdateLocks(t => t.Release(s.Card.Name, LockRelease.Ended));   // DL-8 d: it never ran
            }
        }

        return changed;
    }

    /// <summary>The read-cards and fix-duplicates steps; a fix moved main, so the cards are read again.</summary>
    private void ReadCardsAndFix()
    {
        Step(TickStep.ReadCards);
        ReadCards();

        Step(TickStep.FixDuplicates);
        if (_host.FixDuplicates(Cards))
        {
            ReadCards();
        }
    }

    /// <summary>
    /// CARD-3: when the cards cannot be read the previous list is kept. 981-A: the sha taken just
    /// before a read is the one the cards are at; a failed read leaves none, so the next tick reads.
    /// </summary>
    private void ReadCards()
    {
        var sha = _host.MainSha();
        var cards = _host.ReadCards();
        if (cards is not null)
        {
            Cards = cards;
        }

        _readSha = cards is null ? null : sha;
    }

    /// <summary>981-A: main is known and at the sha the cards were last read at.</summary>
    private bool MainUnchanged() =>
        _readSha is not null && string.Equals(_host.MainSha(), _readSha, StringComparison.Ordinal);

    /// <summary>
    /// Start-Worker for one dispatch attempt (WT-6); the folder is taken only when the worker started.
    /// The worker's type is recorded on it (WT-W5).
    /// </summary>
    private bool Start(WorkerPool pool, TypeStart attempt)
    {
        var type = pool.Types.First(t => Text.Same(t.Name, attempt.Type));
        var slot = FreeFolder(pool, type);
        if (slot is null)
        {
            return false;
        }

        var card = attempt.Candidate.Card;
        if (_host is IClaimingHost claims && claims.PostClaim(slot.Value, card, attempt.Candidate.Resume))
        {
            // 995-B: the claim goes to main off the tick; the slot is held until it lands (StartClaimed).
            var holder = new RunningWorker
            {
                Slot = slot.Value,
                Pid = 0,
                Start = _clock.Now,
                Card = card.Name,
                Category = card.Category,
                Id = card.Id,
                LastProgress = _clock.Now,
                Resume = attempt.Candidate.Resume,
                Type = type.Name,
            };
            _starting[slot.Value] = new PendingStart(card, attempt.Candidate.Resume, type.Name, holder);
            _log.Write($"slot {slot.Value}: starting {card.Name}, its claim is posted");
            UpdateLocks(t => t.Starting(card.Name, _config.Project, slot.Value));   // 1018-D: no held set until it lands
            return true;
        }

        var w = _host.StartWorker(slot.Value, card, attempt.Candidate.Resume, type.Name);
        if (w is null)
        {
            return false;
        }

        _running[slot.Value] = w with { Type = type.Name };
        Hold(card, slot.Value);   // a resume or a host that claims in StartWorker: its claim is on main
        return true;
    }

    /// <summary>The lock host when locks are on and the host keeps a table; null otherwise.</summary>
    private ILockingHost? Locking => Locks.On ? _host as ILockingHost : null;

    /// <summary>The lock step (design <c>dynamic-locks.md</c>, section 6), when locks are on.</summary>
    private void LockStep()
    {
        if (Locking is { } locking)
        {
            Step(TickStep.Locks);
            locking.LockStep();
        }
    }

    /// <summary>
    /// DL-1, DL-10, DL-22: the plan gated again on the lock table - running cards' held sets, other
    /// projects' holders in the same repository, candidates' remembered sets. Unchanged when locks are off.
    /// </summary>
    private DispatchPlan GateOnLocks(DispatchPlan plan)
    {
        if (Locking is not { } locking)
        {
            return plan;
        }

        var busy = Busy();
        var categories = Categories.Current;
        var names = new Dictionary<string, string>(Text.Comparer);
        foreach (var w in busy)
        {
            names.TryAdd(w.Id, w.Card);
        }

        return ScopeGate.ApplyLocks(
            plan, c => CardScope.Of(c, categories), DispatchPlanner.RunningScopes(Cards, busy, categories), names,
            locking.ReadLocks(), categories?.AddOnly);
    }

    /// <summary>DL-1, DL-11: the card holds its claim (and its remembered set) from now; its age is fixed.</summary>
    private void Hold(Card card, int slot)
    {
        var claim = CardScope.Of(card, Categories.Current) ?? CardScope.AreaLetter(card.Category);
        UpdateLocks(t => t.Start(card.Name, CardAge.Of(card), claim with { Problem = null }, _clock.Now, _config.Project, slot));
    }

    /// <summary>Reads the lock table, changes it and writes it back when it changed; nothing when locks are off.</summary>
    private void UpdateLocks(Func<LockTable, LockTable> change)
    {
        if (Locking is not { } locking)
        {
            return;
        }

        var before = locking.ReadLocks();
        var after = change(before);
        if (!ReferenceEquals(before, after))
        {
            locking.SaveLocks(after);
        }
    }

    /// <summary>
    /// The folder a new worker of <paramref name="type"/> takes (WT-W2): the lowest-numbered with no
    /// running worker. A type migrated from slots keeps to its own enabled slots until the pool form
    /// is saved, as Host still starts a worker with the settings of the slot it is given (WT-C10.5).
    /// </summary>
    private int? FreeFolder(WorkerPool pool, WorkerType type)
    {
        var folders = pool.Migrated && type.Slots.Count > 0
            ? type.Slots.Where(s => Workers.Enabled.GetValueOrDefault(s, true)).Order()
            : Enumerable.Range(1, pool.MaxWorkers);
        foreach (var f in folders)
        {
            if (!_running.ContainsKey(f) && !_starting.ContainsKey(f))
            {
                return f;
            }
        }

        return null;
    }

    /// <summary>
    /// The types now (WT-C10): the file's <c>pool</c>, or, while the file has none, those its slots
    /// make - rebuilt each tick, since Disable and Enable change the caps without changing the text.
    /// </summary>
    private WorkerPool Pool() => _poolTypes ?? (Workers.Pool.Migrated
        ? WorkerTypesJson.FromSlots(_config, Workers.Slots, Workers.Workers, Workers.Enabled)
        : Workers.Pool);

    private IEnumerable<int> SlotNumbers() => Enumerable.Range(1, Workers.Slots);

    private List<DispatchType> DispatchTypes(WorkerPool pool) =>
    [
        .. pool.Types.Select(t =>
        {
            var e = WorkerSelection.Effective(_config, t);
            var ofType = Busy().Where(w => Text.Same(w.Type, t.Name)).OrderBy(w => w.Slot).ToList();
            return new DispatchType
            {
                Name = t.Name,
                Max = t.Max,
                Running = ofType.Count,
                RunningCards = [.. ofType.Select(w => w.Id)],   // WT-12 (935-A): the note names the cards, by slot
                Model = e.Model,
                ModelNames = _models.GetValueOrDefault(e.Model) ?? [],
                Premium = e.Premium,
                Endpoint = e.Endpoint,
                Agent = t.Agent,
                Slots = t.Slots,
                Projects = t.Projects,   // WT-C8, MP-10: which projects the type serves
            };
        }),
    ];

    private List<SlotState> SlotStates() =>
    [
        .. SlotNumbers().Union(_running.Keys).Order().Select(slot => new SlotState   // a folder above maxWorkers stays listed while it runs (WT-W5)
        {
            Slot = slot,
            Enabled = Workers.Enabled.GetValueOrDefault(slot, true),
            Worker = Workers.Workers.GetValueOrDefault(slot),
            Running = _running.GetValueOrDefault(slot),
        }),
    ];

    /// <summary>995-B: a slot waiting for its claim to land.</summary>
    private sealed record PendingStart(Card Card, bool Resume, string Type, RunningWorker Holder);
}
