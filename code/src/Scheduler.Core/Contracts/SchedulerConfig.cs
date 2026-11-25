// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>
/// scheduler.json with the command line applied (A1). Defaults are CFG-6's; the ones that
/// depend on the repository (Project, WorkspaceRoot, LogDir) are filled in by the loader.
/// </summary>
public sealed record SchedulerConfig
{
    /// <summary>The default quota patterns of <c>worker.quotaPatterns</c> (CFG-6).</summary>
    public static IReadOnlyList<string> DefaultQuotaPatterns { get; } =
        ["exceeded your (monthly )?quota", "quota exceeded", "rate limit"];

    /// <summary>The default quota patterns of a named agent (CFG-10).</summary>
    public static IReadOnlyList<string> DefaultAgentQuotaPatterns { get; } =
        ["usage limit", "rate limit", "Failed to authenticate"];

    /// <summary>The repository's top level (CFG-1, CFG-4).</summary>
    public string Repo { get; init; } = "";

    /// <summary>The project name; default the repository folder's name.</summary>
    public string Project { get; init; } = "";

    /// <summary>The jobs folder in the repository, without a trailing <c>/</c>.</summary>
    public string JobsDir { get; init; } = "jobs";

    /// <summary>The git remote.</summary>
    public string Remote { get; init; } = "origin";

    /// <summary>The main branch.</summary>
    public string Branch { get; init; } = "main";

    /// <summary>
    /// The worker count: how many workers may run at once in all (WT-C1, WT-1). It is the pool's
    /// <c>maxWorkers</c>, or the <c>slots</c> of scheduler.json or workers.json (CFG-6, CFG-8, WRK-2),
    /// or what <c>-Slots</c> pinned for this run.
    /// </summary>
    public int MaxWorkers { get; init; } = 2;

    /// <summary>
    /// The old name of <see cref="MaxWorkers"/>: the worktree folders, the page and accounting still
    /// speak of slots, and the folder number is the worker's number (WT-W3). Same value as
    /// <see cref="MaxWorkers"/>; it goes as the worker types replace the slots.
    /// </summary>
    public int Slots { get => MaxWorkers; init => MaxWorkers = value; }

    /// <summary><c>-Slots</c> was given: workers.json's <c>slots</c> and the pool's <c>maxWorkers</c> are ignored (CFG-8, WT-C1).</summary>
    public bool SlotsPinned { get; init; }

    /// <summary>The page's port.</summary>
    public int Port { get; init; } = 8765;

    /// <summary><c>localhost</c> or <c>lan</c> (CFG-17).</summary>
    public string Listen { get; init; } = "localhost";

    /// <summary>Other machines may use the page's buttons (PAGE-1, PAGE-7).</summary>
    public bool RemoteControl { get; init; }

    /// <summary>Time each tick's steps.</summary>
    public bool Profile { get; init; }

    /// <summary>Where the slot worktrees go; default the repository path plus <c>-workspaces</c> (alias <c>slotRoot</c>).</summary>
    public string WorkspaceRoot { get; init; } = "";

    /// <summary>The log folder; default <c>%LOCALAPPDATA%</c>, then the project, then <c>scheduler</c>.</summary>
    public string LogDir { get; init; } = "";

    /// <summary>The environment variable that carries threads per worker (WRK-7).</summary>
    public string JobsEnvVar { get; init; } = "AGENT_JOBS";

    /// <summary>The worker's window style.</summary>
    public string WorkerWindow { get; init; } = "Hidden";

    /// <summary><c>limits.pollSeconds</c>: seconds between ticks.</summary>
    public int PollSeconds { get; init; } = 15;

    /// <summary><c>limits.maxRetries</c> (DISP-7, DISP-11).</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary><c>limits.stallMinutes</c> (TIME-8).</summary>
    public int StallMinutes { get; init; } = 30;

    /// <summary><c>limits.maxMinutes</c> (TIME-1).</summary>
    public int MaxMinutes { get; init; } = 240;

    /// <summary><c>limits.stopGraceMinutes</c> (TIME-5, TIME-6, TIME-7).</summary>
    public int StopGraceMinutes { get; init; } = 20;

    /// <summary><c>limits.warnMinutes</c> (TIME-2).</summary>
    public int WarnMinutes { get; init; } = 20;

    /// <summary><c>limits.pageStopGraceMinutes</c> (TIME-10).</summary>
    public int PageStopGraceMinutes { get; init; } = 5;

    /// <summary><c>health.command</c>; "" means no health check (CFG-7).</summary>
    public string HealthCommand { get; init; } = "";

    /// <summary>
    /// <c>limits.healthWarnMinutes</c> (HLTH-11, 1100-B): how long a running health check may take before
    /// the scheduler says so once in the log and in the page's health line. 0 turns the warning off; it
    /// never stops or fails the check (that stays <see cref="MaxMinutes"/>'s).
    /// </summary>
    public int HealthWarnMinutes { get; init; } = 5;

    /// <summary>EST-2: the kinds a card's <c>Kind:</c> may name when <c>cardKinds</c> is missing or empty.</summary>
    public static IReadOnlyList<string> DefaultCardKinds { get; } = ["design", "implementation", "docs", "check"];

    /// <summary>EST-2: <c>cardKinds</c>, the project's kind words; the default four when missing, empty or not an array of words.</summary>
    public IReadOnlyList<string> CardKinds { get; init; } = DefaultCardKinds;

    /// <summary>Text added to every worker prompt (WRK-10).</summary>
    public string PromptExtra { get; init; } = "";

    /// <summary><c>worker.cli</c>.</summary>
    public string WorkerCli { get; init; } = "copilot";

    /// <summary><c>worker.args</c>.</summary>
    public string WorkerArgs { get; init; } = "-p {prompt} --allow-all --no-ask-user";

    /// <summary><c>worker.web</c>: workers may use the web (WRK-11).</summary>
    public bool WorkerWeb { get; init; }

    /// <summary><c>worker.model</c>, or <c>-Model</c>.</summary>
    public string Model { get; init; } = "";

    /// <summary><c>worker.endpoint</c>.</summary>
    public string Endpoint { get; init; } = "";

    /// <summary><c>worker.metricsUrl</c>.</summary>
    public string MetricsUrl { get; init; } = "";

    /// <summary><c>worker.quotaPatterns</c>.</summary>
    public IReadOnlyList<string> QuotaPatterns { get; init; } = DefaultQuotaPatterns;

    /// <summary><c>worker.env</c>: variables for the default agent, with placeholders (CFG-12, WRK-8).</summary>
    public IReadOnlyDictionary<string, string> WorkerEnv { get; init; } =
        new Dictionary<string, string>(Text.Comparer);

    /// <summary><c>cfg.agents</c> by name (CFG-10).</summary>
    public IReadOnlyDictionary<string, AgentDefinition> Agents { get; init; } =
        new Dictionary<string, AgentDefinition>(Text.Comparer);

    /// <summary><c>cfg.categories</c>: category names by letter, used until categories.json is read (CFG-13).</summary>
    public IReadOnlyDictionary<string, string> CategoryNames { get; init; } =
        new Dictionary<string, string>(Text.Comparer);

    /// <summary><c>page.theme</c> (PAGE-11).</summary>
    public string PageTheme { get; init; } = "";

    /// <summary><c>-DryRun</c> (DISP-21).</summary>
    public bool DryRun { get; init; }

    /// <summary><c>-Paused</c>: start in state paused (CFG-7).</summary>
    public bool StartPaused { get; init; }

    /// <summary><c>-Config</c> was given: used as is, never re-read from main (CFG-3).</summary>
    public bool ExplicitConfig { get; init; }
}
