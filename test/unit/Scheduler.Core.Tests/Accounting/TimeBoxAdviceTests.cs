// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;

namespace Scheduler.Core.Tests.Accounting;

/// <summary>
/// Roadmap item 4 (card 640-A): the words the Queue window shows beside a card about its time box.
/// A card whose <c>Time box:</c> is shorter than its area's 90th percentile on the worker it is
/// headed for is warned; a card with no time box is given the suggestion. Nothing is said when
/// there is no history to speak from, and nothing is ever changed on the card itself (item 3).
/// </summary>
public sealed class TimeBoxAdviceTests
{
    private static AreaTypical Typical(string area, string model, int p90) =>
        new() { Area = area, Model = model, Runs = 12, Median = 40, P90 = p90 };

    // The warning, worded as the card asks for it.
    [Fact]
    public void TimeBoxAdvice_ShortTimeBox_WarnsWithAreaAndModel()
    {
        var text = TimeBoxAdvice.Text(60, Typical("A", "claude-opus-5-5", 95));

        Assert.Equal("time box 60 min; area A on claude-opus-5-5 usually needs up to 95", text);
    }

    // A card with no Time box is given the suggestion, nothing else.
    [Fact]
    public void TimeBoxAdvice_NoTimeBox_SuggestsThePercentile()
    {
        Assert.Equal("suggested time box: 95 min", TimeBoxAdvice.Text(null, Typical("A", "claude-opus-5-5", 95)));
    }

    // Nothing to say: the time box covers what the area usually needs (equal counts as covered),
    // and no figures at all means no words, not a guess.
    [Fact]
    public void TimeBoxAdvice_SaysNothingWhenThereIsNothingToWarn()
    {
        Assert.Equal("", TimeBoxAdvice.Text(95, Typical("A", "opus", 95)));
        Assert.Equal("", TimeBoxAdvice.Text(120, Typical("A", "opus", 95)));
        Assert.Equal("", TimeBoxAdvice.Text(0, null));
        Assert.Equal("", TimeBoxAdvice.Text(60, null));
        Assert.Equal("", TimeBoxAdvice.Text(null, null));
    }
}
