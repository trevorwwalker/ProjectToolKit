# Test plan: dispatch and categories

Rules: DISP-1 to DISP-21, LANE-1 to LANE-5. Source: `scheduler-next/spec.md` A3 and A4;
`scheduler/schedule.ps1` dispatch L2867-2945 where the spec leaves the result open.
Dispatch decisions are Core: given cards, running workers, slots, retries, held categories, server
states and the scheduler state, return the starts and the notes. Starting a process is Host.

Shared setup unless a test says otherwise: maxRetries 2, slots 1-2 enabled with categories '' (all),
servers up, state `running`.

## Selection

### Dispatch_EmptyQueue_NothingStartedNoNotes
- Checks: DISP-9, DISP-14 · Project: Core
- Setup: no cards at all; then only done/ and review/ cards.
- Action: one tick.
- Expected: no start, no notes, no error; status file lists every slot idle.

### Dispatch_AllWorkersBusy_CandidateWaitsForFreeWorker
- **Retired (card 960-D).** The slot dispatcher is gone (939-A); replaced by TypeDispatch_TotalReached_NothingStarts_NoteSaysNofM and TypeDispatch_Notes_EachReasonInOrder (TypeDispatchTests.cs). Kept for the history.
- Checks: DISP-15, DISP-19 · Project: Core
- Setup: both slots running cards of categories A and B; queue card 030-C ready.
- Action: one tick.
- Expected: no start; note "next up; waiting for a free worker". Category C counts as taken for this tick.

### Dispatch_AllWorkersBusy_ThenOneFinishes_StartsNextTick
- Checks: DISP-15, TIME-21 · Project: Core
- Setup: as above; slot 2's worker finishes.
- Action: next tick.
- Expected: 030-C starts on slot 2.

### Dispatch_FinishedAndBusyLanes
- Checks: DISP-1 · Project: Core
- Setup: running worker on 010-A; cards in done/ and review/.
- Expected: finished set = done+review ids; busy categories = {A}.

### Dispatch_Orphan_ResumedBeforeQueuedSamePriority
- Checks: DISP-2, DISP-8, DISP-14 · Project: Core
- Setup: active 050-A `Status: ready` (no worker); queue 010-B ready; one free slot.
- Expected: 050-A starts as a resume (orphans before new cards within a priority).

### Dispatch_HighPriorityQueued_BeatsNormalOrphan
- Checks: DISP-14 · Project: Core
- Setup: active orphan 010-A Prio 1; queue 020-B `Priority: high`; one free slot.
- Expected: 020-B starts.

### Dispatch_ActiveNeedsHuman_NotAnOrphan
- Checks: DISP-2 · Project: Core
- Setup: active 010-A `Status: needs-human: q`.
- Expected: not resumed; category A free for a queued A card.

### Dispatch_OrphanInBusyLane_SkippedSilently
- Checks: DISP-3 · Project: Core
- Setup: running worker on 020-A; active orphan 010-A whose scope overlaps 020-A's (card 560-A: scopes, not categories).
- Expected: 010-A not started and has no note. With scopes that do not overlap it is resumed.

### Dispatch_ActiveWaitingOrBlocked_NotOrphanDoesNotHoldLane
- Checks: DISP-4 · Project: Core
- Setup: active 010-A `Status: waiting: planner`; active 011-A ready depending on unfinished 005-B; queue 020-A ready.
- Expected: notes "claimed; waiting: planner" and "claimed; waits for 005-B"; 020-A starts.

### Dispatch_OrphanInHeldLane_NotResumed
- Checks: DISP-5 · Project: Core · **retired by card 560-A (DISP-5 is gone; LANE-2 never holds a category)**
- Was: category A held; active orphan 010-A not started, note "orphan; category A is held (redefined): needs you".
- Now: replaced by `Lanes_Redefined_AcceptedAtOnce` below.

### Dispatch_LowPriorityOrphan_WaitsBehindHigherPriorityQueued
- Checks: DISP-6 · Project: Core
- Setup: active 010-A `Priority: low`; queue 020-A Prio 1, ready, no deps, scope overlapping 010-A's (560-A; was: same category). A 020-A with a disjoint scope does not hold 010-A back.
- Expected: 020-A starts; note on 010-A "orphan; waits behind higher-priority 020-A".

### Dispatch_LowPriorityOrphan_QueuedCardNotReadyOrBlocked_DoesNotBlock
- Checks: DISP-6 · Project: Core
- Setup: as above but 020-A `Status: waiting: dev`; second case 020-A depends on unfinished 001-B.
- Expected: 010-A resumes in both cases.

### Dispatch_LowPriorityOrphan_BehindStuckQueued
- Checks: DISP-6, DISP-11 · Project: Core · **blocked on C3**
- Setup: active 010-A low; queue 020-A Prio 1 ready with retries 2 (stuck).
- Expected today: 010-A waits for ever (L2886). Expected after C3: to be written.

### Dispatch_StuckOrphan_NotResumedListedWithRetry
- Checks: DISP-7 · Project: Core
- Setup: active orphan 010-A with retries 2; queue 020-A ready.
- Expected: 010-A not resumed and listed under Waiting on you with Retry; note "stuck: 3 runs ended without a result: needs you" (n is the real count); category A not held, 020-A starts.

### Dispatch_QueuedCrashLoop_DispatchedMaxRetriesTimesThenStuck
- Checks: DISP-11, TIME-17 · Project: Core
- Setup: queued card whose every run crashes.
- Expected: dispatched maxRetries times, then stuck with the DISP-7 note.

### Dispatch_QueuedQuotaAndServerDownRuns_KeptDispatching
- Checks: DISP-11, TIME-17 · Project: Core
- Setup: queued card whose runs end `out of quota` or `model server down`.
- Expected: not counted; the card is never stuck by them.

### Dispatch_OrphanRetriesOneBelowMax_Resumed
- DISP-16 is also checked by `TypeDispatch_ResumeCountsRetryBeforeStart` (moved by 939-A).
- Checks: DISP-7, DISP-16 · Project: Core
- Setup: orphan with retries 1.
- Expected: resumed; retries becomes 2 before the start (L2927), even if the start then fails.

### Dispatch_QueuedOrder_PrioNumLane
- Checks: DISP-9, DISP-13 · Project: Core
- Setup: queue 020-B, 010-C low, 010-A, 015-D high; four free slots.
- Expected: start order 015-D, 010-A, 020-B, 010-C.

### Dispatch_QueuedNotReady_NoteIsStatus
- Checks: DISP-10 · Project: Core
- Setup: queue card `Status: blocked on hardware`.
- Expected: not a candidate; note `blocked on hardware`.

### Dispatch_QueuedStuck_RetriesAtMax
- Checks: DISP-11 · Project: Core
- Setup: queue 010-A ready with retries 2.
- Expected: not started; stuck, note "2 runs failed to start: needs you".

### Dispatch_QueuedChecksInOrder_DepsThenBusyThenHeld
- Checks: DISP-12 · Project: Core
- Setup: queue 010-A with an unfinished dep, and a running card whose scope overlaps 010-A's (card 560-A).
- Expected: note "waits for <dep>". Remove the dep: "waits: overlaps <running card> on <path>". Stop the running worker: 010-A starts. (The held-category step is gone.)

### Dispatch_OverlappingCandidates_OnlyFirstStartsPerTick
- Checks: DISP-13, DISP-22 · Project: Core
- Setup: queue 010-A and 020-A ready with overlapping scopes; two free slots.
- Expected: only 010-A starts; 020-A note "waits: overlaps 010-A on <path>". With disjoint scopes both start.

### Dispatch_NotRunning_NoStartsNoteSaysState
- **Retired (card 960-D).** The slot dispatcher is gone (939-A); replaced by TypeDispatch_Notes_EachReasonInOrder (TypeDispatchTests.cs). Kept for the history.
- Checks: DISP-14, DISP-19 · Project: Core
- Setup: state `paused`, then `paused-red`, `paused-quota`, `draining`; one ready card.
- Expected: no start; note "next up; dispatching is <state>".

### Dispatch_LowestFittingSlot
- **Retired (card 960-D).** The slot dispatcher is gone (939-A); replaced by TypeDispatch_SeveralTypesUsable_FirstListedTakesIt (TypeDispatchTests.cs). Kept for the history.
- Checks: DISP-15 · Project: Core
- Setup: slots 1 (disabled), 2 (categories `B`), 3 (server down), 4 (fits), 5 (fits); card 010-A.
- Expected: starts on slot 4.

### Dispatch_NoSlotFits_LaneStaysTaken
- **Retired (card 960-D).** The slot dispatcher is gone (939-A); replaced by TypeDispatch_CardAsksNamedWorker_FullTypeNamesItsCard (TypeDispatchTests.cs). Kept for the history.
- Checks: DISP-15 · Project: Core · **blocked on C4**
- Setup: queue 010-A `Worker: opus` (no opus worker free), 020-A ready with no Worker line; one free slot.
- Expected today: neither starts (category A taken by 010-A). Expected after C4: to be written.

### Dispatch_ServerUp_CachedFor20Seconds
- Moved by 939-A to `Dispatch/ServerCacheTests.cs`, same name.
- Checks: DISP-15, ACCT-8 · Project: Core
- Setup: fake clock and fake HTTP; server down at t=0, up at t=5.
- Expected: at t=10 still treated as down (cached); at t=20 up.

### Dispatch_LaneAllowed
- **Retired (card 960-D).** The slot dispatcher is gone (939-A); replaced by TypeDispatch_AreaNotInTypesAreas_DoesNotFit (TypeDispatchTests.cs). Kept for the history.
- Checks: DISP-17 · Project: Core
- Cases: worker categories '' -> any category allowed; `AB` -> A yes, C no; `ab` from workers.json -> upper-cased, A yes.

### Dispatch_WorkerFits_Rules
- **Retired (card 960-D).** The slot dispatcher is gone (939-A); replaced by TypeDispatch_NoWorkerLine_OnlyDefault, TypeDispatch_WorkerPremium_IsUnknownName_NotDispatched (the premium tests of 960-D were superseded 2026-11-23 by 1146-A), TypeDispatch_WorkerName_ModelOrShort_PicksThatType and TypeDispatch_WorkerSlotNumber_MatchesMigratedTypeOnly (TypeDispatchTests.cs). Kept for the history.
- Checks: DISP-18 · Project: Core
- Setup (as of 960-D, when premium existed): slot 1 non-premium model `qwen3-32b`; slot 2 premium named `opus` with model `claude-opus-5-5`; models.json gives `claude-opus-5-5` short `Opus`.
- Cases: no Worker line -> slot 1 only; `Worker: premium` -> slot 2 only; `Worker: 2`, `Worker: opus`, `Worker: OPUS`, `Worker: claude-opus-5-5` -> slot 2; `Worker: qwen3-32b` -> slot 1; `Worker: gpt` -> none.

### Dispatch_CardPremiumFlag_Unused
- **Retired (card 960-D).** The slot dispatcher is gone (939-A); replaced by the WT-3 tests (TypeDispatchTests.cs). Kept for the history.
- Checks: CARD-10, DISP-18 · Project: Core · **blocked on C14**
- Setup: card `Worker: qwen3-32b (any)`; only a qwen worker.
- Expected today: it fits (Card.Premium is set by any Worker line, L625, but never read). After C14: drop the field, or define what it means.

### Dispatch_NotStartedNotes_AllVariants
- **Superseded by the WT rules** (worker-types.md); removed with the slot dispatcher (939-A).
- Checks: DISP-19 · Project: Core
- Cases: no worker anywhere fits the Worker request -> "next up; asks for worker X, and no worker fits (workers.json)"; a fitting worker exists but is busy -> "next up; waiting for worker X"; a server down -> "next up; waiting for a free worker (a model server does not answer)"; otherwise "next up; waiting for a free worker"; any of these for an orphan gets the prefix "orphan to resume; ".

### Dispatch_LocksLineIgnored
- Checks: DISP-20 · Project: Core
- Setup: running 010-A with `Locks: src/x`; queue 020-B with `Locks: src/x`.
- Expected today (card 560-A reversed DISP-20): 020-B does **not** start; note "waits: overlaps 010-A on src/x". The old expectation (locks ignored) is retired; the scope tests are in scopes.md.
- Name kept in the index; rename to `Dispatch_LocksLine_InScope` when the test is next touched.

### Dispatch_DryRun_LogsAndExitsAfterFirstTick
- Checks: DISP-21 · Project: Host
- Setup: `-DryRun`, one ready card.
- Expected: log "DRY RUN slot 1 <- 010-A-x"; no process started, no slot prepared; the loop exits after one tick.

### Dispatch_StartWorkerFails_CardNotRestartedSameTick
- Moved by 939-A: now `TypeDispatch_StartWorkerFails_CardNotTriedAgainButTheNextIs` in `Dispatch/TypeDispatchTests.cs`.
- Checks: DISP-11, DISP-15 · Project: Core
- Setup: Start-Worker fails (worktree cannot be created) for 010-A; two free slots.
- Expected: 010-A is not tried on the second slot in the same tick (L2930 marks it started); slot stays idle.

## Categories

### Lanes_ReadFromMain
- Checks: LANE-1 · Project: Core
- Setup: categories.json with categories A (owns, except, alsoOwns) and shared addOnly/rerecord/rerecordRule.
- Expected: all fields parsed.

### Lanes_InvalidJsonOrNoLanesKey_PreviousKept
- Checks: LANE-1 · Project: Core
- Setup: a good read; then invalid JSON; then `{}`.
- Expected: previous categories kept both times; the invalid JSON is logged, the missing key is not.

### Lanes_NewLane_Accepted
- Checks: LANE-2 · Project: Host
- Setup: lanes-accepted.json without category E; categories.json adds E.
- Expected: E accepted and stored; not held.

### Lanes_ChangedNoPendingCards_AcceptedAndLogged
- Checks: LANE-2 · Project: Host
- Setup: category A's owns changes; no A cards in queue/ or active/.
- Expected: accepted, logged, stored.

### Lanes_ChangedWithPendingCards_HeldWithUpToSixIds
- Checks: LANE-2 · Project: Core · **retired by card 560-A (nothing is held)**
- Setup: category A changes; 8 A cards in queue/active.
- Expected: held; reason lists 6 ids; logged once over several ticks.

### Lanes_SameDefinitionDifferentFormatting_NotAChange
- Checks: LANE-2 · Project: Core
- Setup: categories.json re-indented, same content.
- Expected: not held (compared as compact JSON).

### Lanes_AcceptFromPage_ReleasesLane
- Checks: LANE-3 · Project: Host, Web · **dead since 560-A: no category is held, so there is nothing to release (the route only re-stores a definition)**
- Setup: category A held.
- Action: POST `/accept-lane` for A.
- Expected: definition stored; category free; its cards dispatchable next tick.

### Lanes_Overlaps_Warnings
- Checks: LANE-4 · Project: Core
- Cases: A and B both own `src/x.cs` -> warning; A owns `src/`, B owns `src/a/*` -> warning; same with A `except: ["src/a/"]` -> none.

### Lanes_Overlaps_GlobOwnsNotSeenAsFolder
- Checks: LANE-4, DISP-25 · Project: Core · lands with card 977-A (until then the code gives no warning: known gap C12)
- Setup: A owns `vm/68000.*`, B owns `vm/68000.cpp`; then A owns `vm/`, B owns `vm/*.h`; then A owns `src/*`, B owns `src/x/`.
- Expected: a warning for the first two pairs, none for the third; disjoint patterns (`vm/*.h`, `vm/*.cpp`) give none; an `except` that provably covers one side removes the warning.

### Lanes_Brief_InPrompt
- Checks: LANE-5 · Project: Core
- Setup: category D owns two paths, except one, alsoOwns one; shared addOnly and rerecord lists.
- Expected: brief text names owns, "but not" the except, alsoOwns, the add-only and re-record lists, and "All categories are defined in docs/jobs/categories.json".

### Lanes_Redefined_AcceptedAtOnce
- Checks: LANE-2, AREA-2 · Project: Host, Web · card 560-A (developer, Q1) · tests: `LaneAcceptTests`, `PageSnapshotTests` (Host.Tests)
- Setup: category A changes while 8 A cards are in queue/active.
- Expected: the new definition is accepted and stored at once; no Waiting on you row and no Accept button; the A cards stay dispatchable.

### Dispatch_LocksPathsAreScope_AndAreaDefault
- Checks: DISP-22, DISP-23, DISP-24 · Project: Core · card 560-A · tests: `Scheduler.Core.Tests/Scopes`
- Cases: a card with only a `Locks:` line (backticks and notes in parentheses ignored); a card with no Scope and no Locks takes its category's scope; with categories.json read and no category entry it is not started; with no categories.json read it takes "category A" and one card per category runs; paths compare case-sensitively; an add-only overlap is allowed when categories.json lists the path or both cards mark it, not when only one does.
- Expected: start or "waits: overlaps <card> on <path>" accordingly.

### Dispatch_NotStartedNote_NamesWhatStopsEachFittingSlot
- **Superseded by the WT rules** (TypeDispatch_Notes_EachReasonInOrder, TypeDispatch_FullType_NoteNamesHeldCards in worker-types.md); removed with the slot dispatcher (939-A).
- Checks: DISP-19 · Project: Core
- Setup: card `Worker: Opus`; slots 1 (disabled), 2 (category not allowed), 3 (server down), 4 (busy running 530-D), 5 (does not fit the line).
- Expected: the note lists slots 1 to 4 as "disabled", "category not allowed", "server down", "busy with 530-D"; slot 5 is not listed. With the running card not given, slot 4 reads "busy". A busy slot with its server down reads "server down".
