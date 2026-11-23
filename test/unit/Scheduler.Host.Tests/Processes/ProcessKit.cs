using Scheduler.Core.Contracts;
using Scheduler.Core.Health;
using Scheduler.Host.Slots;

namespace Scheduler.Host.Tests.Processes;

/// <summary>Builders and fakes shared by the worker-process tests.</summary>
internal static class ProcessKit
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    public static RunningWorker Worker(int slot, int pid, string id = "010-A", DateTimeOffset? start = null) => new()
    {
        Slot = slot,
        Pid = pid,
        Start = start ?? T0,
        Card = id + "-card",
        Category = id[^1..],
        Id = id,
        Log = $"C:\\logs\\{id}.log",
        RawLog = $"C:\\logs\\{id}.jsonl",
        RawPosition = 1234,
        Job = $"job-{slot}-{pid}",
        TimeBox = 90,
        StopWhy = "over the 90 min limit",
        StopGraceMinutes = 20,
        Msgs = true,
        Model = "m1",
        Endpoint = "http://localhost:1",
        MetricsUrl = "http://localhost:1/metrics",
        Agent = "claude",
    };

    public static Card CardOf(CardFolder folder, string name, string status = "", string why = "", params string[] depends) => new()
    {
        Folder = folder,
        Path = name + ".md",
        Name = name,
        Num = long.Parse(name[..name.IndexOf('-')], System.Globalization.CultureInfo.InvariantCulture), // 931-A: any width
        Category = name[4..5],
        Id = name[..5],
        Status = status,
        Why = why,
        Depends = depends,
    };

    public static string ExeCmd(string dir, string name, string body)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "@echo off\r\n" + body + "\r\n");
        return path;
    }
}

internal sealed class NoHealthRunner : IHealthRunner
{
    public string? MainShortSha() => null;
    public string? Start(string sha, string command) => null;
    public bool IsRunning() => false;
    public int? ExitCode() => null;
    public void Kill() { }
}

/// <summary>An <see cref="IWorkerProcesses"/> that only records what ends.</summary>
internal sealed class RecordingProcesses : IWorkerProcesses
{
    public List<(string Job, int Pid)> Ended { get; } = [];

    public StartedProcess? Start(string commandLine, string jobName, string workingDirectory, IReadOnlyDictionary<string, string?> environment) => null;
    public bool IsAlive(int pid, DateTimeOffset start) => false;
    public DateTimeOffset? CmdStartTime(int pid) => null;
    public void EndJob(string jobName, int pid) => Ended.Add((jobName, pid));
    public int? ExitCode(int pid) => null;
    public long? LogSize(string path) => 4096;
    public DateTimeOffset? LogLastWrite(string path) => null;
}

internal static class Jobs
{
    public static readonly JobObjects Objects = new();
}
