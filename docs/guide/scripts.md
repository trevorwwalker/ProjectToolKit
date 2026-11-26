# The scripts in `bin/`

ProjectToolkit comes with fifteen PowerShell scripts in its `bin/` folder (plus `publish-release.ps1` itself, which builds the
releases and calls two of the fifteen, `template-history.ps1` and `test-installer.ps1`; see their sections near the last
one). This page says
what each one is for, how to call it, what it changes on disk, and when you would run it. Only the first five are in the
release branch, and the installer leaves out `build-and-deploy.ps1` and `deploy-scheduler.ps1` too (an installed kit has no
source to build; it updates with the tray's **Check for update...**): the rest are developer-only, kept out of every release by
`publish-release.ps1`'s exclude list.

New to the toolkit? Start with the [guide index](README.md). Most people only ever need the first three scripts; the rest are
checks and measurements for people working on the toolkit itself.

| Script | Who it is for | In one line |
|---|---|---|
| [`projecttoolkit.ps1`](#projecttoolkitps1) | everyone | Start a new project, or add or refresh a job queue in one you have. |
| [`build-and-deploy.ps1`](#build-and-deployps1) | a clone of the toolkit | Bring the toolkit up to date and restart the scheduler. |
| [`deploy-scheduler.ps1`](#deploy-schedulerps1) | a clone of the toolkit (`build-and-deploy` calls it) | Swap in a new scheduler build without stopping the workers. |
| [`init-project.ps1`](#init-projectps1) | old instructions only | An old name that forwards to `projecttoolkit.ps1`. |
| [`test-projecttoolkit.ps1`](#test-projecttoolkitps1) | toolkit developers | Checks that `projecttoolkit.ps1` still works. |
| [`startup-gate.ps1`](#startup-gateps1) | toolkit developers, not released | Fails if the scheduler's start got clearly slower. |
| [`git-baseline.ps1`](#git-baselineps1) | **developer tool**, not released | Counts the processes and time a test run or scheduler job costs. |
| [`test-profile.ps1`](#test-profileps1) | **developer tool**, not released | Finds the slowest tests. |
| [`process-start-check.ps1`](#process-start-checkps1) | **developer tool**, not released | Measures how fast this PC starts programs. |
| [`process-start-dose.ps1`](#process-start-doseps1) | **developer tool**, not released | Finds which kind of work lowers this PC's process-start rate. |
| [`paged-pool-probe.ps1`](#paged-pool-probeps1) | **developer tool**, not released | Finds which operation of a test run leaves kernel paged pool behind. |
| [`pool-tags.ps1`](#pool-tagsps1) | **developer tool**, not released | Reads the kernel pool by tag; no poolmon needed. |
| [`live-stream-measure.ps1`](#live-stream-measureps1) | **developer tool**, not released | Measures what the scheduler's live page costs. |
| [`template-history.ps1`](#template-historyps1) | **developer tool**, not released | Writes the template manifest `publish-release.ps1` puts in a release. |
| [`test-installer.ps1`](#test-installerps1) | **developer tool**, not released | Installs and checks a built `ProjectToolkit-Setup.exe`. |

## Before you start

- Call each script by its full path in the kit's `bin` folder (an install puts the kit in `%LOCALAPPDATA%\Programs\ProjectToolkit`;
  a clone is wherever you cloned it). The examples write `.\bin\...` from the kit's folder. Each script finds the kit from its
  own location, so the full path works from anywhere.
- Most scripts have built-in help: `Get-Help .\bin\projecttoolkit.ps1 -Full` prints the parameters and examples.
  Only `live-stream-measure.ps1` keeps its notes in comments at the top of the file instead.
- **Switchyard** (the scheduler, `scheduler-next.exe`, with its tray and page) is the program that hands job cards to AI
  workers. The **planner** is the AI session you plan with: you
  describe what you want and it writes plans and cards. Wherever this page says "ask your planner", the
  [planner guide](planner.md) shows how.

---

## projecttoolkit.ps1

**What it is for.** The setup script. It does three independent jobs, chosen with `-Action`:

- `new-project` makes a new project: the standard folder layout, a `README.md`, `.gitignore`, `.gitattributes`, a conventions
  file, the planner brief (`docs/planner-brief.md`) and the two design documents to fill in, and then it does the second job too.
- `new-library` makes a new **library project**: the same setup with the cut-down layout for a repository of static
  libraries that other projects nest at `code/extern/<repository>/` (`bin/`, `code/lib/`, `code/tools/`, `test/unit/`,
  `docs/design/`, `out/`; no `assets/`, `code/src/`, `code/extern/`, `extern/`, `test/integration/` or `test/data/`), the
  library variants of `README.md` and `docs/conventions.md`, and one default category (`A=library:code/,test/`). See
  [Library projects](library-projects.md).
- `jobs` sets up the **job queue** alone: the `jobs/` folders (`queue`, `active`, `review`, `done`, `plans`, `archive`,
  `requests`), the rules every worker reads (`jobs/README.md`), and the project's settings files. Use it to add a queue to a
  repository you already have, or with `-Update` to pull in newer rules from the toolkit.

**Why.** Workers and the scheduler expect one layout. The script lays it down so you do not have to remember it, and
`-Update` carries a fix to the rules into every project.

### Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `-Action` | (required) | `new-project`, `new-library` or `jobs`. |
| `-Repo` | (required) | The project's folder. Created if it does not exist, and `git init`-ed on branch `main` if it is not a git repository yet. |
| `-Project` | the name in `jobs/scheduler.json`, else the folder name | The project's name. |
| `-Categories` | `A=core:code/src/,test/` | The kinds of work, as `LETTER=name:path,path;LETTER=name:path`. Each category owns its paths, so two workers never edit the same file. One is fine to start with. Used only when `categories.json` is first written. |
| `-Modules` | none | Comma-separated folder names to create under `code/src/` (`new-project` only). Leave it out until your design names the modules. |
| `-Gate` | none | The commands a worker must pass before every push (build, test). Several strings, or one string with `;;` between commands. `{jobs}` becomes the thread count the scheduler allows. |
| `-Model` | none | The worker model, with `-Endpoint`. Left out, Copilot uses GitHub's own models. |
| `-Endpoint` | none | A local OpenAI-compatible server's `/v1` address, for example `http://localhost:18400/v1`. Left out, no server is needed. |
| `-JobsEnvVar` | `AGENT_JOBS` | The environment variable that tells a worker how many threads it may use. |
| `-Force` | off | Overwrite files that already exist. Without it nothing already there is touched. |
| `-Update` | off | With `-Action jobs`: refresh the kit's own text and fill in anything missing. |
| `-JoinPool` / `-NoPool` | joins | By default the last step adds the project to the scheduler's pool file. `-NoPool` skips that. `-JoinPool` forces the join even where it is skipped by itself (below), but not without `jobs/scheduler.json`. |
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

The script ends with a numbered list of what to do next. In short: describe the project, write the design, have your planner
write the project's plan in `jobs/plans/` (see the [planner guide](planner.md)), commit and push to `main`, set up the workers
and start Switchyard (or, if it already runs, it picks the project up at its next tick), then have the planner card the plan's
first step.

**2. Add a queue to a repository you already have.**

```powershell
.\bin\projecttoolkit.ps1 -Action jobs -Repo C:\Projects\MyExistingApp
```

Only the queue and `docs/known_gaps.md` are added. Nothing already there is overwritten.

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
templates, .gitattributes) and filled in any folder or file it was missing.
Left alone, because they are the project's own now: README.md, .gitignore, docs/conventions.md,
docs/design/, jobs/{project,categories,lanes,workers,scheduler}.json.
```

`same` means the file already matched the kit; `kept` means it is yours and was left alone. It is safe to repeat.

**4. A new library project.**

```powershell
.\bin\projecttoolkit.ps1 -Action new-library -Repo C:\Projects\MyLib -NoPool
```

The cut-down layout for a repository of static libraries (`-NoPool` keeps the experiment out of your real pool):
the library variants of `README.md` and `docs/conventions.md`, one default category, `A=library:code/,test/`, and
the same queue and pool join as `new-project`. How a host nests the result is in
[Library projects](library-projects.md).

### What it changes on disk

Only files under `-Repo`, plus `%LOCALAPPDATA%\ProjectToolkit\pool.json` for the pool join. It never overwrites a file that
exists unless you give `-Force`. With `-Update` it rewrites only the kit's own text: `jobs/README.md`, the card, plan and
request templates and `jobs/attachments/README.md` (and `.gitattributes`, with `-Action new-project` or `new-library`). Your `README.md`,
`.gitignore`, `docs/conventions.md`, `docs/design/`, `jobs/project.md`, `jobs/categories.json` and `jobs/scheduler.json` are
never touched by it.

### When to run it

Once when you start a project (or put a queue into an existing one), and again with `-Update` after the toolkit has changed
its job rules. Update the toolkit itself first (see [`build-and-deploy.ps1`](#build-and-deployps1)).

### A card, before and after

The queue is folders of Markdown cards. A card the planner writes sits in `jobs/queue/new-A-version-flag.md`; the scheduler
gives it a number:

```
# new-A: Add a --version flag

Category: A
Status: ready
Depends on: none
Locks: none
Plan: jobs/plans/<name>_plan.md, step 1

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
| `-Project` | the running scheduler's repository | With `-Single`, the project folder to serve. |

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

> **Toolkit developer tool**, not in the released toolkit: it guards the toolkit's own startup time against
> `test\perf\startup-baseline.json`.

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

> **Developer tool**, not in the released toolkit. Measurement for people working on the scheduler's code; you do not need
> it to use the toolkit.

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

> **Developer tool**, not in the released toolkit. Measurement; not needed to use the toolkit.

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

> **Developer tool**, not in the released toolkit. A health check for the PC, not part of the toolkit.

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

> **Developer tool**, not in the released toolkit. Measures the scheduler's live page; you do not need it to use the toolkit.

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

---

## process-start-dose.ps1

> **Developer tool**, not in the released toolkit. A PC-health experiment; you do not need it to use the toolkit.

**What it is for.** Finds which kind of work lowers this PC's process-start rate (card 1158-B). From a healthy start it
gives the PC measured doses - `whoami` starts, `git` starts, a build, test runs, waits - and reads the rate (three runs of
`process-start-check.ps1`) after each one, appending each reading as a JSON line to `<OutDir>\dose.jsonl` so an interrupted
experiment survives. Steps run one at a time (`-Step baseline|whoami|git|build|test1|fsmon|report...`); `report` prints the
recorded readings as markdown tables. **What it changes.** `out\process-start-dose\dose.jsonl`; step `build` rebuilds the
solution and step `fsmon` ends git's fsmonitor daemons. **When to run it.** When a PC's
process starts are slow and you want to know what work caused it. Reference: [`docs/perf/process-start-log.md`](../perf/process-start-log.md).

---

## paged-pool-probe.ps1

> **Developer tool**, not in the released toolkit. A PC-health experiment; you do not need it to use the toolkit.

**What it is for.** Finds which operation of a test run leaves kernel paged pool behind (card 1159-B). Run with pwsh (7) on a
quiet PC: each `-Probe` (drift, build, host:<Folder>, core, files, gitinit, gitfsmon, gitcommits, ...) reads
`Pool Paged/Nonpaged Bytes` and the ended-but-held process count, does ONE kind of work, reads again, waits a minute and
reads a third time, appending one JSON line to `<OutDir>\probe.jsonl`. Run every probe twice; a difference under 3 MB is
nothing. **What it changes.** Its `probe.jsonl` and `out\test-tmp` folders. **When to run it.** When a PC is short on paged
pool and you want the operation responsible.

---

## pool-tags.ps1

> **Developer tool**, not in the released toolkit. A PC-health experiment; you do not need it to use the toolkit.

**What it is for.** Reads the kernel pool by tag (`NtQuerySystemInformation` class 22), so no poolmon is needed (card
1160-B). Three uses: `-Snapshot <file>` writes the whole tag table plus the pool counters as JSON; `-Top <n> [-From file]`
prints the n largest tags by paged and by nonpaged bytes; `-Diff <before>,<after>` prints tags whose bytes changed by more
than 0.5 MB, largest first. It never elevates; a refused call reports itself and exits 3. **What it changes.** Only the
snapshot file you name. **When to run it.** To watch a suspected tag (for example `Toke`) while reproducing a pool leak.

---

## template-history.ps1

> **Developer tool**, not in the released toolkit. `publish-release.ps1` calls it when it builds a release; you
> do not need it to use the toolkit.

**What it is for.** Writes the manifest `assets\templates\template-history.json` for a commit: the kit's version and,
for every template under `assets\templates`, the git blob ids each of its versions has had on that commit's history
(`INS-17` in the installer's design). An installed kit has no `.git`, so `TemplateRefresh` cannot walk the kit's
history for the past versions of a template; the release carries them in this manifest instead, which lets it tell an
old copy of a template from one the project edited. The shape is
`{ "version": "<kit version>", "templates": { "<template path>": ["<git blob id>", ...] } }`: the version is the kit's
`<Version>` from `code/src/Directory.Build.props` at that commit, a template path is its path under `assets/templates`
with forward slashes (`jobs/README.md`), and the ids are git's own (`git hash-object`), newest first. Deleted
templates are not listed - only what the commit has - and the manifest never lists itself.

### Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `-Repo` | the kit's own folder | The git repository to read. It must have a `.git`; the script throws when it does not. |
| `-Commit` | `HEAD` | The commit whose history the manifest is written from. Any revision git accepts. |
| `-OutFile` | none | Where the JSON goes, with a BOM-less UTF-8 write so any PowerShell can read it. With no `-OutFile` the JSON is printed on the console and nothing is written. |
| `-Version` | read from the commit | The kit version to write in the manifest, instead of the `<Version>` in `code/src/Directory.Build.props` at that commit. |

### Examples

```powershell
.\bin\template-history.ps1                                    # the kit at HEAD, the JSON on the console
.\bin\template-history.ps1 -OutFile out\test-tmp\template-history.json
.\bin\template-history.ps1 -Commit <sha> -OutFile <stage>\assets\templates\template-history.json
```

The last is what a release does: `publish-release.ps1` passes the release commit, the staging folder's manifest path
and the version it is releasing, and refuses to commit or push when the script writes no manifest. It prints one line
when it writes a file: `template-history.json: <version>, <n> templates, <n> blob ids -> <OutFile>`.

**What it changes.** Only the file named by `-OutFile`, creating its folder when needed; nothing else on disk. It only
reads git. **When to run it.** You normally never do: `publish-release.ps1` runs it as part of building a release. Run
it by hand to check the manifest a release would carry, or when changing this script or the templates.

---

## test-installer.ps1

> **Developer tool**, not in the released toolkit. `publish-release.ps1` calls it to test the installer it just
> built; you do not need it to use the toolkit.

**What it is for.** Installs a test build of the installer silently into a scratch folder, checks the result, uninstalls
it, and checks that nothing else on the machine was touched (`INS-24`, section 10 of
`docs\design\tdd\installer.md`). `publish-release.ps1` runs it after Inno Setup has compiled the setup with `/DTest`, so
the test AppId keeps it from ever upgrading or removing a real install. Its five steps, in order:

1. install: `Setup /VERYSILENT /SUPPRESSMSGBOXES /CURRENTUSER /NOICONS /TASKS="" /DIR=<scratch>\app`;
2. check the files: both exes, `assets\templates`, `template-history.json`, `bin\projecttoolkit.ps1`, `LICENSE`, the
   uninstaller; no pdb files and no `code\`;
3. run the installed `bin\projecttoolkit.ps1 -Action new-project -NoPool` into a scratch repository, which proves the
   templates are found;
4. uninstall silently with the `unins000.exe` beside the install;
5. check the install folder is gone, `%LOCALAPPDATA%\ProjectToolkit\pool.json` is as it was, no process runs from the
   scratch folder, and - when you pass `-AllowRunningSwitchyard` - every Switchyard process running beforehand is still
   running with the same start time.

Each check prints `ok` or `FAIL`. **Exit codes:** 0 when every check passed, 1 otherwise (each failed check is printed,
and so is the scratch folder, which is then kept). It refuses to start, with exit 1, while a `scheduler-next.exe` or
`scheduler-tray.exe` is running unless you pass `-AllowRunningSwitchyard` (card 1258-B, card 1266-B: the uninstall it
ends with runs `-StopForUpdate`), and likewise when an earlier test install is still registered at `-TestUninstallKey`.

### Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `-Setup` | none | The compiled test installer to install (an `.exe` built with `/DTest`). Required, and the script throws when the file is not there. |
| `-Scratch` | a new folder under `$env:TEMP` | The folder to work in. It must not exist or must be empty; the install goes in `<scratch>\app`, the scratch repository in `<scratch>\repo`, the installer's log in `<scratch>\setup.log`. |
| `-KeepScratch` | off | Leave the scratch folder in place so you can look at a failure. Without it the folder is removed when every check passed; a failing run always keeps it. |
| `-AllowRunningSwitchyard` | off | Run beside a Switchyard started from another folder, and prove it is left alone: the id, start time and path of each such process are recorded before the install and compared after the uninstall. Without the switch the test refuses to start (exit 1) while one runs. |
| `-TestUninstallKey` | the `/DTest` AppId's key under `HKCU` | The registry key of the test AppId's uninstall entry: `HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7E1B5C0A-2D44-4B6F-9A3E-5C1D8F20A6B1}_is1`. When it exists - an earlier test install is still registered, whose old `-StopForUpdate` the new setup would run - the test refuses (exit 1) and says which key, with the uninstaller to run instead. |
| `-SelfTest` | off | Install nothing: run only the survivors comparison on `-SelfTestBefore` and `-SelfTestAfter` and exit 0 when it passes, 1 otherwise. |
| `-SelfTestBefore` | empty | For `-SelfTest`: the processes running beforehand, as `id,start,path` items separated by `\|`. |
| `-SelfTestAfter` | empty | For `-SelfTest`: the processes running afterwards, in the same form. |

**Time limits.** The install has 180 seconds and the uninstall 120 seconds; a step that does not finish in its time is
reported as a failed check, and the script ends that process and everything running from the scratch folder. After the
uninstaller exits it allows up to 10 more seconds for the temporary copy of itself to remove the install folder.

### Examples

```powershell
.\bin\test-installer.ps1 -Setup $env:TEMP\ptk-setup\ProjectToolkit-Setup.exe
.\bin\test-installer.ps1 -Setup <setup> -AllowRunningSwitchyard    # beside a Switchyard you will not stop
.\bin\test-installer.ps1 -SelfTest -SelfTestBefore '<id>,<start>,<path>' -SelfTestAfter '<id>,<start>,<path>'
```

**What it changes.** Installs into `<scratch>\app` and uninstalls it again; writes the scratch folder, the scratch
repository and `setup.log` under it, and removes the scratch folder at the end unless a check failed or you passed
`-KeepScratch`. It never touches a real install (the test AppId) and it checks that your `pool.json` is unchanged.
**When to run it.** `publish-release.ps1` runs it on the installer of every release. Run it by hand after changing the
Inno Setup script, the installer's steps, or this script - build the test setup first.

---

## publish-release.ps1 (and what a release leaves out)

`publish-release.ps1` is itself developer-only: it takes a commit of `main`, copies the repository minus an exclude list into
the release branch, builds the scheduler and tray into the installer `ProjectToolkit-Setup.exe` (Inno Setup, tested by
`test-installer.ps1`), tags the commit and publishes the GitHub Release with the installer. The exclude list is the
rule for what ships: `jobs`, `docs/perf`, `test/perf/git-baseline.json`, and the eleven **developer tools** -
`startup-gate.ps1`, `git-baseline.ps1`, `test-profile.ps1`, `process-start-check.ps1`, `process-start-dose.ps1`,
`paged-pool-probe.ps1`, `pool-tags.ps1`, `live-stream-measure.ps1`, `template-history.ps1`, `test-installer.ps1` and
`publish-release.ps1` itself - stay in the toolkit repository only. None of them is called by a released script, by the build
or by the tests.
