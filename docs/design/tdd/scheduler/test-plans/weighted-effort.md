# Test plan: multi-project pool and weighted effort

Rules: MP-1 to MP-9 (decided design, spec B1). Open questions Q1-Q4, Q11-Q14 (spec B3) decide
the exact results of several tests; those tests are marked **blocked on Qn** and give the parts
that are already decided. All selection logic is Core with an injected clock.

Shared setup: projects P (weight 70) and O (weight 30); a pool of 2 workers that can run any card.

### Pool_TopLevelConfig_ProjectsKeepOwnRepoAndJobs
- Checks: MP-1 · Project: Host · **blocked on Q12** (which settings are top-level)
- Setup: top-level config listing P and O, each with its own repository and `docs/jobs/`.
- Expected: cards, categories/categories, README and scheduler.json are read per project from that project's main.

### Pool_AnyWorkerTakesAnyProjectsCard
- Checks: MP-2 · Project: Core · **blocked on Q4** (where pool workers are defined)
- Setup: one free worker; only O has a ready card.
- Expected: the worker takes O's card.

### Pool_ThreadsBudget_CoresOverPoolSize
- Checks: MP-3, WRK-7 · Project: Core
- Setup: 16 cores; pool of 4 workers across two projects (P has 3 slots' worth of cards, O 1).
- Expected: 4 threads per worker, whatever the project.

### Pool_ProjectWeights_Parsed
- Checks: MP-4 · Project: Core
- Expected: weights 70 and 30 read from the top-level config; a missing weight -> to be decided (**blocked on Q2**).

### Pool_OnlyProjectsWithRunnableCardTakePart
- Checks: MP-5 · Project: Core
- Setup: P far below its share but its only ready card asks for a worker the free one is not (or depends on an unfinished card); O has a runnable card.
- Expected: O gets the worker.

### Pool_NoProjectHasRunnableCard_WorkerStaysIdle
- Checks: MP-5 · Project: Core
- Setup: both projects' queues empty or blocked.
- Expected: no start; no error.

### Pool_FurthestBelowShare_GetsWorker
- Checks: MP-6 · Project: Core · **blocked on Q2** (window length, difference vs ratio, tie-break, whether running minutes count)
- Setup: last 2 h: P used 60 worker-minutes, O used 60; both have runnable cards.
- Expected: P gets the free worker (70 % weight, 50 % use). Exact numbers per Q2's arithmetic.

### Pool_IdleProjectShare_GoesToOthers
- Checks: MP-6 · Project: Core
- Setup: O has no cards for the whole window; P has used 100 % of the minutes.
- Expected: P keeps getting every free worker.

### Pool_ProjectWithNoMinutes_WinsOutright
- Checks: MP-6 · Project: Core · **blocked on Q2**
- Setup: O gets its first card after 2 h of P-only work.
- Expected (if Q2 says so): O gets the next free worker.

### Pool_WindowSlides
- Checks: MP-6 · Project: Core · **blocked on Q2**
- Setup: P's minutes all older than the window length.
- Expected: P's share counts as 0.

### Pool_HighPriorityCard_VsWeights
- Checks: MP-6, MP-7, DISP-14 · Project: Core · **blocked on Q1**
- Setup: O over its share with a `Priority: high` card; P under its share with a normal card.
- Expected: to be written once Q1 decides whether high priority jumps the weights.

### Pool_OrphanResume_VsWeights
- Checks: MP-7, DISP-2 · Project: Core · **blocked on Q3**
- Setup: O over its share with an orphan in active/; P under with a queued card.
- Expected: to be written once Q3 decides.

### Pool_WithinProject_SelectionAsToday
- Checks: MP-7 · Project: Core
- Setup: P chosen; P has the cards from `Dispatch_QueuedOrder_PrioNumLane` and an orphan.
- Expected: the same card as the single-project dispatch would pick (re-run the A3 dispatch tests through the pool with one project; they must all pass unchanged, except where scopes replace categories per scopes.md).

### Pool_CombinedPage_FilterAndOneWaitingOnYou
- Checks: MP-8 · Project: Web · **blocked on Q12** (theme, listen, port)
- Setup: P and O each with a question card, a review card, runs on two models.
- Expected: one Waiting on you list with both projects' items, each labelled; the project dropdown (shown only with two or more projects; one selection for the whole page, kept when switching views and remembered in the browser) hides the other project's rows; stats per project and per model.

### Pool_PlannerHealthGates_PerProject
- Checks: MP-9 · Project: Core
- Setup: P has a planner outbox and health command; O has neither.
- Expected: P's outbox and health are run against P's repository only; O shows no planner line and no health state.

### Pool_RedMainInOneProject
- Checks: MP-9, HLTH-5 · Project: Core · **blocked on Q11**
- Setup: P's health goes red; O's is green.
- Expected (decided part): P's state is red. Whether O's cards still dispatch: per Q11.

### Pool_QuotaPause_PerAgent
- Checks: TIME-15 · Project: Core · **blocked on Q11**
- Setup: a run of P on agent `claude` hits quota; O has a card for a `copilot` worker and another for a `claude` worker.
- Expected: to be written once Q11 decides.

### Pool_WorkerRequest_Kept
- Checks: DISP-18 · Project: Core · **blocked on Q13**
- Expected: the `Worker-Fits` cases from dispatch.md hold for pool workers if Q13 keeps the rule.

### Pool_MaxRetriesAndLimits_Source
- Checks: DISP-7, TIME-1 · Project: Core · **blocked on Q14**
- Setup: P maxRetries 2 and maxMinutes 240; O maxRetries 5 and maxMinutes 60.
- Expected: per project, or the global value, per Q14.
