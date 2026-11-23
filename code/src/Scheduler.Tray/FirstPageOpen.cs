namespace Scheduler.Tray;

/// <summary>
/// 1014-B: opens the scheduler's page once for a tray started by hand: after the page answers, waiting up to
/// <see cref="Window"/> for it. A <c>-Quiet</c> tray never opens it, and a restart inside the same tray
/// does not open it again - this runs once per tray start.
/// </summary>
public sealed class FirstPageOpen(bool quiet, Func<Uri, CancellationToken, Task<bool>> answers, Action<Uri> open, Func<TimeSpan, CancellationToken, Task> delay)
{
    /// <summary>How long the page is waited for.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    /// <summary>How often the page is tried while waiting.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(1);

    private bool _done;

    /// <summary>True when the page was opened.</summary>
    public bool Opened { get; private set; }

    /// <summary>Waits for the page to answer and opens it, the first call only; false when quiet, already tried or it never answered.</summary>
    public async Task<bool> RunAsync(Uri page, CancellationToken cancel)
    {
        if (quiet || _done)
        {
            return false;
        }

        _done = true;
        var waited = TimeSpan.Zero;
        while (!cancel.IsCancellationRequested)
        {
            if (await answers(page, cancel))
            {
                open(page);
                Opened = true;
                return true;
            }

            if (waited >= Window)
            {
                return false;
            }

            await delay(Every, cancel);
            waited += Every;
        }

        return false;
    }
}
