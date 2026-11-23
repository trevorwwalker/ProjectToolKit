# Test plan: dynamic file locks

Rules: DL-1 to DL-27 (spec B2.1; design `dynamic-locks.md`, 994-D, amended by 1018-D). Written from the design's
section 7 and the three amendments of 1018-D: the pool page, claims that land in the background (995-B), spark
workers. Core tests are pure (no disk, no git, no clock); Host tests use a temporary repository and a bare remote;
Web tests render from a model. Nothing here is built yet: the names are the intended xUnit methods, one root cause
each. The cards that build them are 1016-A to 1023-D.

## Grant (Core: LockArbiter, LockTable)

### Lock_FreePath_Granted
- Checks: DL-4, DL-5 · Project: Core
- Setup: card A holds `src/a/`; A asks for `docs/x.md`; no other holder.
- Expected: granted; the table's held set for A gains `docs/x.md` with the grant time.

### Lock_PathInsideOwnHeldSet_GrantedNoTableChange
- Checks: DL-4 · Project: Core
- Setup: A holds `src/a/`; A asks for `src/a/b.cs`.
- Expected: granted; the table is unchanged.

### Lock_OneOfTwoEntriesHeld_NeitherGranted
- Checks: DL-5 · Project: Core
- Setup: B holds `x/1.cs`; A asks for `x/1.cs` and `y/2.cs`.
- Expected: no entry granted (waiting or yield by age, DL-12); A's held set unchanged.

### Lock_GlobVsFile_Conflicts
- Checks: DL-5, SC-8 · Project: Core
- Setup: B holds `vm/68000.cpp`; A asks for `vm/68000.*`; then the reverse.
- Expected: both ways round conflict.

### Lock_DifferentRepositories_NeverConflict
- Checks: DL-22 · Project: Core
- Setup: B holds `src/a.cs` in repository R1; A (repository R2) asks for `src/a.cs`.
- Expected: granted. Two projects in one repository do conflict.

### Lock_AddOnlyPath_NeverConflicts
- Checks: DL-15, SC-5 · Project: Core
- Setup: B holds `docs/known_gaps.md` as an add-only path; A asks for it.
- Expected: granted without waiting; the `<Version>` file the same.

### Lock_CardNotInSlot_Refused
- Checks: DL-22 · Project: Core
- Setup: a request names a card that is not the running entry of the slot that wrote it.
- Expected: refused: unknown card; the table is unchanged.

### Lock_TwoRequestsOneCard_MergedBehindFirst
- Checks: DL-4 (design section 5) · Project: Core
- Setup: A has an open request; a second request from A arrives.
- Expected: answered waiting and merged; one open request per card.

## Narrowness

### Lock_TooBroad_Refused
- Checks: DL-6 · Project: Core
- Setup: asks for `**`; for the repository root; for a category's whole default scope; for a folder of over 200
  tracked files; for a folder of exactly 200.
- Expected: the first four refused with "too broad: ask for the files or the folder you need"; the last granted.
  The request is never narrowed by the scheduler.

## Wait-die

### Lock_OlderRequester_Waits
- Checks: DL-11, DL-12 · Project: Core
- Setup: A (older) asks for a path held by B (younger).
- Expected: waiting, naming the holder and the give-up time; the request stays open.

### Lock_YoungerRequester_Yields
- Checks: DL-12 · Project: Core
- Setup: A (younger) asks for a path held by B.
- Expected: `yield` naming the path and B; A's held set released; its remembered set holds what it held and what it
  asked for.

### Lock_AgeOrder_HighBeatsNormalThenLowerNumber
- Checks: DL-11 · Project: Core
- Setup: high priority card 990 vs normal card 100; two normal cards 100 and 101.
- Expected: 990 is older than 100; 100 is older than 101.

### Lock_AgeFixedAtFirstClaim_SurvivesPriorityChange
- Checks: DL-11, DL-1 · Project: Core
- Setup: a card is claimed as normal, yields, its priority line is changed to high, it starts again.
- Expected: its age is the one stored at its first claim.

### Lock_WaitingRequest_GrantedWhenHolderReleases
- Checks: DL-12, DL-8 · Project: Core
- Setup: A waits on B's path; B's finish is applied.
- Expected: in the same tick, before dispatch, A is granted.

### Lock_ConflictWithSeveralHolders_YoungerThanAnyYields
- Checks: DL-12 · Project: Core
- Setup: A is older than B but younger than C; the entry overlaps both.
- Expected: yield (younger than any conflicting holder).

## Wait limit

### Lock_WaitLimit_TurnsToYield
- Checks: DL-13 · Project: Core
- Setup: an older card waits; the clock passes `lockWaitMinutes` (20 by default; 5 when set so).
- Expected: the answer turns to `yield`; the younger holder is never stopped.

### Lock_WaitLimit_NeverPastWarnTime
- Checks: DL-13 · Project: Core
- Setup: the warn time comes in 8 minutes; the limit is 20.
- Expected: the answer turns to `yield` at the warn time.

## Release

### Lock_FinishApplied_ReleasedAndWaiterGranted
- Checks: DL-8 a, DL-9 · Project: Core
- Setup: B's To: done applied on main (CM-10); A waits on B.
- Expected: released and A granted in that tick; B's remembered set dropped (DL-10).

### Lock_FinishNotPushed_StillHeld
- Checks: DL-9 · Project: Core
- Setup: B's finish left for the next tick (CM-18).
- Expected: B's locks held; A still waiting.

### Lock_ParkSendBackFailKill_ReleasedRemembered
- Checks: DL-8 b to d, DL-10 · Project: Core
- Setup: B granted `docs/z.md` beyond its claim; each of park (To: active), send-back (To: queue), failed, killed,
  timed out, stopped.
- Expected: released each time; `docs/z.md` is B's remembered set.

### Lock_ParkedCard_RememberedSetNotHeld
- Checks: DL-10 · Project: Core
- Setup: B is needs-human with a remembered set; C asks for that path.
- Expected: C is granted; B gets it back only when dispatch finds it free.

## Resume

### Lock_YieldedCard_NotOfferedWhileRememberedEntryHeld
- Checks: DL-10, DL-1 · Project: Core (ScopeGate)
- Setup: A yielded asking for `x/1.cs`; B still holds it.
- Expected: A is not started; the note names B. After B releases, A is offered.

### Lock_Resume_InitialClaimIncludesRememberedSet
- Checks: DL-1, DL-10 · Project: Core
- Setup: a card with a remembered set starts.
- Expected: its held set is its scope plus the remembered set.

### Lock_Dispatch_UsesHeldSetsNotDeclaredScopes
- Checks: DL-1, DL-7 · Project: Core
- Setup: B declares `src/a/`, was granted `docs/x.md`; a queued card's scope is `docs/x.md`.
- Expected: not started, the note names B; a grant never reserves for a queued card.

## Claims land in the background (995-B)

### Lock_StartingCard_CountsAgainstCandidates_NoHeldSet
- Checks: DL-1 (1018-D) · Project: Core
- Setup: card A is starting (claim posted, not landed); a candidate overlaps A's scope; another asks for a lock.
- Expected: the candidate is held back by A's scope; the table has A as starting, with no held set and no age.

### Lock_ClaimLanded_RecordsHeldSetAndAge
- Checks: DL-1, DL-11 (1018-D) · Project: Core
- Setup: A is starting, then its claim lands (`ClaimState.Landed`).
- Expected: A's held set (scope plus remembered set) and age are recorded now, not when the tick posted the claim.

### Lock_StartingCard_CannotRequestLock
- Checks: DL-1 (1018-D) · Project: Core
- Setup: a request arrives from a slot whose card is still starting.
- Expected: refused: unknown card (DL-22); the table is unchanged.

### Lock_ClaimFailed_EntryDropped
- Checks: DL-1 (1018-D) · Project: Core
- Setup: A is starting; its claim fails (`ClaimState.Failed`).
- Expected: the starting entry is gone; A was never held; nothing is remembered.

## Results

### Lock_Yielded_GivesRunBack_NotStuck
- Checks: DL-14 · Project: Core
- Setup: a run ends `LANE-RESULT: yielded` with its request applied; one with runs at 0; one without the move applied.
- Expected: the run is given back (never below 0, C8) and the stuck count is not raised (C2); without the move
  applied it is an ordinary result of its kind.

### Lock_YieldedCardInActive_IsOrphanFirstAmongItsPriority
- Checks: DL-14, DISP-14 · Project: Core
- Expected: resumed before a queued card of the same priority.

## Push check (Core: PushCheck)

### PushCheck_ExemptPaths_Allowed
- Checks: DL-15 · Project: Core
- Setup: the pushed range changes the card's own card file, a new file in `jobs/requests/`, a shared add-only file.
- Expected: allowed.

### PushCheck_OtherJobsPaths_Refused
- Checks: DL-15 · Project: Core
- Setup: another card's file, a card in `jobs/done/`, `jobs/plans/x.md`, `jobs/.logs/x`, `jobs/requests/answered/x.md`, each with `jobs/` held (developer, 2026-11-21).
- Expected: refused even when held, each path named with the rule. A held `jobs/card-template.md`, `jobs/attachments/x.md` or `jobs/README.md` is allowed; unheld, refused with the lock command. The audit agrees (DL-19).

### PushCheck_HeldPath_Allowed_UnheldRefusedWithCommand
- Checks: DL-18 · Project: Core
- Expected: a held path passes; an unheld one is refused naming the path and `<exe> lock <path> --why ...`.

### PushCheck_WipRef_NeverRefused
- Checks: DL-18 · Project: Core
- Expected: a push to `wip/<card>` with unheld paths is allowed.

### PushCheck_Version_LowerOrEqualRefused
- Checks: DL-17 · Project: Core
- Setup: main has 1.4.7; the range sets 1.4.7; 1.4.6; 1.4.8.
- Expected: the first two refused "bump again from main's version"; the last allowed.

### PushCheck_AddOnlyFileWithDeletedLines_Warning
- Checks: DL-16 · Project: Core
- Setup: lines deleted from `docs/known_gaps.md`; a deleted `<Version>` line only.
- Expected: a warning for the first, not a refusal; none for the second.

## Audit of main (Core: MainAudit)

### Audit_TrailerAndHeldPaths_Clean
- Checks: DL-19 · Project: Core
- Expected: no stray.

### Audit_UnheldPath_Stray
- Checks: DL-19 · Project: Core
- Setup: the path was not in the card's held set at the time the commit landed (but is in it now).
- Expected: a stray.

### Audit_NoTrailerWhileWorkersRan_Stray
- Checks: DL-19 · Project: Core
- Expected: a stray; with no workers running, none.

### Audit_SchedulersOwnCommits_Ignored
- Checks: DL-19 · Project: Core
- Setup: `jobs: start`, `jobs: done`, planner batches, page actions.
- Expected: not checked.

### Audit_StrayTurnsDoneIntoReview
- Checks: DL-19, CM-5 · Project: Core (CardMoves)
- Setup: a To: done request for a card with a stray.
- Expected: applied as To: review, Status `review: changed <path> without its lock`; nothing reverted.

## Table (Core: LockTableText; Host)

### Table_RoundTrip
- Checks: DL-20 · Project: Core
- Expected: ages, held entries with grant times, remembered sets, open requests with deadlines and the audited
  commit survive write then read.

### Table_Unreadable_StartsEmptyWithLogLine
- Checks: DL-21 · Project: Core
- Expected: an empty table, running cards fall back to their initial claims, the audit begins at main's head, one
  log line says so.

### Table_Restart_KeepsAdoptedReleasesRest
- Checks: DL-21, ADOPT-2 · Project: Core
- Setup: A adopted with an open request (deadline 10:42); B not adopted.
- Expected: A keeps its held set and deadline; B released, its granted entries remembered.

### Table_Restart_StartingCardReleasedNothingRemembered
- Checks: DL-21 (1018-D) · Project: Core
- Setup: a card was starting (claim posted, not landed) at the restart; once with the claim on main, once not.
- Expected: its entry is released and nothing remembered; on main it is an orphan resumed as DISP-14, else queued.

### Table_PerRepository_SharedByProjects
- Checks: DL-22 · Project: Core
- Expected: two projects of one repository share one table file; different repositories have separate files.

## Worker types

### Lock_WorkerType_NotConsidered
- Checks: DL-23 · Project: Core
- Setup: a yielded card whose `Worker:` line asks for another type than the one that yielded.
- Expected: started again by the type its line asks for; the arbiter's answer is the same for every type.

## Host (temporary repository and bare remote; a fake CLI)

### LockHooks_CommitMsg_AppendsCardTrailer
- Checks: DL-18 · Project: Host
- Setup: a worker environment with `PTK_CARD`, `GIT_CONFIG_COUNT` and `core.hooksPath`; a commit.
- Expected: the message ends with `Card: <id>`.

### LockHooks_PrePush_RefusesUnheldAllowsWip
- Checks: DL-18 · Project: Host
- Expected: a push of an unheld path to main exits non-zero naming the path and command; to `wip/<card>` succeeds.

### LockCommand_Granted_Exit0
- Checks: DL-4, DL-27 · Project: Host
- Expected: the `.lock` file is written (tmp then rename); on the reply, one line `granted` and exit 0.

### LockCommand_WaitingThenGranted_AcrossTwoCalls
- Checks: DL-12, DL-27 · Project: Host
- Expected: the first call prints one line `waiting: held by <card>, gives up at <time>` and exits 2; the second
  finds the open request (no new `.lock` file) and exits 0 once granted.

### LockCommand_Yield_Exit3
- Checks: DL-12, DL-27 · Project: Host
- Expected: one line `yield: <path> held by <card> (older)`, exit 3.

### LockCommand_Refused_Exit4
- Checks: DL-6, DL-27 · Project: Host
- Expected: one line `refused: too broad...`, exit 4.

### LockCommand_NoReply_PrintsAndExits2
- Checks: DL-27, design section 5 · Project: Host
- Setup: no reply within 110 seconds (a short test limit through a setting).
- Expected: a line every 30 seconds (tested at a shorter interval), then "no answer yet", exit 2.

### LockRequestStep_Applied_ReplyWrittenAfterTable
- Checks: DL-20, CM-18 · Project: Host
- Expected: requests are read in name order; the table is written, then the reply, then the request moved to
  `moves\done\` (to `moves\failed\` when unreadable).

### LockRelease_OnApplied_Finish_Park_SendBack_RunEnd
- Checks: DL-8, DL-9 · Project: Host
- Expected: each release happens in the tick that applies the move or sees the run end, before dispatch.

### LockAudit_RunsOnMainMove
- Checks: DL-19 · Project: Host
- Setup: a commit without a trailer and a commit with an unheld path pushed by hand (`--no-verify`).
- Expected: both reported as strays; the audited commit in the table advances.

## Prompt and worker types (Core)

### LockPrompt_ScopeMode_HasCommandAndYieldSteps
- Checks: DL-4, DL-12, DL-27 · Project: Core
- Expected: the scope-mode prompt contains the lock command with the exe's full path, the rule that a yield
  commits to `wip/<card>`, journals, hands back To: active as `ready` and ends `LANE-RESULT: yielded`, and the
  sentence on a refused push.

### LockPrompt_SparkWorker_SameShortLiteralText
- Checks: DL-27 (1018-D) · Project: Core
- Setup: the prompt for a Claude worker and for a spark worker (local model, copilot CLI).
- Expected: the same lock paragraph; it names the command written out and each exit code once, in one sentence per
  answer; no wording that depends on the agent.

### LockCommand_Output_OneLineAndExitCode
- Checks: DL-27 (1018-D) · Project: Core (LockReply)
- Expected: every answer's text is a single line without a line break, and maps to exit 0, 2, 3 or 4 as the table
  in design section 3 says.

### LockEnv_WorkerGetsCardExeAndHooks
- Checks: DL-18 · Project: Core (WorkerEnv)
- Expected: `PTK_CARD`, `PTK_SCHEDULER_EXE`, `GIT_CONFIG_COUNT`, `GIT_CONFIG_KEY_0` = `core.hooksPath`, its value.

## Page (Web: the pool page, `CombinedPageRenderer`)

### LockPage_WorkerWindowAndQueueRow_ShowHolds
- Checks: DL-24 · Project: Web (pool page)
- Setup: a model with a card holding an initial claim and two granted entries.
- Expected: the worker's window and the Queue row show the initial claim on one line and "also holds: ..." on
  another, each with its age in minutes; the single page's Control window is not rendered.

### LockPage_WaitingCard_ShowsWaitNote
- Checks: DL-25 · Project: Web (pool page)
- Expected: "waits for lock on `<path>` held by `<card>` since `<time>`, gives up at `<time>`".

### LockPage_YieldedCard_ShowsYieldedNote
- Checks: DL-25 · Project: Web (pool page)
- Expected: in the Queue: "yielded: waits for `<path>` held by `<card>`"; a card held back by the scope gate shows
  the dispatch note as before.

### LockPage_ProjectsWindow_ShowsDayCounts
- Checks: DL-26 · Project: Web (pool page)
- Expected: the day's grants, waits, yields, refusals, refused pushes and strays are in the Projects window's
  statistics, and each event was a log line.
