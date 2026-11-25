# Worker types with caps (roadmap item 9): design and plan

Card 530-D, 2026-11-19. Design only: nothing here is built yet. It is written to settle the open
points with the developer and then be carded (section 6). Rule IDs `WT-n` are stable names for test
cases, like the spec's. Once the cards land, `spec.md` takes these rules over (card 1 of section 6).

**Update 2026-11-23 (cards 1146-A, 1148-D): the `premium` flag is gone.** A card with no `Worker:` line goes to the
pool's `defaultWorker` type; any other type must be named; `Worker: premium` is no longer special and a type may be
named Premium. Text below that still says premium is marked *superseded*; it is kept as history.

Sources: roadmap item 9 (`jobs/plans/scheduler-roadmap_plan.md`); spec A3 (DISP-*), A5 (WRK-*), A6
(TIME-10, TIME-12, TIME-15), A9 (ADOPT-*), B1 (MP-*) and B3 questions 4, 11, 13, 14 with the
developer's answers on cards 300-A, 310-A and 330-B; the code in `Scheduler.Core/Dispatch/`,
`Scheduler.Core/Categories/WorkerSelection.cs`, `Scheduler.Core/Pool/`, `Scheduler.Host/Pool/`,
`Scheduler.Host/Slots/`, `Scheduler.Web/Views/PageRenderer.cs` and `Scheduler.Web/Server/PageRouter.cs`.

## 0. What the code does today, and what that means for this plan

- **One project, fixed slots.** `SchedulerTick.Dispatch` gives each candidate, in DISP-14 order, the
  lowest-numbered slot that is enabled, idle, allowed the card's category, fits its `Worker:` line and
  whose server answers (the slot dispatcher, removed by 939-A; now `TypeDispatcher`, WT-1 to WT-6). A slot is one entry of `workers.json`
  (`WorkerDefinition`), enabled or not in the local `workers-state.json` (`WorkersEnabledState`).
- **The pool is built in Core only.** `PoolDispatcher`, `PoolGate`, `PoolShares`, `PoolWeights` and
  `PoolWorker` choose a card for *one given free worker* (a slot with a fixed model; `DispatchSlot` is removed, 939-A). No
  Host code calls them, and `PoolConfig` has no worker list yet (`docs/known_gaps.md`, 300-A and
  340-C). The running scheduler is single-project.
- **This project's workers.json**: 4 slots; slots 1 and 2 run the default agent (copilot on spark)
  and take only categories A, B and C; slot 3 (Sonnet) and slot 4 (Opus) are premium with no category limit.
  So today a design card (category D) without a `Worker:` line is never started: no non-premium worker
  takes category D. The migration (WT-C10) keeps that, and Q2 asks whether to keep it.
  *(superseded 2026-11-22: the premium flag is gone, 1146-A. A card with no `Worker:` line now goes to the
  pool's `defaultWorker` type, whatever that type is (WT-C3a); a type takes other cards only when a card names it.)*
- **The slot number is everywhere**: worktree folder `slotN` and its branch, `stop-slotN`,
  `warn-slotN`, `msg-slotN.txt`, the prompt, running.json, accounting.csv's `slot` column, the live
  feed and the page's `?slot=` URLs. This design keeps the number as *the worker's folder number*
  (WT-W3) so none of that has to change.

Therefore the plan is: worker types in today's single-project scheduler first (cards 2 to 6 of
section 6), then the pool dispatcher rewritten over types (card 7), so that wiring the pool into Host
(not part of item 9) starts from types and never from slots.

## 1. Config

### The `pool` section

```json
"pool": {
  "maxWorkers": 4,
  "types": {
    "spark":  { "max": 2, "agent": "",       "model": "spark", "endpoint": "http://spark.local:18400/v1" },
    "sonnet": { "max": 1, "agent": "claude", "model": "claude-sonnet-5-5" },
    "opus":   { "max": 1, "agent": "claude", "model": "claude-opus-5-5" }
  },
  "defaultWorker": "spark"
}
```

- **WT-C1** `maxWorkers`: how many workers run at once in all (across every project of a pool).
  An integer of 1 or more; missing or invalid -> scheduler.json's `slots` (CFG-6, default 2).
  `-Slots` on the command line pins it for one run, as it pins the slot count today (CFG-8).
- **WT-C2** `types`: an object; each key is a type's name. Names compare case-insensitively (G-1).
  A name that is only digits is refused (logged, the type skipped): a number means a slot in a
  `Worker:` line (DISP-18). *Superseded 2026-11-23 (1146-A): `premium` was refused too; it is now an ordinary name.*
- **WT-C3** A type's fields: `max` (integer, 0 or more; 0 = off, which is what Disable was; missing
  -> `maxWorkers`), `agent` (an entry of scheduler.json's `agents`; empty = the `worker` section, as
  a worker's `agent` today), `model`, `endpoint`, `metricsUrl`, and, in a pool only,
  `projects` (list of project names; empty = every project; see Q1).
- **WT-C3a** `defaultWorker` (1146-A): the name of the type that takes a card with no `Worker:` line. Missing -> the first
  type whose old `premium` was not true, else the first type; a name that is no type -> logged, and the same fallback.
  Save writes it after `maxWorkers`, and never writes `premium`. Renaming the default type renames the setting; a Save whose
  default names no row is refused. The slots form takes its default the same way (first slot not marked premium).
  `premium` here is only the key of an old file: it sets nothing, decides nothing but this fallback, and is never written (1146-A).
- **WT-C4** A type's effective worker (endpoint, model, metrics, agent, cli) is `Effective-Worker`
  (WRK-4) with the type in place of the worker definition. Nothing else about agents changes.
- **WT-C5** The order of `types` in the file is meaningful: when several types could take a card,
  the first listed with room takes it (WT-5). JSON object order is kept as written.
- **WT-C6** Invalid JSON, or a `pool` with no valid type: logged, the previous settings kept (as
  WRK-1). A sum of `max` above `maxWorkers` is allowed (that is the point: caps share the total).

### Where it lives

- **WT-C7 One project**: in that project's `jobs/workers.json`, read from main as today (WRK-1),
  re-applied only when its text changes. The page's Worker types panel writes it back the way
  `/worker-settings` does today (commit and push, keeping main's line endings).
- **WT-C8 A pool**: in the top-level pool config, beside `projects`, `port`, `theme` and
  `remoteControl` (330-B Q3). Each type may name its `projects`. The projects' own workers.json
  files are not read while they run in a pool (logged once per project at start). The panel writes
  the top-level file, which is the PC's own (not in a repository).
- **WT-C9** Which agents exist still comes from each project's scheduler.json (CFG-10). In a pool, a
  type's `agent` is looked up in the scheduler.json of the project whose card it runs; a type whose
  agent a project does not define does not fit that project's cards (noted, WT-8).

### Migration from today's workers.json

- **WT-C10** A workers.json with a `pool` key uses it; `slots` and `workers` beside it are ignored
  (logged once). Without `pool`, the scheduler derives the types, in memory, from `slots` and
  `workers` and goes on working unchanged:
  1. `maxWorkers` = `slots` (or `-Slots`).
  2. Each slot 1..`slots` is one worker: its `workers.<n>` entry, or an empty one (the `worker`
     section) when it has none.
  3. Workers with the same agent, endpoint, model, metricsUrl, categories (upper-cased) are
     one type. (*Premium was part of the key until 2026-11-23 (1146-A).*) Its `max` = how many of them are enabled in workers-state.json (WRK-3), so a disabled
     slot carries over as one less in the cap, and all disabled is cap 0.
  4. A type's name: the first member's `name`; else `Model-Short` of its effective model; else
     `worker`. A name already taken gets `-2`, `-3`, ...; a name refused by WT-C2 gets `worker-` in
     front.
  5. The type keeps the slot numbers it was made from, for `Worker: <number>` (WT-3, Q3).
- **WT-C11** The first Save from the Worker types panel writes the `pool` form, removes `slots` and
  `workers`, and updates the file's `about` text. From then on workers-state.json is not read. Until
  that Save nothing is written: an existing project keeps working with no change to its files.
- **WT-C12** This project's workers.json migrates to: `maxWorkers` 4; `worker` (spark through the
  default agent, max 2); `Sonnet` (claude, claude-sonnet-5-5, max 1); `Opus`
  (claude, claude-opus-5-5, max 1); `defaultWorker` `worker` (the type that takes cards with no `Worker:` line, WT-C3a). `Worker: Opus` on a card picks the Opus type by its
  name, as it picks slot 4 by its name today.

## 2. Dispatch

What stays exactly as today: the plan's candidates and their order (DISP-1 to DISP-14, DISP-22 to
DISP-24: orphans, priority, dependencies, scopes), the per-project gate (310-A Q1: a red project
stops only itself), the pool's choice between projects (MP-5 to MP-7: resumes first, then the
project furthest below its share; priority never jumps the weights), per-project time limits and
retries (310-A Q4), and the meaning of a `Worker:` line (310-A Q3; but see Q3 on slot numbers).

- **WT-1 Total.** A worker starts only while fewer than `maxWorkers` workers are running, counted
  across every project of the pool, adopted workers included.
- **WT-2 Cap.** A worker of type T starts only while fewer than T's `max` workers of type T are
  running. `max` 0 means T never starts. A cap or total lowered below what is running stops nothing:
  the running workers finish, and no new one of that type (or none at all) starts until the count
  is below again (as WRK-2 never lowers the slot count below a running slot).
- **WT-3 Fit.** Type T fits card C when all hold:
  - `Worker:` (DISP-18): no `Worker:` line (or a blank one, or the card template's placeholder text left in) -> T is
    the pool's `defaultWorker` type, and no other type takes it; any other value -> it equals T's name, T's effective model,
    `Model-Short` of that model, or models.json's `short` for it (case-insensitive). A value of only
    digits (a slot number) matches the type made from that slot by WT-C10 while workers.json has no
    `pool`, and nothing after (Q3).
  - Category: none. A type fits a card of any category (card 969-A removed the categories list; Q2 is settled that way).
  - Pool only: T serves C's project (`projects` empty or naming it; Q1), and the project defines
    T's agent (WT-C9).
  - *Superseded 2026-11-23 (1146-A, 1148-D): before, no line meant "T is not premium" and `Worker: premium` meant "T is
    premium". Now `Worker: premium` is an ordinary name: it fits only a type named Premium, else no type fits.*
- **WT-4 Usable now.** A type that fits is usable now when WT-2 allows one more, its agent is not
  out of quota (WT-9) and its model server answers (`Server-Up`, ACCT-8, cached 20 s; no endpoint =
  up).
- **WT-5 Which type.** Of the usable types that fit, the first in the config's order (WT-C5). This
  lets the developer list the cheaper type first.
- **WT-6 Order, one project.** While the state is running and WT-1 allows one more, each candidate
  in DISP-14 order starts on the type WT-5 gives it. A candidate with no usable type is skipped and
  the next one goes (no card waits behind another card's type). A resume counts its retry first
  (DISP-16); a start that fails is not tried again this tick (as today).
- **WT-7 Order, pool.** While WT-1 allows one more: every eligible project (`PoolGate.Eligible`)
  offers its first candidate, in its own DISP-14 order and not yet tried this tick, that has a
  usable type (WT-4); if any offer is a resume, only resumes take part (300-A Q3); of those, the
  project furthest below its share (MP-6, 300-A Q2) starts its card on WT-5's type. Repeat until no
  project has an offer or WT-1 stops it. This replaces "a worker comes free, which project gets
  it": with types, a free place in the total is offered to the cards, and the card's fit picks the
  type.
- **WT-8 Scopes while waiting.** A candidate that cannot start keeps its scope taken for the rest of
  the tick (DISP-13 unchanged), so a later card that overlaps it does not overtake it. With caps this
  happens more often than with slots; it keeps the order cards were planned in. (Spec C4 flagged the
  category version of this; with scopes only overlapping cards wait.)
- **WT-9 Quota, per agent.** An out-of-quota result (TIME-15) marks the run's agent out of quota,
  with the time; types using that agent do not start, in every project, and other agents' types
  run on (310-A Q2). This replaces the project-wide `paused-quota` state, in a single project too:
  the state stays `running`. The page's Resume clears every agent's mark; the Worker types panel
  has a Resume per agent. The retry count is given back as today (TIME-15).
- **WT-10 Model servers.** A type whose server does not answer does not start (WT-4), as today.
  There is no separate per-server limit: a type's `max` is the capacity of its server, and two types
  on one server share it only through their caps (Q5).
- **WT-11 Threads.** Threads per worker = max(1, floor(cores / `maxWorkers`)) (WRK-7, MP-3), set as
  today in the jobs environment variable and `{jobs}`.
- **WT-12 Queue notes.** A candidate that is not started gets one note (DISP-19's place), with
  "orphan to resume; " in front for orphans, in this order of checks:
  1. state not running -> "next up; dispatching is <state>" (as today; per project in a pool).
  2. no type fits at all -> "next up; asks for worker <X>, and no worker type fits" (with a
     `Worker:` line), or "next up; no worker type serves project <P>" (pool).
  3. otherwise, for each type that fits, why it is not usable, joined by ", ":
     "<type> is off" (max 0), "<type> (<r> of <max> running: <cards>)" for a type at its cap, <cards> being the cards its running workers hold in slot order, up to three then "+n" (the bare "<type> (2 of 2 running)" when no card names are known; 935-A, `TypeDispatcher.NotUsableBecause`), "<type>: <agent> out of quota since HH:mm",
     "<type>: model server does not answer"; the note is "next up; waiting for <reasons>"; one full type reads "next up; waiting for <type> (<r> of <max> running: <cards>)".
  4. every fitting type usable but the total reached -> "next up; waiting for a free worker
     (<n> of <maxWorkers> running)".
  The Queue window's worker column and the time limit from history (640-A, `TypeDispatcher.Fitting`)
  use the type WT-5 would pick if nothing were running and no agent were out of quota.
- **WT-13 Types removed or changed while running.** A running worker keeps the type and settings it
  started with. It counts toward WT-1 always, and toward the cap of a type of the same name if one
  still exists. Its window shows its type name, with "(removed)" when the type is gone.
- **WT-14 Dry run.** Logs "DRY RUN <type> in slot <n> <- <card>" (DISP-21).

## 3. Worktrees

- **WT-W1 Folders.** Each project has the folders `<workspaceRoot>\slot1` .. `slot<maxWorkers>`
  (WRK-5's names and branches, so today's worktrees are reused as they are). Any type uses any
  folder. A git worktree belongs to one repository, so in a pool each project has its own set; a
  project never runs more than `maxWorkers` workers, so its set always has a free folder.
- **WT-W2 Taking a folder.** A starting worker takes its project's lowest-numbered folder with no
  running worker (an abandoned worker, TIME-11, frees its folder as it frees its slot today), and the
  folder is prepared as WRK-5 says (fetch, checkout -B, reset, clean) and its files cleared (WRK-6).
- **WT-W3 The worker's number.** The folder number is the worker's number: `stop-slot<n>`,
  `warn-slot<n>`, `msg-slot<n>.txt`, the prompt's worktree, running.json's `slot`, accounting.csv's
  `slot`, the live feed and the page's `?slot=` keep using it. In a pool the number is unique only
  within a project; page actions also carry `project=` (as 340-C's combined page already posts).
- **WT-W4 Fewer folders.** Folders above `maxWorkers` after it is lowered are left on disk, unused;
  nothing deletes a worktree.
- **WT-W5 Adoption.** running.json gains each worker's `type` (ADOPT-1). At start an entry is adopted
  by pid and start time exactly as ADOPT-2; it keeps its folder number even above `maxWorkers`
  (ADOPT-2's "raises the count" goes: WT-1 and WT-2 count it instead). An entry written before this
  change has no `type`: it gets the WT-C10 type of its slot, or, when none, counts toward WT-1 only.
- **WT-W6** The health check's worktree (HLTH-3) is unchanged.

## 4. Page

- **WT-P1 Worker windows**: one per running worker, in a pool across all projects (project shown,
  and filtered by the project filter, MP-8). Title: "<type> - slot <n> - <card>". Buttons: Minimize
  or Expand, Stop, Kill, and the message box as today (WRK-14). No window for an idle folder, and no
  Disable, Enable or Settings. With none running, one line: "No workers running (0 of 4)".
- **WT-P2 Stop and Kill** send the stop request (TIME-10, `pageStopGraceMinutes`) or kill (TIME-12)
  as today, but there is no slot left to disable. What happens to the card next is Q4; the
  recommendation is that the card waits under Waiting on you, "stopped from the page", with Resume.
- **WT-P3 Worker types panel** (in the control window, replacing the per-worker Settings dialogs):
  the total (`maxWorkers`, a number); then one row per type, in config order: name, "<running> of
  <max>", the cap (a number; 0 = off), agent (a choice of scheduler.json's agents and the default),
  model, endpoint, metrics URL, a Default column (read-only today), categories, and in a pool projects; an Add type row; Remove on a
  type with none running (WT-P7); Save. An agent out of quota shows "out of quota since HH:mm" and Resume.
  Save writes the config as WT-C7 / WT-C8 say and applies it on the next tick.
- **WT-P4 Routes.** Gone: `/enable`, `/disable`, `/worker-settings`, `/slots-add`, `/slots-remove`
  (Spec C10). New: `/worker-types` (POST, the panel's form) and `/agent-resume?agent=<name>`. Kept:
  `/stop`, `/kill`, `/send` (by `slot`, plus `project` in a pool), `/stopall`, `/open-workers`.
- **WT-P5 status.txt** (ADOPT-3): one line per running worker (type, slot, card), then a line
  "types: worker 2/2, Sonnet 0/1 (off), Opus 1/1", then the lines it has today.
- **WT-P6 accounting.csv** (ACCT-1) gains a `type` column at the end; an old header is rewritten in
  place as ACCT-4 does for `model`. Stats per model (ACCT-6) are unchanged.
- **WT-P7 Rename, removal and caps in Save** (card 947-B, developer decision 2026-11-19). Each row posts
  the name it was loaded with, so a rename is never read as a remove plus an add.
  - **Rename**: the same commit that writes workers.json rewrites the `Worker:` header line of every card
    in `queue/`, `active/` and `review/` that names the old type (case-insensitive, the value only, a
    `(reason)` after it kept); the commit message names the cards it changed. A worker running under the
    old name keeps running and is adopted under the new name (shown so, and written into running.json at
    the save, so a restart adopts it likewise).
  - **Removal**: a type left out of the list is refused, with nothing committed, when open cards name it
    (the notice names the cards) or when one of its workers is still running (the notice names the type
    and each slot with its card: "type Sonnet has slot 2 (952-B-gaps) running: stop it or keep the
    type"). A type no card names and with nothing running is removed.
  - **Cap 0 is "off"**: always allowed, even with workers running (they finish; no new one starts).
  - **A cap-only change** (no rename, add or removal) touches only workers.json: no card is rewritten.

## 5. What changes in the code

Categories from `jobs/categories.json`: A core (`Scheduler.Core`), B host (`Scheduler.Host`), C web
(`Scheduler.Web`), D design.

### Core (category A)

| File | Change |
|---|---|
| `Contracts/WorkerType.cs` (new) | A type: name, max, agent, model, endpoint, metricsUrl, categories, projects, and the slot numbers it was migrated from. |
| `Contracts/WorkerPool.cs` (new) | `maxWorkers` and the ordered types. |
| `Config/WorkerTypesJson.cs` (new) | Parse the `pool` section (WT-C1 to WT-C6), and `FromWorkersJson` for the migration (WT-C10), given the enabled map. Build the `pool` text for Save (WT-C11). |
| `Contracts/WorkerDefinition.cs`, `Config/SchedulerConfigLoader.ApplyWorkersJson` | Kept for the migration only; `Slots` becomes `MaxWorkers` (WT-C1, CFG-8). |
| `Categories/WorkerSelection.cs` | `Effective` takes a `WorkerType` (WT-C4); `JobsPerWorker(cores, maxWorkers)` (WT-11). |
| `Dispatch/DispatchSlot.cs` | Deleted by 939-A. Replaced by `Dispatch/DispatchType.cs`: a type as dispatch sees it (name, model names, categories, `IsDefault`, endpoint, agent, max, running). |
| `Dispatch/SlotDispatcher.cs` | Deleted by 939-A. Became `Dispatch/TypeDispatcher.cs`: `Fits` (WT-3), `Usable` (WT-4), `Choose` (WT-5), `Dispatch` (WT-1, WT-2, WT-6), notes (WT-12), `Fitting` for the Queue window. `LaneAllowed` and `WorkerFits` keep their rules, on a type. |
| `Dispatch/DispatchStart.cs` | Deleted by 939-A. Carries the type's name instead of a slot number; Host picks the folder (WT-W2). |
| `Tick/SchedulerTick.cs` | Runs by type: counts running per type, the out-of-quota agents (WT-9) instead of `PausedQuota`, `DispatchTypes()` in place of `DispatchSlots()`, metrics per type's URL. |
| `Contracts/SlotState.cs`, `Contracts/StateFile.cs`, `Launch/Adoption.cs` | The running worker gains `Type`; adoption by WT-W5. |
| `Pool/PoolWorker.cs`, `Pool/PoolDispatcher.cs`, `Pool/PoolGate.cs` | The choice for "one free worker" becomes WT-7's loop over offers with a usable type; `PoolWorker` goes (a type's `projects` replaces it); `PoolGate.AgentPaused` works on types. `PoolShares` and `PoolWeights` are unchanged. |
| `WebContract/PageSlotView.cs`, `WebContract/IPageActions.cs` | A running-worker view and a `PageWorkerTypeView`; actions `Enable`, `Disable`, `WorkerSettings`, `SlotsAdd`, `SlotsRemove` go; `SaveWorkerTypes` and `ResumeAgent` come. |
| `Results/RunResults.cs`, `PageModel/PageResume.cs` | The out-of-quota result marks the run's agent instead of setting `PausedQuota` (WT-9); the page's Resume clears every agent's mark. |
| `Watchdog/WatchdogRules.cs`, `Launch/WorkerPrompt.cs` | No rule change; they keep the slot number (WT-W3). |

### Host (category B)

| File | Change |
|---|---|
| `Slots/SlotWorktrees.cs` | `FreeFolder(running)` (WT-W2); `Prepare` unchanged. |
| `Slots/SlotWorkerStarter.cs`, `Tick/TickHost.cs`, `Tick/TickHostParts.cs` | Start a worker of a type in a free folder; dry-run text (WT-14). |
| `Slots/WorkersEnabledState.cs` | Read only for the migration (WT-C10), then not at all (WT-C11). |
| `PageActions/HostPageActions.cs`, `PageActions/WorkersJsonText.cs` | Save types (commit and push workers.json, WT-C7); Stop and Kill no longer disable (WT-P2, Q4); per-agent Resume (WT-9). |
| `PageActions/HostPageSnapshot.cs`, `PageActions/PageActionParts.cs` | Running-worker views and type views; `Fitting` by type (WT-12). |
| `Processes/RunningJson.cs`, `Processes/WorkerAdopter.cs`, `Processes/WorkerFinisher.cs` | `type` in running.json; adoption (WT-W5); the finished run's agent for WT-9. |
| `State/AccountingCsv.cs`, `State/StateFiles.cs` | `type` column (WT-P6); status.txt lines (WT-P5). |
| `Pool/PoolConfig.cs` | Parse the pool's `pool` section with `projects` per type (WT-C8). |
| `Observe/*` | The observe mode mirrors the old scheduler's slots: map each slot to its migrated type (WT-C10) or leave observe on slots (it compares with schedule.ps1, which has no types). |

### Web (category C)

| File | Change |
|---|---|
| `Views/PageRenderer.cs` | Worker windows per running worker (WT-P1); the Worker types panel (WT-P3) replacing `AppendSettingsDialogs` and the Disable/Enable buttons. |
| `Views/CombinedPageRenderer.cs` | The same for the combined page, with the project on each window. |
| `Views/PageScript.cs` | The panel's add and remove rows; no per-slot settings dialog. |
| `Server/PageRouter.cs` | Routes as WT-P4. |
| `Server/LiveFeed.cs` | Workers keyed by (project, slot); a worker appearing or ending changes the page version so the windows are redrawn. |

### Test plan

New file `test-plans/worker-types.md` (written by card 1 of section 6). Tests, by rule:

| Test | Rules | Project |
|---|---|---|
| `WorkerTypes_PoolSection_ParsedInOrder_DefaultsApplied` | WT-C1, WT-C3, WT-C5 | Core |
| `WorkerTypes_NameDigits_Refused_PremiumIsAName` | WT-C2 | Core |
| `DefaultWorker_*` (DefaultWorkerTests.cs) | WT-C3a | Core |
| `WorkerTypes_Save_DefaultWorker_WrittenOrRefused` | WT-C3a, WT-C11 | Host |
| `WorkerTypes_InvalidJson_PreviousKept` | WT-C6 | Core |
| `WorkerTypes_Migration_SameSettingsMerge_CapIsEnabledCount` | WT-C10 | Core |
| `WorkerTypes_Migration_ThisProjectsWorkersJson_GivesWorkerSonnetOpus` | WT-C10, WT-C12 | Core |
| `WorkerTypes_Migration_NamesFromModelShort_ClashesNumbered` | WT-C10 | Core |
| `WorkerTypes_PoolKeyPresent_SlotsAndWorkersIgnored` | WT-C10 | Core |
| `WorkerTypes_Save_WritesPoolForm_DropsSlots` | WT-C11 | Core, Host |
| `TypeDispatch_TotalReached_NothingStarts_NoteSaysNofM` | WT-1, WT-12 | Core |
| `TypeDispatch_TypeAtCap_NextCardOfOtherTypeStarts` | WT-2, WT-6 | Core |
| `TypeDispatch_CapZero_TypeOff_NoteSaysOff` | WT-2, WT-12 | Core |
| `TypeDispatch_CapLoweredBelowRunning_NothingStopped_NoneStarted` | WT-2, WT-13 | Core |
| `TypeDispatch_NoWorkerLine_OnlyDefault`, `TypeDispatch_BlankWorkerLine_OnlyDefault`, `TypeDispatch_DefaultFull_NoLineWaits_OthersNotUsed`, `TypeDispatch_NoDefaultType_NoLineNotDispatched` | WT-3 | Core |
| `TypeDispatch_WorkerPremium_IsUnknownName_NotDispatched`, `TypeDispatch_TypeNamedPremium_TakesItsCards` | WT-3 | Core |
| *Superseded 2026-11-23:* `TypeDispatch_NoWorkerLine_OnlyNonPremium`, `TypeDispatch_WorkerPremium_OnlyPremium_FirstListed` | WT-3, WT-5 | - |
| `TypeDispatch_WorkerName_ModelOrShort_PicksThatType` | WT-3 | Core |
| `TypeDispatch_WorkerSlotNumber_MatchesMigratedTypeOnly` | WT-3 (Q3) | Core |
| `TypeDispatch_AreaNotInTypesAreas_DoesNotFit` | WT-3 (Q2) | Core |
| `TypeDispatch_ServerDown_TypeNotUsable_OtherTypeTakesCard` | WT-4, WT-10 | Core |
| `TypeDispatch_SeveralTypesUsable_FirstListedTakesIt` | WT-5 | Core |
| `TypeDispatch_WaitingCardKeepsScope_OverlappingLaterCardWaits` | WT-8 | Core |
| `TypeDispatch_QuotaOnClaude_CopilotTypesRunOn_StateStaysRunning` | WT-9 | Core |
| `TypeDispatch_AgentResume_ClearsOnlyThatAgent` | WT-9 | Core |
| `TypeDispatch_Threads_CoresOverMaxWorkers` | WT-11 | Core |
| `TypeDispatch_Notes_EachReasonInOrder` | WT-12 | Core |
| `TypeDispatch_Fitting_IgnoresRunningAndQuota` | WT-12 | Core |
| `TypeDispatch_DryRun_LogsTypeAndSlot` | WT-14 | Core |
| `PoolTypes_TotalAcrossProjects_SharesPickProject` | WT-1, WT-7 | Core |
| `PoolTypes_ResumeFirst_EvenWhenOtherProjectBelowShare` | WT-7 | Core |
| `PoolTypes_TypeServesOnlyItsProjects` | WT-3, WT-7 (Q1) | Core |
| `PoolTypes_ProjectWithoutTheAgent_TypeDoesNotFit` | WT-C9 | Core |
| `PoolTypes_RedProjectOffersNothing_OthersRunOn` | WT-7 | Core |
| `Folders_LowestFreeFolderTaken_AnyType` | WT-W1, WT-W2 | Host |
| `Folders_AbandonedWorkerFreesFolder` | WT-W2 | Host |
| `Folders_StopAndWarnFilesKeepSlotNumber` | WT-W3 | Host |
| `Adoption_EntryWithoutType_GetsMigratedType` | WT-W5 | Core |
| `Adoption_FolderAboveMaxWorkers_KeptAndCounted` | WT-W5, WT-1 | Core |
| `Page_WorkerWindows_OnlyRunningWorkers_StopKillNoDisable` | WT-P1 | Web |
| `Page_WorkerTypesPanel_RowsInOrder_RunningOfMax_OffAtZero` | WT-P3 | Web |
| `Page_OldSlotRoutes_Gone_NewRoutesPost` | WT-P4 | Web |
| `Page_StopFromPage_CardWaitsOnYou` | WT-P2 (Q4) | Core, Host |
| `StatusFile_TypesLine` | WT-P5 | Host |
| `Accounting_TypeColumn_OldHeaderRewritten` | WT-P6 | Host |
| `WorkerTypes_RenameWithOpenCards_OneCommitRewritesTheirWorkerLines` | WT-P7 | Host |
| `WorkerTypes_RenameWithRunningWorker_AdoptedUnderTheNewName` | WT-P7 | Host |
| `WorkerTypes_RemoveTypeUsedByOpenCards_RefusedNamingTheCards` | WT-P7 | Host |
| `WorkerTypes_RemoveUnusedType_Saves` | WT-P7 | Host |
| `WorkerTypes_RemoveTypeWithRunningWorker_RefusedNamingTheWorker` | WT-P7 | Host |
| `WorkerTypes_OffTypeWithRunningWorker_Saves` | WT-P7 | Host |
| `WorkerTypes_CapChangeOnly_WritesOnlyWorkersJson` | WT-P7 | Host |
| `WorkerTypes_NewRowWithNoOriginalName_IsNotARename` | WT-P7 | Host |

The tests that check DISP-15, DISP-17 to DISP-19, WRK-2, WRK-3, WRK-7, TIME-10, TIME-12 and ADOPT-2
on slots are rewritten to the WT rule that replaces each, never deleted without one in its place.

## 6. Cards (proposed, in order)

Numbers are placeholders for the planner. "Worker" is the `Worker:` line proposed.

1. **D - spec and test plan for worker types.** Move the WT rules into `spec.md` (replacing DISP-15,
   DISP-17 to DISP-19, WRK-1 to WRK-3, WRK-7, TIME-10, TIME-12, TIME-15's state, ADOPT-2, ACCT-1),
   with the developer's answers to this card's questions; write `test-plans/worker-types.md`.
   Depends on: this card's answers. Worker: Sonnet.
2. **A - worker types in config, and the migration.** `WorkerType`, `WorkerPool`, `WorkerTypesJson`
   (parse, migrate, build), `MaxWorkers` in the config. Tests WorkerTypes_*. Depends on: 1.
   Worker: none (default type).
3. **A - dispatch by type.** `DispatchType`, `TypeDispatcher` (WT-1 to WT-6, WT-8, WT-10 to WT-14),
   per-agent quota (WT-9), `SchedulerTick` by type, `Adoption` with types (WT-W5), the WebContract
   changes (views and actions). Tests TypeDispatch_*, Adoption_*. Depends on: 2. Worker: Sonnet
   (the core of the change; it touches the tick).
4. **B - Host runs types.** Folders (WT-W1 to WT-W4), running.json `type`, adopter, finisher's agent
   for quota, accounting column, status.txt, threads, dry run, saving types (commit workers.json),
   Stop/Kill without disable, per-agent Resume, workers-state.json only for migration, observe mode.
   Tests Folders_*, Accounting_*, StatusFile_*. Depends on: 3. Worker: Sonnet.
5. **C - page: worker windows and the Worker types panel.** WT-P1 to WT-P4 in `PageRenderer`,
   `CombinedPageRenderer`, `PageScript`, `PageRouter`, `LiveFeed`. Tests Page_*. Depends on: 3 (the
   contract); can run beside 4. Worker: none (default type).
6. **B - this project's workers.json in the pool form.** Not code: after 4 and 5 are in, save the
   Worker types panel once (or write the file by hand) and check the page and a run. Goes to review
   for the developer's look at the panel. Depends on: 4, 5. Worker: none. (Could be the developer's
   own step instead of a card.)
7. **A - the pool dispatcher over types.** `PoolDispatcher`/`PoolGate` become WT-7's loop;
   `PoolWorker` goes; `PoolConfig` (B) reads the pool's `pool` section with `projects`, so it locks
   `Scheduler.Host/Pool/PoolConfig.cs`. Tests PoolTypes_*. Depends on: 3. Worker: Sonnet. Wiring
   the pool into Host stays its own later work (not part of item 9).

Cards 2 to 5 change one category each; 4 and 5 can run side by side once 3 is done.

## Open points

Asked on card 530-D under Questions, each with a recommendation:

1. Each type names the projects it serves (replacing 300-A Q4's worker list). Recommended: yes.
2. Per-category limit on a type: today's workers 1-2 take only categories A-C. Settled by 969-A: a type has no categories list at all, and no per-category or per-scope count limit.
3. `Worker: <slot number>` has nothing to name without slots, though 310-A Q3 kept the `Worker:`
   line unchanged. Recommended: a number matches the migrated type until the `pool` form is saved,
   and nothing after.
4. What Stop and Kill from the page do to the card now that there is no slot to disable.
   Recommended: the card waits under Waiting on you with Resume.
5. Model-server capacity: a separate per-server limit, or the types' caps only. Recommended: caps
   only.
6. Where caps are kept: in workers.json on main (each change a commit) or locally on the PC as
   Disable was. Recommended: in workers.json with the rest of the type's settings.
