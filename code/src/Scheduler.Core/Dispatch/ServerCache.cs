// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Dispatch;

/// <summary>
/// Whether each model server answers, asked at most every 20 seconds (ACCT-8, schedule.ps1
/// L1004-1019). A change, and a first answer of "down", is logged. No endpoint is always up.
/// </summary>
public sealed class ServerCache(IServerProbe probe, IClock clock, ISchedulerLog log)
{
    private readonly Dictionary<string, (bool Up, DateTimeOffset At)> entries = new(Text.Comparer);

    /// <summary>
    /// Server-Up (L1005): the cached answer if it is under 20 seconds old and not
    /// <paramref name="fresh"/>; otherwise the server is asked again (its probe has the 5 s timeout).
    /// </summary>
    public bool IsUp(string endpoint, bool fresh = false)
    {
        if (string.IsNullOrEmpty(endpoint))
        {
            return true;
        }

        var known = entries.TryGetValue(endpoint, out var e);
        if (known && !fresh && (clock.Now - e.At).TotalSeconds < 20)
        {
            return e.Up;
        }

        var up = probe.IsUp(endpoint);
        if ((!known && !up) || (known && e.Up != up))
        {
            var what = up ? "answers again: its workers take cards again" : "does not answer: its workers take no new cards until it does";
            log.Write($"model server {endpoint} {what}");
        }

        entries[endpoint] = (up, clock.Now);
        return up;
    }

    /// <summary>Server-Known-Down (L1018): the last answer was "down"; never asks.</summary>
    public bool KnownDown(string endpoint) =>
        !string.IsNullOrEmpty(endpoint) && entries.TryGetValue(endpoint, out var e) && !e.Up;

    /// <summary>Servers-Down (L1019): every endpoint whose last answer was "down".</summary>
    public IReadOnlyList<string> Down() => [.. entries.Where(p => !p.Value.Up).Select(p => p.Key)];
}
