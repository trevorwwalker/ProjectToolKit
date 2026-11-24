# ProjectToolkit

A kit for building software with AI agents working a shared job queue. It is three things, not one
application: the **canonical project layout** with the templates and documentation that go with it,
the **job queue system** the agents work, and the **scheduler** that runs a queue. New here? Start with the
[user guide](docs/guide/README.md): what each tool is for and how to run it. Then
[`docs/workflow.md`](docs/workflow.md): the workflow, and in section 11, how to use this kit.
Point an LLM at that document to stand up a new project.

[`docs/agent-lessons.md`](docs/agent-lessons.md) is the lessons from running workers on a real
project, as rules for cards, briefs and scheduler settings - read it before applying the kit to a
large or complex codebase.

## Actions

Two independent things the kit does, and one script with one verb each. The first does the second as
part of standing up a project.

- **Set up a new project** from the canonical layout:
  `bin/projecttoolkit.ps1 -Action new-project -Repo <path> -Lanes "A=core:code/src/,test/;B=tools:code/tools/" -Gate "<cmd>;;<cmd>"`.
  It writes the layout of workflow.md section 1 out of `assets/templates/` - the folders, `README.md`,
  `.gitignore`, `.gitattributes`, `docs/conventions.md`, the gdd and tdd design documents, and a
  `code/src/<module>/` folder for each `-Modules` name - and then sets up the job queue.
  `bin/init-project.ps1` is a forwarder to it, for older instructions.
- **Set up or refresh the job queue** in a repository that is already there:
  `bin/projecttoolkit.ps1 -Action jobs -Repo <path>` writes `jobs/` (the queue folders, the protocol
  README, `project.md`, `lanes.json`, `workers.json`, `models.json`, `scheduler.json`, the card and
  plan templates, the requests folder), `docs/ai/` and `docs/known_gaps.md`, and nothing else. Add
  `-Update` to pull kit improvements into a project: it refreshes the text the kit owns, fills in what
  the project is missing, and leaves the project's own files alone.
- **Run the queue.** The scheduler (`scheduler-next.exe`, C# on .NET 10) is what every project runs,
  from here (projects keep no copy), configured by the project's `jobs/scheduler.json`. Publish the runnable
  folder once, then start it (add `-Paused` to start paused):
  `dotnet publish <ProjectToolkit>\code\src\Scheduler.Host -c Release -o <ProjectToolkit>\out\windows\scheduler`
  then `<ProjectToolkit>\out\windows\scheduler\scheduler-next.exe <repository>`. Without a console window, as a
  tray icon (double-click opens the page; its menu has View log and Exit):
  `<ProjectToolkit>\out\windows\scheduler\scheduler-tray.exe <repository>` (same arguments) - that one publish
  puts `scheduler-tray.exe` beside `scheduler-next.exe`, with the page themes. While developing,
  `dotnet run --project <ProjectToolkit>\code\src\Scheduler.Host -c Release -- <repository>` is the quick way.
  Build intermediates go to `build/`, never beside the source. Several projects can run at once.
  Its source is `code/src/`
  (`Scheduler.Core`, `Scheduler.Host`, `Scheduler.Web`), its tests `test/unit/`, the solution
  `code/src/SchedulerNext.slnx` (its `Directory.*.props` beside it; `test/unit/` imports them). Its spec, test plans and the planner outbox protocol are in
  `docs/design/tdd/scheduler/`; its roadmap is `docs/design/gdd/scheduler-roadmap.md`. The page's
  themes are runtime assets in `assets/themes/` (`theme.default.json`, Dark,
  Light, Rams, Kings, Dodgers, Lakers, Clippers, Chargers), copied beside the exe; a project can
  name its own with `page.theme`. A project set up before 2026-11-18 keeps its queue in
  `docs/jobs/` until it is moved; the scheduler and `-Update` find either. The PowerShell scheduler
  it replaced (`scheduler/schedule.ps1`) is in git history.
- `assets/templates/` holds the generic text every project gets: the jobs README, the project.md skeleton,
  the card, plan and request templates, example lanes.json, workers.json and scheduler.json, the
  root README, `docs/conventions.md`, `.gitignore` and `.gitattributes`, and the gdd and tdd design
  skeletons. `projecttoolkit.ps1 -Action new-project` writes them into a repository along with the
  canonical file layout (workflow.md section 1); `-Action jobs -Update` refreshes the ones the kit
  owns and leaves the project's own alone.

Fix the toolkit, not a project's copy, then run `-Action jobs -Update` on each project.
