using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.Health;
using Scheduler.Host.Slots;
using Scheduler.Host.State;

namespace Scheduler.Host.Processes;

/// <summary>
/// What happens after a worker has finished and its result is settled (TIME-20): log line,
/// accounting row, health check wanted, recent list, job ended, slot freed, raw stream closed,
/// slot files cleared. The result itself (and the retry counts) are Core's <c>RunResults.Settle</c>.
/// </summary>
public sealed class WorkerFinisher(
    IClock clock,
    ISchedulerLog log,
    AccountingCsv accounting,
    HealthCheck health,
    IWorkerProcesses processes,
    SlotFiles slotFiles)
{
    /// <summary>
    /// Do the bookkeeping for <paramref name="worker"/>, which ended as <paramref name="result"/>
    /// and ran on the model <paramref name="model"/> (the accounting column), and remove it from
    /// <paramref name="running"/>. <paramref name="closeRawStream"/> runs after the job ended: a live
    /// build inside a wait could open the stream again before.
    /// </summary>
    public void Finish(RunningWorker worker, string result, string model, IDictionary<int, RunningWorker> running,
        Action<RunningWorker>? closeRawStream = null)
    {
        var now = clock.Now;
        var minutes = AccountingRow.MinutesOf(now - worker.Start);
        log.Write($"slot {worker.Slot} finished: {worker.Card} -> {result} after {minutes} min");
        accounting.Add(new AccountingRow
        {
            Finished = now,
            Card = worker.Card,
            Category = worker.Category,
            Slot = worker.Slot,
            Result = result,
            Minutes = minutes,
            LogBytes = processes.LogSize(worker.RawLog) ?? 0,
            StopReason = worker.StopWhy,
            Model = model,
            CardType = worker.CardType,
            Version = worker.CardVersion,
        }, worker.Log, worker.Type);
        health.RunFinished();
        processes.EndJob(worker.Job, worker.Pid); // anything the worker left running goes with it
        running.Remove(worker.Slot);
        closeRawStream?.Invoke(worker);
        slotFiles.Clear(worker.Slot);
    }
}
