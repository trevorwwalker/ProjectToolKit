// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Accounting;

namespace Scheduler.Core.Tests.Accounting;

/// <summary>
/// Roadmap item 3 (card 630-A): the daily figures the Trends window charts - per local day and per
/// model, how many cards finished (done or review) and how many runs, over the last 14 days. The
/// runs that were nobody's fault are left out as ACCT-6 leaves them out.
/// </summary>
public sealed class TrendsTests
{
    // A noon instant keeps every day of the window on a whole local date under the UTC zone.
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private static RunStat Run(string card, string result, int minutes, string model, DateTimeOffset finished) =>
        new() { Finished = finished, Result = result, Minutes = minutes, Model = model, Card = card };

    // The card's runs of the last 14 days, by local day and model; the UTC zone stands in for the
    // machine's own so the dates the test asserts do not depend on where the test runs.
    private static IReadOnlyList<TrendDay> Daily(IReadOnlyList<RunStat> runs, IReadOnlyList<ModelEntry>? models = null) =>
        Trends.Daily(runs, Now, models: models, zone: TimeZoneInfo.Utc);

    // ACCT-1's rows, read back for the chart. The Control window's figures only reach 48 h behind
    // the start (ACCT-4), so the 14-day chart reads the file's own lines through here.
    [Fact]
    public void Trends_RunsFromRows_ReadsAccountingsOwnLines()
    {
        var lines = new[]
        {
            "finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model",
            "2026-11-19T11:30:00,010-A-some-card,A,1,done,32,12345,\"worker ended\",opus",
            "2026-11-18T09:30:00,020-B-other,C,2,review,20,4321,\"asked to stop\",gpt",
            // a stop reason with a comma in it: the row is still readable, the model is the last field
            "2026-11-18T08:00:00,030-C-third,B,3,done,10,100,\"stopped, then killed\",opus",
            // ACCT-4: a row from before the model column has eight fields and no model
            "2026-11-17T08:00:00,040-D-old,B,1,done,10,100,\"worker ended\"",
            // outside the 14 days, and not a row at all
            "2026-11-01T08:00:00,050-E-old,A,1,done,10,100,\"worker ended\",opus",
            "a line that is not a row",
        };

        var runs = Trends.RunsFromRows(lines, Now, zone: TimeZoneInfo.Utc);

        Assert.Equal(4, runs.Count);
        var first = runs[0];
        Assert.Equal("010-A", first.Card);
        Assert.Equal("done", first.Result);
        Assert.Equal(32, first.Minutes);
        Assert.Equal("opus", first.Model);
        // ACCT-1's times are local, so the row reads back with the machine's own offset (ACCT-4).
        Assert.Equal("2026-11-19T11:30:00", first.Finished.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
        Assert.Equal("gpt", runs[1].Model);
        Assert.Equal("opus", runs[2].Model);
        Assert.Equal("030-C", runs[2].Card);
        Assert.Equal("", runs[3].Model);

        // Straight into the chart: three days carry something, and the oldest day of all does not.
        var days = Daily([.. runs]);
        Assert.Equal(3, days.Count(day => day.HasData));
        Assert.Equal((1, 1), (days[13].Segments.Single().CardsDone, days[13].Segments.Single().Runs));
        var yesterday = days.Single(day => day.Date == new DateOnly(2026, 11, 18));
        Assert.Equal(2, yesterday.CardsDone);
        Assert.Equal(2, yesterday.Runs);
        Assert.Equal(["gpt", "opus"], yesterday.Segments.Select(s => s.Model).ToArray());
        Assert.True(Trends.HasTrend(runs, Now, TimeZoneInfo.Utc));
    }

    // R3 (roadmap item 3): days along the bottom, and within a day one entry per model holding the
    // cards that finished and the runs it took. A card that ran twice and finished counts once.
    [Fact]
    public void Trends_Daily_GroupsByLocalDayAndModel()
    {
        var runs = new[]
        {
            Run("010-A", "done", 30, "opus", Now.AddHours(-1)),
            Run("010-A", "done", 20, "opus", Now.AddHours(-2)),
            Run("020-B", "review", 15, "opus", Now.AddHours(-3)),
            Run("030-C", "done", 10, "gpt", Now.AddHours(-4)),
            Run("040-D", "done", 10, "gpt", Now.AddDays(-1)),
            Run("050-E", "stopped", 5, "gpt", Now.AddDays(-1)),
            Run("060-F", "done", 5, "opus", Now.AddDays(-3)),
        };

        var days = Daily(runs);

        // Fourteen days, oldest first, ending today, whether or not anything ran on them.
        Assert.Equal(14, days.Count);
        Assert.Equal(new DateOnly(2026, 11, 6), days[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 19), days[13].Date);

        // Today: two models, the cards that finished and the runs behind them.
        var today = days[13];
        Assert.Equal(3, today.CardsDone);
        Assert.Equal(4, today.Runs);
        Assert.Equal(["gpt", "opus"], today.Segments.Select(s => s.Model).ToArray());
        Assert.Equal((1, 1), (today.Segments[0].CardsDone, today.Segments[0].Runs));
        Assert.Equal((2, 3), (today.Segments[1].CardsDone, today.Segments[1].Runs));

        // Yesterday: two runs, only the one that finished as a card.
        var yesterday = days[12];
        Assert.Equal(["gpt"], yesterday.Segments.Select(s => s.Model).ToArray());
        Assert.Equal((1, 2), (yesterday.Segments[0].CardsDone, yesterday.Segments[0].Runs));

        // A day nothing ran on is still there, with nothing in it.
        Assert.Empty(days[11].Segments);
        Assert.Equal(0, days[11].Runs);
        Assert.False(days[11].HasData);

        // Three days back.
        Assert.Equal((1, 1), (days[10].Segments.Single().CardsDone, days[10].Segments.Single().Runs));

        // Nothing at all says so with an empty list of days... a window of nothing has no trend.
        Assert.All(Daily([]), day => Assert.False(day.HasData));
    }

    // models.json's short name is what the chart labels a model with, as ACCT-6's per-model lines do.
    [Fact]
    public void Trends_Daily_LabelsModelsWithTheShortName()
    {
        var runs = new[]
        {
            Run("010-A", "done", 30, "opus-key", Now.AddHours(-1)),
            Run("020-B", "done", 30, "unnamed-key", Now.AddHours(-2)),
        };
        var models = new[] { new ModelEntry { Key = "opus-key", Short = "Opus 4.5" } };

        var segments = Daily(runs, models)[13].Segments;

        // Name order, with models.json's short name where there is one and the key where there is not.
        Assert.Equal("opus-key", segments[0].Model);
        Assert.Equal("Opus 4.5", segments[0].Label);
        Assert.Equal("unnamed-key", segments[1].Model);
        Assert.Equal("unnamed-key", segments[1].Label);
    }

    // ACCT-6's exclusions: a run that never started, hit a quota or found a down model server is
    // nobody's doing, and a run with no card is no card's work. None of them shows in the chart.
    [Fact]
    public void Trends_Daily_LeavesOutRunsThatWereNobodysFault()
    {
        var runs = new[]
        {
            Run("050-E", "no log", 1, "opus", Now.AddDays(-1)),
            Run("060-F", "Out of Quota", 1, "opus", Now.AddDays(-1)),
            Run("070-G", "MODEL SERVER DOWN", 1, "opus", Now.AddDays(-1)),
            Run("", "done", 10, "opus", Now.AddDays(-1)),
            Run("080-H", "done", 30, "opus", Now.AddHours(-1)),
        };

        var days = Daily(runs);

        Assert.Empty(days[12].Segments);
        Assert.False(days[12].HasData);
        Assert.Equal((1, 1), (days[13].Segments.Single().CardsDone, days[13].Segments.Single().Runs));
    }

    // The window is 14 local days: the first day's earliest instant is in, the day before is out.
    [Fact]
    public void Trends_Daily_CoversFourteenLocalDays()
    {
        var runs = new[]
        {
            Run("010-A", "done", 10, "opus", new DateTimeOffset(2026, 11, 6, 0, 30, 0, TimeSpan.Zero)),
            Run("020-B", "done", 10, "opus", new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.Zero)),
            Run("030-C", "done", 10, "opus", Now.AddMinutes(-5)),
            Run("040-D", "done", 10, "opus", Now.AddMinutes(-1)),
        };

        var days = Daily(runs);

        Assert.Equal(new DateOnly(2026, 11, 6), days[0].Date);
        Assert.Equal(1, days[0].Runs);
        Assert.Equal(1, days[0].Segments.Single().CardsDone);
        Assert.Equal(3, days.Sum(day => day.Runs));
        // A chart that wants fewer days may ask for fewer.
        Assert.Equal(2, Trends.Daily(runs, Now, days: 2, zone: TimeZoneInfo.Utc).Count);
        Assert.Equal(2, Trends.Daily(runs, Now, days: 2, zone: TimeZoneInfo.Utc).Sum(day => day.Runs));
    }

    // 1327-C: one day with a counted run is a trend (the two-days rule is gone); no data is none.
    [Fact]
    public void Trends_HasTrend_OneDayWithDataIsEnough()
    {
        var todayOnly = new[]
        {
            Run("010-A", "done", 30, "opus", Now.AddHours(-1)),
            Run("020-B", "done", 30, "opus", Now.AddHours(-2)),
            Run("030-C", "review", 30, "opus", Now.AddHours(-3)),
        };
        Assert.True(Trends.HasTrend(todayOnly, Now, TimeZoneInfo.Utc));
        Assert.True(Trends.HasTrend(Daily(todayOnly)));

        var twoDays = todayOnly.Append(Run("040-D", "done", 30, "opus", Now.AddDays(-1))).ToArray();
        Assert.True(Trends.HasTrend(twoDays, Now, TimeZoneInfo.Utc));
        Assert.True(Trends.HasTrend(Daily(twoDays)));

        // Runs that are all excluded are no data.
        var quiet = new[] { Run("050-E", "no log", 1, "opus", Now.AddHours(-1)) };
        Assert.False(Trends.HasTrend(quiet, Now, TimeZoneInfo.Utc));
        Assert.False(Trends.HasTrend(Daily(quiet)));

        // Nothing at all, and two days older than the window, are both nothing to chart.
        Assert.False(Trends.HasTrend([], Now, TimeZoneInfo.Utc));
        Assert.False(Trends.HasTrend((IReadOnlyList<TrendDay>)[]));
        var stale = new[]
        {
            Run("060-F", "done", 10, "opus", new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero)),
            Run("070-G", "done", 10, "opus", new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero)),
        };
        Assert.False(Trends.HasTrend(stale, Now, TimeZoneInfo.Utc));
    }

    // A card that tried on two models the same day is one card under each, which is what makes the
    // comparison the chart exists for; a retry yesterday counts on both days.
    [Fact]
    public void Trends_Daily_CountsACardUnderEachModelItRanOn()
    {
        var runs = new[]
        {
            Run("010-A", "done", 20, "opus", Now.AddHours(-5)),
            Run("010-A", "done", 20, "gpt", Now.AddHours(-1)),
            Run("010-A", "done", 20, "opus", Now.AddDays(-1)),
        };

        var days = Daily(runs);

        Assert.Equal(2, days[13].CardsDone);
        Assert.Equal(1, days[13].Segments.Single(s => s.Model == "opus").CardsDone);
        Assert.Equal(1, days[13].Segments.Single(s => s.Model == "gpt").CardsDone);
        Assert.Equal((1, 1), (days[12].Segments.Single().CardsDone, days[12].Segments.Single().Runs));
    }

    // The local zone as a fixed -8: a machine's own zone would make the test's dates its business.
    private static readonly TimeZoneInfo WestEight =
        TimeZoneInfo.CreateCustomTimeZone("test-utc-8", TimeSpan.FromHours(-8), "test -8", "test -8");

    // The date is the local one, not the UTC one: a run just after midnight local belongs to that day.
    [Fact]
    public void Trends_Daily_UsesTheLocalDate()
    {
        var zone = WestEight;
        var runs = new[]
        {
            // 2026-11-19 00:30 in Los Angeles is 2026-11-19 08:30 UTC: the same UTC day either way.
            Run("010-A", "done", 10, "opus", new DateTimeOffset(2026, 11, 19, 8, 30, 0, TimeSpan.Zero)),
            // 2026-11-19 16:30 UTC is 2026-11-19 08:30 local, still the 19th.
            Run("020-B", "done", 10, "opus", new DateTimeOffset(2026, 11, 19, 16, 30, 0, TimeSpan.Zero)),
            // 2026-11-20 06:30 UTC is 2026-11-19 22:30 local: the 19th locally, the 20th in UTC.
            Run("030-C", "done", 10, "opus", new DateTimeOffset(2026, 11, 20, 6, 30, 0, TimeSpan.Zero)),
        };

        var days = Trends.Daily(runs, Now, zone: zone);

        // The window's last day is today's local date, and all three runs fall on it.
        Assert.Equal(new DateOnly(2026, 11, 19), days[13].Date);
        Assert.Equal(3, days[13].Runs);
        Assert.Equal(3, days[13].CardsDone);
    }
}
