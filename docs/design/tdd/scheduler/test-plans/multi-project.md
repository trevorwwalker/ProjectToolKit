# Test plan: multi-project pool

Rules: MP-C1 to MP-C9, MP-10 to MP-22, MP-H1 to MP-H9, MP-P1 to MP-P9 (`spec.md`, B1.1 to B1.4).
Source: `multi-project.md` (design, card 955-D; answers 2026-11-19; 989-D 2026-11-20), section 6's test list.
This plan supersedes `weighted-effort.md` (written for MP-1 to MP-9 while B3 was open); its tests stay valid and
are covered here by the rules that settle them. Cards: 997-A (Core: MP_10 to MP_22, MP_C9), 998-B (Host: MP_C*, MP_H*),
999-C (Web: MP_P*), 1000-B and 1001-B (serve the pool, one-project pool). Tests are written first.

Shared setup unless a test says otherwise: projects `AmigaX` (weight 70) and `ProjectToolkit` (weight 30), in that order,
separate repositories; types `spark` (max 6, default agent), `Sonnet` (max 4, agent `claude`, premium), `Opus` (max 2,
agent `claude`, premium), all serving both projects; `maxWorkers` 6; fake clock; both projects `running`, servers up, no
agent out of quota, nothing running, no minutes in the 2 h window. Core tests use fake projects: no git or processes.

## Config (MP-C*)

### MP_C1_PoolFile_DefaultPath_OrGivenPath
- Checks: MP-C1 · Project: Host
- Setup: no `-Pool` file; then `-Pool D:\x\p.json`.
- Expected: the first reads `%LOCALAPPDATA%\ProjectToolkit\pool.json`; the second that path; neither is inside a repository; saving the Worker types panel rewrites the same file and makes no commit.

### MP_C2_ProjectEntries_SkipNamelessRepeatedAndMismatched
- Checks: MP-C2 · Project: Core
- Setup: entries: no name; no repo; `AmigaX` twice (second as `amigax`); `Other` whose scheduler.json `project` is `Else`; one valid.
- Expected: only the valid one and the first `AmigaX` stay; each skipped entry is logged with the reason; defaults `jobsDir` `jobs`, `remote` `origin`, `branch` `main` apply.

### MP_C2_MissingWeight_SmallestOther
- Checks: MP-C2, MP-4 · Project: Core
- Setup: weights 70, none, 30.
- Expected: the missing weight is 30 (the smallest other); all missing -> equal weights.

### MP_C3_PageSettings_TopLevel_ProjectsIgnored
- Checks: MP-C3 · Project: Core
- Setup: pool file `port` 9000, `listen` lan, `remoteControl` true; each project's scheduler.json says port 8765, `listen` local, `remoteControl` false, another theme and `logDir`.
- Expected: the page uses 9000 / lan / remote control on / the pool theme; omitted keys give 8765, `local`, false, default theme; each project's `logDir` still holds its state files.

### MP_C4_ProjectWorkersJson_NotRead_LoggedOnce
- Checks: MP-C4, WT-C8 · Project: Host
- Setup: both projects have a workers.json.
- Expected: types come from the pool file's `pool` section; one log line per project that its workers.json is ignored, not one per tick.

### MP_C5_NoCategoriesPerType
- Checks: MP-C5, MP-P7 · Project: Core
- Setup: a type with an old `areas` key, or per-project category lists.
- Expected: ignored; every category fits (969-A).

### MP_C6_PerProjectFilesReadFromOwnMain
- Checks: MP-C6 · Project: Host
- Setup: two temporary repositories whose scheduler.json, categories.json and cards differ.
- Expected: each project's limits, planner, health command, categories and cards come from its own main only.

### MP_C7_PoolFileChanged_ReappliedNextTick
- Checks: MP-C7 · Project: Core
- Setup: a third project added, a weight changed and a type cap changed in the file between ticks.
- Expected: the next tick uses all three; `port` changed alone logs "takes effect at next start" and changes nothing.

### MP_C7_InvalidPoolFile_KeepsPrevious
- Checks: MP-C7 · Project: Core
- Setup: invalid JSON; then a file with no valid project.
- Expected: logged each time; the previous projects, weights and types stay in force.

### MP_C7_RemovedProject_OffersNothing_WorkersKept
- Checks: MP-C7, MP-H6 · Project: Core
- Setup: `ProjectToolkit` removed while it has one running worker.
- Expected: it offers no cards from the next tick; the worker is still counted (MP-11) and watched to its end.

### MP_C8_AgentFromRunningCardsProject
- Checks: MP-C8 · Project: Core
- Setup: type `Sonnet` (agent `claude`); `claude` defined differently in the two projects' scheduler.json.
- Expected: a card of each project starts with its own project's `claude` definition; `spark` (empty agent) starts that project's `worker` section.

### MP_C9_OneProject_Unchanged
- Checks: MP-C9 · Project: Core
- Setup: every existing `SchedulerTick` test, through `Run`.
- Expected: all pass unchanged; `Run` is `Prepare`, `Dispatch`, `Complete` in a row.

### MP_C9_NoPoolFlag_OneProjectScheduler
- Checks: MP-C9 · Project: Host
- Setup: `scheduler-next <repository>` with no `-Pool`; then `-Pool` with a file holding one project.
- Expected: the first reads the repository's own scheduler.json and workers.json and takes no pool mutex; the second is a valid pool with one project; `-Pool` together with a repository is refused with the usage text; `-Port` overrides the pool's port.

## Dispatch (MP-10 to MP-22)

### MP_10_OneDispatchAcrossProjects
- Checks: MP-10 · Project: Core
- Setup: both projects have ready cards; 2 workers free in total.
- Expected: each project's prepare steps run before the single dispatch; the dispatch runs once; each project's retries are saved and its status written after it, in pool order.

### MP_10_UnreadableCards_PreviousListStillOffers
- Checks: MP-10, CARD-3 · Project: Core
- Setup: `AmigaX` cards cannot be read this tick; previous list had a ready card.
- Expected: the previous list is used; `AmigaX` still offers.

### MP_11_TotalAndCapsCountEveryProject
- Checks: MP-11, WT-1, WT-2 · Project: Core
- Setup: `maxWorkers` 6; `Opus` max 2; one Opus runs for each project; three more running (one adopted).
- Expected: the total is 5 (adopted counted), so one more starts at most; no third `Opus` starts for either project.

### MP_12_RedProject_OthersRunOn
- Checks: MP-12, MP-H7 · Project: Core
- Setup: `AmigaX` paused-red, then paused, then draining; `ProjectToolkit` running with a ready card.
- Expected: the card of `ProjectToolkit` starts each time; `AmigaX` offers nothing and its queue note is "next up; dispatching is <state>".

### MP_13_ResumesFirst_AcrossProjects
- Checks: MP-13 · Project: Core
- Setup: `AmigaX` has a ready card; `ProjectToolkit` has an orphan in active/ and a `Priority: high` card.
- Expected: the orphan is resumed first, although `AmigaX` is furthest below its share; with an orphan in each project, the shares pick between the two resumes.

### MP_14_FurthestBelowShare_Wins
- Checks: MP-14, MP-6 · Project: Core
- Setup: window minutes `AmigaX` 20, `ProjectToolkit` 40; weights 70 / 30.
- Expected: shares 33% / 67%, gaps +37 / -37 points: `AmigaX` starts. With three projects (50 / 30 / 20, minutes 10 / 10 / 0) the largest gap decides, not "no minutes wins".

### MP_14_NoMinutes_BeatsSome_TwoProjects
- Checks: MP-14 · Project: Core
- Setup: `ProjectToolkit` has no minutes in the window, `AmigaX` has some, though `AmigaX` is far below its weight.
- Expected: `ProjectToolkit` wins (300-A: no minutes wins).

### MP_14_HighPriority_DoesNotJumpWeights
- Checks: MP-14, 300-A Q1 · Project: Core
- Setup: `ProjectToolkit` has a `Priority: high` card and is above its share; `AmigaX` is below with a normal card.
- Expected: `AmigaX`'s card starts; inside `ProjectToolkit` the high-priority card is still first.

### MP_14_TieToFirstListed
- Checks: MP-14 · Project: Core
- Setup: equal gaps (equal weights, equal minutes), then the project order swapped in the file.
- Expected: the first listed wins each time.

### MP_14_IdleProject_ShareGoesToOthers
- Checks: MP-14, MP-6 · Project: Core
- Setup: `AmigaX` has no ready card; only `ProjectToolkit` offers.
- Expected: `ProjectToolkit` starts; the gap is taken over the offering projects only (weight / sum of the offering weights).

### MP_15_RunsFromAccountingAndRunning_ClippedToWindow
- Checks: MP-15 · Project: Core (the accounting read in Host: MP_H3)
- Setup: now = 12:00, window starts 10:00; `AmigaX` accounting rows 09:30-10:30 (ends inside the window), 10:30-11:00 and 08:00-09:00 (ends before it); one worker running since 11:45.
- Expected: the first row counts 30 minutes (clipped to the window); 10:30-11:00 counts 30; the 08:00-09:00 row counts 0; the running worker counts 15 up to now; the total is 75.

### MP_15_StartInTick_AddsNoMinutes_SameProjectMayFillIdle
- Checks: MP-15 · Project: Core
- Setup: 4 free workers; `AmigaX` far below its share with 4 ready cards; `ProjectToolkit` has one.
- Expected: `AmigaX` takes places until its offers run out in that tick (starts add no minutes); `ProjectToolkit` still starts when `AmigaX` has no offers left.

### MP_16_OrphanOfRemovedProject_NotResumed
- Checks: MP-16 · Project: Core
- Setup: an orphan in a project that is no longer in the pool file.
- Expected: not resumed by any project; one log line, not one per tick; the card stays in that project's active/.

### MP_17_QuotaByAgentName_AllProjects
- Checks: MP-17, WT-9 · Project: Core
- Setup: a `claude` run in `AmigaX` ends out of quota.
- Expected: `Sonnet` and `Opus` start nothing in either project; `spark` still starts; Resume for `claude` clears it for both.

### MP_18_RetriesAndLimits_PerProject
- Checks: MP-18 · Project: Core
- Setup: `AmigaX` maxRetries 2 / maxMinutes 30; `ProjectToolkit` maxRetries 5 / maxMinutes 60; a card of each failed twice, and one just under each limit.
- Expected: each project's card is stopped or retried by its own limits; the time limit from history reads only that project's accounting.csv.

### MP_19_Threads_FromPoolMaxWorkers
- Checks: MP-19, WT-11 · Project: Core
- Setup: 16 cores; `maxWorkers` 6; `AmigaX` `slots` 3.
- Expected: 2 threads for every worker, given in each project's own `jobsEnvVar` and `{jobs}`; each health check uses 2; 1 core gives 1.

### MP_20_SeparateRepositories_NeverOverlap
- Checks: MP-20, DISP-24 · Project: Core
- Setup: both projects have a running and a ready card on the same path text (`src/`).
- Expected: neither waits for the other; with both projects naming the same repository, the paths are compared.

### MP_21_AboveShareNote
- Checks: MP-21 · Project: Core
- Setup: `ProjectToolkit` offers but loses on shares (used 67% of 30%); another project's type list does not serve `Other`.
- Expected: `ProjectToolkit`'s first queued card says "next up; ProjectToolkit is above its share (67% of 30%)"; `Other` says "next up; no worker type serves project Other".

### MP_22_DryRun_NamesProject
- Checks: MP-22, WT-14 · Project: Core
- Setup: `-DryRun`; one ready card in each project.
- Expected: log lines "DRY RUN AmigaX spark in slot 1 <- <card>" and the same for the other; no worker started, no retry counted.

## Host (MP-H*)

### MP_H1_ProjectMutexTaken_ProjectLeftOut
- Checks: MP-H1 · Project: Host
- Setup: a one-project scheduler holds `ProjectToolkit`'s mutex; the pool starts.
- Expected: the pool runs with `AmigaX` only; the page snapshot says "not in the pool: its own scheduler is running"; after that scheduler exits the next minute-retry adds the project. A second pool, or a one-project scheduler of a pooled project, refuses with the existing message.

### MP_H2_PerProjectParts_OneFetchPerTick
- Checks: MP-H2 · Project: Host
- Setup: two temporary repositories with bare remotes.
- Expected: each project has its own slot worktrees `slot1..slot6` under its `workspaceRoot`; one fetch of each main per tick.

### MP_H3_StateFilesPerProjectLogDir
- Checks: MP-H3 · Project: Host
- Setup: both projects run a fake worker.
- Expected: running.json, retries, accounting.csv, status.txt, prompts, logs, slot files and move requests are in each project's own logDir; the pool's logDir holds only the pool log (lines prefixed `AmigaX:`) and the pool status.txt (projects, state, share, WT-P5 lines).

### MP_H4_SharedParts_OneInstance
- Checks: MP-H4 · Project: Host
- Setup: the composition of two projects.
- Expected: one clock, process table, quota, server cache and page server; the pause between ticks is the smallest `pollSeconds`; one model probe per endpoint.

### MP_H5_WorkerNumbers_UniquePerProject
- Checks: MP-H5, WT-W3 · Project: Host
- Setup: a worker in slot 1 of each project.
- Expected: both exist; each sees `stop-slot1`, `warn-slot1`, `msg-slot1.txt` and the moves folder in its own project's logDir; the live feed addresses them by project and slot.

### MP_H6_AdoptionPerProject
- Checks: MP-H6 · Project: Host
- Setup: running.json of each project with a live fake worker.
- Expected: each is adopted into its own project's tick; both count toward the pool's total (MP-11).

### MP_H6_RemovedProject_WorkersWatchedToEnd
- Checks: MP-H6 · Project: Host
- Setup: a project is removed from the file while its worker runs; the worker then ends.
- Expected: it is reaped, finished and accounted in its project's accounting.csv; the project offers no cards in between.

### MP_H7_RedMain_PausesOnlyThatProject
- Checks: MP-H7 · Project: Host
- Setup: `AmigaX`'s health command fails.
- Expected: `AmigaX` is paused-red and its health check ran in its own worktree with its own command; the other project dispatches.

### MP_H8_PauseOneProject_OthersRun
- Checks: MP-H8 · Project: Host
- Setup: `/pause?project=AmigaX`; then `/pause` with no project; then `/resume` with no project.
- Expected: only `AmigaX` paused; then both; then both running. Stop all and exit stops every project's workers, then exits.

### MP_H9_TrayPoolArguments
- Checks: MP-H9 · Project: Host (Tray)
- Setup: `scheduler-tray -Pool` and `-Pool D:\x\p.json`.
- Expected: starts the pool with the file; the tooltip reads "pool: AmigaX running, ProjectToolkit paused-red"; double-click opens the pool's page; Exit and View log as before.

### MP_22_PoolEndToEnd_TwoRepositories_DryRun
- Checks: MP-10, MP-14, MP-22, MP-H2 · Project: Host
- Setup: two temporary git repositories with bare remotes, one ready card each, a pool file, `-DryRun`.
- Expected: the DRY RUN lines pick one card per project in the order the shares give; no agent is started; main is unchanged.

## Web (MP-P*)

### MP_P1_ThemeList_PoolThemeFirst
- Checks: MP-P1 · Project: Web
- Setup: pool theme `dark`; `AmigaX`'s `page.theme` is a Workbench theme file.
- Expected: the theme menu lists `dark` first, then the loader's themes, including the Workbench theme.

### MP_P2_Dropdown_OnlyWithTwoProjects
- Checks: MP-P2 · Project: Web
- Setup: one active project; then two; then one of two is not in the pool (MP-H1).
- Expected: no dropdown with one project and the page looks as today; with two, one dropdown beside the theme menu in every view, items "All projects", `AmigaX`, `ProjectToolkit`.

### MP_P2_Selection_OnePerPage_Remembered
- Checks: MP-P2 · Project: Web
- Setup: pick `AmigaX`, switch Workers -> board -> Gantt, reload.
- Expected: every window and view is filtered to `AmigaX`; the selection is not reset by view switches; the page script stores and restores it; All shows everything. Pause on `AmigaX` posts `project=AmigaX`; on All posts none.

### MP_P3_WaitingRowsLabelled_ButtonsPostProject
- Checks: MP-P3 · Project: Web
- Setup: one review card in each project.
- Expected: one Waiting on you, each row labelled with its project, every button posting `project=` beside `card=`.

### MP_P4_ProjectsWindow_WeightAndShare
- Checks: MP-P4 · Project: Web
- Setup: `AmigaX` weight 70, 20 of 60 minutes used; `ProjectToolkit` red.
- Expected: per project: state, weight, share (33% used against 70%), running workers, queue length, health (red, last sha), planner line, buttons Pause / Drain / Resume / Check main posting that project.

### MP_P5_Statistics_PerProjectAndAllLine
- Checks: MP-P5 · Project: Web
- Setup: accounting rows in both projects for model `Sonnet`.
- Expected: each project's own stats text; an All projects line for `Sonnet` is the sum; trends respect the filter and All sums.

### MP_P6_WorkerWindowTitle_NamesProject
- Checks: MP-P6 · Project: Web
- Setup: a running worker in each project.
- Expected: titles "AmigaX - spark - slot 1 - <card>"; Stop, Kill and the message box post `project=` and `slot=`.

### MP_P7_WorkerTypesPanel_ProjectsColumn
- Checks: MP-P7 · Project: Web
- Setup: a type serving only `AmigaX`.
- Expected: a Projects column names it; no category lists; saving posts to `/worker-types` and writes the pool file.

### MP_P8_PerProjectRoute_WithoutProject_Refused
- Checks: MP-P8 · Project: Web
- Setup: in a pool, each per-project route (`/answer`, `/card`, `/done`, `/health`, `/health-now`, `/kill`, `/log`, `/open`, `/open-lanes`, `/open-workers`, `/planner-toggle`, `/repo-file`, `/request`, `/resume-card`, `/retry`, `/review`, `/send`, `/stop`, `/template-keep`, `/template-overwrite`) without `project=`; then with an unknown project.
- Expected: refused with "which project?" and no action taken; with a valid project, the action goes to that project only; `/accept-lane` answers not found.

### MP_P8_PoolWideRoutes_NoProjectNeeded
- Checks: MP-P8 · Project: Web
- Setup: `/`, `/tick`, `/stopall`, `/worker-types`, `/agent-resume`, `/font`, `/pause`, `/drain`, `/resume` without `project=`.
- Expected: all answered; the last three act on all projects. In a one-project (non-pool) page every route ignores `project=`.

### MP_P9_ProjectChip_OnlyUnderAllProjects
- Checks: MP-P9 · Project: Web
- Setup: rows in Queue, Recent events, Waiting on you, Requests, board, Gantt and the Projects window for both projects; dropdown on All, then `AmigaX`; then one active project.
- Expected: under All every such row carries a chip with the project's short name, a colour stable per project and not a category colour, full name and repository on hover; with one selected or one active project, no chip.
