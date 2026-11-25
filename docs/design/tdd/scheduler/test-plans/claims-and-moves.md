# Test plan: claims and card moves from the page

Rules: MOVE-1 to MOVE-11. Source: `scheduler-next/spec.md` A10. Text edits to a card (answer
line, Status replace, journal line) are Core; git commits, worktrees and pushes are Host; the
POST routes are Web.

Claims (queue/ -> active/) were made by workers (since card 660-B the scheduler writes them, see "Moves by request" below and ../card-moves.md), per `docs/jobs/README.md` step 3 ("git mv it into
active/, add `Claimed: <date>, lane <L>`, commit as `jobs: start NNN-L-slug`, push"); the
scheduler only sees the result through CARD-1. Spec flag C17 noted the README was not read; it is
in this repository and settles the claim format, so no test is blocked on it. The scheduler's
side of a claim is covered by `Dispatch_Orphan_*` and `Cards_ReadFromRemoteMainOnly_*`.

### Moves_AllCommits_ViaAnswersWorktree_RetryThreeTimes
- Checks: MOVE-1 · Project: Host
- Setup: bare remote; another clone pushes to main between our reset and push, twice; then a third time.
- Action: answer a question.
- Expected: worktree `answers` reset to main each try; commit contains only the card's paths; success on the third try in the first case; when rejected three times, error "rejected three times" and a page notice.

### Moves_Answer_LineAndNote
- Checks: MOVE-2 · Project: Core
- Setup: card with `- Q1: Ok? [yes -> done | no | later]`; clock 2026-11-18 11:20.
- Action: answer Q1 with `no` and a 600-character note.
- Expected: line ends ` -> **no** (developer, 2026-11-18 11:20)`; next line `  Note: ` + 500 characters.

### Moves_Answer_DestinationMovesCard
- Checks: MOVE-2 · Project: Core
- Cases: active card, choice `-> done` -> moved to done/, Status `done`; `-> queue` -> queue/, Status `ready`; `-> active` from review -> active/, `ready`.
- Expected also: commit message "jobs: answer 010-A Q1: yes - to done".

### Moves_Answer_LastOpenQuestion_ActiveBecomesReady
- Checks: MOVE-2 · Project: Core
- Setup: active needs-human card with Q1 and Q2 open.
- Action: answer Q1 (no destination); then Q2.
- Expected: after Q1 Status unchanged; after Q2 Status `ready` and the card stays in active/.

### Moves_Answer_AlreadyAnswered_IsError
- Checks: MOVE-2 · Project: Core
- Setup: Q1 already answered.
- Expected: error; card unchanged; nothing committed.

### Moves_Answer_OnlyThisRoundDecidesDestination
- Checks: MOVE-2 Â· Project: Core
- Setup: card with Q1 answered `-> active` at 12:20, a worker's dated Result line at 12:41, then Q4 answered `close it as done -> done` at 13:37.
- Expected: card goes to done/, Status `done` (935-A). Two questions both answered after the worker line: the most-work destination (active/) still wins.
- Also: no dated worker line, a date-only line, or an answer with no `(developer, ...)` stamp -> every answer counts as before; a page `- <date> developer: ...` line does not start a round; no destination left in this round -> an active card is `ready` where it is.

### Moves_SetStatus_ReplacesFirstAndDropsContinuations
- Checks: MOVE-3 · Project: Core
- Setup: `Status: needs-human: a\n  b\n  c\nLocks: none`.
- Action: set Status `ready`.
- Expected: `Status: ready\nLocks: none`.

### Moves_DeveloperUncommittedEdit_Replayed3Way
- Checks: MOVE-4 · Project: Host
- Setup: developer edited the card's `## Dev` section in the repository working tree (uncommitted); main changed another part.
- Action: answer a question.
- Expected: both edits on main; the repository's copy reverted to main's.

### Moves_DeveloperAppendOnlyEdit_AppendedWhen3WayFails
- Checks: MOVE-4 · Project: Host
- Setup: developer appended lines at the end; main rewrote the last lines so `git apply --3way` fails.
- Expected: the appended text is at the end of main's copy.

### Moves_DeveloperConflictingEdit_ErrorNothingCommitted
- Checks: MOVE-4, MOVE-11 · Project: Host
- Setup: developer and main changed the same line.
- Expected: error "could not be merged with main"; nothing pushed; the developer's edit is still in the working tree; notice on the page.

### Moves_JournalLine_AppendedAtEnd
- Checks: MOVE-5 · Project: Core
- Expected: the card ends with a blank line, `- 2026-11-18 11:20 developer: <line>`, a blank line.

### Moves_Done_ReviewCardsOnly
- Checks: MOVE-6 · Project: Core
- Setup: review card; active card.
- Action: POST `/done` for each.
- Expected: review card: Status `done`, in done/, journal "reviewed, done (from the page).", commit "jobs: done <name> (reviewed from the page)". Active card: error, unchanged.

### Moves_ResumeCard_EachSource
- Checks: MOVE-7 · Project: Core
- Cases: review card -> active/ ready, journal "sent back from review", retries cleared; active needs-human -> ready, "resumed", retries cleared; active waiting-on-dev -> active ready, retries cleared; queue waiting-on-dev -> queue ready, retries **not** cleared; queue needs-human (1149-B) -> queue ready, "resumed", retries cleared.

### Moves_ResumeQueuedNeedsHuman_StaysInQueueReadyAndClearsRetries
- Checks: MOVE-7 (1149-B) · Project: Core
- Action: `CardMoves.Resume` on a `queue/` card whose Status starts `needs-human`.
- Expected: ok; stays in queue/ (never pulled into the pool) with Status ready; journal "resumed from the page; read any ## Dev section first."; commit "jobs: resumed <name> (from the page)"; this card's retry entry cleared, another card's kept.

### Moves_ResumeQueueCard_RetriesNotCleared
- Checks: MOVE-7 · Project: Core · **blocked on C9**
- Expected today: queue resume of a waiting-on-developer card and every Answer keep the retry count. A queue resume of a *needs-human* card clears it (1149-B). Expected after C9: to be written.

### Moves_RetryAndResumeState_ClearRetries
- Checks: MOVE-8 · Project: Core
- Setup: retries {010-A: 2, 020-B: 2}.
- Action: POST `/retry` 010-A; then `/resume` from `paused`.
- Expected: after retry {020-B: 2}; after resume {}.

### Moves_OpenCard_RepositoryCopyElseAnswers
- Checks: MOVE-9 · Project: Host
- Setup: fake shell launcher; card present in the repository working tree; second case only in the answers worktree.
- Expected: the launcher gets the repository path, else the answers path. (Bringing the editor to the front is not tested: see index.md, untestable.)

### Moves_WorkerSettings_ValidatedAndCommitted
- **Retired** (943-A): the per-worker routes this test drove (`/worker-settings` and the others) are gone, and the test with them. MOVE-10 is covered by the tests of `SaveWorkerTypes` (`WorkerTypeActionTests`, `WorkerTypeRenameTests`, `WorkerTypesFromMainTests` in Host.Tests, and `WorkerTypeContractTests` in Core.Tests).

### Moves_PageError_NoticeTenMinutesOrUntilSuccess
- Checks: MOVE-11 · Project: Web
- Setup: fake clock; failing action at t0.
- Expected: notice shown at t0+9:59, gone at t0+10:00; a successful action at t0+1 removes it at once; the failure is logged.

## Moves by request (card 660-B; design in ../card-moves.md, rules CM-1 to CM-24)

Tests: `Scheduler.Host.Tests/Moves/CardRequestsTests.cs` and Core's `Moves/CardRequest*`. Core parses and orders; Host commits.

### Moves_Request_NamedAndParsed
- Checks: CM-1, CM-2, CM-3 · Project: Core
- Cases: `<time>-slot3.move` with header lines and `---` and a body; no `---` (header only); a `.tmp` file is never read; missing Card, no or unknown To, unknown From are unreadable.
- Expected: fields parsed; the unreadable cases are failed (CM-17).

### Moves_Apply_TextStatusAndClaimed
- Checks: CM-4, CM-5, CM-6 · Project: Core
- Cases: body replaces main's text but main's `## Dev` section is kept before `## Result`; `To: done` sets `done`; `To: review` keeps a `review...` Status else sets `review`; `Status:` header with no body; a claim adds `Claimed: <date>, lane <L>` once.
- Expected: the resulting text of each case.

### Moves_Apply_MergesAnsweredQuestions
- Checks: CM-22 (CM-4 for `## Dev`) · Project: Host (`CardRequestsTests`: `Plan_KeepsAnsweredQuestion`, `Plan_KeepsQuestionAddedOnMain`, `Plan_WorkerTextWins`, `Plan_KeepsAnsweredQuestionWithItsNote_AndLineEndings`, `Plan_NothingToKeep_LeavesTextAsItIs`)
- Cases: body with Q1 unanswered while main has Q1 answered; a question only main has; a main answer with indented `Note:` lines and CRLF line endings; no answered question on main.
- Expected: the result keeps main's answered line and its notes, keeps the main-only question, takes Status, Task and Result from the body, and is unchanged when there is nothing to keep.

### Moves_Apply_RewordedOrDroppedAnswer_KeptAndLogged
- Checks: CM-22 · Project: Host (`Plan_RewordedAnsweredQuestion_KeepsMainLineAndLogs`, `Plan_DroppedAnsweredQuestion_KeepsMainLineAndLogs`)
- Cases: the body rewords an answered question; the body drops it.
- Expected: main's answered line is kept either way, and the log line names the question.

### Moves_Apply_OneCommitOnlyTheCardsPaths_PushedWithRetries
- Checks: CM-7 · Project: Host
- Expected: one commit `jobs: start|done|review|wait|status <card>` with only the old and new card paths; up to three tries from a fresh main (MOVE-1); a request whose commit cannot be pushed three times stays where it is.

### Moves_Requests_AppliedInFileNameOrder_AtStartDoneAndTick
- Checks: CM-8, CM-9, CM-10, CM-11, CM-12 · Project: Host
- Setup: three requests out of order by write time; a queued card to start; a worker that ends.
- Expected: applied by name (time, then slot), each on the previous result; the claim is on main before the worker starts; a worker's finish is on main before its result is judged; leftovers on the next tick.

### Moves_Conflicts_FailedOrAppliedInPlace
- Checks: CM-13, CM-14, CM-15, CM-16, CM-18 · Project: Host
- Cases: card not on main; card in From; card already in To (text applied in place; a second claim fails as "already claimed"); card elsewhere; failed -> `moves\failed\` with `.reason.txt` and a log line; applied -> `moves\done\`.

### Moves_WorkerThatMovesItself_StillWorks
- Checks: CM-19, CM-20 · Project: Host
- Setup: a worker `git mv`s the card and also writes a request; separately a claim whose worker fails to start.
- Expected: the request finds the card already in To and applies its text; the `queue/` copy is removed by CARD-19; the unstarted claimed card is resumed as an orphan on a later tick.

### Moves_Finish_WorkNotOnMain_WaitsOnPlanner
- Checks: CM-23 (cards 1237-B, 1300-B) · Project: Host (`CardRequestStepSettleTests`: `FinishWithCommitsOnWipNotOnMain_WaitsOnPlannerInQueue_CM23`, `ReviewWithLocalCommitOnly_WaitsOnPlannerInQueue_CM23`, `FinishWithEverythingOnMain_SettledAsBefore_CM23`, `OldWipBranchAndParkRequest_Untouched_CM23`)
- Cases: a worker's To: done whose run's commit is only on `wip/<card>`; a To: review whose commits are only in the slot worktree; a finish whose commits are on main; a wip branch from before the run and a park request.
- Expected: the first two go to queue/ with `Status: waiting: planner (finished, but its work is not on main: <where>)` and a `scheduler:` Result line naming what was asked for, logged in one line; the others are settled or left as asked.

### Moves_ToDone_WorkNotOnMain_WaitsOnPlanner
- Checks: CM-24 (card 1300-B) · Project: Host (`AnswersCommitterUnlandedTests`: `AnswerToDone_WithWipNotOnMain_WaitsOnPlannerInQueue_CM24`, `DoneButtonOnReview_WithWipNotOnMain_WaitsOnPlannerInQueue_CM24`, `EverythingOnMain_AnswerAndDoneSettledDone_CM24`, `AnswerNotToDone_Untouched_CM24`, `PlannerBatchToDone_AppliedAndLogged_CM24`; `CardRequestStepSettleTests.RequestToDoneWithEarlierWipNotOnMain_WaitsOnPlannerInQueue_CM24`; `WipBranchCleanupTests.BranchWithWorkNotOnMain_IsKeptAndCardWaitsOnPlanner`)
- Cases: an answer whose choice ends `-> done` on a card whose fix is only on `wip/<card>`; the Done button on a review card with work on `wip/<card>-*`; the same with no wip branch and with a wip branch whose work reached main rebased; an answer that does not move the card; a planner batch to done/; a move request to done/ whose wip branch is from an earlier session.
- Expected: the held moves land in queue/ as `waiting: planner (...)` with the answer still recorded, a Result line naming who asked (an answer from the page, the Done button, a move request), a `jobs: wait` commit and one log line, and the page is handed the edit as committed; everything on main is settled done as before; the answer that does not move is untouched; the planner batch is applied to done/ and logged as `planner: <card> moved to done with work not on main: <branch>`.
