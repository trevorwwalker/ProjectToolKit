# ProjectToolkit guide

This guide explains how to use ProjectToolkit. It is for someone who has just been given the kit. The design
documents under [`docs/design/`](../design/) stay the source of truth for how things are built; this guide says
how to use them, and links to them instead of repeating them.

## What the toolkit is

ProjectToolkit is a kit for building software with AI agents that share a job queue. It is three parts:

1. **The project layout.** A standard folder structure for a project (`code/`, `test/`, `docs/`, `assets/`,
   `jobs/`), with templates for the files every project needs. Described in [workflow.md](../workflow.md).
2. **The job queue.** A `jobs/` folder in your repository. Each piece of work is a *card* (a Markdown file) that
   moves through `queue/` -> `active/` -> `done/` (or `review/` when a person must check it). Plans, requests
   to the planner and the rules every worker follows (`jobs/README.md`) live there too.
3. **The scheduler.** A program (`scheduler-next.exe`) that watches the queue, starts one AI worker per card in
   its own git worktree, checks the work, and serves a web page where you watch and steer it. A small tray
   program (`scheduler-tray.exe`, called Switchyard in this guide) keeps it running from the Windows
   notification area.

You can use the layout and queue without running the scheduler (workers can be started by hand), but the
scheduler is what makes the queue run unattended.

## Where to start

0. New to the toolkit? Start with the walkthrough: [getting-started.md](getting-started.md). Working with a
   planner LLM: [planner.md](planner.md).
1. Build and start the scheduler: [switchyard.md](switchyard.md).
2. Set up a project, or add the queue to an existing one: [projects.md](projects.md).
3. Look things up: the table below, [scripts.md](scripts.md) for `bin/` and
   [scheduler-cli.md](scheduler-cli.md) for the executable's commands.

The pages named below (switchyard, planner and the rest) are being written one at a time; a link
works once its page is on main.

## Every tool and file

The "Page" column is the guide page that covers it.

### The guide pages

| Page | What it is for | Read it |
|---|---|---|
| New to the toolkit | The walkthrough, from nothing to a first finished card. | [getting-started.md](getting-started.md) |
| Working with a planner LLM | Set up and direct the planner that writes plans and cards | [planner.md](planner.md) |

### Scripts in `bin/`

| Tool | What it is for | Page |
|---|---|---|
| `bin/projecttoolkit.ps1` | Sets up a project: `-Action new-project` lays down the whole layout and queue; `-Action jobs` writes or refreshes just the queue (`-Update` pulls kit improvements into a project). | [projects.md](projects.md) |
| `bin/init-project.ps1` | Old name for `projecttoolkit.ps1`; only forwards to it. Kept for older instructions. | [scripts.md](scripts.md) |
| `bin/build-and-deploy.ps1` | Brings the kit's main branch up to date, then deploys the scheduler as a pool and makes sure the tray is running. | [switchyard.md](switchyard.md) |
| `bin/deploy-scheduler.ps1` | Publishes a new scheduler beside the running one, swaps it in, and rolls back if the new one does not start. The tray's "Update and restart" item does the same. | [switchyard.md](switchyard.md) |
| `bin/test-projecttoolkit.ps1` | A self-test of `projecttoolkit.ps1` in a temporary folder. Exit code 0 means every check passed. | [scripts.md](scripts.md) |
| `bin/startup-gate.ps1` | A check that the scheduler's start-up time has not got clearly slower; `-Record` saves a new baseline. | [scripts.md](scripts.md) |
| `bin/test-profile.ps1` | Times the test suites and lists the slowest tests. | [scripts.md](scripts.md) |
| `bin/git-baseline.ps1` | Measures how many git processes the tests and the scheduler start; writes `docs/perf/git-baseline.md`. | [scripts.md](scripts.md) |
| `bin/process-start-check.ps1` | Measures how fast this PC starts processes (starts per second). | [scripts.md](scripts.md) |
| `bin/live-stream-measure.ps1` | Measures what the page's live stream costs a running scheduler. | [scripts.md](scripts.md) |

### The scheduler program (`code/src/`, published to `out/windows/scheduler/`)

| Tool | What it is for | Page |
|---|---|---|
| `scheduler-next.exe <repository>` | Runs one project's queue. | [scheduler-cli.md](scheduler-cli.md) |
| `scheduler-next.exe -Pool [<pool file>]` | Runs every project of the pool file in one process (the normal way). | [scheduler-cli.md](scheduler-cli.md) |
| `scheduler-next.exe lock <path> --why "<reason>"` | A worker's request for a file outside its card's scope. | [scheduler-cli.md](scheduler-cli.md) |
| `scheduler-next.exe check-push` | The pre-push check that a push to main touches only files the card holds. | [scheduler-cli.md](scheduler-cli.md) |
| `scheduler-next.exe --deliver-message` | The hook that hands a worker the developer's message. Started by agents, not by you. | [scheduler-cli.md](scheduler-cli.md) |
| `scheduler-next.exe -Compare <old log> <new log>` | Compares two scheduler logs. | [scheduler-cli.md](scheduler-cli.md) |
| `scheduler-next.exe --version` | Prints the version. | [scheduler-cli.md](scheduler-cli.md) |
| `scheduler-tray.exe` | The tray icon: starts the scheduler without a console window, opens the page, shows the log, updates and restarts. | [switchyard.md](switchyard.md) |
| The scheduler's page (`http://localhost:<port>/`) | Workers, plans, the card views and Gantt chart, questions and reviews, worker types, health check. | [switchyard.md](switchyard.md) |

The scheduler is C# on .NET 10: `Scheduler.Core` (logic), `Scheduler.Host` (processes, git, the executable),
`Scheduler.Web` (the page) and `Scheduler.Tray` (the tray), in `code/src/SchedulerNext.slnx`. Its tests are in
`test/unit/`; its specification is in [`docs/design/tdd/scheduler/`](../design/tdd/scheduler/).

### Configuration files

| File | What it is for | Page |
|---|---|---|
| `%LOCALAPPDATA%\ProjectToolkit\pool.json` | The pool file, on your PC and in no repository: the projects the scheduler runs, the page's port, the worker types and the log folder. | [switchyard.md](switchyard.md) |
| `jobs/scheduler.json` | One project's scheduler settings: slots, port, workspace folder, worker command, health check, time limits. | [projects.md](projects.md) |
| `jobs/categories.json` | The project's categories: what each is for and which files it owns, so two workers never edit the same file. | [projects.md](projects.md) |
| `jobs/models.json` | The models a project can name on a card. | [projects.md](projects.md) |
| `jobs/project.md` | The project's own gates (build and test commands), locations and standing briefs. Workers read it after `jobs/README.md`. | [projects.md](projects.md) |
| `jobs/README.md` | The protocol every worker follows. The same in every project; refreshed by `-Update`, never edited in a project. | [projects.md](projects.md) |
| `assets/themes/*.json` | The page's colour themes (`theme.default.json` and the named ones). A project can name its own with `page.theme`. | [switchyard.md](switchyard.md) |

### Templates (`assets/templates/`)

| Template | What it is for | Page |
|---|---|---|
| `jobs/README.md`, `jobs/project.md` | The worker protocol and the project skeleton. | [projects.md](projects.md) |
| `jobs/card-template.md`, `card-template-feature.md`, `card-template-bug.md` | New cards. | [projects.md](projects.md) |
| `jobs/plan-template.md` | New plans. | [projects.md](projects.md) |
| `jobs/categories.json`, `models.json`, `scheduler.json` | Starting versions of the configuration files above. | [projects.md](projects.md) |
| `jobs/requests/README.md`, `jobs/attachments/README.md` | The planner-request and attachment folders. | [projects.md](projects.md) |
| `ai/README.md` | The README of `docs/ai/`, where new plans are written. | [projects.md](projects.md) |
| `readme.md`, `conventions.md`, `gitignore`, `gitattributes`, `design/gdd.md`, `design/tdd.md` | The root README, coding conventions, git settings and design-document skeletons of a new project. | [projects.md](projects.md) |

### Other documents

| Document | What it is |
|---|---|
| [workflow.md](../workflow.md) | The workflow and the canonical layout; section 11 is how to use the kit. |
| [agent-lessons.md](../agent-lessons.md) | Lessons from running workers on a real project, as rules for cards, briefs and settings. |
| [known_gaps.md](../known_gaps.md) | Known gaps and defects. |
| [`docs/design/`](../design/) | The design documents (`gdd` for what a thing is, `tdd` for how it is built), including the scheduler's spec. |
| [`docs/perf/`](../perf/) | Measurements written by the `bin/` measuring scripts. |
