// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Dispatch;
using Scheduler.Core.Results;

namespace Scheduler.Core.Tests.Dispatch;

/// <summary>
/// The first half of a dispatch decision (DISP-1 to DISP-14; schedule.ps1 L2867-2923): finished and
/// busy categories, orphans, queued cards, their notes and the candidate order. Which slot a candidate
/// goes to is DISP-15 (card 150-A); "the card starts" here means it is first in
/// <see cref="DispatchPlan.Ordered"/> (one free slot) or in the first N (N free slots).
/// Test plan: scheduler-next/test-plans/dispatch.md. Shared setup: maxRetries 2.
/// </summary>
public class DispatchCandidatesTests
{
    private const int MaxRetries = 2;

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

    private static DispatchPlan Plan(
        IEnumerable<Card> cards,
        IEnumerable<RunningWorker>? running = null,
        IReadOnlyDictionary<string, int>? retries = null) =>
        DispatchPlanner.Plan(
            [.. cards],
            [.. running ?? []],
            retries ?? new Dictionary<string, int>(),
            null,   // no categories.json: each area letter is its scope (560-A)
            MaxRetries);

    private static string[] OrderedIds(DispatchPlan plan) => [.. plan.Ordered.Select(c => c.Card.Id)];

    private static string? NoteOf(DispatchPlan plan, string name) =>
        plan.Notes.SingleOrDefault(n => n.Card == name)?.Note;

    // The run counts after one of the card's runs ended the way the worker did (940-A).
    private static Dictionary<string, int> End(
        RunningWorker w, string[]? readable, string[]? raw, int? exit,
        IReadOnlyDictionary<string, int> retries, bool serverDown = false) =>
        new(RunResults.Settle(
            w, readable, raw ?? readable, false, SchedulerConfig.DefaultQuotaPatterns,
            () => !serverDown, exit, true, new Dictionary<string, int>(retries)).Retries);


    // DISP-9, DISP-14.
    [Fact]
    public void Dispatch_EmptyQueue_NothingStartedNoNotes()
    {
        var empty = Plan([]);
        Assert.Empty(empty.Candidates);
        Assert.Empty(empty.Ordered);
        Assert.Empty(empty.Notes);
        Assert.Empty(empty.Stuck);

        var finishedOnly = Plan([Card(CardFolder.Done, "010-A-x"), Card(CardFolder.Review, "020-B-y", "review: look")]);
        Assert.Empty(finishedOnly.Candidates);
        Assert.Empty(finishedOnly.Ordered);
        Assert.Empty(finishedOnly.Notes);
        Assert.Empty(finishedOnly.Stuck);
    }

    // DISP-1, DISP-12, CARD-17 (1059-B): a ready card whose dependency is in review/ is not a
    // candidate and its note says it waits for that card; when the developer accepts the dependency
    // into done/, the dependent is dispatched.
    [Fact]
    public void Dispatch_ReviewerCard_HoldsDependentUntilDone()
    {
        var dependent = Card(CardFolder.Queue, "030-D-next", depends: ["020-C"]);

        var underReview = Plan([dependent, Card(CardFolder.Review, "020-C-dep", "review: check")]);
        Assert.Empty(underReview.Candidates);
        Assert.Empty(underReview.Ordered);
        Assert.Equal("waits for 020-C", NoteOf(underReview, "030-D-next"));

        var accepted = Plan([dependent, Card(CardFolder.Done, "020-C-dep")]);
        Assert.Equal(["030-D"], OrderedIds(accepted));
        Assert.Null(NoteOf(accepted, "030-D-next"));
    }

    // DISP-1 (1059-B): only done/ counts as finished.
    [Fact]
    public void Dispatch_FinishedAndBusyCategories()
    {
        Card[] cards =
        [
            Card(CardFolder.Active, "010-A-run"),
            Card(CardFolder.Done, "001-B-d"),
            Card(CardFolder.Review, "002-C-r", "review: check"),
            Card(CardFolder.Queue, "003-D-q"),
        ];
        var finished = DispatchPlanner.Finished(cards);
        Assert.Equal(["001-B"], finished.Order(StringComparer.Ordinal));

        var busy = DispatchPlanner.BusyCategories([Running(1, "010-A-run")]);
        Assert.Equal(["A"], busy.Keys);
        Assert.Equal("worker 1", busy["A"]);
        Assert.True(busy.ContainsKey("a"));     // G-1: categories compare case-insensitively.
    }

    // DISP-2, DISP-8, DISP-14.
    [Fact]
    public void Dispatch_Orphan_ResumedBeforeQueuedSamePriority()
    {
        var plan = Plan([Card(CardFolder.Active, "050-A-orphan"), Card(CardFolder.Queue, "010-B-new")]);
        Assert.Equal(["050-A", "010-B"], OrderedIds(plan));
        Assert.True(plan.Ordered[0].Resume);
        Assert.False(plan.Ordered[1].Resume);
        Assert.Equal("orphan 050-A", plan.Busy["A"]);
    }

    // DISP-14.
    [Fact]
    public void Dispatch_HighPriorityQueued_BeatsNormalOrphan()
    {
        var plan = Plan([Card(CardFolder.Active, "010-A-orphan"), Card(CardFolder.Queue, "020-B-urgent", prio: 0)]);
        Assert.Equal(["020-B", "010-A"], OrderedIds(plan));
        Assert.False(plan.Ordered[0].Resume);
    }

    // DISP-2.
    [Fact]
    public void Dispatch_ActiveNeedsHuman_NotAnOrphan()
    {
        var plan = Plan([Card(CardFolder.Active, "010-A-parked", "needs-human: q"), Card(CardFolder.Queue, "020-A-next")]);
        Assert.Equal(["020-A"], OrderedIds(plan));
        Assert.False(plan.Ordered[0].Resume);
        Assert.Null(NoteOf(plan, "010-A-parked"));
    }

    // DISP-3: the running card itself is skipped silently; an orphan whose scope overlaps it
    // waits, and its note names the running card (job card 560-A, SC-2, item 7).
    [Fact]
    public void Dispatch_OrphanInBusyScope_WaitsForTheRunningCard()
    {
        var plan = Plan(
            [Card(CardFolder.Active, "020-A-running"), Card(CardFolder.Active, "010-A-orphan")],
            [Running(1, "020-A-running")]);
        Assert.Empty(plan.Ordered);
        Assert.Null(NoteOf(plan, "020-A-running"));
        Assert.Equal("waits: overlaps 020-A on area A", NoteOf(plan, "010-A-orphan"));
        Assert.Equal("worker 1", plan.Busy["A"]);
    }

    // DISP-4.
    [Fact]
    public void Dispatch_ActiveWaitingOrBlocked_NotOrphanDoesNotHoldCategory()
    {
        var plan = Plan(
        [
            Card(CardFolder.Active, "010-A-sentback", "waiting: planner"),
            Card(CardFolder.Active, "011-A-blocked", "ready", 1, "005-B"),
            Card(CardFolder.Queue, "005-B-dep", "waiting: dev"),
            Card(CardFolder.Queue, "020-A-next"),
        ]);
        Assert.Equal("claimed; waiting: planner", NoteOf(plan, "010-A-sentback"));
        Assert.Equal("claimed; waits for 005-B", NoteOf(plan, "011-A-blocked"));
        Assert.Equal(["020-A"], OrderedIds(plan));
        Assert.False(plan.Ordered[0].Resume);
    }

    // DISP-5 is gone (job card 560-A, 320-A Q5): no category is held, an orphan simply resumes.
    [Fact]
    public void Dispatch_Orphan_NeverHeld()
    {
        var plan = Plan([Card(CardFolder.Active, "010-A-orphan")]);
        Assert.Equal(["010-A"], OrderedIds(plan));
        Assert.Null(NoteOf(plan, "010-A-orphan"));
    }

    // DISP-6.
    [Fact]
    public void Dispatch_LowPriorityOrphan_WaitsBehindHigherPriorityQueued()
    {
        var plan = Plan([Card(CardFolder.Active, "010-A-scout", prio: 2), Card(CardFolder.Queue, "020-A-work")]);
        Assert.Equal(["020-A"], OrderedIds(plan));
        Assert.Equal("orphan; waits behind higher-priority 020-A", NoteOf(plan, "010-A-scout"));
    }

    // DISP-6.
    [Fact]
    public void Dispatch_LowPriorityOrphan_QueuedCardNotReadyOrBlocked_DoesNotBlock()
    {
        var notReady = Plan([Card(CardFolder.Active, "010-A-scout", prio: 2), Card(CardFolder.Queue, "020-A-work", "waiting: dev")]);
        Assert.Equal(["010-A"], OrderedIds(notReady));
        Assert.True(notReady.Ordered[0].Resume);
        Assert.Equal("waiting: dev", NoteOf(notReady, "020-A-work"));

        var blocked = Plan([Card(CardFolder.Active, "010-A-scout", prio: 2), Card(CardFolder.Queue, "020-A-work", "ready", 1, "001-B")]);
        Assert.Equal(["010-A"], OrderedIds(blocked));
        Assert.True(blocked.Ordered[0].Resume);
        Assert.Equal("waits for 001-B", NoteOf(blocked, "020-A-work"));
    }

    // DISP-6, DISP-11.
    // Spec C3: today's behaviour, kept until the developer decides (003-D review).
    [Fact]
    public void Dispatch_LowPriorityOrphan_BehindStuckQueued()
    {
        var plan = Plan(
            [Card(CardFolder.Active, "010-A-scout", prio: 2), Card(CardFolder.Queue, "020-A-stuck")],
            retries: new Dictionary<string, int> { ["020-A"] = 2 });
        Assert.Empty(plan.Candidates);
        Assert.Equal("orphan; waits behind higher-priority 020-A", NoteOf(plan, "010-A-scout"));
        Assert.Equal("stuck: 2 runs ended without a result: needs you", NoteOf(plan, "020-A-stuck"));
    }

    // DISP-6, DISP-9 (1082-A): a stale queue/ copy of the orphan itself, of higher priority, is never a
    // candidate; the orphan does not wait behind it for ever.
    [Fact]
    public void Dispatch_LowPriorityOrphan_StaleQueueCopyOfItself_DoesNotBlock()
    {
        var plan = Plan([Card(CardFolder.Active, "010-A-scout", prio: 2), Card(CardFolder.Queue, "010-A-scout", prio: 1)]);
        Assert.Equal(["010-A"], OrderedIds(plan));
        Assert.True(plan.Ordered[0].Resume);
        Assert.Null(NoteOf(plan, "010-A-scout"));
    }

    // DISP-6, DISP-9 (1082-A): nor behind a stale queue/ copy of a card already in done/.
    [Fact]
    public void Dispatch_LowPriorityOrphan_StaleQueueCopyOfDoneCard_DoesNotBlock()
    {
        var plan = Plan(
            [Card(CardFolder.Active, "010-A-scout", prio: 2), Card(CardFolder.Done, "020-A-work"), Card(CardFolder.Queue, "020-A-work", prio: 1)]);
        Assert.Equal(["010-A"], OrderedIds(plan));
        Assert.True(plan.Ordered[0].Resume);
    }

    // DISP-7 before DISP-6 (1082-A): a stuck orphan behind a higher-priority queued card is listed as
    // stuck (Waiting on you), not only noted as waiting behind it.
    [Fact]
    public void Dispatch_StuckLowPriorityOrphan_BehindHigherPriorityQueued_ListedAsStuck()
    {
        var plan = Plan(
            [Card(CardFolder.Active, "010-A-scout", prio: 2), Card(CardFolder.Queue, "020-A-work")],
            retries: new Dictionary<string, int> { ["010-A"] = 2 });
        Assert.Equal(["010-A"], plan.Stuck.Select(c => c.Id));
        Assert.Equal("stuck: 2 runs ended without a result: needs you", NoteOf(plan, "010-A-scout"));
        Assert.Equal(["020-A"], OrderedIds(plan));
    }

    // DISP-7.
    [Fact]
    public void Dispatch_StuckOrphan_NotResumedListedWithRetry()
    {
        var plan = Plan(
            [Card(CardFolder.Active, "010-A-orphan"), Card(CardFolder.Queue, "020-A-next")],
            retries: new Dictionary<string, int> { ["010-A"] = 2 });
        Assert.Equal("stuck: 2 runs ended without a result: needs you", NoteOf(plan, "010-A-orphan"));
        Assert.Equal(["010-A"], plan.Stuck.Select(c => c.Id));
        Assert.Equal(["020-A"], OrderedIds(plan));
        Assert.False(plan.Ordered[0].Resume);
    }

    // DISP-7, DISP-16.
    [Fact]
    public void Dispatch_OrphanRetriesOneBelowMax_Resumed()
    {
        var retries = new Dictionary<string, int> { ["010-A"] = 1 };
        var plan = Plan([Card(CardFolder.Active, "010-A-orphan")], retries: retries);
        Assert.Equal(["010-A"], OrderedIds(plan));
        Assert.True(plan.Ordered[0].Resume);
        Assert.Empty(plan.Stuck);

        // L2927: the count goes up before Start-Worker runs, so a failed start still counts.
        DispatchPlanner.CountResume(retries, plan.Ordered[0]);
        Assert.Equal(2, retries["010-A"]);

        // A new card's start does not count; a resume with no count yet starts from 0.
        var fresh = new Dictionary<string, int>();
        DispatchPlanner.CountResume(fresh, new DispatchCandidate(Card(CardFolder.Queue, "020-B-q"), false));
        Assert.Empty(fresh);
        DispatchPlanner.CountResume(fresh, new DispatchCandidate(Card(CardFolder.Active, "030-C-o"), true));
        Assert.Equal(1, fresh["030-C"]);
    }

    // DISP-9, DISP-13.
    [Fact]
    public void Dispatch_QueuedOrder_PrioNumCategory()
    {
        var plan = Plan(
        [
            Card(CardFolder.Queue, "020-B-b"),
            Card(CardFolder.Queue, "010-C-c", prio: 2),
            Card(CardFolder.Queue, "010-A-a"),
            Card(CardFolder.Queue, "015-D-d", prio: 0),
        ]);
        Assert.Equal(["015-D", "010-A", "020-B", "010-C"], OrderedIds(plan));
        Assert.Empty(plan.Notes);

        // Same Prio and Num: Category breaks the tie (L2901).
        var tie = Plan([Card(CardFolder.Queue, "010-B-b"), Card(CardFolder.Queue, "010-A-a")]);
        Assert.Equal(["010-A", "010-B"], tie.Candidates.Select(c => c.Card.Id));

        // A stale queue/ copy of a card that is also in another folder is skipped silently (L2902).
        var stale = Plan([Card(CardFolder.Done, "010-A-a"), Card(CardFolder.Queue, "010-A-a")]);
        Assert.Empty(stale.Candidates);
        Assert.Empty(stale.Notes);
    }

    // DISP-10.
    [Fact]
    public void Dispatch_QueuedNotReady_NoteIsStatus()
    {
        var plan = Plan([Card(CardFolder.Queue, "010-A-hw", "blocked on hardware")]);
        Assert.Empty(plan.Candidates);
        Assert.Equal("blocked on hardware", NoteOf(plan, "010-A-hw"));
    }

    // DISP-11.
    [Fact]
    public void Dispatch_QueuedStuck_RetriesAtMax()
    {
        var plan = Plan([Card(CardFolder.Queue, "010-A-x")], retries: new Dictionary<string, int> { ["010-A"] = 2 });
        Assert.Empty(plan.Candidates);
        Assert.Equal("stuck: 2 runs ended without a result: needs you", NoteOf(plan, "010-A-x"));
        Assert.Equal(["010-A"], plan.Stuck.Select(c => c.Id));
    }

    // DISP-11, TIME-17 · 940-A (closes spec C2): a queued card whose runs keep crashing is
    // dispatched maxRetries times and then sticks, so one start per tick cannot go on for ever.
    [Fact]
    public void Dispatch_QueuedCrashLoop_DispatchedMaxRetriesTimesThenStuck()
    {
        var card = Card(CardFolder.Queue, "010-A-crash");
        var retries = new Dictionary<string, int>();
        for (var run = 1; run <= MaxRetries; run++)
        {
            var plan = Plan([card], retries: retries);
            Assert.Equal(["010-A"], OrderedIds(plan));
            Assert.False(plan.Ordered[0].Resume);
            retries = End(Running(1, "010-A-crash"), ["no result"], null, 1, retries);   // crashed (exit 1)
            Assert.Equal(run, retries["010-A"]);
        }

        var stuck = Plan([card], retries: retries);
        Assert.Empty(stuck.Candidates);
        Assert.Empty(stuck.Ordered);
        Assert.Equal(["010-A"], stuck.Stuck.Select(c => c.Id));
        Assert.Equal("stuck: 2 runs ended without a result: needs you", NoteOf(stuck, "010-A-crash"));
    }

    // DISP-11, TIME-15 and TIME-16 · 940-A: runs the card is not to blame for never add up, so a
    // card is not stuck by an afternoon with the model server down.
    [Fact]
    public void Dispatch_QueuedQuotaAndServerDownRuns_KeptDispatching()
    {
        var card = Card(CardFolder.Queue, "010-A-crash");
        var retries = new Dictionary<string, int>();
        for (var run = 1; run <= 5; run++)
        {
            var plan = Plan([card], retries: retries);
            Assert.Equal(["010-A"], OrderedIds(plan));
            Assert.Empty(plan.Stuck);
            retries = run % 2 == 0
                ? End(Running(1, "010-A-crash"), ["no result"], null, null, retries, serverDown: true)
                : End(Running(1, "010-A-crash"), ["no result"], ["Error: quota exceeded"], null, retries);
        }

        Assert.Empty(retries);
    }

    // DISP-12.
    [Fact]
    public void Dispatch_QueuedChecksInOrder_DepsThenScope()
    {
        Card running = Card(CardFolder.Active, "005-A-run");
        Card withDep = Card(CardFolder.Queue, "010-A-x", "ready", 1, "001-B");
        Card noDep = Card(CardFolder.Queue, "010-A-x");

        var deps = Plan([running, withDep], [Running(1, "005-A-run")]);
        Assert.Equal("waits for 001-B", NoteOf(deps, "010-A-x"));

        // SC-2, 560-A item 7: the note names the running card whose scope it overlaps.
        var busy = Plan([running, noDep], [Running(1, "005-A-run")]);
        Assert.Equal("waits: overlaps 005-A on area A", NoteOf(busy, "010-A-x"));
        Assert.Empty(busy.Ordered);
    }

    // DISP-13.
    [Fact]
    public void Dispatch_OneCandidatePerCategoryPerTick()
    {
        var plan = Plan([Card(CardFolder.Queue, "020-A-second"), Card(CardFolder.Queue, "010-A-first")]);
        Assert.Equal(["010-A"], OrderedIds(plan));
        Assert.Equal("waits: overlaps 010-A on area A", NoteOf(plan, "020-A-second"));   // SC-3, 560-A item 7
        Assert.Equal("next: 010-A", plan.Busy["A"]);
    }
}
