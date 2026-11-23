# Best Practices for Building Clean-Sheet Software with AI Agents

This document describes a workflow for building a substantial piece of software from scratch, with AI models writing nearly all of the code. The human acts as architect and director. A **frontier model** plans, writes specifications and designs tests. A **base model**, the cheaper and faster coding agent, writes and debugs the code. The workflow depends on a tight, automated feedback loop: build, test, debug, fix, repeat.

> **If you are an LLM asked to start a new project with this document:** read it all, then follow
> section 11. It uses **ProjectToolkit** (the folder this document is in), which sets up
> the job queue, its rules and the scheduler in a new repository with one command, and is the
> source for updating them in existing projects.
>
> For a large or complex codebase, also read [agent-lessons.md](agent-lessons.md): what
> running workers on a real project taught about card size, briefs, people in the loop,
> measurement and the scheduler.

## Roles

| Role | Responsibilities |
|---|---|
| Human | Architecture, file layout, interface contracts, conventions, final review, deciding when something is "done", answering parked questions, the manual checks in `review/`, starting and pausing the scheduler |
| Frontier model (the planner) | System descriptions, prompt scaffolding, implementation plans, unit test plans, turning plans into job cards and spreading them across lanes so every worker has work, answering agents' requests, unblocking the base model when it stalls, maintaining the scheduler |
| Base model (worker) | Doing one job card per session: writing code, running builds and tests, using the debugger through MCP, fixing failures, recording what it did on the card |
| Scheduler (a script) | Dispatching cards to workers, keeping lanes apart, watching workers, checking that `main` stays green, showing status and controls |

## 1. File Layout

The file layout gets more of your personal effort than anything else, and it is the hardest thing to change later. Keep art and content assets separate from code so that each can evolve on its own. This is the layout **ProjectToolkit** seeds (section 11) and the one every card is worked under; a project keeps its own copy of the rules in `docs/conventions.md`, and that file, not this document, is what agent prompts point at.

```
project/
├── README.md        # What the project is, its honest status, its design in brief
├── .gitignore       # Build output, generated files, editor cruft (committed placeholders survive)
├── .gitattributes   # Text/binary and line-ending rules for the whole repository
├── assets/          # What the shipped application loads at runtime (art, audio, fonts, data)
├── bin/             # The project's own executables and scripts (not third-party)
├── build/           # Build intermediates (compiler output, the CMake build tree); git-ignored, delete at will
├── code/
│   ├── extern/      # Third-party SOURCE we compile into our build
│   ├── lib/         # Static libraries linked by both the application and the tools
│   ├── src/         # The application, one folder per module
│   └── tools/       # Source for custom tools (output goes to bin/)
├── docs/
│   ├── ai/          # New plans only, not yet turned into job cards
│   ├── design/
│   │   ├── gdd/     # Intent: what it must do
│   │   └── tdd/     # Architecture and technical specs: how
│   ├── reference/   # Technical reference material (see section 5)
│   ├── conventions.md   # Where files go and how code is written: the agent-facing authority
│   └── known_gaps.md    # Differences from the spec, and assumptions made where it is silent
├── extern/
│   └── bin/         # Prebuilt third-party binaries, SDKs, middleware
├── jobs/            # The job queue (see section 8)
│   ├── queue/       #   cards waiting to be taken
│   ├── active/      #   cards an agent is working
│   ├── review/      #   finished, waiting for the human's manual checks
│   ├── done/        #   finished and verified
│   ├── plans/       #   plans that have cards
│   ├── archive/     #   plans whose cards are all done
│   ├── requests/    #   agents' requests to the planner, one file each
│   └── README.md    #   the protocol every agent follows
├── out/             # Built packages and final results, one folder per platform; git-ignored
└── test/
    ├── unit/        # Unit tests, one file per module, mirroring the code/ paths
    ├── integration/ # Multi-system and end-to-end tests; they drive the real binary
    └── data/        # Test vectors, golden outputs, fixtures
```

- **assets/**: What the shipped application loads at runtime. A different product can replace this folder without touching code, and several asset sets can sit side by side sharing one executable. The numbers that define behaviour (balance, tuning, tables) live here as data files and exist nowhere else; generated blobs are outputs, never inputs.
- **bin/**: The project's own executables and scripts: setup and helper scripts, and the tools built from `code/tools/` with the support files they need at runtime. Scripts are committed; built binaries are not (the `.gitignore` excludes `*.exe`, `*.dll` and the like). Third-party executables go in `extern/bin/`.
- **build/**: Build intermediates only: compiler output, object files, the CMake build tree (`cmake -B build`). It is git-ignored and can be deleted at any time without loss. The build definition (a `CMakeLists.txt`, or a .NET solution with its `Directory.*.props`) lives in `code/` beside what it builds, `code/src/` for the application, and is the source of truth for how anything is compiled.
- **out/**: Built packages and final results, one folder per platform; git-ignored.
- **code/extern/**: Vendored third-party source that is compiled into the project, one directory per dependency, pinned to a version and noted in that directory's README (origin, version, license, local changes). Everything we do not compile ourselves goes in `extern/bin/` instead, and the two are never mixed.
- **code/lib/**: Static libraries linked by the application *and* the tools — logging, serialization, the debugger protocol. A folder is not a library: code used by only one side belongs with that side.
- **code/src/**: The application, one folder per module, with the interfaces written first. An interface header declares no OS call, handle, socket, clock, file, thread or graphics context. OS-specific code lives in `code/src/platform/<platform>/` with the shared part in `code/src/platform/common/`, and utilities used across modules in `code/src/systems/`.
- **code/tools/**: Source for internal tools such as asset converters, trace diffing tools and test harnesses. Anything that generates content is a tool here, not a manual edit of an asset.
- **docs/**: Everything the human and the AI need to understand the system.
  - **docs/design/gdd/** says what the project must do, **docs/design/tdd/** how it is built. A card cites the section it implements; when the two disagree, the design is corrected first.
  - **docs/ai/** is an inbox. A plan lives here only until the planner breaks it into job cards; then it moves to `jobs/plans/`, and to `jobs/archive/` when its last card is done. An empty `docs/ai/` means every plan is scheduled.
  - **docs/conventions.md** and **docs/known_gaps.md** are the two files every agent reads: the rules it works under, and the known departures from the specification.
- **extern/bin/**: Third-party tools and middleware used as-is. All third-party material lives either in this folder or in `code/extern/`.
- **test/**: Test code and test data. Unit tests mirror the `code/` paths, integration tests drive the real binary, and `test/data/` is referenced by path, never inlined; goldens are regenerated by an explicit command, never as a side effect of a failing test. Test plans have no folder of their own: they travel with the commit that implements the code they cover (section 6).
- Every kept folder that has no files yet carries a `.gitkeep`, deleted as soon as real files land in it. `out/` is git-ignored wholesale and keeps its `.gitkeep`; `build/` is git-ignored entirely and is not kept at all (the build creates it).

## 2. Initial Implementation (Human-Led Skeleton)

Build the skeleton of `code/src/` by hand, one folder per module (re-run `projecttoolkit.ps1 -Action new-project -Modules "app,platform,systems"` once the design has named them). **Do not write code at this stage.** Write the architecture, and keep it where the kit put it: the intent in `docs/design/gdd/`, the technical design in `docs/design/tdd/`.

1. **Create every file you expect to need**, with a header block that states:
   - the purpose of the file and what it is responsible for,
   - what it explicitly does not do,
   - the systems it depends on and the systems that depend on it,
   - pseudocode for the key functions and data flow.
2. **Write the interface headers first.** Public types, function signatures and ownership rules form the contract that the AI fills in. Interfaces that are vague at this stage produce inconsistent code later.
3. **Fill in `docs/conventions.md`.** ProjectToolkit seeds it with the layout rules above and leaves the project's own sections marked **fill in**: naming, formatting, error handling, logging, memory ownership, header and include rules, and comment style. Every agent prompt references this file. Add a formatter config (such as `.clang-format`) so that style is enforced by tooling rather than by memory.
4. **Iterate on the outline with the frontier model.** Ask it to find gaps, circular dependencies, unclear ownership and missing systems. Revise the outline until a new reader could implement any file from its description alone.

Then hand the outline to the coding agent:

- Let the AI write all of the code. When output drifts, correct it by adding detail to the file descriptions or to `conventions.md`, not by hand-editing code. A correction made in the specification carries forward; a hand edit is forgotten.
- **Get a clean build as early as possible.** Correct behavior is not the goal yet. Stubs that compile and run are enough. A warning-free build, with warnings treated as errors from day one, establishes the baseline that every later change is measured against.
- Commit often. Small, frequent commits give you cheap rollback when the agent goes in a bad direction.

## 3. Debugger Protocol

Once the framework compiles, build a debugging interface into the application **before** building features. The AI's ability to change code, run it and observe the result quickly is what makes the rest of the process work. Remove every obstacle that slows that cycle down.

### Requirements

- **A headless build.** The application must run without a window, audio or user input, so that an agent can drive it from scripts.
- **Determinism.** The same inputs must produce the same execution every time. Use seeded random number generators, a fixed time step and no wall-clock dependencies in core logic. Without determinism, bugs cannot be reproduced reliably, and the AI ends up chasing failures that never repeat.
- **Execution control**: run, pause, step, step over, step out, continue, and run until a condition (cycle N, frame N, or an address).
- **Breakpoints and watchpoints**: code breakpoints, memory read/write watchpoints, and conditional breaks.
- **State inspection**: read and write registers, memory, variables and object state. Dump the state of a subsystem in a readable form.
- **Snapshots**: save and restore full state, so that a failure can be reproduced from just before it occurs.
- **Tracing**: optional execution and event logs that can be diffed against a known-good reference. This is especially valuable when matching an existing system's behavior, as with emulators, protocol implementations or ports.
- **Structured output**: every response is machine-readable (JSON or similar) so that agents can parse it reliably.

### Transport

Expose the protocol over TCP. A listening socket is simple to add and allows any external tool to attach. Where an established protocol fits, use it. The GDB Remote Serial Protocol, for example, gives you existing debuggers, front ends and tooling for free. A standalone debugger UI is optional: the MCP layer (section 4) lets the LLM act as the debugger.

## 4. MCP Interface

Build an MCP server that connects to the debugger protocol. **The MCP server and the debugger together are the feedback loop** the AI uses to debug the entire application.

Typical tools:

| Tool | Purpose |
|---|---|
| `build` | Compile, and return errors and warnings in condensed form |
| `run_tests` | Run all tests or a filtered set, and return pass/fail with failure details |
| `launch` | Start the headless build with arguments, a snapshot or a test scenario |
| `break` / `watch` / `clear` | Manage breakpoints and watchpoints |
| `step` / `continue` / `run_until` | Control execution |
| `read_state` / `write_state` | Inspect or modify registers, memory and objects |
| `snapshot_save` / `snapshot_load` | Capture and restore state |
| `trace_diff` | Compare an execution trace against a reference trace and report the first divergence |
| `logs` | Fetch recent log output, filtered |

Design guidelines:

- **Keep output compact.** Raw dumps quickly consume the agent's context window. Summarize, truncate with clear markers, and allow the agent to request more detail.
- **Return errors the agent can act on**, for example "breakpoint address not mapped," not just "failed."
- **Make tools idempotent where possible**, so that a retry does no harm.

## 5. Reference Material

Give the AI a knowledge base. Collect technical documentation throughout the project in `docs/reference/`:

- specifications, hardware manuals, datasheets, protocol and file format documents,
- documentation for the third-party libraries you use,
- notes on known quirks and undocumented behavior as you discover them.

Convert the material to plain text or Markdown where possible, and keep an index file that describes what each document covers. Agents read text far more reliably than scanned PDFs. Ask the agent to cite the reference section in code comments when it implements behavior defined by a specification, so that future fixes can be checked against the source.

## 6. The Frontier Model's Role

Use the frontier model for work that depends on judgment and planning:

- **System descriptions**: write the intent in `docs/design/gdd/` and the architecture and technical detail in `docs/design/tdd/`, detailed enough to serve as direct instructions for the base model. A card cites the section it implements.
- **Prompt scaffolding**: maintain reusable prompts (as standing briefs in `jobs/project.md`) for recurring tasks such as implementing a module from its specification, triaging a failing test, or adding regression coverage.
- **Implementation plans**: break large systems into ordered steps that the base model can complete one at a time.
- **Job cards and scheduling**: turn each plan into cards in `jobs/queue/`, ordered so that two or more agents can work at once without touching the same files (section 8).
- **Unit test plans**: this is the frontier model's most important job.

### The Unit Test Plan

Make working with the frontier model a regular part of your workflow. It asks questions, you answer them, and together you make plans. For each system:

1. Describe in as much detail as possible the results you expect: inputs, outputs, side effects, timing and edge cases.
2. Have the frontier model write the test plan that covers normal behavior, boundary conditions, error paths and interactions with other systems. It travels with the commit that adds the tests - in the job card or the plan that asked for them - rather than in a folder of its own.
3. Where a reference implementation or a specification exists, generate test vectors and golden outputs in `test/data/`.
4. Measure coverage with gcov or llvm-cov. Aim for **100% branch coverage on critical systems**, and use the coverage report to find untested paths rather than treating it as the finish line.

Tests are the specification. A test written before or alongside the code turns "the code looks right" into "the code demonstrably behaves correctly."

## 7. The Feedback Loop

Combine the reference material, the base model and the debugger MCP tools into an autonomous fix cycle:

1. **Build.** Fix all compile errors and warnings.
2. **Run tests.** Collect the failures.
3. **Triage.** Pick one failure. Read the test, the relevant specification section and the code.
4. **Investigate.** Use the debugger tools: reproduce the failure, set breakpoints, inspect state, and diff traces against the reference.
5. **Fix.** Make the smallest change that addresses the root cause.
6. **Verify.** Re-run the failing test and then the full suite to catch regressions.
7. **Commit**, then repeat until the suite passes.

### Guardrails

- **The agent may not change tests to make them pass** without explicit human approval. Agents will "fix" a failure by weakening its assertion if that is allowed. Treat test changes as a separate, reviewed activity.
- **Escalate when stuck.** If the base model fails to fix an issue after a set number of attempts (for example, three), hand the problem and the investigation notes to the frontier model, or to the human.
- **One problem at a time.** Fixing several failures in a single change makes regressions hard to trace.
- **Use version control as an undo button.** When an agent's change makes things worse, revert it rather than layering further fixes on top.

### Continuous Integration

Run the build and the full test suite on every commit, either in CI or as a required build step. A change that breaks the suite is not finished.

## 8. The Job Queue and the Scheduler: Several Agents at Once

Once the feedback loop works, the limit is how much work you can hand out, not how fast an agent codes. A job queue lets you plan with the frontier model in one session while several base-model workers work through the results unattended. The queue is just files in the repository, so every state change is a commit. The scheduler is the only writer of card moves: it hands the cards out, and a worker asks for any move it wants with a move request in the scheduler's moves folder.

The design borrows directly from early operating systems, because the problem is the same one: a queue of jobs, a few processors, shared resources that must not be used by two jobs at once, and processes that crash or hang.

| OS idea | Here |
|---|---|
| Job | A card in `jobs/queue/` |
| Process | A worker: a fresh agent session that does exactly one card and exits |
| CPU | A slot: its own git worktree, so concurrent builds never share a directory |
| Mutex | A lane: a set of files only one card at a time may change |
| Scheduler | A script that assigns cards to free slots, watches the workers, and shows status |
| Write-ahead log | The claim commit and the journal lines a worker adds to its card |

### The flow

```
plan with the frontier model  ->  docs/ai/<name>_plan.md
planner breaks it into cards  ->  jobs/queue/<n>-L-slug.md, plan moves to jobs/plans/
scheduler assigns a card      ->  a fresh worker in a free slot
worker does the card          ->  queue/ -> active/ -> done/ (or review/, or parked for the human)
last card of a plan is done   ->  plan moves to jobs/archive/
```

### A job card

One file per piece of work a worker can finish on its own, named `<n>-L-slug.md`: `<n>` is a positive integer of any width that identifies the card (the highest number in use + 1; never renumbered; order is Priority, then Depends on, then the number), `L` is the lane.

```markdown
# 030-A: Short title

Lane: A
Status: ready | waiting: <who or what> | needs-human: <question>
Priority: high | low (optional; normal if left out)
Depends on: 015-B (or "none")
Locks: <files or functions in another lane's area this card must touch> (or "none")
Plan: jobs/plans/<plan>.md (the steps this card covers)

## Task
What to do, pointing at the plan for the detail.

## Done when
The checks that end the job.

## Stop and ask if
Anything specific to this job, on top of the standard stop conditions.

## Result
Filled in by the worker: a journal line per commit, then what was verified and a checklist of
anything left for the human.
```

Keep cards small: a day or two of agent work, ending in something checkable. The plan holds the detail; the card says which part of it is this job and how to tell it is finished. A card is self-contained, which is what lets a worker with no memory of earlier cards pick it up.

### Lanes: keeping parallel workers apart

Two workers editing the same file is the failure that costs the most, because neither one notices until a rebase conflicts. Prevent it by layout, not by luck:

- **Each lane owns a set of files or folders.** For example: lane A the core, lane B the front end, lane C the tools and debugger. A lane is a mutex: the scheduler never runs two of its cards at once. How many lanes, and how many workers, is a per-project choice: one lane and one worker is a fine start, and both are changed later in the project's config (section 11).
- **Cards move between lanes to keep workers busy.** A card whose footprint is predictable (a contained bug fix, a cleanup, a self-contained feature) can be given to whichever lane would otherwise sit idle, with a `Locks:` line naming the other lane's files it changes. Open-ended work (bring-up, fault hunts) stays in its home lane, because its files can't be named in advance.
- **Behaviour-changing work is attributable.** It lives in one lane, or in a card that locks every file it changes, so a failing gate always points at one change.
- **Shared files** (top-level build files, a host interface header, `.gitignore`, append-only logs) may be touched by any lane, additively: pull immediately before, push immediately after.
- **Locks.** While a card with locks is in `active/`, other lanes stay out of the locked files. A card parked for the human releases its locks while it waits; whoever resumes it checks them again.
- **Schedule the meeting points.** When a feature spans lanes, split it into cards and make the later one depend on the earlier.
- **Hot spots become one file per entry.** Anything every worker writes to, like requests to the planner, is a folder of small files instead of one shared file, so two workers never edit the same file.

### The scheduler

The scheduler is a script that runs continuously and does what a human would otherwise do by hand: start workers, notice when they finish or get stuck, and decide what runs next.

- **Fresh worker per card.** Each card gets a new agent session (a CLI agent run non-interactively). The worker does that card and exits, so no context ever carries from one card to the next, and a long session can't degrade.
- **Slots are worktrees.** Each slot is its own git worktree, reset to `main` before each card. Workers pull and push with rebase against `main`; the push is the lock.
- **It assigns the card.** Every minute it reads the cards from `origin/main`. First it resumes any card in `active/` that isn't parked and has no worker (an orphan from a session that died), a limited number of times. Then it takes the highest-priority, lowest-numbered card that is `ready`, whose dependencies are in `done/` or `review/`, and whose lane is free. Workers never race each other for a card.
- **Watchdog.** A worker whose output hasn't grown for half an hour is asked to stop cleanly; one that doesn't, or that runs past a hard limit, is killed and its card left for resume.
- **Clean stops.** A stop is cooperative: the scheduler raises a flag the worker checks before each build, gate run and commit. A worker that sees it pushes only green work, records where to resume, leaves the card ready, and exits. Killing is the last resort.
- **Core sharing.** Each worker is told to use the machine's cores divided by the number of slots, for builds and tests, so the workers don't oversubscribe the machine and thrash. The status page shows CPU and memory, which is how you size the number of slots.
- **Health check.** Whenever `main` has moved, the scheduler builds and gates it in a worktree of its own. If anything is red, it stops dispatching until `main` is green again, so a bad commit doesn't become the starting point for every later card.
- **Startup-time gate (1032-B).** The health check ends with `bin\startup-gate.ps1`: it generates a pool in a temp folder (3 projects of 300 cards and one of 2,000, real git repositories), starts the built scheduler on it three times on a free port with its own pool name (`-PoolName`) and log folders, and compares the median time to the page answering and to every project's first snapshot (the `pool: start took` line) with `test\perf\startup-baseline.json`. It fails when a median is over the baseline by more than 50% **and** by more than 2 s, after one more measurement (load on the PC); the output names both times. The baseline is re-recorded only on purpose, with `bin\startup-gate.ps1 -Record`, in the card that makes startup faster or accepts a slower start, and that card's Result says so. Workers run the same script before a push that changes `code\src`.
- **Accounting.** Every finished card is a row in a CSV: card, lane, result, time. Over time it shows which kinds of card are expensive and which lane is the bottleneck.
- **A status page with controls.** The scheduler serves a local web page: each slot's card, time and latest output; what is waiting on the human (parked questions, cards in review); every queued card and why it isn't running; recent results; the health of `main`. Its controls are pause and resume, drain (finish running cards, start nothing new), clean stop and kill per slot, enabling and disabling slots, and adding or removing slots.

Some agent apps can't start a fresh session per card on their own; they keep one long session going. A long session works too, if you prefer to watch it in the app: it follows the same protocol and takes cards one after another, and you end it after its context has been compacted twice. The scheduler is what turns that into something that runs overnight without anyone watching.

### The protocol every worker follows

`jobs/README.md` is the worker's whole brief. In outline:

1. **Pull** (`git pull --rebase`). A conflict in a file the lane doesn't own is a stop.
2. **Take the card.** Under the scheduler, the card is assigned. Run by hand, the worker resumes its lane's unfinished card first, otherwise takes the next takeable one.
3. **Claimed for you**: under the scheduler, the scheduler claims the card - it moves it to `active/` with its `Claimed:` line in a commit of its own before the session starts, and a worker never moves a card. Run by hand, the worker claims it itself.
4. **Do the work** under the project's rules: a test first for anything that changes behaviour, one root cause per commit, push each commit once it is green. **Keep a journal**: with each commit, one line on the card saying what is done and what is next.
5. **Gates before every push**: the full unit-test run against its baseline, the debugger and MCP tests, and every build configuration compiles. For the scheduler that includes the startup-time gate, `binstartup-gate.ps1` (see the health check above). Better as one command that runs them in parallel.
6. **Finish**: fill in `## Result` and set `Status:`, then ask for the move with a move request in the scheduler's moves folder - `To: done`, or `To: review` when what remains needs a person (a checklist in the result). The scheduler commits the card with your text; you leave that last edit uncommitted. The human approves review cards into `done/`, or sends them back with notes.
7. **Archive the plan** when every card naming it is in `done/`, in the same commit.
8. **Report** in a fixed format, ending with a machine-readable result line (`done`, `review`, `waiting`, `needs-human`, `stopped`, `failed`) that the scheduler records.

### Stop conditions

Workers run unattended, so the stop rules matter as much as the work rules.

- **Park and ask** for a decision the plan doesn't settle, a file outside the lane with no lock, three attempts at localising a fault that didn't converge, a fix needing an architectural change the card didn't ask for, or a gate failure the worker didn't cause. The worker sets `Status: needs-human: <question>`, records what it found, leaves the card in `active/` and hands it to the scheduler with a move request `To: active`. A parked card does not stop the work: its lane is free for the next card, and the human answers when they next look.
- **Hand to the planner** for anything only the planner can do: a check on a reference system, a new or changed test that needs verifying elsewhere, reference images, a new plan. The worker writes a request file with a reproducible case, sets the card to `waiting: planner (request #N)`, and hands the card back with a move request `To: queue`. The planner answers in the same file and sets the card back to `ready`.
- **Stop when asked**: the scheduler's stop flag, at the next safe point.

### The standard prompt

Under the scheduler, each worker's prompt is generated: the card it has been assigned, its worktree, its lane's rules, how many threads it may use, the stop flag, and the result line to end with. Run by hand, one prompt per lane, pasted into a session, never changes:

```
You are the lane <A|B|C> agent for <project> (<path>).
Read jobs/README.md and follow it exactly. It is your whole brief.

Work the queue continuously, one job after another, in this one session:
- Pick by step 2 of the README: your lane's unfinished card in active/ first (never a
  needs-human one), otherwise the lowest-numbered takeable card in queue/.
- Claim it, do it, run the gates, move it to done/ or review/, archive its plan if that was the
  plan's last card, push.
- A question for the developer does not stop you: park the card as needs-human, push, and take
  the next job.
- Then go straight on to the next job. Do not end your turn, summarise, or ask whether to
  continue between jobs.

End your turn only when your lane has no card it can take, or your context has been compacted
twice. Then give the session report.

The other lanes' agents are working at the same time: stay inside your lane's files, pull before
you start each job, and push as soon as each commit is green.
```

### Running it

- **The planner keeps every lane's queue full.** Idle workers are the waste to watch for. When a lane runs low, move a card with a predictable footprint into it, or write new ones. Measure throughput by how little the workers sit idle.
- **Plans settle what a worker would otherwise have to ask.** Every open question answered in the plan is a parked card that doesn't happen at 2 a.m.
- **Watch the page, not the workers.** What needs you is listed there: parked questions, cards in review, a red `main`. Clearing those is the human's part of the throughput.
- **Reorganise only while the queue is drained.** Moving the queue, renaming plans or changing lane ownership while a worker is mid-card leaves it writing to paths that no longer exist. Drain, reorganise, then resume.
- **Workers in worktrees, on `main`.** Each worker's worktree is reset to `main` for each card, and it pushes straight back to `main`. Long-lived branches per agent just move the collision to merge time.
- **Don't mix manual sessions with the scheduler.** A hand-started session's claimed card looks like an orphan to the scheduler, which then starts a second worker on it.

## 9. Ongoing Development

- **Every new feature comes with tests.** Write the test plan with the frontier model before or alongside the implementation.
- **Every bug fix comes with a regression test** that fails before the fix and passes after it.
- **Keep the documentation current.** When behavior changes, update the design documents and conventions so that future agent sessions start with accurate context.
- Expect the suite to grow to thousands of tests. More detail is better. The size of the suite is what lets you hand large changes to an agent with confidence.
- **Keep unit tests out of shipping code where possible.** Runtime checks and error detection in the shipping build are not unit tests. Tests live in `test/` and build as separate targets.

## 10. Common Pitfalls

- **Convention drift**: over long sessions, agents gradually stop following the style rules. Re-supply `conventions.md`, and let the formatter and linters enforce what they can.
- **Context exhaustion**: verbose tool output and very large files degrade the agent's performance. Keep files focused and tool output compact.
- **Confident wrong fixes**: an agent can produce a plausible change that hides a symptom without fixing the cause. Determinism, trace diffing and regression tests are the defense.
- **Over-specifying too early**: build the skeleton broad and shallow first, then deepen one system at a time.
- **Skipping the debugger**: without a way to observe execution, the agent guesses. Building the debugger first is what makes the rest of the process fast.
- **Parallel agents in the same files**: two agents with overlapping work produce conflicts neither can resolve well. Give every file an owning lane before starting a second agent.
- **Idle workers**: a lane whose queue is empty while another's is long wastes a worker. Move contained cards across lanes with locks, and keep plans flowing.
- **Oversubscription**: several workers that each build and test with every core make the machine thrash. Give each worker its share of the cores.
- **Hung or dead workers**: an agent session can loop or die mid-card. A watchdog and orphan resume recover both without a human.
- **A red `main` that spreads**: one bad push becomes the starting point for every later card. Check `main` after each card lands and stop dispatching while it is red.
- **Stale plans**: a plan that says "not started" about work that has landed sends the next agent down the wrong path. Agents update a plan's progress lines for their own card; the planner does the rest.

## 11. Standing Up a New Project with ProjectToolkit

ProjectToolkit (the repository this document is in, `C:\Projects\ProjectToolkit`) is a kit, not an
application: the canonical layout of section 1 with the templates and documentation that go with it,
the job queue of section 8, and the scheduler that runs it. It does two independent things, both from
`bin/projecttoolkit.ps1`, and the first does the second as part of standing up a project:

- `-Action new-project` lays a new project down from the canonical layout, then sets up its queue.
- `-Action jobs` sets up, or with `-Update` refreshes, the job queue in a repository that is already
  there, and touches nothing else in it.

The scheduler is the third thing and is not an action: every project runs the kit's own
scheduler (`code/src/Scheduler.Host`, `scheduler-next.exe`), and projects keep no copy of it.

```
ProjectToolkit/
├── README.md                 # what is here
├── bin/
│   ├── projecttoolkit.ps1    # the setup actions: -Action new-project | jobs
│   └── init-project.ps1      # forwards to projecttoolkit.ps1, for older instructions
├── docs/
│   ├── workflow.md           # this document
│   ├── agent-lessons.md      # rules learned running workers on a real codebase
│   └── design/               # the scheduler's roadmap (gdd/) and spec, test plans, planner outbox (tdd/scheduler/)
├── code/src/                 # the scheduler: SchedulerNext.slnx and Directory.*.props; Scheduler.Core
│                             #   (rules), Scheduler.Host (processes, git, the exe), Scheduler.Web (page)
├── test/unit/                # its xUnit tests
├── assets/
│   ├── themes/               # the scheduler page's themes, loaded at run time
│   └── templates/            # the generic text every project gets (below)
├── jobs/                     # the toolkit's own job queue
└── assets/templates/
    ├── readme.md             # the project's root README: overview, status, design in brief
    ├── conventions.md        # docs/conventions.md: the layout above, plus the project's own rules
    ├── gitignore             # build output, generated files, editor cruft (placeholders survive)
    ├── gitattributes         # text/binary and eol rules
    ├── design/
    │   ├── gdd.md            # docs/design/gdd/<project>.md: the intent document, section by section
    │   └── tdd.md            # docs/design/tdd/<project>-technical.md: the technical design
    ├── ai/README.md          # docs/ai/, the new-plans inbox
    └── jobs/
        ├── README.md         # the generic worker protocol (every project's jobs/README.md)
        ├── project.md        # the project-specific half: gates, locations, briefs
        ├── categories.json   # an example: each category's purpose, the paths it owns, exceptions, shared files
        ├── scheduler.json    # an example config, with every setting
        ├── card-template.md
        ├── plan-template.md
        └── requests/README.md
```

**One source, many copies.** `assets/templates/` is the only place the generic text is written. A project's `jobs/README.md`, its card and plan templates and `.gitattributes` are copies of it, refreshed by `-Action jobs -Update`, because that is the folder the scheduler and its workers read from - a worker only ever has its own checkout of the project. So a fix goes in the kit, never in a project's copy: edit `templates/`, run `-Action jobs -Update` over the projects, and commit. What the project owns outright - `README.md`, `.gitignore`, `docs/conventions.md`, `docs/design/`, and the four queue config files - the refresh leaves alone.

**The split that makes it reusable.** `jobs/README.md` is the protocol, the same everywhere: how to take, claim, do, gate and finish a card, the stop conditions, the session report. What is particular to the project is in files beside it: `project.md` (the gate commands, where things live, the standing briefs, the schedule), `categories.json` (each category's purpose, the paths it owns and its exceptions, and the shared files). In a pool the worker types are the pool file's (`pool.json`), so a project has no `workers.json`. `categories.json` is the one to hand a model that needs to understand the categories. Workers read the README, `project.md` and their own category's paths, which the scheduler puts in their prompt. The scheduler reads `scheduler.json` and `categories.json` (or the older `lanes.json`), the last from `main`.

### Steps for the LLM setting up a project

1. **Settle the basics with the human:** the project's name and repository path; the first categories (a letter, a name and the folders each owns; start with as few as the work allows); the build and test commands that will be the gates; the agent CLI and model the workers use.
2. **Run the kit**, from PowerShell. For a new project, the layout action, which also sets up the queue:
   ```powershell
   C:\Projects\ProjectToolkit\bin\projecttoolkit.ps1 -Action new-project -Repo C:\Projects\<Name> `
       -Categories "A=core:code/src/,test/;B=tools:code/tools/" `
       -Modules "app,platform,systems" `
       -Gate "cmake -S code -B build;;cmake --build build --parallel {jobs};;ctest --test-dir build -j {jobs}"
   ```
   It creates the repository if needed and the canonical layout of section 1 (every folder, with a `.gitkeep` in the empty ones), the root `README.md`, `.gitignore` and `.gitattributes`, `docs/conventions.md`, the two design documents under `docs/design/`, the `jobs/` tree with the generic README and templates, `project.md` with the gates filled in, `categories.json` with each category's name and owned paths, `scheduler.json`, `docs/ai/`, and `docs/known_gaps.md` (the scheduler is not copied: every project runs ProjectToolkit's). It never overwrites an existing file unless told to (`-Force`). Separate gate commands with `;;`. `{jobs}` is the scheduler's per-worker thread count.

   For a repository that already exists and only needs the queue - or to pull kit improvements into one that has it - the queue action alone, which writes nothing outside `jobs/` and `docs/ai/`:
   ```powershell
   C:\Projects\ProjectToolkit\bin\projecttoolkit.ps1 -Action jobs -Repo C:\Projects\<Name>          # add it
   C:\Projects\ProjectToolkit\bin\projecttoolkit.ps1 -Action jobs -Repo C:\Projects\<Name> -Update  # refresh it
   ```
   `-Update` refreshes the text the kit owns and fills in any missing folder or file; leave the other arguments off it. It keeps a project's existing `categories.json`, older `lanes.json` and any old `workers.json`. `-Lanes` still works as the old name of `-Categories`; `-Slots` is accepted and ignored (no `workers.json` is written: in a pool the worker types are the pool file's).
3. **Do sections 1 to 5** of this document: the layout, the skeleton, the debugger, the MCP server, the reference material. Fill in the **fill in** sections of `docs/conventions.md` and `README.md` as you settle them, and write the design in `docs/design/gdd/` and `docs/design/tdd/` - the gates in `project.md` grow as these land.
4. **Fill in `categories.json` and `project.md`** with the human: each category's purpose, exceptions and the shared files; locations and standing briefs.
5. **Check `scheduler.json`** (below): the worker command and default model, the health command, the thread variable, `promptExtra` for build hints the workers need. The worker types (server, model, agent, how many) are the pool file's, not the project's. Set `project` to the pool name.
6. **Plan and card** the first work: a plan in `docs/ai/` (from `plan-template.md`), cards in `jobs/queue/` (from `card-template.md`), the plan moved to `jobs/plans/`.
7. **Commit and push to `main`.** The last step of the kit joined the project to the pool (`%LOCALAPPDATA%\ProjectToolkit\pool.json`: `{ "name", "repo", "weight": 50 }`, added once; `-NoPool` skips it). The running pool picks the project up at its next tick once `jobs/` is on `main`; there is no per-project scheduler to start. `scheduler.json`'s `project` must equal the name in the pool file.

### The scheduler's config

| Setting | Meaning |
|---|---|
| `project` | Name shown on the page; also the log folder (`%LOCALAPPDATA%\<project>\scheduler`) unless `logDir` is set. |
| `lanes` | Only for a project without `categories.json` or `lanes.json`: letter to name. |
| `planner.outbox` | Where a planner that cannot reach git leaves card changes for the scheduler (`jobs\.planner-outbox`). |
| `jobsDir`, `remote`, `branch` | Where the queue lives and which branch workers pull from and push to. |
| `workspaceRoot` | Where the worker and health-check worktrees go; default `<repo>-workspaces` (for example `C:\Projects\Foo-workspaces\slot1`). |
| `port` | The status page's port. |
| `jobsEnvVar` | The environment variable holding each worker's thread allowance (cores divided by slots). |
| `workerWindow` | `Hidden` (default), `Minimized` or `Normal`: how worker and health-check consoles appear. Each is titled with the project, slot and card. |
| `worker.cli`, `worker.args` | The agent command, run once per card. `{prompt}` is replaced by the generated prompt; `{model}`, `{endpoint}` and `{jobs}` are filled in everywhere. |
| `worker.model`, `worker.endpoint`, `worker.env` | The model, its server, and environment variables for the worker (for example a bring-your-own-model endpoint for the Copilot CLI: `COPILOT_PROVIDER_BASE_URL`, `COPILOT_PROVIDER_TYPE`, `COPILOT_MODEL`). |
| `worker.quotaPatterns` | Text in a worker's output that means the account is out of quota: dispatching pauses instead of burning retries. |
| `health.command` | Builds and gates `main` after each card lands. Empty turns the health check off. |
| `promptExtra` | One or two lines added to every worker prompt, for project-specific build hints. |
| `limits` | `pollSeconds`, `maxRetries`, `stallMinutes`, `warnMinutes` (how long before a card's limit the worker is warned), `stopGraceMinutes`, `maxMinutes` (the limit for a card with no `Time box`). |

The worker types (model server, model, agent, how many) are the pool file's, not the project's; a project has no `workers.json`. An old one is left alone and ignored.

### Changing it as the project grows

- **More or fewer workers:** change the pool file's worker types (`%LOCALAPPDATA%ProjectToolkitpool.json`). Watch the page's CPU and memory, and each server's load: more workers than the machine or the model server can feed makes every worker slower.
- **New or changed categories:** edit `categories.json` and push. A category with nothing in `queue/` or `active/` changes at once; one with pending cards is held until you check them and press Accept on the page. Rename any queued cards whose category letter changes. Split a category when its queue is consistently the longest and its work has a natural seam; merge categories that are mostly idle.
- **New gates:** add them to `project.md` and to `health.command`.
- **Kit improvements:** fix the kit, not a project's copy, then pull the change into each project with `projecttoolkit.ps1 -Action jobs -Repo <path> -Update`. That refreshes the kit's own text (the jobs README, the card, plan and request templates, `docs/ai/README.md`, `.gitattributes`) and fills in any canonical folder or file the project is missing; it leaves the files the project owns alone (`README.md`, `.gitignore`, `docs/conventions.md`, `docs/design/`, `project.md`, `categories.json` or an older `lanes.json`, an old `workers.json`, `scheduler.json`). A project with neither `categories.json` nor `lanes.json` gets a `categories.json` made from its `scheduler.json` lanes, to fill in. Restarting the scheduler is safe at any time: it adopts workers still running from the previous run.
