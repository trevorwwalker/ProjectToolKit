// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray;

/// <summary>Where the scheduler the tray started is.</summary>
public enum SchedulerState
{
    /// <summary>Started; its page address is not known yet.</summary>
    Starting,

    /// <summary>Running, with its page known.</summary>
    Running,

    /// <summary>Asked to stop all and exit; the tray waits for it to end.</summary>
    Stopping,

    /// <summary>Ending the scheduler so a freshly built one can start; its workers keep running (670-B).</summary>
    Restarting,

    /// <summary>Ending the scheduler and the tray; its workers keep running (670-B).</summary>
    Leaving,

    /// <summary>Ended by itself; the tray offers Restart.</summary>
    Exited,
}
