# scheduler-next test plan: index

Written from `scheduler-next/spec.md` (card 001-D, plan `docs/jobs/plans/scheduler-next-phase1_plan.md`
step 1). The spec numbers its rules by category (`CFG-1`, `DISP-7`, ...) rather than `R1`, `R2`; those
IDs are the rule numbers here. Where the spec left a result open, the test cites the line of
`scheduler/schedule.ps1` (v2.3.16) it was settled from.

## Files

| File | Category | Rules | Tests |
|---|---|---|---|
| [config.md](config.md) | Start-up and configuration | CFG-1..18, G-1 | 32 |
| [cards.md](cards.md) | Reading cards and requests from main | CARD-1..20 | 27 |
| [dispatch.md](dispatch.md) | Dispatch and categories | DISP-1..24, LANE-1..5, AREA-1..2 | 43 |
| [workers.md](workers.md) | Workers, slots, starting a card, restart and adoption | WRK-1..14, ADOPT-1..3 | 22 |
| [time-limits.md](time-limits.md) | Time limits, watchdog, stop reasons, results | TIME-1..22 | 30 |
| [health.md](health.md) | Health check of main, gates | HLTH-1..10 | 17 |
| [accounting.md](accounting.md) | Accounting, statistics, server metrics | ACCT-1..9 | 11 |
| [claims-and-moves.md](claims-and-moves.md) | Claims and card moves from the page | MOVE-1..12, CM-1..21 | 24 |
| [planner.md](planner.md) | Planner outbox | PLAN-1..8 | 13 |
| [page.md](page.md) | Page and live view | PAGE-1..12 | 16 |
| [weighted-effort.md](weighted-effort.md) | Multi-project pool and weighted effort (new) | MP-1..9 | 19 |
| [scopes.md](scopes.md) | Per-card scopes instead of categories (new) | SC-1..7 | 14 |
| [worker-types.md](worker-types.md) | Worker types with caps (new, card 850-D) | WT-C1..12, WT-1..14, WT-W1..6, WT-P1..7 | 50 |
| [multi-project.md](multi-project.md) | Multi-project pool (new, card 996-D; supersedes weighted-effort.md) | MP-C1..9, MP-10..22, MP-H1..9, MP-P1..9 | 54 |
| [dynamic-locks.md](dynamic-locks.md) | Dynamic file locks (new, card 1018-D; design 994-D) | DL-1..27 | 65 |

## Conventions

- Each test: `### Name` (the intended xUnit method name, `Area_Condition_Result`), then
  `Checks:` (rule IDs) and `Project:`, `Setup`, `Action` (omitted when it is just "run the
  decision"), `Expected`.
- **Core** = pure logic, no disk, git, process, network or real clock: inject them (a clock, a
  card source, a process table, an HTTP fake). Most rules are tested here.
  **Host** = the exe: git, worktrees, files in the log folder, processes, job objects, mutexes.
  **Web** = the page: HTTP routes, rendering, the listener. A test naming two projects is written
  in the first and exercises the second.
- Host tests that need git use a temporary repository and a bare remote, never this repository.
  Tests that need a worker use a fake CLI (a small script that writes a chosen log and exits with
  a chosen code), never a real agent.
- Boundaries use `>=` exactly as `Watch` does (schedule.ps1 L788-803): a test at exactly the
  limit expects the limit's action; a test just under it expects none.

## Boundaries and error paths (the card's list)

| Case | Tests |
|---|---|
| Empty queue | `Dispatch_EmptyQueue_NothingStartedNoNotes`, `Pool_NoProjectHasRunnableCard_WorkerStaysIdle` |
| All workers busy | `Dispatch_AllWorkersBusy_CandidateWaitsForFreeWorker`, `Dispatch_AllWorkersBusy_ThenOneFinishes_StartsNextTick`, `Dispatch_NoSlotFits_LaneStaysTaken` |
| Dependency cycles | `Cards_DependencyCycle_BothWaitForever`, `Page_QueueOrder_DepsAndCycle` |
| Malformed card file | `Cards_MalformedCard_NoStatusLine_IsNotReady`, `Cards_NonMatchingFiles_Ignored`, `Cards_Priority_Mapping_LastWins`, `Cards_TimeBox_ParsedLastWins`, `Cards_FieldLinesInSections_AreIgnored`, `Cards_DuplicateInQueue_RemovedFromMain`; malformed config files: `Config_RemoteConfigInvalid_FallsBackToCheckoutWithReason`, `Lanes_InvalidJsonOrNoLanesKey_PreviousKept`, `Workers_JsonUnchangedText_NotReapplied_InvalidKeepsPrevious`, `Planner_Validation_Failures` |
| Time limit exactly reached | `Time_LimitExactlyReached_StopRequestedNoWarn`, `Time_JustUnderLimit_NoStop`, `Time_LimitPlusGraceExactly_Killed`, `Time_Warn_WrittenOnceInWindow`, `Time_Warn_TinyLimit_NeverWritten`, `Time_Stall_ExactlyStallMinutes_StopRequested`, `Health_HangingCheck_KilledAfterMaxMinutesTreatedRed` |
| Health red mid-run | `Health_RedMidRun_RunningWorkersContinueNoNewStarts`, `Health_Red_PausesOnlyFromRunning`, `Pool_RedMainInOneProject` |

## Untestable or manual, with the reason

These rules have tests for the parts that can be automated; the parts below are checked by hand
when the port runs beside the old scheduler.

| Rule | Part not tested automatically | Reason |
|---|---|---|
| CFG-15 | The page actually opens in the browser | Needs a desktop session; the test only checks the launcher is called. |
| MOVE-9 | The editor window is brought to the front | Window focus depends on the desktop session and Windows' foreground rules; the test only checks which path is opened. |
| PAGE-11 | The theme choice is kept per browser | Kept in the browser's own storage by page script; no browser in the test run. |
| HLTH-7 | That workers only push gate-passing commits | Worker behaviour, not the scheduler's; the scheduler's part is the prompt text, tested in `Workers_Prompt_ContainsEveryPart`. |
| WRK-13 | Job objects on a non-Windows build agent | Windows only; the test is skipped off Windows (the scheduler itself is Windows only). |
| TIME-21 | The exact 250 ms | Timing on a loaded build machine; the test allows 1 s. |

No rule is untestable as a whole.

## Blocked tests

A test marked **blocked on Qn** waits for the developer's answer to the spec's open question n
(spec B3); **blocked on Cn** waits for a decision on flag n (spec part C: keep today's behaviour
or fix it). Each states today's behaviour, so it can be written to pin the current behaviour if
the decision is "keep".

| Question | Tests |
|---|---|
| Q1 high priority vs weights | `Pool_HighPriorityCard_VsWeights` |
| Q2 window and weight arithmetic | `Pool_FurthestBelowShare_GetsWorker`, `Pool_ProjectWithNoMinutes_WinsOutright`, `Pool_WindowSlides`, `Pool_ProjectWeights_Parsed` |
| Q3 orphans and weights | `Pool_OrphanResume_VsWeights` |
| Q4 pool worker definitions | `Pool_AnyWorkerTakesAnyProjectsCard` |
| Q5 scope syntax | `Scope_CardNamesScope_Parsed`, `Scope_FolderContainsFile_Overlaps`, `Scope_AllowedOverlap_AddOnlyFile`, `Scope_EmptyScope_Rejected` |
| Q6 overlap across projects | `Scope_AnyProject_Overlap` |
| Q7 glob overlap | `Scope_GlobOverlap_Rules` |
| Q8 scope enforcement | `Scope_OutOfScopeChange_Detected` |
| Q9 logging rejected pushes | `Scope_RejectedPushAndConflict_Logged` |
| Q10 held categories | answered by 560-A: never held (`Scope_HeldAreaWhenRedefined` now expects the new default) |
| Q11 red health / quota in a pool | `Pool_RedMainInOneProject`, `Pool_QuotaPause_PerAgent` |
| Q12 one page, whose settings | `Pool_TopLevelConfig_ProjectsKeepOwnRepoAndJobs`, `Pool_CombinedPage_FilterAndOneWaitingOnYou` |
| Q13 Worker: request in the pool | `Pool_WorkerRequest_Kept` |
| Q14 maxRetries and limits per project | `Pool_MaxRetriesAndLimits_Source` |
| C1 failed planner batch reported as sent | `Planner_FailedBatch_ReportedAsSent` |
| C2 card that never claims retried for ever (closed, 940-A) | `Time_CardNeverClaims_EveryRunCounts` |
| C3 stuck queued card blocks an orphan | `Dispatch_LowPriorityOrphan_BehindStuckQueued` |
| C4 category reserved by a card no worker can take | `Dispatch_NoSlotFits_LaneStaysTaken` |
| C5 Resume overrides red health | `Health_ResumeOverridesRed` |
| C6 draining is one-way | `Page_ResumeInDraining` |
| C7 no health check at start-up | `Health_NoStartupCheck_OthersPushesUnchecked` |
| C8 retry count can go negative (closed, 940-A) | `Time_RetryCount_ClampedAtZero` |
| C9 queue resume keeps retries | `Moves_ResumeQueueCard_RetriesNotCleared` |
| C10 /slots-add, /slots-remove | `Page_SlotsAddRemove_ApiOnly` |
| C12 category overlap with globs | `Lanes_Overlaps_GlobOwnsNotSeenAsFolder` |
| C13 accounting.csv quoting | `Acct_Row_QuoteOrCommaInFields` |
| C14 Card.Premium unused | `Dispatch_CardPremiumFlag_Unused` |
| C15 waiting card shown twice | `Page_WaitingOnDevInActive_ShownTwice` |
| C16 health kill limit | `Health_KillLimit_UsesMaxMinutes` |

C17 (worker protocol not seen) is settled: `docs/jobs/README.md` is in this repository and defines
the claim; see claims-and-moves.md. C18 (solution already scaffolded) needs no test.

## Rule -> tests

| Rule | Tests (file) |
|---|---|
| G-1 | `Porting_TextCompares_AreCaseInsensitive` (config.md) |
| CFG-1 | `Config_RepoArgument_ResolvesToGitTopLevel` (config.md), `Config_NoRepoNoConfig_UsesCurrentDirectoryTopLevel` (config.md), `Config_NoRepoAnywhere_FailsWithNameTheRepository` (config.md), `Config_NonGitRepoArgument_IsError` (config.md) |
| CFG-2 | `Config_DefaultConfigPath_IsRepoDocsJobsSchedulerJson` (config.md), `Config_MissingConfigFile_IsFatal` (config.md) |
| CFG-3 | `Config_WithoutConfigArg_RereadsFromRemoteMain` (config.md), `Config_RemoteConfigInvalid_FallsBackToCheckoutWithReason` (config.md), `Config_RemoteBranchJobsDirFromCheckoutCopy` (config.md), `Config_ExplicitConfigFile_UsedAsIsNoFetch` (config.md) |
| CFG-4 | `Config_CfgRepo_UsedOnlyWhenNoRepoOnCommandLine` (config.md) |
| CFG-5 | `Config_CfgHelper_NullAndEmptyMeanDefault_ZeroAndFalseAreValues` (config.md) |
| CFG-6 | `Config_EmptyFile_AllDefaults` (config.md), `Config_JobsDirTrailingSlash_Trimmed` (config.md), `Config_SlotRootAlias_SameAsWorkspaceRoot` (config.md) |
| CFG-7 | `Config_CommandLineOverrides_OneRun` (config.md), `Config_SlotsZeroOrPortZero_DoNotOverride` (config.md) |
| CFG-8 | `Config_SlotsOnCommandLine_PinsCountOverWorkersJson` (config.md) |
| CFG-9 | `Config_DefaultAgentFormat_GuessedFromArgs` (config.md) |
| CFG-10 | `Config_NamedAgent_DefaultsAndFormatGuess` (config.md), `Config_AgentEndpoint_PresenceRecordedSeparatelyFromValue` (config.md) |
| CFG-11 | `Config_AgentOf_UnknownNameGivesDefault` (config.md) |
| CFG-12 | `Config_Expand_SubstitutesPlaceholders` (config.md) |
| CFG-13 | `Config_LaneNames_FromConfigUntilLanesJsonRead` (config.md) |
| CFG-14 | `Config_SecondScheduler_SameProject_FailsAfterWait` (config.md), `Config_AbandonedMutex_CountsAsOwned` (config.md) |
| CFG-15 | `Config_StartupOrder_AdoptSeedListenLog` (config.md) |
| CFG-16 | `Config_GitNeverPrompts` (config.md) |
| CFG-17 | `Config_ListenLan_FallsBackToLocalhostWithReason` (config.md), `Config_ListenLocalhostFails_IsFatal` (config.md) |
| CFG-18 | `Config_Version_ShownOnPageAndLog` (config.md) |
| VER-1 | none (a rule for planners and workers, in `jobs/README.md`) |
| VER-2 | `CoreInfo_Version_IsTheBuildsInformationalVersionWithoutTheSha`, `CoreInfo_Version_IsTheAssemblysMajorMinorRevision` (Core), `VersionSwitch_PrintsTheBuildsVersion` (Host) |
| VER-3 | none (a rule for workers; `CoreInfo_Version_IsMajorMinorRevDigits` checks the shape it keeps) |
| VER-4 | none (a rule for workers) |
| CARD-1 | `Cards_ReadFromRemoteMainOnly_NotWorkingTree` (cards.md), `Cards_BlobCache_ReparsesOnlyChangedBlobs` (cards.md) |
| CARD-2 | `Cards_Fetch_AtMostEvery15Seconds` (cards.md) |
| CARD-3 | `Cards_LsTreeFails_KeepsPreviousList` (cards.md) |
| CARD-4 | `Cards_PathPattern_FieldsAndId` (cards.md), `Cards_NonMatchingFiles_Ignored` (cards.md) |
| CARD-5 | `Cards_DoneFolder_NotOpened` (cards.md) |
| CARD-6 | `Cards_Title_FirstHashSpaceLine` (cards.md) |
| CARD-7 | `Cards_Status_FirstLineAndContinuations` (cards.md), `Cards_MalformedCard_NoStatusLine_IsNotReady` (cards.md) |
| CARD-8 | `Cards_Priority_Mapping_LastWins` (cards.md), `Cards_FieldLinesInSections_AreIgnored` (cards.md) |
| CARD-9 | `Cards_FieldLinesInSections_AreIgnored` (cards.md), `Cards_TimeBox_ParsedLastWins` (cards.md) |
| CARD-10 | `Cards_WorkerRequest_TextBeforeParenthesis` (cards.md), `Dispatch_CardPremiumFlag_Unused` (dispatch.md) |
| CARD-11 | `Cards_FieldLinesInSections_AreIgnored` (cards.md), `Cards_DependsOn_AllIdsLastLineWins` (cards.md), `Cards_DependsOn_IdsOfAnyWidth` (cards.md) |
| CARD-12 | `Cards_Questions_ParsedWithChoicesAndDestinations` (cards.md) |
| CARD-13 | `Cards_Why_FromStatusOrResult` (cards.md) |
| CARD-14 | `Cards_LatestJournal_GreatestDateTimeWins` (cards.md), `Cards_ShortText_StripsAndTruncates` (cards.md) |
| CARD-15 | `Cards_WaitsOnDev_Patterns` (cards.md) |
| CARD-16 | `Cards_WaitsOnPlanner_Patterns` (cards.md) |
| CARD-17 | `Cards_DepsDone_DoneOrReviewOnly` (cards.md), `Cards_DependencyCycle_BothWaitForever` (cards.md) |
| CARD-18 | `Cards_Requests_ReadAndOpenFlag` (cards.md) |
| CARD-19 | `Cards_DuplicateInQueue_RemovedFromMain` (cards.md), `Cards_DuplicateOutsideQueue_LoggedOnceLeftAlone` (cards.md) |
| CARD-20 | `Cards_DependsOn_IdsOfAnyWidth` (cards.md), `Cards_PathPattern_FieldsAndId` (cards.md) |
| DISP-1 | `Dispatch_FinishedAndBusyLanes` (dispatch.md) |
| DISP-2 | `Dispatch_Orphan_ResumedBeforeQueuedSamePriority` (dispatch.md), `Dispatch_ActiveNeedsHuman_NotAnOrphan` (dispatch.md), `Pool_OrphanResume_VsWeights` (weighted-effort.md) |
| DISP-3 | `Dispatch_OrphanInBusyLane_SkippedSilently` (dispatch.md) |
| DISP-4 | `Dispatch_ActiveWaitingOrBlocked_NotOrphanDoesNotHoldLane` (dispatch.md), `Page_WaitingOnDevInActive_ShownTwice` (page.md) |
| DISP-5 | `Dispatch_OrphanInHeldLane_NotResumed` (dispatch.md; retired by 560-A) |
| DISP-6 | `Dispatch_LowPriorityOrphan_WaitsBehindHigherPriorityQueued` (dispatch.md), `Dispatch_LowPriorityOrphan_QueuedCardNotReadyOrBlocked_DoesNotBlock` (dispatch.md), `Dispatch_LowPriorityOrphan_BehindStuckQueued` (dispatch.md) |
| DISP-7 | `Dispatch_StuckOrphan_NotResumedListedWithRetry` (dispatch.md), `Dispatch_OrphanRetriesOneBelowMax_Resumed` (dispatch.md), `Pool_MaxRetriesAndLimits_Source` (weighted-effort.md) |
| DISP-8 | `Dispatch_Orphan_ResumedBeforeQueuedSamePriority` (dispatch.md) |
| DISP-9 | `Dispatch_EmptyQueue_NothingStartedNoNotes` (dispatch.md), `Dispatch_QueuedOrder_PrioNumLane` (dispatch.md) |
| DISP-10 | `Cards_MalformedCard_NoStatusLine_IsNotReady` (cards.md), `Dispatch_QueuedNotReady_NoteIsStatus` (dispatch.md) |
| DISP-11 | `Dispatch_LowPriorityOrphan_BehindStuckQueued` (dispatch.md), `Dispatch_QueuedStuck_RetriesAtMax` (dispatch.md), `Dispatch_StartWorkerFails_CardNotRestartedSameTick` (dispatch.md), `Time_CardNeverClaims_RetriedWithoutLimit` (time-limits.md) |
| DISP-12 | `Cards_DependencyCycle_BothWaitForever` (cards.md), `Dispatch_QueuedChecksInOrder_DepsThenBusyThenHeld` (dispatch.md) |
| DISP-13 | `Dispatch_QueuedOrder_PrioNumLane` (dispatch.md), `Dispatch_OverlappingCandidates_OnlyFirstStartsPerTick` (dispatch.md) |
| DISP-14 | `Dispatch_EmptyQueue_NothingStartedNoNotes` (dispatch.md), `Dispatch_Orphan_ResumedBeforeQueuedSamePriority` (dispatch.md), `Dispatch_HighPriorityQueued_BeatsNormalOrphan` (dispatch.md), `Dispatch_NotRunning_NoStartsNoteSaysState` (dispatch.md), `Health_RedMidRun_RunningWorkersContinueNoNewStarts` (health.md), `Pool_HighPriorityCard_VsWeights` (weighted-effort.md) |
| DISP-15 | *Superseded by the WT rules (960-D).* `Dispatch_AllWorkersBusy_ThenOneFinishes_StartsNextTick` (dispatch.md), `Dispatch_ServerUp_CachedFor20Seconds` (ServerCacheTests.cs), `TypeDispatch_StartWorkerFails_CardNotTriedAgainButTheNextIs` (TypeDispatchTests.cs); the slot tests `Dispatch_AllWorkersBusy_CandidateWaitsForFreeWorker`, `Dispatch_LowestFittingSlot` and `Dispatch_NoSlotFits_LaneStaysTaken` are retired |
| DISP-16 | `Dispatch_OrphanRetriesOneBelowMax_Resumed` (dispatch.md) |
| DISP-17 | *Superseded by the WT rules (960-D).* `TypeDispatch_AreaNotInTypesAreas_DoesNotFit` (worker-types.md); `Dispatch_LaneAllowed` is retired |
| DISP-18 | *Superseded by the WT rules (960-D).* `TypeDispatch_NoWorkerLine_OnlyNonPremium`, `TypeDispatch_WorkerPremium_OnlyPremium_FirstListed` (worker-types.md), `Pool_WorkerRequest_Kept` (weighted-effort.md); `Dispatch_WorkerFits_Rules` and `Dispatch_CardPremiumFlag_Unused` are retired |
| DISP-19 | *Superseded by WT-12 (960-D).* `TypeDispatch_Notes_EachReasonInOrder` (worker-types.md); `Dispatch_NotStartedNote_NamesWhatStopsEachFittingSlot`, `Dispatch_AllWorkersBusy_CandidateWaitsForFreeWorker`, `Dispatch_NotRunning_NoStartsNoteSaysState` and `Dispatch_NotStartedNotes_AllVariants` are retired |
| DISP-20 | `Dispatch_LocksLineIgnored` (dispatch.md; reversed by 560-A, now expects the wait), `Scope_LocksLine_IsPartOfScope` (scopes.md) |
| DISP-22 | `Dispatch_LocksPathsAreScope_AndAreaDefault` (dispatch.md), `Scope_LocksLine_IsPartOfScope` (scopes.md), `Dispatch_OverlappingCandidates_OnlyFirstStartsPerTick` (dispatch.md) |
| DISP-23, DISP-24 | `Dispatch_LocksPathsAreScope_AndAreaDefault` (dispatch.md), `Scope_NoOwnScope_TakesAreaDefault` (scopes.md), `Scope_AllowedOverlap_AddOnlyFile` (scopes.md) |
| DISP-21 | `Dispatch_DryRun_LogsAndExitsAfterFirstTick` (dispatch.md) |
| LANE-1 | `Lanes_ReadFromMain` (dispatch.md), `Lanes_InvalidJsonOrNoLanesKey_PreviousKept` (dispatch.md) |
| LANE-2 | `Lanes_Redefined_AcceptedAtOnce` (dispatch.md, 560-A), `Lanes_NewLane_Accepted` (dispatch.md), `Lanes_ChangedNoPendingCards_AcceptedAndLogged` (dispatch.md), `Lanes_ChangedWithPendingCards_HeldWithUpToSixIds` (dispatch.md), `Lanes_SameDefinitionDifferentFormatting_NotAChange` (dispatch.md), `Scope_HeldAreaWhenRedefined` (scopes.md) |
| LANE-3 | `Lanes_AcceptFromPage_ReleasesLane` (dispatch.md; dead since 560-A) |
| AREA-1 | `Scope_AreasAreLabels_NotLimits` (scopes.md), `Scope_AreaBriefInPrompt` (scopes.md) |
| AREA-2 | `Lanes_Redefined_AcceptedAtOnce` (dispatch.md), `Scope_HeldAreaWhenRedefined` (scopes.md) |
| LANE-4 | `Lanes_Overlaps_Warnings` (dispatch.md), `Lanes_Overlaps_GlobOwnsNotSeenAsFolder` (dispatch.md) |
| LANE-5 | `Lanes_Brief_InPrompt` (dispatch.md), `Scope_AreaBriefInPrompt` (scopes.md) |
| WRK-1 | `Workers_JsonFromMain_ParsedAndLanesUpperCased` (workers.md), `Workers_JsonUnchangedText_NotReapplied_InvalidKeepsPrevious` (workers.md) |
| WRK-2 | `Config_SlotsOnCommandLine_PinsCountOverWorkersJson` (config.md), `Workers_SlotsFromJson_NeverBelowHighestRunning` (workers.md) |
| WRK-3 | `Workers_EnabledState_LocalAndPersisted` (workers.md) |
| WRK-4 | `Config_AgentEndpoint_PresenceRecordedSeparatelyFromValue` (config.md), `Workers_EffectiveWorker_EndpointModelMetrics` (workers.md) |
| WRK-5 | `Workers_PrepareSlot_CreatesAndResetsWorktree` (workers.md) |
| WRK-6 | `Workers_ClearSlotFiles_BeforeStart` (workers.md) |
| WRK-7 | `Pool_ThreadsBudget_CoresOverPoolSize` (weighted-effort.md), `Workers_ThreadsPerWorker` (workers.md) |
| WRK-8 | `Workers_Environment_OtherAgentsVarsRemovedOwnSet` (workers.md), `Workers_ContextLength_FromServerMinusN` (workers.md) |
| WRK-9 | `Page_LogFile_CardIdOfAnyWidth` (page.md), `Workers_LogAndPromptFileNames` (workers.md) |
| WRK-10 | `Workers_Prompt_ContainsEveryPart` (workers.md), `Workers_Prompt_WebOn_NoNoWebText` (workers.md) |
| WRK-11 | `Workers_Prompt_WebOn_NoNoWebText` (workers.md), `Workers_WebOff_ArgsPerAgent` (workers.md) |
| WRK-12 | `Workers_CommandLine_QuotingAndPause` (workers.md) |
| WRK-13 | `Workers_JobObject_EndingKillsWholeTree` (workers.md) |
| WRK-14 | `Workers_PageMessage_AppendedToFile` (workers.md) |
| TIME-1 | `Time_Limit_TimeBoxElseMax` (time-limits.md), `Pool_MaxRetriesAndLimits_Source` (weighted-effort.md) |
| TIME-2 | `Time_WarnLead_MinOfWarnAndQuarter` (time-limits.md), `Time_Warn_TinyLimit_NeverWritten` (time-limits.md) |
| TIME-3 | `Time_Progress_IsRawLogSizeChange` (time-limits.md) |
| TIME-4 | `Time_Warn_WrittenOnceInWindow` (time-limits.md), `Time_Warn_TinyLimit_NeverWritten` (time-limits.md), `Time_Warn_NotWhenStopping` (time-limits.md), `Time_LimitExactlyReached_StopRequestedNoWarn` (time-limits.md) |
| TIME-5 | `Time_StopGrace_KillAfterGrace` (time-limits.md) |
| TIME-6 | `Time_LimitPlusGraceExactly_Killed` (time-limits.md), `Time_OverLimitPlusGrace_KilledEvenWithLongerPageGrace` (time-limits.md) |
| TIME-7 | `Time_LimitExactlyReached_StopRequestedNoWarn` (time-limits.md), `Time_JustUnderLimit_NoStop` (time-limits.md), `Time_OverLimitAndQuiet_OnlyTimeLimitReason` (time-limits.md) |
| TIME-8 | `Time_OverLimitAndQuiet_OnlyTimeLimitReason` (time-limits.md), `Time_Stall_ExactlyStallMinutes_StopRequested` (time-limits.md) |
| TIME-9 | `Time_LimitExactlyReached_StopRequestedNoWarn` (time-limits.md), `Time_SecondStopRequest_OnlyShortensGrace` (time-limits.md) |
| TIME-10 | `Time_PageStop_DisablesSlot_StopAllDrains` (time-limits.md) |
| TIME-11 | `Time_Kill_EndsTreeKeepsFirstReason` (time-limits.md), `Time_KillFails_AbandonedAfterTwoMinutes` (time-limits.md) |
| TIME-12 | `Time_PageKill_DisablesAndTicks` (time-limits.md) |
| TIME-13 | `Time_Alive_PidAndStartTime` (time-limits.md), `Adopt_OnlyCmdWithMatchingStartTime` (workers.md) |
| TIME-14 | `Time_Result_LastLaneResultLine` (time-limits.md) |
| TIME-15 | `Time_Quota_DetectedFromNonModelLines` (time-limits.md), `Pool_QuotaPause_PerAgent` (weighted-effort.md) |
| TIME-16 | `Time_ServerDown_NoResult` (time-limits.md) |
| TIME-17 | `Time_NoLog_NonResume_CountsRetry`, `Time_CardNeverClaims_EveryRunCounts`, `Time_Retry_EveryRunWithNoResultCounts`, `Time_Retry_RunsOutsideTheCardDoNotCount`, `Time_Retry_ResumeRunNotCountedTwice` (time-limits.md) |
| TIME-18 | `Time_EndReason_Mapping` (time-limits.md) |
| TIME-19 | `Time_ResultRetryBookkeeping`, `Time_RetryCount_ClampedAtZero` (time-limits.md) |
| TIME-20 | `Time_AfterFinish_Bookkeeping` (time-limits.md) |
| TIME-21 | `Dispatch_AllWorkersBusy_ThenOneFinishes_StartsNextTick` (dispatch.md), `Time_ProcessExit_NoticedWithin250ms` (time-limits.md) |
| TIME-22 | `Time_Retries_PersistAcrossRestart` (time-limits.md) |
| HLTH-1 | `Health_WantedAfterFinishedRunAndCheckMain_NotAtStartup` (health.md), `Health_CheckMainSameSha_RunsAgain` (health.md), `Health_NoStartupCheck_OthersPushesUnchecked` (health.md) |
| HLTH-2 | `Health_NotStarted_Conditions` (health.md), `Health_CheckMainSameSha_RunsAgain` (health.md) |
| HLTH-3 | `Health_RunsInHealthWorktreeWithLog` (health.md) |
| HLTH-4 | `Health_HangingCheck_KilledAfterMaxMinutesTreatedRed` (health.md), `Health_KillLimit_UsesMaxMinutes` (health.md) |
| HLTH-5 | `Health_Green_ResumesFromPausedRed` (health.md), `Health_Red_PausesOnlyFromRunning` (health.md), `Health_RedMidRun_RunningWorkersContinueNoNewStarts` (health.md), `Health_ResumeOverridesRed` (health.md), `Pool_RedMainInOneProject` (weighted-effort.md) |
| HLTH-6 | `Health_RedBanner_WithShaAndLogLink` (health.md) |
| HLTH-7 | `Health_SchedulerDoesNotRunGates` (health.md), `Workers_Prompt_ContainsEveryPart` (workers.md) |
| HLTH-8 | `Health_NewCheck_EndsPreviousBeforeBuilding` (health.md) |
| HLTH-9 | `Health_StartAfterRestart_EndsLeftoverCheck` (health.md) |
| HLTH-10 | `Health_OnlyFileLocks_RetriedOnceThenCouldNotRun` (health.md), `Health_RealFailureWithLocks_StaysRedAndPausedRedKept` (health.md) |
| HLTH-11 | `Health_PastTimeBox_WarnsOnce` (health.md), `Health_InsideTimeBox_DoesNotWarn` (health.md), `Health_WarnMinutesZero_TurnsWarningOff` (health.md), `Health_Ended_ResultLineCarriesDuration` (health.md), `Health_KilledThenNewCheck_WarnsAgain` (health.md) |
| ACCT-1 | `Acct_Row_FormatAndHeader` (accounting.md), `Acct_Row_QuoteOrCommaInFields` (accounting.md) |
| ACCT-2 | `Acct_ModelColumn_KeyElseWorkerElseConfig` (accounting.md) |
| ACCT-3 | `Acct_ModelKey_Resolution` (accounting.md) |
| ACCT-4 | `Acct_Startup_OldHeaderRewritten_Last48hLoaded` (accounting.md) |
| ACCT-5 | `Acct_SeedRecent_LastTenWithLogs` (accounting.md) |
| ACCT-6 | `Acct_CardStats_24hByCardExclusions` (accounting.md), `Acct_CardStats_PerModelLines_WhenTwoModels` (accounting.md) |
| ACCT-7 | `Acct_ServerMetrics_SampledAndLine` (accounting.md) |
| ACCT-8 | `Acct_ServerUp_TimeoutCacheAndLogging` (accounting.md), `Dispatch_ServerUp_CachedFor20Seconds` (dispatch.md) |
| ACCT-9 | `Acct_HostLoad_Sampled` (accounting.md) |
| ADOPT-1 | `Adopt_RunningJson_WrittenEveryTick` (workers.md) |
| ADOPT-2 | `Adopt_OnlyCmdWithMatchingStartTime` (workers.md), `Adopt_SlotAboveCount_RaisesCount` (workers.md), `Adopt_StopDeadlineCarriesOver_QuietFromLogWrite` (workers.md) |
| ADOPT-3 | `Adopt_StatusTxt_Lines` (workers.md) |
| MOVE-1 | `Moves_AllCommits_ViaAnswersWorktree_RetryThreeTimes` (claims-and-moves.md) |
| MOVE-2 | `Moves_Answer_LineAndNote` (claims-and-moves.md), `Moves_Answer_DestinationMovesCard` (claims-and-moves.md), `Moves_Answer_LastOpenQuestion_ActiveBecomesReady` (claims-and-moves.md), `Moves_Answer_AlreadyAnswered_IsError` (claims-and-moves.md), `Moves_Answer_OnlyThisRoundDecidesDestination` (claims-and-moves.md) |
| MOVE-3 | `Moves_SetStatus_ReplacesFirstAndDropsContinuations` (claims-and-moves.md) |
| MOVE-4 | `Moves_DeveloperUncommittedEdit_Replayed3Way` (claims-and-moves.md), `Moves_DeveloperAppendOnlyEdit_AppendedWhen3WayFails` (claims-and-moves.md), `Moves_DeveloperConflictingEdit_ErrorNothingCommitted` (claims-and-moves.md) |
| MOVE-5 | `Moves_JournalLine_AppendedAtEnd` (claims-and-moves.md) |
| MOVE-6 | `Moves_Done_ReviewCardsOnly` (claims-and-moves.md) |
| MOVE-7 | `Moves_ResumeCard_EachSource` (claims-and-moves.md), `Moves_ResumeQueueCard_RetriesNotCleared` (claims-and-moves.md) |
| MOVE-8 | `Moves_RetryAndResumeState_ClearRetries` (claims-and-moves.md), `Health_ResumeOverridesRed` (health.md) |
| MOVE-9 | `Moves_OpenCard_RepositoryCopyElseAnswers` (claims-and-moves.md) |
| MOVE-10 | `Moves_WorkerSettings_ValidatedAndCommitted` (claims-and-moves.md) |
| MOVE-11 | `Moves_DeveloperConflictingEdit_ErrorNothingCommitted` (claims-and-moves.md), `Moves_PageError_NoticeTenMinutesOrUntilSuccess` (claims-and-moves.md) |
| PLAN-1 | `Planner_Enabled_OnlyWithFetchRepoAndOutbox_OffByDefault` (planner.md) |
| PLAN-2 | `Planner_Step_AtMostOncePerMinute_FetchOnly` (planner.md), `Planner_Batches_CompleteOnlyInNameOrder` (planner.md) |
| PLAN-3 | `Planner_Validation_Failures` (planner.md) |
| PLAN-4 | `Planner_Write_New` (planner.md), `Planner_Write_BaseMatchesBlob_KeepsLineEndings` (planner.md), `Planner_Write_MovedSinceBase_FollowsSameName` (planner.md), `Planner_Write_Merge_AppendOrFail` (planner.md) |
| PLAN-5 | `Planner_Moves_And_Removes` (planner.md) |
| PLAN-6 | `Planner_Commit_MessageTrailersThreeTries` (planner.md) |
| PLAN-7 | `Planner_Release_ReadyCardsRetriesCleared` (planner.md) |
| PLAN-8 | `Planner_BatchFolder_SentOrFailedWithClash` (planner.md), `Planner_FailedBatch_ReportedAsSent` (planner.md) |
| PAGE-1 | `Page_ServerThread_SnapshotsAndPostQueue` (page.md), `Page_RemotePost_WithoutRemoteControl_403` (page.md) |
| PAGE-2 | `Page_SnapshotCadence` (page.md) |
| PAGE-3 | `Page_LiveJson_RateAndShape` (page.md) |
| PAGE-4 | `Page_ClickTick_Debounced` (page.md) |
| PAGE-5 | `Page_Windows_Present` (page.md), `Page_WaitingOnDevInActive_ShownTwice` (page.md) |
| PAGE-6 | `Page_QueueOrder_DepsAndCycle` (page.md), `Page_WaitingOnDevInActive_ShownTwice` (page.md) |
| PAGE-7 | `Page_RemotePost_WithoutRemoteControl_403` (page.md) |
| PAGE-8 | `Page_StateText` (page.md) |
| PAGE-9 | `Page_Routes_GetAndPost` (page.md), `Page_ResumeInDraining` (page.md), `Page_SlotsAddRemove_ApiOnly` (page.md), `Page_LogFile_CardIdOfAnyWidth` (page.md) |
| PAGE-10 | `Page_RenderEvents_CopilotAndClaude` (page.md) |
| PAGE-11 | `Page_Themes_OrderAndExtends` (page.md) |
| PAGE-12 | `Page_Exit_DrainingWithNoWorkers` (page.md) |
| PAGE-13 | `Page_LiveFeed_NumberedLinesAndSince` (page.md) |
| MP-1 | `Pool_TopLevelConfig_ProjectsKeepOwnRepoAndJobs` (weighted-effort.md) |
| MP-2 | `Pool_AnyWorkerTakesAnyProjectsCard` (weighted-effort.md) |
| MP-3 | `Pool_ThreadsBudget_CoresOverPoolSize` (weighted-effort.md) |
| MP-4 | `Pool_ProjectWeights_Parsed` (weighted-effort.md) |
| MP-5 | `Pool_OnlyProjectsWithRunnableCardTakePart` (weighted-effort.md), `Pool_NoProjectHasRunnableCard_WorkerStaysIdle` (weighted-effort.md) |
| MP-6 | `Pool_FurthestBelowShare_GetsWorker` (weighted-effort.md), `Pool_IdleProjectShare_GoesToOthers` (weighted-effort.md), `Pool_ProjectWithNoMinutes_WinsOutright` (weighted-effort.md), `Pool_WindowSlides` (weighted-effort.md), `Pool_HighPriorityCard_VsWeights` (weighted-effort.md) |
| MP-7 | `Pool_HighPriorityCard_VsWeights` (weighted-effort.md), `Pool_OrphanResume_VsWeights` (weighted-effort.md), `Pool_WithinProject_SelectionAsToday` (weighted-effort.md) |
| MP-8 | `Pool_CombinedPage_FilterAndOneWaitingOnYou` (weighted-effort.md) |
| MP-9 | `Pool_PlannerHealthGates_PerProject` (weighted-effort.md), `Pool_RedMainInOneProject` (weighted-effort.md) |
| SC-1 | `Scope_CardNamesScope_Parsed` (scopes.md), `Scope_EmptyScope_Rejected` (scopes.md) |
| SC-2 | `Scope_NoOverlap_RunsSideBySide` (scopes.md), `Scope_SameFile_RunsAfter` (scopes.md), `Scope_FolderContainsFile_Overlaps` (scopes.md), `Scope_GlobOverlap_Rules` (scopes.md), `Scope_AnyProject_Overlap` (scopes.md) |
| SC-3 | `Scope_NoOverlap_RunsSideBySide` (scopes.md), `Scope_SameFile_RunsAfter` (scopes.md) |
| SC-4 | `Scope_NoOwnScope_TakesAreaDefault` (scopes.md), `Scope_AreaBriefInPrompt` (scopes.md), `Scope_HeldAreaWhenRedefined` (scopes.md) |
| SC-5 | `Scope_AllowedOverlap_AddOnlyFile` (scopes.md) |
| SC-6 | `Scope_RejectedPushAndConflict_Logged` (scopes.md) |
| SC-7 | `Scope_CheckedByScheduler_AtDispatch` (scopes.md), `Scope_OutOfScopeChange_Detected` (scopes.md) |
| DL-1 | `Lock_Resume_InitialClaimIncludesRememberedSet`, `Lock_Dispatch_UsesHeldSetsNotDeclaredScopes`, `Lock_YieldedCard_NotOfferedWhileRememberedEntryHeld`, `Lock_StartingCard_CountsAgainstCandidates_NoHeldSet`, `Lock_ClaimLanded_RecordsHeldSetAndAge`, `Lock_StartingCard_CannotRequestLock`, `Lock_ClaimFailed_EntryDropped` (dynamic-locks.md) |
| DL-2, DL-3 | none: planner practice and "reads need no lock" (no check of its own; DL-18 tests show a read is never refused) |
| DL-4, DL-5 | `Lock_FreePath_Granted`, `Lock_PathInsideOwnHeldSet_GrantedNoTableChange`, `Lock_OneOfTwoEntriesHeld_NeitherGranted`, `Lock_GlobVsFile_Conflicts`, `Lock_TwoRequestsOneCard_MergedBehindFirst`, `LockCommand_Granted_Exit0` (dynamic-locks.md) |
| DL-6 | `Lock_TooBroad_Refused`, `LockCommand_Refused_Exit4` (dynamic-locks.md) |
| DL-7 | `Lock_Dispatch_UsesHeldSetsNotDeclaredScopes` (dynamic-locks.md) |
| DL-8, DL-9 | `Lock_FinishApplied_ReleasedAndWaiterGranted`, `Lock_FinishNotPushed_StillHeld`, `Lock_ParkSendBackFailKill_ReleasedRemembered`, `Lock_WaitingRequest_GrantedWhenHolderReleases`, `LockRelease_OnApplied_Finish_Park_SendBack_RunEnd` (dynamic-locks.md) |
| DL-10 | `Lock_ParkSendBackFailKill_ReleasedRemembered`, `Lock_ParkedCard_RememberedSetNotHeld`, `Lock_YieldedCard_NotOfferedWhileRememberedEntryHeld`, `Lock_Resume_InitialClaimIncludesRememberedSet` (dynamic-locks.md) |
| DL-11 | `Lock_OlderRequester_Waits`, `Lock_AgeOrder_HighBeatsNormalThenLowerNumber`, `Lock_AgeFixedAtFirstClaim_SurvivesPriorityChange`, `Lock_ClaimLanded_RecordsHeldSetAndAge` (dynamic-locks.md) |
| DL-12 | `Lock_OlderRequester_Waits`, `Lock_YoungerRequester_Yields`, `Lock_ConflictWithSeveralHolders_YoungerThanAnyYields`, `LockCommand_WaitingThenGranted_AcrossTwoCalls`, `LockCommand_Yield_Exit3` (dynamic-locks.md) |
| DL-13 | `Lock_WaitLimit_TurnsToYield`, `Lock_WaitLimit_NeverPastWarnTime` (dynamic-locks.md) |
| DL-14 | `Lock_Yielded_GivesRunBack_NotStuck`, `Lock_YieldedCardInActive_IsOrphanFirstAmongItsPriority` (dynamic-locks.md) |
| DL-15 | `Lock_AddOnlyPath_NeverConflicts`, `PushCheck_ExemptPaths_Allowed`, `PushCheck_OtherJobsPaths_Refused` (dynamic-locks.md) |
| DL-16 | `PushCheck_AddOnlyFileWithDeletedLines_Warning` (dynamic-locks.md) |
| DL-17 | `PushCheck_Version_LowerOrEqualRefused` (dynamic-locks.md) |
| DL-18 | `PushCheck_HeldPath_Allowed_UnheldRefusedWithCommand`, `PushCheck_WipRef_NeverRefused`, `LockHooks_CommitMsg_AppendsCardTrailer`, `LockHooks_PrePush_RefusesUnheldAllowsWip`, `LockEnv_WorkerGetsCardExeAndHooks` (dynamic-locks.md) |
| DL-19 | `Audit_TrailerAndHeldPaths_Clean`, `Audit_UnheldPath_Stray`, `Audit_NoTrailerWhileWorkersRan_Stray`, `Audit_SchedulersOwnCommits_Ignored`, `Audit_StrayTurnsDoneIntoReview`, `LockAudit_RunsOnMainMove` (dynamic-locks.md) |
| DL-20 | `Table_RoundTrip`, `LockRequestStep_Applied_ReplyWrittenAfterTable` (dynamic-locks.md) |
| DL-21 | `Table_Unreadable_StartsEmptyWithLogLine`, `Table_Restart_KeepsAdoptedReleasesRest`, `Table_Restart_StartingCardReleasedNothingRemembered` (dynamic-locks.md) |
| DL-22 | `Lock_DifferentRepositories_NeverConflict`, `Lock_CardNotInSlot_Refused`, `Table_PerRepository_SharedByProjects` (dynamic-locks.md) |
| DL-23 | `Lock_WorkerType_NotConsidered` (dynamic-locks.md) |
| DL-24, DL-25 | `LockPage_WorkerWindowAndQueueRow_ShowHolds`, `LockPage_WaitingCard_ShowsWaitNote`, `LockPage_YieldedCard_ShowsYieldedNote` (dynamic-locks.md) |
| DL-26 | `LockPage_ProjectsWindow_ShowsDayCounts` (dynamic-locks.md) |
| DL-27 | `LockPrompt_ScopeMode_HasCommandAndYieldSteps`, `LockPrompt_SparkWorker_SameShortLiteralText`, `LockCommand_Output_OneLineAndExitCode`, `LockCommand_NoReply_PrintsAndExits2`, `LockCommand_Granted_Exit0` (dynamic-locks.md) |
| MOVE-12, CM-1..CM-21 | `Moves_Request_NamedAndParsed`, `Moves_Apply_TextStatusAndClaimed`, `Moves_Apply_OneCommitOnlyTheCardsPaths_PushedWithRetries`, `Moves_Requests_AppliedInFileNameOrder_AtStartDoneAndTick`, `Moves_Conflicts_FailedOrAppliedInPlace`, `Moves_WorkerThatMovesItself_StillWorks` (claims-and-moves.md) |
| REV-1..REV-5 | `Page_ReviewPanel_CommitsHealthFilesAndButtons` (page.md) |
| TREND-1..TREND-3 | `Trends_Daily_PerDayAndModel`, `Trends_HiddenUnderTwoDays`, `Trends_ReadFromAccountingCsv_ElseLoadedFigures` (accounting.md) |
| TLH-1..TLH-5 | `History_Window_Newest30Within30Days`, `History_TooFewRuns_NoFigure`, `History_MedianAndP90_Arithmetic`, `History_WorkerTypeAndModelMatch`, `History_QueueAdvice_WarnShortSuggestMissing` (time-limits.md) |
| HOOK-1..HOOK-4 | `Hook_Deliver_PrintsAndMovesFile`, `Hook_ClaudeWorker_GetsSettingsFileAndEnv`, `Hook_PromptWiring_HooksSetFromAgent` (workers.md; the last waits for 700-B) |
| TRAY-1..TRAY-8 | `Tray_Arguments_RepositoryThenSchedulerArgsAndNoBrowser`, `Tray_StartLine_PageAddressAndTooltip`, `Tray_Exit_StopAllWaitsThenKillsOnlyIfPageFails`, `Tray_RestartAndExitLeave_EndOnlySchedulerProcess` (config.md) |
| BUILD-1..BUILD-4 | `Build_ArtifactsLayout_AndPublish` (config.md, manual) |

## Test -> card

Each test is implemented by exactly one card of the port (card 003-D, plan
`docs/jobs/plans/scheduler-next-port_plan.md`). Cards are in `docs/jobs/queue/` until taken.

| Card | Tests |
|---|---|
| 010-A Core contracts: models, ports and text compares | `Config_CfgHelper_NullAndEmptyMeanDefault_ZeroAndFalseAreValues`, `Porting_TextCompares_AreCaseInsensitive` |
| 020-A Page contract: snapshot, actions and live events | `Page_SnapshotCadence`, `Page_ClickTick_Debounced` |
| 030-A Card files: path and fields | `Cards_PathPattern_FieldsAndId`, `Cards_NonMatchingFiles_Ignored`, `Cards_DoneFolder_NotOpened`, `Cards_Title_FirstHashSpaceLine`, `Cards_Status_FirstLineAndContinuations`, `Cards_MalformedCard_NoStatusLine_IsNotReady`, `Cards_Priority_Mapping_LastWins`, `Cards_FieldLinesInSections_AreIgnored`, `Cards_TimeBox_ParsedLastWins`, `Cards_WorkerRequest_TextBeforeParenthesis`, `Cards_DependsOn_AllIdsLastLineWins`, `Cards_DependsOn_IdsOfAnyWidth` |
| 040-B Git and reading cards from main | `Config_GitNeverPrompts`, `Cards_ReadFromRemoteMainOnly_NotWorkingTree`, `Cards_BlobCache_ReparsesOnlyChangedBlobs`, `Cards_LsTreeFails_KeepsPreviousList`, `Cards_DuplicateInQueue_RemovedFromMain`, `Cards_DuplicateOutsideQueue_LoggedOnceLeftAlone` |
| 050-C Web server and routes | `Page_ServerThread_SnapshotsAndPostQueue`, `Page_RemotePost_WithoutRemoteControl_403`, `Page_LiveJson_RateAndShape`, `Page_Routes_GetAndPost`, `Page_SlotsAddRemove_ApiOnly`, `Page_LogFile_CardIdOfAnyWidth` |
| 060-A Card state: questions, why, journal, waits and requests | `Cards_Fetch_AtMostEvery15Seconds`, `Cards_Questions_ParsedWithChoicesAndDestinations`, `Cards_Why_FromStatusOrResult`, `Cards_LatestJournal_GreatestDateTimeWins`, `Cards_ShortText_StripsAndTruncates`, `Cards_WaitsOnDev_Patterns`, `Cards_WaitsOnPlanner_Patterns`, `Cards_DepsDone_DoneOrReviewOnly`, `Cards_DependencyCycle_BothWaitForever`, `Cards_Requests_ReadAndOpenFlag` |
| 070-A Config model: defaults, overrides and agents | `Config_EmptyFile_AllDefaults`, `Config_JobsDirTrailingSlash_Trimmed`, `Config_SlotRootAlias_SameAsWorkspaceRoot`, `Config_CommandLineOverrides_OneRun`, `Config_SlotsZeroOrPortZero_DoNotOverride`, `Config_SlotsOnCommandLine_PinsCountOverWorkersJson`, `Config_DefaultAgentFormat_GuessedFromArgs`, `Config_NamedAgent_DefaultsAndFormatGuess`, `Config_AgentEndpoint_PresenceRecordedSeparatelyFromValue`, `Config_AgentOf_UnknownNameGivesDefault`, `Config_Expand_SubstitutesPlaceholders`, `Config_LaneNames_FromConfigUntilLanesJsonRead` |
| 080-B Config loading and the repository | `Config_RepoArgument_ResolvesToGitTopLevel`, `Config_NoRepoNoConfig_UsesCurrentDirectoryTopLevel`, `Config_NoRepoAnywhere_FailsWithNameTheRepository`, `Config_NonGitRepoArgument_IsError`, `Config_DefaultConfigPath_IsRepoDocsJobsSchedulerJson`, `Config_MissingConfigFile_IsFatal`, `Config_WithoutConfigArg_RereadsFromRemoteMain`, `Config_RemoteConfigInvalid_FallsBackToCheckoutWithReason`, `Config_RemoteBranchJobsDirFromCheckoutCopy`, `Config_ExplicitConfigFile_UsedAsIsNoFetch`, `Config_CfgRepo_UsedOnlyWhenNoRepoOnCommandLine` |
| 090-C Page views | `Health_RedBanner_WithShaAndLogLink`, `Moves_PageError_NoticeTenMinutesOrUntilSuccess`, `Page_Windows_Present`, `Page_StateText`, `Page_Themes_OrderAndExtends` |
| 100-A Categories and workers.json | `Lanes_ReadFromMain`, `Lanes_InvalidJsonOrNoLanesKey_PreviousKept`, `Lanes_ChangedWithPendingCards_HeldWithUpToSixIds`, `Lanes_SameDefinitionDifferentFormatting_NotAChange`, `Lanes_Overlaps_Warnings`, `Lanes_Overlaps_GlobOwnsNotSeenAsFolder`, `Lanes_Brief_InPrompt`, `Workers_JsonFromMain_ParsedAndLanesUpperCased`, `Workers_JsonUnchangedText_NotReapplied_InvalidKeepsPrevious`, `Workers_SlotsFromJson_NeverBelowHighestRunning`, `Workers_EffectiveWorker_EndpointModelMetrics`, `Workers_ThreadsPerWorker` |
| 110-B Slots, worktrees and job objects | `Workers_EnabledState_LocalAndPersisted`, `Workers_PrepareSlot_CreatesAndResetsWorktree`, `Workers_ClearSlotFiles_BeforeStart`, `Workers_JobObject_EndingKillsWholeTree`, `Time_Kill_EndsTreeKeepsFirstReason` |
| 120-A Dispatch: orphans and queued candidates | `Dispatch_EmptyQueue_NothingStartedNoNotes`, `Dispatch_FinishedAndBusyLanes`, `Dispatch_Orphan_ResumedBeforeQueuedSamePriority`, `Dispatch_HighPriorityQueued_BeatsNormalOrphan`, `Dispatch_ActiveNeedsHuman_NotAnOrphan`, `Dispatch_OrphanInBusyLane_SkippedSilently`, `Dispatch_ActiveWaitingOrBlocked_NotOrphanDoesNotHoldLane`, `Dispatch_OrphanInHeldLane_NotResumed`, `Dispatch_LowPriorityOrphan_WaitsBehindHigherPriorityQueued`, `Dispatch_LowPriorityOrphan_QueuedCardNotReadyOrBlocked_DoesNotBlock`, `Dispatch_LowPriorityOrphan_BehindStuckQueued`, `Dispatch_StuckOrphan_NotResumedListedWithRetry`, `Dispatch_OrphanRetriesOneBelowMax_Resumed`, `Dispatch_QueuedOrder_PrioNumLane`, `Dispatch_QueuedNotReady_NoteIsStatus`, `Dispatch_QueuedStuck_RetriesAtMax`, `Dispatch_QueuedChecksInOrder_DepsThenBusyThenHeld`, `Dispatch_OneCandidatePerLanePerTick` |
| 130-C Accounting rows and statistics (Core) | `Acct_Row_FormatAndHeader`, `Acct_Row_QuoteOrCommaInFields`, `Acct_ModelColumn_KeyElseWorkerElseConfig`, `Acct_ModelKey_Resolution`, `Acct_CardStats_24hByCardExclusions`, `Acct_CardStats_PerModelLines_WhenTwoModels`, `Acct_ServerMetrics_SampledAndLine` |
| 140-B State files, accounting.csv and samplers | `Lanes_NewLane_Accepted`, `Lanes_ChangedNoPendingCards_AcceptedAndLogged`, `Acct_Startup_OldHeaderRewritten_Last48hLoaded`, `Acct_SeedRecent_LastTenWithLogs`, `Acct_ServerUp_TimeoutCacheAndLogging`, `Acct_HostLoad_Sampled` |
| 150-A Dispatch: slots, worker fit and notes | *Retired with the slot dispatcher (939-A, 960-D); see the DISP-15 to DISP-19 rows.* Still current: `Dispatch_ServerUp_CachedFor20Seconds` (ServerCacheTests.cs), `Dispatch_LocksLineIgnored` |
| 160-A Starting a worker: prompt, environment, command line | `Workers_Environment_OtherAgentsVarsRemovedOwnSet`, `Workers_ContextLength_FromServerMinusN`, `Workers_LogAndPromptFileNames`, `Workers_Prompt_ContainsEveryPart`, `Workers_Prompt_WebOn_NoNoWebText`, `Workers_WebOff_ArgsPerAgent`, `Workers_CommandLine_QuotingAndPause`, `Adopt_SlotAboveCount_RaisesCount`, `Adopt_StopDeadlineCarriesOver_QuietFromLogWrite` |
| 170-A Watchdog: limits, warnings, stalls and stops | `Time_Limit_TimeBoxElseMax`, `Time_WarnLead_MinOfWarnAndQuarter`, `Time_Warn_WrittenOnceInWindow`, `Time_Warn_TinyLimit_NeverWritten`, `Time_Warn_NotWhenStopping`, `Time_LimitExactlyReached_StopRequestedNoWarn`, `Time_JustUnderLimit_NoStop`, `Time_LimitPlusGraceExactly_Killed`, `Time_OverLimitPlusGrace_KilledEvenWithLongerPageGrace`, `Time_OverLimitAndQuiet_OnlyTimeLimitReason`, `Time_Progress_IsRawLogSizeChange`, `Time_Stall_ExactlyStallMinutes_StopRequested`, `Time_StopGrace_KillAfterGrace`, `Time_SecondStopRequest_OnlyShortensGrace`, `Time_KillFails_AbandonedAfterTwoMinutes` |
| 180-A Run results and retries | `Time_Alive_PidAndStartTime`, `Time_Result_LastLaneResultLine`, `Time_Quota_DetectedFromNonModelLines`, `Time_ServerDown_NoResult`, `Time_NoLog_NonResume_CountsRetry`, `Time_EndReason_Mapping`, `Time_ResultRetryBookkeeping`, `Time_StoppedWithZeroEntry_GoesNegative`, `Time_CardNeverClaims_RetriedWithoutLimit` |
| 190-C Health check decisions (Core) | `Health_WantedAfterFinishedRunAndCheckMain_NotAtStartup`, `Health_NotStarted_Conditions`, `Health_CheckMainSameSha_RunsAgain`, `Health_HangingCheck_KilledAfterMaxMinutesTreatedRed`, `Health_KillLimit_UsesMaxMinutes`, `Health_Green_ResumesFromPausedRed`, `Health_Red_PausesOnlyFromRunning`, `Health_RedMidRun_RunningWorkersContinueNoNewStarts`, `Health_ResumeOverridesRed`, `Health_NoStartupCheck_OthersPushesUnchecked`, `Health_SchedulerDoesNotRunGates` |
| 200-A Claims and card moves | `Moves_Answer_LineAndNote`, `Moves_Answer_DestinationMovesCard`, `Moves_Answer_LastOpenQuestion_ActiveBecomesReady`, `Moves_Answer_AlreadyAnswered_IsError`, `Moves_SetStatus_ReplacesFirstAndDropsContinuations`, `Moves_JournalLine_AppendedAtEnd`, `Moves_Done_ReviewCardsOnly`, `Moves_ResumeCard_EachSource`, `Moves_ResumeQueueCard_RetriesNotCleared`, `Moves_RetryAndResumeState_ClearRetries` |
| 210-A Planner outbox | `Planner_Enabled_OnlyWithFetchRepoAndOutbox_OffByDefault`, `Planner_Batches_CompleteOnlyInNameOrder`, `Planner_Validation_Failures`, `Planner_Write_New`, `Planner_Write_BaseMatchesBlob_KeepsLineEndings`, `Planner_Write_MovedSinceBase_FollowsSameName`, `Planner_Write_Merge_AppendOrFail`, `Planner_Moves_And_Removes`, `Planner_Release_ReadyCardsRetriesCleared`, `Planner_FailedBatch_ReportedAsSent` |
| 220-C Page model: queue order and events (Core) | `Page_QueueOrder_DepsAndCycle`, `Page_WaitingOnDevInActive_ShownTwice`, `Page_ResumeInDraining`, `Page_RenderEvents_CopilotAndClaude` |
| 230-A The tick | `Dispatch_AllWorkersBusy_ThenOneFinishes_StartsNextTick`, `Dispatch_StartWorkerFails_CardNotRestartedSameTick` |
| 240-B Worker processes and adoption | `Adopt_RunningJson_WrittenEveryTick`, `Adopt_OnlyCmdWithMatchingStartTime`, `Adopt_StatusTxt_Lines`, `Time_AfterFinish_Bookkeeping`, `Time_ProcessExit_NoticedWithin250ms`, `Time_Retries_PersistAcrossRestart` |
| 250-B Card moves in git | `Moves_AllCommits_ViaAnswersWorktree_RetryThreeTimes`, `Moves_DeveloperUncommittedEdit_Replayed3Way`, `Moves_DeveloperAppendOnlyEdit_AppendedWhen3WayFails`, `Moves_DeveloperConflictingEdit_ErrorNothingCommitted`, `Moves_OpenCard_RepositoryCopyElseAnswers` |
| 260-B Planner outbox in git | `Planner_Step_AtMostOncePerMinute_FetchOnly`, `Planner_Commit_MessageTrailersThreeTries`, `Planner_BatchFolder_SentOrFailedWithClash` |
| 270-B The program: start-up, single instance, listen, health runner, exit | `Config_SecondScheduler_SameProject_FailsAfterWait`, `Config_AbandonedMutex_CountsAsOwned`, `Config_StartupOrder_AdoptSeedListenLog`, `Config_ListenLan_FallsBackToLocalhostWithReason`, `Config_ListenLocalhostFails_IsFatal`, `Dispatch_DryRun_LogsAndExitsAfterFirstTick`, `Health_RunsInHealthWorktreeWithLog`, `Page_Exit_DrainingWithNoWorkers` |
| 280-B Page actions wired to the host | `Config_Version_ShownOnPageAndLog`, `Lanes_AcceptFromPage_ReleasesLane`, `Workers_PageMessage_AppendedToFile`, `Time_PageStop_DisablesSlot_StopAllDrains`, `Time_PageKill_DisablesAndTicks`, `Moves_WorkerSettings_ValidatedAndCommitted` |
| 300-A Pool: weighted effort | `Pool_AnyWorkerTakesAnyProjectsCard`, `Pool_ThreadsBudget_CoresOverPoolSize`, `Pool_ProjectWeights_Parsed`, `Pool_OnlyProjectsWithRunnableCardTakePart`, `Pool_NoProjectHasRunnableCard_WorkerStaysIdle`, `Pool_FurthestBelowShare_GetsWorker`, `Pool_IdleProjectShare_GoesToOthers`, `Pool_ProjectWithNoMinutes_WinsOutright`, `Pool_WindowSlides`, `Pool_HighPriorityCard_VsWeights`, `Pool_OrphanResume_VsWeights`, `Pool_WithinProject_SelectionAsToday` |
| 310-A Pool: per-project rules | `Pool_PlannerHealthGates_PerProject`, `Pool_RedMainInOneProject`, `Pool_QuotaPause_PerAgent`, `Pool_WorkerRequest_Kept`, `Pool_MaxRetriesAndLimits_Source` |
| 320-A Scopes instead of categories | `Scope_CardNamesScope_Parsed`, `Scope_NoOverlap_RunsSideBySide`, `Scope_SameFile_RunsAfter`, `Scope_FolderContainsFile_Overlaps`, `Scope_GlobOverlap_Rules`, `Scope_AnyProject_Overlap`, `Scope_NoOwnScope_TakesAreaDefault`, `Scope_AreaBriefInPrompt`, `Scope_AllowedOverlap_AddOnlyFile`, `Scope_CheckedByScheduler_AtDispatch`, `Scope_HeldAreaWhenRedefined`, `Scope_EmptyScope_Rejected` |
| 330-B Pool and scope host parts | `Pool_TopLevelConfig_ProjectsKeepOwnRepoAndJobs`, `Scope_RejectedPushAndConflict_Logged`, `Scope_OutOfScopeChange_Detected` |
| 340-C Combined page | `Pool_CombinedPage_FilterAndOneWaitingOnYou` |
| 390-B Side-by-side run, observe only | none (roadmap item 1: the day beside the old scheduler) |
