# Card moves written by the scheduler (roadmap item 2, card 660-B)

Part of the scheduler spec (`spec.md`, section A13). Moved here from `Scheduler.Core/Moves/` by card
720-D. Rules are CM-1 to CM-24; their tests are in `test-plans/claims-and-moves.md`. This adds to
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
3. **CM-11** on every tick, beside the planner step, for anything left, except the move requests of a
   card whose worker is still running (CM-12).

**CM-12** The worker never waits for an answer: a finish request is its last act, and the scheduler
applies it within the same tick the session ends in. Card 1237-B: a request (not a claim) of a card whose
worker is still running, or starting, waits in the folder until that run ends, and is applied in the
finish of that run (CM-10); requests of other cards pass it. Before 1237-B the planner step (CM-11) applied
any waiting request within seconds, so 1228-B was moved to `done/` and its scope released while its worker
was still pushing; the push was then refused and the code stayed on `wip/1228-B`. The tick hands the move
step the running cards on its own thread (`CardRequestStep.Running`); tests `..._CM12` in
`CardRequestStepSettleTests`.

**CM-23 Work not on main is not settled** (cards 1237-B, 1300-B). When a run ends with a waiting `To: done` or
`To: review` request for its card, the scheduler counts the commits made since the run started that main
does not have, on the slot worktree's HEAD or on a `wip/<card>` branch (`wip/<n>-<L>` or `wip/<n>-<L>-*`, on the
remote or local). If there are any, the request is sent back before it is applied: `To: queue` with
`Status: waiting: planner (finished, but its work is not on main: <where>)`, where `<where>` names the card's
wip branches on the remote, or says `only in the worktree slot<N>` when there is none, and a line is added to the
card's Result: `scheduler: the worker's finish asked for <done|review>/, but its work is not on main: <where>; it
waits on the planner (CM-23).` The card is listed under the cards waiting on the planner in the page's Requests to
the planner window and no worker takes it; the scheduler's log says which folder was asked for, in one line. (Card
1300-B replaced the earlier outcome, a park as `needs-human` in `active/`: in the developer's words, "cards that
haven't pushed to main can't be "done". Done implies the work was completed. I think a task that leaves its work
sitting in an incomplete state should be in a request state for planner to evaluate".) A card
whose run's commits are all on main, or that made none, is settled as before; so is one git cannot judge
(the slot folder is gone, git fails), with a log line. Only commits made since the run started count here; a
wip branch left from an earlier session is CM-24's.
Cost: no git on a tick without a finishing card, none for a finish that asks for `active` or `queue`; one
`git rev-list --count` in the slot worktree per finished card that asks for done or review, and a second
(`git for-each-ref`, to name the branch) only when commits are found. No fetch: the slot worktrees share the
repository's refs, so `origin/main` there is the newest main any worker pushed or the scheduler fetched.
Code: `Moves/UnpushedWork.cs`, `CardRequestStep.NotSettled`, `TickHost.CheckSettled`; tests `..._CM23` in
`CardRequestStepSettleTests`.

**CM-24 No card reaches done with its work off main, wherever the move comes from** (card 1300-B). A change
that moves a card into `done/` through the answers worktree - an answer from the page whose choice ends
`-> done`, the page's Done on a review card, or a move request (one CM-23 did not hold, as when the work is on a
wip branch from an earlier session) - is held while a `wip/<n>-<L>` or `wip/<n>-<L>-*` branch of the card, remote
or local, holds work `origin/main` lacks. The card goes to `queue/` instead with
`Status: waiting: planner (finished, but its work is not on main: <branches>)`; the change's other edits stay (an
answer is still written after its question), a line is added to the card's Result:
`scheduler: <an answer from the page | the Done button | a move request> asked for done/, but its work is not on
main: <branches>; it waits on the planner (CM-24).`, the commit is `jobs: wait <card> (...)`, and the log says
the same in one line. A branch "holds work main lacks" when it has commits main does not (one `git rev-list
--count` over the card's wip refs) and, of those branches (one `git for-each-ref`), the files it changed differ
from main as the wip cleanup judges them (1049-B), so a branch whose work reached main rebased or cherry-picked
does not hold the card; one git cannot compare counts as holding it. Git is asked only for a change that moves a
card into `done/`; when it cannot answer at all, the change is applied as asked, with a log line. A planner batch
that moves a card into `done/` is the planner's evaluation and is applied as it is; when the card still has work
not on main the log says `planner: <card> moved to done with work not on main: <branch>` (and the wip cleanup
keeps the branch and notes it in the Result, as before). Nothing changes for a card with no such work, for a park
(`needs-human` in `active/`), for `To: queue` or `To: active`, or for a wip branch whose work is all on main. A card
whose wip branch can never be landed (it conflicts with main) waits on the planner too; that is the point.
Code: `AnswersCommitter.Hold` (in every card update), `UnpushedWork.Wip`, the page's `CommitCard` (takes the edit as
committed, so the page shows the card in queue/); tests `..._CM24` in `AnswersCommitterUnlandedTests` and
`CardRequestStepSettleTests`.

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
