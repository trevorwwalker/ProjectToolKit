// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// The Waiting on you row of a stuck card (DISP-7, DISP-11) says what its last run ended as and links
/// the log that shows it, beside the Retry button (940-A's page half). The row's note is Core's
/// <see cref="Scheduler.Core.PageModel.PageWaiting.StuckNote"/>; what these tests check is that the page
/// draws the last result in it and makes the log openable, on both the page and the combined page.
/// Test plan: scheduler-next/test-plans/page.md (PAGE-5, PAGE-9).
/// </summary>
public sealed class PageStuckRunRowTests
{
    private const string Log = @"logs\010-A-20261118-093012.log";

    private static PageWaitingItem Row(string log = Log) => new()
    {
        Kind = PageWaitingKind.Stuck,
        Name = "010-A-x",
        Note = "2 runs ended without a result (last: crashed (exit 1)). Read its logs, fix the card, then Retry.",
        LastResult = "crashed (exit 1)",
        Log = log,
    };

    private static string Render(params PageWaitingItem[] waiting) =>
        new PageRenderer().Render(
            ViewsFixture.Plain() with { Waiting = waiting },
            remote: false,
            controlsAllowed: true);

    // PAGE-5: the stuck row names the last run's result and links its log beside Retry.
    [Fact]
    public void Page_StuckCard_RowShowsLastResultAndLogLink()
    {
        var html = Render(Row());

        Assert.Contains(">stuck<", html, StringComparison.Ordinal);
        Assert.Contains("last: crashed (exit 1)", html, StringComparison.Ordinal);
        Assert.Contains("href='/log?file=010-A-20261118-093012.log'", html, StringComparison.Ordinal);
        Assert.Contains(">Retry</button>", html, StringComparison.Ordinal);
    }

    // PAGE-9: the link carries the file name alone, the way the /log route wants it, and says what it opens.
    [Fact]
    public void Page_StuckCard_LogLinkNamesTheFileAlone()
    {
        var html = Render(Row());

        Assert.Contains("href='/log?file=010-A-20261118-093012.log' title='The last run&#39;s log'>log</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("file=logs", html, StringComparison.Ordinal);
    }

    // The link sits in the row beside Retry, so the row reads card - actions - note.
    [Fact]
    public void Page_StuckCard_LogLinkIsBesideRetry()
    {
        var html = Render(Row());

        var from = html.IndexOf("data-card='010-A'", StringComparison.Ordinal);
        var row = html[from..html.IndexOf("</tr>", from, StringComparison.Ordinal)];
        Assert.Contains(">Retry</button>", row, StringComparison.Ordinal);
        Assert.Contains("/log?file=", row, StringComparison.Ordinal);
    }

    // A card whose runs left no log gets no link - nothing to open - but keeps the note and Retry.
    [Fact]
    public void Page_StuckCard_WithoutLog_NoLink()
    {
        var html = Render(Row(log: ""));

        Assert.Contains(">stuck<", html, StringComparison.Ordinal);
        Assert.Contains(">Retry</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/log?file=", html, StringComparison.Ordinal);
    }

    // A row of another kind shows no log link of its own (510-B's queue links are elsewhere).
    [Fact]
    public void Page_OtherWaitingKinds_NoStuckLogLink()
    {
        var html = Render(new PageWaitingItem
        {
            Kind = PageWaitingKind.Waiting,
            Name = "030-A-x",
            Note = "waiting: developer, see Q1",
        });

        Assert.DoesNotContain("/log?file=", html, StringComparison.Ordinal);
    }

    // MP-8: the one Waiting on you across projects carries the link too.
    [Fact]
    public void CombinedPage_StuckCard_RowShowsLastResultAndLogLink()
    {
        var snapshot = ViewsFixture.Plain() with { Project = "beta", Waiting = [Row()] };
        var html = new CombinedPageRenderer().Render([snapshot], "slate", remote: false, controlsAllowed: true);

        Assert.Contains(">stuck<", html, StringComparison.Ordinal);
        Assert.Contains("last: crashed (exit 1)", html, StringComparison.Ordinal);
        Assert.Contains("href='/log?project=beta&file=010-A-20261118-093012.log'", html, StringComparison.Ordinal);
        Assert.Contains(">Retry</button>", html, StringComparison.Ordinal);
    }
}
