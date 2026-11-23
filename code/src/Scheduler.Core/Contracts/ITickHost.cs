namespace Scheduler.Core.Contracts;

/// <summary>
/// The steps of a tick (spec A3, <c>Tick</c> L2795) that need git, files or processes, which Host
/// implements (cards from main 040-B, worker processes 240-B, the planner in git 260-B). Core's
/// <c>SchedulerTick</c> calls them in the spec's order around its own areas.
/// </summary>
public interface ITickHost
{
    /// <summary>Index logs: refresh the list of worker logs the page shows.</summary>
    void IndexLogs();

    /// <summary>Read cards from main (CARD-1); null when git failed, so the previous list is kept (CARD-3).</summary>
    IReadOnlyList<Card>? ReadCards();

    /// <summary>
    /// Main's commit sha now (981-A), or null when it is not known; a light wake re-reads the cards
    /// only when it differs from the sha they were read at, and a null sha always re-reads them.
    /// </summary>
    string? MainSha() => null;

    /// <summary>Fix duplicates (CARD-19); true when a queue/ copy was removed and the cards must be re-read.</summary>
    bool FixDuplicates(IReadOnlyList<Card> cards);

    /// <summary>The planner step: its outbox committed to main (PLAN-2).</summary>
    void PlannerStep();

    /// <summary>categories.json on main, or null when there is none.</summary>
    string? ReadCategories();

    /// <summary>workers.json on main, or null when there is none.</summary>
    string? ReadWorkers();

    /// <summary>
    /// Read models.json; by model, the other names it answers to in a <c>Worker:</c> line:
    /// <c>Model-Short</c> of it and models.json's <c>short</c> (DISP-18, L2715).
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> ReadModels();

    /// <summary>The requests in the jobs folder's requests/ (REQ rules).</summary>
    IReadOnlyList<JobRequest> ReadRequests();

    /// <summary>
    /// Watch a live worker: time limits, warnings and stop requests (TIME-1 to TIME-12); returns the
    /// worker as it is after the watch.
    /// </summary>
    RunningWorker Watch(RunningWorker worker);

    /// <summary>
    /// A worker that is no longer alive: its result, retries and the bookkeeping after it
    /// (TIME-14 to TIME-20). Returns the scheduler's state after it (out of quota pauses it).
    /// </summary>
    SchedulerState Finish(RunningWorker worker, SchedulerState state, IDictionary<string, int> retries);

    /// <summary>Start-Worker: start <paramref name="card"/> on <paramref name="slot"/>; null when the start failed.</summary>
    RunningWorker? StartWorker(int slot, Card card, bool resume);

    /// <summary>
    /// Start-Worker as the dispatcher asked for it (WT-6, 959-A): <paramref name="type"/> is the type
    /// WT-5 chose, so the worker starts with exactly that type's agent, model, endpoint and env - the
    /// folder number says nothing about who runs in it (WT-W1, WT-W3). The tick calls this one; a host
    /// that picks its own settings (a dry run, observe) inherits the forwarding to the slot form.
    /// </summary>
    RunningWorker? StartWorker(int slot, Card card, bool resume, string type) => StartWorker(slot, card, resume);

    /// <summary>Save the retry counts (TIME-22).</summary>
    void SaveRetries(IReadOnlyDictionary<string, int> retries);

    /// <summary>Write running.json and status.txt (ADOPT-1, ADOPT-3).</summary>
    void WriteStatus(IReadOnlyList<SlotState> slots, SchedulerState state);
}
