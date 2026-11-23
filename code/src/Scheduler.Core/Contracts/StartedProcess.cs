namespace Scheduler.Core.Contracts;

/// <summary>A process started by <see cref="IWorkerProcesses.Start"/>: what <c>Alive</c> needs (TIME-13).</summary>
public sealed record StartedProcess
{
    /// <summary>The process id.</summary>
    public required int Pid { get; init; }

    /// <summary>The process's start time.</summary>
    public required DateTimeOffset Start { get; init; }
}
