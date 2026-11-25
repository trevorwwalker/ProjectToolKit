# Plans: what a plan is and how to write one

A plan says where a piece of work is going and the steps that get it there, from the general to the specific. The
specific end of a plan is **job cards**, the small tasks Switchyard's workers run. This page is for a developer
and for a planner LLM writing plans with them. The template is [`jobs/plan-template.md`](../../jobs/plan-template.md);
how the planner works day to day is in [planner.md](planner.md).

## 1. What a plan is

A plan is one Markdown file in `jobs/plans/`, named `<name>_plan.md`. It has a **goal**, then a sequence of
**discrete steps**. A reader sees the progression and drills down:

```
root plan  ->  step  ->  child plan  ->  step  ->  cards
```

The page's Plans view shows that path: each plan with its steps, each step with its cards, and how many are done.

**What is not a plan:**

| It is | Where it goes |
|---|---|
| A design document (how something is built) | `docs/design/`. A plan links it under `Inputs:`; it never copies it. |
| Research notes | The `## Result` of the card that did the research. The step keeps one `Found:` line pointing at it. |
| A bug, or a one-off | A card with `Plan: none (<who>, <date>)`. |

## 2. SMART, field by field

A good plan is Specific, Measurable, Attainable, Relevant and Time-based. Each letter is a field you fill in, not a
feeling you check at the end.

| Letter | Where it lives in the plan |
|---|---|
| **Specific** | The `Goal:` line and each step's title. A title is a verb and an object and names what it delivers: "Write the plan template", not "Templates". |
| **Measurable** | `Done when:` on every step, written as facts a person or a test can check. The Plans view also shows done cards out of created cards. |
| **Attainable** | A step breaks into cards that one worker finishes in one session. Something not yet known becomes a step called "Investigate ...", not a hope. |
| **Relevant** | The `Why:` line, `Scope:` and `Out of scope:`. A step that cannot say how it serves the goal is not a step. |
| **Time-based** | Order and `Depends on:`, with an `Estimate:` taken from the cards' time boxes. Plans carry no target dates. |

## 3. The header lines

Every plan starts with a title line, `# Plan: <the goal as an end state>`, then these lines, one short line each:

- **Status:** one of `draft`, `ready to card`, `carded <date>`, `on hold: <why>`, `done`. A plan starts as `draft`.
- **Parent:** `jobs/plans/<parent>_plan.md, step <n>`, the one step this plan carries out. A root plan leaves it out
  (see section 5).
- **Goal:** one sentence: what is true when the plan is finished, and how that is checked.
- **Why:** how this serves the parent step (or, for a root, the project).
- **Scope:** the categories and folders the plan changes.
- **Out of scope:** what it must not change.
- **Inputs:** design and research documents and earlier cards. Links, not copies.

Then two sections that are not steps:

- **`## Decisions`**: the developer's decisions, each dated and in the developer's own words, as
  `- 2026-11-23 22:30 (developer): ...`. Anything the planner suggests that the developer has not decided is a
  proposal, and does not go here.
- **`## Open questions`**: each as `- Q1: <question> [<choice> | <choice>] Recommended: <choice>, because <reason>`,
  with two to four choices and a recommendation. When the developer answers, the answer moves to Decisions. A plan
  with open questions is not ready to card.

## 4. Steps

Only a `##` heading that **starts with a number** is a step: `## 1. Write the template`. A `###` heading under it
is a sub-step (`### 1.1 ...`, numbered n.m). Any other heading (`## Decisions`, `## Notes`) is not a step, and the
Plans view does not count it. (A plan with no numbered `##` heading at all is read the old way, every `##` a step, so
plans written before this rule keep their steps. Write new plans with numbers.)

A step has these fields, in this order:

| Field | What it holds |
|---|---|
| `Delivers:` | What exists when the step is done. |
| `Done when:` | Facts a person or a test can check. |
| `Depends on:` | Steps, in this plan or another, or `none`. |
| `Estimate:` | Minutes, until carded; then the cards' time boxes. |
| `Tasks:` | The intended cards, one per line: title, category, worker type. Written before carding. |
| `Found:` | One line per finished research card, naming the card and what it found. |

**A plan never lists card numbers.** The Plans view finds a step's real cards from their `Plan:` line
(`Plan: jobs/plans/<name>_plan.md, step <n>`), so `Tasks:` is only the intention.

**Steps are append-only.** Never renumber or reuse a number, because cards and child plans point at step numbers. A
step that is dropped stays, marked as dropped. The order of work comes from `Depends on:`, not from the numbers, so a
step added at the end can be the first to run.

## 5. Plans nest

**The first plan of a project is its root plan**, and writing it is one of the most important first steps: write the
design (`docs/design/gdd/<name>.md`), then have a planner write the root plan from it, before any worker is set up
or any card is written. The walkthrough shows it:
[getting-started.md, section 4](getting-started.md#4-the-design-and-the-projects-plan).

- **One root plan per project.** Its goal is the project. It grows as the work is found: new needs become new steps.
- **Every other plan has one `Parent:` line**, `Parent: jobs/plans/<parent>_plan.md, step <n>`, naming exactly one
  parent. A plan with no `Parent:` line is a root, and a project normally has one.
- **A step is carried out by cards, by a child plan, or both.** A step too large to list as `Tasks:` gets a child
  plan.
- **There is no fixed depth.** Three levels (root, child, grandchild) is usually enough.
- Plans are not merged. Each stays its own file; the `Parent:` line is how the page connects them.

## 6. Ready to card: a checklist

A plan can be carded when all of these are true:

- [ ] The Goal is one sentence and says how it is checked.
- [ ] There are no open questions.
- [ ] Every step is a verb and an object, and has Delivers, Done when and Tasks.
- [ ] Every decision is dated and says who made it.
- [ ] Scope and Out of scope are filled in.
- [ ] The Parent step exists (or the plan is the project's root).

Then show the plan to the developer, set `Status: ready to card`, and card **one step at a time**.

## 7. After carding

- Set `Status: carded <date>` once the cards are made.
- A decision made later is added to Decisions (dated, with who), and the step it changes is updated in the same
  batch as the card that follows from it.
- A new need is a new step at the end, never a rewrite of an old one.
- A finished plan stays in `jobs/plans/` for good. It is the record of how the work went.

## 8. A worked example

The Tally stop-words feature from [planner.md](planner.md), as a child of Tally's root plan. The file is
`jobs/plans/stop-words_plan.md`. It is shown in a longer fence because it contains headings of its own.

````markdown
# Plan: Tally can skip the words in a stop-words file

Status: ready to card
Parent: jobs/plans/tally_plan.md, step 2
Goal: running `tally --stop-words stop.txt notes.txt` leaves out every word listed in stop.txt, ignoring case, and the unit tests for the Core and the command line pass.
Why: Tally's step 2 is "Make the counts useful"; stop words are the first thing users ask for, so this is the largest part of it.
Scope: `code/src/Tally.Core/`, `code/src/Tally.Cli/` and their test projects (categories A and B).
Out of scope: stemming, other languages, and how results are printed.
Inputs: `docs/design/stop-words.md`.

## Decisions

- 2026-11-22 10:05 (developer): matching ignores case.

## Open questions

None.

## 1. Add a stop-words list to the Core

Delivers: a `StopWords` class in `Tally.Core` that loads a file, one word per line, and answers `Contains(word)`.
Done when: its unit tests pass, including upper-case, lower-case and blank-line cases.
Depends on: none
Estimate: 45 min
Tasks:
- StopWords class and its tests (A, Spark)

## 2. Make the counter skip stop words

Delivers: `WordCounter` takes an optional `StopWords` and does not count a word it contains.
Done when: a counter test shows a listed word absent from the result, and the same text without a list counts it.
Depends on: step 1
Estimate: 45 min
Tasks:
- WordCounter skips stop words, with tests (A, Spark)

## 3. Add the --stop-words option to the command line

Delivers: `tally --stop-words <file>` loads the list and passes it to the counter; a missing file gives a clear error and exit code 2.
Done when: the CLI tests pass for a good file and a missing file, and a run on a sample file leaves the listed words out.
Depends on: step 2
Estimate: 45 min
Tasks:
- Option parsing and error message (B, Spark)
````

The example passes the checklist in section 6: one goal sentence with its check; no open questions; each step is a
verb and an object with Delivers, Done when and Tasks; its one decision is dated and names who made it; Scope and Out of
scope are filled in; and the parent step (`tally_plan.md`, step 2) is the one the plan carries out.
