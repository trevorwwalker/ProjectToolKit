# Projects and the job queue

[Guide index](README.md) · Next: [Working with a planner](planner.md)

This page explains how to start a project with the toolkit, what the `jobs/` folder is, and how a piece of work (a **card**)
travels through it. It uses one small example throughout: a command-line tool called **Recipe Box**, kept in
`C:\Projects\MyProject`.

## The idea in two minutes

You describe what you want. Workers (AI agent sessions) do it, one card at a time, unattended. Three things make that
safe:

- **Work is cut into cards.** A card is one Markdown file that says what to do and when it is finished. A worker
  needs nothing else, so a fresh session can pick it up with no memory of the last one.
- **Each card lives in a folder that says where it is**: `queue/` (waiting to run), `active/` (a worker has it),
  `review/` (finished, but you must look), `done/` (finished and checked).
- **Only the scheduler moves cards.** Workers ask for a move; the scheduler makes it and commits it. This is why two
  workers never fight over the same file. See [the scheduler guide](switchyard.md) for how it runs.

You rarely write cards by hand. You talk to a **planner** (an LLM session) that turns your wishes into a plan and cards;
see [Working with a planner](planner.md).

## Set up a new project

`bin\projecttoolkit.ps1` is the setup tool. It has two actions: `new-project` and `jobs`.

`new-project` lays down the standard folder layout (code, tests, docs, design documents) and then adds the queue.
Run it from the toolkit folder:

```powershell
.\bin\projecttoolkit.ps1 -Action new-project -Repo C:\Projects\MyProject `
    -Categories "A=app:code/src/,test/;D=docs:docs/"
```

Real output (shortened):

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
created  jobs/card-template.md
created  jobs/categories.json
created  jobs/project.md
created  jobs/scheduler.json
...
MyProject is set up in C:\Projects\MyProject with the canonical layout and a job queue. Next:
  1. Fill in README.md ...
```

The project name is the last part of the folder name. The command prints a numbered "Next" list; the steps are
below. Useful switches:

| Switch | Meaning |
|---|---|
| `-Categories "A=name:path,path;B=name:path"` | The categories (see below). One is fine to start with. |
| `-Modules "app,platform"` | Create `code/src/<module>/` folders. Leave it out until your design names the modules. |
| `-NoPool` | Do not add the project to the scheduler's pool (`%LOCALAPPDATA%\ProjectToolkit\pool.json`). Use it for experiments. |
| `-Force` | Overwrite files that already exist. Without it nothing is overwritten. |

Nothing already in the folder is overwritten, so it is safe to run it on a folder that has a few files.

### Add the queue to a project you already have

If the repository exists, add only the queue:

```powershell
.\bin\projecttoolkit.ps1 -Action jobs -Repo C:\Projects\MyProject
```

This creates `jobs/`, `docs/ai/` and `docs/known_gaps.md`, then joins the project to the scheduler pool (unless `-NoPool`).

### Bring in improvements later: `-Update`

The toolkit improves over time. To pull its newer text into a project that already has a queue:

```powershell
.\bin\projecttoolkit.ps1 -Action jobs -Repo C:\Projects\MyProject -Update
```

```
same     jobs/README.md
same     jobs/card-template.md
...
Left alone, because they are the project's own now: README.md, .gitignore, docs/conventions.md,
docs/design/, jobs/{project,categories,lanes,workers,scheduler}.json.
```

`-Update` refreshes only what the toolkit owns (the jobs `README.md`, the card, plan and request templates,
`docs/ai/README.md`) and fills in anything missing. Your own files are never touched. Never edit
`jobs/README.md` in a project: the next `-Update` would replace your edit.

### After setup: five things to fill in

1. `README.md` and `docs/conventions.md`: what the project is and its rules.
2. The design: `docs/design/gdd/<name>.md` (what it must do) and then the technical design (how). Ask a planner to
   draft these with you.
3. `jobs/categories.json` (below).
4. `jobs/project.md` (below), and in `jobs/scheduler.json` the `project` name and the health command.
5. Commit and push to `main`. The scheduler's pool picks the project up at its next tick; there is nothing to start.

## What is in `jobs/`

| Path | Holds |
|---|---|
| `queue/` | Cards waiting to be worked. |
| `active/` | The card a worker has now, or a card parked and waiting for you. |
| `review/` | Finished cards that need your hands: something to try, or a decision. |
| `done/` | Finished and verified cards. Kept for good. |
| `plans/` | Plans that have been turned into cards. Kept for good. |
| `requests/` | Questions workers put to the planner, one file each. |
| `attachments/` | Screenshots and logs for a card, in `attachments/<number>/`. |
| `.logs/` | The scheduler's and workers' logs. Not in git; ignore it. |
| `project.md` | This project's gates, file locations and standing rules. |
| `categories.json` | Who may change which files. |
| `scheduler.json`, `models.json` | Scheduler settings; the model list. |
| `card-template*.md`, `plan-template.md` | Starting points for a card or plan written by hand. |
| `README.md` | The rules every worker follows. |

Why separate folders? Because "where is the card" is the whole status of the work. You can look at the folders in a
file browser and know what is waiting, what is running and what needs you.

## A card's life

### What a card looks like

A card is Markdown with a short header, then sections. Here is a card for Recipe Box, as the planner leaves it:

```markdown
# 12-A: Add `recipebox search <word>`

Category: A
Type: feature
Plan: jobs/plans/search_plan.md, step 1
Status: ready
Priority: normal
Depends on: none
Scope: code/src/RecipeBox/Search.cs, test/unit/SearchTests.cs
Worker: Sonnet
Time box: 45 min

## Task

Add a `search` command that prints the titles of recipes whose title or ingredients contain the word
(case-insensitive), one per line, and exits 1 when nothing matches.

## Done when

- `recipebox search tomato` lists "Tomato soup" and "Shakshuka" for the sample data.
- A test covers a match, no match, and mixed case.
- The build and tests pass.

## Questions

## Result
```

The header fields:

| Field | Meaning |
|---|---|
| Title `# 12-A:` | The card's number, then its category letter. |
| `Category` | Which category's files it may change. |
| `Type` | `feature` or `bug` (bug cards also say the `Version` where it was seen). |
| `Plan` | The plan file (in `jobs/plans/`) and step it belongs to, or `none (<who>, <date>)` for a one-off. |
| `Status` | Where it stands: `ready`, `waiting: ...`, `needs-human: ...`, `review: ...`. |
| `Priority` | `high` runs first, `low` last; leave out for normal. |
| `Depends on` | Cards that must be done first, or `none`. |
| `Scope` / `Locks` | The files it may change; and files of another category it needs. |
| `Worker`, `Time box` | Which kind of model, and how many minutes it gets. Usually set by the planner. |

Sections: **Task** (what to do), **Done when** (checkable facts), **Questions** (for you), **Result** (the worker's
journal and what it verified).

### Statuses

- `ready`: can be taken as soon as its dependencies are done.
- `waiting: <what>`: held back, for example waiting for the planner to answer a request.
- `needs-human: <question>`: a worker stopped because only you can decide. It stays in `active/`.
- `review: <what to check>`: finished, but you must look. It is in `review/`.
- `done`: finished and verified. It is in `done/`.

### The journey

```
queue/   --scheduler starts a worker-->   active/   -->   done/
                                                     -->   review/   (you check it, then accept or send back)
```

1. The scheduler picks the first `ready` card whose dependencies are done and whose files no running card is using, moves
   it to `active/` and starts a worker on it.
2. The worker does the work, runs the gates (see `project.md` below) and pushes each green commit to `main`. It writes a
   dated line in **Result** at least every 45 minutes, so a later session can resume.
3. It ends by asking for a move: `done` when every check passed and nobody needs to look, `review` otherwise.
4. If it hits a decision it cannot make, it sets `needs-human`, writes the question under **Questions** and stops.

### Questions and answers

A worker's question is written as one line with choices, in plain words:

```markdown
## Questions

- Q1: Found: two recipes have the same title but different ingredients. Question: should search list both?
  [yes, list both -> done | no, merge them first -> queue | ask me later]
```

The choice after `->` says where the card goes when you pick it. You answer from the scheduler's page (next section); the
answer is written under the question and the card continues. You can also write a reply by hand in a section headed
`## Dev`; where it conflicts with the card, your reply wins, and the worker answers it in **Result**.

### Waiting on you

The scheduler's page has a **Waiting on you** list: every card that cannot move without a person. A card is there when it is
`needs-human`, when it is in `review/`, when it is waiting for you, or when it is *stuck* (it ran out of tries; use **Retry**
once you have looked). Each row shows the reason in a line, taken from the card's Status or its latest Result line.
Make that list empty and the work flows. See [the scheduler guide](switchyard.md) for the page.

### Review

A `review/` card has the worker's Result and a checklist of what is left. Try the thing, then answer its questions on the
page: choose the `-> done` answer to accept, or `-> queue` to send it back with a note.

### Done

`done/` is the record. Cards are never deleted. Nobody archives them away either: with plans, they are the complete history
of how the project was built.

### Card numbers: do not choose one

Numbers are handed out by the scheduler so two writers never take the same one. To ask for a card, name the file
`new-<L>-<slug>.md` in `jobs/queue/`, with the title `# new-<L>: <title>`. Example: `new-A-search-command.md` starting with
`# new-A: Add recipebox search`. At its next step the scheduler renames it `<n>-A-search-command.md` (for example
`12-A-search-command.md`), fixes the title and any `Depends on: new-A-search-command` lines in the same commit, and logs
`card numbered: new-A-search-command -> 12-A-search-command`. Numbers are never reused or renumbered. To run a card earlier,
give it a `Priority` or make others depend on it.

A `new-` file is not a card until it has a number, so it is never started early.

## Plans

A plan is the written-down answer to "how do we build this?", split into steps that become cards.

- Plans live **only** in `jobs/plans/`, named `<name>_plan.md`, one `##` section per step with its cards. Once a plan has been
  carded it stays there for good, finished or not; nothing is archived.
- `docs/ai/` holds only brand-new plans that have no cards yet.
- **Design documents** (`docs/design/...`) are inputs to a plan, not plans. When a design is carded, the planner writes the plan
  in `jobs/plans/` linking to it. The guide explains use; the design document stays the source of truth for design.
- A card's `Plan:` line names the plan file and the step.

A good plan says what the code does today, what it should do, the steps, and how the whole thing is known to be finished
(`jobs/plan-template.md` has the headings). You do not have to write it: ask the planner ([guide](planner.md)).

## Requests

When a worker needs something only the planner can give (a decision the plan does not settle), it writes a request: a new
file `jobs/requests/NNN-slug.md` saying which card is waiting and what is needed, sets that card to
`waiting: planner (request #N)`, and moves on to another card. The planner answers in the same file and sets the card back to
`ready`. Answered requests are kept as the record (`requests/answered/`).

## Categories

A category is a letter for a kind of work and the set of files it may change. Several workers run at once; a **category's**
files are changed by one card at a time, so two workers never edit the same file. `jobs/categories.json`:

```json
"A": { "name": "app",  "purpose": "the program",   "owns": ["code/src/", "test/"], "except": [] },
"D": { "name": "docs", "purpose": "documentation", "owns": ["docs/"],              "except": [] }
```

A path ending in `/` is a folder and everything under it. `except` lists files carved out. The `shared` section names files any
category may *add to* (such as `.gitignore`) but not rewrite. If a card must change another category's files it names them in
`Locks:`, and while it is active no other card touches them. Start with one category; split when you want two kinds of work
running at once without colliding. Ask the planner to fill in each category's purpose, owns and exceptions.

## `project.md`: gates and briefs

`jobs/project.md` is the project-specific half of the workers' rules (the generic half is `jobs/README.md`). Its important
part is **Gates**: the commands every worker must pass before it pushes. For Recipe Box:

```markdown
## Gates

Run before every push, all of them, from the repository root:

- `dotnet build RecipeBox.slnx -warnaserror`
- `dotnet test RecipeBox.slnx --no-build`
```

"Green" means the gates passed on that commit. Code that does not pass is pushed to a `wip/<card>` branch instead, never to
`main`. The same file lists where things live and any standing briefs (rules such as "never weaken a test"). Put the same
gates, joined with `&&`, in `scheduler.json` as the health command so the scheduler can check that `main` stays green.

## How a planner session works with the queue

The planner is an LLM session you talk to. It may run in a sandbox that can read and write files in the project folder but
cannot reach the git server. So it does not push. Instead it leaves a **batch** in the **outbox**,
`jobs/.planner-outbox/` (ignored by git): a folder holding the new files and a `batch.json` with a commit message and the list
of writes, moves and removes, written last so a half-finished batch is never taken.

If **Planner push** is on in the scheduler's page, the scheduler fetches, applies each complete batch to `main` (only paths
under `jobs/`, merging if a file changed in the meantime), commits and pushes it, then moves the batch to `sent/` (or
`failed/` with a reason). With the switch off, batches wait. Every card the planner creates uses a `new-<L>-<slug>` name, so
the scheduler numbers it. A card the planner sets back to `ready` runs again even if it ran out of tries before.

For you the effect is simple: tell the planner what you want, read its plan, and watch cards appear in `queue/`.
The details of working with it are in [Working with a planner](planner.md); the outbox design is
`docs/design/tdd/scheduler/planner-outbox.md`.

## An example from start to finish

1. `new-project` for Recipe Box (above). Fill in the README and design with the planner.
2. Ask the planner: "Add a search command." It writes `jobs/plans/search_plan.md` and leaves `new-A-search-command.md`.
3. The scheduler numbers it `12-A-search-command` and puts it in `queue/`.
4. A worker takes it (`active/`), writes `Search.cs` and a test, runs the two gates, pushes, and asks for `review` because
   you must try the command by hand.
5. It appears under **Waiting on you**. You run `recipebox search tomato`, pick `-> done`, and the card moves to `done/`.

## Where to go next

- [Working with a planner](planner.md): what to say, and what to check in what it writes.
- [The scheduler (Switchyard)](switchyard.md): the page, the tray and the controls.
- [Guide index](README.md)
