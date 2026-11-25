// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Server;

/// <summary>
/// 966-B item 5: any click or key puts the page into fast mode for 15 s - it asks for /live.json about
/// thirty times a second - and the feed, seeing asks that close together, tells the scheduler loop to
/// wake at the same rate. After 15 s with no interaction both go back to the normal rates.
/// </summary>
public class LiveFeedFastModeTests
{
    private static LiveFeed Feed(TestClock clock) => new(clock, new FakePageData());

    [Fact]
    public void Feed_AsksThirtyASecond_IsFast()
    {
        var clock = new TestClock();
        var feed = Feed(clock);

        feed.Ask();
        Assert.False(feed.Fast);
        clock.Advance(TimeSpan.FromMilliseconds(33));
        feed.Ask();

        Assert.True(feed.Fast);
    }

    [Fact]
    public void Feed_AsksEveryTwoSeconds_IsNeverFast()
    {
        var clock = new TestClock();
        var feed = Feed(clock);

        for (var i = 0; i < 5; i++)
        {
            feed.Ask();
            Assert.False(feed.Fast);
            clock.Advance(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void Feed_FastAsksStop_BackToNormalWithinASecond()
    {
        var clock = new TestClock();
        var feed = Feed(clock);
        feed.Ask();
        for (var i = 0; i < 30; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(33));
            feed.Ask();
        }

        Assert.True(feed.Fast);
        clock.Advance(LiveFeed.FastHold);
        Assert.True(feed.Fast);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(feed.Fast);
    }

    [Fact]
    public void Feed_BuildCap_ThirtyASecond()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1000d / 30), LiveFeed.BuildInterval);

        var clock = new TestClock();
        var feed = Feed(clock);
        feed.Ask();
        feed.Build();
        clock.Advance(TimeSpan.FromMilliseconds(34));
        feed.Ask();

        Assert.True(feed.BuildDue());
    }

    [Fact]
    public void Page_ClickOrKey_AsksEvery33msFor15Seconds()
    {
        var script = PageScript.Html;

        Assert.Contains("var INTERACT = 33, LIVELY = 15000", script, StringComparison.Ordinal);
        Assert.Contains("Date.now() < lively ? INTERACT : waitFor ? FAST : EVERY", script, StringComparison.Ordinal);
        Assert.Contains("lively = Date.now() + LIVELY", script, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('pointerdown', interact, true);", script, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('keydown', interact, true);", script, StringComparison.Ordinal);
    }
}
