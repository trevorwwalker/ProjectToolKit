# Test plan: time limits, watchdog, stop reasons and results

Rules: TIME-1 to TIME-22. Source: `scheduler-next/spec.md` A6; `scheduler/schedule.ps1`
`Card-Limit` L697-701 and `Watch` L780-803 for the boundaries. The watchdog decision is Core with
an injected clock and log size; files and kills are Host.

Shared setup: maxMinutes 240, warnMinutes 20, stopGraceMinutes 20, stallMinutes 30,
pageStopGraceMinutes 5, maxRetries 2.

## Limits and warning

### Time_Limit_TimeBoxElseMax
- Checks: TIME-1 · Project: Core
- Cases: Time box 90 -> 90; none -> 240; `Time box: 0` -> 240 (L698 treats 0 as unset).

### Time_WarnLead_MinOfWarnAndQuarter
- Checks: TIME-2 · Project: Core
- Cases: limit 240 -> 20; 60 -> 15; 10 -> 2; 3 -> 0; 1 -> 0.

### Time_Warn_WrittenOnceInWindow
- Checks: TIME-4 · Project: Core
- Setup: limit 120, lead 20.
- Action: watch at age 99.9, 100.0, 110, 119.99.
- Expected: no warn at 99.9; warn-slotN written at exactly 100.0 with "About 20 minutes are left of this card's 120-minute limit (deadline HH:mm)" and logged; not rewritten at 110 or 119.99 while the file exists.

### Time_Warn_TinyLimit_NeverWritten
- Checks: TIME-2, TIME-4 · Project: Core
- Setup: Time box 3 (lead 0).
- Action: watch at ages 0 to 3.
- Expected: no warn file at any age (the window `>= 3 and < 3` is empty).

### Time_Warn_NotWhenStopping
- Checks: TIME-4 · Project: Core
- Setup: limit 120; stop requested from the page at age 90.
- Action: watch at 105.
- Expected: no warn file.

## Time limit exactly reached

### Time_LimitExactlyReached_StopRequestedNoWarn
- Checks: TIME-4, TIME-7, TIME-9 · Project: Core
- Setup: limit 120; warn file not yet written (the scheduler was busy); log growing.
- Action: watch at age exactly 120.0.
- Expected: no warn file (age is not < limit); stop-slotN created; StopWhy "over the 120 min limit"; grace 20.

### Time_JustUnderLimit_NoStop
- Checks: TIME-7 · Project: Core
- Action: watch at age 119.999.
- Expected: no stop request.

### Time_LimitPlusGraceExactly_Killed
- Checks: TIME-6 · Project: Core
- Setup: limit 120; stop requested at age 120 with grace 20.
- Action: watch at 139.99, then 140.0.
- Expected: alive at 139.99; at 140.0 killed. Which message: the stop-grace rule (L797) is checked first, so the kill reason is "still running 20 min after a stop request (over the 120 min limit)"; the End-Reason is still `time limit` (TIME-18 matches StopWhy).

### Time_OverLimitPlusGrace_KilledEvenWithLongerPageGrace
- Checks: TIME-6 · Project: Core
- Setup: limit 60; a stop with grace 100 recorded (hand-built state).
- Action: watch at age 80.
- Expected: killed "over the 60 min limit".

### Time_OverLimitAndQuiet_OnlyTimeLimitReason
- Checks: TIME-7, TIME-8 · Project: Core
- Setup: limit 60; no output for 45 min.
- Action: watch at age 61.
- Expected: one stop request, reason "over the 60 min limit" (L802-803 `elseif`).

## Stall and stop

### Time_Progress_IsRawLogSizeChange
- Checks: TIME-3 · Project: Core
- Setup: log size constant 10 min, then grows by 1 byte.
- Expected: quiet resets to 0 at the growth; a rewrite of the same size does not reset it.

### Time_Stall_ExactlyStallMinutes_StopRequested
- Checks: TIME-8 · Project: Core
- Action: watch with quiet 29.99, then 30.0.
- Expected: nothing at 29.99; at 30.0 stop request "no output for 30 min".

### Time_StopGrace_KillAfterGrace
- Checks: TIME-5 · Project: Core
- Setup: stalled stop at t0, grace 20.
- Expected: alive at t0+19.99; killed at t0+20 with "still running 20 min after a stop request (no output for 30 min)".

### Time_SecondStopRequest_OnlyShortensGrace
- Checks: TIME-9 · Project: Core
- Setup: watchdog stop with grace 20 at t0.
- Action: page Stop (grace 5) at t0+2; then another request with grace 30.
- Expected: grace 5, StopAsked and StopWhy unchanged from the first request; a log line "stop deadline moved to 5 min"; the 30 request changes nothing.

### Time_PageStop_DisablesSlot_StopAllDrains
- Checks: TIME-10 · Project: Host, Web
- Action: POST `/stop` slot 1; then POST `/stopall` with two running.
- Expected: slot 1 stop with grace 5 and disabled; stop all: both get grace 5, state `draining`.

### Time_Kill_EndsTreeKeepsFirstReason
- Checks: TIME-11 · Project: Host
- Setup: worker with child processes; StopWhy already "no output for 30 min".
- Action: kill "over the 60 min limit".
- Expected: whole tree gone; StopWhy unchanged; log "its card stays in active/ and will be resumed".

### Time_KillFails_AbandonedAfterTwoMinutes
- Checks: TIME-11 · Project: Core
- Setup: process reported alive after a kill (fake process table).
- Action: ticks at kill+1:59 and kill+2:00, +3:00.
- Expected: still occupying the slot at 1:59; at 2:00 abandoned, slot released, logged once.

### Time_PageKill_DisablesAndTicks
- Checks: TIME-12 · Project: Host, Web
- Action: POST `/kill` slot 1.
- Expected: killed "killed from the page", slot disabled, a tick runs at once.

### Time_Alive_PidAndStartTime
- Checks: TIME-13 · Project: Core
- Cases: pid exists, start +1.9 s -> alive; +2.1 s -> not; pid gone -> not; abandoned -> not.

## Results

### Time_Result_LastLaneResultLine
- Checks: TIME-14 · Project: Core
- Setup: text log with `LANE-RESULT: review` then `LANE-RESULT: done`; json-format log where `LANE-RESULT: done` appears only inside a tool output line and `* LANE-RESULT: needs-human` is a model line.
- Expected: `done`; `needs-human`; missing log -> `no log`; no line -> `no result line`.

### Time_Quota_DetectedFromNonModelLines
- Checks: TIME-15 · Project: Core
- Setup: no result line; raw log cases: plain `Error: You have exceeded your monthly quota`; JSON `{"type":"result","is_error":true,"result":"rate limit"}`; JSON `"subtype":"error..."`; model text `* we hit the rate limit earlier` only; `ratelimited` (not whole word).
- Expected: `out of quota` for the first three (retry count decremented; state running -> `paused-quota`, logged); `no result line` for the last two.

### Time_ServerDown_NoResult
- Checks: TIME-16 · Project: Core
- Setup: no result line, no quota text; fresh server check fails.
- Expected: `model server down`; retry count decremented.

### Time_NoLog_NonResume_CountsRetry
- Checks: TIME-17 · Project: Core
- Setup: queued card run that left no log; second case a resume run.
- Expected: retries +1 for the non-resume; the resume already counted at dispatch (DISP-16), no second increment.

### Time_EndReason_Mapping
- Checks: TIME-18 · Project: Core
- Cases: StopWhy "over the 60 min limit" -> `time limit`; "no output for 31 min" -> `stalled`; "killed from the page" -> `killed`; "stopped from the page" (L3955) and "stop all from the page" (L3868) -> `stopped from page`; none and exit 3 -> `crashed (exit 3)`; none and exit 0 -> `ended early`.

### Time_ResultRetryBookkeeping
- Checks: TIME-19 · Project: Core
- Cases: `stopped` with entry 2 -> 1; done/review/waiting/needs-human -> entry removed.

### Time_RetryCount_ClampedAtZero
- Checks: TIME-17, TIME-19 · Project: Core (closes C8)
- Setup: entry 0; result `stopped` (also quota, server down); a count below 0 carried in on a resume.
- Expected: the count stays 0 (`Math.Max(0, n - 1)`; `DispatchPlanner.CountResume` clamps the carried-in count).

### Time_CardNeverClaims_EveryRunCounts
- Checks: TIME-17, DISP-11 · Project: Core (closes C2)
- Setup: queued card whose runs end `ended early` (also `crashed`, `time limit`, `stalled`) without claiming.
- Expected: each run adds 1 to the card's count; at maxRetries it is stuck.

### Time_Retry_EveryRunWithNoResultCounts
- Checks: TIME-17 · Project: Core
- Setup: runs ending `no log`, `crashed (exit 1)`, `ended early`, `time limit`, `stalled`, for a card in queue/ and a card in active/.
- Expected: each adds 1 to a queued card; the resume run of an active card adds nothing more than DISP-16 already did.

### Time_Retry_RunsOutsideTheCardDoNotCount
- Checks: TIME-15, TIME-16, TIME-17, TIME-19 · Project: Core
- Setup: runs ending `out of quota`, `model server down`, `stopped from the page`, `killed from the page`, `stop all from the page`, and a worker killed after ignoring a stop; entries at 2 and at 0.
- Expected: each gives one back; the count never goes below 0.

### Time_Retry_ResumeRunNotCountedTwice
- Checks: TIME-17, DISP-16 · Project: Core
- Setup: active orphan resumed (count + 1 at dispatch); the run ends `crashed (exit 1)`.
- Expected: the count stays at the dispatch value.

### Time_AfterFinish_Bookkeeping
- Checks: TIME-20 · Project: Host
- Setup: 11 recent events; worker finishes `done`.
- Expected: log line, one accounting row, health wanted, recent list 10 long with this run first, job ended, slot free, slot files cleared.

### Time_ProcessExit_NoticedWithin250ms
- Checks: TIME-21 · Project: Host
- Setup: pollSeconds 15; a worker that exits after 1 s.
- Expected: a tick runs within 250 ms of the exit (allow 1 s on CI), not at the next 15 s poll.

### Time_Retries_PersistAcrossRestart
- Checks: TIME-22 · Project: Host
- Setup: retries {010-A: 1}.
- Action: tick, restart.
- Expected: retries.json written each tick; after restart 010-A has 1.

## Time limits from history (card 640-A; spec TLH-1 to TLH-5)

Tests: `Scheduler.Core.Tests/Accounting/AreaDurationsTests.cs`, `TimeBoxAdviceTests.cs`. The page display of the words is not tested (TLH-5).

### History_Window_Newest30Within30Days
- Checks: TLH-1 · Project: Core
- Setup: 40 done runs over 45 days for one category and model.
- Expected: only the newest 30 that finished within 30 days count.

### History_TooFewRuns_NoFigure
- Checks: TLH-2 · Project: Core
- Setup: groups of 2 and 3 done or review runs.
- Expected: 2 -> no warning and no suggestion; 3 -> a figure.

### History_MedianAndP90_Arithmetic
- Checks: TLH-3 · Project: Core
- Cases: odd count; even count (mean of the middles, a half rounds up); p90 of 3 runs is the largest; p90 of 10 is the 9th.

### History_WorkerTypeAndModelMatch
- Checks: TLH-4 · Project: Core
- Setup: slots with categories and premium flags; a card asking for none, for premium, for a name.
- Expected: the lowest enabled fitting slot, busy or down ignored; rows matched by `ModelColumn.Resolve`.

### History_QueueAdvice_WarnShortSuggestMissing
- Checks: TLH-5 · Project: Core
- Cases: Time box shorter than the usual length -> warning words; no Time box -> suggestion; enough box -> nothing.
