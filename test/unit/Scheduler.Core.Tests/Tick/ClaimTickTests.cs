using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;

namespace Scheduler.Core.Tests.Tick;

/// <summary>
/// 995-B: the claim at dispatch leaves the tick. A host that claims off the tick (<see cref="IClaimingHost"/>)
/// is handed the claim and the slot shows the card as starting; the worker starts at a later tick, once
/// the claim is on main (CM-9), and a failed claim frees the slot.
/// </summary>
public sealed class ClaimTickTests
{
    private readonly TickClock _clock = new();
    private readonly TickProcesses _processes = new();
    private readonly TickLog _log = new();
    private readonly ClaimingTickHost _host;
    private readonly SchedulerTick _tick;

    public ClaimTickTests()
    {
        _host = new ClaimingTickHost(_processes, _clock);
        _tick = new SchedulerTick(
            new SchedulerConfig(), _clock, _log, new TickLoad(), new TickProbe(), _processes, new TickFiles(), _host);
    }

    [Fact]
    public void DispatchTick_PostsTheClaim_AndStartsNoWorker()
    {
        _host.Cards.Add(TickHost.Card(CardFolder.Queue, 10, "A"));

        _tick.Run();

        Assert.Equal([(1, "010-A-x")], _host.Posted);
        Assert.Empty(_host.Starts);
        Assert.Empty(_tick.Running);
        Assert.Equal("010-A-x", _tick.Starting[1].Card);
    }

    [Fact]
    public void ClaimLands_TheWorkerStartsAtTheNextTick_InTheSameSlot()
    {
        _host.Cards.Add(TickHost.Card(CardFolder.Queue, 10, "A"));
        _tick.Run();

        _tick.Run();   // still pending: nothing starts, and the card is not dispatched again
        Assert.Empty(_host.Starts);
        Assert.Single(_host.Posted);

        _host.Claims[1] = ClaimState.Landed;
        _tick.Run();

        Assert.Equal([(1, "010-A", true)], _host.Starts);
        Assert.Equal("010-A", _tick.Running[1].Id);
        Assert.Empty(_tick.Starting);
        Assert.Single(_host.Posted);
    }

    [Fact]
    public void ClaimFails_TheSlotIsFreed_AndNoWorkerStarts()
    {
        _host.Cards.Add(TickHost.Card(CardFolder.Queue, 10, "A"));
        _tick.Run();

        _host.Cards.Clear();   // someone else moved the card
        _host.Claims[1] = ClaimState.Failed;
        _tick.Run();

        Assert.Empty(_host.Starts);
        Assert.Empty(_tick.Running);
        Assert.Empty(_tick.Starting);
        Assert.Contains(_log.Lines, l => l.Contains("claim of 010-A-x failed", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoDispatchesInOneTick_PostTwoClaimsInOrder_InTwoSlots()
    {
        _host.Cards.AddRange([TickHost.Card(CardFolder.Queue, 10, "A"), TickHost.Card(CardFolder.Queue, 20, "B")]);

        _tick.Run();

        Assert.Equal([(1, "010-A-x"), (2, "020-B-x")], _host.Posted);
        Assert.Equal(2, _tick.Starting.Count);
    }

    [Fact]
    public void StartingSlot_HoldsItsSlot_AgainstTheNextCard()
    {
        _host.Workers = """{"slots":1}""";
        _host.Cards.Add(TickHost.Card(CardFolder.Queue, 10, "A"));
        _tick.Run();

        _host.Cards.Add(TickHost.Card(CardFolder.Queue, 20, "B"));
        _tick.Run();

        Assert.Equal([(1, "010-A-x")], _host.Posted);
    }

    [Fact]
    public void ResumedCard_IsNotClaimed_AndStartsInTheTick()
    {
        _host.Cards.Add(TickHost.Card(CardFolder.Active, 10, "A"));

        _tick.Run();

        Assert.Empty(_host.Posted);
        Assert.Equal([(1, "010-A", true)], _host.Starts);
    }
}

/// <summary>A tick host that claims off the tick: queued cards are posted; the test says when each lands.</summary>
internal sealed class ClaimingTickHost(TickProcesses processes, TickClock clock) : ITickHost, IClaimingHost
{
    private readonly TickHost _inner = new(processes, clock);

    public List<Card> Cards => _inner.Cards;

    public string? Workers { get => _inner.Workers; set => _inner.Workers = value; }

    public List<(int Slot, string Card, bool Started)> Starts => _inner.Starts;

    public List<(int Slot, string Card)> Posted { get; } = [];

    public Dictionary<int, ClaimState> Claims { get; } = [];

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
