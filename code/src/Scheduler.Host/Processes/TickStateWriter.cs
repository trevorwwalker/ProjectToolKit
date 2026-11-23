using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;

namespace Scheduler.Host.Processes;

/// <summary>
/// The state files a tick ends with: retries.json (TIME-22), then running.json and status.txt
/// (ADOPT-1, ADOPT-3), in the order <c>Tick</c> and <c>Write-StatusFile</c> wrote them.
/// </summary>
public sealed class TickStateWriter(IStateFiles files, IClock clock)
{
    /// <summary>Write all three.</summary>
    public void Write(
        string state,
        SchedulerConfig config,
        int slots,
        Func<int, bool> slotEnabled,
        IReadOnlyDictionary<int, RunningWorker> running,
        Func<int, WorkerStats?>? stats,
        IReadOnlyDictionary<string, int> retries,
        IReadOnlyList<Card> cards,
        IReadOnlyList<WorkerType>? types = null)
    {
        RetriesFile.Save(files, retries);
        files.Write(StateFile.Running, RunningJson.Write(running.Values, stats));
        files.Write(StateFile.Status, StatusFile.Build(clock.Now, state, config, slots, slotEnabled, running, cards, types));
    }
}
