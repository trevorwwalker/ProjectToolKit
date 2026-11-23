namespace Scheduler.Tray;

/// <summary>
/// The scheduler a deploy swaps: the tray ends it the way Restart does (workers keep running and the new
/// scheduler adopts them, TRAY-5/TRAY-8) and starts it again from the same path (967-B).
/// </summary>
public interface ISchedulerController
{
    /// <summary>End the running scheduler, leaving its workers alone. True when it ended.</summary>
    Task<bool> StopAsync(CancellationToken cancel);

    /// <summary>
    /// Start the scheduler from the running folder and wait for it to say it is up.
    /// <paramref name="replaces"/> is the version this one takes over from, which the scheduler writes
    /// into its log so the two can be told apart afterwards; "" when nothing was replaced.
    /// True when the start line came within <paramref name="window"/>.
    /// </summary>
    Task<bool> StartAsync(string replaces, TimeSpan window, CancellationToken cancel);
}
