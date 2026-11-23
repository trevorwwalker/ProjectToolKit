namespace Scheduler.Tray;

/// <summary>
/// <c>-Replaces:&lt;version&gt;</c> (967-B): the version the scheduler the tray started takes over from,
/// which the scheduler writes into its log so the two builds can be told apart afterwards. Like
/// <c>-NoBrowser</c> and <c>-ShutdownEvent</c> it is read at the scheduler's entry point: its command line
/// leaves a switch it does not bind alone (CFG-7).
/// </summary>
public static class ReplacesSwitch
{
    /// <summary>The switch as it is typed.</summary>
    public const string Name = "-Replaces";
}
