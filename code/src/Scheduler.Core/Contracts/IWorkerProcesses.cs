// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>
/// Worker and health-check processes. Used by starting a card (WRK-12, WRK-13, WRK-8), the
/// watchdog and kills (TIME-3, TIME-11, TIME-13), adoption (ADOPT-2) and the health check (A7).
/// </summary>
public interface IWorkerProcesses
{
    /// <summary>
    /// Start <paramref name="commandLine"/> under <c>cmd /c</c> in <paramref name="workingDirectory"/>,
    /// inside the named job object (WRK-13), with <paramref name="environment"/> applied: a null
    /// value removes the variable (WRK-8). Null when it could not start.
    /// </summary>
    StartedProcess? Start(string commandLine, string jobName, string workingDirectory,
        IReadOnlyDictionary<string, string?> environment);

    /// <summary>A process with <paramref name="pid"/> exists and started within 2 s of <paramref name="start"/> (TIME-13).</summary>
    bool IsAlive(int pid, DateTimeOffset start);

    /// <summary>The start time of a <c>cmd</c> process with <paramref name="pid"/>, or null (ADOPT-2).</summary>
    DateTimeOffset? CmdStartTime(int pid);

    /// <summary>End the job object and the process tree under <paramref name="pid"/> (TIME-11).</summary>
    void EndJob(string jobName, int pid);

    /// <summary>The process's exit code once it has ended, or null while it runs or when unknown (TIME-18).</summary>
    int? ExitCode(int pid);

    /// <summary>A log file's size in bytes, or null when it does not exist (TIME-3, TIME-14).</summary>
    long? LogSize(string path);

    /// <summary>A log file's last write time, or null when it does not exist (ADOPT-2).</summary>
    DateTimeOffset? LogLastWrite(string path);
}
