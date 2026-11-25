# scheduler-next.exe: the command line

[Guide index](README.md) · the program itself is described in [switchyard.md](switchyard.md);
the rules it implements are in the design documents linked below and in
[spec.md](../design/tdd/scheduler/spec.md). This page is about **how to run it from a terminal**.

`scheduler-next.exe` is the scheduler of Switchyard ([switchyard.md](switchyard.md)): the program
that watches a project's job
queue, starts AI workers on the ready cards, serves the status page, and moves cards along. Most of
the time you never type its command line — the tray icon starts it for you, and workers are started
by the scheduler, not by hand. You reach for this page when you want to start a scheduler yourself,
watch what a new project's queue would do before it runs anything, or understand the two small
commands (`lock`, `check-push`) that a running worker calls.

The examples use the installed kit's copy:
`%LOCALAPPDATA%\Programs\ProjectToolkit\out\windows\scheduler\scheduler-next.exe` (a kit built from a
clone has it under the clone's `out\windows\scheduler`).

If you run it from that folder, plain `scheduler-next` works the same. Every argument form below was
run against the build at the time of writing and its real output is quoted.

`--help` (or `-h`, `-?`, `/?`, in any case, anywhere among the arguments) prints the usage and exits with
code 0, before anything else is done. Any other unknown switch is ignored. A bad start prints one line
saying what is missing and exits with code 2. The forms it accepts are:

```
usage: scheduler-next <repository> [-Config <file>] [-Slots n] [-Port n] [-Model m] [-NoHealthCheck] [-NoBrowser] [-Paused] [-DryRun]
       scheduler-next -Pool [<pool file>] [-Slots n] [-Port n] [-NoHealthCheck] [-NoBrowser] [-Paused] [-DryRun]
```

Other commands and switches, used by the tray and by workers, are not in that usage text; they are
each covered below.

## Before you start a scheduler: what it needs

The scheduler runs **one project**, identified by a git repository. From the repository it reads
(the rules are CFG-1 to CFG-7 in [spec.md](../design/tdd/scheduler/spec.md)):

| What | Where | If it is missing |
|---|---|---|
| The repository | the first plain argument, `-Repo <folder>`, or the current directory (each resolved up to the git top level) | exit 2: `Name the project's repository: scheduler-next <folder> (or run it from inside the repository, or pass -Config).` |
| The config | `-Config <file>`, else `<repo>\jobs\scheduler.json`, else `<repo>\docs\jobs\scheduler.json` | exit 2, and the message tells you to copy `ProjectToolkit\assets\templates\jobs\scheduler.json` |
| The queue | the `jobs/` folders (`queue/`, `active/`, …) named by the config's `jobsDir` | the scheduler runs with an empty queue |
| Worker types | `jobs/workers.json` **on main** (a single project only; a pool takes them from the pool file) | no worker type exists, so no card is ever dispatched |

A folder that is not inside a git repository is refused at once:

```
> scheduler-next C:\somewhere\not-a-repo
C:\somewhere\not-a-repo is not inside a git repository.
```

To set a project up properly, use
`& "$env:LOCALAPPDATA\Programs\ProjectToolkit\bin\projecttoolkit.ps1" -Action jobs` (see the
[scripts guide](scripts.md)) — it copies every template with the right folders. To decide **what**
to queue, ask the LLM planner: that is the [planner guide](planner.md).

Once it starts, the scheduler serves its page at `http://localhost:<port>/` (port 8765 unless the
config's `port` or `-Port` says otherwise), writes its log to `<logDir>\scheduler.log` (default
`%LOCALAPPDATA%\<project>\scheduler`, CFG-6) and a copy of every line to dated files under
`<repo>\jobs\.logs`. One scheduler per project is allowed: a second start of the same project waits
60 seconds for the first's mutex and then fails with

```
Another <project> scheduler is still running
```

and exit code 1 (CFG-14). Everything the scheduler does while running — picking cards, health
checks, retries — you watch and control on the page, not on the command line.

## Run one project

```
> scheduler-next C:\Projects\MyProject
```

That runs one project. The normal way, and the tray's, is a pool (below). Run from inside the
repository even the folder can be left out. Switches change that one run without touching the config
file; names match ignoring case, and
a value may be glued on (`-Slots:4`) or follow as its own argument. An unrecognised switch is
ignored, not an error (CFG-7).

| Switch | Does |
|---|---|
| `-Repo <folder>` | The repository, instead of the positional argument or the current directory. |
| `-Config <file>` | Use this `scheduler.json` as it lies, with no re-read from main (CFG-3). Useful for trying a config change. |
| `-Slots n` | How many workers at once; without it, `workers.json`'s `slots` (or its pool's `maxWorkers`), else `scheduler.json`'s `slots`, else 2. A non-number counts as not given. Pins the count so the page cannot resize it. |
| `-Port n` | The page's port, over the config's `port` (default 8765). A zero counts as not given. |
| `-Model m` | The default worker model, over the config's `worker.model`. |
| `-NoHealthCheck` | Skip the health check (the config's `health.command` is treated as empty), so no check worktree is made. |
| `-NoBrowser` | Do not open the page in a browser at start (the tray passes this; the tray's own double-click is the way in). |
| `-Paused` | Start with dispatching paused, like pressing Pause on the page: the scheduler watches but takes no cards. |
| `-DryRun` | Decide, never start: every worker start is only logged as a `DRY RUN` line, and the scheduler exits after the first tick (DISP-21). |

A first run tells you all you need to know about a project. This is real output from a small demo
project with two workers and one ready card, started with `-DryRun -NoBrowser -NoHealthCheck`:

```
23:55:21 Config: jobs/scheduler.json from origin/main
23:55:22 startup: git: C:\Program Files\Git\mingw64\bin\git.exe (found beside the cmd wrapper)
23:55:25 accounting: 0 earlier run(s) found
23:55:25 scheduler-next <version> started for myproject: 2 slot(s), page http://localhost:8765/ (dry run)
23:55:25 main model: C:\Projects\MyProject: in process, 1 open card(s) at ac5a40d in 106 ms
23:55:26 DRY RUN spark in slot 1 <- 1001-A-hello
23:55:26 tick took 1.51s (1 tick(s) since the last such line, longest 1.51s); slowest step ReadCards 1.46s; git 1, model 0.0ms; woken by Poll 1.
```

The first line shows CFG-3: without `-Config` the scheduler re-reads `scheduler.json` from
`origin/main`, because its checkout may sit behind what was pushed. The `DRY RUN` line is the
dry run answering "what would you work on?" without starting anything — the way to sanity-check a
queue, a `workers.json` change or a category split before letting the scheduler act on it. (A card
is dispatched only when its category is in `jobs/categories.json`, every `Depends on` card is in
`done/` (a card in `review/` does not release its dependents), and a worker type that is up and has a free slot takes it. If no `DRY RUN`
line appears, one of those is why.)

That `1001-A-hello` line refers to this card, sitting in `jobs/queue/1001-A-hello.md`:

```markdown
# 1001-A: Add a hello function

Category: A
Status: ready
Depends on: none
Locks: none
Plan: none (developer, 2026-11-22)

## Task

Add src/hello.c with a hello() that returns 1.

## Done when

The build passes and hello() returns 1.
```

When you drop `-DryRun` and the scheduler really starts a worker on it, the same file appears in
`jobs/active/` with one line added under `Status:` — the scheduler is the only writer of card
moves (CM-9 in [card-moves.md](../design/tdd/scheduler/card-moves.md)), so a worker never relabels
its own card:

```markdown
# 1001-A: Add a hello function

Category: A
Status: ready
Claimed: 2026-11-22, lane A
```

The worker then works inside the card's `Scope`, journals into `## Result`, and asks the scheduler
to move the card by writing a request file; the page is where you watch it. The queue's own rules
are [jobs/README.md](../../jobs/README.md).

Exit codes for any run: **0** ended cleanly, **1** could not start (for example the mutex above),
**2** bad command line or configuration.

## Run a pool of projects

```
> scheduler-next -Pool
> scheduler-next -Pool C:\Configs\pool.json -Slots 4 -NoBrowser
```

`-Pool` runs every project named in one pool file in a single process, with one page for all of
them (the design is [multi-project.md](../design/tdd/scheduler/multi-project.md); the rules are
MP-*). The pool file defaults to `%LOCALAPPDATA%\ProjectToolkit\pool.json`. Two differences are
worth knowing:

- `-Pool` takes **no repository argument and no `-Config`** — the pool file names the projects. You
  get exit 2 and `-Pool takes no repository and no -Config: the pool file names the projects.`
  if you pass either.
- The worker types come from the **pool file**, not from each project's `workers.json` (MP-C4); the
  log says so: `workers.json is not read in a pool: the worker types are the pool file's`.

`-Slots`, `-NoHealthCheck`, `-Paused` and `-DryRun` apply to every project; `-Port` is the pool's
own page. `-PoolName:<name>` names the pool's single-instance mutex, so a second pool (a test, a
benchmark) can run while the live one does. A missing or bad pool file is exit 2:

```
pool file: C:\Configs\pool.json cannot be read (Could not find a part of the path 'C:\Configs\pool.json'.); no pool
No pool to run: C:\Configs\pool.json must be a JSON file naming at least one project.
```

## The worker's commands

A running worker calls these itself; its prompt names the exact commands. They are documented here
so you can see and explain what the worker is doing — and run them by hand while debugging. Neither
starts a scheduler; both talk to the scheduler only through files under its log folder. The full
design is [dynamic-locks.md](../design/tdd/scheduler/dynamic-locks.md).

### `lock`: ask for a file outside your card's scope

```
> scheduler-next lock docs/guide/setup.md --why "the guide must link the new page"
granted
```

Exit **0** means go ahead and edit. The command works when the scheduler started the worker, because
it identifies the card by the environment the scheduler sets (`PTK_CARD`, plus `PTK_LOG_DIR` and
`PTK_SLOT`); run from any other shell it refuses (exit **4**):

```
refused: unknown card: PTK_CARD is not set (run it from a worker the scheduler started)
```

What happens inside one call, from a real run against a scratch log folder:

1. The worker writes a request into `<logDir>\moves\`, timed and slotted
   (`20261122-235601-918-slot1.lock`), naming the card, slot, paths and reason:

   ```
   Card: 1001-A-hello
   Slot: 1
   Paths:
     src/hello.c
   Why: need it to add hello()
   ```

2. The scheduler answers in order in `<logDir>\moves\replies\` with the same name and `.reply`.
   The worker's call waits up to 110 seconds, printing a line every 30.
3. The worker prints the answer and exits with its code:

| Exit | Last line says | The worker then |
|---|---|---|
| 0 | `granted` | edits the files. |
| 2 | `waiting: held by <card>, gives up at <time>` (or `waiting: no answer yet; run the same command again`) | runs the same command again; it keeps the open request and waits on. |
| 3 | `yield: <path> held by <card> (older)` or `(wait limit)` | commits to `wip/<card>`, notes where it stopped in the card's Result, hands the card back, and ends. |
| 4 | `refused: <reason>` | asks for fewer files (the exact ones, never a whole category or the repository root), or parks the card and asks the developer. |

Bad arguments are refused at once, exit 4: `refused: no paths: lock <path> [<path> ...] --why "<reason>"`.

### `check-push`: the pre-push hook's gate

```
> scheduler-next check-push [remote [url]]
```

You do not run this — git does, through the `pre-push` hook the scheduler installs (the worker's
environment sets git's `core.hooksPath` to `<logDir>\hooks` (`GIT_CONFIG_*` variables) when locks are
on; the repository's own config is untouched). For each ref pushed to **main**, it lists the files the
push changes and refuses any the card does not hold (its scope, what its `Locks:` line names, and the
shared add-only files), with the line that tells the worker how to ask:

```
1001-A-hello does not hold docs/guide/setup.md; ask: C:\...\scheduler-next.exe lock docs/guide/setup.md --why "<one line>"
```

It also refuses deleting main, pushing while main has moved on since the last pull, and a version
number that is not above main's (DL-15 to DL-18; the version rule is in the queue README). Pushes to
`wip/<card>` branches are never refused. Exit **0** lets the push through, **1** refuses it (and
prints `check-push: push refused; push your work to wip/<card> instead if you cannot get the lock`);
with no `PTK_CARD` (a human pushing by hand) it checks nothing and exits 0.

## Two more commands

Neither is for typing by hand: `--version` is for scripts, `--deliver-message` for the workers' agents.

- `--version` prints the build's version and exits 0: `<Major>.<Minor>.<Rev>`.
- `--deliver-message <claude|copilot|text>` is the agent hook the scheduler installs in each Claude
  worker (WRK-14). After every tool call the agent runs it; if the developer typed a message on the
  page (the worker's `PTK_MSG_FILE`), it hands the text to the model and moves the file to its
  `.read.txt` name. It prints the agent's reply with the message when one is waiting, nothing
  otherwise, and always exits 0, so it cannot break a worker.
- Tray-only switches on a normal run: `-ShutdownEvent:<name>` (an event the tray sets to ask this
  scheduler to end by itself, so no Ctrl+C is shared with workers), `-Replaces:<version>` (logged
  when a deploy started this build in place of that one), and `-NoBrowser` above.

## There is no `plan-fixup` command

The design [views.md](../design/tdd/scheduler/views.md) (rule VW-17) describes a one-time
`scheduler-next.exe plan-fixup --project <name>` that lists cards whose `Plan:` line names a moved
or missing plan file, with `--apply` to fix them. **It is not implemented**; there is no such
command in the executable. Fix a card's `Plan:` line by hand or through the planner
([planner guide](planner.md)).

## Where to go next

- The status page and its buttons: [switchyard.md](switchyard.md) and
  [card-moves.md](../design/tdd/scheduler/card-moves.md).
- Setting a project's queue up: [jobs/README.md](../../jobs/README.md) and the
  [scripts guide](scripts.md).
- Deciding what to work on, by LLM: [planner.md](planner.md).
