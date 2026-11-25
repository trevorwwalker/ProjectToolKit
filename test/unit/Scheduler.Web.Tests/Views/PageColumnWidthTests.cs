// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 958-C (the developer, 2026-11-19: "the right got dramatically wider, to the loss of the
/// left"). Two things held the right column open, and both are answered here. The split of the two
/// columns was a fixed 1600 px cap against a <c>1fr</c> track, so at 60% zoom - a 2542 CSS px
/// viewport - the left stopped growing at 1600 px and the right took the rest; the tracks now share
/// the width in fixed proportions, so the page looks the same at any zoom. And a grid track is floored
/// by the min-content width of what sits in it, so one unbreakable card name, queue note, worker-type
/// name or event line kept the side column wide; any desk text may break anywhere, and nothing inside
/// the side column can hold its track open. Long text wraps and stays on show - it is never clipped
/// into a tooltip.
/// </summary>
public sealed class PageColumnWidthTests
{
    private const string Medium = "@media (min-width: 1400px)";

    // Text with no space, hyphen or slash in it: the browser's only way to fit it is to break it.
    private static string Unbreakable(string lead) => lead + new string('u', 160);

    private static string Render(PageSnapshot snapshot) =>
        new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);

    // The queue item and the event row the page has to hold: a long unbroken card name, a long
    // unbroken busy note, a long unbroken worker-type name and an event line with a whole path in it.
    private static PageSnapshot LongTexts() => ViewsFixture.Plain() with
    {
        Queue =
        [
            new PageQueueItem
            {
                Name = "958-C-" + new string('u', 160) + ".md",
                Title = "the right column keeps its width",
                Note = Unbreakable("nextupwaitingforOpus1of1running530D"),
                Log = @"C:\Users\trevo\AppData\Local\ProjectToolkit\logs\" + new string('l', 90) + ".log",
            },
        ],
        Events =
        [
            // A finished run: its row carries the worker type, the card, the Result and the log.
            new PageEvent
            {
                Time = ViewsFixture.Built,
                Text = "slot 3  958-C done, 5 min",
                Card = "958-C-" + new string('u', 160) + ".md",
                Result = Unbreakable("donewithajournallinethatisverylong"),
                Type = Unbreakable("premiumfrontierworker"),
                Minutes = 5,
                Log = @"C:\Users\trevo\AppData\Local\ProjectToolkit\logs\" + new string('l', 90) + ".log",
            },
            // An event that is not a run: its own line of text, a whole path with no break in it.
            new PageEvent
            {
                Time = ViewsFixture.Built,
                Text = @"resumed C:\Users\trevo\AppData\Local\ProjectToolkit\workspaces\slot3\" + new string('w', 80) + " waiting",
            },
        ],
    };

    // Where the side column starts in the rendered page; everything after it is the right column.
    private static int SideColumnAt(string html)
    {
        var at = html.IndexOf("id='col-side'", StringComparison.Ordinal);
        Assert.True(at >= 0, "the page has no side column");
        return at;
    }

    // 958-C (the developer's reply, 2026-11-19): the split is proportional, so it looks the same at
    // any zoom - 60 parts left, 40 right with the 440 px floor, and no pixel cap on the left. The
    // narrow-window fallback is unchanged: one column below 1400 px, two from 1400 px.
    [Fact]
    public void CommonCss_SideTrack_SplitsProportionallyWithNoPixelCap()
    {
        var medium = MediaBlock(PageThemes.CommonCss, Medium);
        Assert.Contains("grid-template-columns: minmax(0, 60fr) minmax(440px, 40fr)", medium, StringComparison.Ordinal);
        var desk = Regex.Match(medium, @"\.desk\s*\{([^}]*)\}").Groups[1].Value;
        Assert.DoesNotContain("1600px", desk, StringComparison.Ordinal);
        Assert.DoesNotContain("1fr", desk, StringComparison.Ordinal);
        Assert.Contains(Medium, PageThemes.CommonCss, StringComparison.Ordinal);
        var outside = PageThemes.CommonCss.Replace(medium, "", StringComparison.Ordinal);
        Assert.DoesNotContain("grid-template-columns", outside, StringComparison.Ordinal);
    }

    // 958-C: the side column's own children cannot hold the track open either - each is allowed to
    // shrink to nothing, so the widest thing in it never sizes the column.
    [Fact]
    public void CommonCss_SideColumnChildren_MayShrinkToNothing()
    {
        var medium = MediaBlock(PageThemes.CommonCss, Medium);
        foreach (var selector in new[] { "#col-side > .col", "#col-side .win", "#col-side .inner", "#col-side table" })
        {
            Assert.Contains(selector, medium, StringComparison.Ordinal);
        }

        foreach (Match rule in Regex.Matches(medium, @"([^{}]+)\{([^}]*)\}"))
        {
            if (!rule.Groups[1].Value.Contains("#col-side", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var part in rule.Groups[2].Value.Split(';'))
            {
                Assert.False(
                    part.Trim().StartsWith("min-width:", StringComparison.Ordinal) && !part.Contains("min-width: 0", StringComparison.Ordinal),
                    $"{rule.Groups[1].Value.Trim()} sets a min-width that is not 0: {part.Trim()}");
            }
        }
    }

    // 958-C: long text breaks anywhere rather than setting a width. The rule is on the desk, so it
    // is inherited by every cell, link, note and event line in every window, in both layouts.
    [Fact]
    public void CommonCss_LongText_BreaksAnywhere()
    {
        var css = PageThemes.CommonCss;
        var desk = Regex.Matches(css, @"\.desk\s*\{([^}]*)\}").Cast<Match>().Any(m => m.Groups[1].Value.Contains("overflow-wrap: anywhere", StringComparison.Ordinal));
        Assert.True(desk, "the common style lets no desk text set a column's width");
    }

    // 958-C: the rule that breaks long text must not be the one that hides it - the page keeps the
    // whole note and name on show, so nothing needs a tooltip to be read.
    [Fact]
    public void CommonCss_LongText_Wraps_DoesNotHideIt()
    {
        // It is outside the media query, so the stacked (narrow) layout wraps too.
        var outside = PageThemes.CommonCss.Replace(MediaBlock(PageThemes.CommonCss, Medium), "", StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", outside, StringComparison.Ordinal);
        var side = MediaBlock(PageThemes.CommonCss, Medium);
        Assert.DoesNotContain("text-overflow: ellipsis", side, StringComparison.Ordinal);
        Assert.DoesNotContain("white-space: nowrap", side, StringComparison.Ordinal);
    }

    // 958-C: a page built from today's data - a long unbroken busy note, card name, worker-type name
    // and event line - still shows all of it, inside the side column, under the wrapping style.
    [Fact]
    public void Page_LongNoteAndCardName_StayInSideColumnAndKeepTheirText()
    {
        var html = Render(LongTexts());
        var side = SideColumnAt(html);

        var queue = html.IndexOf("nextupwaitingforOpus1of1running530D", side, StringComparison.Ordinal);
        var card = html.IndexOf(new string('u', 160), side, StringComparison.Ordinal);
        var worker = html.IndexOf("premiumfrontierworker", side, StringComparison.Ordinal);
        var line = html.IndexOf("C:\\Users\\trevo\\AppData\\Local\\ProjectToolkit\\workspaces\\slot3\\", side, StringComparison.Ordinal);
        Assert.True(queue > side, "the busy note is not in the side column");
        Assert.True(card > side, "the long card name is not in the side column");
        Assert.True(worker > side, "the long worker-type name is not in the side column");
        Assert.True(line > side, "the long event line is not in the side column");

        // The wrapping style is in the page's head, before the body the browser lays out.
        var wrap = html.IndexOf("overflow-wrap: anywhere", StringComparison.Ordinal);
        var body = html.IndexOf("<body", StringComparison.Ordinal);
        Assert.True(wrap >= 0 && wrap < body, "the page must carry the wrapping style before its body");
    }

    // 958-C: the combined page shares the same common style, so it holds its columns the same way.
    [Fact]
    public void CombinedPage_CarriesTheSameRules()
    {
        var html = new CombinedPageRenderer().Render([LongTexts()], "", remote: false, controlsAllowed: true);
        var side = SideColumnAt(html);
        Assert.True(html.IndexOf("nextupwaitingforOpus1of1running530D", side, StringComparison.Ordinal) > side, "the note left the side column");
        Assert.Contains("overflow-wrap: anywhere", html, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 60fr) minmax(440px, 40fr)", html, StringComparison.Ordinal);
    }

    // The text of a media query's block, up to its closing brace.
    private static string MediaBlock(string css, string query)
    {
        var start = css.IndexOf(query, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no '{query}' in the common style");
        var open = css.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            depth += css[i] == '{' ? 1 : css[i] == '}' ? -1 : 0;
            if (depth == 0)
            {
                return css[start..(i + 1)];
            }
        }

        return css[start..];
    }
}
