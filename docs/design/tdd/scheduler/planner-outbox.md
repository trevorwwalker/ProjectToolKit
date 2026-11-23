# Planner outbox (scheduler 2.3.0)

From: the developer, 2026-11-14 22:33 - the planner (a Claude session that edits job cards) may pull
and push job files itself, through the scheduler, with a switch on the page to turn that on and off.

## Why it goes through the scheduler

The planner works on the developer's PC through a sandboxed bridge: it can read and write files in
`C:\Projects\AmigaX`, but it cannot reach `git.local` (the bridge's proxy answers 403), so it can
neither fetch nor push. The scheduler can. It already commits page answers from its `answers`
worktree (`Answers-Tree`, `Update-Card`), so the planner's edits take the same road.

## What the planner leaves

A **batch** is a folder `<outbox>\<yyyyMMdd-HHmmss>-<slug>\` holding the new file contents and a
`batch.json`, written last (its presence means the batch is complete):

```json
{
  "message": "jobs: close 452-G; card 502-G",
  "trailers": ["Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>", "Claude-Session: https://claude.ai/code/session_..."],
  "ops": [
    { "op": "write",  "path": "docs/jobs/queue/502-G-x.md", "base": "new",      "file": "f1.md" },
    { "op": "write",  "path": "docs/jobs/active/498-P-y.md", "base": "<blob sha of the copy the planner edited>", "file": "f2.md" },
    { "op": "move",   "from": "docs/jobs/review/452-G-z.md", "to": "docs/jobs/done/452-G-z.md" },
    { "op": "remove", "path": "docs/jobs/review/498-P-y.md" }
  ]
}
```

`base` is `git rev-parse origin/main:<path>` at the time the planner read the file, or `new`.

## What the scheduler does (only while the switch is on)

Every minute or so, between ticks:

1. **Fetch.** `git fetch` in the developer's copy (`planner.fetchRepo`, e.g. `C:\Projects\AmigaX`), so
   the planner reads a fresh `origin/main`. Fetch only: never pull, never touch that working tree.
2. **Apply each complete batch, oldest first,** in the answers worktree at the current main:
   - Every path must be under `docs/jobs/`. Any other path fails the whole batch.
   - `write`: if the path's current blob is `base` (or `base` is `new` and the path is absent), copy the
     file in. Otherwise merge three ways (`git merge-file` with `git cat-file -p <base>` as the
     ancestor). If that conflicts and the planner's change only appends to `base`'s text (the
     `Appended-Text` rule), append it to main's copy, as `Update-Card` does for a `## Dev` reply.
     Anything else fails the batch.
   - `move` (after the writes): `git mv`. Missing source: fail. Target already there (a worker moved
     it): skip the move, note it in the log.
   - `remove`: `git rm`. Already gone: skip.
3. **Commit** the batch's paths only, with its message and trailers, and **push**; if the push is
   rejected, start again from the new main, up to three times (as `Update-Card`).
4. On success, move the batch folder to `<outbox>\sent\` and log `planner: <message>`. On failure, move
   it to `<outbox>\failed\` with `reason.txt`, and log it.

## The switch and the page

- Off by default. Its state is this PC's business, kept beside `workers-state.json` (not in the
  repository), and it survives a restart.
- The Control window gets a **Planner push: on / off** button (local only unless `remoteControl`),
  with the last result: `sent 14:02 jobs: close 452-G` or `failed 14:05: <reason>`, and a count of
  batches waiting and failed.
- While off, batches simply wait in the outbox, and nothing is fetched.

## Config (scheduler.json)

```json
"planner": { "outbox": "docs\\jobs\\.planner-outbox", "fetchRepo": "C:\\Projects\\AmigaX" }
```

`outbox` is relative to `fetchRepo` (the planner can only write there). The project's `.gitignore`
lists the outbox folder.

## Setting a card ready clears its run count (2.3.1)

From: the developer, 2026-11-15 09:06. A card that ran out of tries is listed as **stuck** and
waits for the page's **Retry** button, because its run count (`$retries`, saved by `Save-Retries`)
lives only on this PC, where a batch cannot reach it. So when the planner requeues a stuck card,
nothing happens until the developer presses Retry.

Rule: after a batch is committed and pushed, for every `write` in it whose resulting file has
`Status: ready` (the card's own Status line, as `Read-Cards` reads it), and whose card id has an
entry in `$retries`, remove that entry and call `Save-Retries`, as the Retry button does
(schedule.ps1 ~1384). Log it: `planner: <id> set ready, run count cleared`. Do nothing for a card
the batch did not write, or wrote with any other status, so a batch that only adds a Dev line to a
stuck card does not release it.

No new op and no new config: the planner releases a card only by deliberately setting it ready.

Version: a Rev change (2.3.1) - it adds behaviour to an existing interface without changing it.

## Card numbers: only the scheduler hands them out (1111-B, 3.0.x)

From: the developer, 2026-11-22 19:01 - "the scheduler should hand out the card numbers. It doesn't
really matter if a given project's cards are linear." Two sessions had both taken 1106 as "highest on
main + 1" within minutes, and one card was lost.

- **A card asks for a number** by its file name: `new-<L>-<slug>.md` in a card folder (in practice
  `queue/`), its title line `# new-<L>: <title>`. Any writer may make one: a batch (`write` with
  `"base": "new"` to `.../queue/new-<L>-<slug>.md`), a commit on main by hand or by another session.
  Such a file is not a card (CARD-4 wants `<n>-<L>-<slug>.md`), so it is never dispatched before it
  has a number.
- **Only the scheduler assigns**, on the repository's git writer (the single writer to main):
  - (a) **A batch:** after the batch is worked out and before its commit, each `new-*` card it writes
    is written at `<n>-<L>-<slug>.md` instead, in the same commit with the batch's message.
  - (b) **On main:** each step beside the planner's (every project, switch or no switch), the model
    of main is read; a `new-*` file in the project's jobs folder is moved to its number in a scheduler
    commit `jobs: number new-<L>-<slug> as <n>` (several: `..., new-<L>-<slug> as <n>`).
- **The counter** is one for the whole pool (`CardNumbering`, Host): next = 1 + the highest card number
  in any folder (queue, active, review, done, archive) of every project in the pool, read from the
  in-memory models of main, and never at or below a number already handed out in this run. Numbers
  are unique across projects and need not be consecutive within one. While any model of main has not
  loaded, nothing is numbered: a batch with a `new-*` card waits in the outbox, a `new-*` file on main
  waits, and the log says so once.
- **References:** in every file of the batch (or, on main, the new cards and the queue's cards) a
  `new-<L>-<slug>.md` becomes `<n>-<L>-<slug>.md`, a bare `new-<L>-<slug>` (a `Depends on:` line, text)
  becomes `<n>-<L>`, and the card's own title `# new-<L>:` becomes `# <n>-<L>:`. A `new-*` name that is
  not in the batch is left as it is and logged.
- **Log:** `card numbered: <project>/new-<L>-<slug> -> <n>-<L>-<slug>` (the project is left out outside a
  pool); references that cross projects carry the project name (`<project>/<n>-<L>`).
- **For the planner and other writers:** never pick a number yourself; name new cards `new-<L>-<slug>`
  and refer to them by that name within the same batch or commit.

Code: `Scheduler.Core/Cards/CardNumbers.cs` (names, highest, renaming), `Scheduler.Host/Moves/CardNumbering.cs`
(the counter and the step for main), `PlannerStep` (batches).

## Version

A new feature that leaves existing interfaces alone (new config keys are optional): the
developer picks the number; 2.3.0 is suggested.
