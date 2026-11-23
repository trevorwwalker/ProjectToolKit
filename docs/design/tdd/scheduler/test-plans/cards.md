# Test plan: reading cards and requests from main

Rules: CARD-1 to CARD-20. Source: `scheduler-next/spec.md` A2; `scheduler/schedule.ps1`
`Read-Cards` L591-645 where the spec leaves the result open. Parsing tests are Core (text in, card
out); anything that runs git is Host.

### Cards_ReadFromRemoteMainOnly_NotWorkingTree
- Checks: CARD-1 · Project: Host
- Setup: remote main has `queue/010-A-x.md` with `Status: ready`; the repository working tree has an uncommitted edit setting `Status: blocked` and an untracked `queue/020-A-y.md`.
- Action: read cards.
- Expected: one card, 010-A, Status `ready`.

### Cards_BlobCache_ReparsesOnlyChangedBlobs
- Checks: CARD-1 · Project: Host
- Setup: two cards on main; a counting parser.
- Action: read, change one card on main, fetch, read again.
- Expected: second read parses one blob only; a card moved to another folder (same blob, new path) is re-parsed.

### Cards_Fetch_AtMostEvery15Seconds
- Checks: CARD-2 · Project: Core
- Setup: fake clock and fake git.
- Action: request a fetch at t=0, 10, 15, 29 s.
- Expected: fetches at 0 and 15 only (t=15 is exactly the boundary: fetches).

### Cards_LsTreeFails_KeepsPreviousList
- Checks: CARD-3 · Project: Host
- Setup: a successful read of 3 cards; then git returns nothing (remote ref missing).
- Action: read again.
- Expected: the same 3 cards.

### Cards_PathPattern_FieldsAndId
- Checks: CARD-4 · Project: Core
- Setup: paths `docs/jobs/queue/7-B-slug.md`, `docs/jobs/active/010-A-x.md`, `docs/jobs/done/1200-C-y.md`.
- Expected: Folder, Name (`7-B-slug`), Num 7, Category `B`, Id `7-B` (digits as the file writes them); Id `010-A` (leading zeros kept); Id `1200-C`.

### Cards_NonMatchingFiles_Ignored
- Checks: CARD-4 · Project: Core
- Setup: `queue/README.md`, `queue/010-AB-x.md`, `queue/x-A-010.md`, `queue/sub/010-A-x.md`, `plans/010-A-x.md`, `queue/010-A-x.txt`.
- Expected: no cards. (Boundary: malformed file names.)

### Cards_DoneFolder_NotOpened
- Checks: CARD-5 · Project: Core
- Setup: `done/010-A-x.md` whose text is unreadable garbage; a counting blob reader.
- Expected: card known with folder/name/id/category; Status '' and no blob read.

### Cards_Title_FirstHashSpaceLine
- Checks: CARD-6 · Project: Core
- Setup: text `## Not\n# First\n# Second`.
- Expected: Title `First` (a `## ` line is not a title, L617 `^#\s+`).

### Cards_Status_FirstLineAndContinuations
- Checks: CARD-7 · Project: Core
- Setup: `Status: needs-human: long\n   question here\n Status: no\nStatus: second`.
- Expected: Status `needs-human: long question here`; later Status lines ignored; a 1-space indented line is not a continuation.

### Cards_MalformedCard_NoStatusLine_IsNotReady
- Checks: CARD-7, DISP-10 · Project: Core
- Setup: queue card with a title only (no Status, no fields); a second card that is empty text.
- Action: read cards and plan dispatch.
- Expected: both read with Status '', Prio 1, no deps; neither is a candidate; the queue note is the empty Status (L2903). No exception, other cards still dispatched.

### Cards_Priority_Mapping_LastWins
- Checks: CARD-8 · Project: Core
- Setup: cases: none; `Priority: high`; `Priority: low`; `Priority: urgent`; `Priority: high` then later `Priority: low`; `Priority:` with no word.
- Expected: 1, 0, 2, 1, 2, 1.

### Cards_FieldLinesInSections_AreIgnored
- Checks: CARD-6, CARD-7, CARD-8, CARD-9, CARD-10, CARD-11 · Project: Core
- Setup: header `Priority: high`, `Time box: 30`, `Worker: Opus`, `Depends on: 010-A`; under `## Result` and `## Questions` lines `Priority: low`, `Time box: 90`, `Worker: Sonnet`, `Depends on: 020-B`, `Status: done`; a second card with no fields in its header and `Priority: high` under `## Result`.
- Expected: Prio 0, TimeBox 30, WorkerReq `Opus`, deps [010-A], Status from the header; the second card has Prio 1, no time box, worker or deps. (Reverses the old test, which expected the section lines to count.)

### Cards_TimeBox_ParsedLastWins
- Checks: CARD-9 · Project: Core
- Setup: `Time box: 90 min`; `Time box: 30` then `Time box: 60`; `Time box: none`.
- Expected: 90; 60; 0 (not set).

### Cards_WorkerRequest_TextBeforeParenthesis
- Checks: CARD-10 · Project: Core
- Setup: `Worker: Opus (premium, for the core)`; `Worker: 3`; `Worker:  qwen 32b  `.
- Expected: `Opus`; `3`; `qwen 32b`.

### Cards_DependsOn_AllIdsLastLineWins
- Checks: CARD-11 · Project: Core
- Setup: header `Depends on: 010-A, 020-B (and 30-C)`; then a second card whose header has `Depends on: 010-A` followed by `Depends on: none` (the last in the header wins).
- Expected: [010-A, 020-B, 30-C]; [] for the second.

### Cards_DependsOn_IdsOfAnyWidth
- Checks: CARD-11 · Project: Core
- Setup: `Depends on: 1000-A, 930-B`; a card `1000-A-x.md` in queue and `999-B-y.md` in done.
- Expected: both ids are read as dependencies; id `1000-A` sorts after `999-B` (a number, not text).

### Cards_Questions_ParsedWithChoicesAndDestinations
- Checks: CARD-12 · Project: Core
- Setup: under `## Questions`: `- Q1: Ok? [yes -> done | no -> queue | later]`, `- Q2: One? [only]`, `- Q3: Done? [a | b] -> **a** (developer, 2026-11-18 10:00)`; a matching line under `## Result`.
- Expected: one question Q1 with choices yes(done), no(queue), later(none). Q2 (one choice) and Q3 (answered) do not count; the Result line is ignored.

### Cards_Why_FromStatusOrResult
- Checks: CARD-13 · Project: Core
- Setup: (a) active `Status: needs-human: pick one`; (b) queue `Status: waiting: developer to test`; (c) review card with Status `review` and a dated Result; (d) review `Status: review: check X`.
- Expected: (a) `pick one`/status; (b) `developer to test`/status; (c) newest journal text/result; (d) `check X`/status.

### Cards_LatestJournal_GreatestDateTimeWins
- Checks: CARD-14 · Project: Core
- Setup: Result entries `- 2026-11-18 09:00 a`, `* **2026-11-18 10:30** b\n  more`, `2026-11-17 23:00 c`, and an undated line.
- Expected: `b more`. Tie on the same key: the later entry wins. No dated entry: the loose text.

### Cards_ShortText_StripsAndTruncates
- Checks: CARD-14 · Project: Core
- Setup: text with `**bold**`, backticks, runs of spaces and newlines, 400 characters.
- Expected: markers removed, whitespace collapsed, 300 characters plus `...`.

### Cards_WaitsOnDev_Patterns
- Checks: CARD-15 · Project: Core
- Setup: queue/active cards with Status `waiting: developer`, `waiting: the dev`, `Waiting: YOU now`, `waiting: human`, `waiting: developers`, `waiting:developer`; a review card with `waiting: developer`.
- Expected: true, true, true, true, false (`\b`), true; false for the review card.

### Cards_WaitsOnPlanner_Patterns
- Checks: CARD-16 · Project: Core
- Setup: `waiting: planner (request #3)`, `waiting: the planner`, `waiting: planners`, done-folder card with `waiting: planner`.
- Expected: true, true, false, false.

### Cards_DepsDone_DoneOrReviewOnly
- Checks: CARD-17 · Project: Core
- Setup: card depending on 010-A in done/, 020-B in review/, 030-C in active/, 040-D missing.
- Expected: false overall; true when only 010-A and 020-B are named; false for a missing id.

### Cards_DependencyCycle_BothWaitForever
- Checks: CARD-17, DISP-12 · Project: Core
- Setup: 010-A depends on 020-B; 020-B depends on 010-A; both ready in queue; free workers.
- Action: plan dispatch.
- Expected: neither is started; notes "waits for 020-B" and "waits for 010-A"; no exception and no infinite loop (L559 checks one level only).

### Cards_Requests_ReadAndOpenFlag
- Checks: CARD-18 · Project: Core
- Setup: `requests/README.md`, `requests/001-x.md` (`# T`, `Status: open`, `For: 010-A`), `requests/002-y.md` (`Status: Answered`), `requests/003.md` (no Status).
- Expected: three requests (README skipped); open: 001 and 003; 002 closed.

### Cards_DuplicateInQueue_RemovedFromMain
- Checks: CARD-19 · Project: Host
- Setup: main has `queue/010-A-x.md` and `active/010-A-x.md`.
- Action: one tick.
- Expected: the queue copy is never a candidate; a commit "jobs: remove the queue/ copy of 010-A-x" removes it on main; cards are re-read; up to 3 push tries on rejection.

### Cards_DuplicateOutsideQueue_LoggedOnceLeftAlone
- Checks: CARD-19 · Project: Host
- Setup: `active/010-A-x.md` and `review/010-A-x.md`.
- Action: three ticks.
- Expected: one log line; no commit.
