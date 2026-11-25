// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Watchdog;

namespace Scheduler.Core.Tests.Watchdog;

/// <summary>
/// The watchdog's decision for one running worker (TIME-1 to TIME-9, TIME-11; <c>Card-Limit</c>
/// L697, <c>Watch</c> L780-803). Test plan: test-plans/time-limits.md. Shared setup: maxMinutes 240,
/// warnMinutes 20, stopGraceMinutes 20, stallMinutes 30.
/// </summary>
public class WatchdogTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    private static readonly SchedulerConfig Cfg = new()
    {
        MaxMinutes = 240,
        WarnMinutes = 20,
        StopGraceMinutes = 20,
        StallMinutes = 30,
        PageStopGraceMinutes = 5,
    };

    private static DateTimeOffset At(double minutes) => Start.AddMinutes(minutes);

    private static RunningWorker Worker(int? timeBox = null, double lastProgress = 0, long size = 10) => new()
    {
        Slot = 1,
        Pid = 1,
        Start = Start,
        Card = "170-A-x",
        Category = "A",
        Id = "170-A",
        TimeBox = timeBox,
        LastSize = size,
        LastProgress = At(lastProgress),
    };

    // A log that is still growing: its progress is the watch time itself, so quiet is 0.
    private static WatchResult Watch(RunningWorker w, double age) =>
        WatchdogRules.Watch(w with { LastProgress = At(age) }, Cfg, At(age), w.LastSize);

    // TIME-1 (L698): Time box 90 -> 90; none -> 240; 0 counts as unset.
    [Fact]
    public void Time_Limit_TimeBoxElseMax()
    {
        Assert.Equal(90, WatchdogRules.Limit(90, Cfg));
        Assert.Equal(240, WatchdogRules.Limit(null, Cfg));
        Assert.Equal(240, WatchdogRules.Limit(0, Cfg));
    }

    // TIME-2 (L701): lead = min(warnMinutes, floor(limit / 4)).
    [Theory]
    [InlineData(240, 20)]
    [InlineData(60, 15)]
    [InlineData(10, 2)]
    [InlineData(3, 0)]
    [InlineData(1, 0)]
    public void Time_WarnLead_MinOfWarnAndQuarter(int limit, int lead)
    {
        Assert.Equal(lead, WatchdogRules.WarnLead(limit, Cfg));
    }

    // TIME-4 (L788-796): the warning is written once, in [limit - lead, limit).
    [Fact]
    public void Time_Warn_WrittenOnceInWindow()
    {
        var w = Worker(timeBox: 120);
        Assert.Null(Watch(w, 99.9).WarnText);

        var r = Watch(w, 100.0);
        Assert.NotNull(r.WarnText);
        Assert.StartsWith("About 20 minutes are left of this card's 120-minute limit (deadline 12:00)", r.WarnText);
        Assert.True(r.Worker.Warned);
        Assert.Contains(r.Log, l => l.Contains("slot 1 warned (170-A-x): 20 min left of 120"));

        Assert.Null(Watch(r.Worker, 110).WarnText);
        Assert.Null(Watch(r.Worker, 119.99).WarnText);
    }

    // TIME-2, TIME-4: Time box 3 has lead 0, so the window is empty.
    [Fact]
    public void Time_Warn_TinyLimit_NeverWritten()
    {
        var w = Worker(timeBox: 3);
        foreach (var age in new[] { 0, 1, 2, 2.99, 3 })
            Assert.Null(Watch(w, age).WarnText);
    }

    // TIME-4: no warning once a stop has been requested.
    [Fact]
    public void Time_Warn_NotWhenStopping()
    {
        var w = Worker(timeBox: 120) with { StopAt = At(90), StopWhy = "stopped from the page", StopGraceMinutes = 5 };
        Assert.Null(Watch(w, 105).WarnText);
    }

    // TIME-4, TIME-7, TIME-9: at the limit the stop is requested and no warning is written.
    [Fact]
    public void Time_LimitExactlyReached_StopRequestedNoWarn()
    {
        var r = Watch(Worker(timeBox: 120), 120.0);
        Assert.Null(r.WarnText);
        Assert.Equal(WatchAction.RequestStop, r.Action);
        Assert.Equal("over the 120 min limit", r.Reason);
        Assert.Equal("over the 120 min limit", r.Worker.StopWhy);
        Assert.Equal(At(120), r.Worker.StopAt);
        Assert.Equal(20, r.Worker.StopGraceMinutes);
    }

    // TIME-7: just under the limit nothing is asked.
    [Fact]
    public void Time_JustUnderLimit_NoStop()
    {
        var r = Watch(Worker(timeBox: 120), 119.999);
        Assert.Equal(WatchAction.None, r.Action);
        Assert.Null(r.Worker.StopAt);
    }

    // TIME-6 (with TIME-5 checked first, L797): kill at limit + grace.
    [Fact]
    public void Time_LimitPlusGraceExactly_Killed()
    {
        var w = Watch(Worker(timeBox: 120), 120.0).Worker;
        Assert.Equal(WatchAction.None, Watch(w, 139.99).Action);

        var r = Watch(w, 140.0);
        Assert.Equal(WatchAction.Kill, r.Action);
        Assert.Equal("still running 20 min after a stop request (over the 120 min limit)", r.Reason);
    }

    // TIME-6: age >= limit + stopGraceMinutes kills whatever the recorded grace.
    [Fact]
    public void Time_OverLimitPlusGrace_KilledEvenWithLongerPageGrace()
    {
        var w = Worker(timeBox: 60) with { StopAt = At(60), StopWhy = "stopped from the page", StopGraceMinutes = 100 };
        var r = Watch(w, 80);
        Assert.Equal(WatchAction.Kill, r.Action);
        Assert.Equal("over the 60 min limit", r.Reason);
    }

    // TIME-7, TIME-8: over the limit and quiet gives only the time-limit reason.
    [Fact]
    public void Time_OverLimitAndQuiet_OnlyTimeLimitReason()
    {
        var w = Worker(timeBox: 60, lastProgress: 16);
        var r = WatchdogRules.Watch(w, Cfg, At(61), w.LastSize);
        Assert.Equal(WatchAction.RequestStop, r.Action);
        Assert.Equal("over the 60 min limit", r.Reason);
    }

    // TIME-3: progress is the raw log's size changing, not its content.
    [Fact]
    public void Time_Progress_IsRawLogSizeChange()
    {
        var w = Worker(size: 10, lastProgress: 0);
        var same = WatchdogRules.Watch(w, Cfg, At(10), 10);
        Assert.Equal(At(0), same.Worker.LastProgress);
        Assert.Equal(10, same.Quiet);

        var grown = WatchdogRules.Watch(same.Worker, Cfg, At(10.5), 11);
        Assert.Equal(At(10.5), grown.Worker.LastProgress);
        Assert.Equal(11, grown.Worker.LastSize);
        Assert.Equal(0, grown.Quiet);
    }

    // TIME-8: quiet >= stallMinutes asks for a stop.
    [Fact]
    public void Time_Stall_ExactlyStallMinutes_StopRequested()
    {
        var w = Worker(lastProgress: 0);
        Assert.Equal(WatchAction.None, WatchdogRules.Watch(w, Cfg, At(29.99), 10).Action);

        var r = WatchdogRules.Watch(w, Cfg, At(30), 10);
        Assert.Equal(WatchAction.RequestStop, r.Action);
        Assert.Equal("no output for 30 min", r.Reason);
        Assert.Equal("no output for 30 min", r.Worker.StopWhy);
    }

    // TIME-5: killed once the grace has passed since the stop request.
    [Fact]
    public void Time_StopGrace_KillAfterGrace()
    {
        var w = WatchdogRules.Watch(Worker(lastProgress: 0), Cfg, At(30), 10).Worker;
        Assert.Equal(WatchAction.None, WatchdogRules.Watch(w, Cfg, At(49.99), 10).Action);

        var r = WatchdogRules.Watch(w, Cfg, At(50), 10);
        Assert.Equal(WatchAction.Kill, r.Action);
        Assert.Equal("still running 20 min after a stop request (no output for 30 min)", r.Reason);
    }

    // TIME-9: a second request only shortens the grace.
    [Fact]
    public void Time_SecondStopRequest_OnlyShortensGrace()
    {
        var first = WatchdogRules.Watch(Worker(lastProgress: 0), Cfg, At(30), 10).Worker;
        Assert.Equal(20, first.StopGraceMinutes);

        var second = WatchdogRules.RequestStop(first, "stopped from the page", 5, At(32));
        Assert.Equal(5, second.Worker.StopGraceMinutes);
        Assert.Equal(first.StopAt, second.Worker.StopAt);
        Assert.Equal("no output for 30 min", second.Worker.StopWhy);
        Assert.Contains(second.Log, l => l.Contains("slot 1 stop deadline moved to 5 min (stopped from the page)"));

        var third = WatchdogRules.RequestStop(second.Worker, "again", 30, At(33));
        Assert.Equal(second.Worker, third.Worker);
        Assert.Empty(third.Log);
    }

    // TIME-11: a worker still there 2 minutes after a kill is abandoned, once.
    [Fact]
    public void Time_KillFails_AbandonedAfterTwoMinutes()
    {
        var w = Worker(timeBox: 60) with { StopAt = At(60), StopWhy = "over the 60 min limit", StopGraceMinutes = 20 };
        var killed = WatchdogRules.Kill(w, "still running 20 min after a stop request (over the 60 min limit)", At(80));
        Assert.Equal(At(80), killed.Worker.KillTime);
        Assert.Contains(killed.Log, l => l.Contains("its card stays in active/ and will be resumed"));
        Assert.Equal("over the 60 min limit", killed.Worker.StopWhy);

        var t159 = WatchdogRules.Kill(killed.Worker, "x", At(81 + 59.0 / 60));
        Assert.False(t159.Worker.Abandoned);
        Assert.Empty(t159.Log);

        var t200 = WatchdogRules.Kill(t159.Worker, "x", At(82));
        Assert.True(t200.Worker.Abandoned);
        Assert.Single(t200.Log);

        var t300 = WatchdogRules.Kill(t200.Worker, "x", At(83));
        Assert.Empty(t300.Log);
    }
}
