# Agent Instructions: Running AI Workers on a Large Codebase

What running an unattended agent job queue on AmigaX (an Amiga emulator in C++, worked by a small
local model through this toolkit's scheduler) taught, written as rules for applying the same
tooling to a larger, more complex codebase. [workflow.md](workflow.md) is the
workflow for a clean-sheet project; this document is about making that workflow hold up at scale.

> **If you are an LLM planning work for a codebase with this toolkit:** apply these rules when you
> write cards, briefs and scheduler settings. They exist because each one was learned the hard
> way.

## 1. Size the work to the model, not to the problem

The single biggest improvement on AmigaX came from splitting the work, not from tuning anything.
Open-ended cards ("scout this game", "investigate this fault") ran four hours and produced
nothing; the same work split into three cards of an hour each finished.

- **One card, one question, one deliverable.** A card a small model can finish names the files
  and functions involved, says exactly what "done" means, and has an obvious stopping point.
- **Time-box every card** (`Time box: N min`), set by the planner from measured completion
  times, and **tell the worker its deadline and warn it before the end**. On AmigaX the longest
  card that ever succeeded took 229 minutes and every session past the 240-minute cap failed; the
  three cap kills in one day all ran on cards with no time box, so their workers never knew a
  deadline, and each lost its whole session. A warning file 20 minutes before the limit, checked
  at every step, turns a kill into a pushed journal and a clean park.
- **Evidence before fixes.** Split a hard fault into evidence-only cards (reproduce, census,
  trace, isolate), each with its own notes file, and write the fix card only once the evidence
  is in.
- **Watch the completion rate by card type.** A type of card that keeps ending without a result
  is too big for the model. Split it; don't retry it.

- **Mean plus three standard deviations is no limit for task times**: they are skewed. Use the
  longest success in a recent window, and judge changes against a dated baseline.

## 2. The repository is the memory

Every worker starts cold. Everything it needs has to be in the repository, and short enough to
read.

- **Keep the worker protocol small** - under 10 KB. Agent tools have read limits (20 KB on
  AmigaX's), and a small model loses the rules in a long brief. Move history, plan mappings and
  operating manuals for people into separate files.
- **Journal in the card.** Every commit adds one dated line to the card's Result: what is done,
  what is next. A resumed session starts from that and nothing else; unpushed work is lost work.
- **Write findings where the next card will look.** Scout reports, notes files and checkpoint
  folders are named by convention, and every card about that subject is told to read them first.

## 3. Ownership boundaries replace coordination

Agents working at the same time must never need to talk to each other.

- **Lanes own disjoint sets of files**, drawn along module or service boundaries. One worker per
  lane at a time; lanes run in parallel.
- **Cross-lane edits need an explicit lock** on the card (`Locks: file: functions`), and the lock
  holds only while the card is active. No lock for a file you need: park and ask.
- **Shared interface files are append-only**, pulled immediately before and pushed immediately
  after an edit.
- **Behaviour changes have a single home.** A change that moves a test baseline belongs to one
  lane, so a moved baseline always points at one commit.

## 4. Keep a person where judgement beats search

On AmigaX the developer played eight games in half an hour and answered eighteen cards that
workers had spent hours failing at. The model was bad at steering a game; the person was fast.

- **Find the steps that are cheap for a person and expensive for a model**: reproducing a UI bug,
  judging whether output looks right, getting a program into a particular state, choosing between
  designs.
- **Turn the person's minutes into reusable artifacts.** Input recordings with their starting
  state, saved states, golden files, annotated screenshots - stored where cards look first.
- **Give agents an explicit way out**: "needs-human: play to X and save a state" instead of
  guessing. The scheduler shows parked cards; the person answers and sets them back to ready.
- **Make answering cheap.** Workers write each decision as a multiple-choice question in the card;
  the page turns them into buttons, commits the answer and moves the card. The person replies at
  length in a `## Dev` section and presses Resume, Send back or Done; the scheduler commits the
  edit. A person who has to type git commands to answer a card answers fewer cards.
- **Check before you build on a person's recording**: replay it once and compare. AmigaX's first
  recording showed two bugs (mixed mouse modes, a snapshot that refused to load across clocks)
  that would have poisoned every later replay.

## 5. Make everything replayable and checkable by a machine

Unattended work can only be trusted if a machine can verify it.

- **Determinism**: the same inputs give the same outputs, run after run - virtual time, recorded
  inputs, snapshots, fixtures for external services, hermetic builds.
- **One gate command** that every change must pass before it is pushed, quiet when green and
  pointing at the failing log when red.
- **A health check on main** after every finished card; red pauses dispatching until it is green
  again.
- **Fixtures as starting points.** Named snapshots of interesting states let a card start where
  the work is instead of re-deriving the setup every session.

## 6. Measure the pipeline, not just the code

Tune from numbers, not impressions.

- **Accounting**: one row per finished card - result, minutes, log size, why it was stopped.
  Completion rate by card type is the most useful single number.
- **Model server metrics** sampled every minute: requests running and waiting, KV-cache use,
  prompt and generation tokens, prefix-cache hits, preemptions. On AmigaX these showed the server
  capped at 3 concurrent requests and 2,048-token steps, which explained slow first answers that
  looked like stalled workers.
- **Structured worker output** (JSON events) rather than terminal text, so silence can be told
  apart from thinking, and slow model calls are visible in the log.
- **Choose the worker count by measurement.** Add workers one at a time; the right number is the
  last one at which total tokens per minute still rises without the KV cache filling.

## 7. Build the scheduler to fail safely

Most of the scheduler's bugs were at the edges, not in the main path.

- **Persistent state** for everything that must survive a restart: running workers (adopted, not
  restarted), retry counts, stop requests and their deadlines.
- **One instance per project**, enforced; a second waits for the first to exit.
- **Liveness by fresh process lookup** with the start time checked, never by a cached handle.
- **Every stop is resumable**: cooperative first (a flag file the worker checks), then a kill,
  with the card left for resume. Kill once, then release the slot.
- **Stuck is surfaced, never retried for ever**: after N runs without a result, a card waits for
  a person.
- **Priority first** in dispatch and in what the page shows; a low-priority leftover never holds
  a lane against urgent work.
- **Version the scheduler** (Major.Minor.Rev) and show the version on its page, so a report can
  always say which build was running.

- **Keep the scheduler's own page fast.** Anything slow in its loop (a process per card read, a
  metrics call with a long timeout) blocks every click behind it. Cache by content, rate-limit
  sampling, answer the page before a tick, and log any tick that runs long.
- **Configuration a model must understand lives in the repository as JSON** (`lanes.json`,
  `workers.json`), not in a Markdown table and a local settings file. Redefining a lane that has
  pending cards is held until a person accepts it.

## 8. A strong model plans, a cheap model executes

- **The planner** (a frontier model with the developer): splitting work, writing cards, reading
  evidence, reviewing hypotheses, answering requests, fixing the tooling.
- **The workers** (a small or local model): tightly specified cards, one at a time.
- **Review hypotheses before acting on them.** A worker's plausible explanation can be wrong - on
  AmigaX a scout blamed a read length that was normal; the likelier cause was copy protection.
  Cards that end in a hypothesis go to `review/`.

## 9. Check what workers tell you before acting on it

A small model writes a confident wrong reading as easily as a right one, and a card built on it
wastes a session or breaks the build. All of these happened on AmigaX in two days:

- **Hardware readings.** A worker called a standard 68000 instruction 68010-only; another decoded
  a legal-looking opcode with the wrong addressing mode and set out to "fix" a correct decoder; a
  third logged an operating system's deliberate trap as an emulator fault, and a later card built
  its root-cause theory on that. Check opcode bits, register numbers and "this can't happen on
  hardware" against the manual before any card acts on them.
- **Proposed fixes.** A correct diagnosis (a blitter register overwritten at the end of a blit)
  came with a fix that would have broken a common idiom (chained blits that rely on the advanced
  pointers). Review the fix, not only the finding.
- **Reference data.** "Identical in two runs, so not timing" was taken from a deterministic
  reference emulator, which proves repeatability, not independence from timing - and timing was
  the cause. Ask where a reference figure came from and what it can show.
- **Missing sources.** A worker that cannot read a manual reconstructs the tables from memory and
  gets them wrong. Make sure the authority is in the repository in a readable form, and have the
  worker transcribe it to text before building on it.

## 10. Gates pass; check what the person sees

Green gates can still ship something unusable.

- **Measure what the person uses.** A presenter-thread change passed every gate and a stress test
  of responsiveness, then showed a black window: no check looked at the picture. Every card that
  touches what the person sees needs a check of the visible result (a screenshot with content in
  it), at rest as well as under stress.
- **A/B builds find a regression fastest.** Two builds a commit apart, run by the person for a
  minute, settled two regressions in minutes after workers' reasoning had not. A headless run of
  the same build splits "the program" from "the window" in one step.
- **Revert first, then fix.** When main is broken for the person, restore the last good state and
  send the card back with what was found; fixing forward under pressure stacks risk.
- **Test rigging comes out.** A worker's load generator, kept running, slowed the person's PC;
  measurement scaffolding must be removed before a card finishes, and the card says so.
- **Never leave the person's working tree dirty.** Workers push continuously, so the person's
  pushes race theirs: give them a pull-rebase-push loop, and have tooling commit from its own
  worktree rather than editing the person's.

## 11. Tuning lanes, workers and the workflow

From the developer's own notes (formerly BestPractices.md).

- **Tune the workers to the work and the machine.** Adjust how many there are to the workload and
  to what the PC and the model server can carry; watch the accounting and the server's load, and
  change the count when a bottleneck shows.
- **Add frontier (premium) workers for the hard cards.** Mark them premium in `workers.json`; a
  card that needs one asks with `Worker: premium` (or names the worker). Premium workers take no
  other cards, so their cost goes only where it is needed.
- **Divide lanes by code that belongs together**, so cards that can run at the same time do; when
  the kind of work changes, redraw them.
- **Let a model write the configuration.** Hand it the codebase and have it draft `lanes.json`
  and `workers.json`; review them before the scheduler reads them from main.
- **Plan with the best model, then let it card the plan.** A frontier model writes the plan and
  turns it into cards that use the right lanes and workers (section 8).

## Starting on a large codebase

In this order:

1. **Draw the lanes** along module boundaries, and list the shared interface files.
2. **Write the worker protocol**, under 10 KB, from `assets/templates/jobs/README.md`.
3. **Build the single gate command** and the health check.
4. **Add the replay and fixture tooling** the domain needs, and the human-artifact folders
   (recordings, states, notes) with their naming convention.
5. **Run the scheduler on one lane** with small, time-boxed cards. Read the accounting after a
   day, fix the card format, then widen to more lanes and workers by measurement.
