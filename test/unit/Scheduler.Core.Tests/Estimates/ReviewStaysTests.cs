// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Estimates;
using Scheduler.Core.MainModel;
using Xunit;
using static Scheduler.Core.Tests.Estimates.EstimateTestRows;

namespace Scheduler.Core.Tests.Estimates;

/// <summary>EST-15, EST-16 (the line) and EST-20: review stays from commit changes, and Table 2.</summary>
public sealed class ReviewStaysTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private static int _sha;

    // A commit at T0 plus `minutes`: "+path" adds, "-path" deletes, "~path" changes.
    private static MainCommit C(int minutes, params string[] changes) => new(
        "c" + Interlocked.Increment(ref _sha),
        null,
        T0.AddMinutes(minutes),
        "jobs: x",
        [.. changes.Select(c => c[0] switch
        {
            '+' => new PathChange(c[1..], null, "b"),
            '-' => new PathChange(c[1..], "b", null),
            _ => new PathChange(c[1..], "a", "b"),
        })]);

    private static ReviewStay Closed(string card, int minutes, string to = "done", int daysAgo = 1) =>
        new(card, Now.AddDays(-daysAgo).AddMinutes(-minutes), Now.AddDays(-daysAgo), to, "e", "l");

    private static readonly IReadOnlyDictionary<string, string> NoKinds = new Dictionary<string, string>();

    [Fact]
    public void EST_15_AddedToReview_ThenMovedToDone_IsOneStayToDone()
    {
        var stays = new ReviewStays("jobs");

        Assert.Empty(stays.Apply(C(0, "+jobs/review/1-A-x.md", "-jobs/active/1-A-x.md")));
        var closed = stays.Apply(C(13, "-jobs/review/1-A-x.md", "+jobs/done/1-A-x.md"));

        var stay = Assert.Single(closed);
        Assert.Equal("1-A-x", stay.Card);
        Assert.Equal("done", stay.To);
        Assert.Equal(T0, stay.Entered);
        Assert.Equal(T0.AddMinutes(13), stay.Left);
        Assert.Empty(stays.Open);
    }

    [Fact]
    public void EST_15_ReviewQueueReviewDone_IsTwoStays()
    {
        var stays = new ReviewStays("jobs");

        var closed = stays.Apply([
            C(0, "+jobs/review/2-B-y.md"),
            C(5, "-jobs/review/2-B-y.md", "+jobs/queue/2-B-y.md"),
            C(20, "-jobs/queue/2-B-y.md", "+jobs/review/2-B-y.md"),
            C(30, "~jobs/review/2-B-y.md"),
            C(45, "-jobs/review/2-B-y.md", "+jobs/done/2-B-y.md"),
        ]);

        Assert.Equal(["queue", "done"], closed.Select(s => s.To));
        Assert.Equal([5.0, 25.0], closed.Select(s => (s.Left!.Value - s.Entered).TotalMinutes));
    }

    [Fact]
    public void EST_15_ARenameInReview_EndsOneStayAndStartsAnother()
    {
        var stays = new ReviewStays("jobs");

        var closed = stays.Apply([
            C(0, "+jobs/review/3-C-old.md"),
            C(10, "-jobs/review/3-C-old.md", "+jobs/review/3-C-new.md"),
            C(15, "-jobs/review/3-C-new.md", "+jobs/active/3-C-new.md"),
        ]);

        Assert.Equal([("3-C-old", ReviewStays.Gone), ("3-C-new", "active")], closed.Select(s => (s.Card, s.To)));
    }

    [Fact]
    public void EST_15_OtherFolders_AndNonCards_OpenNothing()
    {
        var stays = new ReviewStays("jobs");

        stays.Apply([C(0, "+jobs/queue/4-A-z.md", "+jobs/review/.gitkeep", "+jobs/review/sub/5-A-q.md", "+other/review/6-A-w.md")]);

        Assert.Empty(stays.Open);
        Assert.True(stays.Touches("jobs/done/4-A-z.md"));
        Assert.False(stays.Touches("jobs/plans/p.md"));
    }

    [Fact]
    public void EST_16_AClosedStay_RoundTripsThroughItsLine()
    {
        var stay = new ReviewStay("7-D-a", new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.FromHours(-8)), new DateTimeOffset(2026, 11, 20, 10, 30, 0, TimeSpan.FromHours(-8)), "done", "aaa", "bbb");

        var line = ReviewStays.Format(stay);
        var back = ReviewStays.Parse(line);

        Assert.Equal(6, line.Split(',').Length);
        Assert.NotNull(back);
        Assert.Equal(stay.Entered, back.Entered);
        Assert.Equal(stay.Left, back.Left);
        Assert.Equal((stay.Card, stay.To, stay.EnteredCommit, stay.LeftCommit), (back.Card, back.To, back.EnteredCommit, back.LeftCommit));
        Assert.Null(ReviewStays.Parse(ReviewStays.Header));
        Assert.Null(ReviewStays.Parse(""));
    }

    [Fact]
    public void EST_20_OpenStays_AreCountedApart_AndNotInThePercentiles()
    {
        var closed = new[] { Closed("1-A-a", 10), Closed("2-A-b", 20), Closed("3-A-c", 30) };
        var open = new[] { new ReviewStay("4-A-d", Now.AddMinutes(-500), null, "", "e", ""), new ReviewStay("5-A-e", Now.AddMinutes(-42), null, "", "e", "") };

        var table = ReviewStays.Table(new ReviewStaySource(closed, NoKinds), null, open, Now);

        Assert.Equal(new ReviewOpen(2, 500), table.Open);
        var all = table.Groups[0];
        Assert.Equal(ReviewStays.All, all.Kind);
        Assert.Equal(3, all.Stays);
        Assert.Equal(20, all.Median.Minutes);
    }

    [Fact]
    public void EST_20_AStayThatEndsBeforeItStarts_IsSkipped()
    {
        var backwards = new ReviewStay("1-A-a", Now.AddDays(-1), Now.AddDays(-1).AddMinutes(-5), "done", "e", "l");

        var table = ReviewStays.Table(new ReviewStaySource([backwards, Closed("2-A-b", 7)], NoKinds), null, [], Now);

        Assert.Equal(1, table.Groups[0].Stays);
        Assert.Equal(new ReviewOpen(0, null), table.Open);
    }

    [Fact]
    public void EST_20_ToDoneAndToQueue_AddUp_PerKindAndForAll()
    {
        var kinds = new Dictionary<string, string> { ["1-A-a"] = "design", ["2-A-b"] = "design" };
        var closed = new[] { Closed("1-A-a", 10, "done"), Closed("2-A-b", 20, "queue"), Closed("3-B-c", 30, "done"), Closed("4-B-d", 40, ReviewStays.Gone) };

        var table = ReviewStays.Table(new ReviewStaySource(closed, kinds), null, [], Now);

        Assert.Equal(["all", "design", "category:B"], table.Groups.Select(g => g.Kind));
        Assert.Equal((4, 2, 1), (table.Groups[0].Stays, table.Groups[0].ToDone, table.Groups[0].ToQueue));
        Assert.Equal((2, 1, 1), (table.Groups[1].Stays, table.Groups[1].ToDone, table.Groups[1].ToQueue));
        Assert.Equal((2, 1, 0), (table.Groups[2].Stays, table.Groups[2].ToDone, table.Groups[2].ToQueue));
        Assert.Equal(table.Groups[0].Stays, table.Groups.Skip(1).Sum(g => g.Stays));
    }

    [Fact]
    public void EST_20_KindGroupsAndAll_FallBackToThePool_CategoryGroupsDoNot()
    {
        var kinds = new Dictionary<string, string> { ["1-A-a"] = "design" };
        var project = new ReviewStaySource([Closed("1-A-a", 10), Closed("2-B-b", 10)], kinds);
        var poolStays = Enumerable.Range(1, 12).Select(i => Closed($"{i}-B-p", 100 + i)).ToList();
        var poolKinds = poolStays.Take(6).ToDictionary(s => s.Card, _ => "design");
        var pool = new[] { new ReviewStaySource(poolStays, poolKinds) };

        var table = ReviewStays.Table(project, pool, [], Now);

        var all = table.Groups.Single(g => g.Kind == "all");
        var design = table.Groups.Single(g => g.Kind == "design");
        var category = table.Groups.Single(g => g.Kind == "category:B");
        Assert.Equal((FigureSource.Pool, 12), (all.P90.Source, all.P90.Runs));
        Assert.Equal((FigureSource.Pool, 6), (design.Median.Source, design.Median.Runs));
        Assert.Equal(FigureSource.None, category.Median.Source);
        Assert.Null(category.Median.Minutes);
    }

    [Fact]
    public void EST_20_TheKindIsTheCardsNewestAccountingRow()
    {
        var kinds = ReviewStays.KindsOf(Rows(
            Line(card: "1-A-a", kind: "docs", daysAgo: 3),
            Line(card: "1-A-a", kind: "Design", daysAgo: 1),
            Line(card: "1-A-a", kind: "", daysAgo: 0.5),
            Line(card: "2-A-b", kind: "")));

        Assert.Equal("design", kinds["1-A-a"]);
        Assert.False(kinds.ContainsKey("2-A-b"));
        Assert.Equal("category:A", ReviewStays.CategoryKey("2-A-b"));
    }

    [Fact]
    public void EST_20_StaysEndingMoreThan60DaysAgo_AreOut()
    {
        var table = ReviewStays.Table(new ReviewStaySource([Closed("1-A-a", 10, daysAgo: 61), Closed("2-A-b", 10)], NoKinds), null, [], Now);

        Assert.Equal(1, table.Groups[0].Stays);
    }
}
