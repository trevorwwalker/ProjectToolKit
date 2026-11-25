// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Estimates;
using Xunit;

namespace Scheduler.Core.Tests.Estimates;

/// <summary>EST-21: the outcome of a row, by its result word and then its stop reason.</summary>
public sealed class RunOutcomeTests
{
    [Theory]
    [InlineData("done", "", 5, RunOutcome.Done)]
    [InlineData("review", "", 5, RunOutcome.Review)]
    [InlineData("needs-human", "", 5, RunOutcome.Question)]
    [InlineData("waiting", "", 5, RunOutcome.Planner)]
    [InlineData("yielded", "", 5, RunOutcome.Yielded)]
    [InlineData("time limit", "", 90, RunOutcome.TimeLimit)]
    [InlineData("stopped", "over the 10 min limit", 10, RunOutcome.TimeLimit)]
    [InlineData("stopped", "stopped from the page", 3, RunOutcome.Stopped)]
    [InlineData("stopped", "", 3, RunOutcome.Stopped)]
    [InlineData("stopped from page", "", 3, RunOutcome.Stopped)]
    [InlineData("killed", "killed from the page", 3, RunOutcome.Stopped)]
    [InlineData("out of quota", "", 0, RunOutcome.OutOfQuota)]
    [InlineData("crashed (exit 1)", "", 0, RunOutcome.DidNotStart)]
    [InlineData("crashed (exit 1)", "", 5, RunOutcome.Failed)]
    [InlineData("no result line", "", 0, RunOutcome.DidNotStart)]
    [InlineData("no result line", "", 12, RunOutcome.Failed)]
    [InlineData("ended early", "", 0, RunOutcome.DidNotStart)]
    [InlineData("ended early", "", 4, RunOutcome.Failed)]
    [InlineData("failed", "", 4, RunOutcome.Failed)]
    [InlineData("stalled", "", 30, RunOutcome.Failed)]
    [InlineData("some new word", "", 30, RunOutcome.Failed)]
    [InlineData("no log", "", 0, RunOutcome.NotCounted)]
    [InlineData("model server down", "", 0, RunOutcome.NotCounted)]
    [InlineData("DONE", "", 5, RunOutcome.Done)]
    [InlineData("Crashed (exit 3)", "", 0, RunOutcome.DidNotStart)]
    [InlineData("Stopped", "Over the 10 min limit", 10, RunOutcome.TimeLimit)]
    public void EST_21_EachResultWord_MapsToItsOutcome(string result, string stopReason, int minutes, RunOutcome expected) =>
        Assert.Equal(expected, RunOutcomes.Of(result, stopReason, minutes));
}
