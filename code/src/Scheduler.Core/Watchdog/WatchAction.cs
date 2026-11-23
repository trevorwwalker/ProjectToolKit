namespace Scheduler.Core.Watchdog;

/// <summary>What the host must do for a worker after a watch (TIME-5 to TIME-9, TIME-11).</summary>
public enum WatchAction
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Create the stop file (TIME-9); the worker already carries the stop record.</summary>
    RequestStop,

    /// <summary>End the job and kill the process tree (TIME-11).</summary>
    Kill,
}
