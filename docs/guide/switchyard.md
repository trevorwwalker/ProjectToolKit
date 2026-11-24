# Switchyard: the scheduler, the tray and the page

Switchyard is the part of ProjectToolkit that runs your job queue for you. You (with a planner LLM) write plans
and turn them into job cards; Switchyard picks up each ready card, starts an AI worker on it in its own copy of the
repository, watches it, and moves the card along when the worker is finished. You watch and steer it all from one web
page.

It has three pieces:

| Piece | File | What it does |
|---|---|---|
| The scheduler | `scheduler-next.exe` | Reads the cards from `main`, starts workers, checks that `main` still builds, moves cards, serves the page. |
| The tray | `scheduler-tray.exe` | The Switchyard icon by the clock. Starts the scheduler, keeps it running, restarts it after a deploy. |
| The page | `http://localhost:8766/` | Your control panel: workers, questions waiting on you, the queue, plans, health of `main`. |

Back to the [guide index](README.md). For writing plans and cards with an LLM, see the
[planner guide](planner.md). The design documents in [`docs/design/tdd/scheduler/`](../design/tdd/scheduler/) are the
source of truth for exact behaviour; this page explains how to use it. Every command-line switch of
`scheduler-next.exe` is in the [command-line reference](scheduler-cli.md).

## Contents

1. [What you need](#1-what-you-need)
2. [Building and deploying](#2-building-and-deploying)
3. [Starting and stopping: the tray](#3-starting-and-stopping-the-tray)
4. [The pool file](#4-the-pool-file)
5. [Adding and removing a project](#5-adding-and-removing-a-project)
6. [The page](#6-the-page)
7. [Worker types and models](#7-worker-types-and-models)
8. [Health checks, and what a red main does](#8-health-checks-and-what-a-red-main-does)
9. [Locks](#9-locks)
10. [The planner outbox](#10-the-planner-outbox)
11. [Logs and state files](#11-logs-and-state-files)
12. [Troubleshooting](#12-troubleshooting)

The examples follow a small made-up project, `MyProject` in `C:\Projects\MyProject`, with the toolkit itself in
`C:\Projects\ProjectToolkit`.

## 1. What you need

| Need | Why |
|---|---|
| Windows 10 or 11 | The tray is a Windows app, and workers are started and stopped through `cmd.exe` and `taskkill`. |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) (the SDK, not just the runtime) | Deploying *builds* the scheduler with `dotnet publish`. |
| Git for Windows | The scheduler reads cards from `main` with git and gives every worker its own git worktree. |
| An agent CLI | The program a worker runs. The default worker types use Claude Code (`claude`, signed in); a type can instead run another CLI against a local model server (see [section 7](#7-worker-types-and-models)). |
| Windows PowerShell 5.1 or PowerShell 7 | For the scripts in `bin\`. |

Check them from a PowerShell prompt:

```powershell
dotnet --list-sdks      # a line starting 10.
git --version           # git version 2.x.windows.n
claude --version        # only if you use the Claude worker types
```

## 2. Building and deploying

Switchyard runs from `C:\Projects\ProjectToolkit\out\windows\scheduler\`. The first time, build it there by hand:

```powershell
cd C:\Projects\ProjectToolkit
dotnet publish code\src\Scheduler.Host -c Release -o out\windows\scheduler
```

After that, update it with one script. It brings your copy of `main` up to date, builds the new version beside the
running one, swaps it in and makes sure the tray is running:

```powershell
cd C:\Projects\ProjectToolkit
.\bin\build-and-deploy.ps1            # pull main, build, swap, make sure the tray is up
.\bin\build-and-deploy.ps1 -NoPull    # deploy what is checked out now, without fetching
```

It needs the pool file ([section 4](#4-the-pool-file)) to exist already; if it does not, it stops with
`No pool file at ...`. Create it by adding your first project ([section 5](#5-adding-and-removing-a-project)).
`-PoolFile <path>` points it at a pool file somewhere else.

What a deploy does, step by step, and why it is safe to run while workers are busy:

1. `git fetch origin` and `git merge --ff-only origin/main` (skipped with `-NoPull`). If your `main` has local
   commits it cannot fast-forward, and the script stops rather than merge.
2. It builds the new version into `out\windows\scheduler.next` while the old one keeps running.
3. It ends the running `scheduler-next.exe` only. **Workers keep running**; the new scheduler adopts them.
4. It moves the old build to `out\windows\scheduler.prev` and the new one into `out\windows\scheduler`.
5. It starts the new scheduler through the tray. If the new one does not stay up for 30 seconds, it puts the old build
   back, says `did not stay up for 30 s; putting the old build back`, and exits with 1.
6. On success it prints `scheduler: <new version> replaced <old version> in ...`.

The tray's **Update and restart** does the same from its menu. `bin\deploy-scheduler.ps1 -Pool` is the step without
the pull and without starting a tray (see the [scripts guide](scripts.md) for its other switches).

## 3. Starting and stopping: the tray

Start Switchyard with the tray, for every project in the pool:

```powershell
C:\Projects\ProjectToolkit\out\windows\scheduler\scheduler-tray.exe -Pool
```

The tray copies itself to `%LOCALAPPDATA%\ProjectToolkit\tray` and runs from there (so a deploy can replace the
files in `out\`), starts `scheduler-next.exe`, and opens the page once it answers. If a scheduler for the same pool is
already running, the tray attaches to it instead of starting a second one. Add `-Quiet` to start without opening the
page.

To have it start when you log in, nothing is set up for you: make a shortcut in your Startup folder
(`Win+R`, `shell:startup`) whose target is
`C:\Projects\ProjectToolkit\out\windows\scheduler\scheduler-tray.exe -Pool -Quiet`.

Hover over the icon for a one-line status, for example
`pool scheduler: MyProject running, OtherProject paused-red; 2 workers`. Right-click it for the menu:

| Menu item | What it does |
|---|---|
| **Open page** (or double-click the icon) | Opens the page in your browser. |
| **View log** | A window with the scheduler's log as it is written. Look here first when something is wrong. |
| **Restart Switchyard** | Stops the scheduler (Ctrl+C, killed after 20 s) and starts it again. Workers keep running and are adopted. |
| **Update and restart** | The same as `build-and-deploy.ps1` without the pull: build `main`, swap it in, roll back if it does not start. |
| **Exit, leave workers running** | Ends the scheduler and the tray. Running workers carry on; the next scheduler you start adopts them. |
| **Exit (stop all)** | Asks every worker to stop cleanly (the page's **Stop all and exit**), then closes the scheduler and the tray. |

If the scheduler crashes, a balloon says so; click it (or **Restart Switchyard**) to start it again.

You can also stop from the page: **Drain and exit** lets running cards finish and starts nothing new, then exits;
**Stop all and exit** asks every worker to stop at its next safe point and exits.

The page is at `http://localhost:<port>/`, where the port is the `port` in the pool file. A pool file made by the
toolkit says `8766`, so the examples here use `http://localhost:8766/`; with no `port` at all it is 8765.

## 4. The pool file

One Switchyard serves every project on the PC. The list of them, and the worker types they share, is the **pool file**:
`%LOCALAPPDATA%\ProjectToolkit\pool.json` (open it with `notepad $env:LOCALAPPDATA\ProjectToolkit\pool.json`).
Adding your first project creates it ([section 5](#5-adding-and-removing-a-project)); `-Pool <file>` on the tray or
the scheduler uses another one.

An example with two projects:

```json
{
  "about": "The scheduler's pool: the projects it serves and the worker types it runs.",
  "projects": [
    { "name": "MyProject",    "repo": "C:\\Projects\\MyProject",    "weight": 70, "workspaces": 4 },
    { "name": "OtherProject", "repo": "C:\\Projects\\OtherProject", "weight": 30 }
  ],
  "port": 8766,
  "listen": "localhost",
  "remoteControl": false,
  "pool": {
    "maxWorkers": 4,
    "types": {
      "Local":  { "max": 2, "agent": "", "model": "", "endpoint": "http://localhost:18400/v1" },
      "Sonnet": { "max": 4, "agent": "claude", "model": "claude-sonnet-5-5" },
      "Opus":   { "max": 2, "agent": "claude", "model": "claude-opus-5-5" }
    },
    "defaultWorker": "Local"
  }
}
```

| Key | Means | If left out |
|---|---|---|
| `projects[].name` | The project's name. Must be the same as `project` in that repository's `jobs/scheduler.json`, or the project is skipped (the log says why). | required |
| `projects[].repo` | The repository folder. | required |
| `projects[].weight` | Its share of the workers when projects compete: 70 and 30 above give MyProject about 70%. A project with no work gives its share to the others. | the smallest weight of the others (all 1 if none has one) |
| `projects[].workspaces` | The most workers this project runs at once (1 to 64). Each needs its own worktree, so this is also how many `slotN` folders it may use. Also set from the page. | `maxWorkers` |
| `projects[].jobsDir`, `remote`, `branch` | Where the cards are. | `jobs`, `origin`, `main` |
| `port` | The page's port. | 8765 |
| `listen` | `localhost` (this PC only) or `lan` (also other PCs on your network, such as your phone). | `localhost` |
| `remoteControl` | `true` lets other PCs press the buttons, not just look. Only turn it on with `lan` on a network you trust. | `false` |
| `pool.maxWorkers` | The most workers running at once, over all projects. Size it to your PC: each worker builds and tests. | |
| `pool.types` | The worker types ([section 7](#7-worker-types-and-models)). Edited from the page's Worker types panel too. | |

The scheduler re-reads the file whenever it changes, so most edits take effect within one tick (15 seconds) without
a restart. A broken edit (not valid JSON) is logged and the old settings are kept. `port`, `listen` and `logDir` only
change at the next start: use the tray's **Restart Switchyard**.

A pool file made by `projecttoolkit.ps1` starts with `"listen": "lan"` and `"remoteControl": true`, so anyone on your
network can open the page and press its buttons. On a network you do not trust, change them to `"localhost"` and
`false`.

There is no "paused" setting in the file: pausing is done on the page, and a restarted scheduler starts running
(start it with `-Paused` to start paused).

## 5. Adding and removing a project

**Adding.** Give the repository a job queue with the toolkit's script. It writes the `jobs/` folder and adds the
project to the pool file (creating the file if it is the first):

```powershell
cd C:\Projects\ProjectToolkit
.\bin\projecttoolkit.ps1 -Action jobs -Repo C:\Projects\MyProject -Project MyProject
```

```text
== job queue in C:\Projects\MyProject
created  docs/known_gaps.md
created  jobs/README.md
created  jobs/card-template.md
...
created  jobs/project.md
created  jobs/scheduler.json
== scheduler pool
created  C:\Users\you\AppData\Local\ProjectToolkit\pool.json (with the pool defaults) and added MyProject

Next:
  1. Fill in jobs/categories.json with the planner: ...
  2. Fill in jobs/project.md (gates, locations, briefs). In jobs/scheduler.json set project (it must equal the name
     in the pool file) and health.command ...
  3. Write the first plan in docs/ai/ and card it into jobs/queue/.
  4. Commit and push to main. The pool joins the project at its next tick; there is no scheduler to start for it.
```

Do the four steps it lists; the [projects and job queue guide](projects.md) explains each file. Two settings in
`jobs/scheduler.json` matter to Switchyard:

- `project` must match the name in the pool file.
- `health.command` is how Switchyard checks that `main` still builds. Left empty, there is no health check at all.
  For a .NET project, for example:
  `"health": { "command": "dotnet build MyProject.slnx -warnaserror -m:{jobs} && dotnet test MyProject.slnx --no-build" }`.

Switchyard reads `scheduler.json` from `main` (not from your working copy), so commit and push it. At its next tick
the project **joins**: it appears in the projects table, and its ready cards start to run. Nothing else to start or
restart. Writing the categories, the project file and the first plan is work you do with a planner LLM: see the
[planner guide](planner.md).

**Removing.** Delete the project's entry from the pool file. Running workers of that project are left to finish (no
new card of it starts), then the project leaves the page. Its cards and its worktree folders
(`C:\Projects\MyProject-workspaces\`) stay on disk; delete the folder yourself if you want the space back (run
`git worktree prune` in the repository afterwards). To stop a project for a while instead, use its **Pause** or
**Drain** button on the page.

## 6. The page

Open `http://localhost:8766/`. The page updates itself every couple of seconds; you never need to reload it, and
after a deploy it reloads itself onto the new version.

### What every view shares

At the top is the **Projects** window, titled `Switchyard v<version>`. Its title bar holds:

| Control | What it does |
|---|---|
| CPU and memory | This PC's load, so you can see whether more workers would fit. |
| **Pause all** / **Resume all** | Stops every project from starting new workers, or lets them start again. Running workers carry on. |
| **Drain and exit** | Starts nothing new, lets running cards finish, then the scheduler exits. Use it before a reboot. |
| **Stop all and exit** | Asks every worker to stop cleanly, then exits. |
| Project list (**All projects**, then each name) | Shows only one project's rows. Appears with two or more projects. Only changes this browser's view. |
| View list (**Workers**, **Plans**) | Switches view. The view is in the address too (`/?view=plans`), so you can bookmark it or open it on your phone. |
| Theme list | The page's look. Kept in this browser only. |
| `last update HH:mm:ss` | When the page last heard from the scheduler. `scheduler not responding` means it has stopped answering. |

Under the title bar each project has a **notice line**: what the scheduler is doing or why it is not (paused, main
red, out of quota), and the reason when a click of yours failed.

Every button shows `Working...` until its effect appears on the page. Buttons that change things work only on the
PC the scheduler runs on, unless `remoteControl` is on in the pool file; from elsewhere the page is read-only.

### Workers view

The view you will use most (`/?view=workers`, the default). From top to bottom:

**The projects table.** One row per project: its colour chip and name, **Health** (`main not checked yet`,
`checking <sha> (n min)`, or green or red with a **log** link), **Workspaces** (how many worktree slots the project may
use; type a number from 1 to 64 and press Enter; `N busy` shows how many are in use), and its buttons:

| Button | What it does |
|---|---|
| **Check main** | Builds and tests `main` now ([section 8](#8-health-checks-and-what-a-red-main-does)). Shows `Checking...` while it runs. |
| **Categories** | Opens the project's `jobs/categories.json` in your editor on the scheduler's PC. |
| **Planner push: on / off** | Turns the project's [planner outbox](#10-the-planner-outbox) on or off. |
| **Pause** / **Resume** | Stops this project starting new cards, or starts it again. |
| **Drain** | Lets this project's running cards finish and starts nothing new; the project then stays paused. |
| **Stop** | Stops this project's workers and pauses it. The scheduler and the other projects keep running. |

**Statistics.** A range list (1 hour to 2 weeks, 24 hours by default) and lines such as
`Last 24 h (...): 14 cards (16 runs): done 11, review 2, ...`. With locks on, a line
`Locks today: N granted, N waited, N yielded, N refused, ...` ([section 9](#9-locks)).

**Worker types.** Click **Worker types ▸** to open the table of worker types ([section 7](#7-worker-types-and-models)):
Type, Running, Cap, Agent, Model, Endpoint, Metrics, Default (read-only). Edit a row, **Add type**, **Remove** (not while one of
that type is running) and **Max active workers**, then **Save**; the change is written to the pool file and used from
the next tick. An agent that ran out of quota shows `out of quota since HH:mm` and a **Resume** button for when your
quota is back.

**Waiting on you.** Only there when something needs you. One row per card, with what it is waiting for:

| Shown as | Means | Buttons |
|---|---|---|
| question | A worker parked the card with a question (`needs-human`). | One button per answer the worker offered (each says where the card goes next), **Open**, **Resume**. |
| review | The card is finished but needs your eyes. | **Open**, **Done** (to `done/`), **Send back** (to `active/`, ready, for another try). |
| waiting | The card was sent back to wait for something (the planner, another card). | **Open**, **Ready**. |
| stuck | The card failed too many times in a row. | **Open**, **Retry** (forget the failures and try again), **log**. |
| template | A file the toolkit gave this project differs from the toolkit's template. | **Overwrite** (take the toolkit's), **Keep** (keep yours). |

Every row has a **note** box. **Save note** adds your note to the card's `## Dev` section without moving it; any other
button on the row sends the note along. The next worker reads `## Dev` first, and it wins over the card, so this is
how you give a worker direction. **Open** opens the card in your editor.

A question looks like this on the card and is shown as one line with its buttons:

```markdown
## Questions

- Q1: Found: the export writes dates as 11/22/2026 but the importer expects 2026-11-22. Question: should the
  export change to the importer's format? [yes, change the export -> queue | no, leave it -> done]
```

**Worker windows.** One window per running worker, titled with the project chip, the worker type and model, and the
card. The bar shows how much of the card's time box is used and how fast the model is writing. Inside: what the worker
is doing (its log as it runs), the files it holds locks on, and:

| Control | What it does |
|---|---|
| **Message to this worker** and **Send** | Sends the worker a message (up to 2000 characters). A Claude worker is handed it after its next tool call; others read it from `msg-slotN.txt` as the README tells them. It answers in its log, then acts on it. |
| **Minimize** / **Expand** | Folds the window (this browser only). |
| **Stop** | Asks the worker to stop at its next safe point: it pushes what is green and notes in the card where to resume. |
| **Kill** | Ends the worker now. Its unpushed work is lost; use it only when Stop is not answered. |

After a Stop or Kill the card stays in `active/` as `waiting: you (stopped from the page)` and shows under Waiting on you; press **Resume** when it should run again.

**Queue.** The cards that will run next, in order, with a note when one is held back (a lock, a dependency, a yield).
`Nothing queued.` when empty.

**Recent events.** The last ten finished runs: time, project, card, result, a **log** link and **Open**.

**Trends.** A chart of cards finished over the chosen range, once there is history from before today.

### Plans view

`/?view=plans` shows every project's plans as a tree you can fold: each plan with `done/total` cards, its sections,
and the cards under each section, marked ✓ (done), ☐ (not yet) or ✗ (cancelled). A plan links to its text; a card in
review links to its review panel, other cards to their text. **Archived plans** are folded at the bottom, and
**No plan** collects cards that name no plan. Use it to answer "how far along is this plan?".

### Gantt view (coming)

A timeline of runs per card, grouped by category or plan, is designed
([views.md, Gantt](../design/tdd/scheduler/views.md#gantt)) and carded (1132-C), but not on the page yet. Until it
lands, the view list shows only Workers and Plans.

### The review panel

Clicking a review card's link (in the Plans view, for example) opens `/review?project=<name>&id=<card>`: the card's status with **Done**, **Send back** and a note
box, its **Questions** with answer buttons, the **Commits** it made (with files and lines changed), the **Health of
main**, its **Result**, and links to the files the Result names. This is where you check a worker's claim before you
accept it. In a pool its own buttons do not work yet (see [Troubleshooting](#12-troubleshooting)); use the card's row
under Waiting on you to act on it.

### Following one card

What you see on the page is cards moving between folders on `main`. Say the planner left this card in
`jobs/queue/`:

```markdown
# 12-A: Export dates in ISO format

Category: A
Status: ready
Depends on: none
Time box: 60 min
Plan: jobs/plans/export_plan.md, section 2

## Task

Make the CSV export write dates as 2026-11-22 instead of 11/22/2026, with a test.

## Done when

The export test passes and the gates are green.

## Questions

## Result
```

1. It is ready and depends on nothing, so it shows in **Queue**. When a worker type that fits is free, Switchyard
   moves it to `jobs/active/` (commit `jobs: start 12-A-iso-dates`), resets a worktree such as
   `C:\Projects\MyProject-workspaces\slot2` to `main`, and starts the worker there. A **worker window** opens.
2. The worker reads `jobs/README.md`, does the card, runs the gates and pushes green commits to `main`. Every so often
   it adds a journal line to `## Result`; you see its progress in the window.
3. When it is done it hands the card back to Switchyard (it never moves cards itself), and Switchyard commits the move.
   The card is now in `jobs/done/`:

```markdown
# 12-A: Export dates in ISO format

Category: A
Status: done
Claimed: 2026-11-22, lane A
...

## Result

- 2026-11-22 14:05: test ExportWritesIsoDates added, fails as expected; next: change the formatter.
- 2026-11-22 14:21: Exporter.FormatDate writes yyyy-MM-dd; build and tests green; pushed 3f2a9c1.
```

   It shows under **Recent events** as `done`. Had it needed you, it would be in `jobs/review/` (or still in
   `active/` with `Status: needs-human: ...`) and shown under **Waiting on you**.

## 7. Worker types and models

A **worker type** is a kind of worker: which agent CLI it runs, with which model, and how many may run at once. They
live in the pool file's `pool.types` and are edited on the page (**Worker types ▸**).

| Field | Means |
|---|---|
| name (the key) | What the page and cards call it, for example `Sonnet`. |
| `max` | How many of this type may run at once. `0` turns the type off. |
| `agent` | Which agent the type runs: a name from `agents` in the project's `jobs/scheduler.json` (`claude` in the template), or empty for the `worker` section there (an OpenAI-style CLI for a local model). |
| `model` | The model name given to the agent, for example `claude-sonnet-5-5`. |
| `endpoint` | For a local model server: its address, for example `http://localhost:18400/v1`. A type whose server does not answer gets no cards (the page says `model server X does not answer`). |
| `metricsUrl` | Where to read the server's speed figures from (shown as T/s in the worker window). Defaults to `/metrics` on the endpoint's host. |
| `defaultWorker` | Set once in `pool`, beside `types`: the name of the type that takes every card with no `Worker:` line. Costly types are simply not the default, so cards only go to them when they name them. |
| `projects` | Optional list of project names the type serves. Empty: every project. |

The command each agent runs is in the project's `jobs/scheduler.json` (`agents.claude.cli` and `args`; the template's
Claude entry runs `claude -p {prompt} --output-format stream-json ... --model {model}`). `{model}`, `{endpoint}` and
`{jobs}` (build threads per worker: your cores divided by `maxWorkers`) are filled in for each worker.

Four agents are supported: the **Copilot CLI** (an empty `agent`: the `worker` section, an OpenAI-style CLI for a
local model server), **Claude Code** (`claude`), **OpenAI Codex CLI** (`codex`) and **OpenCode** (`opencode`). The
template configures the first two. A type for one of the others names its agent, which must have an entry under
`agents` in the project's `jobs/scheduler.json` — the exact `cli`, `args`, `env` and `quotaPatterns` for each are in
[`docs/design/tdd/scheduler/agents.md`](../design/tdd/scheduler/agents.md), sections 2 and 3; installing and signing
in to them is in the [getting-started Requirements](getting-started.md#requirements). Example types:

```json
    "types": {
      "Sonnet":   { "max": 4, "agent": "claude",   "model": "claude-sonnet-5-5" },
      "Codex":    { "max": 2, "agent": "codex",    "model": "gpt-5" },
      "OpenCode": { "max": 2, "agent": "opencode", "model": "qwen3.8",
                    "endpoint": "http://localhost:18400/v1" }
}
```

A Codex worker runs `codex exec --json` against OpenAI's own service with its ChatGPT or API-key sign-in, so its
`agents` entry says `"endpoint": ""` and the type's `model` is a Codex model; it is never handed a model server. An
OpenCode worker runs `opencode run --format json --standalone --auto`, with its provider and permission config in
the `OPENCODE_CONFIG_CONTENT` value in its entry's `env`: as in the example it runs against the type's (or the
config's) local OpenAI-compatible endpoint (`-m local/{model}` in its args), and with `"endpoint": ""` and a
`provider/model` name instead it runs against that hosted provider.

**Which type takes a card** comes from the card's `Worker:` line, which the planner sets:

| Card says | Goes to |
|---|---|
| no `Worker:` line | the `defaultWorker` type, when it has room |
| `Worker: Opus` (a type name, or a model name) | that type only |

Types are tried in the order they are listed. A type is skipped while it is full, off, out of quota, or its model server
does not answer. Which model suits which card is a planning decision; `jobs/models.json` describes each model for the
planner (see the [planner guide](planner.md)).

**Out of quota.** When a worker's output matches one of its agent's `quotaPatterns` (such as `usage limit`), Switchyard
marks that agent out of quota: no type using it starts new workers, in any project, until you press **Resume** on that
agent in the Worker types panel (or **Resume all**). Other types carry on. It is not cleared on a timer, because only
you know when your quota is back.

## 8. Health checks, and what a red main does

Workers only push commits that passed the gates, but two green commits can still break `main` together. So after every
finished run (and when you press **Check main**) Switchyard checks `main` itself: it resets the worktree
`C:\Projects\MyProject-workspaces\health` to `main` and runs the project's `health.command` there. The result shows in
the projects table, with a **log** link.

- **Green**: nothing changes. A `main` already checked is not checked again.
- **Red** (the command failed, or ran longer than `maxMinutes`): the project is paused (`paused: main is red`). Workers
  already running carry on; no new card of that project starts, so no one builds on a broken `main`. Other projects
  keep running. The log shows `RED ... dispatching paused`.
- **Back to green**: as soon as a check of a newer `main` passes, dispatching resumes by itself
  (`main is green again: dispatching resumed`).
- **Could not run**: a check that failed only because files were locked (MSB3026/MSB3027) is retried once, and then
  reported as "could not run", neither red nor green.

What to do when `main` is red: open the **log** link, find the failing build line or test, and fix `main` (by hand, or
have the planner write a fix card with priority high; see the [planner guide](planner.md)). **Resume** on the project
restarts dispatching without a green check; do that only if you know the failure is harmless (a flaky test), because
every new card would start from the broken `main`.

Details: [spec, section A7](../design/tdd/scheduler/spec.md#a7-gates-hand-over-and-the-health-check).

## 9. Locks

Several workers run at once, so two must not change the same file. Each card has a **scope**: the files it may
change (its `Scope:` line, or its category's files in `jobs/categories.json`). Within its scope a worker needs no
permission.

With locks switched on (`"locks": true` in the project's `jobs/scheduler.json`), a worker that finds it must change a
file outside its scope asks for it while it works:

```powershell
scheduler-next.exe lock code/src/Exporter.cs --why 'the date format is set here'
```

| Exit | Answer | The worker then |
|---|---|---|
| 0 | `granted` | changes the file. |
| 2 | `waiting: held by <card>, gives up at <time>` | asks again; the other card should finish first. |
| 3 | `yield: <path> held by <card> (older)` | saves its work to a `wip/` branch and gives the card back; it runs again later. |
| 4 | `refused: <reason>` | asks for less, or parks the card with a question for you. |

A git hook (`check-push`, installed by Switchyard) refuses a push to `main` that touches a file the card does not
hold, so a worker cannot skip the step. On the page, each worker window shows `holds: ...`, the queue notes cards
held back by a lock, and the statistics show `Locks today: ...`. Without locks, a worker that needs a file outside
its scope parks the card and asks you. Design: [dynamic-locks.md](../design/tdd/scheduler/dynamic-locks.md).

## 10. The planner outbox

The planner (an LLM you plan with) writes plans and cards. If it can push to your repository's `main` itself, you do
not need the outbox. If it cannot (for example it runs in a sandbox that can write files on your PC but not reach your
git server), it leaves its changes in an **outbox** folder and Switchyard commits them for it.

Set it up in the project's `jobs/scheduler.json`:

```json
"planner": { "outbox": "jobs\\.planner-outbox", "fetchRepo": "C:\\Projects\\MyProject" }
```

`fetchRepo` is the copy of the repository the planner reads; `outbox` is the folder inside it. Then turn it on with
the project's **Planner push: off** button (it becomes **Planner push: on**); it starts off, and the setting survives
a restart.

While it is on, about once a minute Switchyard runs `git fetch` in `fetchRepo` (fetch only: it never changes your
working files), so the planner sees an up-to-date `origin/main`. It then takes each finished batch from the outbox,
oldest first, applies it to `main`, commits and pushes, and moves the batch to `sent\` (or to `failed\` with a
`reason.txt`). A batch may only touch files under the project's `jobs/` folder. Cards named `new-A-<slug>.md` get
their number from Switchyard. The batch format is in [planner-outbox.md](../design/tdd/scheduler/planner-outbox.md),
and how to work with the planner is in the [planner guide](planner.md).

## 11. Logs and state files

Everything Switchyard writes lives outside your repository, so it never shows up in `git status`. Per project, in
`%LOCALAPPDATA%\<Project>\scheduler\` (for MyProject, `C:\Users\you\AppData\Local\MyProject\scheduler\`):

| File | What it is | Look at it when |
|---|---|---|
| `scheduler.log` | The project's log: every start, move, health check and decision. Rolled aside at 50 MB (`scheduler.1.log` to `.3`). | Anything is unclear. |
| `<card>-<yyyyMMdd-HHmmss>.log` (and `.jsonl`) | One worker session: everything the worker said and did. The page's **log** links open it. | A card failed or did something odd. |
| `<card>-<yyyyMMdd-HHmmss>.prompt.txt` | The exact brief the worker was given. | You wonder what the worker was told. |
| `health-<sha>-<yyyyMMdd-HHmmss>.log` | One health check of `main`. | `main` is red. |
| `accounting.csv` | One line per finished run: card, result, minutes, model, type. The statistics and trends come from it. | You want numbers (open it in a spreadsheet). |
| `status.txt` | A plain-text snapshot of what is running. | The page is not answering. |
| `running.json`, `retries.json`, `planner-state.json`, `templates.json` | What Switchyard remembers across restarts: running workers (so it can adopt them), failure counts, the planner push switch, template choices. | Rarely; do not edit while it runs. |
| `moves\` | Workers' requests to move cards and to lock files, and the answers. | A card did not move. |
| `locks\` | The lock table. | A lock seems stuck. |
| `stop-slotN`, `warn-slotN`, `msg-slotN.txt` | Signals to the worker in slot N: stop now, time nearly up, a message from you. Written by the page; never create them by hand. | |

The pool's own log is in `%LOCALAPPDATA%\ProjectToolkit\pool\` (`scheduler.log`, dated logs in `logs\`, and
`status.txt`). The tray's **View log** shows it live. Each project also keeps a dated log for a week in its own
`jobs\.logs\`.

The worktrees are beside the repository, in `C:\Projects\MyProject-workspaces\`: `slot1`, `slot2`, ... (one per running
worker, reset to `main` before each card), `health` (for health checks) and `answers` (where Switchyard commits card
moves and your answers). Leave them alone while Switchyard runs.

## 12. Troubleshooting

**The page looks wrong or out of date after an update.** The page reloads itself when the scheduler's version
changes. If a tab was open across a long gap, or the browser cached an old copy, press F5. If the title still shows the
old `Switchyard v<version>`, the old scheduler is still running: use the tray's **Restart Switchyard** and check
**View log** for the start line.

**The page does not open at all.** Check the tray is running (the icon by the clock). The balloon `The page's address
is not known yet` means the scheduler has not started yet or failed to: **View log** says why. A log line
`cannot listen on http://localhost:8766/: port 8766 is already in use by another program` means something else has
the port; stop it, or change `port` in the pool file and restart.

**Nothing starts.** Read the project's notice line on the page:

| Notice | Cause | Fix |
|---|---|---|
| `paused` | Someone pressed Pause, Drain or Stop. | **Resume** (or **Resume all**). |
| `paused: main is red` | The health check failed. | Fix `main` ([section 8](#8-health-checks-and-what-a-red-main-does)). It resumes by itself when green. |
| `paused: out of quota` / `out of quota since HH:mm` | The agent's quota ran out. | Wait for it to come back, then **Resume** on that agent. |
| (no notice, cards in Queue) | No worker type fits the cards: every fitting type is full, off, or its model server does not answer; or the project's **Workspaces** are all busy; or `maxWorkers` is reached. | Look at the Worker types panel and the queue's notes; raise a `max`, fix the server, or set a different `Worker:` line on the card. |
| (no notice, empty Queue) | No card is ready: they wait on dependencies, on you or on the planner. | See **Waiting on you** and the Plans view. |

**A worktree is missing or broken.** A log line such as `worktree add for slot 2 failed` or
`slot 2: ... was not a worktree; it was moved to ... and the worktree added again`. Switchyard repairs a slot folder
that is missing or is not a worktree by itself the next time it uses it. If it keeps failing, run in the repository:

```powershell
cd C:\Projects\MyProject
git worktree list     # what git thinks exists
git worktree prune    # forget worktrees whose folders are gone
```

and let the next card recreate it.

**A card sits in `active/` with no worker.** Its worker ended without handing it back (a crash, a reboot). Switchyard
notices and starts it again a limited number of times, then shows it under Waiting on you as `stuck`: read its
**log**, then **Retry** or **Open** it to fix the card. Do not run hand-started agent sessions on the same repository
while Switchyard runs; their cards look abandoned to it.

**A worker is stuck.** Its window shows `quiet q min` when it has printed nothing for a while. Send it a message; if
it does not answer, **Stop** it (it saves its work and the card waits for your **Resume**), and only if that fails, **Kill**.

**The review panel's buttons fail with "which project?"** In a pool, the review panel's own **Done** and **Send back**
do not yet say which project they are for (a known gap). Use the same buttons on the card's row under
**Waiting on you**.

**A deploy rolled back.** `the new scheduler <version> did not stay up for 30 s; putting the old build back`: the
new build fails at start. The old one is running again. **View log** shows why; fix and deploy again.

**`build-and-deploy.ps1` stops at the start.** `main cannot be fast-forwarded`: your `C:\Projects\ProjectToolkit`
has local commits or changes on `main`; tidy them, or use `-NoPull` to deploy what you have. `No pool file at ...`:
add a project first ([section 5](#5-adding-and-removing-a-project)).
