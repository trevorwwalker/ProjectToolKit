// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Tests.PageModel;

/// <summary>
/// The Queue window's rows (PAGE-6; the queue window of schedule.ps1 L3651-3668 and Order-Queue
/// L3149): orphans by Num, then queued cards by Prio, Num and Category, never before a queued card they
/// depend on, a cycle broken by that order, and the waiting/running/stale cards left out. The
/// waiting-on-developer card in active/ is the one card two windows both list (C15).
/// Test plan: scheduler-next/test-plans/page.md.
/// </summary>
public class PageQueueTests
{
    private static Card Card(CardFolder folder, string name, string status = "ready", int prio = 1, params string[] depends)
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
            Prio = prio,
            Depends = depends,
        };
    }

    private static RunningWorker Running(int slot, string name)
    {
        var parts = name.Split('-');
        return new RunningWorker
        {
            Slot = slot, Pid = 100 + slot, Start = DateTimeOffset.UnixEpoch,
            Card = name, Category = parts[1], Id = $"{parts[0]}-{parts[1]}",
        };
    }

    // PAGE-6. Orphans first by Num, then the queue: 015-D is high, 020-C runs before 010-C which
    // depends on it, and the 060-E/061-E cycle falls back to plain order. The waiting-on-dev card,
    // the card a worker is running and the stale queue/ copy are left out.
    [Fact]
    public void Page_QueueOrder_DepsAndCycle()
    {
        var cards = new List<Card>
        {
            Card(CardFolder.Active, "050-A"),
            Card(CardFolder.Active, "030-B"),
            Card(CardFolder.Queue, "010-C", depends: ["020-C"]),
            Card(CardFolder.Queue, "020-C"),
            Card(CardFolder.Queue, "015-D", prio: 0),
            Card(CardFolder.Queue, "060-E", depends: ["061-E"]),
            Card(CardFolder.Queue, "061-E", depends: ["060-E"]),
            Card(CardFolder.Queue, "080-D", "waiting: the developer: which sha goes in the banner"),
            Card(CardFolder.Active, "090-B"),
            Card(CardFolder.Queue, "070-B"),
            Card(CardFolder.Done, "070-B"),
        };
        var running = new[] { Running(1, "090-B") };

        var queue = PageQueue.Order(cards, running);

        Assert.Equal(
            ["030-B", "050-A", "015-D", "020-C", "010-C", "060-E", "061-E"],
            queue.Select(i => i.Name).ToArray());
    }

    // PAGE-5, PAGE-6, DISP-4 · Spec C15: today's behaviour, kept until the developer decides (003-D review).
    [Fact]
    public void Page_WaitingOnDevInActive_ShownTwice()
    {
        var waiting = Card(CardFolder.Active, "040-B", "waiting: developer: the developer must pick a reading");
        var cards = new[] { waiting, Card(CardFolder.Active, "050-A"), Card(CardFolder.Queue, "020-C") };
        var running = Array.Empty<RunningWorker>();

        var window = PageWaiting.Window(cards);
        var queue = PageQueue.Order(cards, running);

        // Listed under Waiting on you...
        Assert.Single(window, i => i.Kind == PageWaitingKind.Waiting && i.Name == "040-B");
        // ...and as an orphan in the Queue window, though no worker resumes it (L3650).
        var row = Assert.Single(queue, i => i.Name == "040-B");
        Assert.Equal(PageQueue.OrphanNote, row.Note);
        // A plain active card is only an orphan, and a ready queued card only a queue row.
        Assert.Single(queue, i => i.Name == "050-A");
        Assert.DoesNotContain(window, i => i.Name == "050-A");
        Assert.DoesNotContain(window, i => i.Name == "020-C");
    }

    // PAGE-6 (931-A, developer 2026-11-19): card numbers are of any width, so the Queue window
    // still runs by the number - 999-B before 1000-A, and the orphan 5-A before the 1001-C - and a
    // four-digit dependency holds a three-digit card back.
    [Fact]
    public void Page_QueueOrder_IdsOfAnyWidthSortByNumber()
    {
        var cards = new List<Card>
        {
            Card(CardFolder.Queue, "1000-A"),
            Card(CardFolder.Queue, "999-B"),
            Card(CardFolder.Queue, "98-A", depends: ["1000-A"]),
            Card(CardFolder.Active, "1001-C"),
            Card(CardFolder.Active, "5-A"),
        };
        var running = Array.Empty<RunningWorker>();

        var queue = PageQueue.Order(cards, running);

        Assert.Equal(
            ["5-A", "1001-C", "999-B", "1000-A", "98-A"],
            queue.Select(i => i.Name).ToArray());
    }
}
