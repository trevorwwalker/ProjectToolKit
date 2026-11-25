# In-memory model of main (git performance): design and plan

Card 1058-D, 2026-11-22. Approved by the developer the same day (answers: own object reader with CLI fallback; the
review panel keeps its one `git log --numstat`; no disk snapshot unless the startup gate fails; "a click starts no git"
covers read-only clicks; a shared fetch is reused for up to one poll), carded (section 9) and built by cards 1060-A to
1067-B. **`spec.md` section A14 (card 1069-D) now holds MM-1 to MM-18 as current behaviour**; this document stays as the
design, the cost tables and the test plan. Rule IDs `MM-1` onward ("model of main")
are stable names for test cases. (`SC-` is taken: spec B2's scope
rules SC-1 to SC-8.)

Roadmap: the git performance step (`jobs/plans/scheduler-roadmap_plan.md`, "Order of the remaining work", step 5). It replaces card
1045-B (in-process git reads, folded in and archived) and reshapes 1044-B, 1046-B and 1047-B (section 9).

Sources: the card's decisions (developer, 2026-11-22 00:08-00:11); `docs/perf/git-baseline.md` (1041-B, measured
2026-11-21 at 74c8044); cards 1043-B, 1044-B, 1045-B, 1046-B, 1047-B, 1032-B (startup gate); `views.md` (VW-20 to
VW-23, its done reader); `dynamic-locks.md` (DL-22, per repository); `card-moves.md` (CM-1, CM-12); the code on main
at `d4dd143`: `Scheduler.Core/Contracts/IMainRepository.cs`, `Card.cs`, `Scheduler.Core/Cards/` (CardFiles, MainFetch,
RequestFiles), `Scheduler.Host/Git/` (MainRepository, IBlobBatchReader, BlobBatch, CardReader, RequestReader,
MainFetcher, GitWriter, WipBranches), `Moves/AnswersCommitter.cs`, `Program/ProjectParts.cs`, `Tick/TickHost.cs`,
`Tick/HostPageData.cs`, `PageActions/` (HostPageActions, CardWorkerLine, ReviewCommits), `Planner/` (PlannerStep,
AnswersPlannerMain, and Core's PlannerApply), `Pool/` (PoolProjectReader, ScopeAudit), `Locks/AuditRunner.cs`,
`Templates/TemplateRefresh.cs`, `Config/` (ConfigLoader, RepositoryLocator), `Processes/FinishedCardCheck.cs`,
`Program/HealthWorktreeRunner.cs`, `Program/CheckPushCommand.cs`, `Slots/SlotWorktrees.cs`; and both repositories on
this PC (counts below are from 2026-11-22).

## 0. What exists today, and what that means

- **Every read of main is a git process.** `MainRepository` (one per project, made in `ProjectParts`) implements
  `IMainRepository` with the CLI: `HeadSha` is `rev-parse`, `ListTree` is `ls-tree -r`, `ReadFile` is
  `show <ref>:<path>`, `ReadBlob` is `cat-file blob`, and `IBlobBatchReader.ReadBlobs` is one `cat-file --batch`
  (1013-B). The 1041-B baseline counts what that costs with 2 projects: idle 24 git starts per 30 s, a page click
  (Tick now) 84-93 (`show` 59-60 of them), start 81-92, planner batch 67-83, move request 4-50, health check 29,
  claim to finish 140-215. Each git start by name is 2-3 processes more (the cmd wrapper, mingw64's git, `sh`), and a
  real remote adds about 8 (git-remote-https, the credential helper). Process starts are what is slow on this PC
  (1045-B: `git --version` 106-2,239 ms under load; 1026-B: most of the time in long git calls was the lock .NET holds
  around CreateProcess).
- **There are caches, but each reader has its own.** `CardReader` caches parsed cards by blob id and path (CARD-1)
  and skips the listing when main's sha has not moved (987-B), but asks for the sha with a `rev-parse` every read.
  `RequestReader` does the same for requests. Everything else - `scheduler.json`, `categories.json`, `workers.json`,
  `models.json`, `README.md`, a card's text for the page, a review's Result, the planner's current texts - is a fresh
  `git show` each time it is wanted, per project, per tick or per click. views.md plans one more reader of its own for
  done cards (VW-21). **So the fix is one model per repository that every consumer reads**, not more caches.
- **Done cards are never opened** (CARD-5): `CardReader` keeps a done card as its path and blob id only. The views
  (VW-21) need done cards' headers. The model reads them once, in process, after the open cards (MM-5), and the
  views take them from it: one model, not two.
- **The `Card` record holds parsed fields, not the text** (`Contracts/Card.cs`: folder, path, id, title, status,
  priority, time box, worker, scope, locks, type, version, plan, depends, questions, why, latest journal, blob id).
  The text is read again when it is shown. The model keeps the same split (MM-4): parsed fields in memory, text read
  from the object store on demand, in process.
- **The local tracking ref moves without our fetch.** Slots are worktrees of the project's repository
  (`SlotWorktrees`: `worktree add -B <branch> <dir> origin/main`), and so is the `answers` worktree
  (`AnswersCommitter`). A worker's `git push origin HEAD:main` from its slot updates `refs/remotes/origin/main` in the
  shared git folder when it succeeds, and so does the developer's fetch in his checkout. Reading that ref in process
  costs a file read, so the model can see a worker's push at the next tick, often before any fetch (MM-8).
- **Who fetches today.** `MainFetcher` (1002-B) per project: a poll, a worker exit, a planner batch and a health
  check fetch; a click or a move request does not. The planner (`PlannerStep.Fetch`), the answers worktree
  (`AnswersCommitter.AnswersTree`: a fetch per try, one more when it makes the worktree), the health worktree (`HealthWorktreeRunner`) and
  `WipBranches` fetch again on their own. Two projects in one repository fetch it twice.
- **Who writes.** `GitWriter` (987-B) is the one writer of the scheduler's commits: each goes through the `answers`
  worktree (`fetch`, `checkout -B`, `reset --hard`, `clean`, the change, `add`/`mv`/`rm`, `commit`, `push`, up to
  three tries; MOVE-1). After a push succeeds the writer knows the new commit, its parent and every path it wrote.
- **Reads of history.** The locks audit (`AuditRunner`: `log --reverse --no-renames --name-status` from the last
  audited head), the scope audit (`ScopeAudit`: `diff --name-only from to`), the review panel's commits
  (`ReviewCommits`: `log --grep=<card> --numstat`), the template check (`TemplateRefresh`: `log --raw` of each
  template path in ProjectToolkit's repository) and `WipBranches` (`merge-base`, `diff` of a wip branch).
- **Reads that must stay git.** `CheckPushCommand` runs inside a git hook on a push (it sees the pushed objects
  only through git); slot, health and answers worktrees are made and reset with the CLI; fetch and push are remote
  operations. These stay CLI (MM-17).
- **The repositories.** ProjectToolkit: 1 pack (3 MB), a commit-graph, 20 packed refs, `core.fsmonitor=true`, format
  version 0. AmigaX: 2.29 GiB in 4 packs, with a **multi-pack index** and a **cruft pack** (`.mtimes`), 22,397 files
  on main, 785 of them under `jobs/` (6.1 MB). Every pack has its own `.idx` (the multi-pack index is extra, never the
  only index). Neither uses `extensions.*` (no SHA-256, no reftable, no partial clone, not shallow). On a loaded PC
  `ls-tree -r origin/main -- jobs/` took 1.58 s and one `cat-file --batch` of all 785 jobs blobs 0.21 s: the process
  start, not the reading, is the cost.
- **LibGit2Sharp is not available here.** It is not in this PC's NuGet cache, a worker may not download it, and the
  product has no third-party package today (`Directory.Packages.props` lists only test packages). It would also bring
  a native libgit2 per runtime, libgit2's ownership check (`safe.directory`), and on Windows its memory-mapped pack
  files keep `git gc` and `git worktree remove` from deleting packs while a repository handle is open. **The design
  uses a minimal reader of our own** (section 1, MM-1 to MM-3; open point 1): refs (loose and packed), loose objects,
  pack `.idx` v2 and `.pack` entries with both delta kinds, trees, commits. .NET has zlib (`ZLibStream`) and SHA-1.
  It reads; it never writes the repository.

## 1. Model

Per **repository** (keyed by the repository's full path, as the lock table is, DL-22; projects sharing a repository
share it) the Host keeps one **model of main**: an immutable snapshot at one commit of `<remote>/<branch>`, replaced
whole when main moves. Readers take the current snapshot (one reference read, no lock) and keep it for their step,
so a tick, a page render or a planner pass sees one commit throughout.

| Part | Each item | Kept |
|---|---|---|
| Commit | the sha of `<remote>/<branch>` the snapshot is at; its time | always |
| Jobs tree | path to blob id for every file under each project's jobs folder (`queue/`, `active/`, `review/`, `done/`, `archive/`, `plans/`, `requests/`, the jobs files) | always |
| Open cards | queue/, active/, review/: the parsed `Card` (as `CardFiles.ParseText` gives it today) | always |
| Done cards | done/ and archive/: the header fields the views need (title, status, type, plan path and step, version, depends, time box, worker), with id, number, category, folder, blob id | after the second load phase (MM-5) |
| Requests | `jobs/requests/` parsed as `RequestFiles` does today | always |
| Settings files | the text of `jobs/scheduler.json`, `categories.json`, `workers.json`, `models.json`, `README.md` per project, and any other file a consumer asks for by path (MM-6) | always, by blob id |
| Watched files | files outside `jobs/` a consumer has asked for (plans and design documents the views name, `VERSION`): path to blob id; text by blob id | from the first ask |
| Commit log | per commit since the model was loaded: sha, parent, time, subject, changed paths with old and new blob id | grows as main moves; capped (MM-12) |
| Pool view | each project's `PoolProjectView` inputs (its scheduler.json and README at main) | always |

What is **not** kept: the text of cards (the page reads it from the object store on demand, MM-4), the text of
done cards, whole-repository trees (only the jobs subtrees and watched paths are listed), and any working tree's
state.

Beside the snapshot, per repository: the **object reader** (MM-1 to MM-3), the **fetch gate** (MM-9) and the
**advance step** (MM-7, MM-10, MM-11), the only code that makes a new snapshot.

## 2. Rules

### Reading the repository in process

- **MM-1 One reader, read only.** `MainObjects` reads a repository's refs and objects in process: the git folder
  (a `.git` file's `gitdir:` and a worktree's `commondir` followed to the common folder); refs from the loose file,
  else `packed-refs` (a loose ref wins; peeled lines skipped); objects loose (zlib) or in packs through each pack's
  `.idx` v2 (fanout, sorted ids, 32- and 64-bit offsets); pack entries commit, tree, blob, tag, `OFS_DELTA` and
  `REF_DELTA` (a delta base looked up in any pack or loose), with delta bases cached per read by offset. It ignores
  the multi-pack index, commit-graph, bitmaps, `.rev` and `.mtimes` (none is needed to find an object). It never
  writes into the repository, never takes git's locks, and computes no object ids (it only looks them up).
- **MM-2 What it refuses, and the fallback.** A repository with `extensions.objectFormat` other than sha1,
  `extensions.refStorage` (reftable), a promisor remote or partial clone filter, `objects/info/alternates`, a shallow
  file, or a format version above 1 with any extension it does not know is refused at load: that repository's model
  is read through the CLI reader instead (today's `MainRepository`, unchanged, MM-16) and the startup block logs
  `main model: <repo>: git CLI (<reason>)`. A setting `gitReader: "cli"` (pool file, then scheduler.json) forces the
  CLI reader everywhere. An object it cannot find or parse (a corrupt pack, a pack deleted under it) fails that read
  only: the advance keeps the previous snapshot (as CARD-3) and the next advance tries again.
- **MM-3 No handle across ticks.** Pack and index files are opened with `FileShare.ReadWrite | FileShare.Delete` for
  one advance or one on-demand read and closed after it; nothing is memory-mapped. The pack list is read again when
  an id is not found (a fetch or gc made a new pack). `.idx` contents are kept in memory per pack (by file name,
  size and time; AmigaX's 66,092 objects are about 2 MB), so a lookup opens only the `.pack`. git gc, repack and
  `worktree remove` are never blocked by the scheduler.

### What the model holds and when it is loaded

- **MM-4 Parsed fields in memory, text on demand.** Open cards are kept as `Card`, parsed by the code that parses
  them today (`CardFiles.ParseText`, `RequestFiles`). A consumer that needs a file's text (the page's card view, the
  review panel's Result, the planner's current text, a page action's edit) gets it from the model by path at the
  snapshot's commit: the model looks the blob id up and the object reader inflates it, in process. Small settings
  files (MM-6) keep their text.
- **MM-5 Load at startup, in two phases.** A repository's model is built at startup by reading the tracking ref and
  walking only the jobs subtrees (and the settings files) in process. **Phase 1**: open cards, requests and settings
  files; when it is done, the repository's projects join (as 1015-B joins them in the background today), so the
  page and the first tick never wait for done cards. **Phase 2**, right after, in the background: done and archive
  headers (the views say "reading N done cards..." until it ends, VW-21). Repositories load in parallel, at most
  `PTK_JOBS` at once. There is no disk snapshot: the model is rebuilt at every start (open point 3).
- **MM-6 Any path can be watched.** A consumer may ask the model for a file outside the jobs folder (a plan, a design
  document, `VERSION`). The first ask resolves the path at the snapshot's commit in process and adds it to the
  watched set; every advance keeps watched paths current by the same tree diff. Unwatched paths are never read.

### Refresh only on change

- **MM-7 The advance step.** The model moves from commit A to commit B only through the advance step, one at a time
  per repository: diff A's tree against B's in process, descending only into subtrees whose tree id differs and only
  along the jobs folders and watched paths; for each changed path, take the new blob id; parse only blobs not already
  known by id (a card moved between folders keeps its blob id and is not re-parsed: only its folder changes); drop
  what was deleted; walk the commits from B back to A for the commit log (MM-12). done/ is read only where it changed.
  The new snapshot replaces the old one in one assignment. An advance that fails keeps A (CARD-3).
- **MM-8 The ref is read every tick, in process.** At the start of each tick (and of the page's data step), the model
  reads `<remote>/<branch>` from the refs files (a stat and a small read, no process). If it differs from the
  snapshot's commit, the advance runs before anything reads. This picks up every way the ref moves: our fetch, a
  worker's push from its slot, the answers worktree's fetch, the developer's fetch in his checkout.
- **MM-9 One fetch per repository per poll, shared.** The fetch gate (the per-repository successor of `MainFetcher`
  and Core's `MainFetch`) decides when `git fetch -q <remote>` runs: at most once per poll interval per repository,
  plus once after a worker ends (today's `Ask`); a click or a move request never fetches (as 1002-B). Every consumer
  that used to fetch for itself - each project's tick, the planner pass, the health check's main sha, the audit -
  asks the gate instead and is answered by the fetch in flight or the last one, if it is younger than the poll
  interval (1046-B's freshness rule, with the poll interval as N; no new setting). The scheduler's own pushes need
  no rule of their own: a push moves the local tracking ref, and the writer applies the pushed commit to the model
  directly (MM-11), so a shared fetch from before it is never stale for it. A failed fetch makes the next ask fetch. The fetch is followed by MM-8's ref read.
  The answers, health and slot worktrees' own fetches stay where they are (they reset a worktree, MM-17), and their
  ref moves reach the model through MM-8.
- **MM-10 Nothing read when nothing moved.** When the ref equals the snapshot's commit, no tree, blob or file is read
  and no process starts: an idle tick costs the fetch the gate allows (if one is due) and a file read.

### The scheduler's own writes

- **MM-11 A write updates the model after its push.** When `GitWriter`'s commit is pushed (claim, move, page answer,
  page action, planner batch, the duplicate fix-up), the writer hands the model what it pushed: the new commit C, its
  parent P, and the paths it wrote or removed. If P is the snapshot's commit, the model applies the change directly,
  with no diff and no git process: it looks up only those paths in C's tree in process (C's objects are local: the
  answers worktree shares the repository's object store), takes their blob ids and parses their blobs as committed
  (not the writer's text, which `core.autocrlf` or a filter may have changed on commit), and the snapshot moves to C.
  If P is newer
  (the answers worktree's reset took a main that moved), the model first advances to P (MM-7; P's objects are already
  local, the answers worktree fetched them), then applies. The model never steps back: if P is not a descendant of the
  snapshot's commit within the walk limit (the snapshot is already past C, after a reload whose log does not hold C,
  or the two cannot be related), the push leaves the model as it is and the next ref read (MM-8) moves it. A rejected
  or failed push leaves the model as it was; the retry's fetch moves the ref and MM-8 picks it up.
- **MM-12 Others' pushes come through the ref.** A worker's push, the developer's, or another scheduler's (there is
  none, but nothing assumes it) is never guessed at: it reaches the model when the ref moves (MM-8, after MM-9's
  fetch for a push from outside this PC's worktrees). So the model is never newer than `<remote>/<branch>` and never
  older than the last fetch or local push, the same bound as today's reads (CARD-2), usually tighter. The commit log
  keeps the last 10,000 commits per repository or 30 days, whichever is more (the audits need only the commits since
  their last head; the review panel's search falls back to a CLI `log` only beyond the cap, MM-14).
- **MM-13 Pushes ready together go together.** When `GitWriter` holds several items for one repository that are ready
  at the same time (claims of a tick, a planner batch with a move), it makes them one commit per item, as today, but
  one push for all, keeping their order; if the push is rejected, the remaining items are retried from the new main
  one by one as today, so one bad item never drops the others (1046-B's push half). Each item's result is its own.

### Every read from memory

- **MM-14 The consumers.** Each read of main moves to the model, behind the interface it already uses where it has
  one (section 6):

  | Today | Git today | From the model |
  |---|---|---|
  | `CardReader.Read` (tick, page, dispatch, dependencies, queue order, locks) | rev-parse, ls-tree, cat-file | open cards; done cards (views) |
  | `RequestReader` | rev-parse, ls-tree, cat-file | requests |
  | `ConfigLoader` (scheduler.json, categories.json at main) | show | settings files |
  | `TickHost` (workers.json, models.json, main sha) | show, rev-parse | settings files, commit |
  | `PoolProjectReader` (scheduler.json, README.md) | show | pool view |
  | `HostPageData` (card text, request text, review Result) | show | text on demand (MM-4) |
  | `HostPageActions`, `CardWorkerLine`, `FinishedCardCheck` | show | settings files, text on demand |
  | `AnswersPlannerMain`, `PlannerApply.Current` | ls-tree, rev-parse, cat-file | jobs tree, text on demand (at the commit the batch is written on: the model advances to the answers worktree's commit first, MM-11) |
  | `AuditRunner` (locks audit) | log --name-status | commit log |
  | `ScopeAudit` | diff --name-only | in-process tree diff of the two commits |
  | `ReviewCommits` (review panel) | log --grep --numstat | commit log (subject, paths); line counts by an in-process line diff of the two blobs, cached by blob pair (open point 2) |
  | `TemplateRefresh` (ProjectToolkit's templates) | rev-parse, show, log --raw | a model of ProjectToolkit's repository watching `assets/templates/` (MM-6), with each template path's past blob ids from an in-process walk, cached |
  | `RepositoryLocator` (`rev-parse --show-toplevel`) | rev-parse | walk up to the `.git` folder or file, in process |
  | `HealthWorktreeRunner`'s main sha | rev-parse | commit |
  | `WipBranches` merge-base and diff | merge-base, diff | in process after its fetch of the wip branch (the fetch stays) |
- **MM-15 The target.** An idle tick and any page click that writes nothing start **no git process** unless the
  fetch gate allows a fetch. A page action or tick decision that writes costs only its write chain (MM-17). The same
  must hold at 50 projects x 5,000 cards: no per-card, per-file or per-project git process on the read path.
- **MM-16 The CLI reader stays.** `MainRepository` (CLI) stays, behind the same interface as the in-process reader:
  for refused repositories (MM-2), the `gitReader: "cli"` setting, and the parity tests (section 7). Both give the
  same model for the same commit.

### What stays git

- **MM-17 The CLI only where it must be.** Fetch (MM-9); push (MM-13); the answers, health and slot worktrees (made,
  reset, cleaned, committed in); `CheckPushCommand` in its hook; the wip branch's fetch and push. 1047-B then merges
  what is left where one call can answer (`for-each-ref` for several refs, one `worktree list --porcelain` per tick
  instead of per slot, one `status --porcelain=v2 --branch`), and 1043-B's one git executable runs all of it.
- **MM-18 Visible behaviour is identical.** The page, the queue order, dispatch, dependencies, the audits, the
  review panel and the log lines say what they say today; the only differences are when they see a change (sooner,
  MM-8) and how long a tick or click takes. The tick's log line that reports step costs gains `git <n>` (processes
  started in the tick) and `model <ms>` (the advance's time) so the gain stays visible.

## 3. Data flow

```
startup, per repository (parallel, at most PTK_JOBS):
  refs files --> tracking ref sha --> MainObjects: jobs subtrees + settings files --> snapshot (phase 1)
        --> its projects join --> phase 2: done/ and archive/ headers --> snapshot (with done)

every tick, per repository:
  fetch gate (MM-9): due? --yes--> git fetch -q origin (one process chain)
  refs files: ref == snapshot.Commit? --yes--> nothing read (MM-10)
                                       --no---> advance A..B in process (MM-7) --> new snapshot
  tick, page data, planner, audits, dependencies, locks: read the snapshot (no git)

a scheduler write (GitWriter thread):
  answers worktree: fetch, reset, change, commit, push (git CLI, MM-17)
  push ok  --> model.Apply(C, P, paths) (MM-11): P == snapshot? apply : advance to P, then apply
  push rejected --> model unchanged; retry from the new main

a worker's push from its slot (or the developer's fetch):
  refs/remotes/origin/main moves in the shared git folder --> next tick's ref read (MM-8) --> advance
```

## 4. Failure cases

| Case | What happens |
|---|---|
| The reader refuses a repository (MM-2) | That repository uses the CLI reader; logged at start; everything else as designed |
| An object cannot be read (corrupt or missing pack, gc in the middle) | That advance fails; the previous snapshot stays (CARD-3); the pack list is re-read and the next tick tries again; three failures in a row log `main model: <repo>: <reason>` once and switch that repository to the CLI reader until restart |
| A fetch fails | The gate makes the next ask fetch (as `MainFetcher` today); the model stays at the ref it has |
| The ref is moved backwards or to an unrelated commit (a force push) | The advance diffs the two trees as for any move (MM-7 needs no ancestry); the commit log is reset from the new commit and the audits start from it (as they do when their last head is not an ancestor today) |
| A push succeeds but the writer's handover fails or the process stops before it | The next tick's ref read sees C and advances by diff (MM-8): the same snapshot, a little later |
| The writer's path list misses a path its commit changed | Caught by the parity test (section 7: direct apply against a full diff); in production the listed paths' blob ids come from C's tree, so only a path left off the list could be stale, and the next advance that touches it corrects it |
| Phase 2 still reading | Views show "reading N done cards..."; tick and Workers view unaffected (they never read done headers) |
| A watched path does not exist at main | The model answers "missing" (VW-12 shows it so) and keeps watching it |
| A repository removed or moved while running | Its model's reads fail as git fails today; the project shows its error and the others go on |
| Memory pressure (far beyond the budget below) | Done headers are the bulk; nothing else grows with history except the capped commit log (MM-12) |

## 5. Performance

### Today (1041-B, 2 projects, local bare origins, loaded PC)

| Step | git starts by name | of which remote | Time, s |
|---|---|---|---|
| start | 81-92 | 12 | 116-131 |
| idle, 30 s (3 polls) | 24 | 4-5 | 30 |
| click: Tick now | 84-93 | 5-6 | 80-88 |
| move request: review -> done | 4-50 | 1-12 | 6-58 |
| planner batch, one card | 67-83 | 15-18 | 84-95 |
| health check | 29 | 5 | 44-45 |
| claim -> finish, one card | 140-215 | 28-38 | 242-361 |
| Host test suite | at least 1,858 | | 3 min 21 s green on a quiet PC; 343-502 s loaded |

### Target (the same 2 projects, after card 8 of section 9)

| Step | git starts by name, target | What remains |
|---|---|---|
| start | at most 4 per repository (one fetch, the answers and health worktree checks) plus slot adoption's own | fetch, worktree checks |
| idle, 30 s | the fetches only (3-4, one per repository per poll interval), 0 reads | fetch |
| click: Tick now (no write) | 0 | none |
| move request: review -> done | the write chain only, at most 8 | fetch, checkout, reset, clean, mv, commit, push |
| planner batch, one card | at most 10 (one gate fetch, the write chain) | fetch, write chain |
| health check | the health worktree's own (at most 6) | fetch, reset, clean, checkout |
| claim -> finish, one card | the polls' fetches plus at most 25 (claim and finish write chains, slot worktree) | fetch, push, worktree |
| Host test suite | at most 300 (the real-git set of section 7 and the worktree tests) | seed repositories copied, not built (1044-B) |
| Host test suite time, quiet PC | at most 1 min 30 s (from 3 min 21 s) | |
| startup gate (1032-B) | page at most 2.61 s, every project joined at most 36.5 s, as today or better | |

The fetch count does not grow with the number of consumers or of projects in a repository, only with repositories.

### At scale: 50 projects x 5,000 cards

Measured on this PC (loaded): AmigaX's 785 jobs blobs are 6.1 MB, about 7.8 KB a card; one `cat-file --batch` of all
of them took 0.21 s, most of it the process. The numbers below are estimates for the build cards to confirm (card 3
and card 8 measure them; MM-18's log line shows them in use).

| Item | Estimate | Budget the cards hold to |
|---|---|---|
| Done card header in memory (strings interned for category, folder, plan, worker; status cut to 200 characters as VW-13) | about 0.6 KB | 1 KB |
| Open card (`Card`, parsed) | about 2 KB | 4 KB |
| Per project at 5,000 cards (4,900 done, 100 open, requests, settings, jobs tree index) | about 4 MB | 8 MB |
| 50 projects | about 200 MB | 400 MB |
| Pack indexes held (`.idx`, per repository: about 30 bytes an object) | 2 MB for AmigaX | 10 MB per repository |
| Commit log (10,000 commits, about 10 changed paths each) | about 8 MB per repository | 16 MB |
| Startup, one 5,000-card repository: phase 1 (100 open cards, settings) | under 0.2 s | 1 s |
| Startup, phase 2 (4,900 done blobs, 40 MB inflated, deltas resolved) | 1-2 s | 4 s, in the background |
| Startup, 50 repositories, 5 at a time | phase 1 about 2 s; phase 2 about 10-20 s in the background | every project joined at most 36.5 s (today's gate, for 2) |
| Refresh, one card moved queue -> active | 4 tree objects each side (root, jobs, two folders), 1 blob: under 5 ms | 20 ms |
| Refresh, one card moved into done/ of 5,000 | the done/ tree (about 300 KB) inflated and parsed on both sides: 5-10 ms | 50 ms |
| Refresh, a planner batch of 20 new cards | 20 blobs plus the trees: under 30 ms | 100 ms |
| Idle tick, nothing moved | one stat and read of the refs files: under 1 ms | 5 ms |

If phase 2 at 50 repositories cannot hold the startup gate, the first answer is a disk snapshot of done headers by
blob id (open point 3); nothing in the model's shape changes for it.

## 6. What changes in the code

### Core (category A)

- `Contracts/IMainModel` (new): the read side every consumer uses: `Commit`, `Cards` (open), `DoneCards`, `Requests`,
  `Settings(path)`, `Text(path)` (on demand), `Entries(folder)`, `Changed(from, to)`, `Commits(since)`. `IMainRepository`
  stays for the CLI reader and the parity tests (MM-16); its callers move to `IMainModel`.
- `MainModel/` (new, pure): `MainSnapshot` (the immutable parts of section 1), `MainSnapshotBuilder` (from tree
  entries and texts; apply a change set: MM-7, MM-11), `TreeDiff` over an abstract tree source (MM-7), the done header
  parse (with views.md's `CardFiles` header-only parse), `FetchGate` (MM-9, replaces
  `Cards/MainFetch`), all with a fake clock and no I/O.
- Test helper (Core test project, shared with Host tests through a linked file or a small test library):
  `FakeMain` - an `IMainModel` built from a dictionary of path to text, with `Push(paths)` to move it, counting reads.

### Host (category B)

- `Git/Objects/` (new): `MainObjects` (MM-1 to MM-3): `GitFolder` (gitdir, commondir), `Refs`, `LooseObjects`,
  `PackIndex`, `PackReader`, `Delta`, `TreeParse`, `CommitParse`; refusal checks (MM-2).
- `Git/MainModels` (new): one per repository: load (MM-5), the fetch gate's process (MM-9), the advance (MM-7, MM-8),
  `Apply` from the writer (MM-11), the reader fallback (MM-2, MM-16).
- `Git/GitWriter`, `Moves/AnswersCommitter`: hand the pushed commit and paths to the model (MM-11); one push for items
  ready together (MM-13).
- The consumers of MM-14 switch to `IMainModel`; `CardReader` and `RequestReader` become thin views over the snapshot
  (or go, their CARD-1/CARD-3 behaviour now the model's); `MainFetcher` and `PlannerStep.Fetch` go to the gate.
- `Program/ProjectParts`: one `MainModels` entry per repository, shared by its projects; the startup block logs the
  reader per repository (MM-2).
- The tick's step-cost log line: `git <n>`, `model <ms>` (MM-18).

### Web (category C)

- Nothing for the model itself. The views (views.md) read done headers from the model instead of their own reader
  (VW-21 amended: "the views take done/ and archive/ headers from the model of main, MM-5").

### Design (category D)

- `spec.md` takes over MM-1 to MM-18 when the cards land; CARD-5 is amended (done cards' headers are read once, in
  phase 2, for the views; the tick still ignores them); views.md's VW-21 and VW-22 point to the model.

## 7. Test plan

Most Host tests stop needing a repository: they get a `FakeMain` (no git, no folder) behind `IMainModel`. A small
real-git set covers the git layer, on seed repositories built once per test class and copied per test (a file copy,
no git; 1044-B), with per-repository config in the seed's `.git/config` instead of `-c` on each call.

Core (unit, no I/O):
- `MM_7_Advance_OnlyChangedSubtrees_MovedCardKeepsBlobNotReparsed_DeletedDropped_DoneUntouchedUnlessChanged`
  (a counting tree source).
- `MM_7_FailedAdvance_KeepsPreviousSnapshot`.
- `MM_9_FetchGate_OncePerPollPerRepository_SharedByConsumers_ClickAndMoveNeverFetch_WorkerEndAsks_FailedFetchAsksAgain`.
- `MM_10_RefUnchanged_NothingRead`.
- `MM_11_Apply_ParentIsSnapshot_Direct_BlobIdsComputed`; `MM_11_Apply_ParentNewer_AdvancesFirst`;
  `MM_11_RejectedPush_ModelUnchanged`.
- `MM_12_CommitLog_CappedAt10000Or30Days`; `MM_13_OnePushForReadyItems_RejectedRetriesRemainingInOrder_EachResultOwn`.
- `MM_5_PhaseOneJoinsBeforeDoneHeaders`.

Host, with `FakeMain` (no git process; each such test asserts zero git starts through 1043-B's one runner):
- The tick, page data, page actions, planner, dependency, locks-audit and pool tests that today build a repository
  only to have cards on main move to `FakeMain`. Their assertions do not change (MM-18).
- `MM_15_IdleTick_StartsNoGit`; `MM_15_PageClick_StartsNoGit`; `MM_15_ClickThatWrites_StartsOnlyTheWriteChain`.

Host, real git (seed repositories, copied):
- `MM_1_Reader_Parity_WithCli`: refs (loose over packed, packed only, peeled lines), loose and packed objects,
  `OFS_DELTA` and `REF_DELTA`, a repository after `git gc --aggressive`, one with a multi-pack index and a cruft pack
  (`git repack --cruft -d --write-midx`), a worktree's `.git` file and commondir. Same tree entries, blob texts and
  commit fields as the CLI.
- `MM_2_Refuses_Sha256_Reftable_Alternates_Shallow_Promisor_FallsBackToCli`.
- `MM_3_GcAndWorktreeRemove_NotBlockedByAnOpenModel` (gc prunes a pack while a model exists; worktree remove works).
- `MM_7_AdvanceParity_DiffEqualsFullReload` (random sequences of card moves, edits, adds, deletes on a seed).
- `MM_8_WorkerPushFromSlot_SeenByRefReadWithoutFetch`.
- `MM_11_DirectApplyEqualsAdvanceByDiff` (after a real answers-worktree push, with `core.autocrlf=true` and a card
  written with LF).
- `MM_14_ReviewCommits_InProcessMatchesGitLogNumstat`; `MM_14_AuditCommits_MatchGitLogNameStatus`.

Measured, not unit-tested (cards 3 and 8): the section 5 rows with `bin\git-baseline.ps1` (1041-B) on this PC with the
pool running, the startup gate (`bin\startup-gate.ps1`), and a generated 5,000-card repository for the scale rows.

## 8. How the model stays consistent (summary)

- It is always exactly one commit of `<remote>/<branch>` (MM-7): never a mix, never a guess.
- It moves forward only through the advance or the writer's apply, one at a time per repository (MM-7, MM-11).
- Everyone's pushes reach it through the tracking ref: ours at once (MM-11), workers' from their slots at the next
  tick (MM-8), anyone else's after the next fetch (MM-9, MM-12).
- A failure keeps the previous snapshot and is retried (MM-2, MM-7, section 4), as reads of main do today (CARD-3).

## 9. Cards (in order; 1-8 built as cards 1060-A to 1067-B, 9 is the seed-repository card, 10 is 1069-D)

Each is one session. Worker Opus for 1-6 (new code across the Host's reading paths), Sonnet for 7-10. All carry 1045-B's
"Constraints (all git-performance cards)" block (behaviour identical, before/after process counts and times against
1041-B, startup gate, design for scale, Rev bump).

1. **A - model core.** `IMainModel`, `MainModel/` (snapshot, builder, tree diff, done header parse, fetch gate) and `FakeMain`; tests MM-5, MM-7, MM-9 to MM-13 (Core). Scope: `code/src/Scheduler.Core/MainModel/`,
   `Scheduler.Core/Contracts/IMainModel.cs`, `Scheduler.Core/Cards/MainFetch.cs`, the Core tests for them. Worker: Opus.
   Depends on: this design approved.
2. **B - in-process object reader.** `Git/Objects/` (MM-1 to MM-3) with the real-git parity tests MM-1 to MM-3 on
   seed repositories (the seed fixture of 1044-B's first step: built once per class, copied). Scope:
   `code/src/Scheduler.Host/Git/Objects/`, its tests and the seed fixture. Worker: Opus. Depends on: 1043-B. May run
   beside 1.
3. **B - models per repository.** `Git/MainModels`: load in two phases, ref read, advance, fetch gate's process, reader
   fallback, startup log; wired in `ProjectParts` but read by nobody yet; measure startup and the scale rows on a
   generated 5,000-card repository. Scope: `Scheduler.Host/Git/MainModels*.cs`, `Program/ProjectParts.cs`. Worker:
   Opus. Depends on: 1, 2.
4. **B - the tick reads the model.** `CardReader`, `RequestReader`, `ConfigLoader`, `TickHost`, `PoolProjectReader`,
   `FinishedCardCheck`, `MainFetcher` -> gate; their Host tests to `FakeMain`; MM-15 idle test; the step-cost log line
   (MM-18). Scope: those files and their tests. Worker: Opus. Depends on: 3.
5. **B - page and planner read the model; writes update it.** `HostPageData`, `HostPageActions`, `CardWorkerLine`,
   `AnswersPlannerMain` and Core's `PlannerApply` reader, `PlannerStep.Fetch` -> gate; `GitWriter`/`AnswersCommitter`
   apply after push (MM-11); tests to `FakeMain`; MM-15 click tests; MM-11 real-git parity. Scope: those files, plus
   `Scheduler.Core/Planner/PlannerApply.cs` by lock. Worker: Opus. Depends on: 4.
6. **B - history in process.** `AuditRunner`, `ScopeAudit`, `ReviewCommits` (per open point 2), `TemplateRefresh`
   (a model of ProjectToolkit's repository), `RepositoryLocator`, `HealthWorktreeRunner`'s sha, `WipBranches`'
   merge-base and diff; MM-14 parity tests. Scope: those files and their tests. Worker: Opus. Depends on: 5.
7. **B - 1046-B reshaped: one push for items ready together** (MM-13); the fetch half is already done by 3-5. Scope:
   `Git/GitWriter.cs`, `Moves/AnswersCommitter.cs`, tests. Worker: Sonnet. Depends on: 5.
8. **B - 1047-B reshaped: merge the remaining CLI calls** (MM-17) and **measure**: section 5's rows after, with
   `bin\git-baseline.ps1`, the startup gate, and `docs/perf/git-baseline.md` regenerated beside the old numbers.
   Scope: the remaining call sites (`Slots/`, `Program/HealthWorktreeRunner.cs`, `Moves/AnswersCommitter.cs`), `docs/perf/`.
   Worker: Sonnet. Depends on: 6, 7.
9. **B - 1044-B reshaped: seed repositories for the real-git set**; every remaining test that builds a repository
   copies a seed; target: Host suite git starts at most 300, suite at most 1 min 30 s on a quiet PC. Scope:
   `test/unit/Scheduler.Host.Tests/` fixtures. Worker: Sonnet. Depends on: 1042-B, 1043-B, 6.
10. **D - spec (done, 1069-D).** MM-1 to MM-18 into `spec.md`; CARD-5 amended; views.md VW-21/VW-22 pointed at the model; roadmap
    status. Scope: `docs/design/tdd/scheduler/spec.md`, `views.md`, `state-cache.md`,
    `jobs/plans/scheduler-roadmap_plan.md`. Worker: Sonnet. Depends on: 8.

The views' cards (views.md section 8) take done headers from the model: their card 2 ("B - views source") depends on
card 3 here and drops its own done reader.

## Open points (also the card's Questions, with recommendations)

1. **Our own object reader or LibGit2Sharp.** LibGit2Sharp would need a download a worker cannot make, a native
   library, and it holds pack files open (blocking gc and worktree removal on Windows). Our reader is about 800-1,200
   lines of read-only code with parity tests against the CLI, and falls back to the CLI for anything unusual.
   **Recommended: our own reader** (MM-1 to MM-3), CLI fallback kept (MM-16).
2. **The review panel's line counts.** Opening a review shows the card's commits with lines added and removed
   (`git log --numstat`). In process that needs our own line diff of two blobs (cached; small). **Recommended: in
   process**, so a click starts no git; the alternative keeps this one `git log` per review panel opened.
3. **A disk snapshot.** None now: the model is rebuilt at start (the developer's decision). **Recommended: add one
   only if the startup gate fails** once many repositories join (done headers by blob id, the first thing to save).
4. **Clicks that write.** A page action that changes main (Done, Send back, an answer) still runs its write chain
   (fetch, reset, commit, push: about 7 git starts, fewer after card 8). **Recommended: the target "a click starts no
   git" covers clicks that only read; a click that writes costs only its write chain.**
5. **How fresh a shared fetch is.** **Recommended: the poll interval** (no new setting): a consumer reuses a fetch
   younger than one poll (the scheduler's own pushes move the tracking ref and the model directly, MM-11); workers' pushes from their slots
   are seen at the next tick without any fetch (MM-8).
