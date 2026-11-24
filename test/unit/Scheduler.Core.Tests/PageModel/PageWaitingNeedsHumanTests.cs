using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Tests.PageModel;

/// <summary>
/// The Waiting on you rows for a card parked as <c>needs-human</c> (PAGE-5, CARD-13). 1149-B: a
/// worker parks a card it is working on, but the planner or the developer can park one that never
/// left queue/, and the dispatcher takes a needs-human card from either folder (DISP-2, DISP-9) -
/// so a queued one showed nowhere and its question was never read. Both folders are listed now,
/// each card once, with the text after <c>needs-human:</c> as its note.
/// Test plan: scheduler-next/test-plans/page.md (PAGE-5).
/// </summary>
public class PageWaitingNeedsHumanTests
{
    private static Card Parked(CardFolder folder, string name, string reason)
    {
        var parts = name.Split('-');
        var status = $"needs-human: {reason}";
        var (why, whyFrom) = CardWhy.For(folder, status, []);
        return new Card
        {
            Folder = folder,
            Path = $"docs/jobs/{folder.ToString().ToLowerInvariant()}/{name}.md",
            Name = name,
            Num = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            Category = parts[1],
            Id = $"{parts[0]}-{parts[1]}",
            Title = $"The {name} card",
            Status = status,
            Why = why,
            WhyFrom = whyFrom,
        };
    }

    // 1149-B: the card that was missed. Parked in queue/, it is a question row with its own reason.
    [Fact]
    public void Waiting_NeedsHumanInQueue_ListedOnceWithItsStatusNote()
    {
        var card = Parked(CardFolder.Queue, "1085-B", "Leave it, close the app while the pool runs, or remove its worktree?");

        var window = PageWaiting.Window([card]);

        var row = Assert.Single(window);
        Assert.Equal(PageWaitingKind.Question, row.Kind);
        Assert.Equal("1085-B", row.Name);
        Assert.Equal("Leave it, close the app while the pool runs, or remove its worktree?", row.Note);
    }

    // 1149-B: a worker's own park still shows, as it always did.
    [Fact]
    public void Waiting_NeedsHumanInActive_StillListed()
    {
        var card = Parked(CardFolder.Active, "1085-B", "the plan does not settle the trade-off");

        var row = Assert.Single(PageWaiting.Window([card]));

        Assert.Equal(PageWaitingKind.Question, row.Kind);
        Assert.Equal("the plan does not settle the trade-off", row.Note);
    }

    // 1149-B: both parked cards are in the window, ordered by number whatever folder each is in.
    [Fact]
    public void Waiting_NeedsHuman_QueueAndActive_BothListedByNumber()
    {
        var queued = Parked(CardFolder.Queue, "1100-A", "which gate to run");
        var active = Parked(CardFolder.Active, "1090-C", "needs a decision");

        var window = PageWaiting.Window([active, queued]);

        Assert.Equal(["1090-C", "1100-A"], window.Select(i => i.Name).ToArray());
        Assert.All(window, i => Assert.Equal(PageWaitingKind.Question, i.Kind));
    }

    // 1149-B: a card another rule of this window already lists is not listed twice. A stuck card
    // keeps its stuck row, which says more than the park does.
    [Fact]
    public void Waiting_NeedsHuman_AlsoStuck_ListedOnceAsStuck()
    {
        var card = Parked(CardFolder.Active, "1085-B", "why did it crash");

        var window = PageWaiting.Window([card], stuck: [card], maxRetries: 2);

        var row = Assert.Single(window);
        Assert.Equal(PageWaitingKind.Stuck, row.Kind);
    }

    // The new dedupe must not swallow the other rule: a queue card that waits on the developer by
    // its Status still gets exactly one Waiting row (CARD-15, PAGE-5).
    [Fact]
    public void Waiting_WaitsOnDeveloperInQueue_StillListedOnce()
    {
        var parked = Parked(CardFolder.Queue, "1085-B", "asked the developer");
        var waiting = parked with
        {
            Status = "waiting: developer to accept the theme",
            Why = "to accept the theme",
            WhyFrom = "status",
        };
        var alsoWaiting = parked with
        {
            Name = "1086-B",
            Id = "1086-B",
            Path = "docs/jobs/queue/1086-B.md",
            Status = "waiting: dev to say so",
            Why = "to say so",
            WhyFrom = "status",
        };

        var window = PageWaiting.Window([waiting, alsoWaiting]);

        Assert.Equal(2, window.Count);
        Assert.All(window, i => Assert.Equal(PageWaitingKind.Waiting, i.Kind));
    }

    // Only queue/ and active/ wait this way: a card in done/ waits for nothing, and one in review/
    // has its own row (PAGE-5), so it is not a needs-human question.
    [Fact]
    public void Waiting_NeedsHuman_InOtherFolders_NotAQuestionRow()
    {
        var done = Parked(CardFolder.Done, "1085-B", "already answered");
        var review = Parked(CardFolder.Review, "1086-B", "check it by hand");

        Assert.Empty(PageWaiting.Window([done]));

        var row = Assert.Single(PageWaiting.Window([review]));
        Assert.Equal(PageWaitingKind.Review, row.Kind);
    }
}
