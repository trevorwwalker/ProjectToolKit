using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Tests.PageModel;

/// <summary>
/// The Waiting on you row for a card dispatch found stuck (DISP-7, DISP-11). 940-A: it says how many
/// of its runs ended without a result and, when the host names the last run, what that run ended as
/// and which log to read - which is what makes a card that keeps crashing explain itself.
/// Test plan: scheduler-next/test-plans/page.md (PAGE-5).
/// </summary>
public class PageWaitingStuckTests
{
    private static Card Card(CardFolder folder, string name)
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
            Status = "ready",
        };
    }

    // DISP-7, PAGE-5: with no run to name, the row says what it always said.
    [Fact]
    public void Waiting_Stuck_NoRunKnown_KeepsTheCountOfMaxRetries()
    {
        var window = PageWaiting.Window([Card(CardFolder.Queue, "200-A-x")], stuck: [Card(CardFolder.Queue, "200-A-x")], maxRetries: 2);

        var row = Assert.Single(window, i => i.Kind == PageWaitingKind.Stuck);
        Assert.Equal("200-A-x", row.Name);
        Assert.Equal("3 runs ended without a result (stalled or killed). Read its logs, fix the card, then Retry.", row.Note);
        Assert.Equal("", row.LastResult);
        Assert.Equal("", row.Log);
    }

    // 940-A: with the last run named, the row counts the runs that really happened and says what the
    // last one ended as, with the log to open.
    [Fact]
    public void Waiting_Stuck_LastRunNamed_NotesResultAndLog()
    {
        var card = Card(CardFolder.Queue, "200-A-x");
        var runs = new[]
        {
            new PageStuckRun { Card = "200-A-x", Runs = 2, Result = "crashed (exit 1)", Log = @"logs\200-A-20261118-093012.log" },
            new PageStuckRun { Card = "999-Z", Runs = 7, Result = "stalled", Log = "other.log" },
        };

        var window = PageWaiting.Window([card], stuck: [card], maxRetries: 2, stuckRuns: runs);

        var row = Assert.Single(window, i => i.Kind == PageWaitingKind.Stuck);
        Assert.Equal("2 runs ended without a result (last: crashed (exit 1)). Read its logs, fix the card, then Retry.", row.Note);
        Assert.Equal("crashed (exit 1)", row.LastResult);
        Assert.Equal(@"logs\200-A-20261118-093012.log", row.Log);
    }

    // 940-A: the host may know the count but not the last run (a run with no log), or name the card
    // by its id rather than its file name.
    [Fact]
    public void Waiting_Stuck_ByCardIdOrWithoutResult_StillCountsWhatItKnows()
    {
        var card = Card(CardFolder.Active, "200-A-x");

        var byId = PageWaiting.Window([card], stuck: [card], maxRetries: 2,
            stuckRuns: [new PageStuckRun { Card = "200-A", Runs = 5 }]);
        var row = Assert.Single(byId, i => i.Kind == PageWaitingKind.Stuck);
        Assert.Equal("5 runs ended without a result (stalled or killed). Read its logs, fix the card, then Retry.", row.Note);
        Assert.Equal("", row.LastResult);

        var noRuns = PageWaiting.Window([card], stuck: [card], maxRetries: 2,
            stuckRuns: [new PageStuckRun { Card = "200-A-x", Result = "no log" }]);
        var second = Assert.Single(noRuns, i => i.Kind == PageWaitingKind.Stuck);
        Assert.Equal("3 runs ended without a result (last: no log). Read its logs, fix the card, then Retry.", second.Note);
        Assert.Equal("no log", second.LastResult);
    }
}
