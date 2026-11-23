# scheduler-next: behaviour spec

Step 1 of the C# (.NET 10) rewrite in `docs/design/gdd/scheduler-roadmap.md` item 1: the current rules written down as test cases
before porting. Source: `C:\Projects\ProjectToolkit\scheduler\schedule.ps1` v2.3.16 (4,318 lines,
last changed 2026-11-17), `planner-outbox.md`, `theme.default.json`, `themes/`, `start-scheduler.cmd`.
Citations are `Function` (Lnnn) in schedule.ps1.

Not read, because they live in the project repositories, not in the connected folders: the jobs README
(`docs/jobs/README.md`, the worker's whole brief and card format), `scheduler.json`, `categories.json`,
`workers.json`, `models.json`, `docs/jobs/scheduler.md` and `ProjectToolkit/templates/jobs/scheduler.json`.
Their formats below are what schedule.ps1 reads from them, nothing more (see F-1).

Part A is current behaviour (to be kept unless Trevor decides otherwise). Part B is the decided new design.
Part C is what is unclear or looks wrong. Rule IDs are stable names for test cases.

**Porting note that applies everywhere (G-1).** PowerShell's `-match`, `-eq`, `-ne`, `-like`, `-lt`/`-ge`
on strings, `Contains` on hashtables keyed by string, and `Sort-Object` are **case-insensitive** (culture
compare for sorts). Every rule below that matches or compares text does so case-insensitively unless it
says otherwise (`StartsWith(..., Ordinal)` and C# code compiled inside the script are case-sensitive).

---

# Part A - current behaviour

## A1. Start-up and configuration

- **CFG-1** Repository: `-Repo` (or first positional argument) resolved to its git top level; else, without `-Config`, the current directory's top level; else error "Name the project's repository". A non-git `-Repo` is an error. (L112-128)
- **CFG-2** Config file: `-Config` if given, else `<repo>\jobs\scheduler.json`, else (fallback) `<repo>\docs\jobs\scheduler.json`; if both exist, `jobs\` wins and the other is logged; none present is fatal. (L129-130; changed by card 400-B)
- **CFG-3** Without `-Config`, the config is re-read from `<remote>/<branch>:<jobsDir>/scheduler.json` after a `git fetch` (remote/branch/jobsDir taken from the checkout's copy, defaults `origin`/`main`/the folder the config was found in); if that fails or is invalid JSON the checkout's copy is used and the reason printed. A `-Config` file is used as is. (L131-149)
- **CFG-4** `cfg.repo` replaces the repository only when none was given on the command line. (L150-151)
- **CFG-5** `Cfg(value, default)`: null or empty string means default; `0` and `false` are values. (L154)
- **CFG-6** Defaults: project = repo folder name; jobsDir = the folder the config was found in (`jobs`, or the fallback `docs/jobs`; card 400-B; trailing `/` trimmed); remote `origin`; branch `main`; slots 2; port 8765; listen `localhost`; remoteControl false; profile false; workspaceRoot (alias `slotRoot`) `<repo>-workspaces`; logDir `%LOCALAPPDATA%\<project>\scheduler`; jobsEnvVar `AGENT_JOBS`; workerWindow `Hidden`; limits: pollSeconds 15, maxRetries 2, stallMinutes 30, maxMinutes 240, stopGraceMinutes 20, warnMinutes 20, pageStopGraceMinutes 5; health.command ''; promptExtra ''; worker.cli `copilot`; worker.args `-p {prompt} --allow-all --no-ask-user`; worker.web false; worker.model ''; worker.endpoint ''; worker.metricsUrl ''; worker.quotaPatterns `exceeded your (monthly )?quota`, `quota exceeded`, `rate limit`. (L153-216, L304)
- **CFG-7** Command line overrides config for one run: `-Slots` (non-zero), `-Port` (non-zero), `-Model`, `-NoHealthCheck` (health command emptied), `-Paused` (start state `paused`), `-DryRun`. (L89-98, L162-196, L2539)
- **CFG-8** `-Slots` given on the command line pins the worker count: workers.json's `slots` is then ignored. (L161, L2568)
- **CFG-9** Default agent: format `copilot` when worker.args contains `output-format[= ]json`, else `text`; no own endpoint. (L220-226)
- **CFG-10** `cfg.agents.<name>`: cli (default: the name), args, env, quotaPatterns (default `usage limit`, `rate limit`, `Failed to authenticate`), model, format (default guessed: `claude` if args contain `stream-json`, `copilot` if `output-format[= ]json`, else `text`), and `endpoint` - its presence (even "") is recorded separately from its value. (L227-238)
- **CFG-11** `Agent-Of(name)`: that agent if it exists, else `default`. (L239)
- **CFG-12** `{model}`, `{endpoint}`, `{jobs}`, `{project}` are substituted in args, env and health.command; model/endpoint default to the config's when the worker has none. (`Expand` L401)
- **CFG-13** Category names: from `cfg.lanes` until categories.json (or, when absent, lanes.json) on main has been read, then from that file. (L301-303, `Read-Lanes`)
- **CFG-14** One scheduler per project: a named mutex `Global\ProjectToolkit-<project>-scheduler`; waits 60 s, then fails with "Another <project> scheduler is still running"; an abandoned mutex counts as owned. (L4004-4008)
- **CFG-15** Before the loop: adopt running workers (A9), seed recent events and run stats from accounting.csv (A8), start the page listener, log the start line, open the page in the browser (not in dry run). (L4010-4040)
- **CFG-16** Git never prompts: `GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=Never`; every git call has stdin closed; stdout is parsed separately from stderr. (L3990-3991, `Git` L448)
- **CFG-17** Listen `lan`: `http://+:<port>/`; on failure logs why (port taken vs missing URL reservation, with the `netsh http add urlacl` command) and falls back to localhost; failure on localhost too is fatal. (L4013-4033)
- **CFG-18** Version string `Major.Minor.Rev` (Minor = incompatible interface change incl. scheduler.json, card/worker protocol, page URLs, state files; Rev = fixes) shown on the page and in the log. (L104-108)

## A2. Reading cards from main

- **CARD-1** Cards are read only from `<remote>/<branch>` (never a working tree): `git ls-tree -r` of `<jobsDir>/`; a blob is re-read only when its id changes (cache by `blob|path`). (`Read-Cards` L591) Since cards 1060-A to 1066-B the read is from the repository's in-memory model of main (A14, MM-4 to MM-8); the git CLI reads only before the model's first load or when the model falls back (MM-2, MM-16).
- **CARD-2** `git fetch` at most every 15 s. (`Fetch-Main` L532) Now one fetch per repository per poll, shared by every consumer (MM-9).
- **CARD-3** If ls-tree returns nothing (git failed), the previous card list is kept. (L598)
- **CARD-4** A card is a path matching `<jobsDir>/(queue|active|review|done)/(\d+)-([A-Z])-[^/]+\.md`; Folder, Name (file name without .md), Num (the number as a value; order is taken from it), Category (one letter), Id = `{number}-{Category}` with the digits exactly as the file name writes them, no padding: `007-A` stays `007-A`, `1001-A` is `1001-A`. Other files are ignored, and so is a number too big to hold as a card number. (L602; any width decided 2026-11-19, card 931-A)
- **CARD-5** Cards in done/ are not opened by the tick's reader: only folder/name/id/category are known. (L609) Amended by card 1069-D: the in-memory model (A14) reads done/ and archive/ headers once, in process, after the open cards, for the views (VW-21); dispatch, dependencies and the queue still use only done/ ids.
- **CARD-6** (decided 2026-11-19, 760-A) A card's fields (Status, Priority, Time box, Worker, Scope, Locks, Depends on) are read from its header only: the text before the first `## ` heading. A line in a section (Questions, Result, ...) never sets a field. The title is the first `# ` line of the header.
- **CARD-7** Status: the first line of the header starting `Status:`; following lines indented by 2+ spaces are appended with a space. (L618-620)
- **CARD-8** `Priority: high` -> 0, `low` -> 2, any other word -> 1; no line -> 1. The last Priority line in the header wins.
- **CARD-9** `Time box: N` -> TimeBox N (minutes); the last such line in the header wins.
- **CARD-10** `Worker: <x> (...)` -> WorkerReq = `<x>` (text before any `(`, trimmed), from the header.
- **CARD-11** `Depends on:` -> every `\b\d+-[A-Z]\b` on the line (ids of any width); the last such line in the header wins.
- **CARD-12** Questions: lines under `## Questions` (until the next `## `) matching `- Q<n>: <text> [<c1> | <c2> ...]` with at least two choices; a choice ending `-> done|queue|active` carries that destination. An answered line no longer matches. (`Parse-Question` L503)
- **CARD-13** Why (reason it waits on the developer): text after `needs-human:` or `review:` in Status; else for queue/active cards waiting on the developer, Status minus `waiting:`; else for review cards, the newest `## Result` journal entry (`Latest-Journal`). WhyFrom = `status` or `result`. (L631-634)
- **CARD-14** `Latest-Journal`: an entry starts with an optional `-`/`*` bullet, optional `**`, then `yyyy-MM-dd`; an `H:mm`/`HH:mm` in the line's first 40 characters is added to the key; continuation lines join the entry; the greatest key wins, the later entry on a tie; no dated entry -> the loose text. Shortened by `Short-Text` (strip `**` and backticks, collapse whitespace, max 300 chars + `...`). (L568-589)
- **CARD-15** Waits on developer: folder queue or active and Status matches `^waiting:\s*(the\s+)?(developer|dev|you|human)\b`. (`Waits-On-Dev` L552)
- **CARD-16** Waits on planner: folder queue or active and Status matches `^waiting:\s*(the\s+)?planner\b`. (`Waits-On-Planner` L558)
- **CARD-17** `Deps-Done`: every Depends id exists and is in done/. A card in review/, active/ or queue/ does not release its dependents (1059-B). (L559)
- **CARD-18** Requests: `<jobsDir>/requests/*.md` except names containing README; Title (`# `), Status, For: (first of each). Open = Status not starting `answered|closed|done`. (`Read-Requests` L649, page L3610)
- **CARD-19** Duplicate cards: a card in queue/ that also exists (same Id) in another folder is a stale copy: never dispatched, and removed from main (`git rm`, commit "jobs: remove the queue/ copy of ...", push, up to 3 tries), then cards re-read. Two copies outside queue/ are logged once and left alone. (`Fix-Duplicate-Cards` L2767, `Queue-Copies` L2749)
- **CARD-20** (decided 2026-11-19) A card's file name is `<n>-<L>-<slug>.md`; `<n>` is a positive integer of any width and is the card's identity only. A new card takes the highest number in use in queue, active, review, done and archive, plus one: no gaps, no renumbering, numbers never restart. Order is `Priority`, then `Depends on`, then the number; to run a card earlier, give it a priority or make others depend on it, never renumber. Finished cards may be swept from `done/` to `archive/`; their numbers stay used.

## A3. Dispatch (card selection), each tick

Order of a tick (`Tick` L2795): sample host, sample model servers, reap health check, reap/watch workers, index logs, read cards, fix duplicates, planner step, read categories, workers, models, requests, plan, start health check if wanted, dispatch, save retries, write status files.

- **DISP-1** Finished = cards in done/. A card in review/ is not Finished: it keeps its dependents waiting, in dispatch, in the page's queue order and in the "waiting on" lists (1059-B). Busy categories = the category of every running worker's card. (L2867-2871)
- **DISP-2** Orphan candidates (resume) come first: cards in active/ whose Status does not start `needs-human`, by Num. (L2874)
- **DISP-3** An active card whose scope overlaps a running card's is skipped silently. (L2875; scopes instead of categories, card 560-A, see DISP-22)
- **DISP-4** An active card with Status starting `waiting` or with an unfinished dependency is not an orphan and does not hold its category; note "claimed; waits for X" or "claimed; <status>". (L2876-2882)
- **DISP-5** *Gone (card 560-A).* A redefined category is never held (LANE-2), so no orphan waits on one.
- **DISP-6** A low-priority orphan waits behind a queued card whose scope overlaps its own (560-A; was: of the same category) with a lower Prio number, Status `^ready` and all dependencies finished; note "orphan; waits behind higher-priority <id>". (L2885-2888)
- **DISP-7** An orphan with retries >= maxRetries is **stuck**: not resumed, does not hold its category, listed under Waiting on you with Retry; note "stuck: <n> runs ended without a result: needs you", n being the card's real run count (`DispatchPlanner.StuckNote`, one wording for DISP-7 and DISP-11; card 940-A, was maxRetries+1). The card is shown under Waiting on you with its last result and its log. (L2889-2896)
- **DISP-8** Otherwise the orphan is a candidate (Resume) and takes its category for this tick. (L2897-2898)
- **DISP-9** Queued candidates: cards in queue/ sorted by Prio, Num, Category; stale duplicates skipped. (L2901-2902)
- **DISP-10** Status not starting `ready` -> not a candidate; note is the Status. (L2903)
- **DISP-11** Queued card with retries >= maxRetries -> stuck, with the DISP-7 note "stuck: <n> runs ended without a result: needs you" (was "<n> runs failed to start"). Every run that ends without a result counts against a queued card (TIME-17), so a card that crashes, ends early, hits its time limit or stalls is dispatched maxRetries times and then stuck, not retried without limit (closes Part C flag 2). (L2904-2908)
- **DISP-12** Unfinished dependency -> "waits for <ids>". Scope overlap with a running or already chosen card -> "waits: overlaps <card> on <path>" (560-A; was "category X busy (<who>)"; the held-category check is gone). Checked in that order.
- **DISP-13** Otherwise a candidate; its scope is taken for the rest of the tick ("next: <id>"), so a later card whose scope overlaps it waits (560-A; was: at most one candidate per category per tick). (L2913-2914)
- **DISP-14** Dispatch only while state is `running`. Candidates in order Prio, then `Depends on`, then orphans before new cards, then Num as a value (`999-A` before `1000-A`, in dispatch and in every table on the page, PAGE-5, PAGE-6). (L2921-2923)
- **DISP-15** *Superseded by the WT rules (card 850-D; the slot dispatcher is removed by 939-A) (worker types).* A candidate goes to a worker type, not a slot: WT-3 (fit), WT-4 (usable now), WT-5 (which type), WT-6 (order); a candidate with no usable type is skipped this tick and its scope stays taken (WT-8, DISP-13). (Was: the lowest-numbered enabled idle slot that fits, L2924-2925.)
- **DISP-16** A resume increments the card's retry count before starting; the run that follows is not counted again when it settles (TIME-17). A count carried in below 0 is read as 0 (`DispatchPlanner.CountResume`). (L2927)
- **DISP-17** *Superseded by the WT rules (card 850-D; the slot dispatcher is removed by 939-A).* A type fitted a category when its categories list was empty or held the card's category letter. *Gone (969-A): worker types have no categories list; WT-3 has no category test.* (Was `Lane-Allowed` on a worker's lanes string, L2718.)
- **DISP-18** *Superseded by the WT rules (card 850-D; the slot dispatcher is removed by 939-A).* `Worker:` matching is WT-3: no line -> only non-premium types; `premium` -> only premium types; otherwise a type's name, effective model, `Model-Short` or models.json `short` (case-insensitive); a slot number only until the pool form is first saved (WT-C10, WT-C11). (Was `Worker-Fits`, L2710.)
- **DISP-19** *Superseded by WT-12 (the slot dispatcher is removed by card 939-A); kept for the history.* (decided 2026-11-19, 760-A Q3, 800-B) The not-started note of a card that names a `Worker:` line lists, for each slot that fits that line, what stops it: "disabled", "category not allowed", "server down", or "busy with <card id>" ("busy" when the card it runs is not known); a slot that is busy and has its server down says "server down". Slots that do not fit the line are not listed. WT-12 gives the notes of the type dispatcher; this rule is the per-slot wording, and WT-12's busy reason names the cards the same way (WT-12, check 3).
- **DISP-20** *Reversed by card 560-A.* The scheduler reads a card's `Locks:` line: its paths are part of the card's scope (SC-1, DISP-22). (Until 560-A: locks were honoured by workers only, L50, L1762.)
- **DISP-21** Dry run: logs "DRY RUN slot N <- card" instead of starting, then the loop exits after the first tick. (`Start-Worker` L1785, L4304)

## A4. Categories

- **LANE-1** categories.json on main (`<jobsDir>/categories.json`; card 979-A: when that file does not exist the older `<jobsDir>/lanes.json` is read instead, same format): `lanes.<letter>.{name, owns[], except[], alsoOwns}`, `shared.{addOnly[], rerecord[], rerecordRule}`. Invalid JSON is logged and the previous categories kept; no `lanes` key (the key keeps its old name) -> ignored. (`Read-Lanes` L1622)
- **LANE-2** *Changed by card 560-A (developer, Q1): a redefined category is accepted at once and never held; the text below is the old behaviour.* Each category's definition (compact JSON) is compared with lanes-accepted.json (log folder). New category -> accepted. Changed with no cards of that category in queue/ or active/ -> accepted and logged. Changed with such cards -> **held** with a reason listing up to 6 ids; logged once when it becomes held. (L1630-1644)
- **LANE-3** *Dead since 560-A (nothing is held).* Accept from the page stores the new definition and releases the category. (`Accept-Lane` L1647)
- **LANE-4** Overlap warnings (shown, not enforced): two categories owning the same entry; or category A owning a folder entry ending `/` that is a prefix of category B's entry (trailing `*` trimmed) without an `except` covering it. (`Lane-Overlaps` L1659)
  *Update (decided 2026-11-19, 961-A Q1; card 977-A):* `owns` and `except` entries may be globs (SC-8), and the warning uses the same "can one path match both" test as scopes (DISP-25), so `vm/68000.*` against `vm/68000.cpp`, or `vm/` against `vm/*.h`, warns; disjoint patterns do not. Until 977-A lands, the code still treats a glob entry as no folder at all (known gap C12).
- **LANE-5** Category brief in the prompt: owns, "but not" excepts, alsoOwns, shared add-only and re-record lists, and "All categories are defined in <categories.json>". (`Lane-Brief` L1680)

## A5. Workers, slots and starting a card

- **WRK-1** *Replaced by card 850-D.* workers.json on main holds the `pool` section: WT-C1 to WT-C9; re-applied only when its text changes; invalid JSON logged and previous kept (WT-C6). Without `pool`, the old form (`slots`, `workers.<n>.{name, agent, endpoint, model, metricsUrl, categories, premium}`, categories upper-cased) is read and migrated in memory by WT-C10. (`Apply-Workers` L2557)
- **WRK-2** *Replaced by card 850-D.* `maxWorkers` (WT-C1, with `-Slots` pinning it, CFG-8) is the total; WT-1 and WT-2 stop new starts above it or a cap, and nothing running is stopped when it is lowered. (Was `slots`, never below the highest running slot, L2567-2574.)
- **WRK-3** *Replaced by card 850-D.* A type's cap `max` (0 = off) in workers.json is what Disable was; workers-state.json is read only for the migration (WT-C10) and not at all after the first Save (WT-C11). (Was local enabled/disabled per slot, L2675-2692, L3971.)
- **WRK-4** `Effective-Worker`: an agent with format `claude`, or an endpoint key set to "", uses its own service (no endpoint, no metrics). Endpoint: worker's, else none for own-service, else the agent's, else config's. Model: worker's, else agent's, else config's. Metrics: worker's, else derived from worker endpoint (`scheme://authority/metrics`), else none for own service, else derived from agent endpoint, else config metricsUrl, else derived from config endpoint. (L2695)
- **WRK-5** Slot = git worktree `<workspaceRoot>\slotN` on branch `slotN`, created if missing; before every card: fetch, `checkout -B slotN <remote>/<branch>`, `reset --hard`, `clean -fd`. (`Prepare-Slot` L671)
- **WRK-6** Before a start the slot's stop, warn and message files are deleted. (`Clear-Slot-Files` L690)
- **WRK-7** *Replaced by card 850-D.* Threads per worker = max(1, floor(cores / `maxWorkers`)) (WT-11); set in env var jobsEnvVar and `{jobs}`. (`Jobs-Per-Worker` L703)
- **WRK-8** Environment: variables any other agent sets but this one does not are removed; this agent's env is set with substitutions; `{contextLength}` / `{contextLength-N}` = server's max_model_len minus N (min 1024); if the server gives none, the variable is left unset and logged. (L1795-1813)
- **WRK-9** Log names `<id>-<yyyyMMdd-HHmmss>.jsonl` (`<id>` is the card's id at any width; accounting reads it and `/log?file=` serves it) (json formats) with a readable `.log` beside it, or `.log` for text; the prompt goes to `.prompt.txt` and the command line passes only a pointer sentence to it (cmd's 8191-char limit). (L1814-1822)
- **WRK-10** Prompt (`Worker-Prompt` L1746) contains: worktree, category and category name, README is the whole brief, category brief, do/resume text, one card only, never ask, git pull --rebase / push / wip/<id> rules, threads, promptExtra, narration, today's date, time limit with deadline clock time, the three flag files to check every step, message protocol (`Developer:` reply, move to `.read.txt`), output-size rules, journal every 45 min (first within 30), no-web text when web is off, 10-minute command limit and 2-minute sleep limit, stop protocol, question format, and the final `LANE-RESULT: done|review|waiting|needs-human|stopped|failed` line. Double quotes become `'`, newlines collapse to one line.
- **WRK-11** Web off: Copilot CLI gets `--excluded-tools=web_fetch,web_search` and shell denies for curl/wget/iwr/irm/Start-BitsTransfer; Claude gets `--disallowedTools WebFetch,WebSearch,Bash(curl:*)...`. (L192-194, L240, L1825-1828)
- **WRK-12** Started as `cmd /c title ... & ping -n 2 127.0.0.1 >nul & <cli> <args> > log 2>&1` (the 1 s pause lets the job object be in place first); args with whitespace or `(),;&|<>^` are quoted. (L1831-1836)
- **WRK-13** Each worker and health check runs in a named Windows job object `ProjectToolkit-<project>-<slot|health>-<n>-<pid>`; ending the job ends every process in it. (`Enter-Job` L372, `End-Job` L386)
- **WRK-14** Message from the page: text whitespace-collapsed, max 2000 chars, appended as `[HH:mm] text` to msg-slotN.txt and `<< you: text` to the readable log; only for a running worker with Msgs. (L3871-3884)

## A6. Time limits, watchdog and stop reasons

- **TIME-1** Limit = card's Time box if set, else maxMinutes. (`Card-Limit` L697)
- **TIME-2** Warning lead = min(warnMinutes, floor(limit/4)). (`Warn-Lead` L701)
- **TIME-3** Progress = the raw log's size changing. (`Watch` L780)
- **TIME-4** At age >= limit - lead and < limit, not stopping, warn file absent: write warn-slotN with minutes left and the deadline, log it. (L788-796)
- **TIME-5** Stopping and age since stop request >= grace -> kill ("still running <grace> min after a stop request (<why>)"). (L797-800)
- **TIME-6** Age >= limit + stopGraceMinutes -> kill "over the <limit> min limit", whatever the stop grace. (L801)
- **TIME-7** Age >= limit, not stopping -> stop request "over the <limit> min limit" (grace stopGraceMinutes). (L802)
- **TIME-8** Otherwise quiet >= stallMinutes, not stopping -> stop request "no output for <n> min". (L803)
- **TIME-9** Stop request: creates stop-slotN, records time, reason, grace; a second request only shortens the grace. (`Request-Stop` L708)
- **TIME-10** Page Stop: stop request with pageStopGraceMinutes; the slot is no longer disabled (WT-P2): the card waits under Waiting on you, "stopped from the page", with Resume. Stop all: every worker, pageStopGraceMinutes, state `draining`. (L3863-3868, L3954-3959; changed by card 850-D)
- **TIME-11** Kill: end job, kill process tree (taskkill /T /F, then a walk of the process table); reason kept unless one was already set; logged "its card stays in active/ and will be resumed". A worker still alive 2 min after a kill is abandoned (slot released, logged once). (`Kill-Worker` L746, `Kill-Tree` L727)
- **TIME-12** Page Kill: kill "killed from the page", tick now; the slot is no longer disabled (WT-P2): the card waits under Waiting on you with Resume. (L3960-3965; changed by card 850-D)
- **TIME-13** Alive = process with the pid exists, its start time within 2 s of the recorded one, and not abandoned. (`Alive` L769)
- **TIME-14** Result = last `LANE-RESULT: <word>` in the readable log; for json formats only on lines starting `* ` (the model's own text); no log file -> `no log`; none -> `no result line`. (`Result-Of` L2504)
- **TIME-15** Quota: on `no result line`, if the raw log has a quota pattern (whole-word, case-insensitive) on a non-JSON line or a JSON error/`is_error:true`/`subtype:"error`/failed-result line -> result `out of quota`, the run does not count against the card (TIME-17: its count is given back, never below 0), and the run's agent is marked out of quota with the time (WT-9; the state stays `running`; was: state `paused-quota`). Model text never counts. (L2814-2826, `Quota-Hit` L2468)
- **TIME-16** Still `no result line` and the worker's server does not answer (fresh check) -> `model server down`; the run does not count against the card (TIME-17: given back, never below 0). (L2828-2831)
- **TIME-17** Which runs count against a card (card 940-A; closes Part C flags 2 and 8). Every run that ends without a result line counts against the card in queue/ or active/: `no log`, `crashed (exit N)`, `ended early`, `time limit`, `stalled`. A queued card is counted when the run settles; a resume (a card in active/) was already counted at dispatch by DISP-16, so its run is not counted twice. A run that ends for something outside the card does not count and gives one back: `out of quota`, `model server down`, and a stop the developer asked for on the page (`stopped from the page`, `killed from the page`, `stop all from the page`, or a worker that ignored that stop and had to be killed) or a scheduler restart. A count is never below 0. At maxRetries the card is stuck (DISP-7, DISP-11). (L2835; `RunResults.Settle`)
- **TIME-18** Still `no result line` -> `End-Reason`: StopWhy `^over the \d+ min limit` -> `time limit`; `^no output for` -> `stalled`; `^killed` -> `killed`; contains `from the page` -> `stopped from page`; exit code non-zero -> `crashed (exit N)`; else `ended early`. (L2838, `End-Reason` L2492)
- **TIME-19** Result `stopped` (a stop the developer asked for) with a retry entry -> one given back, never below 0 (TIME-17). Results done/review/waiting/needs-human -> retry entry removed. (L2813, L2840)
- **TIME-20** After a finish: log, accounting row, health check wanted, recent list (newest first, max 10), end job, remove from running, close raw stream, clear slot files. (L2841-2850)
- **TIME-21** A worker process exit is noticed within 250 ms and triggers a tick. (L4275-4283)
- **TIME-22** Retry counts persist in retries.json and are saved every tick. (L2527-2536, L2946)

## A7. Gates, hand-over and the health check

- **HLTH-1** Health wanted after every finished run and from the page's Check main (which also forgets the last checked sha). Not run at start-up. (L2843, L3856, L1693)
- **HLTH-2** Not started when no command, dry run, one already running, or main's short sha equals the last checked sha (then wanted is cleared). (`Start-Health` L1695)
- **HLTH-3** Runs in worktree `<workspaceRoot>\health` reset to main, through health.cmd (title, cd, set jobsEnvVar, the command), output to `health-<sha>-<ts>.log`, in a job object. (L1700-1719)
- **HLTH-4** Killed after maxMinutes and treated as red. (`Reap-Health` L1721)
- **HLTH-5** Exit 0 -> green, and state `paused-red` -> `running` (logged). Non-zero -> RED, and state `running` -> `paused-red` (logged). Other states unchanged. (L1733-1743)
- **HLTH-6** Red shows the theme's red-health banner with `{sha}` and a link to the log. (L3537-3539)
- **HLTH-7** Hand-over (worker side, from the prompt): only gate-passing commits go to main; others to `wip/<id>`; card moved to done/review or parked needs-human or sent back waiting, as the README says. The scheduler does not check gates itself. (L1753, L1761)
- **HLTH-8** A new check ends the one before it: `Start` ends the whole process tree of the check this scheduler started, waits for it to go, and only then builds, so two checks never run in the health workspace. The running check's pid, job name, start, sha and log are kept in `<logDir>\health-running.json` and removed when the check is killed or superseded. health.cmd sets `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_DISABLE_BUILD_SERVERS=1` so no build node outlives it. (920-B)
- **HLTH-9** On start the scheduler ends the tree of a check left running by the previous scheduler (the pid in `health-running.json`) and never adopts it. This includes the tray's Restart: the new scheduler ends the old check rather than waiting for it (developer's choice, 920-B Q2, 2026-11-19). (920-B)
- **HLTH-10** A check that exits non-zero and whose log's only errors are MSB3026/MSB3027 (file locks) is retried once, after ending the locking process tree, into the same log. If it fails the same way again the log says `health check of main <sha> could not run (files locked by <name>); not reported as red` and the result is "could not run": not green, not red, state unchanged, dispatching not paused. A log with any other error keeps its real verdict and is not retried. A scheduler already `paused-red` keeps that state. (920-B)
- **HLTH-11** A check still running `limits.healthWarnMinutes` (default 5; 0 = never) after its start is warned about once: the log says `health check of main <sha>: still running after <n> min (warn at <m> min)` and the page's health line carries `still running after <n> min (warn at <m> min)` while that check runs. It never stops or fails the check — only HLTH-4 does that. Every finished check's result line carries the seconds it ran, `in <n> s` (1100-B, from 1079-B: how long a check takes depends on the machine's load, so the duration, not a rate, is what the developer sees).

## A8. Accounting and statistics

- **ACCT-1** accounting.csv in the log folder, header `finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model,type` (the `type` column is added by card 850-D, WT-P6; an old header is rewritten in place as ACCT-4 does for `model`); one row per finished run: ISO time, card name, category, slot, result, whole minutes, readable-log bytes, stop reason in quotes, model, type. (`Account` L844)
- **ACCT-2** Model column = models.json key the worker's model resolves to (`Model-Key`), else the worker's model, else config model. (L848-849)
- **ACCT-3** `Model-Key`: exact key; else the server's `/models` root (cached 10 min) matched by its last path part against each entry's key, name, last part of source or served list; else the longest key the last part starts with; else ''. (L2263)
- **ACCT-4** Start-up: an old header without `,model` is rewritten in place; rows of the last 48 h loaded. (`Seed-RunStats` L863)
- **ACCT-5** Recent events after a restart: last rows (up to 10) from accounting.csv, each with the newest `<id>-<start>.log` that began no later than 1 minute after it finished. (`Seed-Recent` L812)
- **ACCT-6** Control window stats: runs of the last 24 h grouped by card (runs with no log, out of quota or server down excluded), each card under the model of its latest run, state from where the card is now: done / review / questions (active needs-human, waits on dev or planner) / open. Text "n cards (r runs): done d (p%), ... ; x cards/hr" where rate = (done+review)/24. Per-model lines when more than one model. (`Card-Stats` L891, `Stats-Text-Of` L913, `Run-Stats-Html` L929)
- **ACCT-7** Model server metrics: every 30 s, each distinct metrics URL of all slots and running workers; vLLM gauges/counters parsed; a row in `metrics-<host>.csv`; a line "server R running, W waiting, KV k %, T/s, preempted". (`Sample-Server` L1021, `Sample-One` L1030)
- **ACCT-8** Server-Up: GET `<endpoint>/models` (5 s timeout), cached 20 s; changes (and a first "down") logged. No endpoint = up. (L1005)
- **ACCT-9** Host load each tick: CPU %, free/total memory, threads per worker. (`Sample-Host` L963)

## A9. Restart and adoption

- **ADOPT-1** running.json (written every tick) keeps per worker: slot, pid, card, category, id, logs, raw position, start, job, timebox, stop state, kill time, msgs, model, endpoint, metrics, agent, type (WT-W5), stats. (`Save-Running` L2956)
- **ADOPT-2** On start, an entry is adopted only if the pid is a `cmd` process whose start time is within 2 min of the recorded start; a worker keeps its folder number even above `maxWorkers`, and WT-1 and WT-2 count it (WT-W5; was: a slot above the count raised the count). Stop deadlines carry over; quiet time counts from the log's last write. (`Adopt-Running` L2967)
- **ADOPT-3** status.txt: updated time and state, one line per running worker (type, slot, card) then the "types:" line (WT-P5), then needs-you, waits-for-planner, waits-for-you and in-review lines. (`Write-StatusFile` L3007)

## A10. Card changes from the page (claims and moves)

Workers claim cards (queue/ -> active/) themselves, per the README; the scheduler moves cards only for page actions and planner batches.

- **MOVE-1** All scheduler commits go through the `answers` worktree reset to main; commit only the card's paths; push `HEAD:<branch>`; on rejection start again from the new main, up to 3 tries; then "rejected three times". (`Answers-Tree` L1150, `Update-Card` L1204)
- **MOVE-2** Answer: the question line gets ` -> **<label>** (developer, yyyy-MM-dd HH:mm)`, an optional note line `  Note: ...` (max 500 chars) after it. The answer is recorded as given and the card does not move on a single answer: it moves only after its last open question is answered, to the destination needing the most work among the answers (card 200-A, developer decision 2026-11-18). `-> done` sets Status `done`, other destinations `ready`; no destination and no open questions left on an active card -> Status `ready`. Commit "jobs: answer <id> <qid>: <label>[ - to <dest>]". Only the answers given in the current round decide the destination (card 948-A, developer decision 2026-11-19): a round begins at the newest dated line a worker wrote in the card's `## Result` (a worker journals just before it parks the card or puts it up for review), so an answer stamped at or after that line counts and an earlier one, already acted on, does not. The page's own `- <date> developer: ...` journal lines do not start a round (answer Q1, press a page button, answer Q2 is one round). With no dated worker line, or an answer with no `(developer, yyyy-MM-dd HH:mm)` stamp, every answer counts as before; the open count is still every question; when no answer of this round has a destination, an active card becomes `ready` where it is. An already answered question is an error. (`Answer-Card` L1076)
- **MOVE-3** Setting Status replaces the first `Status:` line and drops its indented continuation lines. (L1124-1131, L1243-1249)
- **MOVE-4** Update-Card first collects the developer's uncommitted edits to the card (repository working tree and answers worktree; conflicts left from a failed try ignored) as patches and replays them with `git apply --3way`; if that fails and the edit only appends text at the end, that text is appended to main's copy; otherwise error "could not be merged with main". On success the repository's copy is reverted (main has it). (L1168-1230, L1268)
- **MOVE-5** Update-Card adds a journal line at the end: blank, `- yyyy-MM-dd HH:mm developer: <line>`, blank. (L1250-1254)
- **MOVE-6** Done (review cards only): Status done, to done/, "reviewed, done (from the page).", commit "jobs: done <name> (reviewed from the page)". (`Done-Card` L1521)
- **MOVE-7** Resume / Send back / Ready: a review card -> active/ ready ("sent back from review"); an active needs-human card -> ready ("resumed"); a card waiting on the developer -> ready where it is (queue stays queue; active becomes active ready). Retry count cleared for the non-queue cases. (`Resume-Card` L1530)
- **MOVE-8** Retry: removes the card's retry entry. Resume (state): clears all retry entries. (L3950-3953, L3855)
- **MOVE-9** Open: the repository's copy if present, else the answers worktree's; opened with the .md handler and brought to the front (up to 5 s wait). (`Open-Card`/`Open-Path` L1547-1562, `Raise-Editor` L1575)
- **MOVE-10** Worker settings from the page: endpoint/metrics must start `http(s)://`; categories limited to known category letters; workers.json rewritten by hand-formatted text keeping main's line endings and committed "scheduler: workers - <what>"; nothing committed when unchanged. (L3906-3929, `Commit-Repo-File` L2649, `Workers-Text` L2633)
- **MOVE-11** A failed page action is logged and shown as a notice at the top of the page for 10 minutes or until a later action succeeds. (`Page-Error` L3793, L4260)

## A11. Planner outbox (planner-outbox.md)

- **PLAN-1** Enabled only when `planner.fetchRepo` and `planner.outbox` are set; outbox = fetchRepo + outbox. Switch off by default, stored in planner-state.json, toggled from the page. (L1272-1286, L3858-3864)
- **PLAN-2** At most once a minute while on: `git fetch` in fetchRepo (never pull or touch its tree), then each complete batch (folder with batch.json, not sent/ or failed/) in name order. (`Planner-Step` L1483, `Planner-Batches` L1288)
- **PLAN-3** Validation: valid JSON; non-empty message; at least one op; every path under `<jobsDir>/`, no `..`, no `\` or `:`; write needs a file inside the batch and a base; ops only write/move/remove. Any failure fails the batch. (`Apply-Planner-Batch` L1346-1373)
- **PLAN-4** Writes first. A non-new write whose path is gone is written where a file of the same name now is (noted). base `new`: absent -> written; present with the same text (LF, trailing whitespace ignored) -> noted, skipped; present and different -> fail. base equals current blob -> written with main's line endings. Path gone -> fail. Otherwise 3-way merge (`git merge-file`), else append-only addition appended, else fail. (L1380-1412, `Merge-Planner-Text` L1309)
- **PLAN-5** Moves: same path skipped; moved-elsewhere source followed; target present and source absent -> skipped; both present -> fail; source absent -> fail; else `git mv`. Removes: absent -> skipped, else `git rm`. (L1417-1440)
- **PLAN-6** Nothing to commit -> success. Commit message = message + blank line + trailers (`"` -> `'`); push; 3 tries. (L1442-1451)
- **PLAN-7** After a pushed batch, every written card now `Status: ready` with a retry entry has it removed and is logged "planner: <id> set ready, run count cleared"; tick now. (`Planner-Release` L1456)
- **PLAN-8** Batch folder moved to sent/ or failed/ (with reason.txt); a name clash gets `-HHmmss`. Page line: on/off, waiting count, failed count, last result. (L1490-1519)

## A12. Page and live view

- **PAGE-1** A separate thread serves `/live.json` and `/` from snapshots and queues every POST (202 `{"id":n}` for fetch, 303 otherwise); anything else is handed to the main loop. POST from another machine without remoteControl -> 403. (L4071-4130)
- **PAGE-2** Page snapshot after any click or tick, and every 3 s while the page was asked for in the last 15 s; remote read-only version only while another PC asked in the last 30 s. (`Update-Page` L4171, L4269)
- **PAGE-3** Live snapshot 24 times a second while asked for in the last 5 s; the next build waits at least twice the last build's time. JSON `{t, pageDone, slots:{n:{log, stats, time}}}`. *Changed by card 610-C: see PAGE-13 for the log feed (numbered lines with `?since=`), which replaces "last 20 rendered lines plus the line being written".* (`Live-Beat` L4207, `Live-Json` L3757, `Live-Log` L2356)
- **PAGE-4** A tick asked for by a click waits for 250 ms without requests or 2 s at most. (L4292-4298)
- **PAGE-5** Windows: top bar (project, state, workers, host load, theme selector, last tick time); notice; red banner; Control (state, health, server-down, category overlaps, stats, planner line; buttons Check main, Categories, Workers, Planner push, Pause/Resume, Drain and exit, Stop all and exit); Waiting on you (held categories, stuck, questions, waiting, review - review cards hidden while their worker runs); Requests to the planner (open requests, cards waiting on the planner with "all done: the planner can act now"); one window per worker; Queue; Recent events (last 10); settings dialogs. (`Page` L3524)
- **PAGE-6** Queue order: orphans by Num, then queued cards by Prio, Num, Category but never before a queued card they depend on (cycle -> next by order); waiting-on-dev/planner, running and stale cards left out. (`Order-Queue` L3149, L3651-3668)
- **PAGE-7** Remote view without remoteControl: buttons disabled with "(Only on the PC running the scheduler.)". (`Button` L3030)
- **PAGE-8** State text: paused-red -> "paused: main is red", paused-quota -> "paused: quota used up" (card 850-D: quota no longer pauses the project, WT-9; the page shows the agent out of quota instead, WT-P3); class ok/warn/bad. (L3527-3528)
- **PAGE-9** GET routes: `/`, `/font`, `/log?file=<id>-<ts>.log|recent=k|slot=n` (last 2000 lines, ANSI stripped), `/card?name=`, `/request?name=`, `/health`, `/live.json`, `/status.json`; else 404. POST routes: `/pause` (running only), `/resume` (paused* only), `/health-now`, `/drain`, `/planner-toggle`, `/stopall`, `/tick`, `/send`, `/answer`, `/open-lanes`, `/open-workers`, `/accept-lane`, `/open`, `/resume-card`, `/done`, `/retry`, `/stop`, `/kill`. The routes `/worker-settings`, `/enable`, `/disable`, `/slots-add` and `/slots-remove` are retired (937-B, 943-A) and answer 404. (`Handle` L3798)
- **PAGE-10** Rendering worker output: Copilot events (reasoning `~`, messages `*`, tool calls `>`, failed tools `x`, model calls over 30 s, error events `!`); Claude stream-json the same marks plus session end/cost; stats estimated at 3.5 characters a token; context reset on compaction. (`Render-Event` L1899, `Render-Claude` L1854, LiveScan L1958-2163)
- **PAGE-11** Themes are loaded from beside the exe, copied there from `assets/themes/` (card 420-B): project theme (page.theme, else theme.default.json) first, then theme.default.json and themes/*.json by order and label; `extends` prepends the base CSS; choice kept per browser. Only the project theme's font and banner words are used. (L3198-3226)
- **PAGE-12** Exit: in `draining` with no workers running, the loop ends; finally stops the listener and page thread, writes the status file, releases the mutex. (L4308-4317)

## A13. Behaviour added since the port (cards 520-A to 670-B)

Written by card 720-D from the code and `docs/known_gaps.md`. Where a rule is an assumption of the card
that built it, it says so: that is current behaviour, not a decision.

### Dispatch by scope (card 560-A; developer decisions on cards 320-A and 560-A)

- **DISP-22** Dispatch goes by card scope, not by category (SC-1 to SC-5). A card's scope is its `Scope:` line
  (320-A syntax: comma list, folders end in `/`, `(add-only)` marks an add-only path, `; except <paths>`,
  `none` or empty means the category's) plus the paths on its `Locks:` line (backticks and notes in parentheses
  other than `(add-only)` are ignored). A candidate whose scope overlaps a running card's, or a card chosen
  earlier in the same tick, is not started; its note is "waits: overlaps <card> on <path>" (DISP-12).
- **DISP-23** A card with no `Scope:` and no `Locks:` takes its category's default scope from categories.json (SC-4).
  With categories.json read and no category entry for its letter, it is not started (SC-1). *Assumption:* with no
  categories.json read at all, the card takes its category letter as its scope ("category A"), so one card per category runs.
- **DISP-24** Overlap: paths compare case-sensitively, repositories case-insensitively. An add-only overlap
  is allowed when categories.json lists the path in `shared.addOnly` or both cards mark it `(add-only)`; marked
  on one card only, it still overlaps (SC-5). (Core `Scheduler.Core/Scopes/`.)
- **DISP-25** (decided 2026-11-19, 954-B and 961-A) Glob patterns are valid in `Scope:`, `Locks:` and categories.json `owns`/`except` (SC-8). Two entries overlap when one path can match both, checked both ways round: a pattern and a file overlap when the file matches; a pattern and a folder (`/`) when the folder holds a path that matches (`src/*` does not overlap `src/x/`, `src/**` does); two patterns when some path matches both (`src/a*` and `src/*b` do). An `except` entry leaves a pair out only when it can be shown to cover one side: a glob except clears a named file it matches, never a folder or another glob, so such cards keep apart. A card is unstarted, with a note, only for an entry the matcher does not know (a character class such as `[abc]`). This reverses the earlier rule (320-A) that any `*`, `?` or `[` made the card unstarted.
- **AREA-1** Categories (called lanes, then areas, before 979-A and 980-D) are labels: a card's category and short name, its default scope (SC-4), the
  worker's brief (LANE-5), and colour coding and filtering on the page (roadmap item 1). They no longer
  limit parallelism, and since 969-A they no longer limit which workers take a card either (DISP-17 is gone).
- **AREA-2** (Q10 of B3, answered by the developer on 560-A.) A redefined category is accepted at once and
  never held (LANE-2). Left in the code but dead: `CategoryRegistry.Held` (always empty),
  `PageWaitingKind.HeldLane`, the Accept buttons and `/accept-lane`. A later card may remove them.

### Card moves (card 660-B)

- **MOVE-12** Workers claim and finish cards by writing request files, and the scheduler is the only
  writer of card moves. The whole design is in [card-moves.md](card-moves.md), rules CM-1 to CM-22.

### Review panel (card 650-C, roadmap item 6): current behaviour, assumptions of the card

- **REV-1** A review card's panel lists the commits on `<remote>/<branch>` whose message contains the
  card's id (`<n>-L`), from the card's last `jobs: start` commit on; with no such commit, all of them.
- **REV-2** The panel's "health of main" is the page's current health result (the Health window's), with a
  note when that was checked at a commit other than the card's last.
- **REV-3** The files the Result names are found by a path-shaped word and served from the scheduler's
  repository checkout (`/repo-file`), so a file pushed to main shows only after that checkout has it.
- **REV-4** Done and Send back sit beside the panel (MOVE-6, MOVE-7). Send back has a note box, but
  `/resume-card` takes no note, so the note is not stored. **Open point, not decided: see B3 Q15.**
- **REV-5** There are no Host tests for the commit list (650-C could not change `Scheduler.Host.Tests`).

### Trends (card 630-A, roadmap item 3): assumptions of the card

- **TREND-1** The Control window can show a chart of the last 14 days: per local day and model, the cards
  finished (done or review, each card once per day and model; bars) and the runs (a thin line on a scale of
  its own, with no label beside it: no "runs" key and no "up to n"; card 941-C). Runs excluded by ACCT-6 stay excluded.
- **TREND-2** The chart is hidden unless two or more local days carry counted runs (`Trends.HasTrend`), so a
  scheduler with less than a day of data shows none.
- **TREND-3** The 14 days are read from accounting.csv itself (`Trends.RunsFromRows`, cached by the file's
  last write), because the loaded figures reach only 48 h back (ACCT-4); if the file cannot be read, the
  loaded figures are used. The proper home for the read is Host's `State/AccountingCsv` (open).

### Time limits from history (card 640-A, roadmap item 4): assumptions of the card

- **TLH-1** Per category and model, only the newest 30 finished runs count and only those finished within 30
  days (`AreaDurations.WindowRuns`, `WindowDays`); both cuts apply together.
- **TLH-2** A group with fewer than 3 done or review runs (`MinimumRuns`) gives no figure: no warning, no
  suggestion.
- **TLH-3** The typical length is the median (an even count: the mean of the two middles, rounded up on a
  half) and the long end is the 90th percentile by nearest rank (the ceil(0.9n)-th of the sorted minutes).
- **TLH-4** "The worker type it will get" is the lowest enabled slot whose premium-ness fits the
  card (DISP-18; busy slots and server state ignored). Accounting rows are matched to it through
  the same `ModelColumn.Resolve` that wrote them (ACCT-2).
- **TLH-5** The Queue window warns when a card's Time box is shorter than the group's usual length and
  suggests a Time box when it has none. The words are built in Core and tested there (`TimeBoxAdviceTests`);
  the two renderers only print them. The page display itself was not tested (640-A went to review).

### Message hooks (card 570-B)

- **HOOK-1** `scheduler-next.exe --deliver-message <claude|copilot>` is the hook command. It reads the
  hook's JSON on stdin and takes the worker's message file from the environment variable `PTK_MSG_FILE`
  (set for every worker). If a message is waiting it prints the agent's hook JSON with the text (prefixed
  "Developer:", saying it was added by the scheduler's message hook), moves the file to its `.read.txt`
  name and stamps the read time. With no message, or no variable, it prints nothing, reads no stdin and
  exits 0 at once.
- **HOOK-2** Claude workers get the hook with `--settings <log dir>\worker-hooks.claude.json` (written at
  start-up) for `PostToolUse` and `PostToolUseFailure`. The message lands when the running tool call ends,
  not during it; a message sent after the worker's last tool call stays in the file.
- **HOOK-3** Only Claude has hooks (`MessageHook.Delivers`); Copilot and text workers keep the prompt's
  file check (developer, 570-B Q1). The prompt for a hooked agent differs from the fallback one.
- **HOOK-4** *Not yet wired* (request #002, `jobs/requests/002-message-hook-wiring.md`, card 700-B):
  `TickHost.StartWorker` must set `Hooks = MessageHook.Delivers(agent.Format)` in the prompt request, so
  until then Claude workers get the fallback prompt; and the worker window's "read at HH:mm:ss" needs a
  `PageSlotView.MessageReadAt` field. "Message waiting" already shows while the file exists (WRK-14).

### Tray (cards 550-B and 670-B, roadmap item 8)

- **TRAY-1** `scheduler-tray.exe <repository> [the scheduler's own arguments]` starts `scheduler-next.exe`
  from beside itself, hidden, with those arguments plus `-NoBrowser`, and keeps the last 5000 lines of its
  stdout and stderr, read as UTF-8, for a log window (live, scrolled to the end).
- **TRAY-2** `-NoBrowser` on the scheduler's command line skips opening the page at start (CFG-15). It is
  read at the exe's entry point (`NoBrowserSwitch`); Core's `CommandLine` does not know it. The scheduler
  writes its console as UTF-8 without a byte-order mark.
- **TRAY-3** The page address is read from the scheduler's start line (`page http://...`). Double-click
  and "Open page" open it. The tooltip, every 3 s, is "<project> scheduler: running, N workers" from
  `/live.json`, or the last console line when the page does not answer.
- **TRAY-4** **Exit (stop all)** POSTs `/stopall` and waits for the scheduler to end, however long its
  workers take; only if the page refuses or does not answer does it kill the scheduler process.
- **TRAY-5** **Restart scheduler** ends only the scheduler process (a console Ctrl+C event to it; a kill
  of that one process only if the event did not end it; never its job object), so its workers carry on
  and are adopted (ADOPT-2); then it starts the scheduler again from the same folder with the same
  arguments. The log window keeps its lines and marks the restart.
- **TRAY-6** **Exit, leave workers running** ends the scheduler process the same way and then the tray;
  the next start adopts the workers.
- **TRAY-7** If the scheduler ends by itself, a balloon shows its last line and Restart is offered.
- **TRAY-8** *Open point (670-B Q1, developer, 2026-11-19):* on a desktop, Restart scheduler closed the
  scheduler and did not start it again, and the workers were killed rather than adopted. TRAY-5 is the
  intended behaviour; card 670-B is back in active to fix the shutdown path. A Windows security dialog also
  has to be answered (not yet explained).

### Build and output layout (card 600-B)

- **BUILD-1** The .NET build uses the artifacts layout: `code/src/Directory.Build.props` sets
  `ArtifactsPath` to the repository's `build/`, so every `bin/` and `obj/` (src, tests, tray) is under
  `build/` and nothing is made beside the source.
- **BUILD-2** The runnable folder is made by `dotnet publish code\src\Scheduler.Host -c Release -o
  out\windows\scheduler`, which publishes the tray into the same folder (`PublishTrayBesideScheduler`) and
  copies the themes (PAGE-11). The tray's build copy beside the scheduler asks the Host project for its
  output path, so it follows the layout.
- **BUILD-3** *Assumption:* the page's firewall rule and any testhost rules are bound to the exe path, so
  they must be added again for `out\windows\scheduler\scheduler-next.exe`.
- **BUILD-4** *Open:* the kit's `bin\projecttoolkit.ps1` still tells a new project to start with
  `dotnet run --project ...\Scheduler.Host`; that works but makes no published folder.

### Live feed (card 610-C)

- **PAGE-13** `/live.json` no longer sends the last 20 lines. Each run's lines are numbered from its first
  line and one build keeps the last 500. The page sends the number it has reached per slot
  (`?since=4:1234,1:87`); the answer holds that slot's later lines (at most 500 in one answer), the line
  being written in its own `partial` field, the number the window starts at (`from`), the run's last line
  (`next`) and the log they came from (`run`). With no `since` a fresh page gets the last 500. The page
  appends what arrives, keeps at most 500 lines per window and clears the window when the slot starts
  another run. The answer also carries `page`, the snapshot's version (a hash without the build time, host
  load, and each worker's log tail and stats), which the page compares with `meta[name=page-version]` to
  refresh itself.

### Worker types with caps (card 850-D, roadmap item 9; design 530-D, developer's answers 2026-11-19)

Worker types replace the fixed slots of DISP-15, DISP-17 to DISP-19, WRK-1 to WRK-3, WRK-7, TIME-10,
TIME-12, TIME-15's state, ADOPT-2 and ACCT-1; those rules say so where they stand, and the WT rules
below are the authority. Decisions: each type names the projects it serves (empty = all); a type
has no category list and no per-category cap (969-A); a `Worker:` slot number matches the type migrated
from that slot until the pool form is first saved, then nothing; after Stop or Kill from the page the
card waits under Waiting on you with Resume; caps are per type only, with no per-server limit; caps
live in workers.json with the type's other settings (a change is a commit). Types go into today's
one-project scheduler first, the pool after (WT-7, WT-C8 and WT-C9 apply once the pool is wired into
Host). Not built yet: the test plan is `test-plans/worker-types.md`.

### Versioning (cards 951-A and 952-D; developer's rule 2026-11-19)

The version is `Major.Minor.Rev`, written once, as `<Version>` in `code/src/Directory.Build.props`; it starts at 3.0.0 on 2026-11-19. CFG-18 says where it is shown.

- **VER-1** **Major** is a major change in functionality; **Minor** is an interface change that is not backwards compatible (a config file, page route, card format or command line that old files or callers no longer work with); **Rev** is a bug fix, or a behaviour change that keeps every interface working.
- **VER-2** The version lives only in `code/src/Directory.Build.props` `<Version>`; `CoreInfo.Version` and every display read it from the build, and nothing else holds a copy.
- **VER-3** A card that changes shipped code (anything under `code/src/` that changes behaviour) bumps the version as its last commit before finishing: Rev for a fix or a compatible behaviour change; Minor, with Rev back to 0, for an incompatible interface change; Major only when the card says so (the planner sets it). Tests-only, docs-only and jobs-only changes do not bump.
- **VER-4** The worker pulls right before the bump; if the push is rejected, it pulls and bumps again from main's value, never lower. The card's Result says the new version.

### 1. Config

#### The `pool` section

```json
"pool": {
  "maxWorkers": 4,
  "types": {
    "spark":  { "max": 2, "agent": "",       "model": "spark", "endpoint": "http://spark.local:18400/v1" },
    "sonnet": { "max": 1, "agent": "claude", "model": "claude-sonnet-5-5", "premium": true },
    "opus":   { "max": 1, "agent": "claude", "model": "claude-opus-5-5",   "premium": true }
  }
}
```

- **WT-C1** `maxWorkers`: how many workers run at once in all (across every project of a pool).
  An integer of 1 or more; missing or invalid -> scheduler.json's `slots` (CFG-6, default 2).
  `-Slots` on the command line pins it for one run, as it pins the slot count today (CFG-8).
- **WT-C2** `types`: an object; each key is a type's name. Names compare case-insensitively (G-1).
  A name that is `premium` or only digits is refused (logged, the type skipped): those words mean
  something else in a `Worker:` line (DISP-18).
- **WT-C3** A type's fields: `max` (integer, 0 or more; 0 = off, which is what Disable was; missing
  -> `maxWorkers`), `agent` (an entry of scheduler.json's `agents`; empty = the `worker` section, as
  a worker's `agent` today), `model`, `endpoint`, `metricsUrl`, `premium` (bool, default false),
  and, in a pool only, `projects` (list of project names the type serves; empty = every project). A type has no categories list (969-A): it takes a card of any category.
- **WT-C4** A type's effective worker (endpoint, model, metrics, agent, cli) is `Effective-Worker`
  (WRK-4) with the type in place of the worker definition. Nothing else about agents changes.
- **WT-C5** The order of `types` in the file is meaningful: when several types could take a card,
  the first listed with room takes it (WT-5). JSON object order is kept as written.
- **WT-C6** Invalid JSON, or a `pool` with no valid type: logged, the previous settings kept (as
  WRK-1). A sum of `max` above `maxWorkers` is allowed (that is the point: caps share the total).

#### Where it lives

- **WT-C7 One project**: in that project's `jobs/workers.json`, read from main as today (WRK-1),
  re-applied only when its text changes. The page's Worker types panel writes it back the way
  `/worker-settings` does today (commit and push, keeping main's line endings).
- **WT-C8 A pool**: in the top-level pool config, beside `projects`, `port`, `theme` and
  `remoteControl` (330-B Q3). Each type may name its `projects`. The projects' own workers.json
  files are not read while they run in a pool (logged once per project at start). The panel writes
  the top-level file, which is the PC's own (not in a repository).
- **WT-C9** Which agents exist still comes from each project's scheduler.json (CFG-10). In a pool, a
  type's `agent` is looked up in the scheduler.json of the project whose card it runs; a type whose
  agent a project does not define does not fit that project's cards (noted, WT-8).

#### Migration from today's workers.json

- **WT-C10** A workers.json with a `pool` key uses it; `slots` and `workers` beside it are ignored
  (logged once). Without `pool`, the scheduler derives the types, in memory, from `slots` and
  `workers` and goes on working unchanged:
  1. `maxWorkers` = `slots` (or `-Slots`).
  2. Each slot 1..`slots` is one worker: its `workers.<n>` entry, or an empty one (the `worker`
     section) when it has none.
  3. Workers with the same agent, endpoint, model, metricsUrl, categories (upper-cased) and premium are
     one type. Its `max` = how many of them are enabled in workers-state.json (WRK-3), so a disabled
     slot carries over as one less in the cap, and all disabled is cap 0.
  4. A type's name: the first member's `name`; else `Model-Short` of its effective model; else
     `worker`. A name already taken gets `-2`, `-3`, ...; a name refused by WT-C2 gets `worker-` in
     front.
  5. The type keeps the slot numbers it was made from, for `Worker: <number>` (WT-3).
- **WT-C11** The first Save from the Worker types panel writes the `pool` form, removes `slots` and
  `workers`, and updates the file's `about` text. From then on workers-state.json is not read. Until
  that Save nothing is written: an existing project keeps working with no change to its files.
- **WT-C12** This project's workers.json migrates to: `maxWorkers` 4; `worker` (spark through the
  default agent, max 2); `Sonnet` (claude, claude-sonnet-5-5, premium, max 1); `Opus`
  (claude, claude-opus-5-5, premium, max 1). `Worker: Opus` on a card picks the Opus type by its
  name, as it picks slot 4 by its name today.

### 2. Dispatch

What stays exactly as today: the plan's candidates and their order (DISP-1 to DISP-14, DISP-22 to
DISP-24: orphans, priority, dependencies, scopes), the per-project gate (310-A Q1: a red project
stops only itself), the pool's choice between projects (MP-5 to MP-7: resumes first, then the
project furthest below its share; priority never jumps the weights), per-project time limits and
retries (310-A Q4), and the meaning of a `Worker:` line (310-A Q3; a slot number as in WT-3).

- **WT-1 Total.** A worker starts only while fewer than `maxWorkers` workers are running, counted
  across every project of the pool, adopted workers included.
- **WT-2 Cap.** A worker of type T starts only while fewer than T's `max` workers of type T are
  running. `max` 0 means T never starts. A cap or total lowered below what is running stops nothing:
  the running workers finish, and no new one of that type (or none at all) starts until the count
  is below again (as WRK-2 never lowers the slot count below a running slot).
- **WT-3 Fit.** Type T fits card C when all hold:
  - `Worker:` (DISP-18, unchanged in meaning): no `Worker:` line -> T is not premium;
    `Worker: premium` -> T is premium; any other value -> it equals T's name, T's effective model,
    `Model-Short` of that model, or models.json's `short` for it (case-insensitive). A value of only
    digits (a slot number) matches the type made from that slot by WT-C10 until the first Save from the Worker types panel writes the `pool` form (WT-C11), and nothing after.
  - Category: none. A type fits a card of any category; it has no categories list (card 969-A removed `areas`; an old `areas` key is ignored and logged once).
  - Pool only: T serves C's project (`projects` empty or naming it), and the project defines
    T's agent (WT-C9).
- **WT-4 Usable now.** A type that fits is usable now when WT-2 allows one more, its agent is not
  out of quota (WT-9) and its model server answers (`Server-Up`, ACCT-8, cached 20 s; no endpoint =
  up).
- **WT-5 Which type.** Of the usable types that fit, the first in the config's order (WT-C5). This
  lets the developer list the cheaper type first.
- **WT-6 Order, one project.** While the state is running and WT-1 allows one more, each candidate
  in DISP-14 order starts on the type WT-5 gives it. A candidate with no usable type is skipped and
  the next one goes (no card waits behind another card's type). A resume counts its retry first
  (DISP-16); a start that fails is not tried again this tick (as today).
- **WT-7 Order, pool.** While WT-1 allows one more: every eligible project (`PoolGate.Eligible`)
  offers its first candidate, in its own DISP-14 order and not yet tried this tick, that has a
  usable type (WT-4); if any offer is a resume, only resumes take part (300-A Q3); of those, the
  project furthest below its share (MP-6, 300-A Q2) starts its card on WT-5's type. Repeat until no
  project has an offer or WT-1 stops it. This replaces "a worker comes free, which project gets
  it": with types, a free place in the total is offered to the cards, and the card's fit picks the
  type.
- **WT-8 Scopes while waiting.** A candidate that cannot start keeps its scope taken for the rest of
  the tick (DISP-13 unchanged), so a later card that overlaps it does not overtake it. With caps this
  happens more often than with slots; it keeps the order cards were planned in. (Spec C4 flagged the
  category version of this; with scopes only overlapping cards wait.)
- **WT-9 Quota, per agent.** An out-of-quota result (TIME-15) marks the run's agent out of quota,
  with the time; types using that agent do not start, in every project, and other agents' types
  run on (310-A Q2). This replaces the project-wide `paused-quota` state, in a single project too:
  the state stays `running`. The page's Resume clears every agent's mark; the Worker types panel
  has a Resume per agent. The retry count is given back as today (TIME-15).
- **WT-10 Model servers.** A type whose server does not answer does not start (WT-4), as today.
  There is no separate per-server limit: a type's `max` is the capacity of its server, and two types
  on one server share it only through their caps.
- **WT-11 Threads.** Threads per worker = max(1, floor(cores / `maxWorkers`)) (WRK-7, MP-3), set as
  today in the jobs environment variable and `{jobs}`.
- **WT-12 Queue notes.** A candidate that is not started gets one note (DISP-19's place), with
  "orphan to resume; " in front for orphans, in this order of checks:
  1. state not running -> "next up; dispatching is <state>" (as today; per project in a pool).
  2. no type fits at all -> "next up; asks for worker <X>, and no worker type fits" (with a
     `Worker:` line), or "next up; no worker type serves project <P>" (pool).
  3. otherwise, for each type that fits, why it is not usable, joined by ", ":
     "<type> is off" (max 0), "<type> (<r> of <max> running: <cards>)" for a type at its cap, <cards> being the cards its running workers hold in slot order, up to three then "+n" (the bare "<type> (2 of 2 running)" when no card names are known; 935-A, `TypeDispatcher.NotUsableBecause`), "<type>: <agent> out of quota since HH:mm",
     "<type>: model server does not answer"; the note is "next up; waiting for <reasons>"; one full type reads "next up; waiting for <type> (<r> of <max> running: <cards>)".
  4. every fitting type usable but the total reached -> "next up; waiting for a free worker
     (<n> of <maxWorkers> running)".
  The Queue window's worker column and the time limit from history (640-A, `TypeDispatcher.Fitting`)
  use the type WT-5 would pick if nothing were running and no agent were out of quota.
- **WT-13 Types removed or changed while running.** A running worker keeps the type and settings it
  started with. It counts toward WT-1 always, and toward the cap of a type of the same name if one
  still exists. Its window shows its type name, with "(removed)" when the type is gone.
- **WT-14 Dry run.** Logs "DRY RUN <type> in slot <n> <- <card>" (DISP-21).

### 3. Worktrees

- **WT-W1 Folders.** Each project has the folders `<workspaceRoot>\slot1` .. `slot<maxWorkers>`
  (WRK-5's names and branches, so today's worktrees are reused as they are). Any type uses any
  folder. A git worktree belongs to one repository, so in a pool each project has its own set; a
  project never runs more than `maxWorkers` workers, so its set always has a free folder.
- **WT-W2 Taking a folder.** A starting worker takes its project's lowest-numbered folder with no
  running worker (an abandoned worker, TIME-11, frees its folder as it frees its slot today), and the
  folder is prepared as WRK-5 says (fetch, checkout -B, reset, clean) and its files cleared (WRK-6).
- **WT-W3 The worker's number.** The folder number is the worker's number: `stop-slot<n>`,
  `warn-slot<n>`, `msg-slot<n>.txt`, the prompt's worktree, running.json's `slot`, accounting.csv's
  `slot`, the live feed and the page's `?slot=` keep using it. In a pool the number is unique only
  within a project; page actions also carry `project=` (as 340-C's combined page already posts).
- **WT-W4 Fewer folders.** Folders above `maxWorkers` after it is lowered are left on disk, unused;
  nothing deletes a worktree.
- **WT-W5 Adoption.** running.json gains each worker's `type` (ADOPT-1). At start an entry is adopted
  by pid and start time exactly as ADOPT-2; it keeps its folder number even above `maxWorkers`
  (ADOPT-2's "raises the count" goes: WT-1 and WT-2 count it instead). An entry written before this
  change has no `type`: it gets the WT-C10 type of its slot, or, when none, counts toward WT-1 only.
- **WT-W6** The health check's worktree (HLTH-3) is unchanged.

### 4. Page

- **WT-P1 Worker windows**: one per running worker, in a pool across all projects (project shown,
  and filtered by the project dropdown, MP-8). Title: "<type> - slot <n> - <card>". Buttons: Minimize
  or Expand, Stop, Kill, and the message box as today (WRK-14). No window for an idle folder, and no
  Disable, Enable or Settings. With none running, one line: "No workers running (0 of 4)".
- **WT-P2 Stop and Kill** send the stop request (TIME-10, `pageStopGraceMinutes`) or kill (TIME-12)
  as today, but there is no slot left to disable. The card then waits under Waiting on you, "stopped from the page",
  with Resume.
- **WT-P3 Worker types panel** (in the control window, replacing the per-worker Settings dialogs):
  the total (`maxWorkers`, a number); then one row per type, in config order: name, "<running> of
  <max>", the cap (a number; 0 = off), agent (a choice of scheduler.json's agents and the default),
  model, endpoint, metrics URL, premium, categories, and in a pool projects; an Add type row; Remove on a
  type with none running (WT-P7); Save. An agent out of quota shows "out of quota since HH:mm" and Resume.
  Save writes the config as WT-C7 / WT-C8 say and applies it on the next tick.
- **WT-P4 Routes.** Gone: `/enable`, `/disable`, `/worker-settings`, `/slots-add`, `/slots-remove`
  (Spec C10). New: `/worker-types` (POST, the panel's form) and `/agent-resume?agent=<name>`. Kept:
  `/stop`, `/kill`, `/send` (by `slot`, plus `project` in a pool), `/stopall`, `/open-workers`.
- **WT-P5 status.txt** (ADOPT-3): one line per running worker (type, slot, card), then a line
  "types: worker 2/2, Sonnet 0/1 (off), Opus 1/1", then the lines it has today.
- **WT-P6 accounting.csv** (ACCT-1) gains a `type` column at the end; an old header is rewritten in
  place as ACCT-4 does for `model`. Stats per model (ACCT-6) are unchanged.
- **WT-P7 Rename, removal and caps in Save** (card 947-B, developer decision 2026-11-19). Each row posts
  the name it was loaded with, so a rename is never read as a remove plus an add.
  - **Rename**: the same commit that writes workers.json rewrites the `Worker:` header line of every card
    in `queue/`, `active/` and `review/` that names the old type (case-insensitive, the value only, a
    `(reason)` after it kept); the commit message names the cards it changed. A worker running under the
    old name keeps running and is adopted under the new name (shown so, and written into running.json at
    the save, so a restart adopts it likewise).
  - **Removal**: a type left out of the list is refused, with nothing committed, when open cards name it
    (the notice names the cards) or when one of its workers is still running (the notice names the type
    and each slot with its card: "type Sonnet has slot 2 (952-B-premium) running: stop it or keep the
    type"). A type no card names and with nothing running is removed.
  - **Cap 0 is "off"**: always allowed, even with workers running (they finish; no new one starts).
  - **A cap-only change** (no rename, add or removal) touches only workers.json: no card is rewritten.


## A14. In-memory model of main (cards 1060-A to 1067-B, roadmap item 5, step 5)

Written by card 1069-D from `state-cache.md` (design, card 1058-D) and the build cards' results. This is current
behaviour: the rules below are built and tested; the numbers and the cost tables stay in `state-cache.md`
(sections 5 and 7). Rule IDs `MM-1` to `MM-18` are the test names (`MM_<n>_...`).

**The model.** Per repository (keyed by its full path, as the lock table is, DL-22; projects sharing a repository
share it) the Host keeps one immutable snapshot of `<remote>/<branch>` at one commit, replaced whole when main
moves. It holds: the commit; the jobs tree (path to blob id for each project's jobs folders); the open cards (parsed
`Card`); the done and archive cards' headers (title, status, type, plan path and step, version, depends, time box,
worker, with id, number, category, folder, blob id); the requests; the text of `scheduler.json`, `categories.json`,
`workers.json`, `models.json` and `README.md`; watched files outside `jobs/` (MM-6); the commit log; the pool view.
Not kept: card text (read on demand from the object store), whole-repository trees, working-tree state. Readers take
the current snapshot (one reference read, no lock) and keep it for their step. Code: Core `MainModel/` and
`Contracts/IMainModel`; Host `Git/Objects/` (the reader), `Git/MainModels*.cs`, `Git/InProcessHistory.cs`.

#### Reading the repository in process

- **MM-1 One reader, read only.** `MainObjects` reads a repository's refs and objects in process: the git folder
  (a `.git` file's `gitdir:` and a worktree's `commondir` followed to the common folder); refs from the loose file,
  else `packed-refs` (a loose ref wins; peeled lines skipped); objects loose (zlib) or in packs through each pack's
  `.idx` v2 (fanout, sorted ids, 32- and 64-bit offsets); pack entries commit, tree, blob, tag, `OFS_DELTA` and
  `REF_DELTA` (a delta base looked up in any pack or loose), with delta bases cached per read by offset. It ignores
  the multi-pack index, commit-graph, bitmaps, `.rev` and `.mtimes` (none is needed to find an object). It never
  writes into the repository, never takes git's locks, and computes no object ids (it only looks them up).
- **MM-2 What it refuses, and the fallback.** A repository with `extensions.objectFormat` other than sha1,
  `extensions.refStorage` (reftable), a promisor remote or partial clone filter, `objects/info/alternates`, a shallow
  file, or a format version above 1 with any extension it does not know is refused at load: that repository's model
  is read through the CLI reader instead (today's `MainRepository`, unchanged, MM-16) and the startup block logs
  `main model: <repo>: git CLI (<reason>)`. A setting `gitReader: "cli"` (pool file, then scheduler.json) forces the
  CLI reader everywhere. An object it cannot find or parse (a corrupt pack, a pack deleted under it) fails that read
  only: the advance keeps the previous snapshot (as CARD-3) and the next advance tries again.
- **MM-3 No handle across ticks.** Pack and index files are opened with `FileShare.ReadWrite | FileShare.Delete` for
  one advance or one on-demand read and closed after it; nothing is memory-mapped. The pack list is read again when
  an id is not found (a fetch or gc made a new pack). `.idx` contents are kept in memory per pack (by file name,
  size and time; AmigaX's 66,092 objects are about 2 MB), so a lookup opens only the `.pack`. git gc, repack and
  `worktree remove` are never blocked by the scheduler.

#### What the model holds and when it is loaded

- **MM-4 Parsed fields in memory, text on demand.** Open cards are kept as `Card`, parsed by the code that parses
  them today (`CardFiles.ParseText`, `RequestFiles`). A consumer that needs a file's text (the page's card view, the
  review panel's Result, the planner's current text, a page action's edit) gets it from the model by path at the
  snapshot's commit: the model looks the blob id up and the object reader inflates it, in process. Small settings
  files (MM-6) keep their text.
- **MM-5 Load at startup, in two phases.** A repository's model is built at startup by reading the tracking ref and
  walking only the jobs subtrees (and the settings files) in process. **Phase 1**: open cards, requests and settings
  files; when it is done, the repository's projects join (as 1015-B joins them in the background today), so the
  page and the first tick never wait for done cards. **Phase 2**, right after, in the background: done and archive
  headers (the views say "reading N done cards..." until it ends, VW-21). Repositories load in parallel, at most
  `PTK_JOBS` at once. There is no disk snapshot: the model is rebuilt at every start (developer, 2026-11-22); one is added only if the startup gate (`bin\startup-gate.ps1`) fails once many repositories join (done headers by blob id first).
- **MM-6 Any path can be watched.** A consumer may ask the model for a file outside the jobs folder (a plan, a design
  document, `VERSION`). The first ask resolves the path at the snapshot's commit in process and adds it to the
  watched set; every advance keeps watched paths current by the same tree diff. Unwatched paths are never read.

#### Refresh only on change

- **MM-7 The advance step.** The model moves from commit A to commit B only through the advance step, one at a time
  per repository: diff A's tree against B's in process, descending only into subtrees whose tree id differs and only
  along the jobs folders and watched paths; for each changed path, take the new blob id; parse only blobs not already
  known by id (a card moved between folders keeps its blob id and is not re-parsed: only its folder changes); drop
  what was deleted; walk the commits from B back to A for the commit log (MM-12). done/ is read only where it changed.
  The new snapshot replaces the old one in one assignment. An advance that fails keeps A (CARD-3).
- **MM-8 The ref is read every tick, in process.** At the start of each tick (and of the page's data step), the model
  reads `<remote>/<branch>` from the refs files (a stat and a small read, no process). If it differs from the
  snapshot's commit, the advance runs before anything reads. This picks up every way the ref moves: our fetch, a
  worker's push from its slot, the answers worktree's fetch, the developer's fetch in his checkout.
- **MM-9 One fetch per repository per poll, shared.** The fetch gate (the per-repository successor of `MainFetcher`
  and Core's `MainFetch`) decides when `git fetch -q <remote>` runs: at most once per poll interval per repository,
  plus once after a worker ends (today's `Ask`); a click or a move request never fetches (as 1002-B). Every consumer
  that used to fetch for itself - each project's tick, the planner pass, the health check's main sha, the audit -
  asks the gate instead and is answered by the fetch in flight or the last one, if it is younger than the poll
  interval (the poll interval is the limit; no new setting; the scheduler's own pushes need no rule of their own: a push moves the local tracking ref, and the writer applies the pushed commit to the model directly, MM-11; as built the gate's interval is `PollSeconds` less 2 s, so every poll still fetches). A failed fetch makes the next ask fetch. The fetch is followed by MM-8's ref read.
  The answers, health and slot worktrees' own fetches stay where they are (they reset a worktree, MM-17), and their
  ref moves reach the model through MM-8.
- **MM-10 Nothing read when nothing moved.** When the ref equals the snapshot's commit, no tree, blob or file is read
  and no process starts: an idle tick costs the fetch the gate allows (if one is due) and a file read.

#### The scheduler's own writes

- **MM-11 A write updates the model after its push.** When `GitWriter`'s commit is pushed (claim, move, page answer,
  page action, planner batch, the duplicate fix-up), the writer hands the model what it pushed: the new commit C, its
  parent P, and the paths it wrote or removed. If P is the snapshot's commit, the model applies the change directly,
  with no diff and no git process: it looks up only those paths in C's tree in process (C's objects are local: the
  answers worktree shares the repository's object store), takes their blob ids and parses their blobs as committed
  (not the writer's text, which `core.autocrlf` or a filter may have changed on commit), and the snapshot moves to C.
  If P is newer
  (the answers worktree's reset took a main that moved), the model first advances to P (MM-7; P's objects are already
  local, the answers worktree fetched them), then applies. The model never steps back: if P is not a descendant of the
  snapshot's commit within the walk limit (the snapshot is already past C, after a reload whose log does not hold C,
  or the two cannot be related), the push leaves the model as it is and the next ref read (MM-8) moves it. A rejected
  or failed push leaves the model as it was; the retry's fetch moves the ref and MM-8 picks it up.
- **MM-12 Others' pushes come through the ref.** A worker's push, the developer's, or another scheduler's (there is
  none, but nothing assumes it) is never guessed at: it reaches the model when the ref moves (MM-8, after MM-9's
  fetch for a push from outside this PC's worktrees). So the model is never newer than `<remote>/<branch>` and never
  older than the last fetch or local push, the same bound as today's reads (CARD-2), usually tighter. The commit log
  keeps the last 10,000 commits per repository or 30 days, whichever is more (the audits need only the commits since
  their last head; the review panel's search falls back to a CLI `log` only beyond the cap, MM-14).
- **MM-13 Pushes ready together go together.** When `GitWriter` holds several items for one repository that are ready
  at the same time (claims of a tick, a planner batch with a move), it makes them one commit per item, as today, but
  one push for all, keeping their order; if the push is rejected, the remaining items are retried from the new main
  one by one as today, so one bad item never drops the others (1046-B's push half). Each item's result is its own.

#### Every read from memory

- **MM-14 The consumers.** Each read of main moves to the model, behind the interface it already uses where it has
  one:

  | Today | Git today | From the model |
  |---|---|---|
  | `CardReader.Read` (tick, page, dispatch, dependencies, queue order, locks) | rev-parse, ls-tree, cat-file | open cards; done cards (views) |
  | `RequestReader` | rev-parse, ls-tree, cat-file | requests |
  | `ConfigLoader` (scheduler.json, categories.json at main) | show | settings files |
  | `TickHost` (workers.json, models.json, main sha) | show, rev-parse | settings files, commit |
  | `PoolProjectReader` (scheduler.json, README.md) | show | pool view |
  | `HostPageData` (card text, request text, review Result) | show | text on demand (MM-4) |
  | `HostPageActions`, `CardWorkerLine`, `FinishedCardCheck` | show | settings files, text on demand |
  | `AnswersPlannerMain`, `PlannerApply.Current` | ls-tree, rev-parse, cat-file | jobs tree, text on demand (at the commit the batch is written on: the model advances to the answers worktree's commit first, MM-11) |
  | `AuditRunner` (locks audit) | log --name-status | commit log |
  | `ScopeAudit` | diff --name-only | in-process tree diff of the two commits |
  | `ReviewCommits` (review panel) | log --grep --numstat | **stays git**: its one `git log --grep=<card> --numstat` per panel opened (developer, 2026-11-22) |
  | `TemplateRefresh` (ProjectToolkit's templates) | rev-parse, show, log --raw | a model of ProjectToolkit's repository watching `assets/templates/` (MM-6), with each template path's past blob ids from an in-process walk, cached |
  | `RepositoryLocator` (`rev-parse --show-toplevel`) | rev-parse | walk up to the `.git` folder or file, in process |
  | `HealthWorktreeRunner`'s main sha | rev-parse | commit |
  | `WipBranches` merge-base and diff | merge-base, diff | in process after its fetch of the wip branch (the fetch stays) |
- **MM-15 The target.** An idle tick and any page click that only reads start **no git process** unless the
  fetch gate allows a fetch (a click that writes is not covered: it costs only its write chain, developer, 2026-11-22). A page action or tick decision that writes costs only its write chain (MM-17). The same
  must hold at 50 projects x 5,000 cards: no per-card, per-file or per-project git process on the read path.
- **MM-16 The CLI reader stays.** `MainRepository` (CLI) stays, behind the same interface as the in-process reader:
  for refused repositories (MM-2), the `gitReader: "cli"` setting, and the parity tests (state-cache.md section 7). Both give the
  same model for the same commit.

#### What stays git

- **MM-17 The CLI only where it must be.** Fetch (MM-9); push (MM-13); the answers, health and slot worktrees (made,
  reset, cleaned, committed in); `CheckPushCommand` in its hook; the wip branch's fetch and push. 1047-B then merges
  what is left where one call can answer (`for-each-ref` for several refs, one `worktree list --porcelain` per tick
  instead of per slot, one `status --porcelain=v2 --branch`), and 1043-B's one git executable runs all of it.
- **MM-18 Visible behaviour is identical.** The page, the queue order, dispatch, dependencies, the audits, the
  review panel and the log lines say what they say today; the only differences are when they see a change (sooner,
  MM-8) and how long a tick or click takes. The tick's log line that reports step costs gains `git <n>` (processes
  started in the tick) and `model <ms>` (the advance's time) so the gain stays visible.

#### As built (differences from the design, and what stays as it was)

- **CARD-5 is amended, not dropped.** The tick's `CardReader` still does not open done/ cards for dispatch; the model
  reads their headers once, in process, after the open cards (MM-5 phase 2), for the views (VW-21).
- **Before the first load the CLI reads.** Each consumer reads the repository's model once it has loaded and uses the
  git CLI until then, so start-up does not wait for it (MM-5, MM-16). The first config read at a start has no model and
  uses git as before.
- **Three failed in-process reads in a row** switch that repository to the CLI reader until restart and log
  `main model: <repo>: <reason>; git CLI (in-process reads failed) until restart` (state-cache.md section 4).
- **Start-up log lines**: `main model: <repo>: in process, <n> open card(s) at <sha> in <ms> ms`, `main model: <repo>:
  <n> done card(s) in <ms> ms`, or `main model: <repo>: git CLI (<reason>), ...`.
- **MM-18's tick line** reads `...; git <n>, model <ms>` (git processes started in the tick, from the process-wide
  `GitRunner.Total`; the advance's time). The page-data refresh after a tick is its own step, `PageRefresh`.
- **MM-9**: a click or a move request never fetches; a worker's end does. When the gate does not fetch, the fetcher
  reads the tracking ref in process (MM-8).
- **MM-11**: `AnswersCommitter` hands each pushed commit, its parent and paths to the model (`RepositoryModel.Pushed`)
  and skips it when the model has already seen the commit. The wip-branch note's second push is not applied directly:
  the next tick's ref read picks it up.
- **MM-13**: `GitWriter.PushStep/RunGroup` and `AnswersCommitter.ApplyAll` push items of one repository that wait
  together once, rejected pushes redone one by one. The tick's claims and the planner's move requests still reach the
  committer one `Apply` at a time (one push each) until they call `ApplyAll` (`docs/known_gaps.md`).
- **MM-14 history reads** (`Git/InProcessHistory.cs`): the locks audit, scope audit, template check, repository lookup,
  the health check's short sha and the wip-branch merge base and diff read in process and fall back to git whenever the
  in-process answer could differ (a merge in the range, over 10,000 commits, a rewritten main, a multi-pack index for the
  short sha, a possible inexact rename, a folder git's `safe.directory` would refuse). `ReviewCommits` stays git.
- **MM-17**: the slot, answers and health worktrees each run one forced `checkout -q -f -B` instead of `checkout -B`
  plus `reset --hard`. No `worktree list` or `status` call exists to merge.
- **Not built:** a disk snapshot; LibGit2Sharp (our own reader instead, open point 1, developer's answer).

---

# Part B - decided design (roadmap item 1)

## B1. Multi-project

- **MP-1** One process serves several projects listed in a top-level config; each project keeps its own repository, `docs/jobs/` (cards, categories/categories, README, scheduler.json).
- **MP-2** Workers are one shared pool; any worker can take any project's card.
- **MP-3** Model-server capacity and build threads are budgeted across all projects (today's threads = cores / slots becomes cores / pool size).
- **MP-4** Each project has a weight (e.g. AmigaX 70, Other 30).
- **MP-5** When a worker comes free, only projects with a card that can run now on that worker take part.
- **MP-6** Of those, the project furthest below its share gets the worker: share = its worker-minutes over a sliding window (about the last two hours) against its weight. An idle project's share goes to the others.
- **MP-7** Within the chosen project the card is picked as today (A3: priority, orphans first, dependencies; categories replaced by scopes per B2).
- **MP-8** One combined page: one look, one project dropdown in the top bar (shown only with two or more projects, one selection for the page, remembered in the browser), one Waiting on you across projects, stats per project and per model.
- **MP-9** Planner outbox, health check and gates stay per project.

MP-1 to MP-9 stay as written above. The rules below (design `multi-project.md`, card 955-D, answers 2026-11-19, 989-D 2026-11-20) fix the details; the test plan is `test-plans/multi-project.md`. Not built yet at the time of writing: cards 997-A to 1001-B build them. **MP-C9** keeps a one-project scheduler exactly as today.

### B1.1 Pool config (MP-C*)

- **MP-C1 Where it lives.** The pool file is the PC's own, in no repository (WT-C8): `%LOCALAPPDATA%\ProjectToolkit\pool.json` by default, or the path after `-Pool`. The page's Worker types panel writes it in place (no commit).
- **MP-C2 `projects`.** A list, in order (the order breaks ties, MP-6). Each entry: `name` (required; compared case-insensitively; unique, a repeat is skipped and logged), `repo` (required; the project's main checkout), `weight` (MP-4; missing = the smallest other weight, 300-A Q5), `jobsDir` (default `jobs`), `remote` (default `origin`), `branch` (default `main`). An entry with no name or repo is skipped and logged. The name must equal the project's own scheduler.json `project`; a mismatch is logged and the entry skipped.
- **MP-C3 Top-level page settings.** `port` (default 8765), `listen` (beside the port; default `local`), `remoteControl` (default false), `theme` (a theme name the loader offers, or a path; empty = default theme), `logDir` (the pool's own log folder, default `%LOCALAPPDATA%\ProjectToolkit\pool`), `toolkit`. The projects' own `port`, `listen`, `remoteControl`, `page.theme` and `logDir` are not used for the page in a pool; their `logDir` still holds their state (MP-H3).
- **MP-C4 `pool`.** The worker types exactly as WT-C1 to WT-C6, with `projects` per type (WT-C3). The projects' own workers.json files are not read in a pool (logged once per project, WT-C8).
- **MP-C5 Categories per project.** Moot since 969-A: worker types have no categories list, so neither the pool file nor the Worker types panel carries per-project category lists.
- **MP-C6 What stays per project**, read from each project's own main: scheduler.json (agents, `worker`, health, `limits`, planner and outbox, `jobsEnvVar`, `promptExtra`, `workerWindow`, `workspaceRoot`, `logDir`), categories.json, models.json, cards, plans, requests, README and gates. Template refresh runs per project.
- **MP-C7 Re-reading.** The pool file is re-read when its text changes (checked each tick). Invalid JSON, or a file with no valid project, is logged and the previous settings are kept (as WT-C6). A project added or removed takes effect on the next tick; a removed project's running workers finish and are watched until they end (MP-H6). `port`, `listen` and `logDir` take effect only at the next start (logged).
- **MP-C8 Agents.** A type's `agent` is looked up in the scheduler.json of the project whose card it runs (WT-C9); an empty `agent` is that project's `worker` section. Quota is kept per agent name across the pool (WT-9, MP-17).
- **MP-C9 One project unchanged.** `scheduler-next <repository>` is today's one-project scheduler with its own scheduler.json and workers.json; none of B1.1 to B1.4 applies. A pool starts only with `scheduler-next -Pool [<file>]` (no repository argument; `-Slots`, `-NoHealthCheck`, `-NoBrowser`, `-Paused`, `-DryRun` apply to every project; `-Port` overrides the pool's port). A pool file with one project is a valid pool.

### B1.2 Pool dispatch (MP-10 to MP-22)

The projects' plans (DISP-1 to DISP-14, DISP-22 to DISP-24), WT-1 to WT-14 and WT-7's loop stay as they are.

- **MP-10 One dispatch per tick.** Each tick every project runs its steps up to and including planning, in pool order; then one dispatch (WT-7) runs across all of them; then each project saves retries and writes its status. A project whose cards cannot be read keeps its previous list (CARD-3) and still offers.
- **MP-11 Total and caps are pool-wide.** WT-1's running count is every running worker of every project (adopted ones included); WT-2's count for a type is that type's running workers in every project.
- **MP-12 Eligible projects.** A project offers cards only when its own state is running; paused, draining and paused-red offer nothing and get the note "next up; dispatching is <state>" (WT-12 1). A red main stops only its project.
- **MP-13 Resumes first.** If any eligible project's first usable offer is a resume (an orphan in active/), only resumes take part in that round; among them the shares decide as MP-14.
- **MP-14 Weights choose the project; priority orders inside it.** Of the offers, the project furthest below its share starts its card: share = the project's worker-minutes in the last 2 hours (finished runs clipped to the window, running ones up to now) over all offering projects' minutes; gap = weight / sum of the offering projects' weights minus share; the largest gap wins; ties go to the project listed first. A project with no minutes in the window beats one with some when there are two projects; with three or more the gap decides. A `Priority: high` card never jumps the weights; it is first only inside its project.
- **MP-15 Runs for the shares.** The window's runs come from each project's accounting.csv (rows whose end is in the window) and from every running worker. A start inside a tick adds no minutes in that tick, so one project may take several places in one tick until its offers run out.
- **MP-16 Orphans of a removed project.** An orphan is resumed by its own project only. An orphan of a project no longer in the pool file is not resumed; it stays in that project's active/ (logged once).
- **MP-17 Quota.** An out-of-quota result marks the run's agent name out of quota (WT-9); every type with that agent stops starting, in every project. Resume (per agent, or Resume all) clears it for the pool.
- **MP-18 Retries and time limits** are each project's own: its maxRetries, maxMinutes, stallMinutes, warnMinutes and grace minutes apply to its cards; the time limit from history reads that project's accounting.csv.
- **MP-19 Threads.** Threads per worker = max(1, floor(cores / pool `maxWorkers`)) (MP-3, WT-11), given in the worker's project's `jobsEnvVar` and `{jobs}`. Each project's health check uses the same figure.
- **MP-20 Scopes across projects.** Two projects' cards are checked against each other only when their repositories are the same (DISP-24); in a pool of separate repositories no card waits for another project's scope.
- **MP-21 Queue notes.** WT-12's notes, per project; "next up; no worker type serves project <P>" when no type's `projects` names it; "next up; <P> is above its share (<used>% of <weight>%)" for a project that offered but lost on shares.
- **MP-22 Dry run.** `-DryRun` logs "DRY RUN <project> <type> in slot <n> <- <card>" and starts nothing (WT-14 with the project).

### B1.3 Pool host (MP-H*)

- **MP-H1 One process.** One `scheduler-next -Pool` process runs every project. It holds the pool mutex `Global\ProjectToolkit-pool-scheduler` and each project's own mutex (CFG-14), so a one-project scheduler of a pooled project, or a second pool, refuses to start with the existing message. A project whose mutex is taken is left out and shown as "not in the pool: its own scheduler is running"; the pool retries it each minute.
- **MP-H2 Per project**, built as `HostComposition` builds them today: main repository (one fetch per tick), answers committer, card reader, card requests (moves folder in the project's logDir), planner and outbox, health check and worktree, template refresh, slot worktrees (`workspaceRoot\slot1..slot<maxWorkers>`, WT-W1), slot files, accounting, state files, retries, stopped-card hold, finished-card check, scope audit and git-output log.
- **MP-H3 State files stay per project**, in the project's logDir: running.json, retries, accepted categories, planner switch, accounting.csv, status.txt, prompts and worker logs, slot files, move requests. The pool's logDir holds the pool log (each line prefixed with its project where it concerns one) and the pool's `status.txt` (projects, state, share, then the WT-P5 lines).
- **MP-H4 Shared**: the clock, worker processes and job objects, host load, the model-server cache (one probe per endpoint), agent quota, the page server and live feed, the tick loop and its pause (the smallest `pollSeconds` of the projects).
- **MP-H5 Worker numbers.** A worker's number is its folder number, unique within its project (WT-W3). Everything a worker sees (prompt, `stop-slotN`, `warn-slotN`, `msg-slotN.txt`, moves folder) is in its project's logDir, so its brief does not change. Everything the page and live feed address carries `project` and `slot`.
- **MP-H6 Adoption.** At start each project's running.json is adopted (ADOPT-2, WT-W5) into that project's tick; adopted workers count toward MP-11. A project dropped from the pool file while it has running workers keeps being watched (reaped, finished, accounted) until they end, but offers no cards.
- **MP-H7 Health and gates** stay per project (MP-9): a red main pauses only that project (paused-red); its health check runs in its own worktree with its own command.
- **MP-H8 Stop all and exit** stops every project's workers and exits. Pause, Drain and Resume act on one project (the page sends `project=`, the one picked in the project dropdown) or, with no project (All projects), on all of them.
- **MP-H9 Tray.** `scheduler-tray -Pool [<file>]` starts the pool. The tooltip names "pool" and each project's state ("AmigaX running, ProjectToolkit paused-red"); double-click opens the pool's page; Exit and View log are unchanged.

### B1.4 Pool page (MP-P*)

One page on the pool's port, `CombinedPageRenderer`, fed one page snapshot per project plus a pool part (weights, shares, worker types, total).

- **MP-P1 Settings.** Port, `listen`, `remoteControl` and theme are the pool file's (MP-C3). The theme choice lists the pool's theme first, then every theme the loader offers, including each project's own `page.theme`.
- **MP-P2 Project selector.** One dropdown in the top bar beside the theme menu, in every view (Workers, card board, Gantt). Shown only while two or more projects are active; with one project it is absent and the page looks as today. Items: **All projects**, then each project by name. The choice filters every window and view, is one selection for the whole page (kept across views) and is remembered in the browser. Pause / Drain / Resume act on the selected project, or on every project for All (MP-H8).
- **MP-P3 One Waiting on you** across projects, each row labelled with its project; every button posts `project=` beside `card=`.
- **MP-P4 Projects window.** Per project: state, weight, share in the window (used % against weight %), running workers, queue length, health (green / red / checking, last sha), planner line, and Pause / Drain / Resume / Check main for that project.
- **MP-P5 Statistics** per project (ACCT-6) and per model, with an All projects line per model summed from every project's accounting.csv; trends per project with the filter, All sums them.
- **MP-P6 Worker windows.** One per running worker across projects (WT-P1), titled "<project> - <type> - slot <n> - <card>"; Stop, Kill and the message box post `project=` and `slot=`.
- **MP-P7 Worker types panel** (WT-P3) writes the pool file (no commit) and shows a Projects column per type; no per-project category lists.
- **MP-P8 Routes.** Every route that names a card or a slot takes `project=`; without it, in a pool, it is refused ("which project?"), except the pool-wide routes `/`, `/tick`, `/stopall`, `/worker-types`, `/agent-resume`, `/font`, and `/pause`, `/drain`, `/resume` (all projects, MP-H8). Per project: `/answer`, `/card`, `/done`, `/health`, `/health-now`, `/kill`, `/log`, `/open`, `/open-lanes`, `/open-workers`, `/planner-toggle`, `/repo-file`, `/request`, `/resume-card`, `/retry`, `/review`, `/send`, `/stop`, `/template-keep`, `/template-overwrite`. `/accept-lane` is dead (AREA-2) and not carried over. The one-project page ignores `project=`.
- **MP-P9 Project chip.** Every list row and card that belongs to a project (Queue, Recent events, Waiting on you, Requests, board cards, Gantt rows, the Projects window) carries a project chip in the style of the category chip (970-C): the project's short name in a colour of its own (stable per project, readable in every theme, distinct from category colours), full name and repository path on hover. Shown only while the dropdown is on All projects; left out with one project selected or only one active. Worker window titles keep the project name in text (MP-P6).

**Starting numbers (955-D Q5).** Type caps spark 6, Sonnet 4, Opus 2. Weights 70 / 30 and `maxWorkers` 6 are proposals, settled at AmigaX's cut-over.

## B2. Per-card scopes instead of categories

- **SC-1** Each card names its scope: folders, files or patterns it expects to touch (an extension of today's `Locks:` line).
- **SC-2** A free worker takes any ready card whose scope overlaps no running card's scope, in any project.
- **SC-3** Cards on different files run side by side; cards on the same file run one after another.
- **SC-4** Categories (today's categories) remain only as named default scopes and as the worker's brief of what it owns; a card without its own scope takes its category's.
- **SC-5** A card may allow overlap on a path (add-only files such as known_gaps.md); there the rule is pull, rebase, resolve, re-run the gates.
- **SC-6** Every rejected push and rebase conflict is logged, to show whether scopes can be loosened.
- **SC-7** Scopes are checked by the scheduler.
- **SC-8** (decided 2026-11-19) Patterns: `*` is any run of characters inside one path segment, `?` is one character, `**` (two or more stars together) also crosses `/`. A trailing `/` still means the folder and everything in it; a character class `[abc]` is not matched and keeps the card unstarted. Overlap between entries is DISP-25.

### B2.1 Dynamic file locks (DL-1 to DL-27)

*Design: `dynamic-locks.md` (994-D, approved 2026-11-20; amended by 1018-D, 2026-11-21), where each rule's reasons and the rejected alternatives are. Nothing here is built until cards 1016-A to 1023-D land; a `locks` switch in scheduler.json keeps it off until the switch-on trial. A card's **held set** is a list of scope entries (SC-8 syntax) in one repository: its initial claim plus the entries granted during its run. The scheduler keeps every running card's held set in a lock table per repository (DL-20).*

**Claim and grant**

- **DL-1** A starting card's held set is its scope (CardScope: `Scope:` line, else its category's default, plus `Locks:` paths) plus its remembered set (DL-10). Dispatch checks it against every running card's held set (ScopeOverlap, SC-2), not only its declared scope. The claim (`jobs: start`, CM-9) lands in the background (995-B): the held set is recorded in the lock table when the claim lands (`ClaimState.Landed`), not when the tick posts it. A card still starting is in the table as *starting*: its scope counts against other candidates (as MP-11), it has no held set or age yet and cannot request a lock; a failed claim (`ClaimState.Failed`) drops the entry.
- **DL-2** The planner keeps writing precise `Scope:` lines; dynamic locks are for what it could not foresee. A card with no `Scope:` takes its category's default (SC-4, DISP-23).
- **DL-3** Reading, building and testing any file needs no lock.
- **DL-4** Before changing a path outside its held set, the worker asks (`scheduler-next.exe lock <path>... --why "<reason>"`) and waits for the answer. Entries its held set already covers are granted at once.
- **DL-5** A request is granted when no other running card's held set overlaps any requested entry (`ScopeOverlap.Shared`, with add-only paths). All entries of one request are granted together or not at all.
- **DL-6** An entry that covers a whole category's default, the repository root, `**`, or more than 200 tracked files is refused ("too broad: ask for the files or the folder you need"). The scheduler never widens or narrows a request.
- **DL-7** Grants are checked against running cards only; a queued card that would overlap a new grant waits at dispatch, its note naming the holder.

**Release**

- **DL-8** A card's held set is released, all at once, in the same tick and before dispatch, when (a) its finish request (To: done or review) has been applied on main (CM-10), (b) its park request (To: active) or send-back (To: queue) has been applied, (c) it yields (DL-12), or (d) its run ends any other way (failed, killed, timed out, stopped).
- **DL-9** For (a) the release waits until the finish is on main: a finish that cannot be pushed (CM-18) keeps its locks.
- **DL-10** When a run ends without finishing the card (DL-8 b to d), the entries granted beyond its initial claim become its remembered set, part of its initial claim when it next starts (DL-1). A card that reaches done or review drops it. A parked (needs-human) card's remembered set is not held while it is parked.

**Conflicts: wait-die**

- **DL-11** A card's age is `(priority rank, card number)`: high before normal before low, then the lower number. It is fixed when the card's claim first lands and stored in the lock table, so a later change of its priority line does not change it.
- **DL-12** When a requested entry overlaps a holder's set: a requester older than every conflicting holder **waits** (the request stays open and is granted when the holders release, checked every tick and every release); a requester younger than any conflicting holder **yields**: the answer is `yield` with the path and the holder, and the worker commits its work to `wip/<card>` (never main), writes a resume line in Result, hands the card back with a move request To: active, Status `ready`, and ends `LANE-RESULT: yielded`. Its remembered set gets the entries it held and the entries it asked for.
- **DL-13** An older card waits at most `lockWaitMinutes` (scheduler.json `limits`, default 20) and never past its own warn time; then the answer turns to `yield`. A wait never ends by stopping a younger card.
- **DL-14** A run that ends `LANE-RESULT: yielded` with its request applied gives its run back (C8, never below 0) and does not count toward the stuck limit (C2, 940-A). A yielded card in `active/` is an orphan and goes first among cards of its priority (DISP-14).

**Exemptions**

- **DL-15** Never locked and never refused at push: (a) the shared add-only files in categories.json (`shared.addOnly`), including the `<Version>` line of `Directory.Build.props`; (b) the card's own card file in `jobs/`; (c) new files under `jobs/requests/`. Under `jobs/` only other cards' card files (`queue`, `active`, `review`, `done`, `needs-human`, `waiting`), `plans/`, `archive/`, `requests/answered/`, `.logs/` and `.planner-outbox/` stay refused even when held; every other path under `jobs/` is an ordinary path, allowed when held (developer, 2026-11-21).
- **DL-16** The push check reads the line counts of each add-only file in the pushed range: a change that deletes lines other than the `<Version>` line is a warning (log and the card's page row), not a refusal.
- **DL-17** When the pushed range changes the `<Version>` line, the new value must be greater than main's; a lower or equal value is refused ("bump again from main's version").

**Enforcement**

- **DL-18** Push check (prevention): the scheduler starts every worker with git configured through its environment (`GIT_CONFIG_COUNT`, `core.hooksPath` = `<log>\hooks\`) to run two hooks it owns: `commit-msg` appends a `Card: <card id>` trailer (from `PTK_CARD`); `pre-push` runs `scheduler-next.exe check-push`, which refuses a push to the main branch (non-zero exit) when any path changed in `<remote sha>..<local sha>` is outside the card's held set and DL-15, naming each path and the command to ask for it. Pushes to `wip/<card>` are never refused. It reads the lock table file only.
- **DL-19** Audit of main (detection): on every move of main the scheduler reads the new commits that are not its own and checks each against the held set its `Card:` trailer names, as it was when the commit landed. A commit with no trailer while workers ran, or with a path its card did not hold, is a stray: logged, shown on the card's row and the page, and the card's finish turns into review (CM-5: To: done becomes To: review, `review: changed <path> without its lock`). The audit never reverts anything.

**Storage, restart and projects**

- **DL-20** The lock table is one JSON file per repository, `<log>\locks\<repository key>.json`, written only by the scheduler (tmp then rename, as CM-1): per card its age, held set (each entry with the time granted), remembered set and open request (with its deadline); and the last audited main commit. It is not in git.
- **DL-21** At start the table is read before adoption. Cards whose workers are adopted (ADOPT-2) keep their held sets and open requests, deadlines carried over. Every other card in the table is released as DL-8 d. A card that was starting (claim not landed) has no worker to adopt: its entry is released with nothing to remember; if its claim had reached main it is an orphan in `active/` (DISP-14), if not it is still in `queue/`. A missing or unreadable table starts empty (running cards fall back to their initial claims, the audit begins at main's current commit) and the log says so.
- **DL-22** A lock table belongs to a repository: projects sharing one repository share its table; projects in different repositories never conflict. The card in a request is checked against the slot that wrote it, never taken on the worker's word.
- **DL-23** Locks do not look at worker types; a yielded card restarts with whatever type its `Worker:` line asks for.

**Workers and page**

- **DL-24** (pool page, `CombinedPageRenderer`; the Control window is gone and one-project mode is being retired) Each running worker's window and its Queue row show the card's held set: the initial claim on one line, granted entries on another, each with its age in minutes.
- **DL-25** A waiting card shows "waits for lock on `<path>` held by `<card>` since `<time>`, gives up at `<time>`"; a yielded card in the Queue shows "yielded: waits for `<path>` held by `<card>`". Dispatch's note for a card held back by the scope gate names the holder as today.
- **DL-26** (SC-6) Every grant, wait, yield, refusal, push refused by the hook and stray is a log line, and counted per day in the Projects window's statistics.
- **DL-27** (1018-D) The lock routine is the same for every agent, spark workers (local model, copilot CLI) included: the scope-mode prompt text is short and literal and names the `lock` command with the exe's full path (also `PTK_SCHEDULER_EXE`); the command's output is one line and its exit code is the answer: 0 `granted`, 2 `waiting` (run it again; no new request), 3 `yield`, 4 `refused`. A command waits at most 110 seconds per call. The switch-on trial includes a spark card.

## B3. Open questions (need Trevor's decision before the port)

**All closed except 15.** The answers are in `multi-project.md` section 0 and the Decisions table, and the rules are B1.1 to B1.4: 1 → MP-14; 2 → MP-14, MP-15; 3 → MP-13; 4 → MP-C4, WT-C8; 5 → DISP-22, DISP-24; 6 → MP-20; 7 → SC-8; 8 → scope audit at dispatch and on pushes; 9 → git output log; 10 → AREA-2; 11 → MP-12, MP-17; 12 → MP-C3, MP-H3; 13 → WT-3; 14 → MP-18. 15 stays open (650-C gap (d)). The text below is kept as the record of what was asked.

1. **High priority vs weights**: may a high-priority card jump the project weights? (roadmap)
2. Window and weight arithmetic: exact window length; "furthest below" as weight share minus actual share, or ratio; tie-break; do running workers' minutes so far count; does a project with no minutes in the window win outright?
3. Do orphan resumes go through the weights, or always first (today orphans come first within a priority)?
4. Worker definitions in a shared pool: today workers.json lives in each project repo. Where do pool workers (endpoint, model, premium, agent) live - top-level config? What replaces a worker's old lanes list? (Answered by 969-A: nothing; worker types have no categories list.)
5. Scope syntax: globs, folders with trailing `/`, `except` entries? Is it `Locks:` renamed, or a new `Scope:` line? Which file holds overlap-allowed paths (per card, or categories.json `shared.addOnly`)?
6. Overlap across projects: two repositories never share files - is the cross-project check only for projects that share a repository, or by absolute path?
7. *Answered (SC-8, DISP-25).* Overlap test for patterns: when do two globs "overlap" (exact rule for `src/*` vs `src/a.c`, `**`)?
8. Enforcement: scopes are checked at dispatch only, or are a worker's pushed changes also checked against its scope? What happens when a card touched outside its scope?
9. Logging rejected pushes and rebase conflicts: the workers do the pushing, so the scheduler cannot see these today. Does the worker report them (a line in its log/card), or does the scheduler parse the git output in the raw log?
10. *Answered by card 560-A (developer): a redefined category is accepted at once and never held (AREA-2, LANE-2).*
11. Red health / quota pause in a pool: does red main in project A pause only A's cards? Does an agent's quota pause stop all projects using that agent?
12. One page, one port: which project's theme, `listen`, `remoteControl` and logDir apply? Per-project state files (retries, accepted categories, planner switch) stay per project?
13. Is the per-worker `Worker:` request (slot number, name, model, premium) kept unchanged in the pool?
14. maxRetries and time limits: per project config, or global?
15. Review panel Send back (REV-4): the note box exists but `/resume-card` takes no note, so the note is not stored. Should it be stored (in the card's Dev section, or as a journal line) or should the box go? Open, not decided (card 650-C, gap (d)).

---

# Part C - flags (ambiguous, possible bugs, could not work out)

Not guessed; each needs a decision whether the port keeps or fixes it.

1. **Planner failure reported as sent** (`Planner-Step` L1497-1505): after a failed batch, the code falls through to `$sent++`, sets the last result to "sent HH:mm <message>" (overwriting "failed ...") and logs "planner: <message>" as if sent. Looks like a missing `else`/`continue`.
2. **Card that never claims is retried without limit**: CLOSED (card 940-A, rule TIME-17). Every run that ends without a result counts against a queued card, so it is stuck at maxRetries (DISP-11).
3. **Stuck queued card blocks an orphan for good**: the low-priority-orphan rule (L2886) does not exclude stuck, duplicate or held queued cards, so an orphan can wait behind a card that will never run.
4. **Category reserved by a card no worker can take**: DISP-13/15 - the first candidate takes its category even if no slot fits it (Worker:, categories, server down), so another card of that category that a free worker could run waits.
5. **Resume overrides red health**: `/resume` clears `paused-red` without a green check; the same sha is not re-checked (HLTH-2), so dispatching continues under a red banner. It also clears every card's retry count, not only stuck ones.
6. **Draining is one-way**: the page shows Resume in `draining`, but `/resume` only acts on `paused*`.
7. **Health never runs at start-up** and runs only after a worker finishes or Check main; pushes by others leave main unchecked. Intended?
8. **Retry count can go negative**: CLOSED (card 940-A, rule TIME-17). A count is never below 0; runs ended by quota, a down model server or a stop from the page give one back only down to 0.
9. **Resume-Card on a queue card does not clear retries**, but on active/review cards it does (L1535-1543); Answer-Card never clears them.
10. **`/slots-add` and `/slots-remove`** exist (L3966-3969) but no page button posts to them (the synopsis still mentions Slot + / -). Keep as API or drop?
11. **Three-digit ids**: decided 2026-11-19 (card 931-A): ids are any width; CARD-4, CARD-11, WRK-9, ACCT-5, PAGE-9 and CARD-20 say so.
12. **Category overlap check with globs**: `Lane-Overlaps` tests `$pa.EndsWith('/')` before trimming `*`, so an `owns` entry like `src/*` is never seen as containing another category's path (L1670).
13. **accounting.csv quoting**: only stop_reason is quoted; a `"` in it, or a comma in a card name, breaks the row (L852).
14. **Card.Premium** is set by any `Worker:` line (L625) but never read; `Worker-Fits` uses WorkerReq only.
15. **Waiting-on-dev cards in active/** appear both in Waiting on you and in the Queue window's orphan list ("claimed earlier; resumes when its category is free"), though they are not resumed (L3650 vs DISP-4).
16. **Health kill limit** uses maxMinutes (240), not a health-specific limit (L1725). Intended?
17. **Worker protocol not seen**: claim (README "step 3"), card fields and the session report are defined in the project's `docs/jobs/README.md`, which is outside the connected folders. Part A covers only what schedule.ps1 does; the README should be read before porting the prompt and claim rules.
18. **Already scaffolded**: the repository already holds a .NET solution (SchedulerNext.slnx, `code/src/Scheduler.Core`, `Scheduler.Host`, `Scheduler.Web`, three test projects under `test/unit/` with placeholder tests, xunit 2.9.3, net10.0, TreatWarningsAsErrors), created 2026-11-18 18:16. Left untouched.
