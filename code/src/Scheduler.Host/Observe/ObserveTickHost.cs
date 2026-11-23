using Scheduler.Core.Contracts;

namespace Scheduler.Host.Observe;

/// <summary>
/// The observe run's tick host (roadmap item 1): like dry run (DISP-21) a start is only logged,
/// here as an <see cref="ObserveLine"/> <c>slot N &lt;- card[ (resume)]</c>. Everything that would
/// write to git or touch the old scheduler's workers is left to the old scheduler: no duplicate fix
/// (CARD-19), no planner step (PLAN-1), no watchdog (TIME-3) and no finish (TIME-20), which is only
/// logged as <c>slot N finished: card</c>. Reading, and the own log folder's files, are the inner host's.
/// </summary>
public sealed class ObserveTickHost(ITickHost inner, ISchedulerLog log, IClock clock) : ITickHost
{
    /// <inheritdoc />
    public RunningWorker? StartWorker(int slot, Card card, bool resume)
    {
        log.Write(ObserveLine.Format(clock.Now, $"slot {slot} <- {card.Name}{(resume ? " (resume)" : "")}"));
        return null;
    }

    /// <inheritdoc />
    public void IndexLogs() => inner.IndexLogs();

    /// <inheritdoc />
    public IReadOnlyList<Card>? ReadCards() => inner.ReadCards();

    /// <inheritdoc />
    public bool FixDuplicates(IReadOnlyList<Card> cards) => false;

    /// <inheritdoc />
    public void PlannerStep()
    {
    }

    /// <inheritdoc />
    public string? ReadCategories() => inner.ReadCategories();

    /// <inheritdoc />
    public string? ReadWorkers() => inner.ReadWorkers();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ReadModels() => inner.ReadModels();

    /// <inheritdoc />
    public IReadOnlyList<JobRequest> ReadRequests() => inner.ReadRequests();

    /// <inheritdoc />
    public RunningWorker Watch(RunningWorker worker) => worker;

    /// <inheritdoc />
    public SchedulerState Finish(RunningWorker worker, SchedulerState state, IDictionary<string, int> retries)
    {
        log.Write(ObserveLine.Format(clock.Now, $"slot {worker.Slot} finished: {worker.Card}"));
        return state;
    }

    /// <inheritdoc />
    public void SaveRetries(IReadOnlyDictionary<string, int> retries) => inner.SaveRetries(retries);

    /// <inheritdoc />
    public void WriteStatus(IReadOnlyList<SlotState> slots, SchedulerState state) => inner.WriteStatus(slots, state);
}
