# Working with a planner LLM

Back to the [guide index](README.md).

The workers that run your cards are fresh AI sessions that know nothing beyond the card, the plan it points to and the
repository. Somebody has to turn "I want this feature" into cards a worker can finish: that is the **planner**, a
strong model you talk to (a chat assistant with access to the project folder, or an agent session such as Claude Code
opened on the project). You decide; the planner writes the plans and cards, answers the workers, and reviews what
they did. This page shows how to set one up and direct it, with examples from a small project.

Why a separate planner at all: a small or local model finishes a well-specified one-hour card reliably, and fails an
open-ended one. Splitting the work is the hard part, so a strong model does that once, and the cheap models do the
many pieces ([agent-lessons.md](../agent-lessons.md), sections 1 and 8).

## The example project

The examples use **Tally**, a small C# command-line app that counts words in text files, set up with
`bin/projecttoolkit.ps1` (see [projects.md](projects.md)). It has two categories in `jobs/categories.json`:

| Category | Owns |
|---|---|
| A (core) | `code/src/Tally.Core/`, `test/unit/Tally.Core.Tests/` |
| B (command line) | `code/src/Tally.Cli/`, `test/unit/Tally.Cli.Tests/` |

and gates in `jobs/project.md`: `dotnet build code\src\Tally.slnx -warnaserror` and `dotnet test code\src\Tally.slnx`.

## 1. What the planner does, and what it doesn't

| The planner does | The planner does not |
|---|---|
| Writes plans in `jobs/plans/` with you, then turns each step into cards. | Write or commit product code. Workers do that, behind the gates. |
| Picks each card's category, scope, worker and time box. | Choose card numbers: Switchyard (the scheduler) hands them out. |
| Answers workers' requests (`jobs/requests/`) and helps you answer their questions. | Answer a question that is yours (behaviour, scope, cost); it drafts the choices, you pick. |
| Reviews finished cards in `review/` and says what it checked. | Rewrite a card a worker is running; it adds a `## Dev` note instead. |
| Checks the queue when you ask, and says what needs you. | Push to git. Its card changes go through the **planner outbox**, and the scheduler commits them. |

Why the planner never pushes: the scheduler is the only writer of card moves on main (the README's "Who moves the
cards"), so two writers never race on the same card. The planner hands its changes over in the outbox; the scheduler
applies them on the current main, merges them with any worker edit made in the meantime, and commits them.

## 2. What the planner needs access to

| Access | Why | Needed? |
|---|---|---|
| Read the project folder (your own clone) | It reads the rules, plans, cards, results and code. | Yes |
| Write `jobs/.planner-outbox/` in that folder | Its card and plan changes go there as batches. | Yes, to change cards |
| Run read-only git there (`git log`, `git show`, `git rev-parse`) | A batch that edits a file must name the version it edited (`base`); reviews read commits. | Strongly advised |
| Switchyard's page (`http://localhost:8766/` with the pool file the kit writes) | What is running, waiting, stuck or red. `/status.json` is the same as data. | Optional |
| The log folder (`%LOCALAPPDATA%\<project>\scheduler`) | Worker logs, `accounting.csv` (one row per run), health-check logs. | Optional, useful for failures |

**Turn the outbox on.** In the project's `jobs/scheduler.json`, set both `planner` keys; `fetchRepo` is the folder the
planner reads, and `outbox` is relative to it:

```json
"planner": { "outbox": "jobs\\.planner-outbox", "fetchRepo": "C:\\Projects\\Tally" }
```

A new project's `scheduler.json` has only `outbox`, so the feature stays off until you add `fetchRepo`. Then
press the project's **Planner push: off** button in the projects table on the page's Workers view; it then reads
**Planner push: on**. While it is on, about once a minute the scheduler runs `git fetch` in `fetchRepo` (it never
pulls or touches your working tree) and applies each complete batch. While it is off, batches just wait.

Because the scheduler only fetches, the planner must read cards as they are on main (`git show origin/main:<path>`),
not from your working tree, which may be behind.

**What it reads first:** `jobs/README.md` (the rules its cards must fit), `jobs/project.md` (gates, locations, how to
pick a worker), [`docs/agent-lessons.md`](../agent-lessons.md) (why the rules exist), then `jobs/plans/` and the
cards in `queue/`, `active/` and `review/`.

## 3. The starter brief

Paste this at the start of every planner session, with the three `<...>` values filled in. A new project already has
it at `docs/planner-brief.md` (`projecttoolkit.ps1 -Action new-project` copies it there); its master copy is
`assets/templates/docs/planner-brief.md` in the kit.

```markdown
You are the planner for Tally, working with me, the developer. The project is at <project folder>.
Card changes go through the planner outbox at <project folder>\jobs\.planner-outbox. The scheduler's page is at
<page address, for example http://localhost:8766/>.

**Read first, in this order:** `jobs/README.md` (the workers' rules: you write cards they can follow),
`jobs/project.md` (gates, where things live, how to pick a worker), `docs/agent-lessons.md` (why the rules are as they
are), then `jobs/plans/` and the cards in `jobs/queue/`, `jobs/active/` and `jobs/review/`. Tell me in a few lines what
is in flight before doing anything else.

**What you do:** turn what I want into plans and cards; answer workers' questions and requests; review finished
cards; check the queue when I ask. **What you don't do:** write or commit code; push to git; move a card a worker is
running; decide anything that is mine to decide (behaviour, scope, trade-offs, cost). When a decision is mine, ask
me with two to four short choices.

**Plans.** A plan is a file in `jobs/plans/<name>_plan.md`, written from `jobs/plan-template.md`; how to write one is
in ProjectToolkit's `docs/guide/plans.md`. Nothing else is a plan: design documents are inputs a plan links to.
- Procedure: read the root plan and the plans under the step this work belongs to; say what exists; ask me the open
  questions, each with two to four choices and your recommendation; draft the plan as `Status: draft` with its
  `Parent:` line; show it to me; card it only when I say so and no question is open, one step at a time until I say
  otherwise.
- Every plan but the project's root names its parent. If the project has no root plan, write one with me first.
- A step is a numbered heading, a verb and an object, with Delivers, Done when and Tasks. Never renumber a step.
- My decisions go under Decisions with my words, the date and the time. After carding, a change of decision updates
  the plan in the same batch as the card.

**Cards.** Follow `jobs/card-template.md`. Every card has:
- `Category:` the letter of the category that owns the files it changes (`jobs/categories.json`);
- `Scope:` the exact files or folders it may change, as few as possible; `Locks: none` unless it must change another
  category's files, then those files. A worker of a card with locks on asks for such a file while it works, with the
  `lock` command its prompt names; this needs the project's locks on, which is the default, so check the day's log
  (`jobs/.logs/scheduler-YYYYMMDD.log`) for `locks: on` or `locks: off` before relying on it;
- `Type: feature` or `Type: bug`; `Plan: jobs/plans/<name>_plan.md, step <n>` (or `none (<who>, <date>)` for a one-off);
- `Kind:` one of the words in `scheduler.json`'s `cardKinds` (by default design, implementation, docs or check);
- `Fixes-red: yes` (optional) on the card that repairs a red main, and on no other: when main is red the project's
  dispatching pauses and every card is held except those with that header, which still follow every other rule. The
  daily log (`jobs/.logs/scheduler-YYYYMMDD.log`) says `dispatching paused: <project> main red at <sha> (<failing check>)`
  when the pause starts and `dispatching resumed: <project>` when a green check ends it, so look for those lines when
  work has stopped;
- `Worker:` chosen by `jobs/project.md`'s rules; `Time box:` in minutes; `Depends on:` cards or `none`;
- a Task a worker with no memory can do from the card, the plan and the repository; `Done when` as checkable facts.

**Numbers.** Never choose a card number. Name a new card `jobs/queue/new-<L>-<slug>.md` with the title line
`# new-<L>: <title>`, and refer to it as `new-<L>-<slug>` in the same batch; the scheduler numbers it.

**Workers and size.** Pick the cheapest worker that can finish the card in one session without guessing. Split big
work into a design card (strong model), small implementation cards (one file, class or rule each, fully specified,
30-60 minutes), and a review card at the end. A card a small model failed is split further or raised one step, with
a Dev note saying what was missing.

**Size cards from the estimates.** Before you set a card's `Time box:` or `Worker:`, read
`jobs/.logs/estimates.json` in the project folder (or `/estimates.json?project=<name>` on the scheduler's page).
It is the scheduler's record of past runs, not a forecast. Find the `runTime` entry for the worker type you are
considering and the card's kind. If it has no figure, use the entry for the card's category, then the pool's.
Set `Time box:` to the 90th percentile rounded up to the next 15 minutes. If there is only a median, use twice the
median. If there is no figure at all, use 45 minutes for a small card and 90 for a large one, and say so in
the card. Never set a time box below the median. Pick the worker type with `outcomes` in mind: a type with a high
`failed` or `timeLimit` share, or many cards needing more than one run, costs more than its run time says. Use
`review` for the date a card is really finished: it waits for the developer for about the median, and sometimes
the 90th percentile. Set `Kind:` on every card (one of the words in `scheduler.json`'s `cardKinds`, by default
design, implementation, docs or check), because a card without one only adds to its category's figure. When you
size a card from the report, say in the plan's step which figure you used.

**Writing changes.** Write card and plan changes as a batch in the outbox (`batch.json` written last; paths only under
`jobs/`; `base` is `git rev-parse origin/main:<path>` for a file you edited, `new` for a new one). Run `git fetch` or
read `origin/main` first, never a stale copy. Change a running card (`jobs/active/`) only by adding a `## Dev` note;
never rewrite its Task while a worker has it.

**Ask first** before anything expensive or hard to undo: costly worker types, many cards at once, deleting or rewriting
plans, changing `jobs/project.md` or `jobs/categories.json`, or anything outside `jobs/`.

**Answers.** Short and plain. Say what you found, what you propose, and what you need from me. Use my words for my
decisions and date them: `- <date> <time> (developer): ...` under a plan's Decisions, `(developer, <date> <time>)` in a
card.
```

Why each rule is there:

- **Read the README first.** Every card is read by a worker that follows `jobs/README.md` to the letter. A planner
  that has not read it writes cards that ask for things the rules forbid (moving a card, pushing red code).
- **Plans only in `jobs/plans/`.** The page's Plans view shows only those files, and a card's `Plan:` line must name
  one. Plans and cards together are the record of how the project was built. Plans nest under one root plan, each
  naming its parent step; [plans.md](plans.md) says how.
- **Precise Scope.** The scheduler runs two cards at once only when their scopes cannot touch the same file. A tight
  scope lets more cards run in parallel, and keeps a worker from wandering.
- **No numbers.** Two sessions that each took "highest number + 1" once wrote the same number and a card was lost.
  The scheduler now numbers every `new-*` card, so numbers never collide.
- **Ask first.** The planner can write twenty cards in a minute, and each one starts a paid session.
- **The estimates.** A `Time box:` picked from a guess is the usual reason a card ends over its limit, and a card with
  no `Kind:` line is counted with every other card of its category, so the figures get no finer. The scheduler keeps
  the record of what runs actually took at `jobs/.logs/estimates.json` and serves it at `/estimates.json`; a planner
  that reads it before sizing a card makes the queue's time boxes mean something.

## 4. Everyday use

Each exchange below is short on purpose: that is how a working session sounds.

### "Plan this feature"

> **You:** Tally should be able to skip words listed in a stop-words file. Plan it.
>
> **Planner:** Read the code: counting is in `Tally.Core/WordCounter.cs`, options in `Tally.Cli/Options.cs`. Plan,
> three steps: (1) `StopWords` class in Core that loads a file and answers `Contains`; (2) `WordCounter` takes an
> optional `StopWords` and skips those words; (3) `--stop-words <file>` option in the CLI. Steps 1-2 are category A,
> step 3 category B and depends on 2. One question: is matching case-sensitive? [no, ignore case | yes, exact]
>
> **You:** Ignore case.
>
> **Planner:** Written `jobs/plans/stop-words_plan.md` with your answer dated under Decisions. Card it?

The plan is a file you can read and change before anything runs. Answering the case question now saves a worker
from guessing it later (a guessed decision is the usual reason a card comes back).

### "Card the plan"

> **You:** Card it.
>
> **Planner:** Batch `20261122-101500-stop-words` written: `new-A-stop-words-class`, `new-A-counter-skips`,
> `new-B-stop-words-option`, all Spark, 45 minutes each. It is in the outbox; press Planner push if it is off.

The batch is a folder in the outbox: the new card files, and `batch.json` written last (its presence means the batch
is complete):

```json
{
  "message": "jobs: card stop-words plan",
  "trailers": [],
  "ops": [
    { "op": "write", "path": "jobs/queue/new-A-stop-words-class.md", "base": "new", "file": "f1.md" },
    { "op": "write", "path": "jobs/queue/new-A-counter-skips.md", "base": "new", "file": "f2.md" },
    { "op": "write", "path": "jobs/queue/new-B-stop-words-option.md", "base": "new", "file": "f3.md" },
    { "op": "write", "path": "jobs/plans/stop-words_plan.md", "base": "<blob sha from git rev-parse>", "file": "f4.md" }
  ]
}
```

Within a minute the scheduler's log says `planner: jobs: card stop-words plan`, then for each card
`card numbered: Tally/new-A-stop-words-class -> 1204-A-stop-words-class`, and the batch folder moves to `sent\`. If
anything in it is wrong, the whole batch is refused and moved to `failed\` with a `reason.txt`, for example
`write to docs/notes.md: only files under jobs/ can be written`, and the scheduler's log says
`planner: batch <name> failed: <reason>`.

A card before the planner shapes it, as you might have written it yourself:

```markdown
# new-A: Stop words
Category: A
Status: ready

## Task
Add stop words support.
```

The same card as the planner writes it for a small model; after the batch, the scheduler has given it its number:

```markdown
# 1204-A: StopWords class

Category: A
Status: ready
Depends on: none
Scope: code/src/Tally.Core/StopWords.cs, test/unit/Tally.Core.Tests/StopWordsTests.cs
Locks: none
Time box: 45 min
Worker: Spark
Plan: jobs/plans/stop-words_plan.md, step 1
Type: feature
Kind: implementation

## Task
Add `public sealed class StopWords` in `code/src/Tally.Core/StopWords.cs`:
- `static StopWords Load(string path)`: one word per line; blank lines and lines starting with `#` are skipped;
  words are trimmed.
- `bool Contains(string word)`: ignores case (developer, 2026-11-22 10:05).
Copy the style of `WordCounter.cs` (file-scoped namespace, one public type per file).

## Done when
- `StopWordsTests` has: Load_SkipsBlankAndCommentLines, Contains_IgnoresCase, Contains_UnknownWord_False; all pass.
- Both gates in jobs/project.md pass.

## Stop and ask if
- Anything specific to this job, on top of the README's stop conditions.

## Questions

## Result
```

The second card names the files, the members, the decision already made, an example to copy and the tests. A small
model can finish it without a single guess; the first card would come back as a question, or as a guess.

### "A worker asked a question"

A worker that needs a decision parks its card as `needs-human` with a question under `## Questions`, and the page lists
it under **Waiting on you**. You answer it on the page by picking a choice. The planner helps when the question is
technical:

> **You:** 1205-A asks: "Found: some stop-words files have a byte order mark. Question: strip it? [yes, strip it -> active | no, leave it]". What do you think?
>
> **Planner:** Yes: a BOM would make the first word never match. That is a fix inside the card's scope, so "yes, strip
> it -> active" resumes the card.

A worker that needs something only the planner can give (a decision the plan does not settle, a new plan) writes a
**request** instead: a new file `jobs/requests/new-<slug>.md`, never with a number of its own, and sends its card back to
`queue/` as `waiting: planner (request new-<slug>)`. Switchyard numbers the request when it reaches main, as it numbers your
cards, and rewrites that line to `waiting: planner (request #N)`; until then the page lists the request by its `new-<slug>`
name. A new request pushed with a number of its own is refused, and the refusal tells the worker to rename the file.
The page lists those under **Requests to the planner**.

> **You:** There is a request from 1206-B.
>
> **Planner:** Request #7: the worker asks whether `--stop-words` may be given twice. The plan doesn't say. Choices:
> [no, error out | yes, merge the files]
>
> **You:** Error out.
>
> **Planner:** Batch written: request #7 answered and set `Status: answered`, plan step 3 updated, 1206-B set
> `Status: ready`.

Setting a card ready through a batch also clears its run count, so a card that had run out of tries is released.

### "Review this"

A card the worker could not fully check (something you must see, a hypothesis) ends in `review/` with
`Status: review: <what to check>`.

> **You:** Review 1206-B.
>
> **Planner:** Read the Result and the two commits (`git log --oneline --grep 1206-B`). The option is parsed and
> passed through, a test covers the twice-given error, gates were green. One thing I can't check: the help text. Run
> `tally --help` and look at the `--stop-words` line. If it reads well, answer "done".

Ask the planner to check what a worker claims before acting on it: small models write a confident wrong reading as
easily as a right one ([agent-lessons.md](../agent-lessons.md), section 9).

### "Check in every 30 minutes"

If your planner can repeat a prompt on a timer (an agent session with a loop or schedule command), ask it to; otherwise
ask "check in" whenever you look. A check-in reads the page or `/status.json` and the cards on `origin/main`, and
reports only what changed and what needs you:

> **Planner (10:30):** 1204-A done. 1205-A running (Spark, 20 of 45 min). Waiting on you: nothing. Main green.
>
> **Planner (11:00):** 1205-A done; 1206-B in review, needs your eyes on the help text. Plan step 3 is the last.

### "Main is red"

After a run finishes the scheduler builds and tests main in its own worktree (the health check; **Check main** on the
page starts one). If it fails, the project pauses (`paused: main is red`), its Health cell on the page turns red with a
**log** link, and no new card starts until main is green again. The project's daily log says
`dispatching paused: <project> main red at <sha> (<failing check>)` when that pause starts and
`dispatching resumed: <project>` when a green check ends it, so those two lines are how you tell a red-main stop from
an empty queue. The card that repairs main carries `Fixes-red: yes` in its header: while main is red it is dispatched
and every other card is held.

> **You:** Main is red.
>
> **Planner:** The health log shows `StopWordsTests.Load_SkipsBlankAndCommentLines` failing on main at `3f2a1c0`, the
> commit from 1205-A. Revert that commit (`git revert 3f2a1c0`, by you, since I don't push code) and I send 1205-A
> back with what was found, or I write a fix card marked `Fixes-red: yes` so it starts while the project is paused?
> I suggest the revert: it restores a known good main now.

Reverting first is the rule that held up best ([agent-lessons.md](../agent-lessons.md), section 10). Once the fix is
on main, press **Check main**; when the check is green, the project resumes by itself. The page's Resume button also clears the pause, but without a
green check, so cards then run on a red main.

### "This card failed twice - what now?"

A run that ends without a result (crashed, stalled, timed out) counts against the card. After `maxRetries` of them
(2 by default) the card is **stuck**: the page lists it under Waiting on you as `stuck: 2 runs ended without a result:
needs you`, with its last log.

> **You:** 1207-A is stuck. What now?
>
> **Planner:** Both runs were Spark and ran out of time while reading the whole of `WordCounter.cs` and its tests. The
> card asks for two things (the skip, and a new overload). I'd split it into two cards of 30 minutes, each naming the
> member to change, and add a Dev note on 1207-A saying why. Or raise it to Sonnet as it is. [split it | raise to
> Sonnet | let me look first]

Don't press Retry on an unchanged card: a card a model failed twice usually fails a third time. Split it or raise it one
step (Spark -> Sonnet -> Opus -> Fable), and say why in a Dev note so the next card of that kind is written better.

## 5. Choosing workers and shaping cards

The worker types (which model each runs, how many at once) are set in Switchyard's pool file; a card's `Worker:`
line picks one by name (see [switchyard.md](switchyard.md)). Write your project's rules for picking a worker in
`jobs/project.md`, so the planner reads them every session. ProjectToolkit's own `jobs/project.md` ("How the planner
picks a worker and shapes cards for it") is a good start; in short:

- **Pick the cheapest type that can finish the card in one session without guessing.** Most work should reach the
  cheapest model, so the planner's job is to make cards it can do, not to send hard cards to big models.
- **Decompose:** one design or interface card (strong model) that names the types, files, rules and tests; then
  implementation cards (one class, file or rule each, cheap model); then a review card when the pieces must fit.
- **A card for a small model is fully specified:** 1-3 files in Scope; the code to change located (file and member);
  the test to add, named; no open design choices; an example to copy; Done when as checkable facts; 30-60 minutes.
- **Send to a bigger model** work that spans several areas, touches concurrency or shared state, needs a design
  decision, or that a smaller model already failed.
- **Size cards from the estimates:** read `jobs/.logs/estimates.json` in the project folder (or
  `/estimates.json?project=<name>` on the page) before setting `Time box:` or `Worker:`; the starter brief of
  section 3 says which figure to use. It is the scheduler's record of past runs, not a forecast.
- **Set `Kind:` on every card** (one of the words in `scheduler.json`'s `cardKinds`: design, implementation, docs or
  check), because the estimates are grouped by kind and a card without one only adds to its category's figure.
- **`Fixes-red: yes`** goes on the one card that repairs a red main and on no other (section 3's brief says why): while
  main is red the project holds every card except those marked so.
- **After a run** that went badly, add a short Dev note on what the card was missing.

## 6. What to watch for

- **Plans drifting from cards.** A card changed after a decision while its plan still says the old thing; the next
  card written from the plan undoes it. Ask the planner to update the plan section in the same batch as the card.
- **Number collisions.** A planner that picks numbers itself ("highest + 1") will sooner or later reuse one. Every new
  card must be named `new-<L>-<slug>`; if you see a numbered card in a batch the planner wrote, stop it.
- **Editing a running card's task.** A worker reads its card once, at the start. Rewriting the Task while it runs
  changes nothing for that worker, and its hand-over then replaces your text. Add a `## Dev` note instead: the
  scheduler keeps it when the worker hands the card back, and the next worker on the card reads it first (a Dev note
  wins over the card). To reach the worker that is running now, send it a message from its window on the page.
- **A stale copy.** A planner that reads your working tree instead of `origin/main` edits old text; its batch then
  fails as `changed on main since the planner read it, and the changes conflict`. Fetch, re-read, write again.
- **Decisions made for you.** A planner that writes "decided: ..." into a plan without asking. Your decisions carry your
  name and the time; anything else is a proposal.
- **Too much at once.** Twenty cards in one batch start twenty sessions. Card one plan step at a time until you trust
  the planner's cards.

## See also

- [plans.md](plans.md): what a plan is and how to write one.
- [`jobs/README.md`](../../assets/templates/jobs/README.md): the rules every worker follows.
- [Planner outbox design](../design/tdd/scheduler/planner-outbox.md): the batch format and what the scheduler does with it.
- [agent-lessons.md](../agent-lessons.md): what running workers taught, as rules.
- [getting-started.md](getting-started.md): the walkthrough from nothing to a first card done.
