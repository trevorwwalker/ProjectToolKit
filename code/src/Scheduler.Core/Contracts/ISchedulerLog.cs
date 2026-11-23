namespace Scheduler.Core.Contracts;

/// <summary>
/// The scheduler's own log: every "logged" in the spec (for example LANE-2, TIME-4, TIME-11,
/// TIME-15, DISP-21, CFG-15). Host adds the time stamp and writes the console and log file.
/// </summary>
public interface ISchedulerLog
{
    /// <summary>Write one line.</summary>
    void Write(string line);
}
