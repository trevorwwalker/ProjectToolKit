// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Estimates;
using Xunit;
using static Scheduler.Core.Tests.Estimates.EstimateTestRows;

namespace Scheduler.Core.Tests.Estimates;

/// <summary>EST-22 to EST-25 and EST-27: Table 3, outcomes by worker type.</summary>
public sealed class OutcomeTableTests
{
    private static IEnumerable<string> Runs(int count, string result, string type = "Opus", int from = 0, string stop = "", int minutes = 10) =>
        Many(count, i => Line(result: result, type: type, minutes: minutes, stop: stop, card: $"{from + i + 1}-A-x", minutesAgo: from + i));

    [Fact]
    public void EST_22_Shares_LeaveOutQuotaDidNotStartAndNotCounted_AndAddUpTo100()
    {
        var lines = Runs(5, "done", from: 20)
            .Concat(Runs(3, "review", from: 25))
            .Concat(Runs(2, "failed", from: 28))
            .Concat(Runs(2, "out of quota", from: 0, minutes: 0))
            .Concat(Runs(4, "crashed (exit 1)", from: 2, minutes: 0))
            .Concat(Runs(1, "no log", from: 6, minutes: 0));
        var rows = Rows([.. lines]);

        var group = Assert.Single(OutcomeTable.Build(rows, null, Now));

        Assert.Equal(10, group.Runs);
        Assert.Equal(FigureSource.Project, group.Source);
        Assert.Equal(new OutcomeShare(5, 50), group.Done);
        Assert.Equal(new OutcomeShare(3, 30), group.Review);
        Assert.Equal(new OutcomeShare(2, 20), group.Failed);
        Assert.Equal(2, group.OutOfQuota);
        Assert.Equal(4, group.DidNotStart);
        Assert.Equal(1, group.NotCounted);
        var shares = new[] { group.Done, group.Review, group.Question, group.Planner, group.Yielded, group.TimeLimit, group.Stopped, group.Failed };
        Assert.Equal(100, shares.Sum(s => s.Share));
        Assert.Equal(10, shares.Sum(s => s.Count));
    }

    [Fact]
    public void EST_22_EveryOutcomeWithAShare_IsCountedUnderItsOwnName()
    {
        var lines = Runs(1, "needs-human", from: 0)
            .Concat(Runs(1, "waiting", from: 1))
            .Concat(Runs(1, "yielded", from: 2))
            .Concat(Runs(1, "stopped", from: 3, stop: "over the 5 min limit"))
            .Concat(Runs(1, "stopped", from: 4, stop: "stopped from the page"))
            .Concat(Runs(5, "done", from: 5));

        var group = Assert.Single(OutcomeTable.Build(Rows([.. lines]), null, Now));

        Assert.Equal(1, group.Question.Count);
        Assert.Equal(1, group.Planner.Count);
        Assert.Equal(1, group.Yielded.Count);
        Assert.Equal(1, group.TimeLimit.Count);
        Assert.Equal(1, group.Stopped.Count);
        Assert.Equal(10, group.Question.Share);
    }

    [Fact]
    public void EST_22_WorkerTypesAreGroupedIgnoringCase()
    {
        var rows = Rows([.. Runs(6, "done", type: "Spark"), .. Runs(6, "done", type: "spark", from: 6)]);

        var group = Assert.Single(OutcomeTable.Build(rows, null, Now));

        Assert.Equal(12, group.Runs);
    }

    [Fact]
    public void EST_22_Window_KeepsTheNewest100Runs_AndDropsThoseOver60Days()
    {
        var lines = Runs(100, "done").Concat(Runs(20, "failed", from: 100)).Append(Line(result: "failed", daysAgo: 61));

        var group = Assert.Single(OutcomeTable.Build(Rows([.. lines]), null, Now));

        Assert.Equal(100, group.Runs);
        Assert.Equal(100, group.Done.Share);
        Assert.Equal(0, group.Failed.Count);
    }

    [Fact]
    public void EST_23_CardRunThreeTimes_IsOneCard_MoreThanOneRun1_RunsPerCard3()
    {
        var lines = new[]
        {
            Line(result: "failed", card: "7-A-x", minutesAgo: 30),
            Line(result: "needs-human", card: "7-A-x", minutesAgo: 20),
            Line(result: "done", card: "7-A-x", minutesAgo: 10),
        };

        var group = Assert.Single(OutcomeTable.Build(Rows(lines), null, Now));

        Assert.Equal(1, group.Cards);
        Assert.Equal(1, group.MoreThanOneRun);
        Assert.Equal(3.0, group.RunsPerCard);
    }

    [Fact]
    public void EST_23_CardDoneSentBackAndDoneAgain_CountsTwoFinishesOfOneRunEach()
    {
        var lines = new[]
        {
            Line(result: "done", card: "7-A-x", minutesAgo: 30),
            Line(result: "done", card: "7-A-x", minutesAgo: 10),
        };

        var group = Assert.Single(OutcomeTable.Build(Rows(lines), null, Now));

        Assert.Equal(2, group.Cards);
        Assert.Equal(0, group.MoreThanOneRun);
        Assert.Equal(1.0, group.RunsPerCard);
    }

    [Fact]
    public void EST_23_EarlierRunsOutsideTheWindowStillCount_SetAsideOnesDoNot_TheFinishIsChargedToItsWorkerType()
    {
        var lines = new[]
        {
            Line(result: "failed", type: "Spark", card: "9-B-y", daysAgo: 70),
            Line(result: "out of quota", type: "Spark", card: "9-B-y", daysAgo: 69, minutes: 0),
            Line(result: "crashed (exit 1)", type: "Spark", card: "9-B-y", daysAgo: 68, minutes: 0),
            Line(result: "done", type: "Opus", card: "9-B-y", daysAgo: 2),
        };

        var group = Assert.Single(OutcomeTable.Build(Rows(lines), null, Now), g => g.Worker == "Opus");

        Assert.Equal(1, group.Cards);
        Assert.Equal(1, group.MoreThanOneRun);
        Assert.Equal(2.0, group.RunsPerCard);
    }

    [Fact]
    public void EST_24_UnderTenRuns_GiveCountsAndNullShares_SourceNone()
    {
        var group = Assert.Single(OutcomeTable.Build(Rows([.. Runs(9, "done")]), null, Now));

        Assert.Equal(9, group.Runs);
        Assert.Equal(FigureSource.None, group.Source);
        Assert.Equal(new OutcomeShare(9, null), group.Done);
        Assert.Null(group.Failed.Share);
    }

    [Fact]
    public void EST_25_ProjectWithFewRuns_UsesThePoolsWholeLine_WithItsSourceAndCount()
    {
        var project = Rows([.. Runs(4, "done")]);
        var other = Rows([.. Runs(20, "failed", from: 10).Concat(Runs(20, "done", from: 30))]);

        var group = Assert.Single(OutcomeTable.Build(project, [project, other], Now));

        Assert.Equal(FigureSource.Pool, group.Source);
        Assert.Equal(44, group.Runs);
        Assert.Equal(new OutcomeShare(24, 55), group.Done);
        Assert.Equal(new OutcomeShare(20, 45), group.Failed);
    }

    [Fact]
    public void EST_25_ProjectWithTenRuns_KeepsItsOwnLine_EvenWithAPool()
    {
        var project = Rows([.. Runs(10, "done")]);
        var other = Rows([.. Runs(50, "failed", from: 20)]);

        var group = Assert.Single(OutcomeTable.Build(project, [project, other], Now));

        Assert.Equal(FigureSource.Project, group.Source);
        Assert.Equal(10, group.Runs);
        Assert.Equal(100, group.Done.Share);
    }

    [Fact]
    public void EST_25_FewRunsAnywhere_IsNone()
    {
        var project = Rows([.. Runs(4, "done")]);
        var other = Rows([.. Runs(3, "failed", from: 10)]);

        var group = Assert.Single(OutcomeTable.Build(project, [project, other], Now));

        Assert.Equal(FigureSource.None, group.Source);
        Assert.Equal(4, group.Runs);
        Assert.Null(group.Done.Share);
    }

    [Fact]
    public void EST_27_SameCardNumberInTwoProjects_IsTwoCards()
    {
        var a = Rows(Line(result: "failed", card: "12-A-x", minutesAgo: 20), Line(result: "done", card: "12-A-x", minutesAgo: 10));
        var b = Rows(Line(result: "done", card: "12-A-x", minutesAgo: 5));
        var many = Rows([.. Runs(10, "done", from: 100)]);

        var group = Assert.Single(OutcomeTable.Build(b, [a, b, many], Now));

        // 12 finishes (a's retried once, b's not, and 10 more): if the two 12-A were one card, b's would count as a retry.
        Assert.Equal(FigureSource.Pool, group.Source);
        Assert.Equal(12, group.Cards);
        Assert.Equal(1, group.MoreThanOneRun);
    }
}
