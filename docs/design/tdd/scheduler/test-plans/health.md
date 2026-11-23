# Test plan: health check of main, and gates

Rules: HLTH-1 to HLTH-11. Source: `scheduler-next/spec.md` A7; `scheduler/schedule.ps1`
`Start-Health` L1695-1719 and `Reap-Health` L1721-1743. The state machine (wanted, last sha,
red/green, scheduler state) is Core; running the command is Host.

### Health_WantedAfterFinishedRunAndCheckMain_NotAtStartup
- Checks: HLTH-1 · Project: Core
- Setup: fresh start with a health command.
- Expected: no health run at start-up; wanted after a worker finishes; wanted after POST `/health-now`, which also forgets the last checked sha.

### Health_NotStarted_Conditions
- Checks: HLTH-2 · Project: Core
- Cases: empty command; dry run; one already running; main's short sha equals the last checked sha (wanted cleared, nothing started).
- Expected: no start in each case; otherwise a start.

### Health_CheckMainSameSha_RunsAgain
- Checks: HLTH-1, HLTH-2 · Project: Core
- Setup: last checked sha = main's sha.
- Action: POST `/health-now`.
- Expected: a check starts (the sha was forgotten).

### Health_RunsInHealthWorktreeWithLog
- Checks: HLTH-3 · Project: Host
- Setup: health command `exit 0`; main at sha `abc1234`.
- Expected: worktree `<workspaceRoot>\health` at main; health.cmd sets the title, cds, sets jobsEnvVar, runs the command; output in `health-abc1234-<ts>.log`; runs in a job object.

### Health_HangingCheck_KilledAfterMaxMinutesTreatedRed
- Checks: HLTH-4 · Project: Core
- Setup: check running; fake clock at start + 239.99 then + 240.
- Expected: still running at 239.99; at 240 killed, logged "killed after 240 min; treated as red", result RED, state running -> paused-red.

### Health_KillLimit_UsesMaxMinutes
- Checks: HLTH-4 · Project: Core · **blocked on C16**
- Expected today: the limit is maxMinutes. Expected after C16: to be written.

### Health_Green_ResumesFromPausedRed
- Checks: HLTH-5 · Project: Core
- Cases: exit 0 in `paused-red` -> `running`, logged "main is green again"; exit 0 in `paused` -> stays `paused`; exit 0 in `running` -> stays.

### Health_Red_PausesOnlyFromRunning
- Checks: HLTH-5 · Project: Core
- Cases: exit 1 in `running` -> `paused-red`, logged; exit 1 in `paused` (developer pause) -> stays `paused`; exit 1 in `draining` -> stays `draining`.

### Health_RedMidRun_RunningWorkersContinueNoNewStarts
- Checks: HLTH-5, DISP-14 · Project: Core
- Setup: slot 1 running 010-A; queue 020-B ready; slot 2 free; health check finishes RED.
- Action: next tick; then slot 1 finishes `done`.
- Expected: 010-A keeps running (L1738-1742 do not touch running workers); 020-B not started, note "next up; dispatching is paused-red"; the finish makes health wanted; if main's sha did not change it is not re-run (HLTH-2) and the state stays paused-red; after a new commit lands and the check is green, 020-B starts.

### Health_RedBanner_WithShaAndLogLink
- Checks: HLTH-6 · Project: Web
- Setup: last result RED for sha `abc1234`; theme banner text with `{sha}`.
- Expected: the page shows the banner with `abc1234` and a link to `/health`.

### Health_ResumeOverridesRed
- Checks: HLTH-5, MOVE-8 · Project: Core · **blocked on C5**
- Setup: state paused-red; retries {010-A: 2, 020-B: 1}.
- Action: POST `/resume`.
- Expected today: state running without a green check; every retry entry cleared. Expected after C5: to be written.

### Health_NoStartupCheck_OthersPushesUnchecked
- Checks: HLTH-1 · Project: Core · **blocked on C7**
- Setup: start-up with a new commit on main; no worker finishes.
- Expected today: no check ever runs. Expected after C7: to be written.

### Health_SchedulerDoesNotRunGates
- Checks: HLTH-7 · Project: Core
- Setup: worker finishes `done` having pushed a commit.
- Expected: the scheduler runs only the health command (nothing card-specific); the gate rules reach the worker only through the prompt (asserted in `Workers_Prompt_ContainsEveryPart`).

### Health_NewCheck_EndsPreviousBeforeBuilding
- Checks: HLTH-8 · Project: Host (`HealthSupersedeTests`)
- Setup: a check is running; a second start is requested.
- Expected: the first check's job is ended and gone before the second builds; `health-running.json` names only the second; `Kill` removes the record.

### Health_StartAfterRestart_EndsLeftoverCheck
- Checks: HLTH-9 · Project: Host (`HealthSupersedeTests`)
- Setup: `health-running.json` records a tree from the previous scheduler.
- Expected: the start ends that tree before checking; it is not adopted.

### Health_OnlyFileLocks_RetriedOnceThenCouldNotRun
- Checks: HLTH-10 · Project: Host (`HealthSupersedeTests`)
- Setup: the check exits non-zero and its log's only errors are MSB3026/MSB3027, twice.
- Expected: one retry into the same log; the log says "could not run ... not reported as red"; exit code reported 0, so the state is not paused-red.

### Health_RealFailureWithLocks_StaysRedAndPausedRedKept
- Checks: HLTH-10 · Project: Host (`HealthSupersedeTests`)
- Setup: (a) the log has a lock error and a compile or test error; (b) the scheduler is already `paused-red` and the check could not run.
- Expected: (a) real verdict, red, no retry; (b) the real exit code is reported, so `paused-red` is kept.

### Health_PastTimeBox_WarnsOnce
- Checks: HLTH-11 · Project: Core (`HealthTimeBoxTests`)
- Setup: a check started at T+0; the tick asks again at T+6 min, T+20 min, and the check ends at T+21 min.
- Expected: one log line `health check of main <sha>: still running after 6 min (warn at 5 min)`, the page's health line holding that text while the check runs and nothing after it ends; no further time-box line.

### Health_InsideTimeBox_DoesNotWarn
- Checks: HLTH-11 · Project: Core (`HealthTimeBoxTests`)
- Setup: a check that ends 1 s before the 5 min box.
- Expected: no time-box line, no warning text.

### Health_WarnMinutesZero_TurnsWarningOff
- Checks: HLTH-11 · Project: Core (`HealthTimeBoxTests`)
- Setup: `limits.healthWarnMinutes` 0, a check that runs 3 hours (under maxMinutes).
- Expected: no warning at all; the check still finishes green.

### Health_Ended_ResultLineCarriesDuration
- Checks: HLTH-11 · Project: Core (`HealthTimeBoxTests`)
- Cases: exit 0 and exit 1, each ending 131 s after its start.
- Expected: `health check of main <sha>: green in 131 s` and `health check of main <sha>: RED in 131 s, see <log>; dispatching paused`.

### Health_KilledThenNewCheck_WarnsAgain
- Checks: HLTH-11, HLTH-4 · Project: Core (`HealthTimeBoxTests`)
- Setup: warn at 1 min; the check runs to maxMinutes and is killed; a new sha is then checked.
- Expected: the killed line and `RED in 14400 s`; the new check warns again from its own start.