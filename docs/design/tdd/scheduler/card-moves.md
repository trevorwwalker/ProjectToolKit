# Card moves written by the scheduler (roadmap item 2, card 660-B)

Part of the scheduler spec (`spec.md`, section A13). Moved here from `Scheduler.Core/Moves/` by card
720-D. Rules are CM-1 to CM-22; their tests are in `test-plans/claims-and-moves.md`. This adds to
MOVE-1 to MOVE-11, which still govern the page's own actions. It records the code as of card 660-B.

## Why

Workers moved cards with `git mv` in their own worktrees and pushed. Two failures came back on
2026-11-18: a claim that copied instead of moving (a `queue/` copy left beside the `active/` one,
cleaned up by the scheduler's duplicate fix), and cards that reached `review/` without their Result
(the move was staged from the last added copy, not the edited one). Both come from many writers
moving the same files. Now one writer does it: the scheduler, through its answers worktree, in
order, as it already does for the page's actions.

## The request

A request is a UTF-8 text file in the scheduler's `moves` folder, `<log folder>\moves\` (beside the
`stop-slotN` and `msg-slotN.txt` files workers already use). It is not in git and not in a worktree,
so a slot reset cannot lose it.

- **CM-1 Name.** `<yyyyMMdd-HHmmss-fff>-slot<N>.move`, the time from the writer's clock. A writer makes
  it as `<name>.tmp` and renames it to `.move`, so the scheduler never reads half a file.
- **CM-2 Header.** `Key: value` lines, up to a line that is exactly `---` or the end of the file.
  - `Card:` the card's file name without `.md` (required).
  - `To:` `queue`, `active`, `review` or `done` (required). `To` the folder the card is in already
    changes its text or Status only.
  - `From:` the folder the writer believes the card is in (default `active`).
  - `Slot:` the slot number, for the log (optional).
  - `Status:` the new Status, for a request with no text (optional).
  - `Claim: yes` marks the scheduler's own claim (CM-9).
- **CM-3 Body** (after `---`, optional): the card's whole new text. Without a body, main's text is
  kept and only Status (and the folder) change.

## What the scheduler writes

For each request, against main's current text of the card (read in the answers worktree):

- **CM-4** The text is the body when there is one, otherwise main's text. Any `## Dev` section on main
  that the body lacks is kept (put before `## Result`): the developer's replies are never lost to a
  worker that started before them.
- **CM-22 The body is merged, not pasted.** The body of a request becomes the card's text, except that main's questions survive it: each `- Qn:` line of main's `## Questions` section that carries an answer keeps its answered text and the indented lines under it, whether the body has the same question unanswered, reworded it or dropped it; a question main has and the body lacks is kept. A `## Dev` section still follows CM-4. Everything else - Status, Task, Result - is the body's. When an answered question was reworded or dropped, the log line names it.
- **CM-5** Status: `To: done` sets `done`; `To: review` keeps a Status starting `review`, otherwise
  sets `review`; otherwise the text's Status, or the `Status:` header when given.
- **CM-6** A claim adds `Claimed: <yyyy-MM-dd>, lane <L>` under Status when the card has no Claimed
  line.
- **CM-7** One commit, only the card's old and new paths, pushed to main (MOVE-1: up to three tries
  from a fresh main). Messages: `jobs: start|done|review|wait|status <card>`; the log line says which
  slot asked.

## Ordering

**CM-8** All waiting requests are applied one after another in file-name order (time first, then
slot), each on the main the previous one left. They are applied:

1. **CM-9** when the scheduler starts a worker on a queued card: it writes the claim request itself
   and applies it before the worker starts, so the worker's first pull already shows the card in
   `active/` and it never claims;
2. **CM-10** when a worker has ended, before its result is judged, so the finish the worker asked
   for is on main when the scheduler checks the card;
3. **CM-11** on every tick, beside the planner step, for anything left.

**CM-12** The worker never waits for an answer: a finish request is its last act, and the scheduler
applies it within the same tick the session ends in.

## Conflicts and failures

- **CM-13** The card is not on main: failed.
- **CM-14** The card is in `From`: applied.
- **CM-15** The card is already in `To` (a worker that moved it itself, or the same request twice):
  the text and Status are applied in place; a claim of a card already in `active/` is failed as
  "already claimed", so two claims of one card leave one copy.
- **CM-16** The card is anywhere else (a page action or another request moved it first): failed.
- **CM-17** A request that cannot be read (no Card, no or unknown To, unknown From) is failed.
- **CM-18** A failed request moves to `moves\failed\` with `<name>.reason.txt`, and the reason goes to
  the scheduler's log. A request that was applied moves to `moves\done\`. A commit that could not be
  pushed three times leaves the request where it is, to be tried on the next tick.

## A worker that still moves cards itself

- **CM-19** It keeps working: nothing stops `git mv`, and the duplicate fix (CARD-19) still removes a
  `queue/` copy. Its request, if it also writes one, finds the card already in `To` (CM-15) and only
  applies its text. One thing changes for it: the card is already in `active/` when it starts (the
  README's step 2 already says to skip the claim then), so a script that moves `queue/` to `done/` by
  a fixed path fails and the card is resumed. Under the scheduler the prompt tells workers to use
  requests; a worker run by hand (no scheduler) keeps the README's `git mv`.
- **CM-20** If the claim is on main but the worker then fails to start, the card stays in `active/`
  as ready and is resumed as an orphan on a later tick. If the claim cannot be pushed, the worker
  starts anyway and the claim is tried again on the next tick.

## Changes the toolkit's worker README needs (assets/templates/jobs/README.md)

- **CM-21** The README still tells workers to `git mv`; under the scheduler the prompt overrides it
  (known_gaps, 660-B (d)). The list of README changes is in card 660-B's Result; the README is the
  toolkit's and was not edited by that card.

## Open points

- A worker's finish request carries the card's whole text, which replaces main's apart from the
  `## Dev` sections (CM-4) and the answered questions (CM-22). Another change made to the card on main
  while the worker ran (an edit to its Task, say) is still lost.
- The prompt still says "claim it (step 3)" followed by "is done for you", because a prompt test
  asserts that wording.
