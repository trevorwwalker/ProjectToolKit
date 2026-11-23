using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Tests.PageModel;

/// <summary>
/// The Waiting on you rows for a card whose run finished (done or review) but whose text never
/// changed - 395-B's FinishedCardCheck caught it, and 410-A shows it here as stuck so the developer
/// fills the card in rather than missing it. The row leaves when main's copy moves on from the text
/// it was flagged at: the developer pressed Done (MOVE-6) or committed an edit.
/// Test plan: scheduler-next/test-plans/page.md (PAGE-5).
/// </summary>
public class PageWaitingTests
{
    private const string ExpectedNote =
        "finished without updating the card: its Result was not committed; check the run's log, fill in the card, then Done";

    private static Card Card(CardFolder folder, string name, string status = "ready", string stamp = "")
    {
        var parts = name.Split('-');
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
            BlobId = stamp,
        };
    }

    // PAGE-5, 395-B · 410-A: the flagged card is a stuck row with the note, and it is the only row
    // for that card - its review row would only repeat it.
    [Fact]
    public void Waiting_FinishedWithoutUpdate_ShownAsStuckWithNote()
    {
        var cards = new[]
        {
            Card(CardFolder.Review, "070-B", "ready", "blob-a"),
            Card(CardFolder.Review, "080-C", "review: check the page", "blob-c"),
        };
        var flags = new[]
        {
            new FinishedCardFlag { Card = "070-B", Folder = CardFolder.Review, Stamp = "blob-a" },
        };

        var window = PageWaiting.Window(cards, finishedWithoutUpdate: flags);

        var row = Assert.Single(window, i => i.Name == "070-B");
        Assert.Equal(PageWaitingKind.FinishedWithoutUpdate, row.Kind);
        Assert.Equal(PageWaiting.FinishedWithoutUpdateNote, row.Note);
        Assert.Equal(ExpectedNote, row.Note);
        // The other review card keeps its ordinary review row.
        Assert.Single(window, i => i.Name == "080-C" && i.Kind == PageWaitingKind.Review);
    }

    // 410-A: the row goes once the developer has handled it. Done moves a review card to done/
    // (MOVE-6); committing the filled-in card changes its blob; sending it back leaves both folders.
    [Fact]
    public void Waiting_FinishedWithoutUpdate_GoneAfterDoneOrCardChanged()
    {
        var flag = new FinishedCardFlag { Card = "070-B", Folder = CardFolder.Review, Stamp = "blob-a" };
        var waiting = Card(CardFolder.Review, "070-B", "ready", "blob-a");
        var pressedDone = Card(CardFolder.Done, "070-B", "done", "blob-a");
        var cardChanged = Card(CardFolder.Review, "070-B", "ready", "blob-b");

        Assert.Contains(PageWaiting.Window([waiting], finishedWithoutUpdate: [flag]),
                        i => i.Name == "070-B" && i.Kind == PageWaitingKind.FinishedWithoutUpdate);
        Assert.DoesNotContain(PageWaiting.Window([pressedDone], finishedWithoutUpdate: [flag]),
                              i => i.Kind == PageWaitingKind.FinishedWithoutUpdate);
        Assert.DoesNotContain(PageWaiting.Window([cardChanged], finishedWithoutUpdate: [flag]),
                              i => i.Kind == PageWaitingKind.FinishedWithoutUpdate);
    }

    // 1054-B: 1034-B's path. In review with an open question, the planner answered it and sent the
    // card back to queue/, a worker took it, committed its Result as a journal line and asked for done
    // with that same text - a pure rename, which 395-B's check flags. done/ waits for nothing, so the
    // card leaves Waiting at the answer move and the flag does not bring it back once it is done.
    [Fact]
    public void Waiting_ReviewAnsweredQueuedActiveDone_LeavesAtAnswerAndStaysGoneWhenDoneWithSameText()
    {
        const string name = "1034-B";
        var open = new CardQuestion { Number = 1, Text = "Merge it to main?" };
        var inReview = Card(CardFolder.Review, name, "review: see Q1", "blob-r") with { Questions = [open] };
        var answered = inReview with { Questions = [] };
        var queued = Card(CardFolder.Queue, name, "ready", "blob-q");
        var active = Card(CardFolder.Active, name, "ready", "blob-a");
        var done = Card(CardFolder.Done, name, "done", "blob-a");
        // What FinishedCardCheck records for that run: the done/ folder and the blob it had in active/.
        var flag = new FinishedCardFlag { Card = name, Folder = CardFolder.Done, Stamp = "blob-a" };
        var nameOnly = new FinishedCardFlag { Card = name };

        IReadOnlyList<PageWaitingItem> Rows(Card c, params FinishedCardFlag[] flags) =>
            [.. PageWaiting.Window([c], finishedWithoutUpdate: flags).Where(i => i.Name == name)];

        Assert.Single(Rows(inReview), i => i.Kind == PageWaitingKind.Review);
        Assert.Single(Rows(answered), i => i.Kind == PageWaitingKind.Review);   // review still waits for Done
        Assert.Empty(Rows(queued));
        Assert.Empty(Rows(active));
        Assert.Empty(Rows(done));
        Assert.Empty(Rows(done, flag));
        Assert.Empty(Rows(done, nameOnly));
    }

    // 1054-B: the flag still keeps a review card, the only folder where Done can clear it.
    [Fact]
    public void Waiting_FinishedWithoutUpdate_DoneFolderFlagNeverShows()
    {
        var flag = new FinishedCardFlag { Card = "070-B", Folder = CardFolder.Done, Stamp = "blob-a" };

        Assert.DoesNotContain(PageWaiting.Window([Card(CardFolder.Done, "070-B", "done", "blob-a")], finishedWithoutUpdate: [flag]),
                              i => i.Name == "070-B");
    }

    // 410-A · the minimal Host wiring: Flagged holds names only, so the folder and stamp tests are
    // skipped and the row shows while the card still sits in review/ (1054-B: done/ waits for nothing).
    [Fact]
    public void Waiting_FinishedWithoutUpdate_NameOnlyFlagShowsUntilTheCardMoves()
    {
        var flag = new FinishedCardFlag { Card = "070-B" };
        var inReview = Card(CardFolder.Review, "070-B");
        var sentBack = Card(CardFolder.Active, "070-B", "ready");

        Assert.Single(PageWaiting.Window([inReview], finishedWithoutUpdate: [flag]),
                      i => i.Kind == PageWaitingKind.FinishedWithoutUpdate);
        Assert.DoesNotContain(PageWaiting.Window([sentBack], finishedWithoutUpdate: [flag]),
                              i => i.Kind == PageWaitingKind.FinishedWithoutUpdate);
    }
}
