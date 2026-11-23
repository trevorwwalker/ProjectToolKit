using Scheduler.Core.Contracts;

namespace Scheduler.Host.Program;

/// <summary>
/// Dry run (DISP-21, <c>Start-Worker</c> L1785): a start is only logged as
/// "DRY RUN slot N &lt;- card" and no process starts and no slot is prepared; everything else is the
/// inner host's.
/// </summary>
public sealed class DryRunTickHost(ITickHost inner, ISchedulerLog log, Func<int, Card, string>? typeOf = null) : ITickHost
{
    /// <summary>MP-22: the project's name in a pool, which the dry run's lines carry; null for one project.</summary>
    public string? Project { get; init; }

    /// <inheritdoc />
    public RunningWorker? StartWorker(int slot, Card card, bool resume, string type) =>
        // MP-22: in a pool the type is the one the pool's dispatch chose; one project's line is as it always was (MP-C9).
        Project is { Length: > 0 } && type.Length > 0 ? Log(slot, card, resume, type) : StartWorker(slot, card, resume);

    /// <inheritdoc />
    public RunningWorker? StartWorker(int slot, Card card, bool resume) =>
        Log(slot, card, resume, typeOf?.Invoke(slot, card) ?? "");

    private RunningWorker? Log(int slot, Card card, bool resume, string chosen)
    {
        // WT-14: "DRY RUN <type> in slot <n> <- <card>" when the host can say the type.
        // MP-22: in a pool the line names the project too.
        log.Write(chosen is { Length: > 0 } type
            ? Project is { Length: > 0 } project
                ? Scheduler.Core.Pool.PoolDispatcher.DryRunLine(project, type, slot, card, resume)
                : Scheduler.Core.Dispatch.TypeDispatcher.DryRunLine(type, slot, card, resume)
            : $"DRY RUN {(Project is { Length: > 0 } p ? p + " " : "")}slot {slot} <- {card.Name}{(resume ? " (resume)" : "")}");
        return null;
    }

    /// <inheritdoc />
    public void IndexLogs() => inner.IndexLogs();

    /// <inheritdoc />
    public IReadOnlyList<Card>? ReadCards() => inner.ReadCards();

    /// <inheritdoc />
    public bool FixDuplicates(IReadOnlyList<Card> cards) => inner.FixDuplicates(cards);

    /// <inheritdoc />
    public void PlannerStep() => inner.PlannerStep();

    /// <inheritdoc />
    public string? ReadCategories() => inner.ReadCategories();

    /// <inheritdoc />
    public string? ReadWorkers() => inner.ReadWorkers();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ReadModels() => inner.ReadModels();

    /// <inheritdoc />
    public IReadOnlyList<JobRequest> ReadRequests() => inner.ReadRequests();

    /// <inheritdoc />
    public RunningWorker Watch(RunningWorker worker) => inner.Watch(worker);

    /// <inheritdoc />
    public SchedulerState Finish(RunningWorker worker, SchedulerState state, IDictionary<string, int> retries) =>
        inner.Finish(worker, state, retries);

    /// <inheritdoc />
    public void SaveRetries(IReadOnlyDictionary<string, int> retries) => inner.SaveRetries(retries);

    /// <inheritdoc />
    public void WriteStatus(IReadOnlyList<SlotState> slots, SchedulerState state) => inner.WriteStatus(slots, state);
}
