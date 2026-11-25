// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Runtime.InteropServices;

namespace Scheduler.Host.Git;

/// <summary>
/// Whether this process has a console of its own. A console child started with <c>CreateNoWindow</c> is given a new,
/// windowless console, and Windows starts one <c>conhost.exe</c> for it: a second process for every git the scheduler
/// starts. A child that inherits the scheduler's console needs none. The scheduler is started by the tray without a
/// window, so its console is never seen and neither is anything a child writes to it; git's own input and output are
/// redirected either way. Without a console (a host that has none), the child keeps <c>CreateNoWindow</c> so no
/// window opens.
/// </summary>
public static class OwnConsole
{
    /// <summary>True when the process is attached to a console (visible or not).</summary>
    public static bool Attached { get; } = OperatingSystem.IsWindows() && GetConsoleCP() != 0;

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleCP();
}
