# Dynamic file locks for workers: design and plan

Card 994-D, 2026-11-20. Design only: nothing here is built. It is written for the developer to approve
(open points under Questions on the card, repeated at the end) and then to be carded (section 8). Rule IDs
`DL-1` onward are stable names for test cases; once the cards land, `spec.md` takes them over (B2, beside
SC-1 to SC-8).

**Amended by card 1018-D (planner, developer approved 2026-11-21):** (1) the page work targets the pool page, not the
single page's Control window (DL-24 to DL-26, section 6 Web); (2) a card's initial held set is recorded when its claim
lands in the background (995-B), not when dispatch posts it (DL-1, DL-21); (3) spark workers follow the lock routine as
Claude workers do (section 3, card 9; rule DL-27, added to the spec). The spec rules in B2 carry the same amendments.

Sources: the card's starting points (planner and developer, 2026-11-20); spec B2 (SC-1 to SC-8), DISP-13,
DISP-20 to DISP-25; `card-moves.md` (CM-1 to CM-22); the code in `Scheduler.Core/Scopes/` (ScopeOverlap,
ScopeGate, CardScope, PathGlob), `Scheduler.Core/Dispatch/DispatchPlanner.cs`, `Scheduler.Core/Launch/`
(WorkerPrompt, WorkerEnv, Adoption), `Scheduler.Core/Tick/TickWake.cs` (981-A), `Scheduler.Host/Git/GitWriter.cs`
(987-B), `Scheduler.Host/State/StateFiles.cs`, `Scheduler.Core/Results/RunResults.cs` (940-A, spec C2, C8);
`jobs/categories.json` (shared add-only list and the `<Version>` exception).

## 0. What exists today, and what that means

- **A card holds its scope for its whole run.** Dispatch (ScopeGate, SC-2) starts a card only when its scope
  (Scope line, else its category's default, plus Locks paths: CardScope) overlaps no running card's. Nothing
  is acquired later; nothing is released before the card ends. A card that needs one file outside its scope
  either parks and asks (README "Locks") or strays. Cards whose real footprints are disjoint but whose
  declared scopes overlap (two cards defaulting to the same category) run one after another.
- **Overlap is already a solved, tested function.** `ScopeOverlap.Shared(a, b, addOnly)` handles folders,
  files, globs (SC-8, DISP-25), except lists, add-only paths and different repositories. Locks reuse it
  unchanged: a lock is a scope entry.
- **The scheduler already answers files in a folder, fast.** Move requests (CM-1 to CM-18) are files in
  `<log>\moves\`; writing one wakes a light tick (981-A, TickWake). A lock request rides the same folder
  and wake, so it is answered in seconds, not at the next 15 s poll.
- **Workers push to main themselves.** GitWriter (987-B) is the one writer of the scheduler's own commits
  (claims, moves, planner batches, page actions); workers' code commits go straight from their worktrees
  to `origin/main`. The remote (`git.local`) is a plain git server the scheduler does not configure.
- **Running workers survive a scheduler restart** (ADOPT-2, `StateFile.WorkersState`); a worker that is not
  adopted leaves its card in `active/` as an orphan, resumed first (DISP-14).
- **The worker never waits for the scheduler** (CM-12). A lock request is the first thing a worker would wait
  for; section 3 keeps that wait short, bounded and visible.

What this means: the change is mostly *when* a card's scope is decided (it may grow during the run) and
*who checks* that a worker stayed inside it (today nobody does, mechanically). Dispatch, overlap and the
request folder are reused.

## 1. Model

A card's **held set** is a list of scope entries (same syntax as `Scope:`, SC-8) in one repository. It is the
**initial claim** (DL-1) plus every entry **granted** during the run (DL-4). The scheduler keeps every running
card's held set in a **lock table**, one per repository (DL-20). Reading needs no lock (DL-3): a worker may
read any file; it may change, add or delete only paths its held set covers, plus the exemptions (DL-15).

Locks are **strict two-phase**: a card's held set only grows while it runs, and is released all at once when
the run ends (DL-8). There is no unlock request: releasing a file mid-run would let another card change it
under a worker that may still be relying on it, and saves little (runs are under two hours).

## 2. Rules

### Claim and grant

- **DL-1 Initial claim.** When a card starts, its held set is its scope as today (CardScope: Scope line, else
  its category's default, plus Locks paths), plus its **remembered set** when it resumes (DL-10). Dispatch
  (ScopeGate) checks the initial claim against every running card's *held set* (not only its declared scope),
  so a card that grew its locks keeps newcomers off them.
  *Amended (995-B, 1018-D):* a card's claim (`jobs: start`, CM-9) is pushed in the background, off the tick, and the
  worker starts only once it has landed (`ClaimState.Landed`). The initial held set is **recorded in the lock table
  when the claim lands**, not when the dispatch tick posts it. Until then the card is in the table as **starting**:
  it has a slot and its scope still counts against other candidates at dispatch (the starting slot's stand-in carries
  the scope, MP-11), but it has no held set and no age yet (DL-11 fixes the age at the landing) and cannot make a lock
  request. A claim that fails (`ClaimState.Failed`) drops the starting entry; the card was never held. DL-21 covers a
  restart while a card is starting.
- **DL-2 Planner scopes.** The planner keeps writing precise `Scope:` lines; dynamic locks are for what it could
  not foresee, not a reason to write vaguer ones. The category default stays the default for a card with no
  Scope line (narrowing it is Q3).
- **DL-3 Reads are free.** Reading, building and testing any file needs no lock. A file another card is changing
  may be stale in the reader's worktree; pull-rebase-gates before every push (as today) is the protection.
- **DL-4 Acquire on demand.** Before changing a path outside its held set, the worker asks for it (section 3) and
  waits for the answer. A request names one or more entries (files, folders ending `/`, globs) and a one-line
  reason. Entries that the held set already covers are granted at once.
- **DL-5 Grant.** A request is granted when no other running card's held set overlaps any requested entry
  (`ScopeOverlap.Shared`, with add-only paths). All entries of one request are granted together or not at all,
  so a worker never holds half of what it asked for.
- **DL-6 Narrowness.** An entry that would cover a whole category's default, the repository root, `**`, or more
  than 200 tracked files is refused with "too broad: ask for the files or the folder you need". The scheduler
  never widens or narrows a request itself.
- **DL-7 No pending-dispatch reservation.** Grants are checked against running cards only. A queued card that
  would overlap a new grant simply waits at dispatch (ScopeGate's note names the holder), as it would for a
  declared scope today.

### Release

- **DL-8 Release.** A card's held set is released, all at once, when (a) its finish request (To: done or review)
  has been applied on main (CM-10), (b) its park request (To: active, needs-human) or send-back (To: queue) has
  been applied, (c) it yields (DL-12), or (d) its run ends any other way (failed, killed, timed out, stopped).
  The release happens in the same tick, before dispatch, so the next card can start on those files.
- **DL-9 Release on main, not on exit.** For (a), release waits until the finish is *on main*. A finish that
  cannot be pushed (CM-18 leaves it for the next tick) keeps its locks, so no one starts on a file whose last
  change is not on main yet.
- **DL-10 Remembered set.** When a run ends without finishing the card (DL-8 b to d), the entries it had been
  granted beyond its initial claim are stored as the card's **remembered set**. When the card next starts, the
  remembered set is part of its initial claim (DL-1), so a resumed worker can continue its `wip/<card>` work
  without asking again. A card that reaches done or review drops its remembered set. A needs-human card's
  remembered set is not held while it is parked (README: a parked card releases its locks); whoever resumes it
  gets it back only when dispatch finds it free.

### Conflicts: wait-die

- **DL-11 Age.** A card's age is `(priority rank, card number)`: high before normal before low, then the lower
  number. It is fixed the first time the card is claimed and stored in the lock table (DL-20), so a card that
  yields and returns keeps its age even if its priority line is changed later.
- **DL-12 Wait-die.** When a requested entry overlaps a holder's set:
  - the requester is **older** than every conflicting holder: it **waits**. The request stays open; it is granted
    the moment the holders release (DL-8), checked on every tick and on every release;
  - the requester is **younger** than any conflicting holder: it **dies** (yields). The answer is `yield`, with the
    path and the holder. The worker commits its work in progress to `wip/<card>` (never main), writes a resume
    line in Result, and hands the card back with a move request To: active, Status `ready`, ending with
    `LANE-RESULT: yielded`. Its locks are released (DL-8 c); its remembered set gets the entries it held *and*
    the entries it asked for, so it is not restarted until the holder has finished (DL-1 with ScopeGate).
- **DL-13 Wait limit.** An older card waits at most `lockWaitMinutes` (scheduler.json `limits`, default 20) and
  never past its own warn time. When the limit passes, the answer turns to `yield` and it leaves as in DL-12.
  A wait never ends with a younger card being stopped (that would be wound-wait; section 4).
- **DL-14 Yield is not a failure.** A run that ends `LANE-RESULT: yielded` with its request applied gives its run
  back (as spec C8 does, never below 0) and does not count toward the stuck limit (spec C2, 940-A). A yielded card
  in `active/` is an orphan and goes first among cards of its priority (DISP-14), which, with its fixed age, is
  what makes wait-die starvation-free: every holder that blocks it is older and finishes in bounded time.

### Exemptions

- **DL-15 Outside locking.** Never locked and never refused at push: (a) the shared add-only files in
  categories.json (`shared.addOnly`), including the `<Version>` line of `Directory.Build.props`; (b) the card's own
  card file in `jobs/` (journal lines); (c) new files under `jobs/requests/` (requests to the planner). Under
  `jobs/` only these stay protected, refused even when held (developer, 2026-11-21, narrowed from "every other path
  under `jobs/`"): other cards' card files (`jobs/{queue,active,review,done,needs-human,waiting}/**`, card moves are
  the scheduler's, CM), the planner's `jobs/plans/**`, `jobs/archive/**` and `jobs/requests/answered/**`, and the
  scheduler's own `jobs/.logs/**` and `jobs/.planner-outbox/**`. Every other path under `jobs/` (README.md,
  templates, `attachments/`, `categories.json`, `project.md` ...) is an ordinary path: allowed when held, refused
  with the lock command when not. The list is written once (`ProtectedJobs`), used by DL-18 and DL-19.
- **DL-16 Add-only stays add-only.** The push check (DL-18) also reads the line counts of each add-only file in
  the pushed range: a change that deletes lines other than the `<Version>` line is reported (a warning in the log
  and on the card's page row), not refused, because merging a rebase conflict in such a file legitimately
  rewrites a line or two.
- **DL-17 Version bumps.** Two cards bumping in parallel stay safe without a lock because a push to main is a
  fast-forward or nothing: the second push is rejected, its rebase conflicts on the `<Version>` line, and the
  README rule (pull, bump again from main's value, never lower) gives it the next number. The push check adds one
  guard: when the pushed range changes the `<Version>` line, the new value must be greater than main's; a lower
  or equal value is refused with "bump again from main's version".

### Enforcement

- **DL-18 Push check (prevention).** The scheduler starts every worker with git configured, through the worker's
  environment (`GIT_CONFIG_COUNT`, `core.hooksPath` = `<log>\hooks\`), to run two hooks it owns:
  - `commit-msg` appends a trailer `Card: <card id>` to every commit (from `PTK_CARD` in the environment), so every
    commit on main can be traced to a card without the worker remembering to;
  - `pre-push` runs `scheduler-next.exe check-push`, which, for each pushed ref that is the main branch, lists the
    paths changed in `<remote sha>..<local sha>`, and refuses the push (non-zero exit) when any path is outside the
    card's held set and DL-15 (a protected path is refused even when held, with the rule; an ordinary path under `jobs/` as any other), naming each path and the command to ask for it. Pushes to `wip/<card>` are never
    refused. It reads the lock table file (DL-20) only; it changes nothing.
- **DL-19 Audit of main (detection).** A hook can be skipped (`--no-verify`, a worker that runs `git` with its own
  config). On every move of main, the scheduler reads the new commits that are not its own (`git log --name-only`
  from the last audited commit, kept in the lock table) and checks each against the held set its `Card:` trailer
  names, as it was when the commit landed. A commit with no trailer while workers ran, or with a path its card did
  not hold, or a protected `jobs/` path (DL-15, held or not; developer, 2026-11-21), is a **stray**: logged, shown on the card's row and on the Workers page, and the card's finish is
  turned into review (CM-5 rule: To: done becomes To: review with `review: changed <path> without its lock`).
  The audit never reverts anything.
- **Decision (DL-18/DL-19, card point 4):** enforcement stays on the worker side of the push, with the
  scheduler's own hook and an audit after it, and workers keep pushing to main. Moving workers' pushes behind
  GitWriter (workers push `card/<id>`, the scheduler checks and fast-forwards main) would be airtight, but it puts
  every rebase conflict on the scheduler, which cannot resolve one, and turns every push into a wait for the
  writer's queue. A server-side `pre-receive` hook on `git.local` would also be airtight, but it is set up per
  server and outside the kit, and would not travel to another project's remote. The hook catches the honest
  mistake (the one the card worries about: a worker that forgot to ask); the audit catches the rest and stops it
  from reaching done unseen. Q2 asks whether the developer wants the airtight version instead.

### Storage, restart and projects

- **DL-20 Lock table.** One JSON file per repository, `<log>\locks\<repository key>.json`, written only by the
  scheduler (tmp then rename, as CM-1), holding for each card: its age, held set (initial and granted entries,
  each with the time it was granted), remembered set, open request (if any, with its deadline); and the last
  audited main commit. It is not in git: it describes running processes, like `WorkersState`.
- **DL-21 Restart.** At start, the table is read before adoption. Cards whose workers are adopted (ADOPT-2) keep
  their held sets and open requests; the open request's deadline carries over, as the stop deadline does. Every
  other card in the table is released as in DL-8 d (its granted entries become its remembered set). A card that
  was **starting** (its claim had not landed) has no worker to adopt, and background claims are not kept across a
  restart: its entry is released, with nothing to remember. If the claim had in fact reached main, the card is in
  `active/` with no worker, an orphan resumed as DISP-14 says (its held set recorded when that resume's claim lands);
  if not, it is still in `queue/`. The restart that lost a claim at 23:56 on 2026-11-20 is the case. A missing or
  unreadable table starts empty: running cards fall back to their initial claims and the audit begins at main's
  current commit; the log says so.
- **DL-22 Per repository.** A lock table belongs to a repository, not a project. Projects sharing one repository
  share its table (ScopeOverlap already compares their paths); projects in different repositories never conflict.
  The card in a request is checked against the slot that wrote it (the running entry's card and project), never
  taken on the worker's word.
- **DL-23 Worker types.** Locks do not look at worker types. A yield frees a worker of the card's type; the
  yielded card is started again by whatever type its Worker line asks for.

### Page

All three target the **pool page** (`CombinedPageRenderer`; amended by 1018-D): the worker windows that follow Waiting
on you, the Queue window's rows and the Projects window. The single page's Control window is gone and one-project mode
is being retired, so nothing is built for either.

- **DL-24 Holds.** Each running worker's window and its Queue row show the card's held set: the initial claim on
  one line, granted entries on another ("also holds: docs/known_gaps.md, code/src/Scheduler.Web/Views/"), each with
  its age in minutes.
- **DL-25 Waits.** A waiting card shows "waits for lock on `<path>` held by `<card>` since `<time>`, gives up at
  `<time>`". A yielded card in the Queue shows "yielded: waits for `<path>` held by `<card>`". Dispatch's existing
  note for a card held back by ScopeGate names the holder as it does today.
- **DL-26 Measure** (SC-6). Every grant, wait, yield, refusal, push refused by the hook and stray is a log line,
  and counted per day in the Projects window (which holds the statistics since 1004-C), so the developer can see whether yields are rare enough and
  whether planner scopes can be narrowed.

## 3. The worker's side

**The request.** A worker asks with one command, the same way on every agent (Claude, spark):

```
scheduler-next.exe lock <path> [<path> ...] --why "<one line>"
```

(the full path of the exe is in the prompt and in `PTK_SCHEDULER_EXE`). It writes
`<log>\moves\<yyyyMMdd-HHmmss-fff>-slot<N>.lock` (header `Card:`, `Slot:`, `Paths:` one per line, `Why:`; tmp then
rename, CM-1) and waits up to 110 seconds for `<log>\moves\replies\<same name>.reply`, printing one line every
30 seconds. Its exit code and last line are the answer:

| Exit | Answer | The worker then |
|---|---|---|
| 0 | `granted` | edits the files. |
| 2 | `waiting: held by 990-B, gives up at 10:42` | runs the same command again (it finds the open request and keeps waiting; no new request). |
| 3 | `yield: <path> held by 990-B (older)` | commits its work to `wip/<card>`, journals where to resume, hands the card back To: active as `ready`, ends `LANE-RESULT: yielded`. |
| 4 | `refused: <reason>` (too broad, unknown card) | narrows the request, or parks and asks. |

Splitting the wait into calls of under two minutes keeps within the prompt's rule that no command waits longer.
A worker that cannot run the exe (no Windows host) falls back to the README's rule: park and ask.

**Spark workers (DL-27)** (local model, copilot CLI) follow the same routine as Claude ones (amended by 1018-D): the command
and the prompt text below are the same for every agent. For a small model the prompt stays short and literal (the
command written out with the exe's full path, one sentence per answer), and the `lock` command's output is **one line**
with an **exit code** (the table above), so the worker reads the code, not prose. A spark worker that forgets is caught
by the push check (below), as any worker is.

**Prompt text** (WorkerPrompt, scope mode), replacing "stay inside the card's scope":

> You hold the card's scope (listed above) for the whole session. Read any file you like. Before you change,
> add or delete a file outside what you hold, ask for it: `<exe> lock <path> --why "<reason>"`, and wait for
> the answer (run it again while it says waiting). If it says yield, another card was here first: commit your
> work to wip/<card>, write in Result where you stopped and what you were about to change, hand the card back
> with a move request To: active and Status: ready, and end with LANE-RESULT: yielded. A push to main that
> touches a file you do not hold is refused, and says which file and how to ask for it.

**README** (the kit's `assets/templates/jobs/README.md`, then each project's copy by `-Update`): in
"Categories", the Locks bullet gains "under the scheduler, ask for a file you need with the lock command in your
prompt instead of parking"; a short "Locks while you work" paragraph in step 4 with the table above; "yielded"
added to the session report's LANE-RESULT words. Run by hand, nothing changes: there is no scheduler to ask.

**A worker that forgets to ask** (a spark worker, say) edits a file, commits, passes the gates and pushes. The
hook refuses the push: `994-D does not hold docs/known_gaps.md ... ask: <exe> lock docs/known_gaps.md --why ...`.
It asks, and gets granted (pushes again), waiting (asks again) or yield (its commits go to `wip/<card>`, which the
resumed run picks up). A worker that bypasses the hook reaches main, and DL-19 turns its finish into review.

## 4. Why wait-die (and not the alternatives)

| | Wait-die (chosen) | Wound-wait | Wait-for graph |
|---|---|---|---|
| Who is aborted | the younger *requester* | the younger *holder* | a victim on a cycle |
| Deadlock | impossible: waits only go older to younger | impossible | found and broken |
| Starvation | none with fixed age (DL-11) | none with fixed age | possible without an age rule |
| Cost of an abort here | the requester is at a clean point it chose, about to change one file; it commits WIP and writes its resume note | the holder is mid-edit at an unknown point; it notices only at its next step check (like a stop file), so the abort is slow and loses context | same as wait-die for the victim, but rare |
| Waiting workers | only older cards wait, and only up to 20 min | younger cards wait for older ones | anyone waits, possibly for an hour, burning a worker and its time box |

An "abort" here is not a cheap rollback: it ends a session. Wait-die aborts the session that is at the best moment
to stop (it has just found the conflict and nothing is half-written) and never interrupts a holder. Wound-wait
would stop the card that has done most of its work. Graph detection aborts less often, but lets young cards sit
waiting on long holders, and a waiting session costs a worker slot, tokens and time box while doing nothing; with
at most a handful of workers, freeing the slot (a yield) is worth more than keeping a session alive. The page still
draws the waits as a graph (DL-25).

Expected rate: dispatch already keeps declared scopes apart, so a conflict needs a card to reach for a file another
running card holds. DL-26 counts yields; if they are frequent, the remedy is better planner scopes, not a different
scheme.

## 5. Failure cases and recovery

| Case | What happens |
|---|---|
| Worker asks, then dies or is killed before reading the answer | The open request is dropped with its card's locks (DL-8 d); granted entries go to the remembered set. |
| Scheduler stops while a request is open | DL-21: an adopted worker's request is re-evaluated on the first tick with its old deadline; the worker's next `lock` call finds the reply. |
| Lock table file lost or corrupt | DL-21: starts empty, falls back to initial claims, logs it; DL-19 audit resumes from main's head. Strays in the gap are not seen. |
| Reply file not written (scheduler hung) | The `lock` command prints "no answer yet" and exits 2; the worker retries; the watchdog sees the scheduler's own trouble as today. After its own deadline the worker treats it as yield. |
| Finish pushed but card move fails (CM-18) | Locks held until the finish is on main (DL-9). |
| Two requests from one card at once | The second is answered `waiting` behind the first and merged into it; one open request per card. |
| Older card waits, holder runs to its time box | The older one's wait limit (DL-13) ends first, and it yields; it restarts first when the holder is released. |
| A yielded card's holder parks (needs-human) | The holder's locks are released (DL-8 b); the yielded card can start. The parked card gets them back only when free (DL-10). |
| Hook skipped | DL-19 audit; finish goes to review. |
| Page Stop / Kill | Release as DL-8 d; Kill keeps the remembered set, so the WIP on `wip/<card>` (if any) can be resumed. |
| Worker pushes a file covered by an add-only exemption but deletes lines | DL-16 warning; not refused. |
| Scheduler restarts while a claim has not landed | DL-21: the starting entry is released; the card is in `queue/` (claim lost) or an orphan in `active/` (claim landed), and dispatch starts it again. |
| Card resumed by hand (no scheduler) | No locks; the README's rules as today. The table entry is released when the scheduler next sees the card is not running. |

## 6. What changes in the code

### Core (category A)
- `Scheduler.Core/Locks/` (new): `LockEntry`, `CardLocks` (age, held, remembered, open request), `LockTable` (pure
  grant/release/remember operations, DL-1, DL-5, DL-8, DL-10), `CardAge` (DL-11), `LockArbiter` (DL-5, DL-6,
  DL-12, DL-13: request + table + now -> granted, waiting, yield, refused), `LockRequest`/`LockRequests` (parse
  `.lock` files like `Moves/CardRequests`), `LockReply` (text of a reply), `LockTableText` (JSON, DL-20),
  `PushCheck` (DL-15 to DL-18: changed paths + line counts + held set -> refused paths, warnings),
  `MainAudit` (DL-19: commits with trailers and paths + table history -> strays).
- `Scopes/ScopeGate.cs`: running scopes come from held sets; a candidate's scope includes its remembered set (DL-1).
- `Results/RunResults.cs`, `Results/RunOutcome.cs`: the `yielded` result word (DL-14, spec C2/C8).
- `Moves/CardMoves.cs`: a stray turns a To: done into To: review (DL-19).
- `Launch/WorkerPrompt.cs`: the lock paragraph (section 3); `Launch/WorkerEnv.cs`: `PTK_CARD`,
  `PTK_SCHEDULER_EXE`, `GIT_CONFIG_COUNT`/`KEY_0`/`VALUE_0` for `core.hooksPath`; `Launch/Adoption.cs`: keep
  locks of adopted entries (DL-21).
- `Contracts/StateFile.cs` (or a new contract, since the table is per repository): where the table lives.
- `Tick/TickWake.cs`, `Tick/SchedulerTick.cs`: a `.lock` file wakes a light tick; the lock step runs after the
  move step (so releases from finishes come first) and before dispatch.
- `PageModel/`, `WebContract/`: held set, granted entries, open wait, yielded note, counts (DL-24 to DL-26).

### Host (category B)
- `Scheduler.Host/Locks/LockRequestStep.cs` (new): reads `.lock` files in name order, runs the arbiter, writes
  the table then the reply (`moves\replies\`), moves the request to `moves\done\` or `moves\failed\` (CM-18).
- `Scheduler.Host/Locks/HookFiles.cs` (new): writes `<log>\hooks\commit-msg` and `pre-push` (sh, as Git for
  Windows runs them) at start.
- `Scheduler.Host/Program/` and `Program.cs`: subcommands `lock` (section 3) and `check-push` (DL-18), which read
  files only and never start a scheduler.
- `Scheduler.Host/Git/`: `MainAudit` runner (`git log --name-only --format=%H%x00%(trailers:key=Card)` from the
  last audited commit) on every main move that CardReader already detects.
- `Scheduler.Host/Moves/CardRequestStep.cs`: release on an applied finish, park or send-back (DL-8, DL-9).
- `Scheduler.Host/Pool/` (adoption and run end): release on run end, keep on adoption (DL-8 d, DL-21).
- `Scheduler.Host/Tick/`: the moves-folder watcher wakes on `.lock` as on `.move`.
- `Scheduler.Tray/` swap: an update must not replace the exe while a `lock`/`check-push` call runs from it; the
  swap retries as it does for a running scheduler (check while building it).

### Web (category C)
- `Scheduler.Web/Views/CombinedPageRenderer.cs` (the pool page; amended by 1018-D) and the worker windows: Holds and
  Waits lines (DL-24, DL-25); the Queue rows' yielded note; the day's lock counts in the Projects window (DL-26).
  One-project mode is being retired: no work for the single page.

### Kit and docs (category D, and the kit's owner)
- `assets/templates/jobs/README.md` (then `-Update` into `jobs/README.md`): section 3's README changes.
- `spec.md`: DL rules into B2; `test-plans/dynamic-locks.md`: the test plan below.

## 7. Test plan

Each test named by its rule. Core tests are pure (no disk, no git); Host tests use a temp repository.

- **Grant** (DL-4, DL-5): free path granted; path inside own held set granted without a table change; one of two
  entries overlapping a holder -> neither granted; glob vs file (`vm/68000.*` vs `vm/68000.cpp`) conflicts;
  different repositories never conflict (DL-22); add-only paths never conflict (DL-15).
- **Narrowness** (DL-6): `**`, root, a category's default, a folder over 200 files -> refused with the text.
- **Wait-die** (DL-11, DL-12): older requester -> waiting; younger -> yield naming path and holder; high-priority
  higher number is older than normal lower number; age fixed at first claim survives a priority change.
- **Wait limit** (DL-13): waiting -> yield at `lockWaitMinutes`; and at the warn time when that is sooner.
- **Release** (DL-8, DL-9): finish applied -> released and a waiting request granted in the same tick; finish not
  pushed -> still held; park, send-back, fail, kill -> released, remembered set written.
- **Resume** (DL-10, DL-1): yielded card not offered while its remembered entry is held (ScopeGate note names the
  holder); offered once released; its initial claim includes the remembered set.
- **Results** (DL-14): `yielded` gives the run back, never below 0; does not count toward stuck.
- **Push check** (DL-15 to DL-18): own card file, `jobs/requests/` new file, add-only file -> allowed;
  another card's card file, `jobs/plans/`, `jobs/.logs/` -> refused even when held; held `jobs/card-template.md` -> allowed; held path -> allowed; unheld path -> refused naming it and the command;
  `wip/` ref -> allowed; version line lowered or equal -> refused; add-only file with deleted lines -> warning.
- **Audit** (DL-19): commit with trailer and held paths -> clean; unheld path -> stray; no trailer while workers
  run -> stray; scheduler's own commits ignored; a stray turns To: done into To: review.
- **Table** (DL-20, DL-21): round trip; unreadable -> empty with a log line; restart keeps adopted cards' locks
  and request deadlines, releases the rest to remembered sets.
- **Host** (temp repository): hooks installed through the environment fire (trailer appended, unheld push refused
  with exit code non-zero, `wip/` push allowed); `lock` command: granted, waiting then granted across two calls,
  yield exit 3, timeout prints and exits 2.
- **Prompt** (section 3): scope-mode prompt contains the lock command with the exe's full path and the yield steps.
- **Claims** (DL-1, DL-21; 995-B): a starting card counts against candidates and has no held set; held set recorded
  at `Landed`; failed claim drops the entry; restart releases a starting entry.
- **Spark** (section 3): the lock command's output is one line and an exit code.
- **Page** (DL-24 to DL-26): holds, waits and yielded notes rendered from a model, on the pool page.

## 8. Cards (proposed, in order)

1. **A: lock table and arbiter** - `Scheduler.Core/Locks/` (LockTable, CardAge, LockArbiter, LockRequest(s),
   LockReply, LockTableText); DL-1, DL-4 to DL-13, DL-20 to DL-22. Tests first.
2. **A: dispatch, results and prompt** - ScopeGate on held sets and remembered sets; `yielded`; WorkerPrompt lock
   paragraph; WorkerEnv variables; TickWake for `.lock`; the tick's lock step. Depends on 1.
3. **A: push check and audit logic** - `PushCheck`, `MainAudit`, the stray-to-review rule in CardMoves. Depends on 1.
4. **B: lock requests in the host** - LockRequestStep, replies, table file, release on finish/park/run end,
   adoption, the `lock` subcommand. Depends on 1, 2.
5. **B: hooks and audit in the host** - HookFiles, `check-push` subcommand, the audit runner; the Tray swap check.
   Depends on 3, 4.
6. **C: page** - holds, waits, yielded notes, counts, on the pool page. Depends on 2.
7. **D: spec and test plan** - DL rules into spec B2; `test-plans/dynamic-locks.md`. Can run beside 1.
8. **Kit README** (the kit owner's card): section 3's README text, then `-Update`. Lands with or after 4, before
   locks are switched on.
9. **Switch-on trial** (developer and planner): a `locks` switch in scheduler.json (default off until here); two
   cards with neighbouring scopes, one that is told to need the other's file; check grant, wait, yield and the
   refused push on the page. The trial includes a **spark** card (amended by 1018-D), told to need a file it does
   not hold, to show a local model follows the lock routine.

Cards 1, 3 and 7 can run side by side; 4 and 6 after 2.

## Open points (also the card's Questions, with recommendations)

1. **Wait-die** as the conflict rule, with a 20-minute wait limit for older cards. Recommended (section 4).
2. **Enforcement**: the scheduler's git hooks in the worker's environment plus an audit of main, workers keep
   pushing to main (recommended); or workers push to a card branch and the scheduler checks and fast-forwards main
   (airtight, but conflicts and waits move to the scheduler).
3. **Default scope**: keep the category default for a card with no Scope line (recommended for now: dynamic locks
   cover what it misses, and DL-26 will show whether a narrower default, say the card's own card file only, would
   work).
4. **Order of work**: after multi-project, before the page views (recommended): multi-project lets idle workers take
   AmigaX's cards, which raises use of the pool sooner and with less change; locks are designed per repository
   from the start (DL-22), so nothing has to be redone.
5. **A stray's finish goes to review** (DL-19), rather than only being logged. Recommended: a change outside the
   locks may have collided with another card, and the developer should see it.
