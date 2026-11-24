using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Pool;

namespace Scheduler.Core.Tests.Pool;

/// <summary>Shared by the pool tests: types, a model server that always answers, and a one-call WT-7 dispatch.</summary>
internal static class PoolFakes
{
    /// <summary>A type roomy enough for the test (<c>Max</c> 9, nothing running); the default type unless <paramref name="isDefault"/> is false (1146-A).</summary>
    public static DispatchType Type(
        string name,
        string agent = "",
        string model = "",
        bool isDefault = true,
        string areas = "",
        int max = 9,
        int running = 0,
        int[]? slots = null,
        params string[] projects) =>
        new() { Name = name, Agent = agent, Model = model, IsDefault = isDefault, Max = max, Running = running, Slots = slots ?? [], Projects = projects };

    /// <summary>A server cache whose every server answers.</summary>
    public static ServerCache Servers() => new(new UpProbe(), new FixedClock(), new NoLog());

    /// <summary>WT-7 over the given projects; every start succeeds unless <paramref name="start"/> says otherwise.</summary>
    public static IReadOnlyList<PoolOffer> StartAll(
        IReadOnlyList<PoolProject> projects,
        IReadOnlyList<DispatchType> types,
        DateTimeOffset now,
        int maxWorkers = 1,
        int totalRunning = 0,
        IEnumerable<PoolRun>? runs = null,
        IReadOnlyDictionary<string, SchedulerState>? states = null,
        AgentQuota? quota = null,
        bool migrated = false,
        Func<PoolOffer, bool>? start = null) =>
        PoolDispatcher.Dispatch(
            projects,
            states ?? new Dictionary<string, SchedulerState>(),
            maxWorkers,
            totalRunning,
            types,
            migrated,
            quota ?? new AgentQuota(),
            Servers(),
            runs ?? [],
            new FixedClock(now),
            _ => new Dictionary<string, int>(),
            start ?? (_ => true));

    private sealed class UpProbe : IServerProbe
    {
        public bool IsUp(string endpoint) => true;

        public string? ReadModels(string endpoint) => null;

        public string? ReadMetrics(string metricsUrl) => null;
    }

    private sealed class NoLog : ISchedulerLog
    {
        public void Write(string line)
        {
        }
    }

    private sealed class FixedClock(DateTimeOffset? now = null) : IClock
    {
        public DateTimeOffset Now => now ?? DateTimeOffset.UnixEpoch;
    }
}
