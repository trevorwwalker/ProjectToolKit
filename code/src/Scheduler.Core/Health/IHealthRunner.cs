namespace Scheduler.Core.Health;

/// <summary>
/// What running the health check needs from the machine (HLTH-2 to HLTH-4). Host implements it
/// (270-B: the health worktree, health.cmd, the log file and the job object); Core decides when
/// each member is called and never runs a command of its own (HLTH-7).
/// </summary>
public interface IHealthRunner
{
    /// <summary>
    /// main's short sha (<c>git rev-parse --short</c>, L1693), or null when git gave nothing, in
    /// which case nothing is started and the check stays wanted (HLTH-2).
    /// </summary>
    string? MainShortSha();

    /// <summary>
    /// Run <paramref name="command"/> against <paramref name="sha"/> (HLTH-3) and return the log
    /// file it writes, or null when it could not be started. Core passes the configured health
    /// command and nothing else (HLTH-7).
    /// </summary>
    /// <param name="sha">The short sha being checked.</param>
    /// <param name="command">The command line, as <c>health.command</c> holds it.</param>
    string? Start(string sha, string command);

    /// <summary>The started check is still running (L1723).</summary>
    bool IsRunning();

    /// <summary>
    /// The exit code the finished check reports (L1732), or null while it runs. A check Core has
    /// killed is treated as red whatever this says (HLTH-4).
    /// </summary>
    int? ExitCode();

    /// <summary>Kill the check's process tree and release its job object (HLTH-4, L1726-1727).</summary>
    void Kill();
}
