# The scripts in `bin/`

ProjectToolkit comes with ten PowerShell scripts in its `bin/` folder. This page says what each one is for, how to call it,
what it changes on disk, and when you would run it.

New to the toolkit? Start with the [guide index](README.md). Most people only ever need the first three scripts; the rest are
checks and measurements for people working on the toolkit itself.

| Script | Who it is for | In one line |
|---|---|---|
| [`projecttoolkit.ps1`](#projecttoolkitps1) | everyone | Start a new project, or add or refresh a job queue in one you have. |
| [`build-and-deploy.ps1`](#build-and-deployps1) | everyone | Bring the toolkit up to date and restart the scheduler. |
| [`deploy-scheduler.ps1`](#deploy-schedulerps1) | everyone (`build-and-deploy` calls it) | Swap in a new scheduler build without stopping the workers. |
| [`init-project.ps1`](#init-projectps1) | old instructions only | An old name that forwards to `projecttoolkit.ps1`. |
| [`test-projecttoolkit.ps1`](#test-projecttoolkitps1) | toolkit developers | Checks that `projecttoolkit.ps1` still works. |
| [`startup-gate.ps1`](#startup-gateps1) | toolkit developers | Fails if the scheduler's start got clearly slower. |
| [`git-baseline.ps1`](#git-baselineps1) | **developer tool** | Counts the processes and time a test run or scheduler job costs. |
| [`test-profile.ps1`](#test-profileps1) | **developer tool** | Finds the slowest tests. |
| [`process-start-check.ps1`](#process-start-checkps1) | **developer tool** | Measures how fast this PC starts programs. |
| [`live-stream-measure.ps1`](#live-stream-measureps1) | **developer tool** | Measures what the scheduler's live page costs. |

## Before you start

- Run the commands from a PowerShell prompt in the toolkit's folder (here `C:\Projects\ProjectToolkit`). Each script finds the
  kit from its own location, so you can also call it by full path from anywhere.
- Most scripts have built-in help: `Get-Help .\bin\projecttoolkit.ps1 -Full` prints the parameters and examples.
  `live-stream-measure.ps1` and `init-project.ps1` keep their notes in comments at the top of the file instead.
- The **scheduler** is the program that hands job cards to AI workers. The **planner** is the AI session you plan with: you
  describe what you want and it writes plans and cards. Wherever this page says "ask your planner", the
  [planner guide](planner.md) shows how.

---

## projecttoolkit.ps1

**What it is for.** The setup script. It does two independent jobs, chosen with `-Action`:

- `new-project` makes a new project: the standard folder layout, a `README.md`, `.gitignore`, `.gitattributes`, a conventions
  file and two empty design documents, and then it does the second job too.
- `jobs` sets up the **job queue** alone: the `jobs/` folders (`queue`, `active`, `review`, `done`), the rules every worker
  reads (`jobs/README.md`), and the project's settings files. Use it to add a queue to a repository you already have, or with
  `-Update` to pull in newer rules from the toolkit.

**Why.** Workers and the scheduler expect one layout. The script lays it down so you do not have to remember it, and
`-Update` carries a fix to the rules into every project.

### Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `-Action` | (required) | `new-project` or `jobs`. |
| `-Repo` | (required) | The project's folder. Created, and `git init`-ed on branch `main`, if it does not exist. |
| `-Project` | the name in `jobs/scheduler.json`, else the folder name | The project's name. |
| `-Categories` | `A=core:code/src/,test/` | The kinds of work, as `LETTER=name:path,path;LETTER=name:path`. Each category owns its paths, so two workers never edit the same file. One is fine to start with. Used only when `categories.json` is first written. |
| `-Modules` | none | Comma-separated folder names to create under `code/src/` (`new-project` only). Leave it out until your design names the modules. |
| `-Gate` | none | The commands a worker must pass before every push (build, test). Several strings, or one string with `;;` between commands. `{jobs}` becomes the thread count the scheduler allows. |
| `-Model` | `spark` | The default worker model written to the settings. |
| `-Endpoint` | `http://spark.local:18400/v1` | Where that model is served. Change it to your own, for example `http://localhost:18400/v1`. |
| `-JobsEnvVar` | `AGENT_JOBS` | The environment variable that tells a worker how many threads it may use. |
| `-Force` | off | Overwrite files that already exist. Without it nothing already there is touched. |
| `-Update` | off | With `-Action jobs`: refresh the kit's own text and fill in anything missing. |
| `-JoinPool` / `-NoPool` | joins | By default the last step adds the project to the scheduler's pool file. `-NoPool` skips that. |
| `-Slots` | 0 | Accepted and ignored (an old option). |

The pool join is skipped by itself inside a worker session, for a folder under the temp folder, and under any
`out\test-tmp\` folder, so trying the script in a scratch folder never touches your real pool.

### Examples

**1. A new project.**

```powershell
.\bin\projecttoolkit.ps1 -Action new-project -Repo C:\Projects\MyProject `
    -Categories "A=core:code/src/,test/;B=docs:docs/" `
    -Gate "dotnet build -m:{jobs}"
```

Output (shortened; the real run prints one `created` line per file):

```
git repository created in C:\Projects\MyProject
== canonical layout in C:\Projects\MyProject
created  .gitignore
created  README.md
created  docs/conventions.md
created  docs/design/gdd/MyProject.md
created  docs/design/tdd/MyProject-technical.md
== job queue in C:\Projects\MyProject
created  jobs/README.md
created  jobs/categories.json
created  jobs/project.md
created  jobs/scheduler.json
== scheduler pool
updated  ...\ProjectToolkit\pool.json (added MyProject)

MyProject is set up in C:\Projects\MyProject with the canonical layout and a job queue. Next:
  1. Fill in README.md (what the project is, ...
```

The script ends with a numbered list of what to do next. In short: describe the project, write the design, ask your planner
to fill in `jobs/categories.json` and `jobs/project.md` and to turn your first plan into cards (see the
[planner guide](planner.md)), then commit and push to `main`. The pool picks the project up at its next tick; there is no
scheduler to start for it.

**2. Add a queue to a repository you already have.**

```powershell
.\bin\projecttoolkit.ps1 -Action jobs -Repo C:\Projects\MyExistingApp
```

Only the queue, `docs/ai/` and `docs/known_gaps.md` are added. Nothing already there is overwritten.

**3. Pull the toolkit's newer rules into a project.**

```powershell
.\bin\projecttoolkit.ps1 -Action jobs -Repo C:\Projects\MyProject -Update
```

```
== job queue in C:\Projects\MyProject
kept     docs/known_gaps.md
same     jobs/README.md
same     jobs/card-template.md
...
Refreshed the kit's own text in C:\Projects\MyProject (the jobs README, the card, plan and request
templates, docs/ai/README.md, .gitattributes) and filled in any folder or file it was missing.
Left alone, because they are the project's own now: README.md, .gitignore, docs/conventions.md,
docs/design/, jobs/{project,categories,lanes,workers,scheduler}.json.
```

`same` means the file already matched the kit; `kept` means it is yours and was left alone. It is safe to repeat.

### What it changes on disk

Only files under `-Repo`, plus `%LOCALAPPDATA%\ProjectToolkit\pool.json` for the pool join. It never overwrites a file that
exists unless you give `-Force`. With `-Update` it rewrites only the kit's own text: `jobs/README.md`, the card, plan and
request templates, `docs/ai/README.md` and `.gitattributes`. Your `README.md`, `.gitignore`, `docs/conventions.md`,
`docs/design/`, `jobs/project.md`, `jobs/categories.json` and `jobs/scheduler.json` are never touched by it.

### When to run it

Once when you start a project (or put a queue into an existing one), and again with `-Update` after the toolkit has changed
its job rules. Update the toolkit itself first (see [`build-and-deploy.ps1`](#build-and-deployps1)).

### A card, before and after

The queue is folders of Markdown cards. A card the planner writes sits in `jobs/queue/`:

```
# 1-A: Add a --version flag

Category: A
Status: ready
Depends on: none

## Task
Make `myapp --version` print the version. Test first.

## Result
```

A worker does the job and fills in `## Result`; the scheduler then moves the card to `jobs/done/` (or `jobs/review/` if you
must check something). You never move cards yourself. To get cards written, ask your planner (see the
[planner guide](planner.md)).

---

## build-and-deploy.ps1

**What it is for.** The one command that brings the scheduler up to date: it fast-forwards the toolkit's `main`, runs
[`deploy-scheduler.ps1`](#deploy-schedulerps1), then makes sure the tray icon is running.

**Why.** The scheduler is built from the toolkit's own code. After the toolkit changes, this is how the running scheduler gets
the change without losing workers that are in the middle of a card.

### Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `-PoolFile` | `%LOCALAPPDATA%\ProjectToolkit\pool.json` | The pool file listing the projects the scheduler serves. It must exist (`projecttoolkit.ps1` creates it when a project joins). |
| `-NoPull` | off | Deploy what is checked out now; do not fetch `main` first. |
| `-Single` | off | The old one-project mode, kept for reference only; it will be removed. |
| `-Project` | `ProjectToolkit` | With `-Single`, the project folder to serve. |

### Examples

```powershell
.\bin\build-and-deploy.ps1                 # update main, deploy, make sure the tray is up
.\bin\build-and-deploy.ps1 -NoPull         # deploy my local changes as they are
.\bin\build-and-deploy.ps1 -PoolFile D:\Work\pool.json
```

### What it changes on disk

`git fetch` and a fast-forward merge of `origin/main` in the toolkit folder (it stops with a message if `main` has local
commits or changes: fix that, or use `-NoPull`). Then everything `deploy-scheduler.ps1` changes (below). If no tray is
running it starts `out\windows\scheduler\scheduler-tray.exe`, which opens the scheduler page once.

### When to run it

After you pull toolkit changes, or change the toolkit yourself and want the scheduler to use it. Workers keep running through
the swap, but the scheduler is briefly down.

---

## deploy-scheduler.ps1

**What it is for.** The deploy itself. It publishes a new scheduler into `out\windows\scheduler.next` while the old one keeps
running, ends the old scheduler (its workers keep running and the new one adopts them), keeps the replaced build in
`out\windows\scheduler.prev`, and starts the new one from the **same path** as before, so the page's firewall rule still
matches. If the new one does not stay up, the old build is swapped back and the script exits with 1. The tray's
"Update and restart" menu item does the same; use the script when the tray is not running.

### Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `-Kit` | the folder above `bin/` | The toolkit's root (the folder with `code\` and `out\`). |
| `-Repo` | what the running scheduler was started for | The repository for a one-project scheduler. |
| `-Pool` | off | Start the new scheduler as a pool, through its tray. Without it, it is started the way it was found. |
| `-PoolFile` | `%LOCALAPPDATA%\ProjectToolkit\pool.json` | With `-Pool`, the pool file. |
| `-UsePublished` | off | Deploy the `scheduler.next` already there instead of publishing again. |
| `-StartWindowSeconds` | 30 | How long the new scheduler has to stay up before it is rolled back. |

### Examples

```powershell
.\bin\deploy-scheduler.ps1                 # publish and swap, keeping the mode it was in
.\bin\deploy-scheduler.ps1 -Pool           # always start as a pool
.\bin\deploy-scheduler.ps1 -UsePublished   # swap in the build already published
```

The messages it prints, in order:

```
scheduler: <Kit>\out\windows\scheduler is <old version>
  publishing ... into <Kit>\out\windows\scheduler.next; the running scheduler is left alone
  ending the scheduler (pid 1234); its workers keep running
  starting the new scheduler from <Kit>\out\windows\scheduler\scheduler-next.exe
  the build it replaced is kept in <Kit>\out\windows\scheduler.prev
scheduler: <new version> replaced <old version> in <Kit>\out\windows\scheduler (pid 5678)
```

### What it changes on disk

Only under the toolkit's `out\windows\`: `scheduler.next` (the new build), `scheduler` (the live one, replaced) and
`scheduler.prev` (what it replaced). It ends and restarts the scheduler and, if a tray was running over it, the tray. Exit
code 0 means the new scheduler is running; 1 means it was rolled back.

### When to run it

Normally you run `build-and-deploy.ps1`, which calls this. Run it directly when `main` is already current and you only want
the swap, or with `-UsePublished` to retry a swap.

---

## init-project.ps1

An old name. It prints `init-project.ps1 is now projecttoolkit.ps1 -Action ...` and forwards every parameter (`-Action`
defaults to `new-project`). It exists only so old instructions that name it still work. Use
[`projecttoolkit.ps1`](#projecttoolkitps1) instead; it changes exactly what that script changes.

---

## test-projecttoolkit.ps1

**What it is for.** A self-test of `projecttoolkit.ps1`. It sets up scratch repositories in a temp folder with a temporary
`LOCALAPPDATA`, and checks that `categories.json` is written, `workers.json` is not, the pool join happens once, and so on. It
prints `ok` or `FAIL` for each check and exits 0 only when all pass. It takes no parameters.

```powershell
.\bin\test-projecttoolkit.ps1
```

It touches nothing outside its temp folder, so your real pool file is safe. **When to run it:** after you change
`projecttoolkit.ps1`.

---

## startup-gate.ps1

**What it is for.** Guards the scheduler's start time. It builds a benchmark that writes a pool of generated projects (three of
300 cards and one of 2,000) to a temp folder, starts the built scheduler on it three times, and takes the median time until
the page answers and until every project has joined. It fails when a median is more than 50% **and** more than 2 seconds over
`test\perf\startup-baseline.json`; a failure is measured once more before it counts.

### Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `-Record` | off | Write the baseline from this run instead of comparing. Do this deliberately, in a card that makes startup faster or accepts a slower one. |
| `-Exe` | `build\bin\Scheduler.Host\debug\scheduler-next.exe` | The scheduler to time. Build the solution first. |
| `-Runs` | 3 | Starts per measurement. |
| `-Quick` | off | A small pool, to try the script. Never compared or recorded. |

### Examples

```powershell
dotnet build code\src\SchedulerNext.slnx -warnaserror -m:4
.\bin\startup-gate.ps1 -Quick       # try it
.\bin\startup-gate.ps1              # the real gate
.\bin\startup-gate.ps1 -Record      # accept this PC's numbers as the new baseline
```

The output line to look for is `startup gate: medians of 3: page X s, every project joined Y s`. Exit code 0 means passed (or
recorded), 1 too slow, 2 the benchmark could not run (for example, the solution is not built).

**What it changes.** Temp folders, and `test\perf\startup-baseline.json` with `-Record`. It uses its own port and pool name and
does not touch your live pool. **When to run it.** After a change that could slow the scheduler's start.

---

## git-baseline.ps1

> **Developer tool.** Measurement for people working on the scheduler's code. You do not need it to use the toolkit.

**What it is for.** Counts how many processes, and how much time, a test run or a scheduler job costs, so a later change can be
compared before and after. It counts `git` starts exactly (a `git.exe` shim is put first on `PATH`) and other processes by
snapshotting the process table every 10 ms, so very short ones can be missed: those counts are a lower bound.

### Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `-Part` | `All` | `Tests`, `Scheduler`, `All`, or `Report` (only re-render the markdown). |
| `-Projects` | `Core,Web,Tray,Host` | Test projects for the Tests part. |
| `-Runs` | 3 | Timed runs per project or scenario. |
| `-Serial` | off | One test at a time, so each start belongs to one test (slow for Host). |
| `-Locks` | `Both` | `Both`, `Off` or `On`: which scheduler sessions to measure. |
| `-CountedMinutes` | 25 | Longest a counted test run may take. |
| `-Exe` | `build\bin\Scheduler.Host\debug\scheduler-next.exe` | The scheduler to measure. |
| `-Json` | `test\perf\git-baseline.json` | Where the numbers go. |

### Examples

```powershell
.\bin\git-baseline.ps1 -Part Report                          # redraw the tables from the saved numbers
.\bin\git-baseline.ps1 -Part Tests -Projects Core -Runs 1    # one quick project
.\bin\git-baseline.ps1                                       # everything (long)
```

**What it changes.** `test\perf\git-baseline.json` and `docs\perf\git-baseline.md`, plus temp folders; it builds the shim and
watcher under `build\` when missing. **When to run it.** On a quiet PC, before and after a change meant to cut process starts.
Nothing else should run beside it.

---

## test-profile.ps1

> **Developer tool.** Measurement; not needed to use the toolkit.

**What it is for.** Profiles the test suites: wall time per project (Core, Web, Tray, Host), pass, fail and skip counts, and
the slowest tests. For Host it then re-runs the slowest tests one at a time and counts the processes each starts. It does not
build; run `dotnet build` first.

| Parameter | Default | Meaning |
|---|---|---|
| `-Projects` | `Core,Web,Tray,Host` | Which test projects. |
| `-Top` | 30 | How many slow tests to list. |
| `-RunMinutes` | 25 | Longest one run may take. |
| `-NoCount` | off | Skip the process-counting part. |
| `-Json` | `build\test-profile\test-profile.json` | Where the numbers go. |
| `-FromDir` | none | Analyse an earlier run's temp folder instead of running again. |

```powershell
dotnet build code\src\SchedulerNext.slnx -warnaserror -m:4
.\bin\test-profile.ps1 -Projects Core,Web      # no Host, so no counting
.\bin\test-profile.ps1 -Top 10 -NoCount
```

**What it changes.** `build\test-profile\test-profile.json` and temp folders. **When to run it.** When the tests feel slow
and you want to know which to fix. Run it with nothing else going on.

---

## process-start-check.ps1

> **Developer tool.** A health check for the PC, not part of the toolkit.

**What it is for.** Measures how fast this PC starts programs, in starts per second. Each round starts 8 separate `cmd.exe`
processes (hidden), each starting `whoami.exe` 50 times; the rate is the 400 starts over the round's time. The loops are
separate processes on purpose: PowerShell and .NET start programs one at a time inside one process, so threads would
measure that queue instead of Windows.

| Parameter | Default | Meaning |
|---|---|---|
| `-Rounds` | 5 | Rounds; the median is reported. |
| `-Loops` | 8 | Processes started side by side per round. |
| `-StartsPerLoop` | 50 | `whoami.exe` starts per process. |

```powershell
.\bin\process-start-check.ps1
.\bin\process-start-check.ps1 -Rounds 2 -Loops 4 -StartsPerLoop 20     # a quick try
```

```
23:43:35: round 1: 31 starts/s
23:43:39: round 2: 49 starts/s
median: 40 starts/s
```

For reference, on the toolkit developer's 16-core PC: about 470/s idle, about 260/s with two workers running, 20-70/s when the
PC was unhealthy. **It writes nothing to disk.** Measure with the scheduler idle if you want numbers you can compare.

---

## live-stream-measure.ps1

> **Developer tool.** Measures the scheduler's live page; you do not need it to use the toolkit.

**What it is for.** Drives the page server of a **running** pool the way a browser would and reports, per scenario, the
scheduler process's CPU, requests and frames per second, and the pool's tick times. Scenarios: `none` (no page open),
`stream1` and `stream3` (one or three pages on the live stream), `poll1` and `poll3` (the polling fallback). It starts no pool
and moves no card.

| Parameter | Default | Meaning |
|---|---|---|
| `-Action` | `measure` | `measure`, or `report` to turn a saved CSV into a markdown table. |
| `-Port` | 0 (read from the repo's `jobs\scheduler.json`) | The pool's page server port. |
| `-Repo` | `C:\Projects\ProjectToolkit` | Where to read the port from. |
| `-PoolLog` | the pool's shared log under `%LOCALAPPDATA%\ProjectToolkit\pool\` | Where the "tick took" lines are read. |
| `-Scenarios` | `none,stream1,stream3,poll1` | Which to run. |
| `-Seconds` | 120 | Length of each scenario. |
| `-PollSeconds` | 2 | Poll interval for the poll scenarios. |
| `-ProcessName` | `scheduler-next` | The process to weigh. |
| `-Csv` | `%TEMP%\live-stream-<time>.csv` | Where the rows are written. |
| `-TimeoutSec` | 0 (no cap) | Give up after this many seconds. |

```powershell
.\bin\live-stream-measure.ps1 -Scenarios none,stream1 -Seconds 60
.\bin\live-stream-measure.ps1 -Action report -Csv C:\Temp\live-stream-20261122-120000.csv
```

**What it changes.** Only the CSV file. **When to run it.** After a change to the live page or stream, with the pool running.
Reference readings are in [`docs/perf/live-stream.md`](../perf/live-stream.md).
