// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Tests.Program;

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

internal sealed class TickProcesses : IWorkerProcesses
{
    public Dictionary<int, DateTimeOffset> Alive { get; } = [];

    public List<(string CommandLine, string JobName, string WorkingDirectory)> Started { get; } = [];

    public StartedProcess? Start(string commandLine, string jobName, string workingDirectory,
        IReadOnlyDictionary<string, string?> environment)
    {
        Started.Add((commandLine, jobName, workingDirectory));
        return new StartedProcess { Pid = 4242, Start = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero) };
    }

    public bool IsAlive(int pid, DateTimeOffset start) => Alive.TryGetValue(pid, out var s) && s == start;

    public DateTimeOffset? CmdStartTime(int pid) => Alive.TryGetValue(pid, out var s) ? s : null;

    public void EndJob(string jobName, int pid) => Alive.Remove(pid);

    public int? ExitCode(int pid) => Alive.ContainsKey(pid) ? null : 0;

    public long? LogSize(string path) => 0;

    public DateTimeOffset? LogLastWrite(string path) => null;
}

/// <summary>The host side of a tick: cards in memory; starts recorded (a real start would run a process).</summary>
internal sealed class TickHost : ITickHost
{
    public List<Card> Cards { get; } = [];

    public List<(int Slot, string Card, bool Resume)> Starts { get; } = [];

    public void IndexLogs() { }

    public IReadOnlyList<Card>? ReadCards() => [.. Cards];

    public bool FixDuplicates(IReadOnlyList<Card> cards) => false;

    public void PlannerStep() { }

    public string? ReadCategories() => null;

    public string? ReadWorkers() => """{"slots":2}""";

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ReadModels() => new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<JobRequest> ReadRequests() => [];

    public RunningWorker Watch(RunningWorker worker) => worker;

    public SchedulerState Finish(RunningWorker worker, SchedulerState state, IDictionary<string, int> retries) => state;

    public RunningWorker? StartWorker(int slot, Card card, bool resume)
    {
        Starts.Add((slot, card.Id, resume));
        return null;
    }

    public void SaveRetries(IReadOnlyDictionary<string, int> retries) { }

    public void WriteStatus(IReadOnlyList<SlotState> slots, SchedulerState state) { }

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
