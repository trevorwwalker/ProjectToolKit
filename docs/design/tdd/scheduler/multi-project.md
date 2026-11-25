# Multi-project (roadmap item 1): design and plan

Card 955-D, 2026-11-19. Design only: nothing here is built yet beyond what section 0 lists. It is
written so the developer can settle the open points (settled 2026-11-19, see Decisions) and the work can be
carded (section 7). Rule IDs are stable names for test cases, like the spec's: `MP-1` to `MP-9`
stay as spec B1 has them, `MP-C*` are config, `MP-10` onward dispatch, `MP-H*` host and `MP-P*` page.
Once the cards land, `spec.md` takes these rules over (card 1 of section 7).

Sources: roadmap item 1's multi-project section and the order of the remaining work
(`jobs/plans/scheduler-roadmap_plan.md`); spec B1, B3 and the worker-type rules (WT-*, A13); the
developer's answers on cards 300-A, 310-A, 320-A, 330-B, 340-C, 560-A and 530-D/850-D; the code in
`Scheduler.Core/Pool/`, `Scheduler.Core/Tick/SchedulerTick.cs`, `Scheduler.Host/Pool/`,
`Scheduler.Host/Program/HostComposition.cs`, `Scheduler.Web/Views/CombinedPageRenderer.cs`; AmigaX's
`docs/jobs/scheduler.json` and `workers.json`, read only (card 954-B, which checks what scheduler-next
needs to run AmigaX, had no Result yet when this was written; its gaps come before the cut-over, 5.3).

## 0. What exists today, and what that means for this plan

**Built and tested, but not run by the Host:**

- Core `Pool/`: `PoolWeights.Parse` (MP-4; a missing weight = the smallest other, 300-A Q5),
  `PoolShares` (MP-6: 2 h window, running minutes counted, weight share minus used share, ties to the
  first listed), `PoolGate.Eligible` (a red or paused project offers nothing, 310-A Q1),
  `PoolGate.AgentPaused` (quota per agent, 310-A Q2), `PoolProjectSetup` (per-project health, planner,
  maxRetries and time limits, 310-A Q4), and `PoolDispatcher.Dispatch`, which is WT-7's loop over
  worker types (910-A).
- Host `Pool/`: `PoolConfig` (projects with repo, jobsDir, weight, remote, branch; top-level `port`,
  `theme`, `remoteControl`, `toolkit` and the `pool` section of worker types), `PoolProjectReader`
  (each project's scheduler.json, README, categories.json and cards from its own main), `GitOutputLog`
  and `ScopeAudit` (330-B; nothing calls them from the tick yet).
- Web: `CombinedPageRenderer` (340-C): one page from one `PageSnapshot` per project, a project
  filter on six views (to become one dropdown, MP-P2), one Waiting on you labelled by project, buttons that post `project=`.
  Nothing serves it.

**What the Host does today:** `HostComposition` builds one `SchedulerTick` for one project; the tick
runs every step (read cards, plan, dispatch with `TypeDispatcher`, status) for that project only.
`SingleInstance` holds `Global\ProjectToolkit-<project>-scheduler`. Every state file is in the
project's `logDir`, by default `%LOCALAPPDATA%\<project>\scheduler`, so two projects' state files,
slot files (`stop-slotN`, `warn-slotN`, `msg-slotN.txt`), move requests and accounting.csv already
live apart. Worktrees are per project (`workspaceRoot`, WT-W1).

**What that means:** the remaining work is wiring, not new logic: a tick split so that one dispatch
spans every project, a composition that builds one set of per-project parts and one set of shared
parts, the page served from several snapshots, and the cut-over of AmigaX.

**Spec B3, which questions are closed:**

| B3 | Closed by | Answer |
|---|---|---|
| 1 high priority vs weights | 300-A Q1 | No: weights pick the project, priority orders cards inside it. |
| 2 window arithmetic | 300-A Q2 | 2 h window; running minutes count; weight share minus used share; ties to the first listed (MP-14). |
| 3 orphan resumes | 300-A Q3 | Resumes always go first, ahead of the weights. |
| 4 where pool workers live | 300-A Q4, 530-D | Worker types in the top-level config, each naming the projects it serves (WT-C8). |
| 5 scope syntax | 320-A Q1, Q2 | A `Scope:` line, folders end in `/`, `; except`, `(add-only)` on the card or in categories.json `shared.addOnly` (DISP-22, DISP-24). |
| 6 overlap across projects | 320-A Q3 | Only when two projects share a repository, comparing paths (DISP-24). |
| 7 pattern overlap | 320-A Q4 | No patterns, folders only. |
| 8 enforcement | 330-B Q2 | Checked at dispatch; pushed changes are also checked and an out-of-scope card is logged (`ScopeAudit`). |
| 9 rejected pushes and conflicts | 330-B Q1 | The scheduler reads the git output in the raw worker log (`GitOutputLog`). |
| 10 redefined category | 560-A | Accepted at once, never held (AREA-2). |
| 11 red main, quota | 310-A Q1, Q2 | A red main stops only its project; quota stops only that agent's types, in every project (WT-9). |
| 12 page, port | 330-B Q3, 340-C | Port, theme, remote control and the pool's own log folder are top-level. `listen` is top-level beside the port (955-D Q2). Per-project state files stay per project (MP-H3). |
| 13 Worker: request | 310-A Q3, 530-D | Unchanged in meaning (WT-3). |
| 14 retries and time limits | 310-A Q4 | Per project, each its own. |
| 15 review note | - | Open, not part of this card (650-C gap (d)). |

## 1. Config

### The top-level pool file

```json
{
  "about": "The scheduler's pool: the projects it runs, each with its weight, and the worker types they share.",
  "projects": [
    { "name": "AmigaX",         "repo": "C:\\Projects\\AmigaX",         "weight": 70 },
    { "name": "ProjectToolkit", "repo": "C:\\Projects\\ProjectToolkit", "weight": 30 }
  ],
  "port": 8765,
  "listen": "lan",
  "remoteControl": true,
  "theme": "",
  "logDir": "",
  "toolkit": "",
  "pool": {
    "maxWorkers": 6,
    "types": {
      "spark":  { "max": 6, "agent": "" },
      "Sonnet": { "max": 4, "agent": "claude", "model": "claude-sonnet-5-5" },
      "Opus":   { "max": 2, "agent": "claude", "model": "claude-opus-5-5" }
    },
    "defaultWorker": "spark"
  }
}
```

The type caps (spark 6, Sonnet 4, Opus 2) are the developer's (955-D Q5, 2026-11-19). The weights
(70 / 30) and `maxWorkers` 6 are this design's proposal, not yet confirmed by the developer; the
cut-over (5.3) settles them.

- **MP-C1 Where it lives.** The pool file is the PC's own, not in any repository (WT-C8):
  `%LOCALAPPDATA%\ProjectToolkit\pool.json` by default, or the path after `-Pool` (MP-C9). The
  page's Worker types panel writes it in place (no commit). *Decided (955-D Q1, 2026-11-19).*
- **MP-C2 `projects`.** A list, in order (the order breaks ties, MP-6). Each entry: `name`
  (required; compares case-insensitively; must be unique, a repeat is skipped and logged), `repo`
  (required; the project's main checkout), `weight` (MP-4; missing = the smallest other, 300-A Q5),
  `jobsDir` (default `jobs`), `remote` (default `origin`), `branch` (default `main`). An entry with no
  name or repo is skipped and logged. The name must equal the project's own scheduler.json `project`
  (it names the log folder, mutex and themes); a mismatch is logged and the entry skipped. This is
  what `PoolConfig.Parse` reads today, plus the checks.
- **MP-C3 Top-level page settings** (330-B Q3, 340-C): `port` (default 8765), `listen` (*decided:
  beside the port, 955-D Q2*; default `local`), `remoteControl` (default false), `theme` (a theme name the loader offers, or
  a path; empty = the default theme), `logDir` (the pool's own log folder, default
  `%LOCALAPPDATA%\ProjectToolkit\pool`), `toolkit` (540-B). The projects' own `port`, `listen`,
  `remoteControl`, `page.theme` and `logDir` are not used for the page in a pool; their `logDir`
  still holds their state (MP-H3).
- **MP-C4 `pool`**: the worker types, exactly as WT-C1 to WT-C6, with `projects` per type (WT-C3).
  The projects' own workers.json files are not read in a pool (logged once per project, WT-C8).
- **MP-C5 Categories per project.** Category letters mean different things in different projects (AmigaX's
  D is not ProjectToolkit's D). *Moot since 969-A: worker types have no categories list, so a type needs no per-project letters (decided 955-D Q3, 2026-11-19, before the list was removed).*
- **MP-C6 What stays per project**, read from each project's own main as today (`PoolProjectReader`):
  scheduler.json (agents and the `worker` section, health command, `limits` incl. maxRetries and
  maxMinutes, planner and outbox, `jobsEnvVar`, `promptExtra`, `workerWindow`, `workspaceRoot`,
  `logDir`), categories.json (categories, default scopes, `shared.addOnly`), models.json, the cards, plans and
  requests, the README and the gates. Template refresh (roadmap, 540-B) runs per project.
- **MP-C7 Re-reading.** The pool file is re-read when its text changes (checked each tick); invalid
  JSON, or a file with no valid project, is logged and the previous settings kept (as WT-C6). A
  project added or removed takes effect on the next tick; a removed project's running workers are
  left to finish and are watched until they end (MP-H6). `port`, `listen` and `logDir` take effect
  only at the next start (logged).
- **MP-C8 Agents.** A type's `agent` is looked up in the scheduler.json of the project whose card it
  runs (WT-C9); the default worker (`agent` empty) is that project's `worker` section, so the
  `spark` type above runs copilot against spark for both projects because both define it so.
  Quota is kept per agent *name* across the pool (WT-9): `claude` in AmigaX and `claude` in
  ProjectToolkit are one agent for quota.
- **MP-C9 One project keeps working unchanged.** `scheduler-next <repository>` is today's
  one-project scheduler, with its own scheduler.json and workers.json, and none of this applies. A
  pool starts only with `scheduler-next -Pool [<file>]` (no repository argument; `-Slots`,
  `-NoHealthCheck`, `-NoBrowser`, `-Paused`, `-DryRun` apply to every project; `-Port` overrides the
  pool's port). A pool file with one project is a valid pool.

## 2. Dispatch

What stays exactly as it is: each project's plan of candidates (DISP-1 to DISP-14, DISP-22 to
DISP-24), the worker-type rules WT-1 to WT-14, and `PoolDispatcher.Dispatch` (WT-7). The rules below
fix what the pool adds around them.

- **MP-10 One dispatch per tick.** Each tick, every project runs its steps up to and including
  planning (read cards, planner, categories, models, requests, plan, health start) in pool order; then
  one dispatch (WT-7) runs across all of them; then each project saves retries and writes its
  status. A project whose cards cannot be read keeps its previous list (CARD-3) and still offers.
- **MP-11 Total and caps are pool-wide.** WT-1's running count is every running worker of every
  project (adopted ones included); WT-2's count for a type is its running workers in every project.
- **MP-12 Eligible projects.** A project offers cards only when its own state is running
  (`PoolGate.Eligible`): paused, draining, paused-red (310-A Q1: a red main stops only its project)
  offer nothing and get the note "next up; dispatching is <state>" (WT-12 1).
- **MP-13 Resumes first.** If any eligible project's first usable offer is a resume (an orphan in
  active/), only resumes take part in that round (300-A Q3); among them the shares decide as MP-14.
- **MP-14 Weights choose the project; priority orders inside it.** Of the offers, the project
  furthest below its share starts its card (MP-6, `PoolShares`): share = the project's worker-minutes
  in the last 2 hours (finished runs clipped to the window, running ones up to now) over all
  offering projects' minutes; the gap is weight / sum of the offering projects' weights minus that
  share; the largest gap wins; ties go to the project listed first. With two projects, one with no
  minutes in the window always wins against one with some (300-A's "no minutes wins"); with three
  or more it is the gap that decides, as `PoolShares` computes it. A `Priority: high` card never jumps the weights
  (300-A Q1); it is first only inside its project.
- **MP-15 Runs for the shares.** The window's runs come from each project's accounting.csv (rows
  whose end is inside the window; start and end from the row) and from every running worker. A
  start inside a tick adds no minutes in that tick, so one project may take several places in one
  tick until its offers run out (910-A's note; kept: it fills an idle pool fast).
- **MP-16 Orphans across a removed type or project.** An orphan is resumed by its own project only
  (its worktree folder is in that project's repository). An orphan of a project no longer in the
  pool file is not resumed; it stays in that project's active/ (logged once).
- **MP-17 Quota.** An out-of-quota result marks the run's agent name out of quota (WT-9); every
  type with that agent stops starting, in every project. Resume (per agent, or the control window's
  Resume all) clears it for the pool.
- **MP-18 Retries and time limits** are each project's own (310-A Q4): a project's maxRetries,
  maxMinutes, stallMinutes, warnMinutes and grace minutes apply to its cards; the time limit from
  history (640-A) reads that project's accounting.csv.
- **MP-19 Threads.** Threads per worker = max(1, floor(cores / pool `maxWorkers`)) (MP-3, WT-11),
  given to each worker in its own project's `jobsEnvVar` and `{jobs}`. The health check of each
  project uses the same figure (today it divides by the project's `slots`).
- **MP-20 Scopes across projects.** Two projects' cards are checked against each other only when
  their repositories are the same (DISP-24, 320-A Q3), so in the AmigaX + ProjectToolkit pool no
  card ever waits for the other project's scope.
- **MP-21 Queue notes.** WT-12's notes, per project; "next up; no worker type serves project <P>"
  when no type's `projects` names it; and one more for a project that offered but lost on shares:
  "next up; <P> is above its share (<used>% of <weight>%)".
- **MP-22 Dry run** (`-DryRun`) logs "DRY RUN <project> <type> in slot <n> <- <card>" and starts
  nothing (WT-14 with the project).

## 3. Host

- **MP-H1 One process.** One `scheduler-next -Pool` process runs every project. It holds a pool
  mutex `Global\ProjectToolkit-pool-scheduler` and each project's own mutex (CFG-14), so a
  one-project scheduler of a pooled project, or a second pool, refuses to start with the existing
  message. A project whose mutex is taken (its one-project scheduler is running) is left out and
  shown on the page as "not in the pool: its own scheduler is running"; the pool retries it each
  minute.
- **MP-H2 Per project**, each built as `HostComposition` builds them today: `MainRepository`
  (fetch, read main), `AnswersCommitter`, `CardReader`, `CardRequestStep` (its moves folder in the
  project's logDir), `PlannerStep` and its outbox, `HealthCheck` with its worktree, `TemplateRefresh`,
  `SlotWorktrees` (its own `workspaceRoot\slot1..slot<maxWorkers>`, WT-W1), `SlotFiles`,
  `AccountingCsv`, `StateFiles`, `RetriesFile`, `StoppedCardHold`, `FinishedCardCheck`, the scope
  audit and git-output log. One fetch of each project's main per tick.
- **MP-H3 State files stay per project**, in the project's logDir as today: running.json,
  retries, workers-state.json (for migration only), accepted categories, planner switch, accounting.csv,
  status.txt, prompts and worker logs, slot files, move requests. The pool's own logDir holds the
  pool log (the scheduler's log lines, each prefixed with the project where it concerns one) and
  pool status (`status.txt`: the pool's projects, their state and share, then the WT-P5 lines).
- **MP-H4 Shared**: the clock, `WorkerProcesses` and job objects, `HostLoad`, the model-server cache
  (`ServerCache`, one probe per endpoint for the pool), `AgentQuota`, the page server and live feed,
  the tick loop and its pause between ticks (the smallest `pollSeconds` of the projects).
- **MP-H5 Worker numbers.** A worker's number is its folder number, unique within its project
  (WT-W3). Everything a worker sees (prompt, `stop-slotN`, `warn-slotN`, `msg-slotN.txt`, moves
  folder) is in its project's logDir, so nothing in the worker's brief changes. Everything the page
  and the live feed address carries `project` and `slot`.
- **MP-H6 Adoption.** At start, each project's running.json is adopted as ADOPT-2 / WT-W5, into
  that project's tick; adopted workers count toward MP-11. A project dropped from the pool file
  while it has running workers keeps being watched (reaped, finished, accounted) until they end,
  but offers no cards.
- **MP-H7 Health and gates** stay per project (MP-9): a red main pauses only that project
  (paused-red); its health check runs in its own health worktree with its own command.
- **MP-H8 Stop all and exit** (page, tray Exit) stops every project's workers the way it does
  today, then exits. Pause, Drain and Resume act on one project (the page sends `project=`) or, with
  no project, on all of them: the page sends the project picked in its project dropdown, and no
  project when it says All projects. *Decided (955-D Q4, 2026-11-19).*
- **MP-H9 Tray.** `scheduler-tray -Pool [<file>]` starts the pool instead of a repository. The
  tooltip leads with "Switchyard" and names each project's state ("Switchyard: AmigaX running, ProjectToolkit paused-red");
  double-click opens the pool's page; Exit and View log are unchanged.

## 4. Page

One page on the pool's port (B3 Q12): `CombinedPageRenderer`, fed one `PageSnapshot` per project
plus a pool part (weights, shares, worker types, total).

- **MP-P1 Settings.** Port, `listen`, `remoteControl` and theme are the pool file's (MP-C3). The
  theme choice on the page lists the pool's theme first, then every theme the loader offers,
  including each project's own `page.theme` (so AmigaX's Workbench theme stays one click away).
- **MP-P2 Project selector** (developer, 2026-11-20). One dropdown in the top bar beside the theme menu, available
  in every view (Workers, card board, Gantt). It is shown only while two or more projects are active; with one
  project it is not there and the page looks as today. Its items are **All projects**, then each project by name.
  The choice filters every window and view to that project (All shows the whole collection), is one selection for
  the whole page (kept when switching views, not per view), and is remembered in the browser. Pause / Drain /
  Resume act on the selected project, or on every project for All (MP-H8, unchanged).
- **MP-P3 One Waiting on you** across projects, each row labelled with its project; every button
  posts `project=` beside `card=`.
- **MP-P4 Projects window** (exists, 340-C): per project its state, weight, share in the window
  (used % against weight %), running workers, queue length, health (green/red/checking, last sha),
  planner line, and Pause / Drain / Resume / Check main for that project.
- **MP-P5 Statistics per project and per model**: each project's own stats text (ACCT-6), and an
  All projects line per model summed from every project's accounting.csv. Trends (630-A) per
  project with the filter; All sums them.
- **MP-P6 Worker windows**: one per running worker across projects (WT-P1), title
  "<project> - <type> - slot <n> - <card>"; Stop, Kill and the message box post `project=` and `slot=`.
- **MP-P7 Worker types panel** (WT-P3) writes the pool file (WT-C8, no commit) and shows a Projects
  column per type. Worker types have no categories (969-A, developer 2026-11-19: the planner picks the worker
  with the card's `Worker:` line), so neither the panel nor the pool file carries per-project category lists (MP-C5).
- **MP-P8 Routes.** Every route that names a card or a slot takes `project=`; without it, in a pool,
  it is refused ("which project?") except the pool-wide ones: `/`, `/tick`, `/stopall`,
  `/worker-types`, `/agent-resume`, `/font`, and `/pause`, `/drain`, `/resume` (all projects, MP-H8).
  Per project: `/answer`, `/card`, `/done`, `/health`, `/health-now`, `/kill`, `/log`, `/open`,
  `/open-lanes`, `/open-workers`, `/planner-toggle`, `/repo-file`, `/request`, `/resume-card`,
  `/retry`, `/review`, `/send`, `/stop`, `/template-keep`, `/template-overwrite`; `/accept-lane` is
  dead (AREA-2) and is not carried over.
- **MP-P9 Project chip** (developer, 2026-11-20). Every list row and card that belongs to a project (Queue, Recent
  events, Waiting on you, Requests, the card board's cards, Gantt rows, the Projects window) carries a project chip
  in the same style as the category chip (970-C): the project's short name in a colour of its own (stable per
  project, readable in every theme, distinct from the category colours), the full name and repository path on hover.
  The chip is shown only while the dropdown is on **All projects**; with one project selected, or only one project
  active, it is left out. Worker window titles keep the project name in text (MP-P6).

**What the current page needs for this** (what `PageRenderer` has and `CombinedPageRenderer` does
not yet, checked against the 340-C renderer and the route list):

1. The control window (state, Pause / Drain / Resume / Stop all and exit, version) with the
   per-project buttons of MP-P4.
2. Health per project (banner for a red project, Check main), the planner line and switch per
   project, and template edits (Overwrite / Keep) per project.
3. The review panel (650-C) and card page opened with `project=`.
4. The project dropdown in the top bar (MP-P2): shown only with two or more projects, one page-wide selection
   kept across views and in the browser. The Worker types panel with the Projects column (MP-P7, no categories
   per type), and per-agent Resume.
5. The live feed and worker log keyed by project and slot (`LiveFeed`, `/log`).
6. Trends with the filter (MP-P5) and the per-model All line.
7. Queue notes per project including MP-21's share note; the Recent events Worker column naming
   the type (950-C) with the project.
8. Notices (`PageNotice`) per project, labelled.
9. The project chip (MP-P9) on every project-owned row and card, shown only under All projects.

## 5. AmigaX's move

### 5.1 Decided

- Its queue moves from `docs/jobs/` to `jobs/` (developer, 2026-11-18).
- Its categories stay as they are; they become categories (labels and default scopes) as everywhere.
- Its workers become the pool's shared types; its workers.json is no longer read (WT-C8).
- AmigaX stays idle until multi-project support lands, then joins as the pool's second project.

### 5.2 What maps where (from AmigaX's scheduler.json and workers.json)

| AmigaX today | In the pool |
|---|---|
| `slots` 3, workers.json 8 slots (6 spark, Claude/Sonnet, Opus) | The pool's types: spark 6, Sonnet 4, Opus 2 (955-D Q5); `maxWorkers` proposed 6 |
| `port` 8765, `listen` lan, `remoteControl` true | The pool file's (MP-C3) |
| `page.theme` `docs/jobs/scheduler-theme.json` | `jobs/scheduler-theme.json`, offered on the page (MP-P1), or the pool theme |
| `jobsDir` `docs/jobs` | `jobs` (scheduler.json and the pool entry's default) |
| `planner.outbox` `docs\jobs\.planner-outbox` | `jobs\.planner-outbox` |
| `jobsEnvVar` `AMIGAX_JOBS`, `promptExtra`, `workerWindow`, `limits`, `health`, `worker`, `agents` | Unchanged, per project (MP-C6) |
| `workspaceRoot` `C:\Projects\AmigaX-workspaces` | Unchanged; slot folders up to the pool's `maxWorkers` (WT-W1) |

### 5.3 Cut-over steps (planner and developer, by hand)

1. Before: the gap cards from 954-B are done (what scheduler-next cannot yet read in AmigaX's
   files: for example categories.json `owns` with patterns like `vm/68000.*`, which scopes read as folders
   only (320-A Q4), and `requests.md` / `history.md` as single files), and the pool cards of section 7
   are done and the ProjectToolkit pool runs on its own (a one-project pool file).
2. Drain AmigaX: no card in `active/`, no worker of the old scheduler running; stop and close its
   old scheduler for good.
3. In AmigaX, one commit: `git mv docs/jobs jobs`; scheduler.json `jobsDir`, `planner.outbox` and
   `page.theme` paths; every card, plan, brief and README that names `docs/jobs/` (a search and
   replace, checked by hand); then the generic files refreshed from the toolkit's templates (the
   scheduler's template refresh does this at start, or `projecttoolkit.ps1 -Action jobs -Update`).
   workers.json stays, unread, so AmigaX can still run alone if the pool must be backed out.
4. Write the pool file: both projects with their weights, the types (spark 6, Sonnet 4, Opus 2), port
   and theme. The developer settles the weights and `maxWorkers` here (proposed 70 / 30 and 6).
5. Start the pool with `-DryRun` once: check on the page that both projects' cards, categories, scopes,
   types and shares look right and the DRY RUN lines pick sensible cards.
6. Start it for real (tray: `scheduler-tray -Pool`). Watch the first AmigaX card through to done.
7. Back-out: stop the pool, start `scheduler-next C:\Projects\ProjectToolkit` and, if wanted,
   `scheduler-next C:\Projects\AmigaX` on their own (their workers.json files are still there).

## 6. What changes in the code

### Core (category A)

- `Tick/SchedulerTick.cs`: split `Run` into `Prepare()` (sample, reap, index logs, read cards,
  fix duplicates, planner, categories, workers, models, requests, plan, health start), `Dispatch()` (the
  one-project `TypeDispatcher` call, as today) and `Complete()` (save retries, write status); `Run`
  stays as the three in a row, so one project behaves exactly as today. Expose `Start(type, attempt)`
  for a pool, the running workers by type, and take `AgentQuota` and `ServerCache` from the
  constructor (shared in a pool; new ones in one project).
- New `Pool/PoolTick.cs`: per tick, each project's `Prepare`; builds the `PoolProject` list (name,
  weight, plan, agents), types with pool-wide running counts (MP-11), the runs (MP-15); calls
  `PoolDispatcher.Dispatch`; routes each start to its project's tick; each project's `Complete`.
  Re-applies the pool's types when the pool file changes.
- `Pool/PoolDispatcher.cs`: the share note of MP-21; the "serves project" note (WT-12 2) if not
  already there.
- `Categories/WorkerType.cs`, `WorkerTypesJson`: `Fits` with the project (no categories list, 969-A).
- `WebContract`: a `PoolPageSnapshot` (projects with weight, share, state; types with running per
  project; total), and `project` on page actions that name a card or slot.

### Host (category B)

- `Program.cs`, `Observe/ObserveCommandLine.cs` and the usage text: `-Pool [<file>]` (MP-C9).
- `Pool/PoolConfig.cs`: `listen`, `logDir`, `about`; the checks of MP-C2; a `PoolConfigFile` that
  finds, reads and re-reads it (MP-C1, MP-C7) and writes the `pool` section back for the panel.
- New `Pool/PoolComposition.cs`: the pool's composition root (MP-H2, MP-H4); per project it reuses
  the parts `HostComposition` builds today (factor those into a per-project builder rather than
  copying them); one `SchedulerRunner` over `PoolTick`.
- `Program/SingleInstance.cs`: the pool mutex and each project's (MP-H1).
- `Program/HealthWorktreeRunner.cs`: threads from the pool's `maxWorkers` (MP-19).
- `Program/SchedulerRunner.cs`, `RunnerParts`: run a pool tick; Stop all and exit across projects.
- `Processes/WorkerAdopter.cs`, `RunningJson`: adoption per project into its tick (MP-H6).
- `State/AccountingCsv.cs`: the window's runs (`PoolRun`) for MP-15.
- `Processes/StatusFile.cs`: the pool's status.txt (MP-H3).
- `Tick/FileSchedulerLog.cs`: the pool log with project prefixes.
- `PageActions/HostPageActions.cs`: a `PoolPageActions` that routes by `project=` to that project's
  actions, and the pool-wide actions (MP-P8); `WorkersJsonText`'s pool form writes the pool file.
- `Tick/HostPageData.cs`, `PageActions/HostPageSnapshot.cs`: one snapshot per project plus the
  pool part.
- `Program/PageThemeLoader.cs`: the pool's theme and every project's own theme (MP-P1).
- `Scheduler.Tray`: `TrayArguments` and `TrayTooltip` for `-Pool` (MP-H9).

### Web (category C)

- `Server/IPageData.cs`, `IPageRenderer.cs`: a pool page data source (several snapshots plus the
  pool part); the renderer chosen by it.
- `Server/PageRouter.cs`: `project=` on the per-project routes, refused when missing in a pool
  (MP-P8); the one-project page ignores it.
- `Server/LiveFeed.cs`, `LiveLog.cs`: workers keyed by project and slot.
- `Views/CombinedPageRenderer.cs`, `PageScript.cs`: the items of section 4's list.

### Test plan

Tests named by rule, in `test-plans/multi-project.md` (card 1). Unit level, with a fake clock and
fake projects; no git or processes in Core tests.

- `MP_C2_ProjectEntries_SkipNamelessRepeatedAndMismatched`, `MP_C2_MissingWeight_SmallestOther`
- `MP_C3_PageSettings_TopLevel_ProjectsIgnored`, `MP_C5_AreasPerProject_TextAndObject`
- `MP_C7_PoolFileChanged_ReappliedNextTick`, `MP_C7_InvalidPoolFile_KeepsPrevious`
- `MP_C9_OneProject_Unchanged` (today's `SchedulerTick` tests pass through `Run` unchanged)
- `MP_10_OneDispatchAcrossProjects`, `MP_11_TotalAndCapsCountEveryProject`
- `MP_12_RedProject_OthersRunOn`, `MP_13_ResumesFirst_AcrossProjects`
- `MP_14_FurthestBelowShare_Wins`, `MP_14_HighPriority_DoesNotJumpWeights`, `MP_14_TieToFirstListed`
- `MP_15_RunsFromAccountingAndRunning_ClippedToWindow`
- `MP_16_OrphanOfRemovedProject_NotResumed`, `MP_17_QuotaByAgentName_AllProjects`
- `MP_18_RetriesAndLimits_PerProject`, `MP_19_Threads_FromPoolMaxWorkers`
- `MP_20_SeparateRepositories_NeverOverlap`, `MP_21_AboveShareNote`, `MP_22_DryRun_NamesProject`
- Host: `MP_H1_ProjectMutexTaken_ProjectLeftOut`, `MP_H3_StateFilesPerProjectLogDir`,
  `MP_H6_AdoptionPerProject`, `MP_H6_RemovedProject_WorkersWatchedToEnd`,
  `MP_H8_PauseOneProject_OthersRun`, `MP_H9_TrayPoolArguments`
- Web: `MP_P3_WaitingRowsLabelled_ButtonsPostProject`, `MP_P4_ProjectsWindow_WeightAndShare`,
  `MP_P8_PerProjectRoute_WithoutProject_Refused`, `MP_P8_PoolWideRoutes_NoProjectNeeded`,
  `MP_P6_WorkerWindowTitle_NamesProject`
- A pool end to end in Host with two temporary git repositories and the dry run (`MP_22`), so the
  composition is exercised without starting agents.

## 7. Cards (proposed, in order)

Numbers are placeholders for the planner. "Worker" is the `Worker:` line proposed.

1. **D - spec and test plan for multi-project.** Move the MP rules into `spec.md` (B1 extended, B3
   marked closed as in section 0), with the developer's answers to this card's questions; write
   `test-plans/multi-project.md`. Depends on: this card's answers. Worker: Sonnet.
2. **A - the pool tick.** `SchedulerTick` split, `PoolTick`, shared quota and server cache, pool-wide
   counts, runs input, the notes of MP-21, `PoolPageSnapshot` and `project` in WebContract (no
   categories per type, MP-C5). Tests MP_10 to MP_22, MP_C9. Depends on: 1. Worker: Sonnet
   (it touches the tick).
3. **B - the pool in Host.** `-Pool`, the pool file (find, read, re-read, write back), the per-project
   builder factored out of `HostComposition`, `PoolComposition`, mutexes, adoption, accounting runs,
   threads, status and log, dry run. Tests MP_C1 to MP_C7, MP_H1 to MP_H8, the two-repository dry
   run. Depends on: 2. Worker: Sonnet.
4. **C - the pool page.** Section 4's list in `CombinedPageRenderer`, `PageScript`, `PageRouter`
   (`project=`), `LiveFeed`, and the project dropdown (MP-P2) in the top bar and the project chip (MP-P9). Tests MP_P*. Depends on: 2 (the contract); can run beside 3.
   Worker: none (default type).
5. **B - serve the pool page and the tray.** `PoolPageActions`, pool page data, theme loader, the
   tray's `-Pool` (MP-H9). Depends on: 3, 4. Worker: none (default type).
6. **B - ProjectToolkit as a one-project pool.** Write the pool file for this project alone, run the
   dry run and then a few real cards; to review for the developer's look at the page. Depends on: 5.
   Worker: none. (Could be the developer's own step.)
7. **AmigaX's gap cards from 954-B**, in the categories 954-B names. Can run beside 2 to 5.
8. **Planner and developer - AmigaX cut-over** (5.3). Not a worker card: a checklist for the
   planner with the developer. Depends on: 6, 7.

After these, roadmap item 4 in the order (item 5, the three views of the page) builds on the pool
page, so its cards should follow 4 and 5 here.

## Decisions (formerly open points)

The developer answered card 955-D's questions on 2026-11-19:

| # | Point | Answer |
|---|---|---|
| 1 | Where the pool file lives and how a pool starts (MP-C1, MP-C9) | `%LOCALAPPDATA%\ProjectToolkit\pool.json`, started with `scheduler-next -Pool`; one project starts as today. |
| 2 | Whether `listen` is top-level with the port (MP-C3) | Yes, beside the port. |
| 3 | Categories per project in a type (MP-C5) | Yes, per project (later moot: 969-A removed categories from worker types, 2026-11-19). |
| 4 | What Pause, Drain and Resume act on (MP-H8) | The project picked in the page's project filter; every project when it says All. |
| 5 | Starting numbers | Type caps spark 6, Sonnet 4, Opus 2. Weights and `maxWorkers` were not given: 70 / 30 and 6 stay proposals, settled at the cut-over (5.3). |
