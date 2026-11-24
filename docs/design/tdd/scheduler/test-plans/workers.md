# Test plan: workers, slots, starting a card, restart and adoption

Rules: WRK-1 to WRK-14, ADOPT-1 to ADOPT-3. Source: `scheduler-next/spec.md` A5 and A9.

### Workers_JsonFromMain_ParsedAndLanesUpperCased
- Checks: WRK-1 · Project: Core
- Setup: workers.json `{slots: 3, workers: {"1": {name, agent, endpoint, model, metricsUrl, lanes: "ab", premium: true}}}` (the old `premium` key: read only to choose the default worker, 1146-A).
- Expected: every field read; categories `AB`.

### Workers_JsonUnchangedText_NotReapplied_InvalidKeepsPrevious
- Checks: WRK-1 · Project: Core
- Setup: counting apply; same text twice; then invalid JSON.
- Expected: applied once; invalid JSON logged and previous settings kept.

### Workers_SlotsFromJson_NeverBelowHighestRunning
- Checks: WRK-2 · Project: Core
- Setup: slot 4 running; workers.json changes `slots` from 4 to 2; later to 6.
- Expected: count stays 4 while slot 4 runs; then 6, new slots 5 and 6 enabled.

### Workers_EnabledState_LocalAndPersisted
- Checks: WRK-3 · Project: Host
- Setup: no workers-state.json; then disable slot 2 from the page; restart.
- Expected: all enabled at first; after restart slot 2 disabled (read from workers-state.json). A 1.6-era log-folder workers.json with `enabled` is also read.

### Workers_EffectiveWorker_EndpointModelMetrics
- Checks: WRK-4 · Project: Core
- Cases (one assertion each):
  - agent format `claude` -> no endpoint, no metrics, even if config has an endpoint.
  - agent with `endpoint: ""` -> own service, same.
  - worker endpoint `http://w:8000/v1` -> endpoint that, metrics `http://w:8000/metrics`.
  - worker metricsUrl set -> used as is.
  - no worker endpoint, agent endpoint `http://a/v1` -> endpoint and metrics from the agent.
  - nothing on worker or agent -> config endpoint; metrics = config metricsUrl, else derived from config endpoint.
  - model: worker's, else agent's, else config's.

### Workers_PrepareSlot_CreatesAndResetsWorktree
- Checks: WRK-5 · Project: Host
- Setup: temporary repo and bare remote; no slot folder. Second run: slot has a dirty file, an untracked file and a stale branch.
- Expected: worktree `<workspaceRoot>\slot1` on branch `slot1` at remote main; second run leaves it clean at the new main (checkout -B, reset --hard, clean -fd).

### Workers_ClearSlotFiles_BeforeStart
- Checks: WRK-6 · Project: Host
- Setup: stop-slot1, warn-slot1, msg-slot1.txt exist in the log folder.
- Action: start a card on slot 1.
- Expected: the three files are gone before the worker process starts.

### Workers_ThreadsPerWorker
- Checks: WRK-7 · Project: Core
- Cases: 16 cores 2 slots -> 8; 16 cores 3 slots -> 5; 2 cores 4 slots -> 1; slots 0 -> cores.
- Expected also: the value is in env var jobsEnvVar and `{jobs}`.

### Workers_Environment_OtherAgentsVarsRemovedOwnSet
- Checks: WRK-8 · Project: Core
- Setup: agent A env `{X: 1}`, agent B env `{Y: "{model}"}`; process env has X=old.
- Action: build the env for a B worker.
- Expected: X removed, Y = the model.

### Workers_ContextLength_FromServerMinusN
- Checks: WRK-8 · Project: Core
- Setup: env `{C: "{contextLength-2000}", D: "{contextLength}"}`; server max_model_len 32768; second case 2500; third case none.
- Expected: C 30768, D 32768; C 1024 (minimum) for 2500; with none both unset and one log line.

### Workers_LogAndPromptFileNames
- Checks: WRK-9 · Project: Core
- Setup: card 010-A, start 2026-11-18 11:20:39, format `claude`; then format `text`.
- Expected: `010-A-20261118-112039.jsonl` plus `.log` beside it; text: `.log` only; prompt in `010-A-20261118-112039.prompt.txt`; command line holds only the pointer sentence and is under 8191 characters.

### Workers_Prompt_ContainsEveryPart
- Checks: WRK-10, HLTH-7 · Project: Core
- Setup: card 001-D, category D, slot 4, limit 120 min, start 11:20, web off, promptExtra `X`.
- Expected: one assertion per WRK-10 item: worktree path, category and category name, README as whole brief, category brief, resume text for an active card (do text for a queued one), one card only, never ask, pull/push/`wip/001-D` rules, threads, `X`, narration, today's date, deadline `13:20`, the three flag files, message protocol (`Developer:`, `.read.txt`), output rules, journal every 45 min / first within 30, no-web text, 10-minute and 2-minute limits, stop protocol, question format, the final `LANE-RESULT:` words. No `"` and no newline in the result.

### Workers_Prompt_WebOn_NoNoWebText
- Checks: WRK-10, WRK-11 · Project: Core
- Setup: worker.web true.
- Expected: no no-web paragraph; no web exclusions in args.

### Workers_WebOff_ArgsPerAgent
- Checks: WRK-11 · Project: Core
- Expected: Copilot args contain `--excluded-tools=web_fetch,web_search` and denies for curl, wget, iwr, irm, Start-BitsTransfer; Claude args contain `--disallowedTools WebFetch,WebSearch,Bash(curl:*)` and the rest of the L1825-1828 list.

### Workers_CommandLine_QuotingAndPause
- Checks: WRK-12 · Project: Core
- Setup: args `a`, `b c`, `x(y)`, `p;q`, `m&n`, `v|w`, `<z>`, `^`.
- Expected: `cmd /c title ... & ping -n 2 127.0.0.1 >nul & <cli> a "b c" "x(y)" "p;q" "m&n" "v|w" "<z>" "^" > <log> 2>&1`.

### Workers_JobObject_EndingKillsWholeTree
- Checks: WRK-13 · Project: Host (Windows only)
- Setup: start a worker command that spawns a child that spawns a grandchild, each sleeping.
- Action: end the job.
- Expected: all three processes gone; the job name follows `ProjectToolkit-<project>-<slot|health>-<n>-<pid>` (schedule.ps1 `Enter-Job` L372).

### Workers_PageMessage_AppendedToFile
- Checks: WRK-14 · Project: Host, Web
- Setup: running worker on slot 1 with Msgs; clock at 11:42.
- Action: POST `/send` slot 1 text "a\n\n  b" ; then 3000 characters; then to an idle slot.
- Expected: msg-slot1.txt gets `[11:42] a b`; readable log gets `<< you: a b`; long text cut to 2000; idle slot: nothing written, error notice.

## Restart and adoption

### Adopt_RunningJson_WrittenEveryTick
- Checks: ADOPT-1 · Project: Host
- Setup: one running worker.
- Action: one tick.
- Expected: running.json has every ADOPT-1 field for it.

### Adopt_OnlyCmdWithMatchingStartTime
- Checks: ADOPT-2, TIME-13 · Project: Host
- Setup: running.json entries: (a) live `cmd` pid, start within 2 min; (b) live non-cmd pid; (c) live cmd, start 5 min off (pid reused); (d) dead pid.
- Expected: only (a) adopted.

### Adopt_SlotAboveCount_RaisesCount
- Checks: ADOPT-2 · Project: Core
- Setup: slots 2; adopted worker on slot 4.
- Expected: slot count 4.

### Adopt_StopDeadlineCarriesOver_QuietFromLogWrite
- Checks: ADOPT-2 · Project: Core
- Setup: adopted worker stopping since 10 min with grace 20; log last written 25 min ago.
- Expected: killed 10 min after the restart, not 20; quiet = 25 min at adoption.

### Adopt_StatusTxt_Lines
- Checks: ADOPT-3 · Project: Host
- Setup: 2 slots (one running), one stuck card, one waiting on planner, one waiting on you, one review.
- Expected: status.txt has time and state, one line per slot, then the needs-you, planner, you and review lines.

## Message hooks (card 570-B; spec HOOK-1 to HOOK-4)

Tests: `Scheduler.Core.Tests/Launch/MessageHookTests.cs`, `Scheduler.Host.Tests/Program/MessageDeliveryTests.cs`.

### Hook_Deliver_PrintsAndMovesFile
- Checks: HOOK-1 · Project: Host
- Cases: Claude and Copilot hook input with a waiting message -> the agent's JSON with the "Developer:" text, the file moved to `.read.txt`, read time stamped; no message, no `PTK_MSG_FILE` or no hooks -> nothing printed and stdin not read, exit 0.

### Hook_ClaudeWorker_GetsSettingsFileAndEnv
- Checks: HOOK-2, HOOK-3 · Project: Core
- Expected: `--settings <log dir>\worker-hooks.claude.json` on Claude workers (PostToolUse and PostToolUseFailure), `PTK_MSG_FILE` in every worker's environment; Copilot and text workers get the fallback prompt with the file check; `MessageHook.Delivers` is true only for Claude.

### Hook_PromptWiring_HooksSetFromAgent
- Checks: HOOK-4 · Project: Host · **not written: wired by card 700-B (request #002)**
- Expected after 700-B: `TickHost.StartWorker` sets `Hooks = MessageHook.Delivers(agent.Format)`; the worker window shows when the message was read.
