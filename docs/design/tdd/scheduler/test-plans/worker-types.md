# Test plan: worker types with caps

Rules: WT-C1 to WT-C12, WT-1 to WT-14, WT-W1 to WT-W6, WT-P1 to WT-P6 (`spec.md`, "Worker types with caps").
Source: `worker-types.md` (design, card 530-D), section 5, with the developer's answers of 2026-11-19 written in.
These tests replace those that checked DISP-15, DISP-17 to DISP-19, WRK-1 to WRK-3, WRK-7, TIME-10, TIME-12,
TIME-15's state, ADOPT-2 and ACCT-1 on slots: each is rewritten to the WT rule that replaces it, never
deleted without one in its place. Tests are written first (cards 2 to 7 of the design's section 6).

Shared setup unless a test says otherwise: `maxWorkers` 4; types in this order: `worker` (non-premium,
max 2, default agent), `Sonnet` (premium, max 1, agent `claude`), `Opus` (premium, max 1, agent `claude`);
nothing running, servers up, no agent out of quota, state `running`, one project, cards with no `Worker:` line.

## Config and migration (Core)

### WorkerTypes_PoolSection_ParsedInOrder_DefaultsApplied
- Checks: WT-C1, WT-C3, WT-C5 · Project: Core
- Setup: workers.json with `pool` `{maxWorkers: 4, types: {spark, sonnet, opus}}`; a second text with a type lacking `max`, `premium` and `areas`, and with `maxWorkers` missing and scheduler.json `slots` 3.
- Expected: types in file order; every field read; missing `max` -> `maxWorkers`; `premium` false; no categories list (every category fits; an old `areas` key is ignored); missing, 0 or non-numeric `maxWorkers` -> 3. `-Slots 5` on the command line gives 5 whatever the file says.

### WorkerTypes_NamePremiumOrDigits_Refused
- Checks: WT-C2 · Project: Core
- Setup: types named `premium`, `Premium`, `12`, `Spark`, `SPARK`.
- Expected: the first three are logged and skipped; `Spark` stays; names compare case-insensitively, so the second `SPARK` is also refused as a duplicate.

### WorkerTypes_InvalidJson_PreviousKept
- Checks: WT-C6 · Project: Core
- Setup: a valid pool applied; then broken JSON; then a `pool` with no valid type; then a sum of `max` above `maxWorkers`.
- Expected: broken JSON and no-valid-type are logged and the previous settings stay; the sum above `maxWorkers` is accepted as written.

### WorkerTypes_Migration_SameSettingsMerge_CapIsEnabledCount
- Checks: WT-C10 · Project: Core
- Setup: no `pool`; `slots` 4; slots 1 and 2 with identical settings (categories `ab` and `AB` count as equal), slot 3 differing; workers-state.json: slot 2 disabled.
- Expected: two types; the merged one has `max` 1 (enabled count) and keeps slot numbers 1 and 2; the other has max 1 and slot 3; `maxWorkers` 4. With all of a type's slots disabled its `max` is 0. Nothing is written to any file.

### WorkerTypes_Migration_ThisProjectsWorkersJson_GivesWorkerSonnetOpus
- Checks: WT-C10, WT-C12 · Project: Core
- Setup: this project's workers.json as it stands (4 slots; 1 and 2 default agent on spark; 3 Sonnet premium; 4 Opus premium), all enabled.
- Expected: `maxWorkers` 4; `worker` (max 2), `Sonnet` (premium, max 1), `Opus` (premium, max 1), in that order. `Worker: Opus` picks the Opus type by name.

### WorkerTypes_Migration_NamesFromModelShort_ClashesNumbered
- Checks: WT-C10 · Project: Core
- Setup: slots with: an explicit `name`; no name but a model; no name and no model; two types that would both be called `Sonnet`; one named `premium`.
- Expected: names are the first member's `name`, else `Model-Short` of the effective model, else `worker`; the clash becomes `Sonnet-2`; the refused name becomes `worker-premium`.

### WorkerTypes_PoolKeyPresent_SlotsAndWorkersIgnored
- Checks: WT-C10 · Project: Core
- Setup: workers.json with `pool`, `slots` 9 and a `workers` entry.
- Expected: the pool is used; `slots` and `workers` are ignored; logged once, not every tick.

### WorkerTypes_Save_WritesPoolForm_DropsSlots
- Checks: WT-C11, WT-C7 · Project: Core (text) and Host (commit)
- Setup: old-form workers.json with an `about` text; Save from the panel with one type changed.
- Expected: Core builds text with `pool`, without `slots` and `workers`, `about` updated, main's line endings kept; Host commits and pushes it. Before the first Save nothing is written. After it, workers-state.json is not read.

## Dispatch by type (Core)

### TypeDispatch_TotalReached_NothingStarts_NoteSaysNofM
- Checks: WT-1, WT-12 · Project: Core
- Setup: `maxWorkers` 2; two workers running (types `worker` and `Sonnet`), one of them adopted; a third card ready that fits `worker`, which has room (max 2, 1 running).
- Expected: nothing starts; note "next up; waiting for a free worker (2 of 2 running)".

### TypeDispatch_TypeAtCap_NextCardOfOtherTypeStarts
- Checks: WT-2, WT-6 · Project: Core
- Setup: `worker` has 2 running (max 2); cards in order: 1 without `Worker:` (category A), 2 with `Worker: premium`.
- Expected: card 2 starts on `Sonnet`; card 1 is skipped with note "next up; waiting for worker (2 of 2 running: <cards>)"; no card waits behind another card's type.

### TypeDispatch_CapZero_TypeOff_NoteSaysOff
- Checks: WT-2, WT-12 · Project: Core
- Setup: `Opus` max 0; a card `Worker: Opus`.
- Expected: not started; note "next up; waiting for Opus is off".

### TypeDispatch_CapLoweredBelowRunning_NothingStopped_NoneStarted
- Checks: WT-2, WT-13 · Project: Core
- Setup: `worker` has 2 running; its max is lowered to 1; then one finishes (1 running), then the other (0); a variant removes the type while one runs.
- Expected: no stop request while 2 run; no new `worker` start at 1 running; a start at 0. A running worker whose type was removed counts toward WT-1, and its window says "(removed)".

### TypeDispatch_NoWorkerLine_OnlyNonPremium
- Checks: WT-3 · Project: Core
- Setup: card without `Worker:`; all non-premium types at cap.
- Expected: a premium type never takes it; it waits.

### TypeDispatch_WorkerPremium_OnlyPremium_FirstListed
- Checks: WT-3, WT-5 · Project: Core
- Setup: `Worker: premium`; `Sonnet` and `Opus` both usable.
- Expected: starts on `Sonnet` (first listed); a non-premium type never takes it.

### TypeDispatch_WorkerName_ModelOrShort_PicksThatType
- Checks: WT-3 · Project: Core
- Setup: `Worker:` values `opus`, `claude-opus-5-5`, the model's `Model-Short`, models.json's `short` for it, `nothing`.
- Expected: the first four pick `Opus` (case-insensitive); the last fits no type, note "next up; asks for worker nothing, and no worker type fits".

### TypeDispatch_WorkerSlotNumber_MatchesMigratedTypeOnly
- Checks: WT-3, WT-C10, WT-C11 · Project: Core
- Setup: `Worker: 4`; types migrated from slots (Opus from slot 4); then the same types read from a workers.json with a `pool`.
- Expected: before the pool form is saved, `Worker: 4` picks `Opus`; with the `pool` form it fits nothing (note says no worker type fits).

### TypeDispatch_AreaNotInTypesAreas_DoesNotFit
- Checks: WT-3, WT-12 · Project: Core
- Setup: card in category D without `Worker:`; `worker` is the only non-premium type.
- Expected: not started; note "next up; no worker type takes category D". *Retired by 969-A:* worker types have no categories list, so a card of any category fits a type (`TypeDispatch_AnyCategory_FitsATypeWithoutAWorkerLine`). No per-category or per-scope count limit applies to a type.

### TypeDispatch_ServerDown_TypeNotUsable_OtherTypeTakesCard
- Checks: WT-4, WT-10 · Project: Core
- Setup: `worker`'s model server does not answer (cached 20 s); another non-premium type with no endpoint.
- Expected: the card starts on the other type; with no other type, the note says "worker: model server does not answer". A type with no endpoint counts as up. There is no per-server limit.

### TypeDispatch_SeveralTypesUsable_FirstListedTakesIt
- Checks: WT-5, WT-C5 · Project: Core
- Setup: two non-premium types, the cheaper listed second, then listed first.
- Expected: the first in file order takes the card in both runs.

### TypeDispatch_WaitingCardKeepsScope_OverlappingLaterCardWaits
- Checks: WT-8 · Project: Core
- Setup: card 1 cannot start (its type is at cap) and has a scope; card 2 overlaps card 1's scope and could start; card 3 does not overlap.
- Expected: card 2 waits (note "next: <id>"); card 3 starts.

### TypeDispatch_QuotaOnClaude_CopilotTypesRunOn_StateStaysRunning
- Checks: WT-9 · Project: Core
- Setup: a `claude` worker's run ends `out of quota`; copilot types free.
- Expected: the claude agent is marked out of quota with the time, in every project; `Sonnet` and `Opus` do not start (note "Sonnet: claude out of quota since HH:mm"); copilot types do; the state stays `running`; the retry count is given back.

### TypeDispatch_AgentResume_ClearsOnlyThatAgent
- Checks: WT-9 · Project: Core
- Setup: two agents marked out of quota; Resume for one; then the page's Resume.
- Expected: the per-agent Resume clears only that agent's mark; the page's Resume clears every mark.

### TypeDispatch_Threads_CoresOverMaxWorkers
- Checks: WT-11 · Project: Core
- Setup: cores 16 with `maxWorkers` 4, 3, 40.
- Expected: 4, 5, 1 (floor, at least 1); set in the jobs environment variable and `{jobs}`.

### TypeDispatch_Notes_EachReasonInOrder
- Checks: WT-12 · Project: Core
- Setup: one candidate per reason: state `draining`; `Worker: x` fitting nothing; a category left out by every non-premium type; several fitting types each unusable for a different reason; every fitting type usable but the total reached; an orphan.
- Expected: the notes of WT-12 in the order of its checks, reasons joined by ", ", with "orphan to resume; " in front for the orphan.

### TypeDispatch_FullType_NoteNamesHeldCards
- Checks: WT-12 · Project: Core
- Setup: type `worker` max 5 with 5 running holding 901-A, 902-B, 903-C, 904-D, 905-E in slot order; a second case with max 2 holding 901-A and 902-B; a third with 2 running whose card names are unknown.
- Expected: "next up; waiting for worker (5 of 5 running: 901-A, 902-B, 903-C +2)"; "next up; waiting for worker (2 of 2 running: 901-A, 902-B)"; "next up; waiting for worker (2 of 2 running)".

### TypeDispatch_Fitting_IgnoresRunningAndQuota
- Checks: WT-12 · Project: Core
- Setup: all types at cap and one agent out of quota; the Queue window's worker column and the time limit from history.
- Expected: both use the type WT-5 would pick if nothing were running and no agent were out of quota.

### TypeDispatch_DryRun_LogsTypeAndSlot
- Checks: WT-14, DISP-21 · Project: Core
- Setup: dry run, one card on `Sonnet`.
- Expected: logs "DRY RUN Sonnet in slot 1 <- card"; nothing started; the loop exits after the first tick.

## Pool (Core)

### PoolTypes_TotalAcrossProjects_SharesPickProject
- Checks: WT-1, WT-7 · Project: Core
- Setup: two projects with weights 1:1; `maxWorkers` 2; project A runs 1 worker; both have a candidate.
- Expected: one more starts, from project B (furthest below its share); then the total stops every start.

### PoolTypes_ResumeFirst_EvenWhenOtherProjectBelowShare
- Checks: WT-7 · Project: Core
- Setup: project A has an orphan to resume; project B is further below its share.
- Expected: A's resume starts first.

### PoolTypes_TypeServesOnlyItsProjects
- Checks: WT-3, WT-7, WT-12 · Project: Core
- Setup: `Opus` has `projects: [P1]`; a premium card in P2; another in P1.
- Expected: P2's card has no type, note "next up; no worker type serves project P2"; P1's starts on `Opus`. A type with empty `projects` serves both.

### PoolTypes_ProjectWithoutTheAgent_TypeDoesNotFit
- Checks: WT-C9, WT-3 · Project: Core
- Setup: a type with agent `x`; project P2's scheduler.json has no agent `x`.
- Expected: the type does not fit P2's cards (noted); it fits P1's.

### PoolTypes_RedProjectOffersNothing_OthersRunOn
- Checks: WT-7 · Project: Core
- Setup: project A paused-red; project B has candidates.
- Expected: A offers nothing and B's cards start.

## Worktrees and adoption

### Folders_LowestFreeFolderTaken_AnyType
- Checks: WT-W1, WT-W2 · Project: Host
- Setup: folders slot1 to slot4; slot1 and slot3 busy; a `Sonnet` worker starts, then an `Opus` worker.
- Expected: `Sonnet` takes slot2, `Opus` slot4; each folder is prepared as WRK-5 says and its stop, warn and message files cleared (WRK-6). In a pool each project has its own set.

### Folders_AbandonedWorkerFreesFolder
- Checks: WT-W2, TIME-11 · Project: Host
- Setup: a killed worker still alive 2 minutes later in slot2.
- Expected: abandoned, logged once; slot2 is the lowest free folder again.

### Folders_StopAndWarnFilesKeepSlotNumber
- Checks: WT-W3, WT-W4 · Project: Host
- Setup: a worker of any type in folder 3; `maxWorkers` then lowered to 2.
- Expected: `stop-slot3`, `warn-slot3`, `msg-slot3.txt`, running.json `slot` 3 and accounting.csv `slot` 3; folders above `maxWorkers` stay on disk, unused, and nothing deletes a worktree.

### Adoption_EntryWithoutType_GetsMigratedType
- Checks: WT-W5 · Project: Core
- Setup: a running.json entry with no `type`, slot 4, no `pool`; another for a slot no type was migrated from.
- Expected: the first gets the type migrated from slot 4; the second counts toward WT-1 only.

### Adoption_FolderAboveMaxWorkers_KeptAndCounted
- Checks: WT-W5, WT-1, ADOPT-2 · Project: Core
- Setup: `maxWorkers` lowered to 2 while a worker runs in folder 4; the scheduler restarts.
- Expected: the worker is adopted (pid is `cmd`, start within 2 min) in folder 4 and counts toward WT-1 and its type's cap; no count is raised; stop deadlines carry over.

## Page and files

### Page_WorkerWindows_OnlyRunningWorkers_StopKillNoDisable
- Checks: WT-P1 · Project: Web
- Setup: two workers running, two folders idle; then none.
- Expected: two windows titled "<type> - slot <n> - <card>" with Minimize/Expand, Stop, Kill and a message box; none for idle folders; no Disable, Enable or Settings. With none: "No workers running (0 of 4)". In a pool the project is shown and the project dropdown applies.

### Page_WorkerTypesPanel_RowsInOrder_RunningOfMax_OffAtZero
- Checks: WT-P3 · Project: Web
- Setup: three types, one with max 0, `claude` out of quota since 10:15, one with a running worker.
- Expected: rows in config order with "<running> of <max>", the fields of WT-P3, "off" at 0, Add type, Remove only on a type with none running, Save, and "out of quota since 10:15" with a Resume for that agent.

### WorkerTypes_RenameWithOpenCards_OneCommitRewritesTheirWorkerLines
- Checks: WT-P7 Â· Project: Host
- Action: rename with open cards in queue/, active/ and review/.
- Expected: one commit carries workers.json and each card's `Worker:` line (reason kept); the message names the cards.

### WorkerTypes_RenameWithRunningWorker_AdoptedUnderTheNewName
- Checks: WT-P7 Â· Project: Host
- Action: rename while a worker of that type runs.
- Expected: the worker keeps running, shown and recorded in running.json under the new name.

### WorkerTypes_RemoveTypeUsedByOpenCards_RefusedNamingTheCards
- Checks: WT-P7 Â· Project: Host
- Action: remove a type an open card names.
- Expected: refused, the notice names the cards, nothing committed.

### WorkerTypes_RemoveUnusedType_Saves
- Checks: WT-P7 Â· Project: Host
- Action: remove a type no card names and nothing runs.
- Expected: saved.

### WorkerTypes_RemoveTypeWithRunningWorker_RefusedNamingTheWorker
- Checks: WT-P7 Â· Project: Host
- Action: remove a type whose worker still runs.
- Expected: refused, the notice names the type, slot and card, nothing committed.

### WorkerTypes_OffTypeWithRunningWorker_Saves
- Checks: WT-P7 Â· Project: Host
- Action: cap 0 on a type with a worker running.
- Expected: saves; the worker is untouched.

### WorkerTypes_CapChangeOnly_WritesOnlyWorkersJson
- Checks: WT-P7 Â· Project: Host
- Action: only a cap changes.
- Expected: only workers.json is written; no card is touched.

### WorkerTypes_NewRowWithNoOriginalName_IsNotARename
- Checks: WT-P7 Â· Project: Host
- Action: a new row with no original name.
- Expected: treated as an add, never a rename.

### Page_OldSlotRoutes_Gone_NewRoutesPost
- Checks: WT-P4 · Project: Web
- Setup: POST `/enable`, `/disable`, `/worker-settings`, `/slots-add`, `/slots-remove`; POST `/worker-types` and `/agent-resume?agent=claude`; `/stop`, `/kill`, `/send`, `/stopall`, `/open-workers`.
- Expected: the five old routes are not found; the new two and the kept ones act (by `slot`, plus `project` in a pool).

### Page_StopFromPage_CardWaitsOnYou
- Checks: WT-P2, TIME-10, TIME-12 · Project: Core and Host
- Setup: Stop from the page on one card, Kill on another.
- Expected: stop request with pageStopGraceMinutes, or kill "killed from the page"; no slot or type is disabled; each card waits under Waiting on you, "stopped from the page", with Resume.

### StatusFile_TypesLine
- Checks: WT-P5, ADOPT-3 · Project: Host
- Setup: `worker` 2 of 2, `Sonnet` 0 of 1 with max 0, `Opus` 1 of 1.
- Expected: one line per running worker (type, slot, card), then "types: worker 2/2, Sonnet 0/1 (off), Opus 1/1", then the needs-you, waits-for-planner, waits-for-you and in-review lines.

### Accounting_TypeColumn_OldHeaderRewritten
- Checks: WT-P6, ACCT-1 · Project: Host
- Setup: accounting.csv with the old header and rows; one run finishes on `Sonnet`.
- Expected: the header ends `,model,type` (rewritten in place as ACCT-4 does for `model`, old rows kept with an empty type); the new row has `Sonnet`; stats per model are unchanged.
