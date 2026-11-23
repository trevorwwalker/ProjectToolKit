using Scheduler.Core.Contracts;

namespace Scheduler.Core.Cards;

/// <summary>
/// The <c>git fetch</c> that precedes reading the cards (CARD-2, <c>Fetch-Main</c> L532-536). A
/// tick runs far more often than the remote changes, so the fetch is spaced at 15 seconds and the
/// page does not wait behind a git process every second. Host does the fetching
/// (<see cref="IMainRepository.Fetch"/>); this decides only when it is due, so the rule is testable
/// with a fake clock and a fake git. Its successor is <see cref="MainModel.FetchGate"/> (MM-9), one
/// per repository and shared by every consumer; this one goes when the Host moves to the gate.
/// </summary>
public sealed class MainFetch(IMainRepository repo, IClock clock)
{
    private const int SpacingSeconds = 15;

    private DateTimeOffset lastFetch = DateTimeOffset.MinValue;

    /// <summary>
    /// Fetches when the last one is at least <see cref="SpacingSeconds"/> seconds old and says
    /// whether it did. The boundary counts: exactly 15 s is due, as the script's <c>-lt 15</c>
    /// leaves it. The outcome of the fetch changes nothing - L534 throws it away and L535 re-stamps
    /// the time afterwards - so a fetch that failed is not tried again until the next 15 s is up.
    /// </summary>
    public bool Fetch()
    {
        if ((clock.Now - lastFetch).TotalSeconds < SpacingSeconds)
        {
            return false;
        }

        _ = repo.Fetch();
        lastFetch = clock.Now;
        return true;
    }
}
