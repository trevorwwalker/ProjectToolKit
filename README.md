# ProjectToolkit

A kit for building software with AI agents working a shared job queue. It is three things, not one
application: the **canonical project layout** with the templates and documentation that go with it, the
**job queue** (cards that move through `queue/`, `active/`, `review/` and `done/`) that the agents work, and
**Switchyard**, the scheduler (`scheduler-next.exe`, C# on .NET 10) and its Windows tray program that run one or
more queues, start a worker per card in its own git worktree, and serve a page where you watch and steer.

## Documentation

**Start with the [user guide](docs/guide/README.md)**: what each tool is for and how to run it.

- [Getting started](docs/guide/getting-started.md): **start here**; requirements, install and a first finished card.
- [Working with a planner LLM](docs/guide/planner.md): the planner writes the plans and cards.
- [Switchyard](docs/guide/switchyard.md): the scheduler, its tray, its page and the worker types.
- [Projects](docs/guide/projects.md): set up a project, or add the queue to one you have.
- [Scripts](docs/guide/scripts.md): everything in `bin/`.
- [Scheduler command line](docs/guide/scheduler-cli.md): the executable's commands.
- [`docs/workflow.md`](docs/workflow.md): the workflow, and in section 11 how to use this kit. Point an LLM at it to
  stand up a new project.
- [`docs/agent-lessons.md`](docs/agent-lessons.md): lessons from running workers on a real project, as rules for
  cards, briefs and scheduler settings. Read it before applying the kit to a large codebase.
- Design documents: [`docs/design/`](docs/design/gdd/ProjectToolkit.md) (the game-design-style overview, the
  [technical design](docs/design/tdd/ProjectToolkit-technical.md), the
  [scheduler spec](docs/design/tdd/scheduler/spec.md) and the
  [roadmap](jobs/plans/scheduler-roadmap_plan.md)).

## Install

Windows 10 or 11, with the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).
The runtime is enough; the SDK is not needed to install.

1. Download `ProjectToolkit-Setup.exe` from the
   [latest release](https://github.com/trevorwwalker/ProjectToolkit/releases/latest) and run it. It installs for your
   user only (no administrator rights needed) into `%LOCALAPPDATA%\Programs\ProjectToolkit`, and adds a Start menu
   entry, **Switchyard**.
   The installer is not code-signed yet, so Windows may show "Windows protected your PC" with no **Run** button: click **More info**, then **Run anyway**. You can also choose to install for all users instead.
2. On the installer's task page, two boxes are offered and both are unticked by default: **Start Switchyard when I
   sign in**, and **Add the bin folder to my PATH** (so `projecttoolkit.ps1` can be run by name).
3. Start **Switchyard** from the Start menu, then follow [getting-started.md](docs/guide/getting-started.md).
   Uninstalling from Windows' Installed apps keeps your pool, logs and projects' own state.

To build from source instead, publish the scheduler with
`dotnet publish code\src\Scheduler.Host -c Release -o out\windows\scheduler` (this needs the .NET 10 SDK), then start
`scheduler-tray.exe -Pool` from that folder. The [Switchyard guide](docs/guide/switchyard.md) covers both.

## What it does now

- One scheduler runs a **pool of projects**, each with its own queue, workspace slots and settings.
- **Worker types** run a card with Copilot CLI, Claude Code, Codex CLI or OpenCode (the output format is
  recognised from the command), with a **default worker type** for cards that name none.
- The page has **Plans** (a checklist tree per plan, where you see and steer the project; finished plans and steps hide behind its Hide completed toggle), **Workers** (where the action is) and **Gantt** (with estimates) views, plus questions, reviews and statistics. It opens on the view you left it at, and on Plans the first time.
- The **planner outbox** hands the planner's requests and the developer's answers back and forth.
- A **health check** of the main branch is run by the scheduler, which pauses when main is red.
- The scheduler assigns **card numbers** and is the only writer of card moves; plans live in `jobs/plans/`.

## Actions

Two independent things the kit does, and one script with one verb each. The first does the second as
part of standing up a project.

- **Set up a new project** from the canonical layout:
  `bin/projecttoolkit.ps1 -Action new-project -Repo <path> -Categories "A=core:code/src/,test/;B=tools:code/tools/" -Gate "<cmd>;;<cmd>"`
  (`-Lanes` is the older name for `-Categories` and still works). It writes the layout of workflow.md section 1
  out of `assets/templates/` - the folders, `README.md`, `.gitignore`, `.gitattributes`, `docs/conventions.md`,
  the gdd and tdd design documents, and a `code/src/<module>/` folder for each `-Modules` name - and then sets up
  the job queue. `bin/init-project.ps1` is a forwarder to it, for older instructions.
- **Set up or refresh the job queue** in a repository that is already there:
  `bin/projecttoolkit.ps1 -Action jobs -Repo <path>` writes `jobs/` (the queue folders, the protocol README,
  `project.md`, `categories.json`, `models.json`, `scheduler.json`, the card and plan templates, the requests
  folder) and `docs/known_gaps.md`, then adds the project to the pool file (`%LOCALAPPDATA%\ProjectToolkit\pool.json`; `-NoPool` skips that). An older project keeps its `lanes.json` and
  `workers.json`, which the scheduler still reads; the worker types now live in the pool file. Add `-Update` to
  pull kit improvements into a project: it refreshes the text the kit owns, fills in what the project is missing,
  and leaves the project's own files alone.
- **Run the queue.** Switchyard runs every project in the pool file (`%LOCALAPPDATA%\ProjectToolkit\pool.json`); `-Action jobs` adds a project to it. Installed, start **Switchyard** from the Start menu. From a clone, publish once with `dotnet publish <ProjectToolkit>\code\src\Scheduler.Host -c Release -o <ProjectToolkit>\out\windows\scheduler` (it puts `scheduler-tray.exe` and the page themes beside `scheduler-next.exe`) and start `<ProjectToolkit>\out\windows\scheduler\scheduler-tray.exe -Pool`. One project on its own: `scheduler-next.exe <repository>` (add `-Paused` to start paused); see [scheduler-cli.md](docs/guide/scheduler-cli.md). While developing,
  `dotnet run --project <ProjectToolkit>\code\src\Scheduler.Host -c Release -- <repository>` is the quick way.
  Build intermediates go to `build/`, never beside the source. Several projects can run at once.
  Its source is `code/src/` (`Scheduler.Core`, `Scheduler.Host`, `Scheduler.Tray`, `Scheduler.Web`), its tests
  `test/unit/`, the solution `code/src/SchedulerNext.slnx` (its `Directory.*.props` beside it; `test/unit/` imports
  them). Its spec, test plans and the planner outbox protocol are in `docs/design/tdd/scheduler/`; its roadmap is
  `jobs/plans/scheduler-roadmap_plan.md`. The page's themes are runtime assets in `assets/themes/`
  (`theme.default.json` and the named themes), copied beside the exe; a
  project can name its own with `page.theme`. A project set up before 2026-11-18 keeps its queue in `docs/jobs/`
  until it is moved; the scheduler and `-Update` find either. The PowerShell scheduler it replaced
  (`scheduler/schedule.ps1`) is in git history.
- `assets/templates/` holds the generic text every project gets: the jobs README, the project.md skeleton, the
  card, plan and request templates, the attachments README, example categories.json, models.json and scheduler.json, the planner's brief (`docs/planner-brief.md`), the root README,
  `docs/conventions.md`, `.gitignore` and `.gitattributes`, and the gdd and tdd design skeletons.
  `projecttoolkit.ps1 -Action new-project` writes them into a repository along with the canonical file layout
  (workflow.md section 1); `-Action jobs -Update` refreshes the ones the kit owns and leaves the project's own alone.

Fix the toolkit, not a project's copy, then run `-Action jobs -Update` on each project.

## License

Copyright (c) 2026 Trevor W. Walker. ProjectToolkit is licensed under the
[PolyForm Noncommercial License 1.0.0](LICENSE): you may use, copy, modify and share it for any
noncommercial purpose, provided you keep the license and its `Required Notice:` line with every copy.
All commercial rights are reserved; for commercial use, contact the copyright holder
(https://github.com/trevorwwalker).
