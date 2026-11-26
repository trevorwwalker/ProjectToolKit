// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Core.Accounting;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// 1327-C (developer, 2026-11-24: "starting from new project that had no completed cards ... the trends window
/// didn't show any graph when seven cards had completed over a 2.5 hour period"): a project has its Trends chart
/// as soon as it has one counted run in the last 14 days, and a range with none shows a muted line, not bars.
/// </summary>
public sealed class TrendsFirstDayTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 20, 0, 0, TimeSpan.Zero);

    private static RunStat Run(string card, int minutesAgo) =>
        new() { Finished = Now.AddMinutes(-minutesAgo), Result = "done", Minutes = 20, Model = "opus", Card = card };

    // Seven cards over 2.5 hours, the newest 70 minutes ago, so the last hour has none.
    private static RunStat[] SevenToday() =>
        [.. new[] { 70, 85, 100, 115, 130, 145, 150 }.Select((ago, i) => Run($"{100 + i}-A", ago))];

    private static PageSnapshot Project(string name, params RunStat[] runs)
    {
        var models = new[] { new ModelEntry { Key = "opus", Short = "Opus" } };
        return ViewsFixture.Full() with
        {
            Project = name,
            Trends = Trends.HasTrend(Trends.Daily(runs, Now, models: models, zone: TimeZoneInfo.Utc))
                ? Trends.Daily(runs, Now, models: models, zone: TimeZoneInfo.Utc)
                : [],
            Stats = new PageStats
            {
                Cards = runs.Length,
                Minutes = 0,
                Text = "",
                Ranges =
                [
                    .. CardStats.Ranges.Select(range => new PageStatsRange
                    {
                        Key = range.Key,
                        Cards = 0,
                        Minutes = 0,
                        Text = "",
                        Trend = Trends.Range(runs, Now, range, models, TimeZoneInfo.Utc),
                    }),
                ],
            },
        };
    }

    private static string Render(params PageSnapshot[] projects) =>
        new CombinedPageRenderer().Render(projects, "", remote: false, controlsAllowed: true);

    private static string Trends_(string html)
    {
        var at = html.IndexOf("id='w-trends'", StringComparison.Ordinal);
        if (at < 0)
        {
            return "";
        }

        return html[at..html.IndexOf("</section>", at, StringComparison.Ordinal)];
    }

    // One range's wrapper.
    private static string RangeOf(string window, string key)
    {
        var start = window.IndexOf($"<div class='rng' data-range='{key}'", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no range {key}");
        var next = window.IndexOf("<div class='rng'", start + 10, StringComparison.Ordinal);
        return next < 0 ? window[start..] : window[start..next];
    }

    private static int Bars(string html) => System.Text.RegularExpressions.Regex.Matches(html, "<rect class='tbar'").Count;

    [Fact]
    public void FirstDayProject_HasItsChart_WithBarsWhereCardsFinished_AndMutedLineWhereNone()
    {
        var window = Trends_(Render(Project("Alpha", SevenToday())));

        Assert.NotEqual("", window);
        foreach (var key in new[] { "3h", "6h", "24h" })
        {
            Assert.True(Bars(RangeOf(window, key)) > 0, $"bars in {key}");
            Assert.DoesNotContain("nothing finished", RangeOf(window, key), StringComparison.Ordinal);
        }

        var hour = RangeOf(window, "1h");
        Assert.Equal(0, Bars(hour));
        Assert.Contains("nothing finished in the last 1 hour", hour, StringComparison.Ordinal);
    }

    [Fact]
    public void NewProjectBesideAnOldOne_HasItsOwnChart_AndIsInTheAllProjectsSum()
    {
        var old = Project("Old", Run("001-A", 60 * 24 * 3), Run("002-A", 60 * 24 * 2));
        var fresh = Project("Fresh", SevenToday());
        var window = Trends_(Render(old, fresh));

        var threeHours = RangeOf(window, "3h");
        Assert.Contains("data-project='Fresh'", threeHours, StringComparison.Ordinal);
        Assert.Contains("trendproj allonly", threeHours, StringComparison.Ordinal);
        var fromAll = threeHours[threeHours.IndexOf("trendproj allonly", StringComparison.Ordinal)..threeHours.IndexOf("data-project='Old'", StringComparison.Ordinal)];
        Assert.True(Bars(fromAll) > 0, "the All projects chart holds the new project's bars");
    }

    [Fact]
    public void ProjectWithNoRunIn14Days_HasNoChart_AndNoProjectWithData_NoWindow()
    {
        var quiet = Project("Quiet", Run("001-A", 60 * 24 * 20));
        Assert.Equal("", Trends_(Render(quiet)));

        var window = Trends_(Render(quiet, Project("Fresh", SevenToday())));
        Assert.DoesNotContain("data-project='Quiet'", window, StringComparison.Ordinal);
        Assert.Contains("data-project='Fresh'", window, StringComparison.Ordinal);
    }
}

/// <summary>1488-C: the time labels under the bars are compact, never touch, and show the date where the day changes.</summary>
public sealed class TrendAxisLabelTests
{
    private static List<TrendDay> Hours(DateTime first, int count, int minutes = 60)
    {
        var days = new List<TrendDay>();
        for (var i = 0; i < count; i++)
        {
            var start = first.AddMinutes(i * minutes);
            days.Add(new TrendDay
            {
                Date = DateOnly.FromDateTime(start),
                Time = TimeOnly.FromDateTime(start),
                Label = Trends.HourLabel(start),
                Segments = [new TrendSegment { Model = "opus", Label = "Opus", CardsDone = 1, Runs = 1 }],
            });
        }

        return days;
    }

    [Theory]
    [InlineData(0, 0, "12a")]
    [InlineData(7, 0, "7a")]
    [InlineData(12, 0, "12p")]
    [InlineData(13, 0, "1p")]
    [InlineData(23, 0, "11p")]
    [InlineData(7, 30, "7:30a")]
    public void HourLabel_IsCompact(int hour, int minute, string expected) =>
        Assert.Equal(expected, Trends.HourLabel(new DateTime(2026, 11, 26, hour, minute, 0)));

    [Fact]
    public void TwentyFourHourlyBars_ShowNoTwoTouchingLabels_AndTheDateOnce()
    {
        var html = TrendChart.Html(Hours(new DateTime(2026, 11, 26, 11, 0, 0), 24));
        var labels = Regex.Matches(html, "<text class='tday' x='([0-9.]+)'[^>]*>([^<]+)</text>");
        Assert.NotEmpty(labels);
        var xs = labels.Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        for (var i = 1; i < xs.Length; i++)
        {
            Assert.True(xs[i] - xs[i - 1] >= labels[i].Groups[2].Value.Length * 6.6 + 6 - 0.01, "labels touch: " + labels[i].Groups[2].Value);
        }

        Assert.Equal("11a", labels[0].Groups[2].Value);
        Assert.Equal(24, labels.Count + Regex.Matches(html, "<line class='ttick'").Count);
        Assert.Single(Regex.Matches(html, "tdate'[^>]*>11-27</text>"));
        Assert.DoesNotContain(":00", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ThirtyMinuteBars_KeepWholeHours_AndTheFirstBar()
    {
        var html = TrendChart.Html(Hours(new DateTime(2026, 11, 26, 7, 0, 0), 48, 30));
        var shown = Regex.Matches(html, "<text class='tday' [^>]*>([^<]+)</text>").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal("7a", shown[0]);
        Assert.All(shown, label => Assert.DoesNotContain(":30", label, StringComparison.Ordinal));
    }

    [Fact]
    public void DateLabels_AreUnchanged()
    {
        var days = new List<TrendDay>();
        for (var i = 0; i < 14; i++)
        {
            var d = new DateOnly(2026, 11, 5).AddDays(i);
            days.Add(new TrendDay { Date = d, Label = d.ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture), Segments = [] });
        }

        var html = TrendChart.Html(days);
        Assert.Equal(14, Regex.Matches(html, "<text class='tday").Count);
        Assert.Contains(">11-18</text>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("tdate", html, StringComparison.Ordinal);
    }
}

