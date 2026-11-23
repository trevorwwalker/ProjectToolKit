using Scheduler.Core.Contracts;
using Scheduler.Core.Health;

namespace Scheduler.Core.Tests.Health;

/// <summary>The clock, the log and the machine side the health decisions are tested against.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
}

internal sealed class FakeLog : ISchedulerLog
{
    public List<string> Lines { get; } = [];

    public void Write(string line) => Lines.Add(line);
}

/// <summary>
/// The machine side of the health check (HLTH-2 to HLTH-4): the sha it is told, the runs it was
/// asked to make, and the code the check finishes with. Host really does this (270-B).
/// </summary>
internal sealed class FakeHealthRunner : IHealthRunner
{
    private const string LogPath = @"C:\logs\health.log";

    public FakeHealthRunner(string? sha) => Sha = sha;

    /// <summary>main's short sha; the test moves it on to fake a new commit.</summary>
    public string? Sha { get; set; }

    /// <summary>Every (sha, command) the scheduler asked to run, in order.</summary>
    public List<(string Sha, string Command)> Starts { get; } = [];

    /// <summary>How many times a running check was killed (HLTH-4).</summary>
    public int Kills { get; private set; }

    /// <summary>How many times git was asked for main's sha.</summary>
    public int ShaRequests { get; private set; }

    /// <summary>True from a start until <see cref="FinishWith"/> or a kill.</summary>
    public bool IsRunningValue { get; private set; }

    /// <summary>The exit code the finished check reports.</summary>
    public int? ExitCodeValue { get; private set; }

    /// <summary>When set, the check cannot be started at all.</summary>
    public bool FailToStart { get; set; }

    public string? MainShortSha()
    {
        ShaRequests++;
        return Sha;
    }

    public string? Start(string sha, string command)
    {
        if (FailToStart)
        {
            return null;
        }

        Starts.Add((sha, command));
        IsRunningValue = true;
        ExitCodeValue = null;
        return LogPath;
    }

    public bool IsRunning() => IsRunningValue;

    public int? ExitCode() => ExitCodeValue;

    public void Kill()
    {
        Kills++;
        IsRunningValue = false;
        ExitCodeValue = -1;
    }

    /// <summary>The check ended by itself with this exit code.</summary>
    public void FinishWith(int code)
    {
        IsRunningValue = false;
        ExitCodeValue = code;
    }
}

/// <summary>The model servers always answer, so a dispatch decision is not about servers.</summary>
internal sealed class FakeProbe : IServerProbe
{
    public bool IsUp(string endpoint) => true;

    public string? ReadModels(string endpoint) => null;

    public string? ReadMetrics(string metricsUrl) => null;
}
