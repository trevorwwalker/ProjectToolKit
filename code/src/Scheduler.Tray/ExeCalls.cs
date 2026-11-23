using System.ComponentModel;
using System.Diagnostics;

namespace Scheduler.Tray;

/// <summary>
/// Dynamic locks (design <c>dynamic-locks.md</c> section 6; card 1021-B): the processes running from the
/// scheduler's exe once the scheduler itself has ended - a worker's <c>lock</c> or <c>check-push</c> call. The
/// update must not swap the exe from under one, so it waits for them as it waits for the scheduler.
/// </summary>
public static class ExeCalls
{
    /// <summary>How many processes are running from <paramref name="exe"/> now.</summary>
    public static int Running(string exe)
    {
        var full = Path.GetFullPath(exe);
        var count = 0;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)))
        {
            using (p)
            {
                try
                {
                    if (string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                {
                    // ended meanwhile, or another user's: not one of ours
                }
            }
        }

        return count;
    }
}
