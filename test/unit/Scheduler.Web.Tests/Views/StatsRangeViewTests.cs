// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Core.Accounting;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1095-C: one time-range dropdown drives the pool page's runs summary and its Trends chart.</summary>
public class StatsRangeViewTests
{
    private static TrendDay Bucket(int day, int hour, int cards) => new()
    {
        Date = new DateOnly(2026, 11, day),
        Time = new TimeOnly(hour, 0),
        Label = Trends.HourLabel(new DateTime(2026, 11, day, hour, 0, 0)),
        Segments = cards == 0 ? [] : [new TrendSegment { Model = "opus", Label = "opus", CardsDone = cards, Runs = cards }],
    };

    private static PageSnapshot Project(string name, int cards)
    {
        var ranges = CardStats.Ranges.Select(r => new PageStatsRange
        {
            Key = r.Key,
            Cards = cards,
            Text = $"Last {r.Label} ({cards * 10} min): {cards} cards ({cards} runs): done {cards}, 0 in review, 0 waiting on questions, 0 open; 0.5 cards/hr",
            Trend = [Bucket(20, 10, cards), Bucket(20, 11, cards)],
        }).ToList();
        return new PageSnapshot
        {
            Built = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero),
            State = new PageStateView { State = Scheduler.Core.Contracts.SchedulerState.Running, Text = "running" },
            Project = name,
            Stats = new PageStats { Cards = cards, Text = ranges[4].Text, Ranges = ranges },
            Trends = [Bucket(20, 0, cards), Bucket(21, 0, cards)],
        };
    }

    private static string Render(params PageSnapshot[] projects) =>
        new CombinedPageRenderer(theme: "slate").Render(projects, "slate", remote: false, controlsAllowed: true);

    [Fact]
    public void Page_HasOneSelect_WithTheEightRanges_24hChosen()
    {
        var html = Render(Project("A", 2), Project("B", 3));

        Assert.Single(Regex.Matches(html, "<select id='rangesel'"));
        var options = Regex.Matches(html, "<option value='([^']+)'( selected)?>([^<]+)</option>")
            .Where(m => CardStats.Ranges.Any(r => r.Key == m.Groups[1].Value && r.Name == m.Groups[3].Value))
            .ToList();
        Assert.Equal(8, options.Count);
        Assert.Equal(new[] { "24h" }, options.Where(m => m.Groups[2].Success).Select(m => m.Groups[1].Value).ToArray());
        Assert.Contains("<option value='1h'>1 hour</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value='2w'>2 weeks</option>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_HoldsEightSummaries_PerProject_AndSummedAcrossProjects()
    {
        var html = Render(Project("A", 2), Project("B", 3));

        foreach (var range in CardStats.Ranges)
        {
            var hid = range.Key == CardStats.DefaultRangeKey ? "" : " hidden"; // 1098-C: non-default ranges render hidden.
            Assert.Contains($"<div class='rng' data-range='{range.Key}'{hid}><div class='stats projonly' data-project='A'", html, StringComparison.Ordinal);
            Assert.Contains($"Last {range.Label} (20 min): 2 cards", html, StringComparison.Ordinal);
            Assert.Contains($"Last {range.Label} (50 min): 5 cards (5 runs): done 5", html, StringComparison.Ordinal); // A + B summed
        }

        // The rate of a sum is over the range's own hours: 5 done in 6 h.
        Assert.Contains("Last 6 h (50 min): 5 cards (5 runs): done 5, 0 in review, 0 waiting on questions, 0 open; 0.83 cards/hr", html, StringComparison.Ordinal);
        Assert.Contains("Last 1 week (50 min): 5 cards (5 runs): done 5, 0 in review, 0 waiting on questions, 0 open; 0.03 cards/hr", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_HidesEveryRangeButTheChosen_24hUntilTheScriptSetsAnother()
    {
        var html = Render(Project("A", 2), Project("B", 3));

        Assert.Contains("body:not([data-range]) .rng:not([data-range='24h']) { display: none; }", html, StringComparison.Ordinal);
        foreach (var range in CardStats.Ranges)
        {
            Assert.Contains($"body[data-range='{range.Key}'] .rng:not([data-range='{range.Key}']) {{ display: none; }}", html, StringComparison.Ordinal);
        }

        // The choice is remembered in the browser, as the project's is.
        Assert.Contains("localStorage.setItem(rangeKey, e.target.value)", html, StringComparison.Ordinal);
        Assert.Contains("e.target.id !== 'rangesel'", html, StringComparison.Ordinal);
        Assert.Contains("applyRange();", html, StringComparison.Ordinal);
    }

    // 1098-C (the developer, 2026-11-22): before the card the page hid the other ranges only through
    // the body-attribute style block; on a deployed build whose style did not apply, all eight showed
    // at once. The wrappers other than the default now come rendered hidden, so the server's own
    // markup is enough: exactly one range is visible with no script and no style at all.
    [Fact]
    public void Page_WithoutScriptOrStyle_ShowsTheDefaultRange_Alone()
    {
        var html = Render(Project("A", 2), Project("B", 3));

        // Eight stats wrappers and eight Trends wrappers; every one but the 24 h pair renders hidden.
        var wrappers = Regex.Matches(html, "<div class='rng' data-range='([^']+)'( hidden)?>");
        Assert.Equal(16, wrappers.Count);
        Assert.Equal(new[] { "24h", "24h" }, wrappers.Where(m => !m.Groups[2].Success).Select(m => m.Groups[1].Value).ToArray());
        Assert.All(wrappers, m => Assert.Equal(m.Groups[1].Value == CardStats.DefaultRangeKey, !m.Groups[2].Success));

        // The Trends heading's eight "cards finished, last ..." spans toggle with them.
        var spans = Regex.Matches(html, "<span class='mute rng' data-range='([^']+)'( hidden)?>");
        Assert.Equal(8, spans.Count);
        Assert.Equal(new[] { "24h" }, spans.Where(m => !m.Groups[2].Success).Select(m => m.Groups[1].Value).ToArray());

        // The style block still backs the script up: it hides by body attribute as before.
        Assert.Contains("body:not([data-range]) .rng:not([data-range='24h']) { display: none; }", html, StringComparison.Ordinal);
    }

    // 1098-C: #rangesel's handler and the remembered value clear hidden on the chosen range's
    // wrappers and set it on the rest, so the toggle no longer rests on the style block alone.
    [Fact]
    public void Script_ShowTheChosenRange_HideTheRest_ByTogglingHidden()
    {
        var html = Render(Project("A", 2), Project("B", 3));

        Assert.Contains("var rngs = document.querySelectorAll('.rng');", html, StringComparison.Ordinal);
        Assert.Contains("if (rngs[t].getAttribute('data-range') === v) rngs[t].removeAttribute('hidden'); else rngs[t].setAttribute('hidden', '');", html, StringComparison.Ordinal);

        // The toggle sits in applyRange, after the body attribute is set, so every caller
        // (DOMContentLoaded, the change handler, and each desk refresh) re-hides to the choice.
        var atSet = html.IndexOf("document.body.setAttribute('data-range', v);", StringComparison.Ordinal);
        var atToggle = html.IndexOf("removeAttribute('hidden')", StringComparison.Ordinal);
        Assert.True(atSet >= 0 && atToggle > atSet, "the hidden toggle must run right after applyRange sets the body attribute");
    }

    // 1098-C (the developer, 2026-11-22, sent back from review): the page the live feed rebuilds
    // replaces the desk whole and never the head, so a page opened before a deploy kept the OLD head
    // script - the one that sets the body attribute but never clears the hidden attribute - against
    // the NEW desk markup, and every range but the default came out blank. The range rules therefore
    // ride inside the desk, where the refresh brings the current ones with the current markup.
    [Fact]
    public void Page_TheRangeRulesRideInsideTheDesk_SoAStaleHeadScriptStillShowsTheChosenRange()
    {
        var html = Render(Project("A", 2), Project("B", 3));
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        var desk = html[html.IndexOf("id='desk'", StringComparison.Ordinal)..];

        // The head carries no range rule: it is the part a page keeps across a deploy. (Its script
        // still names the wrappers, which is the other half of the toggle.)
        Assert.DoesNotContain(".rng:not(", head, StringComparison.Ordinal);
        Assert.DoesNotContain(".rng[hidden]", head, StringComparison.Ordinal);

        // The desk carries all of them, in the order that makes them work: first the page without a
        // script (only the default shows, as the hidden markup alone also gives), then the rule that
        // lets go of the server's hidden attribute once a script has marked the body with its choice,
        // then the per-range rules that hide everything else. A stale script sets the body attribute
        // and stops there, and the chosen range shows anyway.
        Assert.Contains("<style data-range-rules>", desk, StringComparison.Ordinal);
        var keepDefault = "body:not([data-range]) .rng:not([data-range='24h']) { display: none; }";
        var letGo = "body[data-range] .rng[hidden] { display: block; }";
        Assert.Contains(keepDefault, desk, StringComparison.Ordinal);
        Assert.Contains(letGo, desk, StringComparison.Ordinal);
        Assert.True(desk.IndexOf(keepDefault, StringComparison.Ordinal) < desk.IndexOf(letGo, StringComparison.Ordinal), "the no-script rule must precede the let-go rule");
        foreach (var range in CardStats.Ranges)
        {
            var hide = $"body[data-range='{range.Key}'] .rng:not([data-range='{range.Key}']) {{ display: none; }}";
            Assert.Contains(hide, desk, StringComparison.Ordinal);
            // Equal specificity with the let-go rule, so it must come after it to win on the rest.
            Assert.True(desk.IndexOf(letGo, StringComparison.Ordinal) < desk.IndexOf(hide, StringComparison.Ordinal), $"the rule for {range.Key} must follow the let-go rule");
        }
    }

    [Fact]
    public void Trends_FollowTheSameDropdown_OneChartPerRange_DrawnFromTheirOwnBuckets()
    {
        var html = Render(Project("A", 2), Project("B", 3));

        var trends = html[html.IndexOf("id='w-trends'", StringComparison.Ordinal)..];
        foreach (var range in CardStats.Ranges)
        {
            var hid = range.Key == CardStats.DefaultRangeKey ? "" : " hidden"; // 1098-C
            Assert.Contains($"<div class='rng' data-range='{range.Key}'{hid}><div class='trendproj allonly'>", trends, StringComparison.Ordinal);
            Assert.Contains($"cards finished, last {range.Label}</span>", html, StringComparison.Ordinal);
        }

        // 8 ranges x (All projects + two projects), each drawn from that range's own buckets: times on the axis.
        Assert.Equal(24, Regex.Matches(trends, "<svg class='trendchart'").Count);
        Assert.Contains(">10a</text>", trends, StringComparison.Ordinal);
        Assert.Contains("per period over the last 2 periods", trends, StringComparison.Ordinal);
    }

    [Fact]
    public void Trends_SummedAcrossProjects_MatchBucketsByDateAndTime()
    {
        var sums = CombinedPageRenderer.SummedTrends(new[]
        {
            (IReadOnlyList<TrendDay>)[Bucket(20, 10, 1), Bucket(20, 11, 2)],
            [Bucket(20, 10, 4)],
        });

        Assert.Equal(2, sums.Count);
        Assert.Equal(5, sums[0].CardsDone);
        Assert.Equal(2, sums[1].CardsDone);
    }
}
