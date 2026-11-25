// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.PageActions;

namespace Scheduler.Host.Tests.PageActions;

/// <summary>PAGE-4 (1194-B): a burst of page clicks is one tick, 250 ms after the last.</summary>
public class ClickTickGateTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => Now += by;
    }

    [Fact]
    public void Page_ClickBurst_AsksOneTick_250msAfterTheLast()
    {
        var clock = new FakeClock();
        var ticks = 0;
        var gate = new ClickTickGate(clock, () => ticks++);

        for (var i = 0; i < 3; i++)
        {
            gate.Ask();
            gate.Poll();
            Assert.Equal(0, ticks);
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        // 100 ms since the last click: still waiting; 249 ms: still waiting; 250 ms: one tick.
        gate.Poll();
        Assert.Equal(0, ticks);
        clock.Advance(TimeSpan.FromMilliseconds(149));
        gate.Poll();
        Assert.Equal(0, ticks);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        gate.Poll();
        Assert.Equal(1, ticks);

        // Nothing pending: no second tick.
        clock.Advance(TimeSpan.FromSeconds(1));
        gate.Poll();
        Assert.Equal(1, ticks);
    }
}
