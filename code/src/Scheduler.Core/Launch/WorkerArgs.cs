// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Launch;

/// <summary>
/// The arguments a worker's command is started with (WRK-12, <c>Start-Worker</c> L1823): the
/// agent's arguments with the placeholders filled in and <c>{prompt}</c> replaced by the pointer
/// sentence, so the brief itself never reaches <c>cmd.exe</c>.
/// </summary>
public static class WorkerArgs
{
    /// <summary>What the Copilot CLI is given when the worker has no web (L201-202).</summary>
    public static IReadOnlyList<string> CopilotNoWeb { get; } =
    [
        "--excluded-tools=web_fetch,web_search",
        "--deny-tool=shell(curl:*),shell(curl.exe:*),shell(wget:*),shell(Invoke-WebRequest:*),"
        + "shell(iwr:*),shell(Invoke-RestMethod:*),shell(irm:*),shell(Start-BitsTransfer:*)",
    ];

    /// <summary>What Claude Code is given when the worker has no web (L241).</summary>
    public static IReadOnlyList<string> ClaudeNoWeb { get; } =
    [
        "--disallowedTools",
        "WebFetch,WebSearch,Bash(curl:*),Bash(wget:*),Bash(Invoke-WebRequest:*),Bash(iwr:*),"
        + "Bash(Invoke-RestMethod:*),Bash(irm:*)",
    ];

    /// <summary>
    /// What Codex is given when the worker has no web (AG-11): its search tool off. The value is
    /// written without quotes, which the Windows argument parser would strip anyway; Codex reads a
    /// value that is not TOML as a string. Downloads from its shell cannot be denied (known_gaps.md).
    /// </summary>
    public static IReadOnlyList<string> CodexNoWeb { get; } = ["-c", "web_search=disabled"];

    /// <summary>
    /// The agent's arguments, one element per argument. Only <c>{prompt}</c>, which comes from
    /// <paramref name="pointer"/>, and <c>{model}</c>, <c>{endpoint}</c>, <c>{jobs}</c> and
    /// <c>{project}</c> (CFG-12) are substituted; anything else is passed through as it is. With
    /// <paramref name="web"/> off, the agent that has web tools loses them and the ordinary
    /// download commands (WRK-11) - a command that is neither Copilot nor Claude is left alone,
    /// as those switches are not its own.
    /// </summary>
    public static IReadOnlyList<string> For(
        SchedulerConfig config,
        AgentDefinition agent,
        int jobs,
        string model,
        string endpoint,
        string pointer,
        bool web)
    {
        var list = new List<string>();
        foreach (var raw in agent.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            list.Add(Text.Same(raw, "{prompt}")
                ? pointer
                : Agents.Expand(config, raw, jobs, model, endpoint));
        }

        // WRK-14, job card 570-B: the hook that hands the developer's messages over after each tool call.
        if (MessageHook.Delivers(agent.Format))
        {
            list.Add("--settings");
            list.Add(WorkerFiles.HookSettings(config.LogDir));
        }

        if (!web)
        {
            if (agent.Format == AgentFormat.Claude)
            {
                list.AddRange(ClaudeNoWeb);
            }
            else if (agent.Format == AgentFormat.Codex)
            {
                list.AddRange(CodexNoWeb);   // AG-11
            }
            else if (Text.IsMatch(agent.Cli, "copilot"))
            {
                list.AddRange(CopilotNoWeb);
            }
        }

        return list;
    }
}
