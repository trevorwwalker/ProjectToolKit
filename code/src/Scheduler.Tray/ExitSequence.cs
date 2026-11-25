// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray;

/// <summary>How the tray's Exit ended the scheduler.</summary>
public enum ExitOutcome
{
    /// <summary>It had already ended.</summary>
    AlreadyEnded,

    /// <summary>The page took "Stop all and exit" and the scheduler ended by itself.</summary>
    StoppedAll,

    /// <summary>The page did not take the request, so the scheduler was killed.</summary>
    Killed,
}

/// <summary>
/// The tray's Exit, as the page's "Stop all and exit" does it: POST <c>/stopall</c>, then wait for
/// the scheduler to end (it lets its workers stop cleanly first, so this can take minutes). Only when
/// the page does not take the request is the scheduler killed, since nothing else could stop it.
/// </summary>
public sealed class ExitSequence(Func<CancellationToken, Task<bool>> postStopAll, Func<bool> hasExited, Func<CancellationToken, Task> waitForExit, Action kill)
{
    /// <summary>Runs it.</summary>
    public async Task<ExitOutcome> RunAsync(CancellationToken cancel = default)
    {
        if (hasExited())
        {
            return ExitOutcome.AlreadyEnded;
        }

        bool posted;
        try
        {
            posted = await postStopAll(cancel).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            posted = false;
        }

        if (!posted)
        {
            if (hasExited())
            {
                return ExitOutcome.AlreadyEnded;
            }

            kill();
            await waitForExit(cancel).ConfigureAwait(false);
            return ExitOutcome.Killed;
        }

        await waitForExit(cancel).ConfigureAwait(false);
        return ExitOutcome.StoppedAll;
    }
}
