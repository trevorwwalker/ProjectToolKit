# Estimates for planners: design and plan

> The rules EST-1 to EST-34 now live in `spec.md` ("Estimates for planners ... as built"); this file is the design they were built from.

Card 1340-D, 2026-11-24. Design only: nothing here is built. It is the design for steps 2 to 5 of
`jobs/plans/estimates_plan.md` (roadmap step 13). The developer should approve it (the open points are under
Questions at the end, and on the card), and then it gets carded (section 8). Rule IDs `EST-1` onward are
stable names for test cases (`EST_<n>_...`). Once the cards land, `spec.md` takes them over, beside ACCT-1 to ACCT-9
and TLH-1 to TLH-5.

Sources: the plan's Goal and Decisions (developer, 2026-11-24 20:43, 20:45, 20:56); spec ACCT-1 to ACCT-9,
TREND-1 to TREND-3, TLH-1 to TLH-5, A14 (MM-1 to MM-18); `card-moves.md` (CM-1 to CM-22); the code named in
section 0; this repository's own `accounting.csv` (838 rows, 2026-11-18 to 2026-11-24) and its main (2,768
commits). Those two were used as test data for the counts below.

## 0. What exists today, and what that means

- **accounting.csv.** One file per project, in the project's log folder. That folder is
  `%LOCALAPPDATA%\<project>\scheduler` unless `scheduler.json` sets `logDir`
  (`Scheduler.Core/Config/SchedulerConfigLoader.cs:57`). It is outside the repository, so a planner never sees it.
  The pool's own log folder is `%LOCALAPPDATA%\ProjectToolkit\pool` (`Scheduler.Host/Pool/PoolConfigFile.cs:30`)
  and has no accounting file. Each project keeps its own (`Scheduler.Host/Program/ProjectParts.cs:106`).
- **Its columns.** Core's header is `finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model`
  (`Scheduler.Core/Accounting/AccountingRow.cs`, `Header`). Host writes that header plus `,type` (the worker
  type, WT-P6) plus `,card_type,version` (1030-A, `AccountingRow.CardColumns`). The full line is
  `Scheduler.Host/State/AccountingCsv.cs:21`. The real file here starts
  `finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model,type,card_type,version`. Rows are appended
  by `AccountingCsv.Add` from `Scheduler.Host/Processes/WorkerFinisher.cs:36`, one per finished run. `finished`
  is local time with no offset. `minutes` is `AccountingRow.MinutesOf(now - worker.Start)`, rounded half to even.
  Only `stop_reason` is quoted, and nothing is escaped (flag C13).
- **How an old file gains a column.** At start-up, `AccountingCsv.SeedRunStats` rewrites the first line to the
  current header when it differs, and rewrites nothing else (`AccountingCsv.cs:111-115`, ACCT-4, WT-P6). Old rows
  keep fewer fields, and every reader that goes by column name (`Field(row, name)`) reads a missing field as "".
  This is how `model`, `type`, `card_type` and `version` were added.
- **The result word.** `result` is the worker's `LANE-RESULT` word (done, review, needs-human, waiting, yielded,
  stopped, failed). When there is none, it is the reason the run ended, from `Scheduler.Core/Contracts/EndReason.cs`
  and `Scheduler.Core/Results/RunResults.cs`: no log, no result line, out of quota, model server down, time limit,
  stalled, killed, stopped from page, `crashed (exit n)`, ended early. `stop_reason` is the scheduler's own reason
  for stopping (`over the N min limit`, `stopped from the page`, `killed from the page`), or `finished without
  updating the card`, or "". This file's mix: crashed 311 (308 of them in one 0-minute burst on 2026-11-18),
  done 302, review 152, needs-human 46, stopped 7, yielded 6, no log 5, waiting 3, and 1 each of time limit,
  model server down, killed, failed and ended early.
- **The worker type is written in mixed case.** The `type` column holds `Opus` 434, `Sonnet` 236, `Spark` 86,
  `spark` 76 and `Haiku` 5 times. Grouping must ignore case, as `Text.Comparer` already does (`Scheduler.Core/Contracts/Text.cs:18`, ordinal ignore-case).
- **Typical length per category and model (TLH-1 to TLH-5).** `Scheduler.Core/Accounting/AreaDurations.cs`
  (`WindowRuns` 30, `WindowDays` 30, `MinimumRuns` 3, `Median`, `Percentile` by nearest rank), `AreaTypical`,
  `AreaRun` and `TimeBoxAdvice`. Its users are the Queue window and `Scheduler.Core/Views/GanttEstimate.cs`.
  **It reads the wrong column today.** `RunsFromRows` takes the model from the *last* field (`fields[^1]`, "the
  model last"). Since WT-P6 and 1030-A, the last field is `version`: it is empty on most rows, which are then
  dropped, and it holds a version such as `3.4.41` on bug cards, which then counts as the model. So TLH gives no
  figure or a wrong one. The new reader (EST-8) fixes this by reading every column by name.
- **Card statistics (ACCT-6).** `Scheduler.Core/Accounting/CardStats.cs` and `Trends.cs` leave out runs whose result
  is `no log`, `out of quota` or `model server down` (`NotTheCardsDoing`). `AccountingCsv.TrendRuns` re-reads the
  file only when its last write moves (770-B). Estimates reuse that cache pattern.
- **The card header.** `Scheduler.Core/Cards/CardFiles.cs` (`ParseText`) reads fields only from the header block,
  which is the text before the first `## `. The first `Type:` wins, a word read in any case and kept lowercase,
  and anything but feature or bug is untyped (lines 198-204). `Card.CardType` travels to `RunningWorker.CardType`
  (`Scheduler.Host/Tick/TickHost.cs:854`) and then to the row (`WorkerFinisher.cs:47`). **A worker adopted after a
  restart loses it.** `Scheduler.Host/Processes/RunningJson.cs:111` rebuilds `RunningWorker` without `CardType`
  or `CardVersion`, so the row of a run that spanned a restart has them empty. `kind` must not inherit that gap
  (EST-6).
- **The model of main (A14).** It holds one snapshot per repository. The snapshot's `Log` is a list of `MainCommit`
  (`Scheduler.Core/MainModel/MainLayout.cs:44`: sha, first parent, committer time, subject, and every changed path
  with old and new blob id). The log is capped at 10,000 commits or 30 days, whichever is more
  (`MainSnapshotBuilder.Cap`). It **starts empty at each start** and grows only as main advances (MM-7, MM-11).
  `MainSnapshot.Commits(since)` gives the commits after a given sha, in memory, or null when the log no longer
  reaches it. `Scheduler.Host/Git/InProcessHistory.cs` walks first-parent ranges in process, up to 10,000 commits,
  and answers null for git to take over (MM-16).
- **The record of moves.** Every move of a card is a commit on main: the scheduler's `jobs: review|done|wait|status`
  (CM-1 to CM-22), page answers (`jobs: answer ... - to done`), planner batches, and a hand-run move. Applied move
  requests are also kept in the scheduler's `moves\done\` folder, but page answers and planner batches are not
  move requests, so that folder is not a complete record. **Main is the only complete record.** One
  `git log --first-parent --name-status -- jobs/review` over this repository's 2,768 commits took 0.35 s. It gave
  149 completed stays in review (median 13 min, 90th percentile 307 min) and 1 open one.
- **`jobs/.logs/`.** This is the scheduler's dated log, `scheduler-YYYYMMDD.log`. It is written by
  `Scheduler.Host/Tick/FileSchedulerLog.cs` into `Path.Combine(config.Repo, config.JobsDir, ".logs")`
  (`RollingFolder`, line 134), which is the project's own checkout, not a worker's slot. The tray reads it
  (`Scheduler.Tray/SchedulerLogTail.cs`). It is ignored by this repository's `.gitignore` (line 7) and by the kit's
  template, `assets/templates/gitignore:13`. Workers may not write there (`Scheduler.Core/Locks/ProtectedJobs.cs`,
  "jobs/.logs ... the scheduler's own").
- **Routes.** `/status.json` is answered by `IPageData.StatusJson` (`Scheduler.Web/Server/PageRouter.cs:175`). In a
  pool, the routes in `PerProject` need `?project=` (`Scheduler.Web/Server/PageRouter.Pool.cs:15`), and the others
  are answered pool-wide.
- **The scheduler's version** is `AssemblyInformationalVersion`, read by `Scheduler.Core/CoreInfo.cs:23`.

What that means: almost everything the report needs is already recorded. What is missing is the card's kind, the
review stays of past cards (they are on main but in no file), and one place where a planner can read the figures.

## 1. Model

The report is history, counted and summarised: it predicts nothing. It has three tables, built from two sources:

| Table | Rows from | Grouped by |
|---|---|---|
| 1. Run time | `accounting.csv` | worker type and kind (category when a run has no kind) |
| 2. Time in review | the review stays file (EST-16), from main's commits | kind (category when none), and all cards |
| 3. Outcomes | `accounting.csv` | worker type |

Each figure is the project's own, or the pool's when the project has too few runs (EST-25). The report is written to
`jobs/.logs/estimates.json` in the project's checkout and served at `/estimates.json` (EST-28 to EST-33). It is
rebuilt only when a run finishes or a review stay changes, never on an idle tick.

## 2. Rules

### The card's kind

- **EST-1 The `Kind:` line.** `Kind: <word>` is an optional line in the card's header, set by the planner. It is
  read like `Type:` (CARD-6 to CARD-11): only from the header block, and the first `Kind:` line wins. The value is
  the first word after the colon, with spaces around it trimmed. It is matched against the project's kind list
  ignoring case, and kept lowercase. *Why: it is the same reading as `Type:`, so a planner who knows one knows the
  other, and the parser has one pattern for both.*
- **EST-2 The list comes from the project.** The known kinds are `scheduler.json`'s `cardKinds` (an array of words),
  and default to `["design", "implementation", "docs", "check"]` (the developer's four) when the key is missing or
  empty. The list is read where `scheduler.json` is read now (`SchedulerConfigLoader`), from main (MM-14). *Why: a
  project such as UltraCommand sizes design, build, test and docs cards. A fixed list would either refuse its words
  or have its planner sort them into words it does not use. A free word with no list would split one kind across
  spellings (`impl`, `implementation`, `code`), each group too thin to give a figure.*
- **EST-3 Unknown or missing is "".** A card with no `Kind:` line, an empty one, or a word not on the list has kind
  "". The scheduler logs `card <name>: Kind: <word> is not one of <list>` once per card and blob (not every tick),
  and a run with kind "" is grouped by category (EST-12). *Why: a word that is not on the list cannot be compared
  with anything. Falling back to the category is what the developer chose for old runs, so a misspelt card is
  counted like an old one instead of being lost.*
- **EST-4 Where it sits in the template.** `assets/templates/jobs/card-template.md` and this repository's
  `jobs/card-template.md` get, right after the `Type:` line:
  `Kind: design | implementation | docs | check (set by the planner; the project's list is scheduler.json's cardKinds)`.
  `card-template-feature.md` and `card-template-bug.md` get the same line. *Why: it sits beside `Type:`, the other
  "what sort of card" field. The comment names where a project's own list lives.*

### The `kind` column

- **EST-5 Its place.** `accounting.csv`'s header gains `,kind` at the end:
  `finished,card,lane,slot,result,minutes,log_bytes,stop_reason,model,type,card_type,version,kind`. Host adds it
  after `AccountingRow.CardColumns` (as `CardFields()` does: commas and line ends replaced, trimmed). *Why: last is
  the only place an old reader that counts fields from the front keeps working, and it is the place every earlier
  column (model, type, card_type and version) was added.*
- **EST-6 The kind is taken at dispatch and kept across a restart.** `RunningWorker` gains `Kind`, set from
  `Card.Kind` where `CardType` is set (`TickHost.cs:854`). `running.json` writes and reads it as `"kind"`, and the
  same change writes `"cardType"` and `"version"`, which are lost today (section 0). *Why: the row must name the
  kind the run was started under, even if the planner edits the card during the run or the scheduler restarts.*
- **EST-7 An old file gains the column, and old rows hold "".** At start-up the header line is rewritten in place
  when it differs from the current one (ACCT-4's existing step, unchanged), and no row is touched. A row from
  before the column has no `kind` field, and the reader takes it as "". *Why: this is how 850-D added `type`, it
  loses no row, and it needs no migration.*

### Reading the rows

- **EST-8 Rows are read by the header's names.** One Core reader, `AccountingRows.Parse(lines)`, takes the first
  line as the header and maps each later line's fields to its names. It handles the quoted `stop_reason`: a field
  that starts with `"` runs to the next field that ends with `"`, so a comma inside the quotes is kept. A line that
  does not start with a digit, or whose `finished` or `minutes` does not parse, is skipped and counted
  (`skipped` in the report, EST-30). Every estimates table reads through it, and so do `AreaDurations.RunsFromRows`
  and `Trends.RunsFromRows`, which fixes the TLH bug of section 0. *Why: three readers have counted fields from
  the end or the front, and one of them already broke when columns were added. Reading by name is the only way
  the next column cannot break them.*

### Which runs count

- **EST-9 The window: the newest 100 runs within 60 days, per group.** For each group of each table, take the
  group's counted runs that finished within the last 60 days, then the newest 100 of those. *Why: TLH-1's 30 runs
  in 30 days is enough for a median but not for a 90th percentile, because by nearest rank the 90th percentile of
  fewer than 10 runs is simply the slowest run. With 100 runs it rests on the slowest 10. 60 days keeps a group to
  roughly the models and kit it runs on today. At this project's pace (838 runs in 7 days), a busy group fills 100
  runs within days, and a quiet one still gets two months.*
- **EST-10 The fewest runs for a figure.** A median needs at least 3 runs (TLH-2's `MinimumRuns`). A 90th
  percentile needs at least 10. A share (Table 3) needs at least 10 runs. A group with fewer runs than a figure
  needs gives `null` for that figure, and the fallback of EST-25 applies to it. *Why: 3 matches what the Queue
  window already trusts for a typical length. Under 10, the 90th percentile is the maximum, and a share moves by
  10 points or more with each run.*
- **EST-11 Median and percentile, as TLH-3.** The median is the middle of the sorted minutes, and for an even count
  the mean of the two middle values, rounded up on a half. The 90th percentile is by nearest rank: the
  ceil(0.9 n)-th of the sorted minutes. Both reuse `AreaDurations.Median` and `Percentile`. *Why: one definition in
  the program, so the report and the Queue window never disagree on the same runs.*

### Table 1: run time by worker type and kind

- **EST-12 The groups.** A counted run belongs to the group (worker type, kind). When its kind is "", it belongs to
  (worker type, `category:<L>`) instead, where `<L>` is the row's `lane`. The worker type is the `type` column,
  compared ignoring case and shown as written in the group's newest row. A row with no `type` (from before WT-P6)
  uses its `model` column, and a row with neither is `other`. *Why: these are the developer's words ("fall back to
  category and worker type"). Old rows from before worker types still say which model ran them.*
- **EST-13 Which runs count.** A run counts when its result is `done` or `review`, with any `stop_reason` (a card
  that finished in its grace time after the limit, or finished without updating its card, did finish), and its
  minutes are 0 or more. Every other result is left out of Table 1, and Table 3 counts it instead. *Why: the
  figure answers "how long does a session that finishes the card take", which is what a `Time box:` bounds. A
  stopped, parked or failed run did not finish the card, so its length says nothing about that. This is TLH's rule
  too (`AreaDurations.FinishedWords`).*
- **EST-14 What each group gives.** `runs` (the counted runs in the window), `median` and `p90` in whole minutes,
  and the oldest and newest `finished` of the runs used. Groups are sorted by worker type, then kind, then
  category groups last.

### Table 2: time in review

- **EST-15 What a stay is.** A review stay starts at the committer time of the commit on main's first-parent line
  that adds `<jobsDir>/review/<card>.md`. It ends at the committer time of the first later commit that deletes that
  path. Where the card went is the folder the same commit adds it to: done, queue or active, else `gone`. Each
  stay counts on its own: a card sent back and reviewed again has two. A stay with no end yet is **open**. Stays are
  matched by file name (`<n>-<L>-<slug>.md`), and a rename inside review/ ends one stay and starts another. *Why:
  the move into review is the moment the developer's hands are needed, and the move out is the moment he acted.
  Both are commits whoever made them (the scheduler, the page's answer, a planner batch, a hand move), so this is
  the one record that has them all.*
- **EST-16 The stays are kept in a file as they happen.** `review-stays.csv` sits in the project's log folder,
  beside `accounting.csv`. Its header is `entered,left,card,to,entered_commit,left_commit`, with times as
  `yyyy-MM-ddTHH:mm:ss` local, as ACCT-1 writes them. A stay is written when it closes. Open stays are kept in
  memory and are rebuilt at start-up from the cards now in review/ and the newest entering commit for each. The
  file's last line covered is recorded in a sibling `review-stays.head` (the last main commit read). *Why: the
  model's log starts empty at every start and is capped (section 0), so it cannot be the store. A file that grows
  one line per stay costs nothing to read.*
- **EST-17 New stays come from the model, with no git process.** When the model's snapshot commit differs from
  `review-stays.head`, the Host takes `MainSnapshot.Commits(head)` (in memory) and scans each commit's `Changes`
  for paths under `<jobsDir>/review/`, `done/`, `queue/` and `active/`. It closes and opens stays, appends the
  closed ones, and moves the head. When the snapshot commit equals the head, nothing is read and nothing runs.
  *Why: MM-15 says an idle tick starts no git process. The model already holds every new commit's changed paths
  for the audits, so this reads memory the advance already paid for.*
- **EST-18 A gap is walked once, in process.** When `Commits(head)` is null (the scheduler was stopped while main
  moved, the head is older than the log, or after a force push), the Host walks the first-parent range from the
  head to the snapshot commit with `InProcessHistory`, along `<jobsDir>/` only. When that answers null (a refused
  repository, MM-2, or more than 10,000 commits), it runs one `git log --first-parent --format=%H %ct
  --name-status --no-renames <head>..<commit> -- <jobsDir>/review`. This happens at most once per gap: at
  start-up, or on the first advance after one. A head not reachable from the commit (after a force push) is treated
  as no head (EST-19). *Why: the gap is the only time history must be read. Doing it in process keeps even that
  free of a git process in the common case, and the git fallback is the same one-process answer MM-16 already
  allows.*
- **EST-19 The first start backfills.** When `review-stays.csv` is missing, the first walk (EST-18) runs from the
  root of main's first-parent history: once per project, in the background after the model's phase 2 (MM-5), so
  the page and the first tick never wait for it. On this repository it read 2,768 commits in 0.35 s with git and
  gave 149 stays. *Why: the stays of past cards cost one walk, once. Without it a new install would show no Table
  2 for weeks, which is the figure the developer called the one "that actually sets the finish date".*
- **EST-20 What Table 2 gives.** For each kind of the card (the `kind` of the card's newest accounting row, else
  `category:<L>`) and for `all`: `stays` (closed stays that ended within 60 days, the newest 100, as EST-9),
  `median` and `p90` in minutes (EST-10, EST-11), and `toDone` and `toQueue` (how many of those ended in done/ and
  queue/). Alongside, `open` gives the count of open stays and the age in minutes of the oldest. Open stays are
  never in the percentiles. A stay with `left` before `entered` (a clock change) is skipped. *Why: by kind because
  a design waits for a different kind of attention than a docs card. `all` always has the most stays, so a planner
  always has some figure. Open stays are shown and not counted, because their end is unknown.*

### Table 3: outcomes by worker type

- **EST-21 The outcome of a run.** Each row of `accounting.csv` gets exactly one outcome, by its `result` first and
  then its `stop_reason`:

  | Outcome | Read from the row |
  |---|---|
  | `done` | result `done` |
  | `review` | result `review` |
  | `question` (parked with a question) | result `needs-human` |
  | `planner` (sent to the planner) | result `waiting` |
  | `yielded` | result `yielded` |
  | `timeLimit` (stopped at the time limit) | result `time limit`; or result `stopped` with stop_reason starting `over the` |
  | `stopped` (stopped by the developer) | result `stopped` (other stop_reason), `stopped from page`, `killed` |
  | `outOfQuota` | result `out of quota` |
  | `didNotStart` | result `crashed (exit n)`, `no result line` or `ended early` with minutes 0 |
  | `failed` (failed or no result) | result `failed`, `no result line`, `stalled`, `ended early`, `crashed (exit n)`, or any other word |
  | `notCounted` | result `no log`, `model server down` |

  Words are compared ignoring case, and `crashed` by its prefix. *Why: these are the developer's eight outcomes,
  plus `stopped` for the developer's own stops (not the worker's doing) and `didNotStart`. The 0-minute crashes are
  the scheduler or CLI failing to launch, not the worker type failing: here 308 of the 311 crashes were one
  0-minute burst on 2026-11-18, which would otherwise make Opus look like it fails half its runs.*
- **EST-22 Shares.** For each worker type (as EST-12), using runs in the EST-9 window: `runs` is all rows of the
  worker type except `notCounted`, `didNotStart` and `outOfQuota`. Each of `done`, `review`, `question`,
  `planner`, `yielded`, `timeLimit`, `stopped` and `failed` is given as a count and as a share of `runs`, a whole
  percent. `outOfQuota`, `didNotStart` and `notCounted` are given as counts only. *Why: quota, the model server
  and a launch failure are the account's or the machine's doing, which is ACCT-6's reason for leaving `no log`,
  `out of quota` and `model server down` out of the card statistics. They are still shown, so nothing is hidden.*
- **EST-23 A card that needed more than one run.** For each worker type: `cards` is the number of distinct cards
  (by id, `<n>-<L>`, per project) whose run ending `done` or `review` falls in the window and was made by that
  worker type. `moreThanOneRun` is how many of those cards had at least one earlier counted run (any outcome but
  `notCounted`, `didNotStart` and `outOfQuota`, any worker type, anywhere in the file) since their last earlier
  `done` or `review`. `runsPerCard` is the mean of the runs each card took to finish, to one decimal. *Why: this is
  "how often does a card need a second go", charged to the worker type that finished it. Earlier runs outside the
  window still count, so a card is not called a one-run card just because its first run was 61 days ago. Starting
  after the last finish keeps a card sent back from review and finished again as a new attempt.*
- **EST-24 The fewest runs.** Shares need 10 runs (EST-10); below that, the counts are given and the shares are
  `null`.

### Project and pool

- **EST-25 Project first, then the pool, per figure.** Each figure (a median, a 90th percentile, a set of shares) is
  worked out from the project's own rows. When the project's group has too few runs for that figure (EST-10), the
  same group's figure from the pool's rows is used if the pool has enough. If neither has enough, the figure is
  `null`. A figure's source is `project`, `pool` or `none`, with the number of runs it rests on. The median and the
  90th percentile of one group may have different sources. *Why: this is the developer's third answer. Choosing
  per figure gives a planner the project's own median as soon as it has 3 runs, while still borrowing the pool's
  90th percentile until it has 10.*
- **EST-26 Category groups never fall back.** A group keyed `category:<L>` (EST-12, EST-20) takes no pool figure,
  because category letters mean different things in different projects (A is Core here and something else
  elsewhere). Its source is `project` or `none`. Kind groups, worker types and `all` do fall back. *Why: borrowing
  another project's "A" would give a confident and meaningless figure.*
- **EST-27 Where the pool's rows come from.** The pool's rows are every member project's `accounting.csv` and
  `review-stays.csv`, each read from that project's log folder (its `SchedulerConfig.LogDir`). Each project's
  folder is read once even when two members share it (compared by full path, ignoring case), and the files are
  re-read only when their last write moves (the `TrendRuns` cache, 770-B). Card ids are kept per project, so two
  projects' card 12-A are two cards. A scheduler run for one project alone has no pool: its project figures are
  its only ones, and `pool` never appears as a source. *Why: the pool is what the scheduler process already
  holds (`PoolComposition` knows each member's config), so nothing new needs to be found. Per-project files stay
  as they are, and MP-P5's "All projects" statistics already sum them the same way.*

### The file and the route

- **EST-28 The file.** `<config.Repo>/<config.JobsDir>/.logs/estimates.json` is the folder `FileSchedulerLog`
  already writes to (`RollingFolder`), in the project's own checkout. The folder is created if it is missing. It is
  not in git: this repository's `.gitignore` and the kit's `assets/templates/gitignore` already ignore
  `jobs/.logs/`, and the step 4 card adds a test that the template still does. *Why: it is the developer's choice,
  and it is the one folder in the project that a planner session in the checkout can read and that is
  already the scheduler's own (ProtectedJobs).*
- **EST-29 When it is written.** The file is rebuilt and written: once at start-up, after the backfill or catch-up
  of EST-18 and EST-19; after each run finishes (after `AccountingCsv.Add`); and when EST-17 or EST-18 closes or
  opens a review stay. It is never written on a tick where none of these happened. A pool rebuilds every member's
  file when any member's rows change, because each one may borrow from the pool. *Why: those are the only events
  that change a figure. A rebuild reads only the two cached files per project, and at this repository's size
  (838 rows) that takes milliseconds.*
- **EST-30 Atomic.** The file is written to `estimates.json.tmp` in the same folder, then moved over
  `estimates.json` with `File.Move(tmp, path, overwrite: true)`, which is one rename on the same volume. A planner
  that reads it at any moment gets the whole old file or the whole new one, never half. If the move fails (the file
  is held open without delete sharing), the error is logged once (as 1118-B logs repeats), the temporary file is
  left in place, and the next trigger tries again. *Why: a planner reads it whenever it likes, and a reader must
  never parse a half-written file.*
- **EST-31 The route.** `GET /estimates.json?project=<name>` answers that project's report, the same document as
  its file. `GET /estimates.json` with no `project` answers the pool's report: the same shape built from the pool's
  rows alone, with every source `pool`. In a scheduler run for one project, it answers that project's report.
  `?project=` naming no project gives 400, as the other per-project routes do. The answer is the last built report
  held in memory, so the route reads no file and starts no process. *Why: the developer's choice. The pool report
  is what a planner of a new project, with no runs of its own, can read.*
- **EST-32 Cost.** No estimates step starts a git process on a tick, except the one walk of EST-18 after a gap,
  and that one falls back to git only when the in-process walk refuses. Nothing is read when nothing changed:
  the accounting and stays files are cached by last write, and the model is read only when its commit moved.
  *Why: MM-15, at 50 projects x 5,000 cards.*
- **EST-33 The shape** is section 3. The units are whole minutes for times and whole percents for shares. Times are
  ISO 8601 with the machine's offset. Field names are fixed (camelCase, English) and new fields are only ever
  added. `schema` is 1, and becomes 2 only if a field changes meaning.

### What the planner is told

- **EST-34 The planner reads the report before it sizes a card.** The kit's planner brief, `docs/guide/planner.md`
  and this project's `jobs/project.md` carry the paragraph of section 4. *Why: the report is useless if nobody
  reads it at the moment a time box is chosen.*

## 3. The JSON

The project's file. Example: a project `UltraCommand` in a pool, 2026-11-24 21:30:

```json
{
  "schema": 1,
  "project": "UltraCommand",
  "written": "2026-11-24T21:30:05-08:00",
  "scheduler": "3.4.42",
  "units": { "time": "minutes", "share": "percent" },
  "window": { "days": 60, "newestRuns": 100, "minRunsMedian": 3, "minRunsP90": 10, "minRunsShare": 10 },
  "rows": { "project": 412, "pool": 1650, "skipped": 0 },
  "help": "Figures are history, not promises. Each figure says where it comes from (project = this project's own runs, pool = every project in the pool, none = too few runs anywhere) and how many runs it rests on. A group named category:<L> holds runs of cards that had no Kind: line.",
  "runTime": [
    { "worker": "Opus", "kind": "design",
      "median": { "minutes": 38, "source": "project", "runs": 14 },
      "p90": { "minutes": 71, "source": "project", "runs": 14 },
      "from": "2026-10-02T09:14:00-07:00", "to": "2026-11-24T20:33:09-08:00" },
    { "worker": "Sonnet", "kind": "implementation",
      "median": { "minutes": 22, "source": "project", "runs": 6 },
      "p90": { "minutes": 55, "source": "pool", "runs": 48 },
      "from": "2026-11-19T10:02:11-08:00", "to": "2026-11-24T18:28:47-08:00" },
    { "worker": "Spark", "kind": "check",
      "median": { "minutes": null, "source": "none", "runs": 1 },
      "p90": { "minutes": null, "source": "none", "runs": 1 },
      "from": "2026-11-23T14:40:00-08:00", "to": "2026-11-23T14:40:00-08:00" },
    { "worker": "Sonnet", "category": "C",
      "median": { "minutes": 6, "source": "project", "runs": 9 },
      "p90": { "minutes": null, "source": "none", "runs": 9 },
      "from": "2026-11-18T11:00:00-08:00", "to": "2026-11-24T20:28:01-08:00" }
  ],
  "review": [
    { "kind": "all",
      "median": { "minutes": 13, "source": "project", "runs": 100 },
      "p90": { "minutes": 307, "source": "project", "runs": 100 },
      "toDone": 91, "toQueue": 9 },
    { "kind": "design",
      "median": { "minutes": 95, "source": "pool", "runs": 12 },
      "p90": { "minutes": 840, "source": "pool", "runs": 12 },
      "toDone": 2, "toQueue": 0 }
  ],
  "reviewOpen": { "count": 1, "oldestMinutes": 42 },
  "outcomes": [
    { "worker": "Opus", "source": "project", "runs": 100,
      "done": { "count": 52, "share": 52 }, "review": { "count": 30, "share": 30 },
      "question": { "count": 9, "share": 9 }, "planner": { "count": 1, "share": 1 },
      "yielded": { "count": 3, "share": 3 }, "timeLimit": { "count": 2, "share": 2 },
      "stopped": { "count": 1, "share": 1 }, "failed": { "count": 2, "share": 2 },
      "outOfQuota": 0, "didNotStart": 4, "notCounted": 1,
      "cards": 80, "moreThanOneRun": 7, "runsPerCard": 1.1 },
    { "worker": "Spark", "source": "pool", "runs": 23,
      "done": { "count": 15, "share": 65 }, "review": { "count": 2, "share": 9 },
      "question": { "count": 1, "share": 4 }, "planner": { "count": 0, "share": 0 },
      "yielded": { "count": 0, "share": 0 }, "timeLimit": { "count": 3, "share": 13 },
      "stopped": { "count": 0, "share": 0 }, "failed": { "count": 2, "share": 9 },
      "outOfQuota": 0, "didNotStart": 0, "notCounted": 0,
      "cards": 17, "moreThanOneRun": 4, "runsPerCard": 1.3 }
  ]
}
```

Notes for reading it cold:

- A `runTime` entry has either `kind` (a card with a `Kind:` line) or `category` (a card with none), never both.
- `runs` beside a figure is the number of runs (or stays) the figure rests on, from the source named. When the
  source is `none`, it is the project's own count, which was too few.
- In `outcomes`, `source` and `runs` apply to the whole entry, because shares are only worth reading together. The
  pool is used when the project has fewer than 10 runs of that worker type.
- `from` and `to` are the oldest and newest finish times of the project's runs in the group, so a planner can see
  how fresh the group is.
- The pool's report (`/estimates.json` with no project) has the same shape, with `"project": null` and every
  source `pool`.

## 4. What a planner is told (the paragraph for step 5)

For `assets/templates/docs/planner-brief.md`, `docs/guide/planner.md` (in the card-writing section, beside the
`Worker:` and `Time box:` bullets) and this project's `jobs/project.md` ("How the planner picks a worker"):

> **Size cards from the estimates.** Before you set a card's `Time box:` or `Worker:`, read
> `jobs/.logs/estimates.json` in the project folder (or `/estimates.json?project=<name>` on the scheduler's page).
> It is the scheduler's record of past runs, not a forecast. Find the `runTime` entry for the worker type you are
> considering and the card's kind. If it has no figure, use the entry for the card's category, then the pool's.
> Set `Time box:` to the 90th percentile rounded up to the next 15 minutes. If there is only a median, use twice the
> median. If there is no figure at all, use 45 minutes for a small card and 90 for a large one, and say so in
> the card. Never set a time box below the median. Pick the worker type with `outcomes` in mind: a type with a high
> `failed` or `timeLimit` share, or many cards needing more than one run, costs more than its run time says. Use
> `review` for the date a card is really finished: it waits for the developer for about the median, and sometimes
> the 90th percentile. Set `Kind:` on every card (one of the words in `scheduler.json`'s `cardKinds`, by default
> design, implementation, docs or check), because a card without one only adds to its category's figure. When you
> size a card from the report, say in the plan's step which figure you used.

*Why these numbers:* the 90th percentile means about 1 run in 10 overruns, and the scheduler's grace time
(`stopGraceMinutes`, TIME-6 and TIME-7) absorbs a small overrun. Rounding to 15 minutes keeps time boxes readable. Twice the median is a
conservative stand-in, because run times are skewed to the long end (here, review stays: median 13, p90 307).

## 5. Failure cases

| Case | What happens |
|---|---|
| No `accounting.csv` (new project, or log folder deleted) | Every Table 1 and Table 3 figure is from the pool or `none`; `rows.project` is 0. The file is still written, so a planner sees "no history" rather than no file. |
| An unreadable row (bad time, bad minutes, a broken quote) | Skipped and counted in `rows.skipped` (EST-8). The rest count. |
| The file is held open or locked when read | That read keeps the last cached rows (as `TrendRuns`), and the error is logged once (1118-B). |
| A project with no runs and no pool (a scheduler run for one project) | Every figure is `none`. The paragraph of section 4 says what a planner does then. |
| A clock change | Rows are kept in file order, and the window is cut by `finished`. A row "from the future" is kept, because it is within the window. A run's `minutes` below 0 is skipped (EST-13). A review stay that ends before it starts is skipped (EST-20). Commit times come from main, so a clock that moves back makes no stay negative unless commits were made across the move. |
| A card run in two projects' pools | Each scheduler reads its pool's member folders. The same project folder in two pools is read by each, but once per pool (EST-27, by path). Card ids are per project, so cards never merge across projects. |
| The `.logs` folder is missing (a fresh clone, or deleted) | It is created before the write (EST-28). If it cannot be created, the error is logged once and the route still answers from memory. |
| `review-stays.csv` deleted | The next start backfills again (EST-19). |
| `review-stays.head` not on main's line (a force push) | Treated as no head: a full backfill rewrites the file (EST-18, EST-19). |
| A repository the object reader refuses (MM-2) | The walk of EST-18 uses its one git process. Idle ticks are still free, because EST-17 reads the model, which the CLI reader also fills. |
| `cardKinds` invalid (not an array of words) | The default list is used, and the error is logged once with the other config warnings. |
| A worker adopted after a restart | `kind` comes from `running.json` (EST-6). A file from before EST-6 reads as "", which is the fallback. |

## 6. What changes in the code

### Core (category A)

- `Contracts/Card.cs`, `Cards/CardFiles.cs`: `Kind`, read by EST-1 against a kind list passed in (EST-2, EST-3).
- `Contracts/SchedulerConfig.cs`, `Config/SchedulerConfigLoader.cs`: `CardKinds` with the default list.
- `Contracts/RunningWorker.cs`: `Kind`.
- `Accounting/AccountingRow.cs`: `Kind`, and `CardColumns` becomes `,card_type,version,kind` (EST-5).
- New `Accounting/AccountingRows.cs`: the reader by name (EST-8). `AreaDurations.RunsFromRows` and
  `Trends.RunsFromRows` move onto it.
- New `Estimates/` folder: `RunOutcome.cs` (EST-21), `RunTimeTable.cs` (EST-12 to EST-14), `OutcomeTable.cs`
  (EST-22 to EST-24), `ReviewStays.cs` (EST-15 to stays from commit changes, and EST-20 for the table),
  `EstimateFigures.cs` (EST-9 to EST-11, EST-25, EST-26), `EstimatesReport.cs` (the document of section 3, and
  `ToJson`).

### Host (category B)

- `State/AccountingCsv.cs`: write `kind`. The header line is unchanged in code: it follows `AccountingRow`.
- `Processes/RunningJson.cs`, and the writer beside it: `kind`, `cardType`, `version` (EST-6).
- `Tick/TickHost.cs:854`: `Kind = card.Kind`.
- New `State/ReviewStaysFile.cs`: the file, the head, EST-17's scan of the model's new commits, and EST-18 and
  EST-19's walk (`InProcessHistory`, else one `git log`).
- New `State/EstimatesFile.cs`: builds the report from the project's and the pool's cached rows, holds the last
  one in memory, and writes it atomically (EST-28 to EST-30). Triggered from `WorkerFinisher.Finish` and from
  `ReviewStaysFile` (EST-29).
- `Pool/PoolComposition.cs`: hands the members' log folders to the pool's rows (EST-27).
- `IPageData` and `HostPageData` (and the pool's data): `EstimatesJson(project?)`.

### Web (category C)

- `Server/PageRouter.cs` and `Server/PageRouter.Pool.cs`: `/estimates.json`, with `?project=` optional (EST-31).

### Kit and docs (category D, and the kit's owner)

- `assets/templates/jobs/card-template.md` (and its feature and bug variants), this repository's
  `jobs/card-template.md`, `card-template-feature.md` and `card-template-bug.md`: the `Kind:` line (EST-4).
- `assets/templates/jobs/scheduler.json` and `docs/guide/scheduler-cli.md` or the settings
  page of the guide: `cardKinds`.
- `assets/templates/docs/planner-brief.md`, `docs/guide/planner.md`, `jobs/project.md`: the paragraph (EST-34).
- `docs/design/tdd/scheduler/spec.md`: EST-1 to EST-34 move in as built, in a new section beside TLH. ACCT-1's
  header text gains `kind`, and TLH-1 notes that rows are read by name (EST-8).

## 7. Test plan

All in `test/unit`, named `EST_<n>_<what>`. Core tests use fixed rows and fixed times, with no clock and no files.

| Rule | Tests |
|---|---|
| EST-1 | `Kind: Docs` reads `docs`; `Kind:   check  ` reads `check`; the first of two `Kind:` lines wins; a `Kind:` below `## Task` is ignored. |
| EST-2 | No `cardKinds`: the four defaults; `cardKinds: ["design","build","test","docs"]`: `build` is known and `implementation` is not. |
| EST-3 | `Kind: impl` gives "" and one log line, not one per tick; no line gives "". |
| EST-5 | `AccountingCsv.Add` writes `kind` last; the header ends `,kind`. |
| EST-6 | `running.json` round-trips `kind`, `cardType` and `version`; an entry without them reads "". |
| EST-7 | A file with the 1030-A header gains `,kind` on the first line, every row is kept byte for byte, and old rows read `kind` "". |
| EST-8 | A stop reason with a comma inside quotes keeps the columns aligned; a row with `version` `3.4.41` gives model `claude-sonnet-5-5` (the TLH bug); a bad time or bad minutes is skipped and counted; an 8-field row from before `model` reads `model` "". |
| EST-9 | 120 runs in a group give the newest 100; a run 61 days old is out. |
| EST-10, EST-11 | 2 runs give no median; 3 give one; 9 give no p90; 10 give p90 = the 9th; an even count rounds up on a half (as TLH-3). |
| EST-12 | A run with kind "" lands in `category:<L>`; `Spark` and `spark` are one group shown as the newest row's spelling; no type uses the model. |
| EST-13 | `review` with `over the 20 min limit` counts; `needs-human` does not; -1 minutes is skipped. |
| EST-15 | add review/X then delete review/X and add done/X gives one stay to done; review, queue, review, done gives two stays; a rename in review gives two stays. |
| EST-16 | Closed stays append; open stays rebuild from the cards in review/ at start-up. |
| EST-17 | A tick whose snapshot commit equals the head reads nothing and starts no process (a fake runner counts zero); a new commit moving a card out of review appends one line. |
| EST-18 | `Commits(head)` null makes one in-process walk; an in-process null makes exactly one `git log`; a second advance does neither. |
| EST-19 | No file: a backfill over a fixture repository gives the expected stays; it runs once. |
| EST-20 | Open stays are counted in `reviewOpen` and not in the percentiles; a stay that ends before it starts is skipped; `toDone` and `toQueue` add up. |
| EST-21 | One row per result word in the table maps to its outcome; `crashed (exit 1)` with 0 minutes is `didNotStart`, with 5 minutes `failed`; `stopped` with `over the 10 min limit` is `timeLimit`. |
| EST-22 | Shares leave out `outOfQuota`, `didNotStart` and `notCounted`, and add up to 100 within rounding. |
| EST-23 | A card run three times (failed, needs-human, done) is one card, `moreThanOneRun` 1, `runsPerCard` 3.0; a card done, sent back and done again counts two finishes of one run each. |
| EST-25 | A project with 5 runs gives its own median and the pool's p90, each with its source and count; with 1 run and a pool of 20, both come from the pool; with nothing anywhere, `none`. |
| EST-26 | A `category:A` group with 2 project runs and 50 pool runs gives `none`. |
| EST-27 | Two members with the same log folder are read once; card 12-A of two projects is two cards. |
| EST-29 | A run finishing writes the file; an idle tick does not (the file time is unchanged). |
| EST-30 | The write goes through `.tmp` and a move; a held target logs once and the next trigger succeeds. |
| EST-31 | `/estimates.json?project=X` equals X's file; no project gives the pool's; an unknown project gives 400. |
| EST-28 | The kit's `assets/templates/gitignore` holds `jobs/.logs/` (a kit test in `bin/test-projecttoolkit.ps1`, which already checks it, line 126). |
| EST-33 | The section 3 example parses into the report type and back unchanged (a golden file). |

## 8. Cards (proposed, in order)

The plan's steps 2 to 5, as carded tasks. Worker types follow `jobs/project.md`: Spark where a rule fixes the work,
Sonnet where judgement is needed, and Opus only for the model of main or git.

| # | Title | Cat. | Scope (1 to 3 files or folders) | Worker | Time box | Depends on | Rules |
|---|---|---|---|---|---|---|---|
| 2a | `Kind:` in the card header and `cardKinds` | A | `Scheduler.Core/Cards/CardFiles.cs`, `Scheduler.Core/Contracts/Card.cs`, `Scheduler.Core/Contracts/SchedulerConfig.cs` (and its loader in `Scheduler.Core/Config/`) | Spark | 45 | 1340-D | EST-1 to EST-3 |
| 2b | The `kind` column, kept across a restart | B | `Scheduler.Host/State/AccountingCsv.cs`, `Scheduler.Host/Processes/RunningJson.cs`, `Scheduler.Host/Tick/TickHost.cs` (Locks: `Scheduler.Core/Accounting/AccountingRow.cs`, `Scheduler.Core/Contracts/RunningWorker.cs`) | Sonnet | 60 | 2a | EST-5 to EST-7 |
| 2c | Card templates and guide: the `Kind:` line | D | `assets/templates/jobs/`, `jobs/card-template.md`, `docs/guide/` | Spark | 30 | 1340-D | EST-4 |
| 3a | Accounting rows by column name (fixes TLH's model column) | A | `Scheduler.Core/Accounting/` | Sonnet | 60 | 1340-D | EST-8, EST-9 to EST-11 |
| 3b | Run time and outcomes tables | A | `Scheduler.Core/Estimates/` | Sonnet | 90 | 3a | EST-12 to EST-14, EST-21 to EST-26 |
| 3c | Review stays: from the model's commits, the file, the backfill | B | `Scheduler.Host/State/ReviewStaysFile.cs`, `Scheduler.Core/Estimates/ReviewStays.cs` | Opus | 90 | 3a | EST-15 to EST-20 |
| 4 | Write estimates.json and serve /estimates.json | B | `Scheduler.Host/State/EstimatesFile.cs`, `Scheduler.Host/Pool/PoolComposition.cs`, `Scheduler.Web/Server/` (Locks) | Sonnet | 90 | 2b, 3b, 3c | EST-27 to EST-33 |
| 5 | Planner brief, planner guide and project.md: read the estimates, set the kind | D | `assets/templates/docs/planner-brief.md`, `docs/guide/planner.md`, `jobs/project.md` | Spark | 30 | 4 | EST-34 |
| 5b | A planner session sizes a card from the report | (developer) | none | developer | 30 | 5 | the plan's step 5 check |
| 5c | Spec: EST rules in, ACCT-1 and TLH-1 amended | D | `docs/design/tdd/scheduler/spec.md` | Spark | 30 | 4 | all |

Changes from the plan, and why:

- **3a is new and comes first in step 3.** Every table reads accounting rows, and today's reader is broken for TLH
  (section 0). One reader by name, built and tested once, keeps the three tables from repeating the bug. 3b and 3c
  build on it.
- **Step 3's "time in review" task is category B, and Opus.** The stays come from the model of main and, once, from
  a walk of history (EST-17 to EST-19). The plan's rule for workers reserves Opus for the model of main and git.
  The pure part (stays from a list of commit changes, and the table) is in Core, in the same card, so it is tested
  without a repository.
- **Step 3's run time and outcomes are one card (3b)**, not two. They share the window, the fallback and the
  grouping (EST-9 to EST-12, EST-25, EST-26), and splitting them would make the second card rework the first.
- **5c is added** so the spec says what was built, as 1069-D did for A14.
- Steps 2 and 3 can run side by side: 3a depends only on this design, and 2b and 3a touch different folders.

## Questions for the developer

- **Q1.** The Queue window's time-limit warnings and suggestions (TLH) read the model from the wrong column of
  `accounting.csv` since the worker-type and card-type columns were added, so they are mostly silent and sometimes
  wrong. Card 3a's reader fixes this as a side effect, and the Queue window starts warning again. Choices:
  **fix it with card 3a (recommended)** | a separate bug card first | leave TLH as it is and fix only the new
  tables.
