// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Core.Accounting;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// 630-A (roadmap item 3): the Trends window in the right-hand column under Recent events - one
/// small daily chart from the accounting rows, days along the bottom, cards done as bars and runs
/// as a thin line, in plain inline SVG with the theme's own colours. It is hidden while the
/// accounting has less than one day of data to chart.
/// </summary>
public sealed class PageTrendsTests
{
    private static readonly DateTimeOffset Built = new(2026, 11, 18, 12, 30, 0, TimeSpan.Zero);

    private static RunStat Run(string card, string result, int minutes, string model, DateTimeOffset finished) =>
        new() { Finished = finished, Result = result, Minutes = minutes, Model = model, Card = card };

    // The window's days, from the same Core builder the host uses, under a fixed zone so the
    // dates the tests name do not depend on the machine running them.
    private static IReadOnlyList<TrendDay> Days(params RunStat[] runs) =>
        Trends.Daily(runs, Built, models: [new ModelEntry { Key = "opus", Short = "Opus 4.5" }], zone: TimeZoneInfo.Utc);

    private static RunStat[] Sample =>
    [
        Run("010-A", "done", 30, "opus", Built.AddHours(-1)),
        Run("020-B", "review", 20, "opus", Built.AddHours(-2)),
        Run("030-C", "done", 45, "gpt", Built.AddHours(-3)),
        Run("040-D", "done", 10, "gpt", Built.AddDays(-1)),
        Run("050-E", "done", 10, "opus", Built.AddDays(-1)),
        Run("060-F", "stopped", 5, "opus", Built.AddDays(-1)),
    ];

    private static PageSnapshot WithTrends(params RunStat[] runs) => ViewsFixture.Full() with { Trends = Days(runs) };

    private static string Render(PageSnapshot snapshot) =>
        new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);

    // The window's own markup.
    private static string WindowOf(string html, string id)
    {
        var at = html.IndexOf($"id='{id}'", StringComparison.Ordinal);
        Assert.True(at >= 0, $"no {id}");
        var start = html.LastIndexOf("<section class='win'", at, StringComparison.Ordinal);
        var end = html.IndexOf("</section>", at, StringComparison.Ordinal);
        return html[start..end];
    }

    // The window sits in the right-hand column under Recent events (590-C's layout).
    [Fact]
    public void Trends_Window_IsUnderRecentEvents()
    {
        var html = Render(WithTrends([.. Sample]));

        Assert.Contains("id='w-trends'", html, StringComparison.Ordinal);
        var events = html.IndexOf("id='w-events'", StringComparison.Ordinal);
        var trends = html.IndexOf("id='w-trends'", StringComparison.Ordinal);
        var colEvents = html.IndexOf("id='col-events'", StringComparison.Ordinal);
        Assert.True(colEvents < events && events < trends, "Trends must follow Recent events in the side column");
        Assert.True(trends < html.IndexOf("</body>", StringComparison.Ordinal), "Trends must be inside the desk");
    }

    // Days along the bottom, cards done as bars, runs as a thin line (roadmap item 3).
    [Fact]
    public void Trends_Chart_BarsLineAndDayLabels()
    {
        var win = WindowOf(Render(WithTrends([.. Sample])), "w-trends");

        Assert.Contains("<svg", win, StringComparison.Ordinal);
        Assert.Contains("</svg>", win, StringComparison.Ordinal);
        // Fourteen days, oldest to newest, whatever ran on them (963-C: the alternate ones carry an
        // extra class the page hides when the window is narrow, but the chart always draws all 14).
        Assert.Equal(14, Regex.Matches(win, "<text class='tday").Count);
        Assert.Contains("11-18", win, StringComparison.Ordinal);
        Assert.Contains("11-05", win, StringComparison.Ordinal);
        // One bar per model that finished something, and a line for the runs.
        Assert.Equal(4, Regex.Matches(win, "<rect class='tbar'").Count);
        Assert.Contains("<polyline class='truns'", win, StringComparison.Ordinal);
        // The legend names each worker type by its own name (963-C) - the model "opus" is what the
        // Opus type runs today - and keys the runs line (1052-C). The "up to n" figure 941-C took out
        // stays out: the axis's own number says it.
        Assert.Contains(">Opus</span>", win, StringComparison.Ordinal);
        Assert.Contains(">gpt</span>", win, StringComparison.Ordinal);
        Assert.DoesNotContain("Opus 4.5", win, StringComparison.Ordinal);
        Assert.Contains("<span class='tswatch truns' style='color: var(--text)'>runs</span>", win, StringComparison.Ordinal);
        Assert.DoesNotContain("up to", win, StringComparison.Ordinal);
        Assert.DoesNotContain("trunskey", win, StringComparison.Ordinal);
    }

    // The colours come from the theme, and the chart is as wide as the window it is drawn in
    // (963-C, the developer 2026-11-19: "the graph should more comfortably fit the space").
    [Fact]
    public void Trends_Chart_UsesThemeColours_AndScalesToTheWindow()
    {
        var win = WindowOf(Render(WithTrends([.. Sample])), "w-trends");

        var svg = Regex.Match(win, "<svg[^>]*>").Value;
        Assert.Contains("<svg", win, StringComparison.Ordinal);
        // viewBox + width 100%: the drawing takes the window's width, and its height is the
        // viewBox's ratio (about a third of the width), which the page's CSS clamps.
        var box = Regex.Match(svg, @"viewBox='0 0 (\d+) (\d+)'");
        Assert.True(box.Success, $"the svg must carry a viewBox: {svg}");
        var width = int.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture);
        var height = int.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture);
        Assert.True(width >= 600, $"the drawing box is drawn wide enough to scale up ({width})");
        Assert.True(width >= (height * 2) && width <= (height * 4), $"the box is about 3:1 ({width}x{height})");
        Assert.Contains("width='100%'", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("height='", svg, StringComparison.Ordinal);
        // The page clamps the height so a very narrow column and a very wide one both stay usable.
        Assert.Contains("min-height", PageThemes.CommonCss, StringComparison.Ordinal);
        Assert.Contains("max-height", PageThemes.CommonCss, StringComparison.Ordinal);
        // Each worker type gets a colour of the theme's, and so does the runs line.
        Assert.Contains("fill='var(--ok)'", win, StringComparison.Ordinal);
        Assert.Contains("fill='var(--warn)'", win, StringComparison.Ordinal);
        Assert.Contains("stroke='var(--text)'", win, StringComparison.Ordinal);
        Assert.DoesNotContain("fill='#", win, StringComparison.Ordinal);
        Assert.DoesNotContain("stroke='#", win, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", win, StringComparison.Ordinal);
    }

    // The day slots spread over the whole box, the bars fill most of their slot, and the count that
    // labels the axis sits beside it (963-C).
    [Fact]
    public void Trends_Chart_DaysSpreadAcrossTheWindow_AndTheAxisLabelSitsBesideIt()
    {
        var win = WindowOf(Render(WithTrends([.. Sample])), "w-trends");

        // The vertical axis gives the plot's left edge and top; the baseline its left and right.
        var axis = Regex.Match(win, "<line class='taxis' x1='([0-9.]+)' y1='([0-9.]+)' x2='([0-9.]+)' y2='([0-9.]+)'");
        Assert.True(axis.Success, "the chart draws its y-axis");
        var left = double.Parse(axis.Groups[1].Value, CultureInfo.InvariantCulture);
        var top = double.Parse(axis.Groups[2].Value, CultureInfo.InvariantCulture);
        Assert.Equal(left, double.Parse(axis.Groups[3].Value, CultureInfo.InvariantCulture), 3);
        var baseLine = Regex.Match(win, "<line class='tbase' x1='([0-9.]+)' y1='([0-9.]+)' x2='([0-9.]+)'");
        var right = double.Parse(baseLine.Groups[3].Value, CultureInfo.InvariantCulture);
        var baseline = double.Parse(baseLine.Groups[2].Value, CultureInfo.InvariantCulture);
        Assert.True(right - left > 500, $"the plot reaches across the box ({left}..{right})");

        var slot = (right - left) / 14;
        var bars = Regex.Matches(win, "<rect class='tbar' x='([0-9.]+)'[^>]*width='([0-9.]+)'")
            .Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          W: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();
        Assert.NotEmpty(bars);
        Assert.True(bars.All(b => b.W >= slot * 0.5), $"a bar takes most of its slot ({bars[0].W} of {slot:0.#})");
        // Every day of the window keeps a place along the bottom, so the first is at the plot's left
        // edge and the last at its right (963-C: the days used to crowd a strip in the middle).
        var days = Regex.Matches(win, "<text class='tday[^']*' x='([0-9.]+)'")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(14, days.Count);
        Assert.True(days[0] < left + slot, $"the first day sits at the plot's left ({days[0]} vs {left})");
        Assert.True(days[^1] > right - slot, $"the last day sits at the plot's right ({days[^1]} vs {right})");

        // The count that labels the axis is beside the axis's top, not across the box.
        var label = Regex.Match(win, "<text class='tmax' x='([0-9.]+)' y='([0-9.]+)'");
        var labelX = double.Parse(label.Groups[1].Value, CultureInfo.InvariantCulture);
        var labelY = double.Parse(label.Groups[2].Value, CultureInfo.InvariantCulture);
        Assert.True(labelX < left && (left - labelX) <= 20, $"the y-axis label is next to its axis ({labelX} vs {left})");
        Assert.True(Math.Abs(labelY - top) <= 12, $"the label is at the top of the axis ({labelY} vs {top})");
    }

    // A day the pool ran nothing on keeps its slot: the gap is the idle day showing (963-C).
    [Fact]
    public void Trends_Chart_AnIdleDayKeepsItsSlot()
    {
        var win = WindowOf(Render(WithTrends(
            Run("010-A", "done", 30, "opus", Built.AddHours(-1)),
            Run("011-A", "done", 20, "opus", Built.AddDays(-2)))), "w-trends");

        // Bars on two days only, and the eleven idle days between and before them still hold their
        // places along the bottom and their points on the runs line.
        Assert.Equal(2, Regex.Matches(win, "<rect class='tbar'").Count);
        Assert.Equal(14, Regex.Matches(win, "<text class='tday").Count);
        var points = Regex.Match(win, "<polyline class='truns'[^>]*points='([^']*)'").Groups[1].Value;
        var xs = points.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => double.Parse(p.Split(',')[0], CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(14, xs.Count);
        Assert.True(xs[^1] - xs[0] > 500, "the idle days keep their width");
        Assert.True(xs[1] - xs[0] > 20, "each day's slot is a share of the window");
    }

    // One legend entry and one colour per worker type (963-C): the runs of an era whose accounting
    // named the model, and of one that named the type, are the same workers.
    [Fact]
    public void Trends_Legend_OneEntryAndOneColourPerWorkerType()
    {
        var days = Trends.Daily(
            [
                Run("100-A", "done", 30, "claude-opus-5-5", Built.AddHours(-1)),
                Run("101-A", "done", 30, "Opus", Built.AddHours(-2)),
                Run("102-B", "done", 20, "claude-sonnet-5-5", Built.AddDays(-1)),
                Run("103-B", "done", 20, "Sonnet", Built.AddDays(-1)),
                Run("104-C", "done", 10, "spark", Built.AddDays(-2)),
                Run("105-C", "done", 10, "worker", Built.AddDays(-2)),
                Run("106-C", "done", 10, "default", Built.AddDays(-3)),
            ],
            Built,
            zone: TimeZoneInfo.Utc);
        var win = WindowOf(Render(ViewsFixture.Full() with { Trends = days }), "w-trends");

        // The fixture's types are worker (spark), Sonnet and Opus: seven model keys, three entries.
        var entries = Regex.Matches(win, "<span class='tswatch' style='color: ([^']+)'>([^<]*)</span>")
            .Select(m => (Colour: m.Groups[1].Value, Label: m.Groups[2].Value))
            .ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal(["Opus", "Sonnet", "worker"], entries.Select(e => e.Label).ToArray());
        Assert.True(entries.Select(e => e.Colour).Distinct().Count() == 3, "no two types share a colour");
        // The model names are gone from the legend; the bars use the same three colours.
        Assert.DoesNotContain("claude-opus-5-5", win, StringComparison.Ordinal);
        Assert.DoesNotContain("claude-sonnet-5-5", win, StringComparison.Ordinal);
        Assert.DoesNotContain(">default<", win, StringComparison.Ordinal);
        var fills = Regex.Matches(win, "<rect class='tbar'[^>]*fill='([^']+)'")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
        Assert.Equal(3, fills.Count);
    }

    // A model no current worker type runs is not folded into one: it keeps its own legend entry.
    [Fact]
    public void Trends_Legend_KeepsAModelNoTypeRuns()
    {
        var days = Trends.Daily(
            [
                Run("110-A", "done", 30, "claude-opus-5-5", Built.AddHours(-1)),
                Run("111-A", "done", 30, "qwen3-32b", Built.AddDays(-2)),
            ],
            Built,
            zone: TimeZoneInfo.Utc);
        var win = WindowOf(Render(ViewsFixture.Full() with { Trends = days }), "w-trends");

        var labels = Regex.Matches(win, "<span class='tswatch'[^>]*>([^<]*)</span>").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(["Opus", "qwen3-32b"], labels);
    }

    // A taller bar for the busier day, drawn from the same top.
    [Fact]
    public void Trends_Chart_BarHeightFollowsCardsDone()
    {
        var win = WindowOf(Render(WithTrends([.. Sample])), "w-trends");

        var heights = Regex.Matches(win, "<rect class='tbar'[^>]*height='([0-9.]+)'")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.NotEmpty(heights);
        Assert.True(heights.Max() > heights.Min(), "the busier day must have the taller bar");
        // Every bar starts at or above the baseline and none runs past the top of the plot; the plot
        // is what the axes draw, so the test reads its box from them (963-C).
        var top = double.Parse(Regex.Match(win, "<line class='taxis' x1='[0-9.]+' y1='([0-9.]+)'").Groups[1].Value, CultureInfo.InvariantCulture);
        var baseline = double.Parse(Regex.Match(win, "<line class='tbase' x1='[0-9.]+' y1='([0-9.]+)'").Groups[1].Value, CultureInfo.InvariantCulture);
        foreach (Match bar in Regex.Matches(win, "<rect class='tbar'[^>]*y='([0-9.]+)'[^>]*height='([0-9.]+)'"))
        {
            var y = double.Parse(bar.Groups[1].Value, CultureInfo.InvariantCulture);
            var height = double.Parse(bar.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.InRange(y, top - 0.5, baseline);
            Assert.InRange(y + height, top, baseline + 0.5);
        }
    }

    // A day whose runs finished nothing has no bar: the bars are cards done, not runs.
    [Fact]
    public void Trends_Chart_NoBarForADayThatFinishedNothing()
    {
        var win = WindowOf(Render(WithTrends(
            Run("010-A", "done", 30, "opus", Built.AddHours(-1)),
            Run("020-B", "done", 20, "opus", Built.AddHours(-2)),
            Run("030-C", "stopped", 5, "opus", Built.AddDays(-1)))), "w-trends");

        Assert.Single(Regex.Matches(win, "<rect class='tbar'"));
        // The run still shows, as a point on the line: the line has a point for every day.
        var points = Regex.Match(win, "<polyline class='truns'[^>]*points='([^']*)'").Groups[1].Value;
        Assert.Equal(14, points.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    // Hidden when there is less than one day of data: today's runs alone are not a trend.
    [Fact]
    public void Trends_Window_HiddenWhenThereIsLessThanOneDayOfData()
    {
        var todayOnly = Render(WithTrends(
            Run("010-A", "done", 30, "opus", Built.AddHours(-1)),
            Run("020-B", "done", 20, "opus", Built.AddHours(-2))));
        Assert.DoesNotContain("id='w-trends'", todayOnly, StringComparison.Ordinal);

        // Nothing at all in the accounting is hidden too, and so is a page that was given no days.
        Assert.DoesNotContain("id='w-trends'", Render(ViewsFixture.Full() with { Trends = [] }), StringComparison.Ordinal);
        var excluded = Render(WithTrends(
            Run("010-A", "done", 30, "opus", Built.AddHours(-1)),
            Run("020-B", "no log", 20, "opus", Built.AddDays(-1))));
        Assert.DoesNotContain("id='w-trends'", excluded, StringComparison.Ordinal);
    }

    // The pool's page: one chart per project, each hideable by the dropdown; 1004-C: and one chart of
    // every project summed, shown under All projects.
    [Fact]
    public void Trends_CombinedPage_OneChartPerProject()
    {
        var first = ViewsFixture.Full() with { Project = "Alpha", Trends = Days(Sample) };
        var second = ViewsFixture.Full() with
        {
            Project = "Beta",
            Trends = Days(
                Run("070-A", "done", 10, "opus", Built.AddDays(-1)),
                Run("071-B", "done", 10, "opus", Built.AddDays(-2))),
        };
        var html = new CombinedPageRenderer().Render([first, second], "", remote: false, controlsAllowed: true);

        var win = WindowOf(html, "w-trends");
        Assert.Equal(3, Regex.Matches(win, "<svg").Count);
        Assert.Contains("<div class='trendproj allonly'>", win, StringComparison.Ordinal);
        Assert.Contains("data-project='Alpha'", win, StringComparison.Ordinal);
        Assert.Contains("data-project='Beta'", win, StringComparison.Ordinal);
        Assert.Contains("data-view='trends'", html, StringComparison.Ordinal);
        Assert.True(
            html.IndexOf("id='w-events'", StringComparison.Ordinal) < html.IndexOf("id='w-trends'", StringComparison.Ordinal),
            "Trends follows Recent events on the pool page too");
    }

    // A pool where no project has a day before today shows no Trends window at all.
    [Fact]
    public void Trends_CombinedPage_HiddenWhenNoProjectHasData()
    {
        var thin = ViewsFixture.Full() with { Trends = Days(Run("010-A", "done", 10, "opus", Built.AddHours(-1))) };
        var empty = ViewsFixture.Full() with { Project = "Beta", Trends = [] };
        var html = new CombinedPageRenderer().Render([thin, empty], "", remote: false, controlsAllowed: true);

        Assert.DoesNotContain("id='w-trends'", html, StringComparison.Ordinal);
    }

    // ===== 1052-C: one scale for both series, and a legend that names every colour =====

    // The plot's box, read from the axes the chart draws.
    private static (double Top, double Baseline) BoxOf(string chart)
    {
        var top = double.Parse(Regex.Match(chart, "<line class='taxis' x1='[0-9.]+' y1='([0-9.]+)'").Groups[1].Value, CultureInfo.InvariantCulture);
        var baseline = double.Parse(Regex.Match(chart, "<line class='tbase' x1='[0-9.]+' y1='([0-9.]+)'").Groups[1].Value, CultureInfo.InvariantCulture);
        return (top, baseline);
    }

    // The number beside the top of the axis, which is what both series are measured against.
    private static string AxisTopOf(string chart) =>
        Regex.Match(chart, "<text class='tmax'[^>]*>([0-9.]+)</text>").Groups[1].Value;

    // One day of the window with the figures the chart is being asked about.
    private static TrendDay ADay(DateOnly date, int cards, int runs) => new()
    {
        Date = date,
        Segments = cards == 0 && runs == 0
            ? []
            : [new TrendSegment { Model = "opus", Label = "Opus", CardsDone = cards, Runs = runs }],
    };

    // A 14-day window ending 2026-11-18, with the days the test names filled in. `ago` is days back
    // from that last day, so 0 is the newest point of the chart.
    private static List<TrendDay> DaysWindow(params (int Ago, int Cards, int Runs)[] busy)
    {
        var last = new DateOnly(2026, 11, 18);
        var window = new List<TrendDay>(14);
        for (var at = 0; at < 14; at++)
        {
            var day = last.AddDays(-(13 - at));
            var hit = busy.FirstOrDefault(b => b.Ago == 13 - at);
            window.Add(ADay(day, hit.Cards, hit.Runs));
        }

        return window;
    }

    // The line's points, oldest first, as y.
    private static List<double> LinePointsOf(string chart) =>
        [.. Regex.Match(chart, "<polyline class='truns'[^>]*points='([^']*)'").Groups[1].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => double.Parse(p.Split(',')[1], CultureInfo.InvariantCulture))];

    // The bars, in the order drawn (day by day), each as (y, height).
    private static List<(double Y, double Height)> BarsOf(string chart) =>
        [.. Regex.Matches(chart, "<rect class='tbar'[^>]*y='([0-9.]+)'[^>]*height='([0-9.]+)'")
            .Select(m => (
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))];

    // The chart of one project (or the summed one), from its name down to the end of its svg.
    private static string ChartOf(string html, string marker)
    {
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the page has no {marker} chart");
        var end = html.IndexOf("</svg>", at, StringComparison.Ordinal);
        return html[at..(end + 6)];
    }

    // 1052-C (the developer): the runs line used to be scaled to its own maximum, so a day's height
    // meant one thing for the bars and another for the line. Both now take the axis's scale, so on a
    // day with 40 cards and 55 runs the line is above the bar and both are measured by the same top.
    [Fact]
    public void Trends_Chart_RunsAndBarsShareOneScale()
    {
        var chart = TrendChart.Html(DaysWindow((0, 20, 10), (1, 40, 55)));
        var (top, baseline) = BoxOf(chart);
        var plot = baseline - top;

        // The axis is labelled with the taller of the two maxima, not with the cards' maximum.
        Assert.Equal("55", AxisTopOf(chart));

        // Bars and points come in day order, oldest first: index 12 is 11-17 and index 13 is 11-18.
        var bars = BarsOf(chart);
        var line = LinePointsOf(chart);
        Assert.Equal(2, bars.Count);
        Assert.Equal(14, line.Count);

        // 11-17: 40 cards of the 55 the axis can show, and all 55 of its runs.
        Assert.Equal(40 / 55d, bars[0].Height / plot, 3);
        Assert.Equal(top, line[12], 1);
        // The line is above the bar of its own day, which is what the developer asked to see.
        Assert.True(line[12] < bars[0].Y, $"the runs point ({line[12]}) sits above the bar's top ({bars[0].Y})");

        // 11-18: 20 cards is 20/55 of the plot and 10 runs is 10/55 of it - the same measure.
        Assert.Equal(20 / 55d, bars[1].Height / plot, 3);
        Assert.Equal(10 / 55d, (baseline - line[13]) / plot, 3);
    }

    // The same rule the other way round: when the cards are the taller series, the axis says their
    // number and the line still reads against it.
    [Fact]
    public void Trends_Chart_TheAxisTopIsTheTallerOfCardsAndRuns()
    {
        var chart = TrendChart.Html(DaysWindow((0, 60, 5), (1, 4, 3)));
        var (top, baseline) = BoxOf(chart);

        Assert.Equal("60", AxisTopOf(chart));
        var bars = BarsOf(chart);
        Assert.Equal(top, baseline - bars[1].Height, 1); // the 60-card day fills the plot
        var line = LinePointsOf(chart);
        Assert.Equal(baseline - (5 / 60d * (baseline - top)), line[13], 1);
    }

    // 1052-C: every colour in the legend has a label. Accounting's rows from before the model column
    // (ACCT-4) carry neither a key nor a short name and drew a bare green swatch; they are the
    // "other" entry, and the runs line is keyed too.
    [Fact]
    public void Trends_Legend_EveryChipHasALabel()
    {
        var last = new DateOnly(2026, 11, 18);
        List<TrendDay> days =
        [
            new() { Date = last.AddDays(-1), Segments = [] },
            new()
            {
                Date = last,
                Segments =
                [
                    // What accounting's rows from before the model column (ACCT-4) look like: no key
                    // and no short name. And a row that named its model but has no short name.
                    new TrendSegment { Model = "", Label = "", CardsDone = 3, Runs = 4 },
                    new TrendSegment { Model = "old-llama", Label = "", CardsDone = 1, Runs = 2 },
                    new TrendSegment { Model = "opus", Label = "Opus", CardsDone = 2, Runs = 6 },
                ],
            },
        ];
        var chart = TrendChart.Html(days, ViewsFixture.Full().WorkerTypes);

        var chips = Regex.Matches(chart, "<span class='tswatch[^']*' style='color: ([^']+)'>([^<]*)</span>")
            .Select(m => (Colour: m.Groups[1].Value, Label: m.Groups[2].Value))
            .ToList();
        Assert.NotEmpty(chips);
        Assert.All(chips, chip => Assert.False(string.IsNullOrWhiteSpace(chip.Label), $"a swatch had no label: {chip.Colour}"));
        Assert.Contains("other", chips.Select(c => c.Label), StringComparer.Ordinal);
        Assert.Contains("old-llama", chips.Select(c => c.Label), StringComparer.Ordinal);
        Assert.Contains("runs", chips.Select(c => c.Label), StringComparer.Ordinal);
        // No two chips say the same thing, and the runs chip is the colour of the line it keys.
        Assert.Equal(chips.Count, chips.Select(c => c.Label).Distinct(StringComparer.Ordinal).Count());
        var runs = chips.Single(c => c.Label == "runs");
        Assert.Contains($"<polyline class='truns' fill='none' stroke='{runs.Colour}'", chart, StringComparison.Ordinal);
    }

    // 1052-C: under All projects the runs are added up over every project, as the cards are (1004-C),
    // so the summed chart's scale comes from the pool's runs, not one project's.
    [Fact]
    public void Trends_CombinedPage_AllProjectsSumsRuns()
    {
        var alpha = ViewsFixture.Full() with
        {
            Project = "Alpha",
            Trends = Days(
                Run("130-A", "done", 10, "opus", Built.AddHours(-2)),
                Run("131-A", "done", 10, "opus", Built.AddHours(-3)),
                Run("132-A", "done", 10, "opus", Built.AddHours(-4)),
                Run("133-A", "stopped", 10, "opus", Built.AddHours(-5)),
                Run("134-A", "stopped", 10, "opus", Built.AddHours(-6)),
                Run("135-B", "done", 10, "opus", Built.AddDays(-2))),
        };
        var beta = ViewsFixture.Full() with
        {
            Project = "Beta",
            Trends = Days(
                Run("140-A", "stopped", 10, "opus", Built.AddHours(-2)),
                Run("141-A", "stopped", 10, "opus", Built.AddHours(-3)),
                Run("142-A", "stopped", 10, "opus", Built.AddHours(-4)),
                Run("143-A", "stopped", 10, "opus", Built.AddHours(-5)),
                Run("144-A", "done", 10, "opus", Built.AddDays(-2))),
        };

        Assert.Equal(9, CombinedPageRenderer.SummedTrends([alpha, beta]).Max(day => day.Runs));

        var trends = WindowOf(new CombinedPageRenderer().Render([alpha, beta], "", remote: false, controlsAllowed: true), "w-trends");
        Assert.Equal("9", AxisTopOf(ChartOf(trends, "All projects</div>")));
        Assert.Equal("5", AxisTopOf(ChartOf(trends, "data-project='Alpha'")));
        Assert.Equal("4", AxisTopOf(ChartOf(trends, "data-project='Beta'")));

        // The summed chart's busiest day reaches the top of the axis with its line, and its bars are
        // the same cards measured against the pool's runs: 3 of 9 on 11-18.
        var all = ChartOf(trends, "All projects</div>");
        var (top, baseline) = BoxOf(all);
        Assert.Equal(top, LinePointsOf(all)[13], 1);
        Assert.Equal(3 / 9d, BarsOf(all)[1].Height / (baseline - top), 3);
    }
}
