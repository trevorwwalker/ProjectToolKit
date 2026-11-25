// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.Json.Nodes;
using Scheduler.Core.Categories;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;
using Scheduler.Core.PageModel;
using Scheduler.Core.Results;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Tests.Launch;

/// <summary>
/// The OpenCode agent (agents.md section 3, AG-2 to AG-19), tested from the recorded runs in
/// <c>Fixtures/opencode</c> (<c>ok</c>, <c>fail</c> and a synthetic <c>quota</c>), never only from
/// hand-written lines.
/// </summary>
public class OpenCodeAgentTests
{
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";
    private const string OpenCodeJson =
        """{"agents":{"oc":{"cli":"opencode","args":"run --format json --standalone --auto --archived -m local/{model} {prompt}","env":{"OPENCODE_CONFIG_CONTENT":"{\"permission\":{\"edit\":\"allow\",\"bash\":\"allow\",\"external_directory\":\"allow\"}}"}}}}""";

    private static readonly string[] DefaultPatterns =
        ["rate limit", "rate_limit", "usage limit", "insufficient_quota", "exceeded your current quota"];

    private sealed class FakeLog : ISchedulerLog
    {
        public void Write(string line)
        {
        }
    }

    private sealed class FakeProbe : IServerProbe
    {
        public bool IsUp(string endpoint) => true;

        public string? ReadModels(string endpoint) => null;

        public string? ReadMetrics(string metricsUrl) => null;
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Launch", "Fixtures", "opencode", name);

    private static string[] Fixture(string name) => File.ReadAllLines(FixturePath(name));

    private static int Exit(string name) => int.Parse(File.ReadAllText(FixturePath(name)).Trim());

    private static List<LiveEvent> Events(string[] lines) =>
        [.. lines.SelectMany(l => WorkerEvents.Render(AgentFormat.OpenCode, l))];

    private static WorkerEnvironment Env(bool web, string? worktree = null)
    {
        var json = web ? OpenCodeJson.Replace("""{"agents":""", """{"worker":{"web":true},"agents":""") : OpenCodeJson;
        var cfg = SchedulerConfigLoader.Load(json, Repo, LocalAppData);
        return WorkerEnv.Build(cfg, Agents.AgentOf(cfg, "oc"), new EffectiveWorker { Agent = "oc", Model = "m", Endpoint = "http://s/v1" },
            jobs: 4, slot: 1, processEnv: null, new FakeProbe(), new FakeLog(), worktree: worktree);
    }

    // AG-2
    [Fact]
    public void Agents_OpenCode_FormatNameAndGuess()
    {
        Assert.Equal(AgentFormat.OpenCode, Agents.FormatOf("opencode"));
        Assert.Equal(AgentFormat.OpenCode, Agents.FormatOf("OpenCode"));
        Assert.Equal(AgentFormat.OpenCode, Agents.GuessFormat("opencode", "run --format json -m x {prompt}"));
        Assert.Equal(AgentFormat.OpenCode, Agents.GuessFormat("opencode.cmd", "run --format=json {prompt}"));
        Assert.Equal(AgentFormat.Text, Agents.GuessFormat("opencode", "run {prompt}"));
        Assert.Equal(AgentFormat.Codex, Agents.GuessFormat("codex", "exec --json {prompt}"));

        var cfg = SchedulerConfigLoader.Load(
            """{"agents":{"oc":{"cli":"opencode","args":"run --format json {prompt}"},"named":{"format":"opencode","args":"x"}}}""",
            Repo, LocalAppData);
        Assert.Equal(AgentFormat.OpenCode, Agents.AgentOf(cfg, "oc").Format);
        Assert.Equal(AgentFormat.OpenCode, Agents.AgentOf(cfg, "named").Format);
    }

    // AG-10, AG-13, AG-18: the command line and no message hook.
    [Fact]
    public void Workers_OpenCode_ArgsAndNoHook()
    {
        var cfg = SchedulerConfigLoader.Load(OpenCodeJson, Repo, LocalAppData);
        var args = WorkerArgs.For(cfg, Agents.AgentOf(cfg, "oc"), jobs: 4, model: "qwen", endpoint: "http://s/v1", pointer: "P", web: false);

        Assert.Equal(["run", "--format", "json", "--standalone", "--auto", "--archived", "-m", "local/qwen", "P"], args);
        Assert.False(MessageHook.Delivers(AgentFormat.OpenCode));
    }

    // AG-17
    [Fact]
    public void Workers_OpenCode_CommandLineClosesStdin()
    {
        var line = WorkerCommandLine.Arguments("t", "opencode", ["run", "--format", "json", "P"], @"C:\l\a.jsonl", closeStdin: true);
        Assert.EndsWith("opencode run --format json P < nul > \"C:\\l\\a.jsonl\" 2>&1", line);
    }

    // AG-19
    [Fact]
    public void Workers_OpenCode_PwdIsTheWorktree()
    {
        Assert.Equal(@"C:\work\slot1", Env(web: true, worktree: @"C:\work\slot1").Set["PWD"]);
        Assert.False(Env(web: true).Set.ContainsKey("PWD"));
        Assert.Equal("1", Env(web: true).Set["OPENCODE_DISABLE_CLAUDE_CODE"]);
    }

    // AG-10, AG-11: the permission config, with the web denied when the worker has none.
    [Fact]
    public void Workers_OpenCode_PermissionsDenyWebWhenOff()
    {
        var on = JsonNode.Parse(Env(web: true).Set["OPENCODE_CONFIG_CONTENT"])!["permission"]!;
        Assert.Equal("allow", (string?)on["bash"]);
        Assert.Null(on["webfetch"]);

        var off = JsonNode.Parse(Env(web: false).Set["OPENCODE_CONFIG_CONTENT"])!["permission"]!;
        Assert.Equal("deny", (string?)off["webfetch"]);
        Assert.Equal("deny", (string?)off["websearch"]);
        Assert.Equal("allow", (string?)off["edit"]);
        Assert.Equal("allow", (string?)off["external_directory"]);
        Assert.Equal("allow", (string?)off["bash"]!["*"]);
        foreach (var command in new[] { "curl", "wget", "Invoke-WebRequest", "iwr", "Invoke-RestMethod", "irm" })
        {
            Assert.Equal("deny", (string?)off["bash"]![command + " *"]);
        }
    }

    [Fact]
    public void OpenCodePermissions_KeepsRulesAndStartsFromNothing()
    {
        var kept = JsonNode.Parse(OpenCodePermissions.WithoutWeb("""{"model":"m","permission":{"bash":{"rm *":"deny"}}}"""))!;
        Assert.Equal("m", (string?)kept["model"]);
        Assert.Equal("deny", (string?)kept["permission"]!["bash"]!["rm *"]);
        Assert.Equal("allow", (string?)kept["permission"]!["bash"]!["*"]);

        foreach (var none in new[] { null, "", "not json", "[1]" })
        {
            var made = JsonNode.Parse(OpenCodePermissions.WithoutWeb(none))!;
            Assert.Equal("deny", (string?)made["permission"]!["webfetch"]);
        }
    }

    // AG-11: the recorded web-off run: the config's `bash` deny stopped `curl` in the `shell` tool.
    [Fact]
    public void OpenCode_WebOffRun_CurlDeniedUnderShellTool()
    {
        var events = Events(Fixture("webdeny.jsonl"));

        var tools = events.Where(e => e.Mark == LiveMark.ToolCall).ToList();
        Assert.Equal(2, tools.Count);
        Assert.Equal("shell: curl -s http://127.0.0.1:9/", tools[0].Text);
        Assert.Equal("shell: git status", tools[1].Text);
        var denied = Assert.Single(events, e => e.Mark == LiveMark.FailedTool);
        Assert.Contains("Permission denied", denied.Text);

        // The config that run used denies the command under the key that works.
        var made = JsonNode.Parse(OpenCodePermissions.WithoutWeb(null))!;
        Assert.Equal("deny", (string?)made["permission"]!["bash"]!["curl *"]);
    }

    // AG-3, AG-4, AG-5, AG-6, AG-7: the recorded good run.
    [Fact]
    public void OpenCode_OkRun_RendersLikeTheOthers()
    {
        var events = Events(Fixture("ok.jsonl"));

        Assert.DoesNotContain(events, e => e.Mark == LiveMark.Error);
        var tools = events.Where(e => e.Mark == LiveMark.ToolCall).ToList();
        Assert.Equal(4, tools.Count);
        Assert.Equal("shell: cat notes.txt", tools[0].Text);
        Assert.Equal("shell: dotnet --no-such-switch", tools[1].Text);
        var failed = Assert.Single(events, e => e.Mark == LiveMark.FailedTool);
        Assert.StartsWith("Could not execute", failed.Text);
        Assert.Equal("LANE-RESULT: done", events.Last(e => e.Mark == LiveMark.Message).Text);
        var end = Assert.Single(events, e => e.Mark == LiveMark.SessionEnd);
        Assert.Contains("153 tokens in, 110 out", end.Text);

        // TIME-14: the result is read from the model's own message lines only.
        var rendered = events.Where(e => e.Mark == LiveMark.Message).Select(e => "* " + e.Text);
        Assert.Equal("done", RunResults.ResultOf(rendered, rendered: true));
    }

    // AG-3: any line that is not JSON is the CLI's own text.
    [Fact]
    public void OpenCode_NonJsonLines_ShownAsErrors()
    {
        var e = Assert.Single(WorkerEvents.Render(AgentFormat.OpenCode, "Install Git for Windows"));
        Assert.Equal(LiveMark.Error, e.Mark);

        var scan = WorkerEvents.ScanOpenCode("some text\n", new WorkerStats(), "");
        Assert.Equal(["some text"], scan.Lines);
    }

    // AG-3: the recorded failure is one error line.
    [Fact]
    public void OpenCode_FailRun_OneErrorLine()
    {
        var events = Events(Fixture("fail.jsonl"));

        var error = Assert.Single(events);
        Assert.Equal(LiveMark.Error, error.Mark);
        Assert.Equal("Model unavailable: opencode/no-such-model", error.Text);
    }

    // AG-14
    [Fact]
    public void OpenCode_OkRun_StatsFilled()
    {
        var stats = new WorkerStats();
        var scan = WorkerEvents.ScanOpenCode(string.Join("\n", Fixture("ok.jsonl")) + "\n", stats, "");

        Assert.Equal(5, stats.Calls);
        Assert.Equal(4, stats.Tools);
        Assert.Equal(1, stats.ToolFails);
        Assert.Equal(287, stats.GenTokens);
        Assert.DoesNotContain(scan.Lines, l => l.Contains("step_start", StringComparison.Ordinal));
        Assert.Equal(1, scan.Lines.Count(l => l.Contains("step_finish", StringComparison.Ordinal)));
    }

    // AG-6: a tool that errors is failed, with its error text; one still running is not shown.
    [Fact]
    public void OpenCode_ToolStates()
    {
        var error = Events(["""{"type":"tool_use","part":{"tool":"edit","state":{"status":"error","input":{"filePath":"a.cs"},"error":"no such file"}}}"""]);
        Assert.Equal("edit: a.cs", error[0].Text);
        Assert.Equal((LiveMark.FailedTool, "no such file"), (error[1].Mark, error[1].Text));

        Assert.Empty(Events(["""{"type":"tool_use","part":{"tool":"shell","state":{"status":"running","input":{"command":"x"}}}}"""]));
    }

    // AG-8: quota is read from the CLI's own error and retry lines, never from the model's text.
    [Fact]
    public void OpenCode_Quota_FromErrorAndRetryLinesOnly()
    {
        Assert.True(RunResults.QuotaHit(Fixture("quota.synthetic.jsonl"), DefaultPatterns));
        Assert.False(RunResults.QuotaHit(Fixture("ok.jsonl"), DefaultPatterns));
        Assert.False(RunResults.QuotaHit(Fixture("fail.jsonl"), DefaultPatterns));
        Assert.False(RunResults.QuotaHit(
            ["""{"type":"text","part":{"type":"text","text":"we hit the rate limit"}}"""], DefaultPatterns));
        Assert.True(RunResults.QuotaHit(
            ["""{"type":"retry","attempt":1,"error":{"message":"Rate limit reached for requests"}}"""], DefaultPatterns));

        var retry = Assert.Single(Events([Fixture("quota.synthetic.jsonl")[0]]));
        Assert.StartsWith("retrying (attempt 1):", retry.Text);
    }

    // AG-9: a failed run with no result line crashed.
    [Fact]
    public void OpenCode_Exit_FailIsCrashed()
    {
        Assert.Equal(1, Exit("fail.exit"));
        Assert.Equal(0, Exit("ok.exit"));
        Assert.Equal(EndReasonKind.Crashed, RunResults.EndReasonOf("", Exit("fail.exit")).Kind);
    }
}
