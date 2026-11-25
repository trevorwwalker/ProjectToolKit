// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Cards;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;

namespace Scheduler.Core.Tests.Launch;

/// <summary>
/// The log, view and prompt file names a worker is started with, and the pointer sentence that
/// keeps the whole brief off the command line (WRK-9; <c>Start-Worker</c> L1814-1822). Test plan:
/// test-plans/workers.md.
/// </summary>
public class WorkerFilesTests
{
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";
    private const string LogDir = @"C:\Users\dev\AppData\Local\Proj\scheduler";
    private const string Jobs = "docs/jobs";

    private static readonly DateTimeOffset Start = new(2026, 11, 18, 11, 20, 39, TimeSpan.Zero);

    private static SchedulerConfig Load(string json) => SchedulerConfigLoader.Load(json, Repo, LocalAppData);

    private static Card Card10A() =>
        CardFiles.Parse(Jobs, $"{Jobs}/active/010-A-core-contracts.md", "Status: ready")
        ?? throw new InvalidOperationException("not a card path");

    // WRK-9 (L1814-1822): <id>-<yyyyMMdd-HHmmss>.jsonl with the readable .log beside it for a json
    // format, .log alone for text, the brief in .prompt.txt, and only a pointer on the command line.
    [Fact]
    public void Workers_LogAndPromptFileNames()
    {
        var cfg = Load("""{"worker":{"cli":"copilot","args":"-p {prompt} --allow-all --no-ask-user"}}""")
            with { LogDir = LogDir };
        var card = Card10A();
        var agent = Agents.AgentOf(cfg, null);

        var json = WorkerFiles.Names(cfg, card, Start, AgentFormat.Claude);
        Assert.Equal(Path.Combine(LogDir, "010-A-20261118-112039.jsonl"), json.RawLog);
        Assert.Equal(Path.Combine(LogDir, "010-A-20261118-112039.log"), json.View);
        Assert.Equal(Path.Combine(LogDir, "010-A-20261118-112039.prompt.txt"), json.PromptFile);
        Assert.StartsWith($"You are a worker for {cfg.Project}. Your whole brief is in the file {json.PromptFile} :", json.Pointer);
        Assert.Contains("read all of it first (Get-Content -Raw -Encoding UTF8)", json.Pointer);
        Assert.Contains("Do not edit, move or commit that file.", json.Pointer);

        var text = WorkerFiles.Names(cfg, card, Start, AgentFormat.Text);
        Assert.Equal(Path.Combine(LogDir, "010-A-20261118-112039.log"), text.RawLog);
        Assert.Equal(text.RawLog, text.View);
        Assert.Equal(Path.Combine(LogDir, "010-A-20261118-112039.prompt.txt"), text.PromptFile);

        // Only the pointer sentence goes on the command line, never the brief, so cmd's 8191
        // characters are not used up by a long category brief.
        var args = WorkerArgs.For(cfg, agent, jobs: 8, model: "m1", endpoint: "", pointer: json.Pointer, web: true);
        Assert.Single(args, a => a.StartsWith("You are a worker for", StringComparison.Ordinal));
        var line = WorkerCommandLine.CommandLine("Proj worker 1 - 010-A-core-contracts", agent.Cli, args, json.RawLog);
        Assert.Contains(json.Pointer, line, StringComparison.Ordinal);
        Assert.True(line.Length < 8191, $"the command line is {line.Length} characters");
    }
}
