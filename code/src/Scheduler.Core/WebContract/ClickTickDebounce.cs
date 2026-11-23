using Scheduler.Core.Contracts;

namespace Scheduler.Core.WebContract;

/// <summary>
/// A tick asked for by a click waits for 250 ms without further requests, or 2 s at most
/// (PAGE-4). Pure logic over <see cref="IClock"/>.
/// </summary>
public sealed class ClickTickDebounce(IClock clock)
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Longest = TimeSpan.FromSeconds(2);

    private DateTimeOffset? _first;
    private DateTimeOffset _last;

    /// <summary>A click asked for a tick.</summary>
    public void Request()
    {
        var now = clock.Now;
        _first ??= now;
        _last = now;
    }

    /// <summary>Whether the tick should run now.</summary>
    public bool TickDue()
    {
        if (_first is not { } first)
        {
            return false;
        }
        var now = clock.Now;
        return now - _last >= Quiet || now - first >= Longest;
    }

    /// <summary>The tick ran: forget the pending requests.</summary>
    public void NoteTicked() => _first = null;
}
