using System.Text.RegularExpressions;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// Card 968-C (the developer, 2026-11-19, screenshot of the Queue): since 958-C let any desk text
/// break anywhere, the Queue's short columns squash - "LOG" and "LANE" break one letter per line,
/// "log" becomes "lo/g", a dependency id breaks at its hyphen, and a card name breaks at every
/// hyphen while the State column sits with room to spare. A column is now either short, in which
/// case its field stays on one line and takes only the width its content needs, or the one column
/// that wraps. The Card column keeps the name on a line and ellipsises it, with the whole name in
/// the tooltip, rather than breaking it.
/// </summary>
public sealed class PageTableColumnTests
{
    private const string Medium = "@media (min-width: 1400px)";

    private static string Render(PageSnapshot snapshot) =>
        new PageRenderer().Render(snapshot, remote: false, controlsAllowed: true);

    // The queue item and the events the tables have to lay out.
    private static PageSnapshot Rows() => ViewsFixture.Plain() with
    {
        Queue =
        [
            new PageQueueItem
            {
                Name = "957-B-instant-page-actions.md",
                Title = "instant page actions",
                Note = "next up",
                Depends = ["956-B", "955-A"],
                Log = @"C:\Users\trevo\AppData\Local\ProjectToolkit\logs\957-B-20261119-101529.log",
            },
        ],
        Events =
        [
            new PageEvent
            {
                Time = ViewsFixture.Built,
                Text = "slot 3  957-B done, 5 min",
                Card = "957-B-instant-page-actions",
                Result = "done",
                Type = "worker",
                Minutes = 5,
                Log = @"C:\Users\trevo\AppData\Local\ProjectToolkit\logs\957-B-20261119-101529.log",
            },
            new PageEvent
            {
                Time = ViewsFixture.Built,
                Text = @"resumed C:\Users\trevo\AppData\Local\ProjectToolkit\workspaces\slot3\968-C.md waiting",
            },
        ],
    };

    // The common style without its comments, so a rule's selector is only its selectors.
    private static string Css() => Regex.Replace(PageThemes.CommonCss, @"/\*.*?\*/", "", RegexOptions.Singleline);

    // The declarations of the first rule whose selector contains <paramref name="selector"/>.
    private static string Rule(string css, string selector)
    {
        foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^}]*)\}"))
        {
            if (rule.Groups[1].Value.Contains(selector, StringComparison.Ordinal))
            {
                return rule.Groups[2].Value;
            }
        }

        Assert.Fail($"the common style has no rule for '{selector}'");
        return "";
    }

    // Everything in the common style outside the wide-screen media query: the table rules are not
    // layout-dependent, so they hold at any zoom (100% and 60% both count).
    private static string Outside() =>
        Css().Replace(MediaBlock(Css(), Medium), "", StringComparison.Ordinal);

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

    // 968-C: the short columns - the number, Log, Category, Depends on, When, Worker, Result, Time and
    // the buttons - hold their field on one line and take only the width their content needs.
    [Fact]
    public void CommonCss_ShortColumns_NeverWrapAndSizeToContent()
    {
        var css = Outside();
        foreach (var window in new[] { "#w-queue", "#w-events" })
        {
            var cells = Rule(css, window + " td.nw");
            Assert.Contains("white-space: nowrap", cells, StringComparison.Ordinal);
            Assert.Contains("width: 1%", cells, StringComparison.Ordinal);
            var heads = Rule(css, window + " th");
            Assert.Contains("white-space: nowrap", heads, StringComparison.Ordinal);
        }
    }

    // 968-C: the State/notes column is the only one that wraps, and it breaks inside a word only for
    // a token too long to fit alone (a log path, an unbroken card name).
    [Fact]
    public void CommonCss_StateColumn_IsTheOnlyColumnThatWraps()
    {
        var css = Outside();
        foreach (var window in new[] { "#w-queue", "#w-events" })
        {
            var cells = Rule(css, window + " td.wrap");
            Assert.Contains("white-space: normal", cells, StringComparison.Ordinal);
            Assert.Contains("overflow-wrap: anywhere", cells, StringComparison.Ordinal);
            Assert.DoesNotContain("white-space: nowrap", cells, StringComparison.Ordinal);
        }
    }

    // 968-C: a card name that will not fit is shortened with an ellipsis and never broken - not
    // inside a word, not at a hyphen - and keeps a floor wide enough to read the id.
    [Fact]
    public void CommonCss_CardColumn_OneLineEllipsisedWithAFloor()
    {
        var css = Outside();
        foreach (var window in new[] { "#w-queue", "#w-events" })
        {
            var cell = Rule(css, window + " td.card");
            Assert.Contains("white-space: nowrap", cell, StringComparison.Ordinal);
            Assert.Contains("text-overflow: ellipsis", cell, StringComparison.Ordinal);
            Assert.Contains("overflow: hidden", cell, StringComparison.Ordinal);
            Assert.Contains("min-width: 22ch", cell, StringComparison.Ordinal);
            Assert.DoesNotContain("white-space: normal", cell, StringComparison.Ordinal);
            Assert.Contains("text-overflow: ellipsis", Rule(css, window + " td.card a"), StringComparison.Ordinal);
        }
    }

    // 968-C keeps 958-C's rule that nothing in the side column holds it open. The card column's
    // 22ch floor is named by the window's own id, which outranks 958-C's "#col-side td { min-width: 0 }".
    [Fact]
    public void CommonCss_CardColumnFloor_OutranksTheSideColumnsShrinkRule()
    {
        var medium = MediaBlock(PageThemes.CommonCss, Medium);
        Assert.Contains("#col-side td", medium, StringComparison.Ordinal);
        Assert.Contains("min-width: 0", medium, StringComparison.Ordinal);
        foreach (var window in new[] { "#w-queue", "#w-events" })
        {
            var selector = RuleSelector(window + " td.card");
            Assert.Contains(window, selector, StringComparison.Ordinal);
            // Every selector in the rule is named by the window's own id, so it outranks that rule.
            Assert.All(selector.Split(','), part => Assert.StartsWith("#w-", part.Trim(), StringComparison.Ordinal));
        }
    }

    // The selector part of the first rule matching <paramref name="selector"/>, trimmed.
    private static string RuleSelector(string selector)
    {
        foreach (Match rule in Regex.Matches(Css(), @"([^{}]+)\{([^}]*)\}"))
        {
            if (rule.Groups[1].Value.Contains(selector, StringComparison.Ordinal))
            {
                return rule.Groups[1].Value.Trim();
            }
        }

        Assert.Fail($"the common style has no rule for '{selector}'");
        return "";
    }

    // 968-C: the Queue row marks each column: the number, Log, Category and Depends on never wrap, the
    // card is the ellipsised one, and the State note is the wrapping one.
    [Fact]
    public void Queue_Row_MarksItsShortCardAndWrappingColumns()
    {
        var html = Render(Rows());
        var at = html.IndexOf("id='w-queue'", StringComparison.Ordinal);
        Assert.True(at >= 0, "the page has no Queue window");
        var row = html.Substring(at, html.IndexOf("</table>", at, StringComparison.Ordinal) - at);

        Assert.Contains("<th>#</th><th>Card</th><th>Log</th><th>Category</th><th>Depends on</th><th>State</th>", row, StringComparison.Ordinal);
        Assert.Contains("<td class='mute nw'>1</td>", row, StringComparison.Ordinal);
        Assert.Contains("<td class='card'>", row, StringComparison.Ordinal);
        Assert.Contains("<td class='nw'>", row, StringComparison.Ordinal);
        Assert.Contains("<td class='ok wrap'>next up", row, StringComparison.Ordinal);

        // Every cell in the row is one of the three kinds: nothing is left to wrap by accident.
        var kinds = Regex.Matches(row, @"<td(?: class='([^']*)')?").Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Where(c => !c.Contains("wrap", StringComparison.Ordinal) && !c.Contains("card", StringComparison.Ordinal))
            .ToList();
        Assert.All(kinds, c => Assert.Contains("nw", c, StringComparison.Ordinal));
    }

    // 968-C: the card name is the field most likely to run out of room, so its link carries the whole
    // name in its tooltip, beside the card's title.
    [Fact]
    public void Queue_CardLink_CarriesTheWholeNameInItsTooltip()
    {
        var html = Render(Rows());
        Assert.Contains(
            "<a href='/card?name=957-B-instant-page-actions.md' title='instant page actions - 957-B-instant-page-actions.md'>957-B-instant-page-actions.md</a>",
            html,
            StringComparison.Ordinal);
    }

    // 968-C: the Recent events row marks its columns the same way - When, Worker, Result, Time, the
    // log and the button never wrap, the card ellipsises, and an event that is not a run keeps its
    // whole line, wrapping as text.
    [Fact]
    public void Events_Row_MarksItsColumns()
    {
        var html = Render(Rows());
        var at = html.IndexOf("id='w-events'", StringComparison.Ordinal);
        Assert.True(at >= 0, "the page has no Recent events window");
        var row = html.Substring(at, html.IndexOf("</table>", at, StringComparison.Ordinal) - at);

        Assert.Contains("<td class='nw'>11-18 12:30</td>", row, StringComparison.Ordinal);
        Assert.Contains("<td class='card'><a href='/card?name=957-B-instant-page-actions' title='957-B-instant-page-actions'>", row, StringComparison.Ordinal);
        Assert.Contains("<td class='nw'><span class='ok'>done</span></td>", row, StringComparison.Ordinal);
        Assert.Contains("<td class='nw'>5 min</td>", row, StringComparison.Ordinal);
        Assert.Contains("<td class='wrap' colspan='6'><span class='ok'>resumed C:\\Users", row, StringComparison.Ordinal);
    }

    // 968-C: the pool page draws the same two windows, so its rows carry the same marks and the same
    // tooltip, and its prose columns (Title, Note) are the wrapping ones.
    [Fact]
    public void CombinedPage_QueueAndEvents_UseTheSameColumns()
    {
        var html = new CombinedPageRenderer().Render([Rows()], "", remote: false, controlsAllowed: true);
        Assert.Contains("<td class='card'><a href='/card?project=ProjectToolkit&name=957-B-instant-page-actions.md' title='ProjectToolkit - 957-B-instant-page-actions.md'>", html, StringComparison.Ordinal);
        Assert.Contains("<td class='wrap'>instant page actions</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class='nw'><span class='proj'>ProjectToolkit</span></td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class='mute wrap'>next up", html, StringComparison.Ordinal);
        Assert.Contains("<td class='mute nw'>", html, StringComparison.Ordinal);
        // 1004-C: a run's event row is its card, linked to its view, and its result - no slot, no minutes.
        Assert.Contains("<td class='card'><a href='/card?project=ProjectToolkit&name=957-B-instant-page-actions' title='ProjectToolkit - 957-B-instant-page-actions'>957-B-instant-page-actions</a></td><td class='nw'><span class='ok'>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("slot 3  957-B done, 5 min", html, StringComparison.Ordinal);
    }

    // 968-C, the developer's second look (2026-11-19 20:50): every header carried "width: 1%", so the
    // State column was sized like a short one and lost the spare width to the card column. Only the
    // short and card columns are sized to content; the note column takes what is left.
    [Fact]
    public void CommonCss_OnlyShortColumnsAreSizedToContent()
    {
        var css = Outside();
        foreach (var window in new[] { "#w-queue", "#w-events", "#w-waiting" })
        {
            Assert.DoesNotContain("width: 1%", Rule(css, window + " th"), StringComparison.Ordinal);
            Assert.DoesNotContain("width: 1%", Rule(css, window + " td.wrap"), StringComparison.Ordinal);
            Assert.Contains("width: 1%", Rule(css, window + " td.nw"), StringComparison.Ordinal);
        }
    }

    // A table cell's max-width is ignored by the automatic table layout, so the name was never
    // capped and the card column took the full name's width. The cap is on the link inside it,
    // which a table does respect when it measures the column.
    [Fact]
    public void CommonCss_CardName_IsCappedOnItsLink()
    {
        var css = Outside();
        foreach (var window in new[] { "#w-queue", "#w-events", "#w-waiting" })
        {
            var link = Rule(css, window + " td.card a");
            Assert.Contains("display: block", link, StringComparison.Ordinal);
            Assert.Contains("max-width: 26ch", link, StringComparison.Ordinal);
            Assert.Contains("text-overflow: ellipsis", link, StringComparison.Ordinal);
        }
    }

    // Buttons and status words never break inside a word ("O/pe/n", "rev/ie/w").
    [Fact]
    public void CommonCss_ButtonsAndStatusWords_NeverWrap()
    {
        var rule = Rule(Outside(), ".desk button");
        Assert.Contains("white-space: nowrap", rule, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: normal", rule, StringComparison.Ordinal);
        Assert.Contains(".desk td.nw span", RuleSelector(".desk button"), StringComparison.Ordinal);
        var choices = Rule(Outside(), ".qchoices");
        Assert.Contains("flex-wrap: wrap", choices, StringComparison.Ordinal);
    }

    // Waiting on you: the status word, Open and the actions are short, the card ellipsises, the notes wrap.
    [Fact]
    public void Waiting_Row_MarksItsColumns()
    {
        var html = Render(ViewsFixture.Full());
        Assert.Contains("<tr data-card='020-B'><td class='nw'><span class='bad'>question</span></td><td class='card'>", html, StringComparison.Ordinal);
        Assert.Contains("</td><td class='nw'><form method='post' action='/open?card=020-B'", html, StringComparison.Ordinal);
        Assert.Contains("<td class='wrap'>found a missing field</td>", html, StringComparison.Ordinal);
    }

    // A question's choices sit under the question, in one cell across the row, as a row of buttons.
    [Fact]
    public void Waiting_QuestionRow_ChoicesSitUnderTheQuestion()
    {
        var html = Render(ViewsFixture.Full());
        Assert.Contains("<tr class='qrow'><td></td><td colspan='4' class='wrap'><span class='q'>", html, StringComparison.Ordinal);
        var row = html[html.IndexOf("<tr class='qrow'>", StringComparison.Ordinal)..];
        row = row[..row.IndexOf("</tr>", StringComparison.Ordinal)];
        Assert.True(row.IndexOf("Which format?", StringComparison.Ordinal) < row.IndexOf("<div class='qchoices'>", StringComparison.Ordinal));
        Assert.Contains("<div class='qchoices'><button form='n-020-B' formaction='/answer?q=1'", row, StringComparison.Ordinal);
    }

    // The pool page draws Waiting on you with the same columns and the choices under the question.
    [Fact]
    public void CombinedPage_Waiting_UsesTheSameColumns()
    {
        var html = new CombinedPageRenderer().Render([ViewsFixture.Full()], "", remote: false, controlsAllowed: true);
        Assert.Contains("<td class='nw'><span class='bad'>question</span></td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class='wrap'>found a missing field</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class='mute nw'>Q1</td><td colspan='2' class='wrap'><span class='q'>", html, StringComparison.Ordinal);
    }

    // Every header on the page breaks only between words ("R/U/N/N/I/N/G" in Worker types), and in
    // Recent events, which has no note column on a run's row, the card takes the spare width.
    [Fact]
    public void CommonCss_HeadersNeverBreakInAWord_AndEventsCardTakesTheRest()
    {
        var css = Outside();
        Assert.Contains("overflow-wrap: normal", Rule(css, ".desk th"), StringComparison.Ordinal);
        Assert.Contains("width: 100%", RuleExact(css, "#w-events td.card"), StringComparison.Ordinal);
    }

    // The declarations of the rule whose whole selector is <paramref name="selector"/>.
    private static string RuleExact(string css, string selector)
    {
        foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^}]*)\}"))
        {
            if (rule.Groups[1].Value.Trim() == selector)
            {
                return rule.Groups[2].Value;
            }
        }

        Assert.Fail($"the common style has no rule '{selector}'");
        return "";
    }
}
