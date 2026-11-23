using Scheduler.Core.Contracts;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>
/// The tick's host side for these tests: categories.json and workers.json as text the test chooses,
/// cards in memory, and the starts and status writes counted rather than done.
/// </summary>
internal sealed class ActionTickHost : ITickHost
{
    public List<Card> Cards { get; } = [];

    /// <summary>
    /// When set, <see cref="ReadCards"/> reads from here instead of <see cref="Cards"/>: for a test
    /// whose cards come from main through <see cref="Scheduler.Host.Git.CardReader"/> (949-B).
    /// </summary>
    public Func<IReadOnlyList<Card>?>? ReadCardsFrom { get; set; }

    public string? CategoriesJson { get; set; }

    public string WorkersJson { get; set; } = """{"slots":2}""";

    public List<(int Slot, string Card, bool Resume)> Starts { get; } = [];

    public int TicksWritten { get; private set; }

    public SchedulerState LastState { get; private set; }

    public void IndexLogs()
    {
    }

    public IReadOnlyList<Card>? ReadCards()
    {
        Reads++;
        return ReadCardsFrom is { } read ? read() : [.. Cards];
    }

    /// <summary>982-B: how many times the tick read the cards.</summary>
    public int Reads { get; private set; }

    /// <summary>982-B: main's sha as the tick sees it; null (the default) means not known.</summary>
    public string? Sha { get; set; }

    public string? MainSha() => Sha;

    public bool FixDuplicates(IReadOnlyList<Card> cards) => false;

    public void PlannerStep()
    {
    }

    public string? ReadCategories() => CategoriesJson;

    public string? ReadWorkers() => WorkersJson;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ReadModels() => new Dictionary<string, IReadOnlyList<string>>();

    public List<JobRequest> Requests { get; } = [];

    public IReadOnlyList<JobRequest> ReadRequests() => [.. Requests];

    public RunningWorker Watch(RunningWorker worker) => worker;

    public SchedulerState Finish(RunningWorker worker, SchedulerState state, IDictionary<string, int> retries) => state;

    public RunningWorker? StartWorker(int slot, Card card, bool resume)
    {
        Starts.Add((slot, card.Id, resume));
        return null;
    }

    public void SaveRetries(IReadOnlyDictionary<string, int> retries)
    {
    }

    public void WriteStatus(IReadOnlyList<SlotState> slots, SchedulerState state)
    {
        TicksWritten++;
        LastState = state;
    }
}

/// <summary>
/// The process table: the workers the test says are alive, and every job the page actions ended.
/// Nothing here starts a real process (test-plans/index.md: a fake CLI, never a real agent).
/// </summary>
internal sealed class ActionProcesses : IWorkerProcesses
{
    public Dictionary<int, DateTimeOffset> Alive { get; } = [];

    public List<(string Job, int Pid)> Ended { get; } = [];

    public StartedProcess? Start(string commandLine, string jobName, string workingDirectory,
        IReadOnlyDictionary<string, string?> environment) => null;

    public bool IsAlive(int pid, DateTimeOffset start) => Alive.TryGetValue(pid, out var s) && s == start;

    public DateTimeOffset? CmdStartTime(int pid) => Alive.GetValueOrDefault(pid);

    public void EndJob(string jobName, int pid)
    {
        Ended.Add((jobName, pid));
        Alive.Remove(pid);
    }

    public int? ExitCode(int pid) => Alive.ContainsKey(pid) ? null : 0;

    public long? LogSize(string path) => null;

    public DateTimeOffset? LogLastWrite(string path) => null;
}

/// <summary>Main with nothing on it: the tests that do not commit use this.</summary>
internal sealed class NullMain : IMainRepository
{
    public bool Fetch() => true;

    public IReadOnlyList<TreeEntry>? ListTree(string folder) => [];

    public string? ReadBlob(string blobId) => null;

    public string? ReadFile(string path) => null;

    public string? HeadSha() => "0000000";
}

/// <summary>The commits the page asked for, kept rather than pushed.</summary>
internal sealed class RecordingCommitter : IJobsCommitter
{
    public List<JobsChange> Changes { get; } = [];

    public string? FailWith { get; set; }

    public CommitResult Apply(JobsChange change)
    {
        if (FailWith is { } error)
        {
            return new CommitResult { Ok = false, Error = error };
        }

        Changes.Add(change);
        return new CommitResult { Ok = true, Sha = "abc1234" };
    }
}
