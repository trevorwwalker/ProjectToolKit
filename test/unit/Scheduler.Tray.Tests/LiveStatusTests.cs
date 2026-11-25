// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray.Tests;

public class LiveStatusTests
{
    [Fact]
    public void Each_slot_in_the_live_feed_is_a_running_worker() =>
        Assert.Equal(2, LiveStatus.Workers(
            """{"t":"23:10:00","pageDone":true,"page":"v1","slots":{"1":{"log":[],"stats":"","time":""},"3":{"log":["x"],"stats":"s","time":"t"}}}"""));

    [Fact]
    public void An_empty_feed_has_no_workers() =>
        Assert.Equal(0, LiveStatus.Workers("""{"t":"","pageDone":false,"page":"","slots":{}}"""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>")]
    [InlineData("[]")]
    [InlineData("""{"t":""}""")]
    public void Anything_else_is_no_answer(string? json) => Assert.Null(LiveStatus.Workers(json));
}
