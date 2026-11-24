# Scheduler roadmap

Ideas set aside for later, from the developer's review of 2026-11-17. Nothing here is committed to.

In the order the developer wants them (2026-11-17). The parse check before hand-over was dropped: the C# rewrite makes it unnecessary.

## Order of the remaining work (developer, 2026-11-19)
1. Item 9, worker types with caps (design on card 530-D, then its cards).
2. Multi-project support (item 1's multi-project section, spec B3 open questions; plan card 955-D), with
   AmigaX joining as the second project: its queue to `jobs/`, categories unchanged, workers into the shared
   worker types, one cut-over. The developer keeps AmigaX idle until this lands (2026-11-19). Includes what
   the current page needs for it (project dropdown, one Waiting on you, per-project stats).
3. Dynamic file locks for workers (proposed 2026-11-20, card 994-D; design in tdd/scheduler/dynamic-locks.md,
   rules DL-1 to DL-26, to be approved): workers ask the scheduler for files beyond their scope while they run,
   conflicts settled by wait-die, pushes checked by the scheduler's git hooks and an audit of main. After
   multi-project, which raises the pool's use sooner (idle workers take AmigaX's cards) with less change;
   locks are per repository from the start, so nothing is redone.
4. Item 5, the views of the page (Workers, Card board, Plans, Gantt): designed by card 1031-D in
   tdd/scheduler/views.md (rules VW-1 to VW-23, to be approved).
5. Git performance: one in-memory model of main per repository (developer, 2026-11-22), designed by card 1058-D in
   tdd/scheduler/state-cache.md, approved and built 2026-11-22 (cards 1060-A to 1067-B; rules MM-1 to MM-18 are in
   tdd/scheduler/spec.md A14, card 1069-D): main read in process
   at startup and refreshed only when it moves, every read of the tick, page, planner and audits from memory, one
   fetch per repository per poll, git kept only for fetch, push, worktrees and hooks. It replaces 1045-B and reshapes
   1044-B, 1046-B and 1047-B; the views (step 4) take done cards' headers from it. Baseline: docs/perf/git-baseline.md
   (1041-B).

Item 5 stays in the plan (the developer wants the alternate views); item 7 is dropped (830-D).

## 1. Rewrite in C# (.NET 10 LTS, supported to Nov 2028; .NET 8/9 reached end of support 2026-11-10)
Compiled and type-checked (this week's run-time-only bugs fail at build), real unit tests, native
job objects and process trees, a proper async web server for the page and live view, one .exe.
Changes could be built and tested before hand-over. About 4,000 lines of behaviour to carry over.
Plan: write the current rules down as test cases first, port, then switch.
Changed 2026-11-18: no day-long side-by-side run (too few AmigaX cards for a day's comparison; the
observe mode and -Compare from 390-B stay available). Instead, switch once the port's queue is
done and try it on a few small test cards, with the old scheduler kept ready to fall back to.
The move to jobs/ (below) is done first, as part of the swap (decided 2026-11-18); the template
refresh waits for a period of tuning and testing on the new scheduler.

### Multi-project (decided 2026-11-17, part of the rewrite)
*Status 2026-11-19: designed by card 955-D in tdd/scheduler/multi-project.md (rules MP-C*, MP-10 to MP-22, MP-H*, MP-P*, and the proposed cards); spec B3 Q1-Q14 are answered there; the developer settled its open points the same day (its Decisions section).*
One scheduler process for several projects; each project keeps its own repository and docs/jobs/
(cards, categories, README, scheduler.json), listed in a small top-level config.
- **Workers are one shared pool** and any worker can take any project's card (it mirrors the real
  resources: one model server, one PC). Model-server capacity and build threads are budgeted across
  all projects.
- **Weighted effort per project.** Each project has a weight (say AmigaX 70, Other 30). When a worker
  comes free, the project furthest below its share gets it: share = its worker-minutes over a
  sliding window (about the last two hours) against its weight. Only projects with a card that can
  run now take part, so an idle project's share goes to the others. Within the chosen project the
  card is picked as today (priority, orphans first, categories, dependencies). Open: whether a high
  priority card may jump the weights.
- **One combined page**: one look, one project dropdown in the top bar (shown only with two or more projects), one Waiting on you across projects,
  stats per project and per model. Planner outbox, health check and gates stay per project.
- **The toolkit is the single source of truth (decided 2026-11-18).** Each project's generic files
  (the jobs README, the card, plan and request templates, docs/ai/README.md) are copies of
  ProjectToolkit's `templates/`, never edited in the project. The scheduler compares every project's
  copies with `templates/` at start and whenever the toolkit's main moves, and refreshes any that
  differ (a commit in that project, as `init-project.ps1 -Update` does today), so an edit to
  `templates/` reaches every project and every worker without running `-Update` by hand. A copy that
  was edited in a project is not overwritten silently: it is shown on the page with Overwrite and Keep.
  Project-specific files (project.md, categories.json, workers.json, scheduler.json) are never touched.

### Per-card scopes instead of categories (decided 2026-11-17, part of the rewrite)
Categories cap parallelism at the number of categories with ready work, cost the planner a partition of every
codebase, and repeat that cost per project. Instead:
- **Each card names its scope**: the folders, files or patterns it expects to touch (an extension
  of today's `Locks:` line).
- **Dispatch checks overlap**: a free worker takes any ready card whose scope does not overlap a
  running card's, in any project. Cards on different files run side by side; cards on the same
  file run one after another.
- **Categories** (today's categories) remain only as named default scopes and as the worker's brief of what
  it owns; a card without its own scope takes its category's.
- **Overlap by choice**: a card may allow overlap on a path (add-only files such as known_gaps.md);
  the rule there is pull, rebase, resolve, re-run the gates.
- **Measured**: every rejected push and rebase conflict is logged, to show whether scopes can be
  loosened.
Scopes are checked by the scheduler, so they are easy to manage automatically.
- **Categories as labels** (developer, 2026-11-18): once dispatch goes by scope, a category is only a category:
  a card's category and short name, its default scope, and the worker's brief. Its value is as a
  label: colour coding boxes in the Gantt view, filtering the task lists. Card 560-A switches dispatch.

### Move the queue from docs/jobs/ to jobs/ (decided 2026-11-18, done at the swap, before the new scheduler starts)
`docs/` is for material written for users; the queue is not. Done when the old scheduler is
retired, in the same coordinated step as moving each project to scheduler-next, not before.
- `templates/jobs/README.md` (including the standard prompt), `docs/workflow.md`,
  `init-project.ps1`, and the scheduler's default `jobsDir`: `docs/jobs` becomes `jobs`.
- Each project, with its queue drained: `git mv docs/jobs jobs`, `jobsDir` in scheduler.json,
  then `init-project.ps1 -Update` (or the automatic refresh above).
- The planner outbox path in scheduler.json, and any card, plan or brief that names `docs/jobs/`.

## 2. The scheduler as the only writer of card moves
*Status 2026-11-19: built by card 660-B; rules CM-1 to CM-22 in tdd/scheduler/card-moves.md.*
If claim races or card conflicts come back: workers ask for a move or status change with a small
request file, and the scheduler commits them in order. Removes the races; the cards stay in git.
(A database instead of git was considered and set aside: card and code stop being one commit, the
workers' brief and the planner flow change, and the cards leave the repository.)

## 3. Trends
*Status 2026-11-19: built by card 630-A; rules TREND-1 to TREND-3 in tdd/scheduler/spec.md (A13).*
The Control window shows the last 24 hours only. A small daily chart from accounting.csv: cards
done per model per day, so a change (a model, a prompt, a category split) shows whether it helped.

## 4. Time limits from history
*Status 2026-11-19: built by card 640-A; rules TLH-1 to TLH-5 in tdd/scheduler/spec.md (A13).*
25 recent stop reasons were "over the 120 min limit". Per category and model, the scheduler knows how
long finished cards really take (accounting.csv). Suggest a card's time box when it is queued, or
warn when one is set shorter than its category usually needs.

## 5. Views of the page
*Status 2026-11-21: designed by card 1031-D in tdd/scheduler/views.md (rules VW-1 to VW-23, cards in its section 8), to be approved; a fourth view, Plans, added by the developer (2026-11-21).*
Compared against agent dashboards in the wild (Vibe Kanban, Emdash, Cline Kanban, GitHub Mission
Control) and job schedulers (Airflow's grid and Gantt views). A view switch on the page:
1. **Workers** - the current page.
2. **Card board** - columns queue / running / review / done (plus waiting on you, waiting on the
   planner), each card showing its category, worker and state. Shows where every card is at a glance.
3. **Gantt** - one row per card, by category:
   - what ran and when (each run a bar, coloured by its result, with its model);
   - where we are now (a "now" line; running cards growing toward their time box);
   - the dependencies (arrows from Depends on:), so chains and category bottlenecks show;
   - an estimated finish for waiting and queued cards: category free time plus the card's expected
     length (its time box, or the category's typical card length from accounting.csv - see Time limits
     from history), carried down each dependency chain.

## 6. Review panel
*Status 2026-11-19: built by card 650-C; rules REV-1 to REV-5 in tdd/scheduler/spec.md (A13); the Send back note is not stored (B3 Q15, open).*
About half of finished cards go to review, so the developer's review time is the bottleneck. A panel
per review card: its commits and changed files, the gate result, the worker's summary and Result,
screenshots it made, with Done and Send back beside them.

## 7. UI redesign
*Dropped 2026-11-19 by the developer: the page is tweaked as he goes.*

## 8. Tray app (noted 2026-11-18, to plan)
*Status 2026-11-19: built by cards 550-B and 670-B; rules TRAY-1 to TRAY-8 in tdd/scheduler/spec.md (A13); Restart scheduler is still being fixed (TRAY-8).*
A Windows tray wrapper for the scheduler, so it runs without a console window:
- an icon in the notification category while the scheduler runs;
- double-clicking the icon opens the scheduler's web page;
- its context menu has **Exit** (stops the scheduler the way the page's Stop all and exit does) and a
  way to **view the log output** (the console's lines, in context).

## 9. Worker types with caps instead of fixed workers (proposed 2026-11-18, to plan)
Instead of a list of fixed workers (slots), the pool sets a total and a maximum per worker type, and
starts the type a card needs while both allow it. Example: at most 4 workers in all; at most 4 spark,
3 Sonnet, 2 Opus.

```json
"pool": {
  "maxWorkers": 4,
  "types": {
    "spark":  { "max": 4, "agent": "default", "model": "spark", "endpoint": "http://spark.local:18400/v1" },
    "sonnet": { "max": 3, "agent": "claude",  "model": "claude-sonnet-5-5" },
    "opus":   { "max": 2, "agent": "claude",  "model": "claude-opus-5-5" }
  },
  "defaultWorker": "spark"
}
```

- Dispatch: while fewer than `maxWorkers` run, the next card that fits starts a worker of the type it
  needs, if that type is under its `max`; otherwise it waits and the next card that fits goes.
- **Decided:** the existing rules for premium workers and worker selection still apply (developer,
  2026-11-18): a card without a `Worker:` line goes to a non-premium type, a premium type takes only
  cards that ask for it, and `Worker: <name>` picks that type. *Superseded 2026-11-23 (developer, cards 1146-A, 1148-D):
  there is no premium flag; a card without a `Worker:` line goes to the pool's `defaultWorker` type, any other type
  must be named, and `Worker: premium` is not special.*
- Worktrees become a shared set of `maxWorkers` folders that any type uses.
- **Decided** (developer, 2026-11-18): per-worker **Disable** and **Settings** go. Worker windows
  become one per running worker, with Stop and Kill only. A **Worker types** panel replaces them:
  each type's cap (0 = off, which is what Disable was) and its settings (agent, model, endpoint).
  Until this lands the existing buttons keep working; nothing more is built on them (510-B item 2
  was skipped for this).
- Open: This replaces 300-A Q4 (one worker list, each naming its projects):
  each type would name the projects it serves, and the caps apply across all projects. Whether any
  type needs a per-category or per-scope limit (probably not: scopes keep cards apart).
