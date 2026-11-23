# The job queue

This file is every worker's whole brief. It is the same for every project built with ProjectToolkit;
what is particular to this project is in [`project.md`](project.md) (gates, locations, briefs),
which a worker reads next, and in [`categories.json`](categories.json) (the categories and the files each owns).

Its master is ProjectToolkit's `assets/templates/jobs/README.md`. Fix that and run
`<ProjectToolkit>\bin\projecttoolkit.ps1 -Action jobs -Repo <this repository> -Update`; never edit this copy, or the next
project gets the old rules.

Plans are made with the developer and written into `docs/ai/`, which holds only new plans. The
planner (the frontier model, working with the developer) turns each one into **job cards** in
`queue/` and moves the plan to `plans/`. Workers take cards, do the work, and move each card along:

```
queue/  ->  active/  ->  done/          finished and verified
                     ->  review/        finished, but needs the developer's hands (manual checks)
```

| Folder | Holds |
|---|---|
| `docs/ai/` | New plans, not yet carded. Workers don't take work from here. |
| `queue/`, `active/`, `review/`, `done/` | The cards. |
| `plans/` | Plans that have cards. A card's `Plan:` line points into here. |
| `archive/` | Plans whose cards are all in `done/`. |
| `requests/` | Requests to the planner, one file each (see its README). |
| `project.md` | This project's gates, locations and standing briefs. |
| `categories.json` | The categories: what each is for, the files it owns, its exceptions, and the shared files. |
| `workers.json` | The scheduler's workers: how many, and each one's server, model and agent; a worker type takes a card of any category. |
| `scheduler.json` | The scheduler's settings: worker command, default model, health check, limits. |

## Categories

Several workers run at once. A **category** is a label for a kind of work: a set of files and folders (its default scope, which a card's `Scope:` or `Locks:` line can replace) that only one card at a time
may change; `categories.json` defines each category (`owns`, `except`) and the shared files, and your prompt
states your own category's.

- **Owned files.** A card edits only files its category owns, plus the `shared` files in `categories.json`
  (add to those; don't rewrite them; pull immediately before and push straight after).
- **Locks.** A card that must change another category's files names them in its `Locks:` line. While it
  is in `active/`, other categories stay out of the locked files. Before editing any file outside its
  category, a worker checks `active/` for a lock on it; if there is none, it parks the card and asks.
  A card parked as `needs-human` releases its locks; whoever resumes it checks them again.
  Under the scheduler with locks switched on, a file you need that your card does not hold is asked for with
  the `lock` command in your prompt instead of parking (see "Locks while you work" in step 4). Locks are per repository.
- **Cross-category cards keep workers busy.** The planner may give a card with a predictable footprint
  to whichever category would otherwise be idle, with a `Locks:` line for the files it changes.
  Open-ended work (bring-up, fault hunts) stays in its home category.
- **Behaviour changes are attributable.** A card that changes the program's behaviour is in the
  category that owns that behaviour, or locks every file it changes.
- **Never** edit another category's cards in `queue/`, except to tick a dependency.
- **Plans** in `plans/` are the planner's. A worker may update a plan's progress or status lines
  for its own card; anything more goes to a request.

## A job card

File name: `<n>-<L>-<slug>.md`. `<n>` is a positive integer of any width (no padding), the card's identity only,
and `L` is the category's letter. The card's header says `Category: <L>`; the older `Lane: <L>` is still read as the same field, so old cards keep working. A new card takes the highest number in use (queue, active, review, done and archive)
plus one: no gaps, no renumbering, numbers never restart. Order is `Priority`, then `Depends on`, then the number;
to run a card earlier, give it a priority or make others depend on it, never renumber. Finished cards may be swept
from `done/` to `archive/` to keep folders small; their numbers stay used. An optional `Priority: high` puts a card
ahead of every normal one, and `Priority: low` behind them. `Time box: N min` is set by the planner; without one the session
gets the scheduler's `maxMinutes`. The template is [`card-template.md`](card-template.md).

## Who moves the cards

The scheduler is the only writer of card moves (the design is
`docs/design/tdd/scheduler/card-moves.md`, rules CM-1 to CM-22). A worker never moves a card and
never commits its own last edit to one: it asks for the move with a **move request**, a file it
writes into the scheduler's moves folder, which your prompt names. The request is not in git and not
in a worktree, so a slot reset cannot lose it. Write it as your last act, in one command:

```powershell
$t = Get-Content -Raw -Encoding UTF8 <the card's path>
$f = '<the moves folder from your prompt>\' + (Get-Date -Format yyyyMMdd-HHmmss-fff) + '-slot<N>'
Set-Content -Encoding UTF8 -NoNewline -Path ($f + '.tmp') -Value ('Card: <n>-<L>-<slug>' + [char]10 + 'To: <queue|active|review|done>' + [char]10 + '---' + [char]10 + $t)
Move-Item ($f + '.tmp') ($f + '.move')
```

- `Card:` is the card's file name without `.md`, `To:` the folder you want it in, and everything
  after the `---` line is the card's whole new text. Edit the card in your worktree first (`Status:`,
  `## Questions`, `## Result`), then hand that file over whole; the scheduler commits it, with your
  text, as `jobs: start`, `jobs: done`, `jobs: review`, `jobs: wait` or `jobs: status` on the main
  branch. It puts back any `## Dev` reply and any answered question the developer put on `main` while
  you worked (CM-4, CM-22).
- `To: done` and `To: review` finish the card; `To: active` parks it (`needs-human`, where it stays)
  or stops it on request; `To: queue` sends it back (`waiting`, for the planner or another category).
- Journal lines stay yours: you commit and push them yourself, to the card where it is
  (`git commit -m 'jobs: journal ...' -- <the card's path>`). A journal line is never a move.
- You never wait for an answer (CM-12): the scheduler applies requests in the order they were
  written, once your session ends, before it judges the result.
- Run by hand, with no scheduler to read the folder, you are the writer instead: move the card with
  git in one commit that `git add`s both the old and the new path, so the move is a commit like every
  other state change.

## Taking a job

1. **Pull.** `git pull --rebase` on the main branch. A conflict in a file your category does not own:
   park and ask.
2. **Pick the card.** Under the scheduler, the card is assigned to you and already claimed: the
   scheduler moved it into `active/` and gave it its `Claimed:` line before your session started, so
   go straight to step 4. Run by hand: first resume your category's unfinished card in `active/` (never
   one marked `needs-human`); otherwise take the lowest-numbered card in `queue/` that is in your
   category, says `Status: ready`, has every `Depends on` card in `done/` or `review/`, and has `Locks`
   that don't overlap an active card. Skipping a waiting or blocked card is intended.
3. **Claim it (by hand only).** Under the scheduler the claim is not yours to make: the scheduler
   writes it (CM-9) and a second claim of the same card is refused as "already claimed" (CM-15), so
   a worker leaves the card where it is. Run by hand, move it into `active/` yourself, add
   `Claimed: <date>, lane <L>`, commit as `jobs: start <n>-L-slug` and push straight away. If the
   push is rejected, pull and check the card is still free; if it is, push again, if not, pick again.
4. **Do the work.** Follow the plan the card points to and the project's rules (`project.md` lists
   the standing briefs). Anything that changes behaviour gets a test first; one root cause per
   commit. Push each commit when it is green. **Give every build, test and program run a timeout** of
   minutes, not hours: a hung command keeps you from seeing a stop request, and the scheduler's
   watchdog then kills the whole session. **Keep a journal:** with every commit, append one
   line to the card's `## Result` saying what is now done and what is next.
   - **Deadline.** Your prompt gives your time limit. Near it the scheduler writes `warn-slotN`
     beside its stop file: journal, push, and finish or park. Never touch either file.
   - **Messages.** `msg-slotN.txt` there is from the developer: read it, move it to
     `msg-slotN.read.txt`, answer it in your log, then act on it.
   - **Locks while you work.** Only when your prompt names a `lock` command (the scheduler has locks switched
     on); run by hand, or with no such command, skip this and use the park rule below. You hold your card's
     scope for the whole session; reading, building and testing any file needs no lock. Before you change,
     add or delete a file outside what you hold, ask for it with one command (the exe's full path is in your prompt):
     `<exe> lock <path> [<path> ...] --why "<one line>"`. Name the files or the folder you need, never the
     repository root, `**` or a whole category (that is refused as too broad). It waits up to 110 seconds,
     prints one line every 30, and ends with an exit code. Read the code:

     | Exit | Answer | You then |
     |---|---|---|
     | 0 | `granted` | edit the files. |
     | 2 | `waiting: held by <card>, gives up at <time>` | run the same command again (it keeps the open request and waits on). |
     | 3 | `yield: <path> held by <card> (older)` | commit your work to `wip/<card>` (never main), write in Result where you stopped and what you were about to change, hand the card back with a move request `To: active` and `Status: ready`, and end with `LANE-RESULT: yielded`. |
     | 4 | `refused: <reason>` | narrow the request, or park and ask. |

     Never skip the command: a push to main that touches a file you do not hold is refused, naming the file and
     how to ask for it. Your own card file in `jobs/`, `jobs/requests/` and the shared add-only files need no lock.
     Yielding is not a failure: the card returns later and keeps the files it asked for.
   - **Keep output small.** Everything a command prints lands in your context and the log. Send
     long output to a file and read it 50 lines at a time; pipe builds and tests through a tail.
   - **Rigging comes out.** Load generators, probes, temporary counters, logging and environment
     hooks added only to measure are removed before the card leaves `active/`, and any process
     you started is stopped. Result says what was removed.
5. **Gates, before every push:** the commands `project.md` lists under "Gates". Use no more threads
   than the scheduler gives you (the environment variable named in `scheduler.json`'s
   `jobsEnvVar`).
6. **Finish.** Fill in `## Result` and set `Status:` - `done` if every check passed and none needs a
   person, `review: <what the developer must check or decide>` if it does. Then ask for the move with
   a request (`To: done` or `To: review`, [Who moves the cards](#who-moves-the-cards)): edit the card
   in your worktree, hand the whole file over, and leave that last edit uncommitted - the scheduler
   commits it as `jobs: done <n>-L-slug` (or `jobs: review ...`) when your session ends. Run by hand,
   move it to `done/` or `review/` yourself, commit it, and check with `git show --stat HEAD` that the
   commit lists the card with changed lines: a move with 0 changed lines means the edit was never
   committed (staging a move stages the file as it was last added, not your unsaved edit), and the
   slot reset will throw it away.
   **Questions.** Every decision you need from the developer, in `review/` or as `needs-human`,
   goes under `## Questions` (above `## Result`), one per line with 2-4 short choices:
   `- Q1: Accept the fix? [yes -> done | no, redo it -> queue | ask me later]`. `-> done`,
   `-> queue` or `-> active` (resume now) moves the card when chosen. The developer answers from
   the scheduler's page; the answer is written after the question.
   **Developer replies.** A section headed `## Dev` is the developer's reply. Read every one
   before acting on a card; where it conflicts with the card, it wins. Never edit or remove it;
   answer it in Result.
7. **Archive the plan** if every card naming it is now in `done/`: plans are not cards, so this move
   stays yours - move it to `archive/` with git and commit it with your own work. When claiming a
   card, also archive any plan whose cards are all in `done/`.
8. **Next.** Under the scheduler, stop: the next card is someone else's session. Run by hand, go
   back to step 1.

## Stop conditions

**Finish the session**, with the report below, when there is no card your category can take, or after
finishing a card once your context has been compacted twice.

**Park the job and ask** when a decision belongs to the developer (behaviour the plan doesn't
settle, a trade-off, a change of scope); you need a file outside your category that no active card has
locked and you have no `lock` command to ask for it; three attempts to localise a fault have not converged; a fix needs an architectural change
the card doesn't ask for; a gate fails and the failure is not yours or can't be fixed within the
card; or a rebase conflicts in a file your category doesn't own. Set `Status: needs-human: <question>`,
put the question under `## Questions` and what you found in `## Result`, leave the card in
`active/`, and hand it to the scheduler with a move request `To: active` (run by hand, commit and
push it there yourself). A parked card does
not end the session when run by hand: take the next card.

**Hand to the planner** for what only the planner can do: a decision a plan doesn't settle that the
references can't settle either, or a new plan. Write a request file in `requests/` - that file is
yours to commit and push - set the card to `Status: waiting: planner (request #N)`, and hand the card
back with a move request `To: queue` (run by hand, move it back to `queue/` yourself). Then take the
next card.

**Stop when asked.** Under the scheduler, a stop flag file named in your prompt means: stop at the
next safe point, push only green work, note in the result where to resume, leave the card in
`active/` as `ready`, hand it over with a move request `To: active`, and end.

## Version

If the project has a version (`Major.Minor.Rev`, written in one place only), every card that changes
shipped code follows this rule.

- **Bump.** A card that changes shipped code (anything that changes behaviour in the product, not tests,
  docs or jobs) bumps the version as its **last commit** before finishing. **Rev** for a fix, or a
  behaviour change that keeps every interface working (config files, page routes, card format, command
  line). **Minor** (Rev back to 0) when such an interface changes in a way old files or callers no longer
  work with. **Major** only when the card says so: the planner sets it for a major change in functionality.
- **No bump** for tests-only, docs-only and jobs-only changes.
- **Pull right before the bump.** If the push is rejected, pull and bump again from main's value; never
  lower it.
- **Say it.** The card's `## Result` names the new version.

## Ending a session

Report: the cards finished and put up for review, with their commits; the card in progress and why
it stopped; requests added; what your category's next card is. Under the scheduler, end with the
`LANE-RESULT:` line your prompt asks for (`yielded` is one of its words: the card gave up a file to an older card,
see "Locks while you work").

## The scheduler

ProjectToolkit's scheduler (`scheduler-next.exe`, run for this repository) runs the queue unattended and serves a status page with controls
(`http://localhost:<port>/`). Workers are fresh agent sessions, one per card, each in its own git
worktree slot; a card's scope keeps cards apart; it resumes orphaned cards, watches for hung workers, shares the
CPU cores between workers, checks that the main branch stays green, pauses when the agent account
is out of quota, and records every card in `accounting.csv`. Its settings are in `scheduler.json`;
the workers (server, model, agent) are in `workers.json`. From its page the developer answers
questions, resumes parked cards, sends review cards back, and messages a running worker.
It is the only writer of card moves: a worker asks for one with a move request in its moves folder
(see [Who moves the cards](#who-moves-the-cards), and CM-1 to CM-22 in the design).
Don't run hand-started sessions at the same time: their claimed cards look like orphans to it.

## The prompt to start an agent by hand

```
You are the category <L> agent for <project> (<path>).
Read jobs/README.md and then jobs/project.md, and follow them exactly.

Work the queue continuously, one job after another, in this one session: pick by step 2, claim,
do it, run the gates, move the card, push. A question for the developer does not stop you: park
the card as needs-human, push, and take the next job. Don't end your turn, summarise, or ask
whether to continue between jobs.

End your turn only when your category has no card it can take, or your context has been compacted
twice. Then give the session report. Other categories' agents are working at the same time: stay inside
your category's files, pull before each job, and push as soon as each commit is green.
```
