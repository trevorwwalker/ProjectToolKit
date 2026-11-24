# Page views (roadmap item 5): Workers, Card board, Plans and Gantt - design and plan

Card 1031-D, 2026-11-21. Design only: nothing here is built. It is written for the developer to approve (open points
at the end, also the card's Questions) and then to be carded (section 8). Rule IDs `VW-1` onward are stable names for
test cases; once the cards land, `spec.md` takes them over beside the page rules (PAGE-*, MP-P*).

Sources: roadmap item 5 and the card's inputs (planner and developer, 2026-11-21, Plans view 16:55); `multi-project.md`
(MP-P1 to MP-P9, the pool page); card 1030-A (in queue: `Type:`, `Version:` and `Plan:` read from the header, and new
accounting columns); the code on main at `ddf13d6`: `Scheduler.Web/Views/CombinedPageRenderer.cs`, `PageScript.cs`,
`TrendChart.cs`, `Scheduler.Web/Server/PageRouter.cs`, `PageVersion.cs`, `LiveFeed.cs`, `Scheduler.Core/WebContract/`
(PageSnapshot, PoolPageSnapshot, PageSlotView, PageQueueItem, PageWaitingKind), `Scheduler.Core/Contracts/Card.cs`,
`IMainRepository.cs`, `Scheduler.Core/Cards/CardFiles.cs`, `CardWaits.cs`, `Scheduler.Core/PageModel/PageWaiting.cs`,
`PageQueue.cs`, `Scheduler.Core/Accounting/` (AccountingRow, AreaDurations, AreaTypical), `Scheduler.Host/Git/CardReader.cs`,
`Scheduler.Host/State/AccountingCsv.cs`, `Scheduler.Host/Program/ProjectParts.cs`; and both projects' `jobs/` folders on
their main branches (counts below are from 2026-11-21).

## 0. What exists today, and what that means

- **One page, one view.** `CombinedPageRenderer` draws the pool page: the Projects window (its title bar carries Pause
  all / Resume all, Drain and exit, Stop all and exit, the project dropdown `#projsel` (MP-P2, only with two or more
  projects), the theme menu and the last tick), then Waiting on you, the worker windows, the Queue, Recent events and
  Trends. This is the **Workers** view; it stays as it is. `PageRenderer` (one-project mode) is being retired, so
  nothing here is added to it.
- **The project filter is client-side.** Every project-owned row carries its project (`data-project`) and the
  `FilterScript` hides the others by `body[data-projsel]`; the chip (`ProjectChip`, MP-P9) shows only under All
  projects. New views reuse both, unchanged.
- **The page refreshes by version.** `PageVersion.Of` hashes each project's whole `PageSnapshot` (without what the
  live feed keeps current); the script polls `/live.json` every 2 s and, when the version differs, fetches `/` and
  swaps the desk. Anything put into `PageSnapshot` is serialised and hashed on every poll. **So the views' data
  (hundreds of done cards, every run) must not go into `PageSnapshot`**: it gets its own snapshot and version (VW-20).
- **The page has no full card list.** `PageSnapshot` carries the Queue (`PageQueueItem`: name, title, note, depends,
  log, time-box note, yielded), Waiting on you (`PageWaitingItem`, kinds Question, Waiting, Review, Stuck,
  FinishedWithoutUpdate, HeldCategory, TemplateEdited) and the slots (`PageSlotView`: card, start, time limit, model,
  type, slot). Done cards, review cards whose worker still runs and the planner's waiting cards are not on it as cards.
- **Done cards are never opened** (CARD-5). `CardReader` lists `git ls-tree -r <jobs>` on main and parses each blob
  once (CARD-1, cached by blob id and path; 1013-B prefetches the new blobs in one git process; 987-B skips the listing
  when main's sha has not moved), but a `done/` card is kept as its path only: id, number, category, folder, blob id -
  no title, status, `Depends on`, time box, worker, type or plan. `CardFiles.FromPath` matches only
  `queue|active|review|done`, so `archive/` cards are not cards at all. The board's Done column, the Plans counts and
  the Gantt's history all need done cards' headers: the repository's in-memory model reads them (spec A14, MM-5) and
  the views take them from it, while the tick's reader keeps CARD-5 (VW-21).
- **Reading done cards once is cheap; every tick is not.** AmigaX has 565 cards in `done/` (5 MB); one
  `git cat-file --batch` of all of them took about 3.3 s on this PC. Done cards' blobs almost never change, so a cache
  by blob id (as CARD-1) makes every read after the first nearly free.
- **The plan files are already in the tree the reader lists.** `ls-tree -r` of the jobs folder includes `jobs/plans/`
  and `jobs/archive/` with their blob ids, so a plan under `jobs/` is read by blob like a card. A plan elsewhere (most of
  ProjectToolkit's, which are design documents) is read with `IMainRepository.ReadFile` at main.
- **The cards do not name a plan the same way.** No code reads `Plan:` yet; 1030-A (queued) adds `Type:`, `Version:`
  and `Plan:` (path, then the step or phase as free text; "none..." means no plan) to the card model. On main today:
  - AmigaX: 573 cards (565 done, 3 archived); 264 have a `Plan:` line: 146 name a `*_plan.md`, 80 say `none (...)`,
    the rest name a standing brief (`docs/jobs/README.md` "Scout cards", `docs/agents/game_bringup_prompt.md`). The plan
    paths are the old `docs/jobs/plans/...` (the queue moved to `jobs/` on 2026-11-18), and some of those plans are now
    in `jobs/archive/` (`linux_plan.md`, `ui_redesign_plan.md`, `emulator_ui_plan.md`). 309 cards have no `Plan:` line;
    most have a `From:` line naming who asked (the developer, a survey, `known_gaps.md`); only 13 of them name a
    `*_plan.md` anywhere in the text (11 in `From:`, such as "`a1000_plan.md` parts 2-7" or "machines_plan step 5").
  - ProjectToolkit: all 192 cards have a `Plan:` line: 114 `none`, 39 the archived `docs/jobs/plans/scheduler-next-*_plan.md`,
    the rest design documents (`docs/design/tdd/scheduler/worker-types.md`, `dynamic-locks.md`,
    `multi-project.md section 7`, `spec.md B2`). `jobs/plans/` is empty; the roadmap's items point to the documents.
- **Plans state their status in different shapes.** AmigaX plans start with a `Status:` line ("Status: accepted
  (developer, 2026-11-07 21:18); decisions below. Phase 0 carded ..."); five archived ones have none. ProjectToolkit's
  design documents have no `Status:` line; the roadmap's items carry an italic `*Status 2026-11-19: built by card ...*`.
- **Runs are in accounting.csv, per project.** Each project's log folder has its own file (`AccountingCsv`,
  `ProjectParts`), header `finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model,type`, where **`type` is
  already the worker's type** (WT-P6). A run's start is `finished - minutes`. ProjectToolkit's file has 545 rows.
  **1030-A plans a `type` column for the card's type: the name is taken** (open point 1).
- **Typical lengths exist.** `AreaDurations` gives the median and 90th percentile of finished runs per category and
  model (last 30 runs or 30 days, at least 3 runs: `AreaTypical`); the Queue already uses it for time-box advice (640-A).
- **Waiting is already classified.** `CardWaits.OnDeveloper` (`waiting: you|developer|dev|human`) and
  `CardWaits.OnPlanner` (`waiting: planner`), on queue/ or active/ cards; `PageWaiting` adds active cards with
  `Status: needs-human`.

## 1. Model

Per project the Host keeps a **views snapshot**: an immutable record, built off the scheduler thread, that the page
thread reads without locks (as PAGE-1, PAGE-2). It holds:

| Part | Each item | From |
|---|---|---|
| Cards | id, number, category, title, folder (queue, active, review, done, archive), Status line, Type, Plan path and step, Version, Depends on, Time box, Worker line, blob id | `CardReader`'s cards for queue/active/review; the repository's model of main (VW-21) for done/ and archive/ |
| Runs | card, start, finished, minutes, result, model, worker type, slot, stop reason | the project's accounting.csv (all rows, VW-22) |
| Running | card, slot, start, time limit, model, type | the project's `PageSnapshot.Slots` |
| Plans | path, title (first `# ` line), status line, where it was found (plans/, archive/, elsewhere, missing), its items when it is the roadmap | plan files at main (VW-12) |
| Typicals | category, model, median, P90 | `AreaDurations` over the same rows |
| Version | a hash of the above except Running | built with it |

The board, the Plans view and the Gantt are three drawings of the same snapshot. The Workers view keeps drawing
`PageSnapshot` as today.

## 2. Rules

### The view switch and what every view shares

- **VW-1 Four views, pool page only.** The pool page (`CombinedPageRenderer`) has four views: **Workers** (today's
  page), **Board**, **Plans**, **Gantt**. A pool with one project has them too. `PageRenderer` gets none.
- **VW-2 The switch.** Four buttons in the title bar, after the project dropdown and before the theme menu; the current
  one is pressed. The view is part of the address (`/?view=board`, `plans`, `gantt`; no `view` or an unknown one is
  Workers), so a view can be bookmarked and opened on the phone, and the browser remembers the last one used (as
  `#projsel` is remembered) when `/` is opened bare.
- **VW-3 The title bar on every view.** Every view starts with the same title bar (name, version, load, Pause all /
  Resume all, Drain and exit, Stop all and exit, dropdown, switch, theme, last update). Under it the Workers view has its
  windows; the other views have their own window filling the desk, and the Waiting on you window above it (so a
  question is never more than one view away). The Projects table itself is shown only on Workers.
- **VW-4 One filter for all views.** The project dropdown (MP-P2) filters every view by the same client-side rule
  (`data-project`, `projhide`); the choice is kept when switching views. Under All projects every card, row and bar
  carries the project chip (MP-P9); with one project chosen, or one project in the pool, it is left out. The exception
  is the Projects table's name cell (1105-C): there the chip *is* the row's project name, so the table always shows it,
  whatever the dropdown says. The rules that decide the chips ride inside `#desk` beside the markup they govern
  (1105-C, as 1098-C did for the ranges), because the live feed replaces the desk whole and never the head: a page
  opened before a deploy must show the name with the head it happens to have.
- **VW-5 Opening a card.** Clicking a card anywhere opens what the page already serves for it, with `project=` (MP-P8):
  a review card its review panel (`/review`), any other card its text (`/card`). Buttons on a card (Done, Send back,
  Retry, answers) stay in Waiting on you and the review panel; the views add no new actions and never move a card (the
  scheduler is the only writer of card moves, CM-1).
- **VW-6 Category and type on every card.** A card shows its category chip (970-C colours, the category's name on
  hover) and, when it has one, its Type (`feature`, `bug`; untyped shows nothing).

### Board

- **VW-7 Columns.** Left to right, each card in exactly one column, first match wins:
  1. **Waiting on you**: active with `Status: needs-human`, or `CardWaits.OnDeveloper`;
  2. **Waiting on the planner**: `CardWaits.OnPlanner`;
  3. **Running**: active, with a worker on it now (`Slots`);
  4. **Queue**: queue/ (ready, blocked by `Depends on`, held, yielded: the Queue window's note says which), and active
     with no worker and no waiting status (an orphan the scheduler resumes; noted "claimed, no worker");
  5. **Review**: review/ (with "worker still running" while its run has not ended, as Waiting on you hides it);
  6. **Done**: done/ and archive/.
- **VW-8 What a card shows.** Number and title (the link of VW-5), category chip, Type, project chip (VW-4), and one
  state line: Queue - the Queue window's note (blocked by X, time-box advice); Running - worker type, model and slot,
  "n of m min"; Waiting - its question count or the reason (`PageWaiting`'s note); Review - the last run's result and
  minutes; Done - when it finished (its last run's `finished`, else nothing) and its model.
- **VW-9 Order in a column.** Queue in dispatch order (as the Queue window, `PageQueue`); Running by start; Waiting and
  Review by number; Done newest first by its last run's finish, cards with no run after them by number, highest first.
- **VW-10 Done is paged.** The Done column shows the newest 30 cards per project and a count ("30 of 565"); "Show 100
  more" adds `done=<n>` to the address. The other columns are never paged (they are short by nature).

### Plans

- **VW-11 What a plan is** (1126-C). A plan is a file in `jobs/plans/` and nothing else: not a design document, not the
  roadmap, not anything in `jobs/archive/`. Plans are never archived; a finished plan stays and shows its green check,
  collapsed. The Plans view lists, per project: the plans in `jobs/plans/` (by name, one with no card naming it too),
  then **No plan**. A card whose `Plan:` names any other path is listed under No plan with that path shown.
- **VW-12 Resolving a card's plan path** (read time, permanent). The path from 1030-A's `Plan:` (backticks and a
  trailing `;`/`,` dropped), then: a leading `docs/jobs/` becomes `jobs/`; `jobs/archive/X` becomes `jobs/plans/X` when
  that is on main (plans were archived before 1126-C); a path in `jobs/plans/` not on main is shown under its own
  name marked **missing**; any other path, `none...`, an empty `Plan:` and no `Plan:` line are No plan.
- **VW-13 A plan's status.** The first line of the file that starts with `Status:` (also `*Status`, `**Status`), cut at
  200 characters; none shows "no status line". The roadmap is no longer a plan (1126-C): its items are not listed.
- **VW-14 Counts.** Per plan, and per step within it: **created** (every card naming it), **done** (done/ and
  archive/), and **pending** split as the board's columns: queue, running, review, waiting on you, waiting on the
  planner. A bar per plan shows done against created. The same counts for No plan.
- **VW-15 Steps.** The step is 1030-A's free text after the path ("step 3", "Phase 1", "section 7", "(5.2)"), compared
  without case, brackets and trailing punctuation; steps are ordered by the first number in them, then as text; a card
  with a plan but no step is under "(no step)". A plan's steps are folded; opening one lists its cards as board cards
  (VW-8).
- **VW-16 New cards name their plan.** Every new card has a `Plan:` line (the 1030-A templates carry it): `jobs/plans/<file>`
  and its step, or a design document's path and its section, or `none (<who>, <date>)` for a one-off. Never the old
  `docs/jobs/` path, never only a `From:` line. The planner writes it; a worker never edits it.
- **VW-17 The one-time fix-up per project.** A command, `scheduler-next.exe plan-fixup --project <name>`, lists for each
  card on main with no `Plan:` line the line it would add, from (in order) its `From:` line naming a `*_plan.md` (with
  the words after it as the step: "parts 2-7", "step 5"), then the first `*_plan.md` named in its body; a card with
  neither gets nothing (it stays No plan). It writes the list to a report beside the project's log; with `--apply` it
  adds the lines in one commit on main (`jobs: plan fix-up`), skipping any card in active/. Existing `Plan:` lines are
  not rewritten: VW-12 reads their old paths (open point 4). Run once per project by the planner with the developer,
  report first. For AmigaX on today's main that is 13 cards; ProjectToolkit has none to add.

### Gantt

- **VW-18 Rows and bars.** One row per card that has a run in the shown window, is running, or is not finished
  (queue, active, review), grouped by category in the categories file's order, and within a category by first start,
  unstarted cards last in dispatch order. Each run (accounting row) is a bar from `finished - minutes` to `finished`,
  coloured by result (done, review, needs-human or waiting, stopped or yielded, failed or no result) with the model's
  short name on it and the result, minutes, model, worker type, stop reason and log on hover. A running card's bar grows
  from its start to now, with its time limit drawn as an outline beyond it. A **now** line crosses all rows.
- **VW-19 Dependencies and estimates.** An arrow from each `Depends on` card's last bar (or estimate) to the dependent's
  first; a dependency not in the window shows as a stub with its id. A queued card, and a running one past its median,
  gets an **estimated** bar (dashed, never coloured as a result): length = its `Time box:`, else its category's median
  for the model its `Worker:` line picks (`AreaTypical`), else the scheduler's `maxMinutes`; start = the latest of now,
  its dependencies' estimated finish, and the moment a simulated worker is free - the project's workspaces (1029-B),
  capped by the pool's `maxWorkers`, filled in dispatch order, a card waiting for any running or earlier-estimated card
  whose scope overlaps its own (`ScopeOverlap`). Waiting cards (on you, on the planner) get no estimate: they show a
  hatched "waiting" mark at now, and their dependents' estimates start "after" them, open-ended. The window is 24 hours
  back to the last estimated finish, at most 3 days ahead; buttons 6 h, 24 h, 3 d, 7 d change the span back.

### Data and speed

- **VW-20 The views have their own snapshot and version.** The views snapshot (section 1) is never part of
  `PageSnapshot` or its version. `/live.json` carries one more field, `views`, the hash of every project's views
  version; a page on Board, Plans or Gantt fetches its own address again when it changes (as the Workers view does with
  `page`). Between rebuilds the Gantt's now line and running bars move in the browser from `/live.json`'s slot minutes.
- **VW-21 Done cards come from the in-memory model, not a reader of the views' own.** The headers of done/ and
  archive/ cards are read once per blob by the repository's model of main (spec A14, MM-5 phase 2, MM-7: in process,
  in the background after the open cards, and only the paths that changed on each advance); the views take them from
  the model's snapshot (`IMainModel.DoneCards`) and read no git. The tick's `CardReader` keeps CARD-5. Until phase 2
  ends (`DoneLoaded` false) the views say "reading N done cards..." and show the rest. A blob the model cannot read
  is left out of that snapshot and tried on the model's next advance.
- **VW-22 Rebuilt only when something moved.** A project's views snapshot is rebuilt when the model's commit moves
  (MM-7, MM-8: the ref read in process, no git process), when its accounting.csv's last write moves (as the Trends rows are, 770-B), or when a worker starts or ends;
  otherwise the old one is served. Plan files are read from the model's text by blob (under `jobs/`) or as watched paths (MM-6; elsewhere), at the model's commit.
- **VW-23 Rendered on demand.** A view's HTML is built only when that view is asked for, and kept per view and version
  until the version moves. Done stays paged (VW-10), Plans' card lists are folded (VW-15), the Gantt shows a window
  (VW-19): the page for AmigaX's 573 cards stays about the size of today's.

## 3. Page layout (text sketches)

Title bar, every view (VW-3):

```
Tasker 3.x | load 41%       [Pause all] [Drain and exit] [Stop all and exit]  [All projects v]
                            [Workers] [Board] [Plans] [Gantt]   [theme v]   last update 17:42:05
```

Board (VW-7 to VW-10), under Waiting on you:

```
| Waiting on you (2) | Planner (1) | Running (3)        | Queue (5)            | Review (1)  | Done (30 of 754) |
|--------------------|-------------|--------------------|----------------------|-------------|------------------|
| [AX] 612-B  bug    | 1032-A      | [PT] 1031-D feat.  | 1033-C  blocked by   | 1028-C      | 1029-B  17:02    |
|  Mouse pin ...     |  ...        |  Opus, slot 2      |   1032-A             |  review,    |  Sonnet          |
|  2 questions       |             |  34 of 120 min     | 1034-A  box 60 <P90  |  58 min     | ...              |
|                    |             |                    |                      |             | [Show 100 more]  |
```

Plans (VW-11 to VW-15), one project:

```
machines_plan.md   Status: draft (planner, 2026-11-07). Freeze lifted ...
  [##########------]  created 41  done 28  queue 6  running 1  review 2  you 3  planner 1
  > step 1 (Kickstart 1.2 on the A500)   8 / 8 done
  > step 5                               12 created, 9 done, 3 pending
  > (no step)                            2
android_plan.md    Status: accepted (developer, 2026-11-07 21:18) ...
...
Archived plans (10)  >
No plan            created 389  done 380  queue 2 ...
```

Gantt (VW-18, VW-19):

```
            15:00        16:00        17:00   |now     18:00        19:00
A core   1030-A                               |  [- - est. Sonnet - -]
         1027-A  [==done, Sonnet==]           |
B host   1029-B       [==done, Opus=====]     |
         1032-B                               |        [- - est. - -]   <- after 1030-A
D design 1031-D                     [=Opus=running=>  ...time limit...]
```

## 4. Data each view needs, and where it comes from

| Need | Board | Plans | Gantt | Source |
|---|---|---|---|---|
| Folder, status, title of unfinished cards | yes | yes | yes | `CardReader` (CARD-1), already read |
| Done/archive cards' headers | Done column | counts | history rows | the model's done headers (VW-21) |
| Type, Plan path and step, Version | yes | yes | hover | 1030-A's header fields |
| Running card, slot, start, limit, model | yes | yes | yes | `PageSnapshot.Slots` |
| Queue notes, dispatch order | yes | | order | `PageQueue` (the Queue window's items) |
| Waiting reasons, questions | yes | counts | marks | `PageWaiting`, `CardWaits` |
| Runs (start, end, result, model, type) | last run | | bars | each project's accounting.csv |
| Typical length per category and model | | | estimates | `AreaDurations` / `AreaTypical` |
| Workspaces, max workers | | | estimates | `PoolProjectView.Workspaces`, `PoolPageSnapshot.MaxWorkers` |
| Scope of each card | | | estimates | `CardScope` with `ScopeOverlap` |
| Plan files, status lines, roadmap items | | yes | | `ls-tree` blobs under `jobs/`; `ReadFile` at main elsewhere |
| Category order, colours | yes | | groups | `PageSnapshot.Categories` |

## 5. Failure cases

| Case | What the views do |
|---|---|
| git fails while reading | keep the previous views snapshot (as CARD-3); the window title says "as of <time>" |
| A done blob cannot be read | left out, counted as "n cards not read", tried at the model's next advance (VW-21) |
| First read of done cards still running | Board, Plans and Gantt show what is read, with "reading N done cards..." |
| accounting.csv missing, held open or with a header the reader does not know | no runs: Board shows no finish times, Gantt shows only running and estimated bars, with a note; Typicals fall back to time box or `maxMinutes` |
| A row names a card that no longer exists | its bar is drawn under the id alone, in its category (row's `lane`) |
| A `Plan:` path that is on main nowhere | the plan is listed under its own name marked missing (VW-12) |
| A plan file with no status line | "no status line" (VW-13) |
| A cycle in `Depends on` | no estimate for the cards in it; their rows say "dependency loop" |
| A project still joining (1015-B) | its column cells and plans show "starting..." |
| The scheduler not responding | as today: the title bar says so and the page stops swapping |

## 6. What changes in the code

### Core (category A)

- `Views/` (new): `ViewCard` (the snapshot's card), `ViewRun`, `ViewsSnapshot` with its version; `BoardColumns.Of`
  (VW-7, VW-9, VW-10), `PlanPaths.Resolve` (VW-12), `PlanStatus.Of` and the roadmap items (VW-13), `PlanCounts.Of`
  (VW-14, VW-15), `GanttRows.Of` and `GanttEstimate.Of` (VW-18, VW-19; a pure simulation over cards, runs, running,
  typicals, workspaces and scopes), `PlanFixup.Propose` (VW-17). All pure, no I/O.
- `Cards/CardFiles`: a header-only parse for done and archive cards (title, status, type, plan, version, depends, time
  box, worker), and `archive/` accepted as a folder for the views only (the tick's list keeps ignoring it).
- `Accounting/`: the runs for the views from accounting.csv's lines (all rows, not the 48-hour or 30-day windows).
- `WebContract/LiveSnapshot`: the `views` version (VW-20).

### Host (category B)

- `Views/ViewsSource` (new): per project, done and archive headers from the model's snapshot (VW-21; no reader of its
  own), plan files from the model's text by blob or as watched paths, accounting rows by last write, and the rebuild
  triggers (VW-22), run as a background step; hands the page an immutable `ViewsSnapshot`.
- `Program`: the `plan-fixup` command (VW-17), report and `--apply` through `GitWriter`.

### Web (category C)

- `CombinedPageRenderer`: the switch (VW-2), the shared title bar (VW-3), the `view` parameter; new `BoardView`,
  `PlansView`, `GanttView` (SVG, in the style of `TrendChart`) drawing the views snapshot; the project chip and
  `data-project` on everything (VW-4).
- `PageRouter`: `/?view=` and `done=`; `PageScript`: remember the view, re-fetch on the `views` version, move the now
  line and running bars from `/live.json`.

### Design and kit (category D, and the kit's owner)

- `spec.md` takes over VW-1 to VW-23 when the cards land.
- The kit's `assets/templates/jobs/README.md` (the kit's owner, not this category): the rule of VW-16 in "A job card".

## 7. Test plan

Core (unit, no I/O):
- `VW_7_EachCardInOneColumn_FirstMatchWins` (needs-human in active, waiting: you in queue, waiting: planner, running,
  orphan active, review with a running worker, done, archive).
- `VW_9_DoneNewestFirst_ByLastRunFinish_CardsWithoutRunsAfter`; `VW_10_DonePagedAt30_WithCount`.
- `VW_12_PlanPath_DocsJobsBecomesJobs_PlansFallsBackToArchive_MissingMarked_NoneIsNoPlan` (cases from both projects'
  real lines: "docs/jobs/plans/ui_redesign_plan.md (5.2)", "`a1000_plan.md` parts 2-7", "none (developer, ...)",
  "docs/design/tdd/scheduler/multi-project.md section 7").
- `VW_13_StatusLine_FirstStatusOrItalicStatus_Cut200_NoneSaysSo`; `VW_13_RoadmapItemsArePlans_DocsNestUnderTheItemNamingThem`.
- `VW_14_Counts_CreatedDonePendingByColumn_PerPlanAndStep`; `VW_15_Steps_NormalisedAndOrderedByNumber`.
- `VW_17_Fixup_FromLineFirst_ThenBody_NeitherGetsNothing_ActiveSkipped_ExistingPlanUntouched`.
- `VW_18_RunBarStartsAtFinishedMinusMinutes_RunningGrowsToNow`.
- `VW_19_Estimate_TimeBoxElseMedianElseMax_AfterDependencies_WorkspacesCap_ScopeOverlapSerialises_WaitingOpenEnded_CycleNoEstimate`.

Host:
- `VW_21_DoneHeadersReadOncePerBlob_OneBatch_GoneBlobsDropped_UnreadableRetried` (counting fake `IMainRepository`).
- `VW_22_RebuiltOnlyWhenShaOrAccountingOrWorkersMove`.
- `VW_17_FixupApply_OneCommit_ReportFirst`.

Web:
- `VW_2_ViewFromAddress_UnknownIsWorkers`; `VW_3_TitleBarOnEveryView`; `VW_4_EveryCardRowBarHasDataProject_ChipOnlyUnderAll`.
- `VW_5_ReviewCardOpensReview_OthersOpenCard_WithProject`.
- `VW_20_ViewsVersionOutOfPageVersion_LiveJsonCarriesViews` (a done card added changes `views`, not `page`).
- `VW_23_RenderedOnlyWhenAsked_CachedByVersion`; a size check: 600 done cards render Board under a set size.

By hand (review): the four views on both projects in two themes and on the phone; the Gantt with AmigaX's real
history; `plan-fixup` report for AmigaX read before `--apply`.

## 8. Cards (proposed, in order)

After 1030-A (Type, Version, Plan on the card model; with its accounting column renamed, open point 1).

1. **A - views model**: section 6 Core except the fix-up; tests VW-7 to VW-19 (Core). Depends on 1030-A.
2. **B - views source**: `ViewsSource` (done headers from the model, no reader of its own, 1069-D), rebuild triggers,
   the `views` version in the live snapshot; tests VW-21, VW-22. Depends on 1 and on 1062-B (the models per
   repository, done).
3. **C - view switch and Board**: VW-1 to VW-10, VW-20, VW-23 on the page. Depends on 2.
4. **C - Plans view**: VW-11 to VW-15. Depends on 3.
5. **C - Gantt view**: VW-18, VW-19. Depends on 3.
6. **A then B - plan fix-up** (`PlanFixup.Propose`, then the command): VW-17. Depends on 1; may run beside 3-5.
7. **By hand, planner and developer**: `plan-fixup` per project, report read, then `--apply`. After 6.
8. **D - spec**: VW rules into `spec.md`; the README rule (VW-16) handed to the kit's owner as a request. After 5.

## 9. What the views leave for later

The views snapshot joins every card's header (Type, Plan, Version, category) with every run (start, minutes, result,
model, worker type), per project. That is the table later analysis needs - features against bugs over time, by plan,
by version, by model - and the views draw none of it beyond the Plans counts. Left for later: charts of that kind
(beside Trends), a CSV of the joined table, filters on the board by category or type, and the version a card shipped
(open point 2).

## Open points (all decided by the developer, 2026-11-21 18:03, as recommended except where noted)

1. **The accounting column name.** accounting.csv's last column `type` is already the worker's type (WT-P6); 1030-A
   would add another `type`. Recommended: 1030-A names its column `card_type` (the card is still in queue). **Decided: yes, renamed in 1030-A.**
2. **Which version.** 1030-A's `Version:` is the version a bug was found in. Analysis "by version" wants the version a
   card shipped (its Result names it). Recommended: leave it out of the views now; a later card records the project's
   version at main after each done or review run, as one more accounting column. **Decided: yes, a later card.**
3. **ProjectToolkit's plans.** Its cards name design documents and the roadmap's items point to them. **Decided (differs
   from the recommendation): add a `Roadmap: item n` line to each design document**, since it survives rewording and
   indexes cheaply. The roadmap's items are the plans and each document is listed under its item (VW-13); the build
   cards add the lines.
4. **The fix-up's reach.** Recommended: add `Plan:` only where it can be inferred (13 AmigaX cards today) and leave
   existing lines as they are, read through VW-12, so 565 done cards are not rewritten; the alternative rewrites every
   old `docs/jobs/plans/` line to its current path in the same commit. **Decided: add only the missing lines**;
   VW-12 treats `docs/jobs/plans/` as `jobs/plans/`.
