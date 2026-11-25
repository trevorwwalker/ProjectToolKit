// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;

namespace Scheduler.Core.Tests.Tick;

internal sealed class TickClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
}

internal sealed class TickLog : ISchedulerLog
{
    public List<string> Lines { get; } = [];

    public void Write(string line) => Lines.Add(line);
}

internal sealed class TickLoad : IHostLoad
{
    public HostLoadSample Sample() => new() { CpuPercent = 10, FreeMemoryBytes = 1, TotalMemoryBytes = 2 };
}

internal sealed class TickProbe : IServerProbe
{
    public bool IsUp(string endpoint) => true;

    public string? ReadModels(string endpoint) => null;

    public string? ReadMetrics(string metricsUrl) => null;
}

internal sealed class TickFiles : IStateFiles
{
    public Dictionary<StateFile, string> Files { get; } = [];

    public string? Read(StateFile file) => Files.GetValueOrDefault(file);

    public void Write(StateFile file, string text) => Files[file] = text;
}

/// <summary>Processes: a pid is alive while it is in <see cref="Alive"/>.</summary>
internal sealed class TickProcesses : IWorkerProcesses
{
    public Dictionary<int, DateTimeOffset> Alive { get; } = [];

    public StartedProcess? Start(string commandLine, string jobName, string workingDirectory,
        IReadOnlyDictionary<string, string?> environment) => null;

    public bool IsAlive(int pid, DateTimeOffset start) => Alive.TryGetValue(pid, out var s) && s == start;

    public DateTimeOffset? CmdStartTime(int pid) => Alive.TryGetValue(pid, out var s) ? s : null;

    public void EndJob(string jobName, int pid) => Alive.Remove(pid);

    public int? ExitCode(int pid) => Alive.ContainsKey(pid) ? null : 0;

    public long? LogSize(string path) => 0;

    public DateTimeOffset? LogLastWrite(string path) => null;
}

/// <summary>Host's side of a tick: cards in memory, starts that succeed unless told not to, every call recorded.</summary>
internal sealed class TickHost(TickProcesses processes, TickClock clock) : ITickHost
{
    private int _nextPid = 1000;

    public List<Card> Cards { get; } = [];

    public string? Workers { get; set; } = """{"slots":2}""";

    public HashSet<string> FailToStart { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(int Slot, string Card, bool Started)> Starts { get; } = [];

    /// <summary>The type the tick named for each start (959-A; the 4-argument Start-Worker).</summary>
    public List<(int Slot, string Card, string Type)> TypeStarts { get; } = [];

    public List<string> Finished { get; } = [];
    /// <summary>What Finish returns instead of the state it was given, or null (an out-of-quota run says paused-quota).</summary>
    public SchedulerState? FinishState { get; set; }


    public List<TickStep> Calls { get; } = [];

    public void IndexLogs() => Calls.Add(TickStep.IndexLogs);

    public IReadOnlyList<Card>? ReadCards()
    {
        Calls.Add(TickStep.ReadCards);
        return FailReadCards ? null : [.. Cards];
    }

    /// <summary>ReadCards fails (git failed, CARD-3) while this is set.</summary>
    public bool FailReadCards { get; set; }

    /// <summary>When set, the next FixDuplicates runs it and says a copy was removed (981-A).</summary>
    public Action? FixDuplicatesOnce { get; set; }

    public bool FixDuplicates(IReadOnlyList<Card> cards)
    {
        Calls.Add(TickStep.FixDuplicates);
        if (FixDuplicatesOnce is { } fix)
        {
            FixDuplicatesOnce = null;
            fix();
            return true;
        }

        return false;
    }

    public void PlannerStep()
    {
        Calls.Add(TickStep.PlannerStep);
        OnPlannerStep?.Invoke();
    }

    /// <summary>Main's sha as MainSha reports it (981-A); null is "not known".</summary>
    public string? Sha { get; set; }

    /// <summary>What the planner step does besides recording the call (981-A: commit a batch, moving main).</summary>
    public Action? OnPlannerStep { get; set; }

    public string? MainSha() => Sha;

    public string? ReadCategories()
    {
        Calls.Add(TickStep.ReadCategories);
        return null;
    }

    public string? ReadWorkers()
    {
        Calls.Add(TickStep.ReadWorkers);
        return Workers;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ReadModels()
    {
        Calls.Add(TickStep.ReadModels);
        return new Dictionary<string, IReadOnlyList<string>>();
    }

    public IReadOnlyList<JobRequest> ReadRequests()
    {
        Calls.Add(TickStep.ReadRequests);
        return [];
    }

    public RunningWorker Watch(RunningWorker worker) => worker;

    public SchedulerState Finish(RunningWorker worker, SchedulerState state, IDictionary<string, int> retries)
    {
        Finished.Add(worker.Id);
        return FinishState ?? state;
    }

    public RunningWorker? StartWorker(int slot, Card card, bool resume)
    {
        var ok = !FailToStart.Contains(card.Id);
        Starts.Add((slot, card.Id, ok));
        if (!ok)
        {
            return null;
        }

        var pid = _nextPid++;
        var start = clock.Now;
        processes.Alive[pid] = start;
        return Worker(slot, pid, start, card, resume);
    }

    // 959-A: what the tick hands the host for a start - the type WT-5 chose goes with it.
    public RunningWorker? StartWorker(int slot, Card card, bool resume, string type)
    {
        TypeStarts.Add((slot, card.Id, type));
        return StartWorker(slot, card, resume);
    }

    public void SaveRetries(IReadOnlyDictionary<string, int> retries) => Calls.Add(TickStep.SaveRetries);

    public void WriteStatus(IReadOnlyList<SlotState> slots, SchedulerState state) => Calls.Add(TickStep.WriteStatus);

    public static RunningWorker Worker(int slot, int pid, DateTimeOffset start, Card card, bool resume = false) => new()
    {
        Slot = slot,
        Pid = pid,
        Start = start,
        Card = card.Name,
        Category = card.Category,
        Id = card.Id,
        LastProgress = start,
        Resume = resume,
    };

    public static Card Card(CardFolder folder, int num, string category, string status = "ready") => new()
    {
        Folder = folder,
        Path = $"docs/jobs/{folder.ToString().ToLowerInvariant()}/{num:000}-{category}-x.md",
        Name = $"{num:000}-{category}-x",
        Num = num,
        Category = category,
        Id = $"{num:000}-{category}",
        Status = status,
    };
}
