# Test plan: planner outbox

Rules: PLAN-1 to PLAN-8. Source: `scheduler-next/spec.md` A11. Batch validation and the
write/move decisions are Core (given main's files and the batch, return the operations or a
failure); git, folders and pushes are Host.

### Planner_Enabled_OnlyWithFetchRepoAndOutbox_OffByDefault
- Checks: PLAN-1 · Project: Core
- Cases: neither set, one set -> feature absent; both set -> present, switched off until toggled; toggle persists in planner-state.json across a restart.

### Planner_Step_AtMostOncePerMinute_FetchOnly
- Checks: PLAN-2 · Project: Host
- Setup: switch on; fake clock; fetchRepo with a dirty working tree.
- Expected: one fetch at t=0 and none until t=60 s; fetchRepo's working tree untouched (no pull, no checkout).

### Planner_Batches_CompleteOnlyInNameOrder
- Checks: PLAN-2 · Project: Core
- Setup: outbox folders `002-b` (batch.json), `001-a` (batch.json), `003-c` (no batch.json), `sent/x`, `failed/y`.
- Expected: processed in order 001-a, 002-b; 003-c, sent and failed skipped.

### Planner_Validation_Failures
- Checks: PLAN-3 · Project: Core
- Cases (each fails the whole batch, nothing written): invalid JSON; empty message; no ops; path outside `docs/jobs/`; path with `..`; path with `\`; path with `:`; write whose file is not in the batch folder; write without base; op `copy`.

### Planner_Write_New
- Checks: PLAN-4 · Project: Core
- Cases: base `new`, absent -> written; present with same text but CRLF and trailing spaces -> noted and skipped; present and different -> fail.

### Planner_Write_BaseMatchesBlob_KeepsLineEndings
- Checks: PLAN-4 · Project: Core
- Setup: main's file CRLF; base = its blob id; batch text LF.
- Expected: written with CRLF.

### Planner_Write_MovedSinceBase_FollowsSameName
- Checks: PLAN-4 · Project: Core
- Setup: base for `queue/010-A-x.md`; the card is now `active/010-A-x.md`.
- Expected: written at the active path; noted.

### Planner_Write_Merge_AppendOrFail
- Checks: PLAN-4 · Project: Core
- Cases: base older than main, non-overlapping change -> 3-way merged; overlapping change where the batch only appends -> appended; overlapping real conflict -> fail; path gone with no same-name file -> fail.

### Planner_Moves_And_Removes
- Checks: PLAN-5 · Project: Core
- Cases: move to same path -> skipped; source moved elsewhere -> that file moved; target present and source absent -> skipped; both present -> fail; source absent and target absent -> fail; normal -> `git mv`. Remove absent -> skipped; present -> `git rm`.

### Planner_Commit_MessageTrailersThreeTries
- Checks: PLAN-6 · Project: Host
- Setup: message `Plan "x"`, trailers `Co-Authored-By: ...`; the push rejected once.
- Expected: commit message `Plan 'x'`, blank line, trailers; pushed on the second try. A batch that changes nothing -> success, no commit.

### Planner_Release_ReadyCardsRetriesCleared
- Checks: PLAN-7 · Project: Core
- Setup: batch writes 010-A (`Status: ready`, retries 2) and 020-B (`Status: waiting: dev`, retries 1).
- Expected: 010-A's entry removed and logged "planner: 010-A set ready, run count cleared"; 020-B kept; a tick at once.

### Planner_BatchFolder_SentOrFailedWithClash
- Checks: PLAN-8 · Project: Host
- Setup: success for `001-a` while `sent/001-a` already exists; failure for `002-b`.
- Expected: `sent/001-a-HHmmss`; `failed/002-b` with reason.txt; page line shows on/off, waiting count, failed count, last result.

### Planner_FailedBatch_ReportedAsSent
- Checks: PLAN-8 · Project: Core · **blocked on C1**
- Setup: a batch that fails validation.
- Expected today: last result "sent HH:mm ..." and a "planner: <message>" log (L1497-1505). Expected after C1 (presumably "failed ..." only): to be written.
