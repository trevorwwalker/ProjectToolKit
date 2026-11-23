# Test plan: per-card scopes instead of categories

Rules: SC-1 to SC-8, DISP-25 (decided design, spec B2). The syntax and the glob overlap test are decided
(SC-8, DISP-25); tests still **blocked on Qn** (Q6, Q8, Q9) state the decided part. Scope parsing and the overlap
decision are Core.

### Scope_CardNamesScope_Parsed
- Checks: SC-1, SC-8 · Project: Core (line name, globs, trailing `/`, `except`: decided on 320-A and 961-A)
- Setup: a card with a scope line listing a folder, a file and a pattern.
- Expected: three scope entries of the right kinds.

### Scope_NoOverlap_RunsSideBySide
- Checks: SC-2, SC-3 · Project: Core
- Setup: running card scope `src/a.cs`; ready card scope `src/b.cs`; free worker.
- Expected: the ready card starts.

### Scope_SameFile_RunsAfter
- Checks: SC-2, SC-3 · Project: Core
- Setup: running card scope `src/a.cs`; ready card scope `src/a.cs`.
- Expected: not started, with a note naming the running card; starts after it finishes.

### Scope_FolderContainsFile_Overlaps
- Checks: SC-2 · Project: Core
- Setup: running `src/`; ready `src/x/y.cs`.
- Expected: overlap (assuming a trailing `/` means a folder).

### Scope_GlobOverlap_Rules
- Checks: SC-2, SC-8, DISP-25 · Project: Core (961-A)
- Cases, each checked both ways round: `src/*` vs `src/a.c` overlap; `src/*` vs `src/x/a.c` none; `src/**` vs `src/x/a.c` overlap; `*.md` vs `docs/a.md` none; `docs/*.md` vs `docs/a.md` overlap; `src/a*` vs `src/*b` overlap; `src/*` vs folder `src/x/` none; `src/**` vs `src/x/` overlap; `a?.c` vs `ab.c` overlap.
- Expected: as listed.

### Scope_GlobExcept_ClearsOnlyProvable
- Checks: DISP-25 · Project: Core (961-A)
- Setup: scope `vm/` with `except vm/gen*` against scope `vm/gen1.cpp`; then against a folder `vm/gen/` and a glob `vm/g*`.
- Expected: the file pair does not overlap; the folder and glob pairs still do (an except glob clears only a named file).

### Scope_CharacterClass_NotStarted
- Checks: SC-8 · Project: Core (961-A)
- Setup: a card whose Locks line has `src/[ab].c`.
- Expected: not started, with a note that a character class is not supported.

### Scope_GlobWithNote_NoteIgnored
- Checks: SC-8, DISP-22 · Project: Core (961-A)
- Setup: `Locks: vm/MemoryMap.* (generated)`, and a second card locking `vm/MemoryMap.h`.
- Expected: the entry is `vm/MemoryMap.*`; the second card waits.

### Scope_AnyProject_Overlap
- Checks: SC-2 · Project: Core · **blocked on Q6**
- Setup: project P and O, both scope `src/a.cs`, different repositories; then two projects sharing one repository.
- Expected: different repositories never overlap (decided: they share no files); the shared-repository case per Q6.

### Scope_NoOwnScope_TakesAreaDefault
- Checks: SC-4 · Project: Core
- Setup: category D (today's category D) owns `scheduler-next/spec.md` and `scheduler-next/test-plans/`; card 001-D has no scope line.
- Expected: its scope is category D's owns minus excepts.

### Scope_AreaBriefInPrompt
- Checks: SC-4, LANE-5 · Project: Core
- Expected: the prompt still carries the category's brief of what it owns (as `Lanes_Brief_InPrompt`).

### Scope_AllowedOverlap_AddOnlyFile
- Checks: SC-5 · Project: Core (declared by an `(add-only)` mark or categories.json `shared.addOnly`, DISP-24)
- Setup: two cards both scoped to `docs/known_gaps.md` marked overlap-allowed; a third pair on a normal file.
- Expected: the known_gaps pair run side by side; the normal pair one after the other; the prompt tells both to pull, rebase, resolve and re-run the gates.

### Scope_RejectedPushAndConflict_Logged
- Checks: SC-6 · Project: Host · **blocked on Q9** (who reports them)
- Setup: a worker log with a rejected push and a rebase conflict.
- Expected: one log entry each, naming the card and the files.

### Scope_CheckedByScheduler_AtDispatch
- Checks: SC-7 · Project: Core
- Setup: as `Scope_SameFile_RunsAfter`.
- Expected: the scheduler, not the worker, prevents the second start (unlike `Dispatch_LocksLineIgnored` today).

### Scope_OutOfScopeChange_Detected
- Checks: SC-7 · Project: Host · **blocked on Q8**
- Setup: card scoped to `src/a.cs` pushes a change to `src/b.cs`.
- Expected: per Q8 (dispatch-only check, or a check of pushed changes and what then happens).

### Scope_HeldAreaWhenRedefined
- Checks: AREA-2, SC-4 · Project: Core · Q10 answered by card 560-A (developer)
- Setup: category D's default scope changes while a D card without its own scope is queued.
- Expected: the new definition is accepted at once, never held; the card's scope is the new default (DISP-23). See `Lanes_Redefined_AcceptedAtOnce` in dispatch.md.

### Scope_EmptyScope_Rejected
- Checks: SC-1 · Project: Core
- Setup: a card with an empty scope line and no category.
- Expected: not dispatched, with a note saying the scope is missing (suggested).

### Scope_LocksLine_IsPartOfScope
- Checks: DISP-22 · Project: Core · card 560-A (reverses DISP-20)
- Setup: running 010-A with `Locks: src/x`; queue 020-B with `Locks: src/x`, no Scope line.
- Expected: 020-B waits with "waits: overlaps 010-A on src/x"; backticks and notes in parentheses on the Locks line are ignored.

### Scope_AreasAreLabels_NotLimits
- Checks: AREA-1 · Project: Core, Web · roadmap item 1
- Setup: two ready cards in the same category with disjoint scopes; two free workers.
- Expected: both start (one category no longer means one card). The page still colours and filters by category letter.
