// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.PageModel;
using Scheduler.Core.WebContract;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// A card whose run finished without committing a Result (395-B, job card 410-A) waits under Waiting
/// on you for the developer to fill the card in and press Done. It is not a review card waiting to be
/// read, and saying so matters (949-B): a row labelled "review" for a card Done had already moved to
/// <c>done/</c> reads as a page that will not let go.
/// Test plan: scheduler-next/test-plans/page.md (PAGE-5).
/// </summary>
public sealed class PageFinishedRowTests
{
    private const string Card = "410-A-waiting-finished-without-update";

    private static PageWaitingItem Row() => new()
    {
        Kind = PageWaitingKind.FinishedWithoutUpdate,
        Name = Card,
        Note = PageWaiting.FinishedWithoutUpdateNote,
    };

    private static string Render(params PageWaitingItem[] waiting) =>
        new PageRenderer().Render(
            ViewsFixture.Plain() with { Waiting = waiting },
            remote: false,
            controlsAllowed: true);

    // PAGE-5: the row reads as stuck, with the note that says what to do, and keeps its Done button.
    [Fact]
    public void Page_FinishedWithoutUpdate_RowReadsAsStuckAndKeepsDone()
    {
        var html = Render(Row());

        Assert.Contains("<span class='bad'>stuck</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">review</span>", html, StringComparison.Ordinal);
        Assert.Contains("finished without updating the card", html, StringComparison.Ordinal);
        Assert.Contains("formaction='/done'", html, StringComparison.Ordinal);
        Assert.Contains(">Done</button>", html, StringComparison.Ordinal);
    }

    // The row still links the card and offers Open, so the developer can read what the worker left.
    [Fact]
    public void Page_FinishedWithoutUpdate_CardLinkedAndOpenable()
    {
        var html = Render(Row());

        Assert.Contains("data-card='410-A'", html, StringComparison.Ordinal);
        Assert.Contains($"action='/open?card={Card}'", html, StringComparison.Ordinal);
        Assert.Contains($"href='/review?card={Card}'", html, StringComparison.Ordinal);
    }
}
