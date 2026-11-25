// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;

namespace Scheduler.Core.Tests.Accounting;

/// <summary>
/// Roadmap item 4 (card 640-A): what finished cards really take, per area and model - the median
/// and the 90th percentile of the minutes over the last 30 runs or 30 days of the done and review
/// rows (ACCT-1). A group with too few runs gives no figure at all.
/// </summary>
public sealed class AreaDurationsTests
{
    // A noon instant keeps the window's edges away from midnight under any machine offset.
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private static AreaRun Run(string area, string model, int minutes, DateTimeOffset finished) =>
        new() { Area = area, Model = model, Minutes = minutes, Finished = finished };

    // ACCT-1's rows, read back for the figures: only rows whose card finished count, and only when
    // they name a card, an area and a model, and their minutes read.
    [Fact]
    public void AreaDurations_RunsFromRows_KeepsFinishedCardRunsOnly()
    {
        var lines = new[]
        {
            "finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model",
            "2026-11-19T11:30:00,010-A-some-card,A,1,done,32,12345,\"worker ended\",opus",
            "2026-11-18T09:30:00,020-B-other,C,2,review,20,4321,\"asked to stop\",gpt",
            // a stop reason with a comma: the row is still readable, the model is the last field
            "2026-11-18T08:00:00,030-C-third,B,3,done,10,100,\"stopped, then killed\",opus",
            // not a finished card: stopped, killed, no log, out of quota, no result line
            "2026-11-18T07:00:00,040-D-stopped,A,1,stopped,50,100,\"over the 120 min limit\",opus",
            "2026-11-18T06:00:00,041-D-killed,A,1,killed,90,100,\"killed\",opus",
            "2026-11-18T05:00:00,042-D-no-log,A,1,no log,0,0,\"never started\",opus",
            "2026-11-18T04:00:00,043-D-quota,A,1,out of quota,3,100,\"quota\",opus",
            // no card, no area, no model (a row from before the model column, ACCT-4), no minutes
            "2026-11-17T08:00:00,,A,1,done,10,100,\"worker ended\",opus",
            "2026-11-17T08:00:00,050-E-nolane,,1,done,10,100,\"worker ended\",opus",
            "2026-11-17T08:00:00,051-E-nomodel,A,1,done,10,100,\"worker ended\"",
            "2026-11-17T08:00:00,052-E-nominutes,A,1,done,x,100,\"worker ended\",opus",
            // outside the 30 days, and not a row at all
            "2026-10-01T08:00:00,060-F-old,A,1,done,10,100,\"worker ended\",opus",
            "a line that is not a row",
        };

        var runs = AreaDurations.RunsFromRows(lines, Now);

        Assert.Equal(3, runs.Count);
        Assert.Equal(("A", "opus", 32), (runs[0].Area, runs[0].Model, runs[0].Minutes));
        Assert.Equal(("C", "gpt", 20), (runs[1].Area, runs[1].Model, runs[1].Minutes));
        Assert.Equal(("B", "opus", 10), (runs[2].Area, runs[2].Model, runs[2].Minutes));
        // ACCT-1's times are local (ACCT-4): the row reads back as the wall-clock reading it was
        // written with, whatever zone the machine keeps.
        Assert.Equal("2026-11-18T09:30:00", runs[1].Finished.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"));
    }

    // A row older than the window drops out; the window is 30 days unless a test says otherwise.
    // The times stay well away from the edges so the machine's own offset cannot swing a row over one.
    [Fact]
    public void AreaDurations_RunsFromRows_DropsRowsOutsideTheWindow()
    {
        var lines = new[]
        {
            // 28 days back: inside a 30-day window, outside a 7-day one.
            "2026-10-22T08:00:00,010-A-recent,A,1,done,10,100,\"worker ended\",opus",
            // 35 days back: outside both.
            "2026-10-15T08:00:00,011-A-too-old,A,1,done,10,100,\"worker ended\",opus",
        };

        var full = AreaDurations.RunsFromRows(lines, Now, days: 30);
        Assert.Single(full);
        Assert.Equal("A", full[0].Area);

        // A week's window keeps neither.
        Assert.Empty(AreaDurations.RunsFromRows(lines, Now, days: 7));
    }

    // The figures themselves: the middle run is the median, and the 90th percentile is the nearest
    // rank - up to it, 9 runs in 10 finished.
    [Fact]
    public void AreaDurations_Typical_MedianAndP90OfMinutes()
    {
        var minutes = new[] { 10, 20, 30, 40, 100 };
        var runs = minutes
            .Select((m, i) => Run("A", "opus", m, Now.AddHours(-i - 1)))
            .ToList();

        var typical = Assert.Single(AreaDurations.Typical(runs));

        Assert.Equal("A", typical.Area);
        Assert.Equal("opus", typical.Model);
        Assert.Equal(5, typical.Runs);
        Assert.Equal(30, typical.Median);
        Assert.Equal(100, typical.P90);

        // An even count: the mean of the two middles, rounded up on a half; p90 the 4th of 4.
        var even = new[] { 10, 20, 30, 40 }
            .Select((m, i) => Run("A", "opus", m, Now.AddHours(-i - 1)))
            .ToList();
        var evenTypical = Assert.Single(AreaDurations.Typical(even));
        Assert.Equal(25, evenTypical.Median);
        Assert.Equal(40, evenTypical.P90);

        // Three runs: p90 is the largest, 90% of 3 rounds up to 3.
        var three = new[] { 5, 50, 95 }
            .Select((m, i) => Run("B", "gpt", m, Now.AddHours(-i - 1)))
            .ToList();
        var threeTypical = Assert.Single(AreaDurations.Typical(three));
        Assert.Equal(50, threeTypical.Median);
        Assert.Equal(95, threeTypical.P90);
    }

    // "Too few runs gives no figure": two runs of an area are an anecdote, not a percentile, and a
    // group is left out entirely rather than showing a made-up number.
    [Fact]
    public void AreaDurations_Typical_TooFewRunsGivesNoFigure()
    {
        var runs = new[]
        {
            Run("A", "opus", 100, Now.AddHours(-1)),
            Run("A", "opus", 200, Now.AddHours(-2)),
            Run("A", "gpt", 10, Now.AddHours(-3)),
        };

        var typicals = AreaDurations.Typical(runs);

        Assert.DoesNotContain(typicals, t => t.Area == "A" && t.Model == "opus");
        // Three is enough: the group appears with all three of its runs.
        Assert.Equal(3, AreaDurations.Typical([.. runs, Run("A", "gpt", 20, Now.AddHours(-4)), Run("A", "gpt", 30, Now.AddHours(-5))])
            .Single(t => t.Model == "gpt").Runs);
    }

    // The window is the newest runs, not everything the file holds: a slow era on an old model must
    // not stretch today's suggestion, so at most the newest 30 runs are measured.
    [Fact]
    public void AreaDurations_Typical_MeasuresOnlyTheNewest30Runs()
    {
        var runs = new List<AreaRun>();
        // Thirty recent runs of 60 minutes, then five older ones of 5: the old five must not count.
        for (var i = 0; i < 30; i++)
        {
            runs.Add(Run("A", "opus", 60, Now.AddHours(-i - 1)));
        }

        for (var i = 0; i < 5; i++)
        {
            runs.Add(Run("A", "opus", 5, Now.AddHours(-i - 40)));
        }

        var typical = Assert.Single(AreaDurations.Typical(runs));

        Assert.Equal(AreaDurations.WindowRuns, typical.Runs);
        Assert.Equal(60, typical.Median);
        Assert.Equal(60, typical.P90);
    }

    // One group's figures are picked out by area and model; text compares as ever, and no entry is
    // invented for a pair that has no history.
    [Fact]
    public void AreaDurations_For_PicksTheAreaAndModel()
    {
        var runs = new[]
        {
            Run("A", "opus", 10, Now.AddHours(-1)),
            Run("A", "opus", 20, Now.AddHours(-2)),
            Run("A", "opus", 30, Now.AddHours(-3)),
            Run("b", "GPT", 40, Now.AddHours(-4)),
            Run("b", "GPT", 50, Now.AddHours(-5)),
            Run("b", "GPT", 60, Now.AddHours(-6)),
        };
        var typicals = AreaDurations.Typical(runs);

        Assert.Equal(30, AreaDurations.For(typicals, "a", "opus")?.P90);
        Assert.Equal(60, AreaDurations.For(typicals, "B", "gpt")?.P90);
        Assert.Null(AreaDurations.For(typicals, "A", "gpt"));
        Assert.Null(AreaDurations.For(typicals, "C", "opus"));
        Assert.Null(AreaDurations.For(typicals, "", "opus"));
        Assert.Null(AreaDurations.For(typicals, "A", ""));
    }
}
