# Test plan: start-up and configuration

Rules: CFG-1 to CFG-18, and the porting note G-1. Source: `scheduler-next/spec.md` A1.
Projects: **Core** = pure logic with no disk, git, process or clock (inject them); **Host** = the
exe and everything touching git, files, processes, mutexes; **Web** = the page and HTTP.

### Config_RepoArgument_ResolvesToGitTopLevel
- Checks: CFG-1 · Project: Host
- Setup: a temporary git repository with a subfolder `a/b`.
- Action: start with `-Repo <repo>\a\b`; again with the folder as the first positional argument.
- Expected: the repository is the top level in both cases.

### Config_NoRepoNoConfig_UsesCurrentDirectoryTopLevel
- Checks: CFG-1 · Project: Host
- Setup: current directory inside a git repository; no `-Repo`, no `-Config`.
- Action: start.
- Expected: the repository is the current directory's top level.

### Config_NoRepoAnywhere_FailsWithNameTheRepository
- Checks: CFG-1 · Project: Host
- Setup: current directory outside any git repository; no `-Repo`, no `-Config`.
- Action: start.
- Expected: fails with "Name the project's repository".

### Config_NonGitRepoArgument_IsError
- Checks: CFG-1 · Project: Host
- Setup: an empty, non-git folder.
- Action: start with `-Repo <folder>`.
- Expected: start-up fails with an error naming the folder; nothing is created.

### Config_DefaultConfigPath_IsRepoDocsJobsSchedulerJson
- Checks: CFG-2 · Project: Host
- Setup: repository with `docs\jobs\scheduler.json`.
- Action: start with `-Repo` only.
- Expected: that file is the config.

### Config_MissingConfigFile_IsFatal
- Checks: CFG-2 · Project: Host
- Setup: repository without `docs\jobs\scheduler.json`; separately, `-Config` naming a missing file.
- Action: start.
- Expected: both fail at start-up, before any listener or worker starts.

### Config_WithoutConfigArg_RereadsFromRemoteMain
- Checks: CFG-3 · Project: Host
- Setup: bare remote whose main has `docs/jobs/scheduler.json` with `slots: 3`; checkout has an older copy with `slots: 1`.
- Action: start with `-Repo`.
- Expected: a `git fetch` happens and slots = 3 (main's copy).

### Config_RemoteConfigInvalid_FallsBackToCheckoutWithReason
- Checks: CFG-3 · Project: Host
- Setup: main's `scheduler.json` is invalid JSON; checkout copy valid. Second case: remote unreachable.
- Action: start.
- Expected: the checkout's copy is used and the log names the reason (invalid JSON / fetch failed).

### Config_RemoteBranchJobsDirFromCheckoutCopy
- Checks: CFG-3 · Project: Host
- Setup: checkout copy sets `remote: up`, `branch: trunk`, `jobsDir: work/jobs/`; remote `up` has `trunk:work/jobs/scheduler.json`.
- Action: start.
- Expected: config is read from `up/trunk:work/jobs/scheduler.json`.

### Config_ExplicitConfigFile_UsedAsIsNoFetch
- Checks: CFG-3 · Project: Host
- Setup: `-Config <file>` in a repo whose main has a different scheduler.json.
- Action: start.
- Expected: the `-Config` file's values are used; main's copy is not read.

### Config_CfgRepo_UsedOnlyWhenNoRepoOnCommandLine
- Checks: CFG-4 · Project: Host
- Setup: config with `repo: <repoB>`; started (a) with `-Config` and no `-Repo`, (b) with `-Config` and `-Repo <repoA>`.
- Expected: (a) repoB, (b) repoA.

### Config_CfgHelper_NullAndEmptyMeanDefault_ZeroAndFalseAreValues
- Checks: CFG-5 · Project: Core
- Setup: values null, `""`, `0`, `false`, `"x"` with default `D`.
- Action: `Cfg(value, D)`.
- Expected: D, D, 0, false, "x".

### Config_EmptyFile_AllDefaults
- Checks: CFG-6 · Project: Core
- Setup: config `{}` for a repository folder named `Proj`.
- Action: load.
- Expected: every default in CFG-6, one assertion each: project `Proj`, jobsDir `docs/jobs`, remote `origin`, branch `main`, slots 2, port 8765, listen `localhost`, remoteControl false, profile false, workspaceRoot `<repo>-workspaces`, logDir `%LOCALAPPDATA%\Proj\scheduler`, jobsEnvVar `AGENT_JOBS`, workerWindow `Hidden`, pollSeconds 15, maxRetries 2, stallMinutes 30, maxMinutes 240, stopGraceMinutes 20, warnMinutes 20, pageStopGraceMinutes 5, health.command '', promptExtra '', worker.cli `copilot`, worker.args `-p {prompt} --allow-all --no-ask-user`, worker.web false, model/endpoint/metricsUrl '', the three default quota patterns.

### Config_JobsDirTrailingSlash_Trimmed
- Checks: CFG-6 · Project: Core
- Setup: `jobsDir: "work/jobs/"`.
- Expected: jobsDir `work/jobs`.

### Config_SlotRootAlias_SameAsWorkspaceRoot
- Checks: CFG-6 · Project: Core
- Setup: config with `slotRoot: X` and no `workspaceRoot`.
- Expected: workspaceRoot = X.

### Config_CommandLineOverrides_OneRun
- Checks: CFG-7 · Project: Core
- Setup: config slots 2, port 8765, model `m1`, health command `h`.
- Action: parse `-Slots 4 -Port 9000 -Model m2 -NoHealthCheck -Paused -DryRun`.
- Expected: slots 4, port 9000, model m2, health command '', start state `paused`, dry run on; the config file is unchanged.

### Config_SlotsZeroOrPortZero_DoNotOverride
- Checks: CFG-7 · Project: Core
- Setup: config slots 2, port 8765.
- Action: parse `-Slots 0 -Port 0`.
- Expected: slots 2, port 8765.

### Config_SlotsOnCommandLine_PinsCountOverWorkersJson
- Checks: CFG-8, WRK-2 · Project: Core
- Setup: `-Slots 3`; workers.json `slots: 5`.
- Action: apply workers.json.
- Expected: slot count stays 3.

### Config_DefaultAgentFormat_GuessedFromArgs
- Checks: CFG-9 · Project: Core
- Setup: worker.args containing `--output-format json`, `--output-format=json`, and neither.
- Expected: format `copilot`, `copilot`, `text`; the default agent has no own endpoint.

### Config_NamedAgent_DefaultsAndFormatGuess
- Checks: CFG-10 · Project: Core
- Setup: `agents: { claude: { args: "-p {prompt} --output-format stream-json" }, bare: {} }`.
- Expected: `claude`: cli `claude`, format `claude`, quota patterns `usage limit`, `rate limit`, `Failed to authenticate`. `bare`: cli `bare`, format `text`.

### Config_AgentEndpoint_PresenceRecordedSeparatelyFromValue
- Checks: CFG-10, WRK-4 · Project: Core
- Setup: agent A with `endpoint: ""`, agent B with no endpoint key.
- Expected: A has endpoint-present = true, value ''; B has endpoint-present = false.

### Config_AgentOf_UnknownNameGivesDefault
- Checks: CFG-11 · Project: Core
- Action: `AgentOf("claude")` with and without a `claude` agent; `AgentOf("nope")`.
- Expected: the named agent when defined, else `default`.

### Config_Expand_SubstitutesPlaceholders
- Checks: CFG-12 · Project: Core
- Setup: args `-m {model} -e {endpoint} -j {jobs} -p {project}`, env and health.command with the same; worker with model `w`, no endpoint; config endpoint `http://c`.
- Expected: `-m w -e http://c -j <threads> -p <project>` in args, env and health command.

### Config_LaneNames_FromConfigUntilLanesJsonRead
- Checks: CFG-13 · Project: Core
- Setup: `cfg.lanes` = A,B; categories.json on main defines A,B,C.
- Action: read names before and after the first `Read-Lanes`.
- Expected: A,B before; A,B,C after.

### Config_SecondScheduler_SameProject_FailsAfterWait
- Checks: CFG-14 · Project: Host
- Setup: hold the mutex `Global\ProjectToolkit-<project>-scheduler` in another process; injectable wait (use 1 s in the test instead of 60 s).
- Action: start.
- Expected: fails with "Another <project> scheduler is still running" after the wait.

### Config_AbandonedMutex_CountsAsOwned
- Checks: CFG-14 · Project: Host
- Setup: a process takes the mutex and exits without releasing it.
- Action: start.
- Expected: start-up succeeds.

### Config_StartupOrder_AdoptSeedListenLog
- Checks: CFG-15 · Project: Host
- Setup: running.json with an adoptable entry; accounting.csv with rows; dry run off; browser launcher faked.
- Action: start and stop before the first tick.
- Expected: in order: workers adopted, recent events and stats seeded, listener started, start line logged, browser launcher called once. In dry run the launcher is not called.

### Config_GitNeverPrompts
- Checks: CFG-16 · Project: Host
- Setup: fake `git` on PATH that records its environment and whether stdin is open, and writes to both stdout and stderr.
- Action: any git-using operation (card read).
- Expected: `GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=Never`, stdin closed; parsed output contains only stdout.

### Config_ListenLan_FallsBackToLocalhostWithReason
- Checks: CFG-17 · Project: Host
- Setup: `listen: lan`; the `+` prefix fails (simulate: port taken; URL reservation missing).
- Expected: log names the cause, and for the reservation case includes the `netsh http add urlacl` command; the page is then served on localhost.

### Config_ListenLocalhostFails_IsFatal
- Checks: CFG-17 · Project: Host
- Setup: the port is held by another listener on localhost.
- Expected: start-up fails.

### Config_Version_ShownOnPageAndLog
- Checks: CFG-18 · Project: Host, Web
- Action: start; GET `/`.
- Expected: the `Major.Minor.Rev` version string appears in the start log line and on the page, and matches the assembly version.

### Porting_TextCompares_AreCaseInsensitive
- Checks: G-1 (with CARD-4, CARD-8, CARD-15, DISP-18) · Project: Core
- Setup: a card `010-b-x.md` with `status: READY`, `PRIORITY: High`, `worker: OPUS`; a worker named `opus`.
- Action: read cards and dispatch.
- Expected: the card is read (category `b`, per the case-insensitive match at schedule.ps1 L603), is ready, has Prio 0, and fits the `opus` worker. Sort orders use culture compare.

## Tray, -NoBrowser and the build layout (cards 550-B, 670-B, 600-B)

Tests: `Scheduler.Tray.Tests` (`TrayArgumentsTests`, `SchedulerProcessTests`), `Scheduler.Host.Tests/Program/NoBrowserSwitchTests.cs`. The icon, menu, balloon and log window are checked by hand (index.md, untestable).

### Tray_Arguments_RepositoryThenSchedulerArgsAndNoBrowser
- Checks: TRAY-1, TRAY-2 · Project: Tray, Host
- Expected: the tray starts `scheduler-next.exe` with the repository, its own arguments and `-NoBrowser`; the exe reads `-NoBrowser` at entry and skips opening the page (CFG-15); a line with non-ASCII characters round-trips as UTF-8; the exe's stdout starts without a byte-order mark.

### Tray_StartLine_PageAddressAndTooltip
- Checks: TRAY-3, TRAY-7 · Project: Tray
- Expected: the address is read from `page http://...`; the tooltip text from `/live.json`, else the last console line; the bounded buffer keeps 5000 lines under two writer threads; a scheduler that ends by itself gives the last line and Restart.

### Tray_Exit_StopAllWaitsThenKillsOnlyIfPageFails
- Checks: TRAY-4 · Project: Tray
- Expected: POST `/stopall`, wait for the process; kill only when the page refuses or does not answer.

### Tray_RestartAndExitLeave_EndOnlySchedulerProcess
- Checks: TRAY-5, TRAY-6 · Project: Tray · fake scheduler whose own child survives
- Expected: only the scheduler process ends (Ctrl+C event, kill of that one process only as a fallback); the child survives; Restart then starts it again with the same arguments and the log keeps its lines with a restart mark.
- Not testable headless: the real Ctrl+C (only a program without a console of its own may send it). **Open (TRAY-8):** 670-B Q1 found on a desktop that Restart did not start the scheduler again and the workers were killed; a test for the restart start-up belongs to the fix.

### Build_ArtifactsLayout_AndPublish
- Checks: BUILD-1, BUILD-2 · Project: build · **manual**
- Expected: a clean `dotnet build` makes no `bin/` or `obj/` under `code/` or `test/`; `dotnet publish code\src\Scheduler.Host -c Release -o out\windows\scheduler` lands `scheduler-next.exe`, the tray and `themes/` in one folder.
