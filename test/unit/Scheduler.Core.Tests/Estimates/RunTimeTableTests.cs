// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Estimates;
using Xunit;
using static Scheduler.Core.Tests.Estimates.EstimateTestRows;

namespace Scheduler.Core.Tests.Estimates;

/// <summary>EST-9 to EST-14, EST-25 and EST-26: Table 1, run time by worker type and kind.</summary>
public sealed class RunTimeTableTests
{
    [Fact]
    public void EST_9_120Runs_GiveTheNewest100_AndA61DayOldRunIsOut()
    {
        // Run i is i minutes old and i minutes long, so the newest 100 are the 0..99 ones.
        var lines = Many(120, i => Line(minutes: i, kind: "design", minutesAgo: i)).Append(Line(minutes: 5000, kind: "design", daysAgo: 61));

        var group = Assert.Single(RunTimeTable.Build(Rows([.. lines]), null, Now));

        Assert.Equal(100, group.Runs);
        Assert.Equal(100, group.P90.Runs);
        Assert.Equal(89, group.P90.Minutes); // the 90th of 0..99
        Assert.Equal(50, group.Median.Minutes); // (49 + 50) / 2 = 49.5, up
    }

    [Fact]
    public void EST_9_RunsOlderThan60Days_AreOut_Within60AreIn()
    {
        var table = RunTimeTable.Build(
            Rows(
                Line(minutes: 5, kind: "docs", daysAgo: 59),
                Line(minutes: 6, kind: "docs", daysAgo: 59),
                Line(minutes: 7, kind: "docs", daysAgo: 59),
                Line(minutes: 900, kind: "docs", daysAgo: 61)),
            null,
            Now);

        var group = Assert.Single(table);
        Assert.Equal(3, group.Runs);
        Assert.Equal(6, group.Median.Minutes);
    }

    [Fact]
    public void EST_10_TwoRunsGiveNoMedian_ThreeGiveOne()
    {
        var two = Assert.Single(RunTimeTable.Build(Rows(Line(minutes: 4, kind: "docs"), Line(minutes: 8, kind: "docs", minutesAgo: 1)), null, Now));
        Assert.Null(two.Median.Minutes);
        Assert.Equal(FigureSource.None, two.Median.Source);

        var three = Assert.Single(RunTimeTable.Build(
            Rows(Line(minutes: 4, kind: "docs"), Line(minutes: 8, kind: "docs", minutesAgo: 1), Line(minutes: 6, kind: "docs", minutesAgo: 2)),
            null,
            Now));
        Assert.Equal(6, three.Median.Minutes);
        Assert.Equal(FigureSource.Project, three.Median.Source);
        Assert.Equal(3, three.Median.Runs);
    }

    [Fact]
    public void EST_10_NineRunsGiveNoP90_TenGiveTheNinth()
    {
        var nine = Assert.Single(RunTimeTable.Build(Rows([.. Many(9, i => Line(minutes: i + 1, kind: "docs", minutesAgo: i))]), null, Now));
        Assert.Null(nine.P90.Minutes);
        Assert.NotNull(nine.Median.Minutes);

        var ten = Assert.Single(RunTimeTable.Build(Rows([.. Many(10, i => Line(minutes: i + 1, kind: "docs", minutesAgo: i))]), null, Now));
        Assert.Equal(9, ten.P90.Minutes); // ceil(0.9 * 10) = the 9th of 1..10
    }

    [Fact]
    public void EST_11_EvenCount_MedianIsTheMeanRoundedUpOnAHalf()
    {
        var group = Assert.Single(RunTimeTable.Build(
            Rows(
                Line(minutes: 1, kind: "docs"),
                Line(minutes: 2, kind: "docs", minutesAgo: 1),
                Line(minutes: 3, kind: "docs", minutesAgo: 2),
                Line(minutes: 4, kind: "docs", minutesAgo: 3)),
            null,
            Now));

        Assert.Equal(3, group.Median.Minutes); // 2.5 up
    }

    [Fact]
    public void EST_12_RunWithNoKind_LandsInItsCategoryGroup()
    {
        var table = RunTimeTable.Build(
            Rows(Line(kind: "design", lane: "A"), Line(kind: "", lane: "C", minutesAgo: 1), Line(kind: "", lane: "C", minutesAgo: 2)),
            null,
            Now);

        Assert.Equal(2, table.Count);
        Assert.Equal("design", table[0].Kind);
        Assert.Equal("", table[0].Category);
        Assert.Equal("", table[1].Kind);
        Assert.Equal("C", table[1].Category);
        Assert.Equal(2, table[1].Runs);
    }

    [Fact]
    public void EST_12_SparkAndSpark_AreOneGroup_ShownAsTheNewestRowsSpelling()
    {
        var table = RunTimeTable.Build(
            Rows(Line(type: "Spark", kind: "docs", minutesAgo: 5), Line(type: "spark", kind: "docs", minutesAgo: 0), Line(type: "Spark", kind: "docs", minutesAgo: 9)),
            null,
            Now);

        var group = Assert.Single(table);
        Assert.Equal("spark", group.Worker);
        Assert.Equal(3, group.Runs);
    }

    [Fact]
    public void EST_12_NoType_UsesTheModel_AndNoModelEither_IsOther()
    {
        var table = RunTimeTable.Build(
            Rows(Line(type: "", model: "claude-opus-5-5", kind: "docs"), Line(type: "", model: "", kind: "docs", minutesAgo: 1)),
            null,
            Now);

        Assert.Equal(["claude-opus-5-5", "other"], table.Select(g => g.Worker).Order().ToArray());
    }

    [Fact]
    public void EST_13_ReviewOverTheLimit_Counts_NeedsHumanDoesNot_NegativeMinutesAreSkipped()
    {
        var table = RunTimeTable.Build(
            Rows(
                Line(result: "review", stop: "over the 20 min limit", kind: "docs"),
                Line(result: "needs-human", kind: "docs", minutesAgo: 1),
                Line(result: "failed", kind: "docs", minutesAgo: 2),
                Line(result: "done", minutes: -1, kind: "docs", minutesAgo: 3)),
            null,
            Now);

        var group = Assert.Single(table);
        Assert.Equal(1, group.Runs);
    }

    [Fact]
    public void EST_14_GroupGivesTheOldestAndNewestFinishOfTheRunsUsed()
    {
        var group = Assert.Single(RunTimeTable.Build(Rows(Line(kind: "docs", daysAgo: 3), Line(kind: "docs", daysAgo: 1), Line(kind: "docs", daysAgo: 70)), null, Now));

        Assert.Equal(2, group.Runs);
        Assert.Equal(Now.AddDays(-3), group.From, TimeSpan.FromSeconds(1));
        Assert.Equal(Now.AddDays(-1), group.To, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void EST_14_SortedByWorkerThenKind_CategoryGroupsLast()
    {
        var table = RunTimeTable.Build(
            Rows(
                Line(type: "Spark", kind: "docs"),
                Line(type: "Opus", kind: "design", minutesAgo: 1),
                Line(type: "Opus", kind: "check", minutesAgo: 2),
                Line(type: "Haiku", kind: "", lane: "B", minutesAgo: 3),
                Line(type: "Opus", kind: "", lane: "A", minutesAgo: 4)),
            null,
            Now);

        Assert.Equal(
            ["Opus/check/", "Opus/design/", "Spark/docs/", "Haiku//B", "Opus//A"],
            table.Select(g => $"{g.Worker}/{g.Kind}/{g.Category}").ToArray());
    }

    [Fact]
    public void EST_25_FiveProjectRuns_GiveTheirOwnMedianAndThePoolsP90_EachWithItsSourceAndCount()
    {
        var project = Rows([.. Many(5, i => Line(minutes: 10 + i, kind: "design", minutesAgo: i))]);
        var other = Rows([.. Many(20, i => Line(minutes: 100 + i, kind: "design", minutesAgo: i))]);

        var group = Assert.Single(RunTimeTable.Build(project, [project, other], Now));

        Assert.Equal(12, group.Median.Minutes);
        Assert.Equal(FigureSource.Project, group.Median.Source);
        Assert.Equal(5, group.Median.Runs);
        Assert.Equal(FigureSource.Pool, group.P90.Source);
        Assert.Equal(25, group.P90.Runs);
        Assert.NotNull(group.P90.Minutes);
    }

    [Fact]
    public void EST_25_OneProjectRunAndAPoolOf20_BothComeFromThePool()
    {
        var project = Rows(Line(minutes: 3, kind: "design"));
        var other = Rows([.. Many(20, i => Line(minutes: 50, kind: "design", minutesAgo: i + 1))]);

        var group = Assert.Single(RunTimeTable.Build(project, [project, other], Now));

        Assert.Equal(50, group.Median.Minutes);
        Assert.Equal(FigureSource.Pool, group.Median.Source);
        Assert.Equal(21, group.Median.Runs);
        Assert.Equal(FigureSource.Pool, group.P90.Source);
        Assert.Equal(1, group.Runs);
    }

    [Fact]
    public void EST_25_NothingAnywhere_IsNone_WithTheProjectsOwnCount()
    {
        var project = Rows(Line(kind: "design"));

        var group = Assert.Single(RunTimeTable.Build(project, [project], Now));

        Assert.Null(group.Median.Minutes);
        Assert.Equal(FigureSource.None, group.Median.Source);
        Assert.Equal(1, group.Median.Runs);
        Assert.Equal(FigureSource.None, group.P90.Source);
    }

    [Fact]
    public void EST_25_NoPool_NeverGivesAPoolSource()
    {
        var project = Rows(Line(kind: "design"), Line(kind: "design", minutesAgo: 1));

        var group = Assert.Single(RunTimeTable.Build(project, null, Now));

        Assert.Equal(FigureSource.None, group.Median.Source);
    }

    [Fact]
    public void EST_26_CategoryGroup_WithTwoProjectRunsAndFiftyPoolRuns_GivesNone()
    {
        var project = Rows(Line(kind: "", lane: "A"), Line(kind: "", lane: "A", minutesAgo: 1));
        var other = Rows([.. Many(50, i => Line(kind: "", lane: "A", minutesAgo: i + 2))]);

        var group = Assert.Single(RunTimeTable.Build(project, [project, other], Now));

        Assert.Equal("A", group.Category);
        Assert.Null(group.Median.Minutes);
        Assert.Equal(FigureSource.None, group.Median.Source);
        Assert.Equal(FigureSource.None, group.P90.Source);
    }
}
