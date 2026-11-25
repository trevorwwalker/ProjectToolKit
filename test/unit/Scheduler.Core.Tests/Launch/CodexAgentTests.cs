// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;
using Scheduler.Core.PageModel;
using Scheduler.Core.Results;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Tests.Launch;

/// <summary>
/// The Codex CLI agent (agents.md section 2, AG-2 to AG-17), tested from the recorded runs in
/// <c>Fixtures/codex</c> (<c>ok</c>, <c>fail</c> and a synthetic <c>quota</c>), never only from
/// hand-written lines.
/// </summary>
public class CodexAgentTests
{
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";

    private static readonly string[] DefaultPatterns =
        ["usage limit", "rate limit", "too many requests", "insufficient_quota", "exceeded your current quota"];

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Launch", "Fixtures", "codex", name);

    private static string[] Fixture(string name) => File.ReadAllLines(FixturePath(name));

    private static int Exit(string name) => int.Parse(File.ReadAllText(FixturePath(name)).Trim());

    private static List<LiveEvent> Events(string[] lines) =>
        [.. lines.SelectMany(l => WorkerEvents.Render(AgentFormat.Codex, l))];

    // AG-2
    [Fact]
    public void Agents_Codex_FormatNameAndGuess()
    {
        Assert.Equal(AgentFormat.Codex, Agents.FormatOf("codex"));
        Assert.Equal(AgentFormat.Codex, Agents.FormatOf("Codex"));
        Assert.Equal(AgentFormat.Codex, Agents.GuessFormat("codex", "exec --json -m x {prompt}"));
        Assert.Equal(AgentFormat.Codex, Agents.GuessFormat("codex.cmd", "exec --json {prompt}"));
        Assert.Equal(AgentFormat.Text, Agents.GuessFormat("codex", "exec {prompt}"));
        Assert.Equal(AgentFormat.Claude, Agents.GuessFormat("claude", "-p --output-format stream-json"));

        var cfg = SchedulerConfigLoader.Load(
            """{"agents":{"cx":{"cli":"codex","args":"exec --json {prompt}","endpoint":""},"named":{"format":"codex","args":"x"}}}""",
            Repo, LocalAppData);
        Assert.Equal(AgentFormat.Codex, Agents.AgentOf(cfg, "cx").Format);
        Assert.Equal(AgentFormat.Codex, Agents.AgentOf(cfg, "named").Format);
    }

    // AG-10, AG-11, AG-13
    [Fact]
    public void Workers_Codex_WebOffAndNoHook()
    {
        var cfg = SchedulerConfigLoader.Load(
            """{"agents":{"cx":{"cli":"codex","args":"exec --json --dangerously-bypass-approvals-and-sandbox -m {model} {prompt}","endpoint":""}}}""",
            Repo, LocalAppData);
        var agent = Agents.AgentOf(cfg, "cx");

        var off = WorkerArgs.For(cfg, agent, jobs: 6, model: "gpt-x", endpoint: "", pointer: "P", web: false);
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", off);
        Assert.Equal(["-c", "web_search=disabled"], off.TakeLast(2));
        Assert.Contains("gpt-x", off);
        Assert.DoesNotContain("--settings", off);

        var on = WorkerArgs.For(cfg, agent, jobs: 6, model: "gpt-x", endpoint: "", pointer: "P", web: true);
        Assert.DoesNotContain("web_search=disabled", on);
        Assert.False(MessageHook.Delivers(AgentFormat.Codex));
    }

    // AG-17
    [Fact]
    public void Workers_Codex_CommandLineClosesStdin()
    {
        var codex = WorkerCommandLine.Arguments("t", "codex", ["exec", "--json", "P"], @"C:\l\a.jsonl", closeStdin: true);
        Assert.EndsWith("codex exec --json P < nul > \"C:\\l\\a.jsonl\" 2>&1", codex);

        var other = WorkerCommandLine.Arguments("t", "claude", ["-p", "P"], @"C:\l\a.jsonl");
        Assert.EndsWith("claude -p P > \"C:\\l\\a.jsonl\" 2>&1", other);
    }

    // AG-3, AG-4, AG-5, AG-6, AG-7: the recorded good run.
    [Fact]
    public void Codex_OkRun_RendersLikeTheOthers()
    {
        var events = Events(Fixture("ok.jsonl"));

        Assert.DoesNotContain(events, e => e.Mark == LiveMark.Error);
        var tools = events.Where(e => e.Mark == LiveMark.ToolCall).ToList();
        Assert.Equal(4, tools.Count);
        Assert.Equal("shell: Get-Content -LiteralPath notes.txt", tools[0].Text);
        Assert.Equal("shell: dotnet --no-such-switch", tools[1].Text);
        var failed = Assert.Single(events, e => e.Mark == LiveMark.FailedTool);
        Assert.Contains("Could not execute", failed.Text);
        Assert.Equal("LANE-RESULT: done", events.Last(e => e.Mark == LiveMark.Message).Text);
        var end = Assert.Single(events, e => e.Mark == LiveMark.SessionEnd);
        Assert.Contains("57491 tokens in, 220 out", end.Text);

        // TIME-14: the result is read from the model's own message lines only.
        var rendered = events.Where(e => e.Mark == LiveMark.Message).Select(e => "* " + e.Text);
        Assert.Equal("done", RunResults.ResultOf(rendered, rendered: true));
    }

    // AG-3: the stdin notice is ignored, any other non-JSON line is the CLI's own text.
    [Fact]
    public void Codex_NonJsonLines_NoticeIgnoredOthersShown()
    {
        Assert.Empty(WorkerEvents.Render(AgentFormat.Codex, "Reading additional input from stdin..."));
        var e = Assert.Single(WorkerEvents.Render(AgentFormat.Codex, "error: the CLI itself"));
        Assert.Equal(LiveMark.Error, e.Mark);

        var scan = WorkerEvents.ScanCodex("Reading additional input from stdin...\nsome text\n", new WorkerStats(), "");
        Assert.Equal(["some text"], scan.Lines);
    }

    // AG-3, AG-7: the recorded failure; the item error is a warning, not the session failure.
    [Fact]
    public void Codex_FailRun_WarningThenSessionFailed()
    {
        var events = Events(Fixture("fail.jsonl"));

        Assert.All(events, e => Assert.Equal(LiveMark.Error, e.Mark));
        Assert.Equal(3, events.Count);
        Assert.DoesNotContain("session failed", events[0].Text);
        Assert.StartsWith("session failed:", events[^1].Text);
        Assert.Contains("not supported", events[^1].Text);
    }

    // AG-14
    [Fact]
    public void Codex_OkRun_StatsFilled()
    {
        var stats = new WorkerStats();
        var scan = WorkerEvents.ScanCodex(string.Join("\n", Fixture("ok.jsonl")) + "\n", stats, "");

        Assert.Equal(1, stats.Calls);
        Assert.Equal(4, stats.Tools);
        Assert.Equal(1, stats.ToolFails);
        Assert.Equal(220, stats.GenTokens);
        Assert.DoesNotContain(scan.Lines, l => l.Contains("item.started", StringComparison.Ordinal));
        Assert.DoesNotContain(scan.Lines, l => l.StartsWith("Reading", StringComparison.Ordinal));
    }

    // AG-8: quota is read from the CLI's own error lines, never from the model's text.
    [Fact]
    public void Codex_Quota_FromErrorLinesOnly()
    {
        Assert.True(RunResults.QuotaHit(Fixture("quota.synthetic.jsonl"), DefaultPatterns));
        Assert.False(RunResults.QuotaHit(Fixture("ok.jsonl"), DefaultPatterns));
        Assert.False(RunResults.QuotaHit(Fixture("fail.jsonl"), DefaultPatterns));
        Assert.False(RunResults.QuotaHit(
            ["""{"type":"item.completed","item":{"type":"agent_message","text":"we hit the rate limit"}}"""], DefaultPatterns));
        Assert.True(RunResults.QuotaHit(
            ["""{"type":"turn.failed","error":{"message":"You've hit your usage limit."}}"""], DefaultPatterns));
    }

    // AG-9: a failed run with no result line crashed.
    [Fact]
    public void Codex_Exit_FailIsCrashed()
    {
        Assert.Equal(1, Exit("fail.exit"));
        Assert.Equal(0, Exit("ok.exit"));
        Assert.Equal(EndReasonKind.Crashed, RunResults.EndReasonOf("", Exit("fail.exit")).Kind);
    }
}
