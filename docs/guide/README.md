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
3. **Switchyard, the scheduler.** A program (`scheduler-next.exe`) that watches the queue, starts one AI worker per card in
   its own git worktree, checks the work, and serves a web page where you watch and steer it. A small tray program
   (`scheduler-tray.exe`) keeps it running from the Windows notification area. Switchyard is the name of all three:
   the scheduler, its tray and its page.

You can use the layout and queue without running the scheduler (workers can be started by hand), but the
scheduler is what makes the queue run unattended.

## Where to start

0. New to the toolkit? Start with the walkthrough: [getting-started.md](getting-started.md).
1. Set up a project, or add the queue to an existing one: [projects.md](projects.md).
2. Write the design, then have a planner write the project's plan from it: [plans.md](plans.md) and
   [planner.md](planner.md). The plan is the centre of the work: the page's Plans view is where you see and steer
   the project, and its Workers view is where the action happens.
3. Set up the workers and start Switchyard, then card the plan's first step: [switchyard.md](switchyard.md).
4. Look things up: the table below, [scripts.md](scripts.md) for `bin/` and
   [scheduler-cli.md](scheduler-cli.md) for the executable's commands.

## Every tool and file

The "Page" column is the guide page that covers it.

### The guide pages

| Page | What it is for | Read it |
|---|---|---|
| New to the toolkit | The walkthrough, from nothing to a first finished card. | [getting-started.md](getting-started.md) |
| Working with a planner LLM | Set up and direct the planner that writes plans and cards | [planner.md](planner.md) |
| Plans | What a plan is, how to write one, and the checklist before carding it | [plans.md](plans.md) |
| Switchyard | The scheduler, its tray and its page: starting it, the views, the worker types, the settings. | [switchyard.md](switchyard.md) |
| Projects and the job queue | Set up a project or add the queue to one you have; what is in jobs/; a card's life. | [projects.md](projects.md) |
| The scripts in bin/ | Every script, its parameters and examples. | [scripts.md](scripts.md) |
| The command line | scheduler-next.exe: run one project or a pool; the worker's commands. | [scheduler-cli.md](scheduler-cli.md) |

### Scripts in `bin/`

| Tool | What it is for | Page |
|---|---|---|
| `bin/projecttoolkit.ps1` | Sets up a project: `-Action new-project` lays down the whole layout and queue; `-Action jobs` writes or refreshes just the queue (`-Update` pulls kit improvements into a project). | [projects.md](projects.md) |
| `bin/init-project.ps1` | Old name for `projecttoolkit.ps1`; only forwards to it. Kept for older instructions. | [scripts.md](scripts.md) |
| `bin/build-and-deploy.ps1` | Brings the kit's main branch up to date, then deploys the scheduler as a pool and makes sure the tray is running. | [switchyard.md](switchyard.md) |
| `bin/deploy-scheduler.ps1` | Publishes a new scheduler beside the running one, swaps it in, and rolls back if the new one does not start. In a tray run from a clone, the "Update and restart" item does the same; an installed tray's item is "Check for update..." instead. | [switchyard.md](switchyard.md) |
| `bin/test-projecttoolkit.ps1` | A self-test of `projecttoolkit.ps1` in a temporary folder. Exit code 0 means every check passed. | [scripts.md](scripts.md) |
| `bin/startup-gate.ps1` | A check that the scheduler's start-up time has not got clearly slower; `-Record` saves a new baseline. | [scripts.md](scripts.md) |
| `bin/test-profile.ps1` | Times the test suites and lists the slowest tests. | [scripts.md](scripts.md) |
| `bin/git-baseline.ps1` | Measures how many git processes the tests and the scheduler start; writes `docs/perf/git-baseline.md`. | [scripts.md](scripts.md) |
| `bin/process-start-check.ps1` | Measures how fast this PC starts processes (starts per second). | [scripts.md](scripts.md) |
| `bin/live-stream-measure.ps1` | Measures what the page's live stream costs a running scheduler. | [scripts.md](scripts.md) |
| `bin/process-start-dose.ps1` | Gives the PC measured doses of work and reads its process-start rate after each, to find what lowers it. | [scripts.md](scripts.md) |
| `bin/paged-pool-probe.ps1` | Finds which operation of a test run leaves kernel paged pool behind. | [scripts.md](scripts.md) |
| `bin/pool-tags.ps1` | Reads the kernel pool by tag. | [scripts.md](scripts.md) |
| `bin/publish-release.ps1` | Developer-only: publishes a release to GitHub (release branch, tag, and the installer ProjectToolkit-Setup.exe). | [scripts.md](scripts.md) |
| `bin/template-history.ps1` | Developer-only: writes template-history.json, every blob id each template has had. | none |
| `bin/test-installer.ps1` | Developer-only: installs a test build of the installer silently into a scratch folder, checks it and uninstalls it. | none |

### The scheduler program (`code/src/`, published to `out/windows/scheduler/`)

| Tool | What it is for | Page |
|---|---|---|
| `scheduler-next.exe <repository>` | Runs one project's queue. | [scheduler-cli.md](scheduler-cli.md) |
| `scheduler-next.exe -Pool [<pool file>]` | Runs every project of the pool file in one process (the normal way). | [scheduler-cli.md](scheduler-cli.md) |
| `scheduler-next.exe lock <path> --why "<reason>"` | A worker's request for a file outside its card's scope. | [scheduler-cli.md](scheduler-cli.md) |
| `scheduler-next.exe check-push` | The pre-push check that a push to main touches only files the card holds. | [scheduler-cli.md](scheduler-cli.md) |
| `scheduler-next.exe --deliver-message` | The hook that hands a worker the developer's message. Started by agents, not by you. | [scheduler-cli.md](scheduler-cli.md) |
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
| `jobs/plan-marks.json` | The marks the developer sets on plans and steps in the Plans view. Written by the page, not by hand. | [switchyard.md](switchyard.md) |
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
| `readme.md`, `conventions.md`, `gitignore`, `gitattributes`, `design/gdd.md`, `design/tdd.md` | The root README, coding conventions, git settings and design-document skeletons of a new project. | [projects.md](projects.md) |
| `docs/planner-brief.md` | The planner's standing brief; new-project copies it to the project's docs/planner-brief.md. | [planner.md](planner.md) |

### Other documents

| Document | What it is |
|---|---|
| [workflow.md](../workflow.md) | The workflow and the canonical layout; section 11 is how to use the kit. |
| [agent-lessons.md](../agent-lessons.md) | Lessons from running workers on a real project, as rules for cards, briefs and settings. |
| [known_gaps.md](../known_gaps.md) | Known gaps and defects. |
| [`docs/design/`](../design/) | The design documents (`gdd` for what a thing is, `tdd` for how it is built), including the scheduler's spec. |
| [`docs/perf/`](../perf/) | Measurements written by the `bin/` measuring scripts. |
