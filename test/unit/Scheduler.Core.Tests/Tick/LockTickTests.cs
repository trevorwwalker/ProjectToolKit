using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Locks;
using Scheduler.Core.Scopes;
using Scheduler.Core.Tick;

namespace Scheduler.Core.Tests.Tick;

/// <summary>
/// Dynamic locks in the tick (design <c>dynamic-locks.md</c>, section 6; DL-1, DL-8, 1018-D): with the
/// <c>locks</c> switch on and a host that keeps the lock table, the tick records held sets at dispatch and
/// when a claim lands, runs the lock step after the move step and before the plan, and gates on held
/// sets; off, it does none of that (test plan section 7, "Release", dispatch half).
/// </summary>
public sealed class LockTickTests
{
    private readonly TickClock _clock = new();
    private readonly TickProcesses _processes = new();
    private readonly TickLog _log = new();
    private readonly LockingTickHost _host;
    private readonly SchedulerTick _tick;
    private readonly List<TickStep> _steps = [];

    public LockTickTests()
    {
        _host = new LockingTickHost(_processes, _clock);
        _tick = new SchedulerTick(
            new SchedulerConfig { Project = "P" }, _clock, _log, new TickLoad(), new TickProbe(), _processes, new TickFiles(), _host)
        {
            Locks = new LockSettings { On = true },
        };
        _tick.StepStarting += _steps.Add;
    }

    private static Card Card(CardFolder folder, int num, string category, string scope) =>
        TickHost.Card(folder, num, category) with { ScopeLine = scope };

    // With locks off (the default), the tick neither reads nor writes the table nor runs the lock step.
    [Fact]
    public void LocksOff_NoLockStep_NoTable_DispatchAsBefore()
    {
        _tick.Locks = LockSettings.Off;
        _host.Cards.Add(Card(CardFolder.Queue, 10, "A", "src/a/"));

        _tick.Run();

        Assert.DoesNotContain(TickStep.Locks, _steps);
        Assert.Equal(0, _host.LockSteps);
        Assert.Equal(0, _host.Reads);
        Assert.Equal(0, _host.Saves);
        Assert.Equal([(1, "010-A-x")], _host.Posted);
    }


    // With locks on, a full tick runs every step of spec A3 and the lock step, in the enum's order.
    [Fact]
    public void LocksOn_FullTick_RunsEveryStepInOrder()
    {
        _tick.Run();

        Assert.Equal(Enum.GetValues<TickStep>(), _steps);
    }
    // The lock step runs after the move step (in the planner step) and before the plan.
    [Fact]
    public void LockStep_RunsAfterThePlannerStep_AndBeforeThePlan()
    {
        _tick.Run();

        var planner = _steps.IndexOf(TickStep.PlannerStep);
        var locks = _steps.IndexOf(TickStep.Locks);
        Assert.True(planner >= 0 && locks > planner && locks < _steps.IndexOf(TickStep.Plan), string.Join(",", _steps));
        Assert.Equal(1, _host.LockSteps);
    }

    // 1018-D: dispatch records the card as starting; the claim landing records its held set and age.
    [Fact]
    public void Claim_StartingAtDispatch_HeldWhenItLands()
    {
        _host.Cards.Add(Card(CardFolder.Queue, 10, "A", "src/a/"));
        _tick.Run();

        var starting = _host.Table.Find("010-A-x")!;
        Assert.Equal(CardLockState.Starting, starting.State);
        Assert.Empty(starting.Held);
        Assert.Equal(1, starting.Slot);

        _host.Claims[1] = ClaimState.Landed;
        _tick.Run();

        var held = _host.Table.Find("010-A-x")!;
        Assert.Equal(CardLockState.Running, held.State);
        Assert.Equal(["src/a/"], held.Held.Select(e => e.Path));
        Assert.Equal(new CardAge(1, 10), held.Age);
        Assert.Equal("P", held.Project);
    }

    // 1018-D: a failed claim drops the starting entry.
    [Fact]
    public void Claim_Failed_DropsTheStartingEntry()
    {
        _host.Cards.Add(Card(CardFolder.Queue, 10, "A", "src/a/"));
        _tick.Run();

        _host.Cards.Clear();
        _host.Claims[1] = ClaimState.Failed;
        _tick.Run();

        Assert.Null(_host.Table.Find("010-A-x"));
    }

    // A resumed card starts at once (its claim is already on main) and holds its claim and remembered set.
    [Fact]
    public void Resume_HoldsItsClaimAndRememberedSet()
    {
        _host.Table = LockTable.Empty()
            .Start("010-A-x", new CardAge(1, 10), Scope.Of("src/a/"), _clock.Now)
            .Grant("010-A-x", ["docs/x.md"], _clock.Now)
            .Release("010-A-x", LockRelease.Yielded);
        _host.Cards.Add(Card(CardFolder.Active, 10, "A", "src/a/"));

        _tick.Run();

        Assert.Equal([(1, "010-A", true)], _host.Starts);
        Assert.Equal(["src/a/", "docs/x.md"], _host.Table.Find("010-A-x")!.Held.Select(e => e.Path));
    }

    // Release, dispatch half (DL-8): a holder released in the lock step frees its files for dispatch in the same tick.
    [Fact]
    public void Release_InTheLockStep_FreesTheFilesForDispatchInTheSameTick()
    {
        _host.Table = LockTable.Empty()
            .Start("900-B-holder", new CardAge(1, 900), Scope.Of("src/b/"), _clock.Now, project: "Q")
            .Grant("900-B-holder", ["src/shared.cs"], _clock.Now);
        _host.Cards.Add(Card(CardFolder.Queue, 10, "A", "src/shared.cs"));

        _tick.Run();
        Assert.Empty(_host.Posted);
        Assert.Contains(_tick.LastPlan!.Notes, n => n.Card == "010-A-x" && n.Note.Contains("900-B-holder", StringComparison.Ordinal));

        _host.OnLockStep = t => t.Release("900-B-holder", LockRelease.Finished);
        _tick.Run();

        Assert.Equal([(1, "010-A-x")], _host.Posted);
    }

    // A lock request wakes a light tick: with main unchanged it runs the lock step and dispatches nothing.
    [Fact]
    public void LockRequestWake_IsLight_ButRunsTheLockStep()
    {
        _host.Sha = "abc";
        _tick.Run();
        _host.Cards.Add(Card(CardFolder.Queue, 10, "A", "src/a/"));
        var before = _host.LockSteps;

        _tick.Run(TickWake.LockRequest);

        Assert.Equal(before + 1, _host.LockSteps);
        Assert.Empty(_host.Posted);
    }
}

/// <summary>A tick host that claims off the tick and keeps a lock table in memory.</summary>
internal sealed class LockingTickHost(TickProcesses processes, TickClock clock) : ITickHost, IClaimingHost, ILockingHost
{
    private readonly TickHost _inner = new(processes, clock);

    public List<Card> Cards => _inner.Cards;

    public string? Sha { get => _inner.Sha; set => _inner.Sha = value; }

    public List<(int Slot, string Card, bool Started)> Starts => _inner.Starts;

    public List<(int Slot, string Card)> Posted { get; } = [];

    public Dictionary<int, ClaimState> Claims { get; } = [];

    public LockTable Table { get; set; } = LockTable.Empty();

    public Func<LockTable, LockTable>? OnLockStep { get; set; }

    public int LockSteps { get; private set; }

    public int Reads { get; private set; }

    public int Saves { get; private set; }

    public LockTable ReadLocks()
    {
        Reads++;
        return Table;
    }

    public void SaveLocks(LockTable table)
    {
        Saves++;
        Table = table;
    }

    public void LockStep()
    {
        LockSteps++;
        if (OnLockStep is { } step)
        {
            Table = step(Table);
        }
    }

    public bool PostClaim(int slot, Card card, bool resume)
    {
        if (resume || card.Folder != CardFolder.Queue)
        {
            return false;
        }

        Posted.Add((slot, card.Name));
        Claims[slot] = ClaimState.Pending;
        return true;
    }

    public ClaimState ClaimOf(int slot) => Claims.GetValueOrDefault(slot, ClaimState.Failed);

    public void IndexLogs() => _inner.IndexLogs();

    public IReadOnlyList<Card>? ReadCards() => _inner.ReadCards();

    public bool FixDuplicates(IReadOnlyList<Card> cards) => _inner.FixDuplicates(cards);

    public void PlannerStep() => _inner.PlannerStep();

    public string? MainSha() => _inner.MainSha();

    public string? ReadCategories() => _inner.ReadCategories();

    public string? ReadWorkers() => _inner.ReadWorkers();

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ReadModels() => _inner.ReadModels();

    public IReadOnlyList<JobRequest> ReadRequests() => _inner.ReadRequests();

    public RunningWorker Watch(RunningWorker worker) => _inner.Watch(worker);

    public SchedulerState Finish(RunningWorker worker, SchedulerState state, IDictionary<string, int> retries) =>
        _inner.Finish(worker, state, retries);

    public RunningWorker? StartWorker(int slot, Card card, bool resume) => _inner.StartWorker(slot, card, resume);

    public RunningWorker? StartWorker(int slot, Card card, bool resume, string type) => _inner.StartWorker(slot, card, resume, type);

    public void SaveRetries(IReadOnlyDictionary<string, int> retries) => _inner.SaveRetries(retries);

    public void WriteStatus(IReadOnlyList<SlotState> slots, SchedulerState state) => _inner.WriteStatus(slots, state);
}
