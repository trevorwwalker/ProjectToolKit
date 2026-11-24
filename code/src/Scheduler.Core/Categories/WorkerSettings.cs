using System.Text.Json;
using System.Text.Json.Nodes;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Categories;

/// <summary>
/// The workers, worker count and worker types of workers.json on main (WRK-1, WRK-2, WT-C10;
/// <c>Apply-Workers</c> L2557). A file with a <c>pool</c> gives the types directly (WT-C7); without
/// one they are derived from its slots and workers, and today's slot count goes on working (WT-C10).
/// </summary>
public sealed class WorkerSettings
{
    private readonly ISchedulerLog _log;
    private readonly SchedulerConfig _config;
    private readonly string _workersRel;
    private string? _seen;

    /// <summary>Starts from the config's worker count and the one type its <c>worker</c> section makes.</summary>
    public WorkerSettings(ISchedulerLog log, SchedulerConfig config, string workersRel)
    {
        _log = log;
        _config = config;
        _workersRel = workersRel;
        Slots = config.Slots;
        Pool = WorkerTypesJson.FromSlots(config, config.Slots, new Dictionary<int, WorkerDefinition>());
    }

    /// <summary>The workers by slot number.</summary>
    public Dictionary<int, WorkerDefinition> Workers { get; } = [];

    /// <summary>
    /// The worker types (WT-C10): the file's <c>pool</c> when it has one, or the types its
    /// <c>slots</c> and <c>workers</c> migrate to. Set by <see cref="Apply"/>; invalid text leaves it
    /// as it was (WT-C6).
    /// </summary>
    public WorkerPool Pool { get; private set; }

    /// <summary>The number of slots now.</summary>
    public int Slots { get; private set; }

    /// <summary>Which slots are enabled; a slot new to the count starts enabled (WRK-2).</summary>
    public Dictionary<int, bool> Enabled { get; } = [];

    /// <summary>
    /// Apply workers.json's text: only when it differs from the text applied last, and invalid
    /// JSON is logged and changes nothing (WRK-1). <paramref name="runningSlots"/> are the slots
    /// with a worker, which the count never drops below (WRK-2), unless the command line pinned
    /// it (CFG-8). True when it was applied.
    /// </summary>
    public bool Apply(string text, IEnumerable<int> runningSlots)
    {
        if (text == _seen)
        {
            return false;
        }

        JsonObject? root;
        try
        {
            root = ConfigJson.Root(text);
        }
        catch (JsonException)
        {
            _log.Write($"{_workersRel} on main is not valid JSON; keeping the previous worker settings");
            return false;
        }

        _seen = text;
        Workers.Clear();

        // WT-C10: a workers.json with a pool uses it, and its slots and workers entries are ignored.
        if (WorkerTypesJson.HasPool(root))
        {
            var pool = WorkerTypesJson.Parse(root, _config, _log);
            if (pool is null)
            {
                return false; // WT-C6: logged; the previous types stay
            }

            Pool = pool;
            SetCount(pool.MaxWorkers, runningSlots, $"workers: {pool.MaxWorkers} from the pool in {_workersRel}");
            _log.Write($"{_workersRel}: {pool.Types.Count} worker type(s); its slots and workers entries are ignored");
            return true;
        }

        foreach (var (slot, worker) in WorkerTypesJson.ReadWorkers(root))
        {
            Workers[slot] = worker;
        }

        var n = ConfigJson.GetInt(root, "slots") ?? 0;
        if (!_config.SlotsPinned && n > 0)
        {
            SetCount(n, runningSlots, $"workers: {n} (from {_workersRel})");
        }

        // WT-C10: without a pool the types are derived from the slots and workers, in memory only.
        Pool = WorkerTypesJson.WithSlotDefault(WorkerTypesJson.FromSlots(_config, Slots, Workers, Enabled), root);
        return true;
    }

    // The worker count as WRK-2 has it: never below the highest slot with a worker, new slots start
    // enabled, and -Slots keeps the count where the command line put it (CFG-8).
    private void SetCount(int wanted, IEnumerable<int> runningSlots, string logLine)
    {
        var n = wanted;
        var top = runningSlots.DefaultIfEmpty(0).Max();
        if (top > n)
        {
            n = top;
        }

        if (_config.SlotsPinned || n == Slots)
        {
            return;
        }

        _log.Write(logLine);
        for (var i = Slots + 1; i <= n; i++)
        {
            Enabled.TryAdd(i, true);
        }

        Slots = n;
    }
}
