# Test plan: page and live view

Rules: PAGE-1 to PAGE-12. Source: `scheduler-next/spec.md` A12. Rendering and ordering are Core
or Web with fixture data; the listener, threads and timing are Web/Host. HTTP tests run the Web
project in-process on a free port.

### Page_ServerThread_SnapshotsAndPostQueue
- Checks: PAGE-1 · Project: Web
- Action: GET `/live.json` and `/` while the main loop is blocked; POST `/tick` as a fetch (Accept JSON) and as a form.
- Expected: the GETs answer from snapshots without the loop; fetch POST -> 202 `{"id":n}`; form POST -> 303; both queued for the loop.

### Page_RemotePost_WithoutRemoteControl_403
- Checks: PAGE-1, PAGE-7 · Project: Web
- Setup: remoteControl false; request from a non-loopback address (inject the remote address).
- Expected: POST -> 403; GET `/` shows buttons disabled with "(Only on the PC running the scheduler.)". With remoteControl true the POST is accepted.

### Page_SnapshotCadence
- Checks: PAGE-2 · Project: Core (timing logic with fake clock)
- Expected: snapshot after every click and tick; every 3 s while `/` was asked for in the last 15 s; none at 16 s without asks; remote version built only while another PC asked in the last 30 s.

### Page_LiveJson_RateAndShape
- Checks: PAGE-3 · Project: Web
- Setup: two running workers with logs of 50 lines and a partial line.
- Expected: JSON `{t, pageDone, slots:{"1":{log, stats, time}, ...}}`; log = last 20 rendered lines plus the partial one; builds stop 5 s after the last ask; the gap between builds is at least twice the last build time (fake clock).

### Page_ClickTick_Debounced
- Checks: PAGE-4 · Project: Core
- Cases: one click, no more requests -> tick at 250 ms; requests every 100 ms -> tick at 2 s.

### Page_Windows_Present
- Checks: PAGE-5 · Project: Web
- Setup: state with one of everything (held category, stuck card, question, waiting card, review card whose worker runs, review card idle, open request, card waiting on planner with deps done, two workers, queue, 12 events).
- Expected: each window and button listed in PAGE-5; the running card's review entry hidden; "all done: the planner can act now" beside the planner card; 10 recent events.

### Page_QueueOrder_DepsAndCycle
- Checks: PAGE-6 · Project: Core
- Setup: orphans 050-A, 030-B; queue 010-C depending on 020-C; 020-C; 015-D high; cycle 060-E <-> 061-E; a waiting-on-dev card, a running card, a stale duplicate.
- Expected: 030-B, 050-A, 015-D, 020-C, 010-C, then 060-E and 061-E in plain order (cycle broken by order); the last three left out.

### Page_WaitingOnDevInActive_ShownTwice
- Checks: PAGE-5, PAGE-6, DISP-4 · Project: Core · **blocked on C15**
- Expected today: listed under Waiting on you and as an orphan in Queue (L3650). Expected after C15: to be written.

### Page_StateText
- Checks: PAGE-8 · Project: Web
- Cases: running -> ok; paused -> warn; paused-red -> "paused: main is red", bad; paused-quota -> "paused: quota used up"; draining -> warn.

### Page_Routes_GetAndPost
- Checks: PAGE-9 · Project: Web
- Expected: every listed GET route answers 200 with valid input; `/log?file=` with a name not matching `<id>-<ts>.log` (e.g. `..\x.log`) -> 404 or 400, never a file outside the log folder; `/log` returns at most 2000 lines with ANSI stripped; unknown path -> 404. `/pause` only in running, `/resume` only in paused*; each other POST route reaches its handler.

### Page_ResumeInDraining
- Checks: PAGE-9 · Project: Core · **blocked on C6**
- Expected today: `/resume` in draining does nothing. Expected after C6: to be written.

### Page_SlotsAddRemove_ApiOnly
- **Superseded:** `/worker-settings`, `/enable`, `/disable`, `/slots-add` and `/slots-remove` are retired (937-B, 943-A); the test becomes: each of them answers 404.
- Checks: PAGE-9 · Project: Web · **blocked on C10**
- Expected today: POST `/slots-add` raises the count by one; `/slots-remove` lowers it (not below the highest running). After C10: keep or delete this test.

### Page_LogFile_CardIdOfAnyWidth
- Checks: PAGE-9, WRK-9 · Project: Web
- Expected: `/log?file=1200-A-<ts>.log` and `1000-A-20261119-093000.log` are served (200); accounting reads the same names; ids sort numerically in every table (`999-A` before `1000-A`).

### Page_RenderEvents_CopilotAndClaude
- Checks: PAGE-10 · Project: Core
- Setup: fixture Copilot jsonl (reasoning, message, tool call, failed tool, 40 s model call, error) and Claude stream-json (same plus result with cost, a compaction).
- Expected: lines marked `~`, `*`, `>`, `x`, the slow call, `!`; Claude session end and cost line; token estimate = characters / 3.5; context count reset after the compaction.

### Page_Themes_OrderAndExtends
- Checks: PAGE-11 · Project: Web
- Setup: project theme with font and banner words; themes/b.json `extends` default.
- Expected: selector order: project theme, default, then themes by order and label; b's CSS starts with the base CSS; banner words and font from the project theme only. (The choice kept per browser is manual: see index.md.)

### Page_Exit_DrainingWithNoWorkers
- Checks: PAGE-12 · Project: Host
- Setup: state draining, one worker running; it finishes.
- Expected: loop ends after that tick, not before; listener and page thread stopped; status file written; mutex released (a second instance can start at once).

## Live feed, review panel and tray-era page changes

### Page_LiveFeed_NumberedLinesAndSince
- Checks: PAGE-13 · Project: Web · card 610-C · tests: `PageLiveHistoryTests`, `PageLogHistoryTests`
- Cases: no `since` -> last 500; `?since=4:1234,1:87` -> later lines only, at most 500, with `from`, `next`, `run`, and the line being written in `partial`; a new run clears the window; the page keeps at most 500 lines per window; `page` changes only when the snapshot (without build time, load, log tails and stats) changes.

### Page_ReviewPanel_CommitsHealthFilesAndButtons
- Checks: REV-1, REV-2, REV-3, REV-4 · Project: Web, Host · card 650-C · tests: `Scheduler.Web.Tests/Server/ReviewPanelTests.cs`
- Cases: commits whose message holds the card id, from the last `jobs: start` on, else all; health result with the "checked at another commit" note; a path-shaped word in Result served by `/repo-file` only if the checkout has it; Done and Send back present; the Send back note is not stored (open point, spec B3 Q15).
- Not tested: the Host commit list (REV-5).
