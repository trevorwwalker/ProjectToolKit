// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;

namespace Scheduler.Core.Tests.Dispatch;

/// <summary>
/// The 20-second model-server cache a dispatch consults before taking a card (ACCT-8; schedule.ps1
/// L2925, L2947-2958). Test plan: scheduler-next/test-plans/dispatch.md. Kept from the slot
/// dispatcher's tests when it was removed (939-A): the cache is still what
/// <see cref="TypeDispatcher"/> asks about each type's endpoint.
/// </summary>
public class ServerCacheTests
{
    private const string Ep = "http://server:8000/v1";

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private sealed class FakeLog : ISchedulerLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string line) => Lines.Add(line);
    }

    /// <summary>A model server per endpoint: up unless listed in <see cref="Down"/>; counts requests.</summary>
    private sealed class FakeProbe : IServerProbe
    {
        public HashSet<string> Down { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Requests { get; private set; }

        public bool IsUp(string endpoint)
        {
            Requests++;
            return !Down.Contains(endpoint);
        }

        public string? ReadModels(string endpoint) => null;

        public string? ReadMetrics(string metricsUrl) => null;
    }

    [Fact]
    public void Dispatch_ServerUp_CachedFor20Seconds()
    {
        // ACCT-8: a down server is re-asked only every 20 s, and the change each way is logged once.
        // The test plan's name for this row is kept so test-plans/dispatch.md still finds it (939-A).
        var probe = new FakeProbe();
        var clock = new FakeClock();
        var log = new FakeLog();
        var cache = new ServerCache(probe, clock, log);

        probe.Down.Add(Ep);
        Assert.False(cache.IsUp(Ep));                     // t=0: down, and a first "down" is logged
        Assert.True(cache.KnownDown(Ep));
        Assert.Equal([Ep], cache.Down());

        clock.Now = DateTimeOffset.UnixEpoch.AddSeconds(5);
        probe.Down.Clear();                               // t=5: the server is up again

        clock.Now = DateTimeOffset.UnixEpoch.AddSeconds(10);
        Assert.False(cache.IsUp(Ep));                     // t=10: still down (cached)
        Assert.Equal(1, probe.Requests);

        clock.Now = DateTimeOffset.UnixEpoch.AddSeconds(20);
        Assert.True(cache.IsUp(Ep));                      // t=20: asked again, up
        Assert.Equal(2, probe.Requests);
        Assert.False(cache.KnownDown(Ep));
        Assert.Empty(cache.Down());
        Assert.Equal(
            [
                $"model server {Ep} does not answer: its workers take no new cards until it does",
                $"model server {Ep} answers again: its workers take cards again",
            ],
            log.Lines);

        // A fresh request skips the cache; no change, nothing logged. No endpoint = up, never asked.
        Assert.True(cache.IsUp(Ep, fresh: true));
        Assert.Equal(3, probe.Requests);
        Assert.True(cache.IsUp(""));
        Assert.Equal(3, probe.Requests);
        Assert.Equal(2, log.Lines.Count);

        // A first answer that is "up" is not logged.
        var other = new ServerCache(new FakeProbe(), clock, log);
        Assert.True(other.IsUp(Ep));
        Assert.Equal(2, log.Lines.Count);
    }
}
