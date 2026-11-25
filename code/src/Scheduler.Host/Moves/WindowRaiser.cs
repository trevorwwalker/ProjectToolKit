// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Moves;

/// <summary>What <see cref="WindowRaiser.Raise"/> did.</summary>
public enum RaiseOutcome
{
    /// <summary>The window is in front.</summary>
    Front,

    /// <summary>Windows refused the foreground; the window flashes in the taskbar instead.</summary>
    Flashed,

    /// <summary>No window showing the file turned up within the limit.</summary>
    NotFound,
}

/// <summary>
/// Brings the window showing an opened file to the front (986-B, MOVE-9's Raise-Editor, L1575):
/// the window whose title contains the file name (an app that was already running and only opened a
/// new tab or window, and most new ones), else the started process's main window once it has stayed
/// for two looks. A minimized window is restored; when
/// Windows refuses the foreground (its foreground lock on a background process) the window flashes in
/// the taskbar. It looks again every <c>step</c> and gives up once <c>limit</c> has passed.
/// </summary>
/// <param name="api">The window calls.</param>
/// <param name="limit">How long to look for the window; about 3 s.</param>
/// <param name="step">How long to wait between looks.</param>
/// <param name="sleep">Waits; <see cref="Thread.Sleep(TimeSpan)"/> unless a test fakes the clock.</param>
public sealed class WindowRaiser(IWindowApi api, TimeSpan limit, TimeSpan step, Action<TimeSpan>? sleep = null)
{
    /// <summary>The usual limit: about 3 s.</summary>
    public static readonly TimeSpan DefaultLimit = TimeSpan.FromSeconds(3);

    /// <summary>The usual wait between looks.</summary>
    public static readonly TimeSpan DefaultStep = TimeSpan.FromMilliseconds(100);

    private readonly Action<TimeSpan> wait = sleep ?? Thread.Sleep;

    /// <summary>Bring the window showing <paramref name="file"/>, started as process <paramref name="pid"/> (null when none was started), to the front.</summary>
    public RaiseOutcome Raise(string file, int? pid)
    {
        if (pid is int granted)
        {
            api.AllowForeground(granted);
        }

        var name = Path.GetFileName(file);
        var waited = TimeSpan.Zero;
        nint seen = 0;
        while (true)
        {
            var window = name.Length > 0 ? api.FindTitled(name) : 0;
            if (window == 0 && pid is int p)
            {
                // The started process's own window counts once it is still there at the next look:
                // a single-instance app's hand-off process (VS Code's Code.exe) may show one for a
                // moment before passing the file to the running app and ending.
                var main = api.MainWindow(p);
                window = main != 0 && main == seen ? main : 0;
                seen = main;
            }

            if (window != 0)
            {
                if (api.IsMinimized(window))
                {
                    api.Restore(window);
                }

                if (api.BringToFront(window))
                {
                    return RaiseOutcome.Front;
                }

                api.Flash(window);
                return RaiseOutcome.Flashed;
            }

            if (waited + step > limit)
            {
                return RaiseOutcome.NotFound;
            }

            wait(step);
            waited += step;
        }
    }
}
