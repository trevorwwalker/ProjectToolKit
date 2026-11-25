// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;
using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>1257-C: with a project chosen, the Waiting on you and Requests windows are hidden unless that project has rows in them.</summary>
public sealed class PoolWindowFilterTests
{
    private static PageSnapshot Project(string name, bool withData) =>
        ViewsFixture.Full() with
        {
            Project = name,
            Waiting = withData
                ? [new PageWaitingItem { Kind = PageWaitingKind.Waiting, Name = "9-A-thing.md", Note = "n" }]
                : [],
            Planner = new PagePlannerView
            {
                OpenRequests = withData
                    ? [new JobRequest { Path = "jobs/requests/5-ask.md", Name = "5-ask", Title = "Ask", For = "pool", Open = true }]
                    : [],
                WaitingCards = [],
            },
        };

    private static string Render() =>
        new CombinedPageRenderer().Render([Project("AmigaX", true), Project("ProjectToolkit", false)], "", remote: false, controlsAllowed: true);

    [Fact]
    public void BothWindows_ComeWithRowsOfTheOneProjectOnly()
    {
        var html = Render();
        var waiting = html[html.IndexOf("id='w-waiting'", StringComparison.Ordinal)..];
        waiting = waiting[..waiting.IndexOf("</section>", StringComparison.Ordinal)];
        var requests = html[html.IndexOf("id='w-requests'", StringComparison.Ordinal)..];
        requests = requests[..requests.IndexOf("</section>", StringComparison.Ordinal)];

        Assert.Contains("data-project='AmigaX'", waiting, StringComparison.Ordinal);
        Assert.DoesNotContain("data-project='ProjectToolkit'", waiting, StringComparison.Ordinal);
        Assert.Contains("data-project='AmigaX'", requests, StringComparison.Ordinal);
        Assert.DoesNotContain("data-project='ProjectToolkit'", requests, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterScript_HidesTheWholeWindow_WhenTheChosenProjectHasNothingInIt()
    {
        var html = Render();

        Assert.Contains("applyWaiting(v);", html, StringComparison.Ordinal);
        Assert.Contains("getElementById('w-waiting')", html, StringComparison.Ordinal);
        Assert.Contains("win.classList.toggle('projhide', v !== 'all' && n === 0)", html, StringComparison.Ordinal);
        Assert.Contains("win.classList.toggle('projhide', v !== 'all' && counts.open === 0 && counts.waiting === 0)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AllProjects_ShowsBothWindows_AndTheTagCountsTheOpenRequest()
    {
        var html = Render();

        Assert.Contains("id='w-waiting'", html, StringComparison.Ordinal);
        Assert.Contains("id='w-requests'", html, StringComparison.Ordinal);
        Assert.Contains("1 open", html, StringComparison.Ordinal);
    }

    // 1280-C: the Project column of Waiting on you and Requests carries one class on its heading and cells.
    private static string RenderWithQuestion()
    {
        var withQuestion = Project("AmigaX", true) with
        {
            Waiting =
            [
                new PageWaitingItem { Kind = PageWaitingKind.Waiting, Name = "9-A-thing.md", Note = "n" },
                new PageWaitingItem
                {
                    Kind = PageWaitingKind.Question,
                    Name = "8-A-ask.md",
                    Note = "q",
                    Questions = [new CardQuestion { Number = 1, Text = "Sure?", Choices = [new CardChoice { Label = "yes" }] }],
                },
            ],
            Planner = new PagePlannerView
            {
                OpenRequests = [new JobRequest { Path = "jobs/requests/5-ask.md", Name = "5-ask", Title = "Ask", For = "pool", Open = true }],
                WaitingCards = ["7-A-wait.md"],
            },
        };
        return new CombinedPageRenderer().Render([withQuestion, Project("ProjectToolkit", false)], "", remote: false, controlsAllowed: true);
    }

    private static List<string> Rows(string html, string tableClass)
    {
        var start = html.IndexOf("<table class='" + tableClass + "'", StringComparison.Ordinal);
        Assert.True(start >= 0, tableClass);
        var table = html[start..];
        table = table[..table.IndexOf("</table>", StringComparison.Ordinal)];
        return System.Text.RegularExpressions.Regex.Matches(table, "<tr[ >].*?</tr>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(match => match.Value).ToList();
    }

    // The cells of a row as (class has projcol, colspan).
    private static List<(bool Proj, int Span)> Cells(string row) =>
        System.Text.RegularExpressions.Regex.Matches(row, "<t[dh]( [^>]*)?>")
            .Select(match =>
            {
                var span = System.Text.RegularExpressions.Regex.Match(match.Value, @"colspan='(\d+)'");
                return (match.Value.Contains("projcol", StringComparison.Ordinal), span.Success ? int.Parse(span.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 1);
            }).ToList();

    [Theory]
    [InlineData("waiting")]
    [InlineData("rq-open")]
    [InlineData("rq-waiting")]
    public void ProjectColumn_IsMarkedOnTheHeadingAndEveryRowKind_AndRowsLineUp(string tableClass)
    {
        var rows = Rows(RenderWithQuestion(), tableClass);
        var heading = Cells(rows[0]);
        Assert.True(rows.Count >= 2);
        Assert.Single(heading, cell => cell.Proj);
        Assert.Contains("class='projcol'>Project", rows[0], StringComparison.Ordinal);
        var all = heading.Sum(cell => cell.Span);
        var picked = heading.Where(cell => !cell.Proj).Sum(cell => cell.Span);
        foreach (var row in rows.Skip(1))
        {
            var cells = Cells(row);
            Assert.Single(cells, cell => cell.Proj);
            Assert.Equal(all, cells.Sum(cell => cell.Span));
            Assert.Equal(picked, cells.Where(cell => !cell.Proj).Sum(cell => cell.Span));
        }
    }

    [Fact]
    public void WaitingOnYou_HasAQuestionRowMarkedLikeTheOthers()
    {
        var rows = Rows(RenderWithQuestion(), "waiting");
        Assert.Contains(rows, row => row.Contains("class='qrow'", StringComparison.Ordinal) && row.Contains("colspan='2'", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectsTable_KeepsItsProjectColumn_AndThePageHidesTheMarkedOnesWhenOneIsPicked()
    {
        var html = RenderWithQuestion();
        Assert.DoesNotContain("projcol", Rows(html, "projects").Aggregate("", (a, b) => a + b), StringComparison.Ordinal);
        Assert.Contains("body[data-projsel]:not([data-projsel='all']) .projcol { display: none; }", html, StringComparison.Ordinal);
    }
}
