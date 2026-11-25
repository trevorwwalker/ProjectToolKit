// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;

namespace Scheduler.Core.Tests.Launch;

/// <summary>
/// What a worker without web access is started with (WRK-11; <c>$NoWebArgs</c> L201,
/// <c>$ClaudeNoWebArgs</c> L241, <c>Start-Worker</c> L1823-1826). Test plan: test-plans/workers.md.
/// </summary>
public class WorkerArgsTests
{
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";

    private const string CfgJson = """
        {"worker":{"cli":"copilot","args":"-p {prompt} --output-format json"},
         "agents":{
           "cop":{"cli":"copilot","args":"-p {prompt} --output-format json"},
           "cl":{"cli":"claude-code","args":"-p {prompt} --output-format stream-json"},
           "other":{"cli":"mycli","args":"-p {prompt}"}}}
        """;

    private static SchedulerConfig Load(string json) => SchedulerConfigLoader.Load(json, Repo, LocalAppData);

    // WRK-11 (L201-202, L241, L1823-1826): web off, a Copilot worker loses its web tools and the
    // download commands, a Claude worker loses its web tools and the same commands, and any other
    // command line is left alone because these switches are not its own.
    [Fact]
    public void Workers_WebOff_ArgsPerAgent()
    {
        var cfg = Load(CfgJson);

        var copilot = WorkerArgs.For(cfg, Agents.AgentOf(cfg, "cop"), jobs: 8, model: "m1", endpoint: "", pointer: "P", web: false);
        Assert.Contains("--excluded-tools=web_fetch,web_search", copilot);
        Assert.Contains(
            copilot,
            a => a.StartsWith("--deny-tool=", StringComparison.Ordinal)
                && a.Contains("shell(curl:*)", StringComparison.Ordinal)
                && a.Contains("shell(curl.exe:*)", StringComparison.Ordinal)
                && a.Contains("shell(wget:*)", StringComparison.Ordinal)
                && a.Contains("shell(Invoke-WebRequest:*)", StringComparison.Ordinal)
                && a.Contains("shell(iwr:*)", StringComparison.Ordinal)
                && a.Contains("shell(Invoke-RestMethod:*)", StringComparison.Ordinal)
                && a.Contains("shell(irm:*)", StringComparison.Ordinal)
                && a.Contains("shell(Start-BitsTransfer:*)", StringComparison.Ordinal));

        var claude = WorkerArgs.For(cfg, Agents.AgentOf(cfg, "cl"), jobs: 8, model: "m1", endpoint: "", pointer: "P", web: false);
        Assert.Contains("--disallowedTools", claude);
        Assert.Contains(
            claude,
            a => a == "WebFetch,WebSearch,Bash(curl:*),Bash(wget:*),Bash(Invoke-WebRequest:*),Bash(iwr:*),"
                + "Bash(Invoke-RestMethod:*),Bash(irm:*)");

        // It is the Copilot command that takes --excluded-tools, not any command named copilot-ish.
        var other = WorkerArgs.For(cfg, Agents.AgentOf(cfg, "other"), jobs: 8, model: "m1", endpoint: "", pointer: "P", web: false);
        Assert.DoesNotContain("--excluded-tools=web_fetch,web_search", other);
        Assert.DoesNotContain("--disallowedTools", other);

        // Web on: nothing is taken away.
        Assert.DoesNotContain(
            "--excluded-tools=web_fetch,web_search",
            WorkerArgs.For(cfg, Agents.AgentOf(cfg, "cop"), jobs: 8, model: "m1", endpoint: "", pointer: "P", web: true));
    }
}
