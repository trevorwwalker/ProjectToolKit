// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Tests.WebContract;

public class PageTimingTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => Now += by;
    }

    // PAGE-2: a snapshot after every click and tick; every 3 s while "/" was asked for in the
    // last 15 s; none at 16 s without asks; the remote version only while another PC asked in
    // the last 30 s.
    [Fact]
    public void Page_SnapshotCadence()
    {
        var clock = new FakeClock();
        var c = new PageCadence(clock);

        // Nothing asked, nothing clicked: no snapshot.
        Assert.False(c.SnapshotDue());

        // A click or a tick makes one due at once, and building it clears that.
        c.NoteClickOrTick();
        Assert.True(c.SnapshotDue());
        c.NoteBuilt();
        Assert.False(c.SnapshotDue());

        // "/" asked for: one every 3 s for 15 s after the last ask.
        c.NoteIndexAsked();
        var built = 0;
        for (var s = 1; s <= 16; s++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            if (c.SnapshotDue())
            {
                built++;
                Assert.True(s <= 15, "no snapshot at 16 s without asks");
                Assert.Equal(0, s % 3);
                c.NoteBuilt();
            }
        }
        Assert.Equal(5, built); // at 3, 6, 9, 12, 15 s

        // A later tick still produces one even with no page asks.
        c.NoteClickOrTick();
        Assert.True(c.SnapshotDue());
        c.NoteBuilt();

        // The remote version: only while another PC asked in the last 30 s.
        Assert.False(c.RemoteVersionWanted());
        c.NoteRemoteAsked();
        Assert.True(c.RemoteVersionWanted());
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(c.RemoteVersionWanted());
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(c.RemoteVersionWanted());
    }

    // PAGE-4: a tick asked for by a click waits for 250 ms without requests, or 2 s at most.
    [Fact]
    public void Page_ClickTick_Debounced()
    {
        // One click, no more requests: the tick runs at 250 ms.
        var clock = new FakeClock();
        var d = new ClickTickDebounce(clock);
        Assert.False(d.TickDue());
        d.Request();
        clock.Advance(TimeSpan.FromMilliseconds(249));
        Assert.False(d.TickDue());
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(d.TickDue());
        d.NoteTicked();
        Assert.False(d.TickDue());

        // Requests every 100 ms: the tick runs at 2 s.
        d.Request();
        var start = clock.Now;
        TimeSpan? ranAt = null;
        for (var ms = 100; ms <= 3000 && ranAt is null; ms += 100)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            if (d.TickDue())
            {
                ranAt = clock.Now - start;
                break;
            }
            d.Request();
        }
        Assert.Equal(TimeSpan.FromSeconds(2), ranAt);
    }
}
