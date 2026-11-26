// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Tests.Accounting;

/// <summary>1095-C: the runs summary and the Trends buckets over the eight ranges the page offers.</summary>
public class StatsRangeTests
{
    // 12:10 local (UTC stands in for the machine's zone, so the buckets the tests name do not move).
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 10, 0, TimeSpan.Zero);

    private static RunStat Run(string card, string result, int minutes, string model, DateTimeOffset finished) =>
        new() { Finished = finished, Result = result, Minutes = minutes, Model = model, Card = card };

    private static Card Done(string id) => new()
    {
        Folder = CardFolder.Done,
        Path = $"jobs/done/{id}-slug.md",
        Name = $"{id}-slug",
        Num = long.Parse(id.AsSpan(0, id.IndexOf('-')), System.Globalization.CultureInfo.InvariantCulture),
        Category = id.AsSpan(id.IndexOf('-') + 1).ToString(),
        Id = id,
        Status = "done",
    };

    [Fact]
    public void Ranges_AreTheEightTheDeveloperNamed_24hTheDefault()
    {
        Assert.Equal(
            new[] { "1 hour", "3 hours", "6 hours", "12 hours", "24 hours", "3 days", "1 week", "2 weeks" },
            CardStats.Ranges.Select(r => r.Name).ToArray());
        Assert.Equal(new[] { "1h", "3h", "6h", "12h", "24h", "3d", "1w", "2w" }, CardStats.Ranges.Select(r => r.Key).ToArray());
        Assert.Equal(TimeSpan.FromHours(24), CardStats.RangeOf(CardStats.DefaultRangeKey)!.Window);
    }

    [Fact]
    public void Summary_EachWindowCutsItsOwnRuns_AndTheLabelFollows()
    {
        var runs = new[]
        {
            Run("1-C", "done", 10, "m", Now.AddMinutes(-30)),
            Run("2-C", "done", 10, "m", Now.AddHours(-5)),
            Run("3-C", "done", 10, "m", Now.AddHours(-20)),
            Run("4-C", "done", 10, "m", Now.AddDays(-2)),
            Run("5-C", "done", 10, "m", Now.AddDays(-6)),
            Run("6-C", "done", 10, "m", Now.AddDays(-13)),
        };
        var cards = new[] { Done("1-C"), Done("2-C"), Done("3-C"), Done("4-C"), Done("5-C"), Done("6-C") };

        var expected = new (string Key, string Label, int Cards)[]
        {
            ("1h", "1 h", 1), ("3h", "3 h", 1), ("6h", "6 h", 2), ("12h", "12 h", 2), ("24h", "24 h", 3),
            ("3d", "3 days", 4), ("1w", "1 week", 5), ("2w", "2 weeks", 6),
        };
        foreach (var (key, label, count) in expected)
        {
            var range = CardStats.RangeOf(key)!;
            Assert.Equal(count, CardStats.GroupByCard(runs, cards, Now, range.Window).Count);
            var head = CardStats.Summary(runs, cards, Now, null, range.Window)[0];
            Assert.StartsWith($"Last {label} (", head, StringComparison.Ordinal);
            Assert.Contains($"{count} cards", head, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Summary_RateIsPerHourOfTheWindow_AndNoRunsSaysTheLabel()
    {
        var runs = new[] { Run("1-C", "done", 10, "m", Now.AddMinutes(-30)), Run("2-C", "done", 10, "m", Now.AddMinutes(-40)) };
        var cards = new[] { Done("1-C"), Done("2-C") };

        Assert.EndsWith("; 2 cards/hr", CardStats.Summary(runs, cards, Now, null, TimeSpan.FromHours(1))[0], StringComparison.Ordinal);
        Assert.EndsWith("; 0.08 cards/hr", CardStats.Summary(runs, cards, Now)[0], StringComparison.Ordinal);
        Assert.Equal(new[] { "Last 3 days: no runs finished." }, CardStats.Summary([], [], Now, null, TimeSpan.FromDays(3)));
    }

    [Fact]
    public void Range_OneHour_IsTwelveFiveMinuteBuckets_EdgesOnTheClock()
    {
        var runs = new[]
        {
            Run("1-C", "done", 5, "m", new DateTimeOffset(2026, 11, 22, 12, 10, 0, TimeSpan.Zero)), // the last bucket's first instant
            Run("2-C", "done", 5, "m", new DateTimeOffset(2026, 11, 22, 12, 9, 59, TimeSpan.Zero)), // the one before it
            Run("3-C", "done", 5, "m", new DateTimeOffset(2026, 11, 22, 11, 15, 0, TimeSpan.Zero)), // the first bucket's first instant
            Run("4-C", "done", 5, "m", new DateTimeOffset(2026, 11, 22, 11, 14, 59, TimeSpan.Zero)), // just before the window
        };

        var buckets = Trends.Range(runs, Now, CardStats.RangeOf("1h")!, null, TimeZoneInfo.Utc);

        Assert.Equal(12, buckets.Count);
        Assert.Equal(new TimeOnly(11, 15), buckets[0].Time);
        Assert.Equal("11:15a", buckets[0].Label);
        Assert.Equal(new TimeOnly(12, 10), buckets[11].Time);
        Assert.Equal(1, buckets[0].Runs);
        Assert.Equal(1, buckets[10].Runs);
        Assert.Equal(1, buckets[11].Runs);
        Assert.Equal(3, buckets.Sum(b => b.Runs));
    }

    [Fact]
    public void Range_ThreeDays_IsTwelveSixHourBuckets_AndTwoWeeksAreFourteenDays()
    {
        var runs = new[]
        {
            Run("1-C", "done", 5, "m", new DateTimeOffset(2026, 11, 22, 1, 0, 0, TimeSpan.Zero)),
            Run("2-C", "review", 5, "m", new DateTimeOffset(2026, 11, 20, 23, 0, 0, TimeSpan.Zero)),
            Run("3-C", "error", 5, "m", new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero)),
        };

        var three = Trends.Range(runs, Now, CardStats.RangeOf("3d")!, null, TimeZoneInfo.Utc);
        Assert.Equal(12, three.Count);
        Assert.Equal(new DateOnly(2026, 11, 19), three[0].Date);
        Assert.Equal("6p", three[0].Label);
        Assert.Equal("11-20", three[1].Label); // a bucket at midnight carries the date
        Assert.Equal("6a", three[2].Label);
        Assert.Equal(1, three[4].CardsDone); // 11-20 18:00 holds the 23:00 run
        Assert.Equal(1, three[9].CardsDone); // 11-22 00:00 holds the 01:00 run
        Assert.Equal(2, three.Sum(b => b.CardsDone));

        var two = Trends.Range(runs, Now, CardStats.RangeOf("2w")!, null, TimeZoneInfo.Utc);
        Assert.Equal(14, two.Count);
        Assert.Equal(Trends.Daily(runs, Now, 14, null, TimeZoneInfo.Utc).Select(d => (d.Date, d.Runs)), two.Select(d => (d.Date, d.Runs)));
        Assert.Equal(new DateOnly(2026, 11, 9), two[0].Date);
        Assert.Equal(1, two[0].Runs);
    }
}
