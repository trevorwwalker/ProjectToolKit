using System.Text.Json.Nodes;
using Scheduler.Core.Cards;
using Scheduler.Core.Config;
using Scheduler.Core.Contracts;
using Scheduler.Core.Categories;
using Scheduler.Core.Launch;

namespace Scheduler.Core.Tests.Launch;

/// <summary>
/// The developer's message handed over by an agent hook after each tool call (WRK-14, job card
/// 570-B): what the hook prints for each agent, the Claude settings that install it, the Claude
/// command line that carries them, the environment that names the message file, and the prompt
/// that no longer asks a hooked worker to look for the file.
/// </summary>
public class MessageHookTests
{
    private const string Repo = @"C:\repos\Proj";
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";
    private const string LogDir = @"C:\Users\dev\AppData\Local\Proj\scheduler";
    private const string ReadFile = LogDir + @"\msg-slot4.read.txt";

    private const string CfgJson = """
        {"agents":{
           "cop":{"cli":"copilot","args":"-p {prompt} --output-format json","format":"copilot"},
           "cl":{"cli":"claude","args":"-p {prompt} --output-format stream-json","format":"claude"},
           "txt":{"cli":"mycli","args":"-p {prompt}"}}}
        """;

    private static SchedulerConfig Load() =>
        SchedulerConfigLoader.Load(CfgJson, Repo, LocalAppData) with { LogDir = LogDir };

    // WRK-14: Claude gets hookSpecificOutput with the message, prefixed Developer:, for the event
    // it called the hook for.
    [Fact]
    public void Reply_Claude_PostToolUse()
    {
        var text = MessageHook.ContextText("[23:40] stop after the build\n", ReadFile);
        var reply = JsonNode.Parse(MessageHook.Reply(AgentFormat.Claude, """{"hook_event_name":"PostToolUse"}""", text)!)!;

        Assert.Equal("PostToolUse", (string?)reply["hookSpecificOutput"]!["hookEventName"]);
        var context = (string?)reply["hookSpecificOutput"]!["additionalContext"];
        Assert.StartsWith("Developer: [23:40] stop after the build", context, StringComparison.Ordinal);
        Assert.Contains("start your reply with 'Developer:'", context, StringComparison.Ordinal);
        Assert.Contains(ReadFile, context, StringComparison.Ordinal);
    }

    // WRK-14: after a failed tool the reply names PostToolUseFailure, so it is still delivered;
    // input that is not JSON falls back to PostToolUse.
    [Fact]
    public void Reply_Claude_FailureEventAndBadInput()
    {
        var failed = JsonNode.Parse(MessageHook.Reply(AgentFormat.Claude, """{"hook_event_name":"PostToolUseFailure"}""", "t")!)!;
        Assert.Equal("PostToolUseFailure", (string?)failed["hookSpecificOutput"]!["hookEventName"]);

        var bad = JsonNode.Parse(MessageHook.Reply(AgentFormat.Claude, "not json", "t")!)!;
        Assert.Equal("PostToolUse", (string?)bad["hookSpecificOutput"]!["hookEventName"]);
    }

    // WRK-14: Copilot gets a top-level additionalContext; a text agent has no hooks and gets nothing.
    [Fact]
    public void Reply_Copilot_And_Text()
    {
        var reply = JsonNode.Parse(MessageHook.Reply(AgentFormat.Copilot, "{}", "Developer: hi")!)!;
        Assert.Equal("Developer: hi", (string?)reply["additionalContext"]);
        Assert.Null(MessageHook.Reply(AgentFormat.Text, "{}", "Developer: hi"));
    }

    // WRK-14: a message longer than one reply carries is cut, and the reply says where the rest is.
    [Fact]
    public void ContextText_LongMessage_Cut()
    {
        var text = MessageHook.ContextText(new string('x', MessageHook.MaxText + 500), ReadFile);
        Assert.True(text.Length < MessageHook.MaxText + 1000);
        Assert.Contains("the whole message is in " + ReadFile, text, StringComparison.Ordinal);
    }

    // WRK-14: the settings run the exe with --deliver-message claude after every tool call, failed
    // ones too, with a timeout.
    [Fact]
    public void ClaudeSettings_HookBothEvents()
    {
        var settings = JsonNode.Parse(MessageHook.ClaudeSettings(@"C:\Tools\scheduler-next.exe"))!;
        foreach (var name in new[] { "PostToolUse", "PostToolUseFailure" })
        {
            var hook = settings["hooks"]![name]![0]!["hooks"]![0]!;
            Assert.Equal("command", (string?)hook["type"]);
            Assert.Equal("\"C:/Tools/scheduler-next.exe\" --deliver-message claude", (string?)hook["command"]);
            Assert.Equal(MessageHook.TimeoutSeconds, (int?)hook["timeout"]);
        }
    }

    // WRK-14: the format on the hook's command line round-trips; anything else is text.
    [Fact]
    public void Format_Names()
    {
        Assert.Equal(AgentFormat.Claude, MessageHook.ParseFormat(MessageHook.FormatName(AgentFormat.Claude)));
        Assert.Equal(AgentFormat.Copilot, MessageHook.ParseFormat(MessageHook.FormatName(AgentFormat.Copilot)));
        Assert.Equal(AgentFormat.Text, MessageHook.ParseFormat("other"));
        Assert.Equal(ReadFile, MessageHook.ReadFileOf(LogDir + @"\msg-slot4.txt"));
    }

    // WRK-14: a Claude worker's command line carries the hook settings; Copilot and text agents' do not.
    [Fact]
    public void Args_Claude_CarriesSettings()
    {
        var cfg = Load();
        var claude = WorkerArgs.For(cfg, Agents.AgentOf(cfg, "cl"), 8, "m", "", "P", web: true);
        var at = claude.ToList().IndexOf("--settings");
        Assert.True(at >= 0);
        Assert.Equal(WorkerFiles.HookSettings(LogDir), claude[at + 1]);

        Assert.DoesNotContain("--settings", WorkerArgs.For(cfg, Agents.AgentOf(cfg, "cop"), 8, "m", "", "P", web: true));
        Assert.DoesNotContain("--settings", WorkerArgs.For(cfg, Agents.AgentOf(cfg, "txt"), 8, "m", "", "P", web: true));
    }

    // WRK-14: every worker's environment names its own slot's message file.
    [Fact]
    public void Env_CarriesMessageFile()
    {
        var cfg = Load();
        var env = WorkerEnv.Build(
            cfg, Agents.AgentOf(cfg, "cl"), new EffectiveWorker { Agent = "cl" }, 8, 4, null, new NoProbe(), new NoLog());
        Assert.Equal(WorkerFiles.MessageFile(LogDir, 4), env.Set[MessageHook.FileVariable]);
    }

    // WRK-14: with hooks the prompt checks only the warn and stop files and says messages arrive
    // by themselves; without, it keeps the check-the-file steps (the fallback for text agents).
    [Fact]
    public void Prompt_Hooks_DropsTheFileCheck()
    {
        var cfg = Load();
        var card = CardFiles.Parse("jobs", "jobs/queue/001-B-x.md", "Status: ready")!;
        string Prompt(bool hooks) => WorkerPrompt.Build(cfg, new WorkerPromptRequest
        {
            Card = card,
            Worktree = @"C:\w",
            Slot = 4,
            Start = new DateTimeOffset(2026, 11, 18, 23, 0, 0, TimeSpan.Zero),
            Hooks = hooks,
        });

        var hooked = Prompt(true);
        Assert.DoesNotContain(WorkerFiles.MessageFile(LogDir, 4), hooked, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkerFiles.MessageReadFile(LogDir, 4), hooked, StringComparison.Ordinal);
        Assert.Contains("check for two files in one command", hooked, StringComparison.Ordinal);
        Assert.Contains("hands each message to you by itself after a tool call", hooked, StringComparison.Ordinal);
        Assert.Contains("start your reply with 'Developer:'", hooked, StringComparison.Ordinal);
        Assert.Contains(WorkerFiles.StopFlag(LogDir, 4), hooked, StringComparison.Ordinal);

        var plain = Prompt(false);
        Assert.Contains(WorkerFiles.MessageFile(LogDir, 4), plain, StringComparison.Ordinal);
        Assert.Contains("check for three files in one command", plain, StringComparison.Ordinal);
        Assert.Contains("added by the scheduler's message hook; that is the developer too", plain, StringComparison.Ordinal);
    }

    private sealed class NoLog : ISchedulerLog
    {
        public void Write(string line)
        {
        }
    }

    private sealed class NoProbe : IServerProbe
    {
        public bool IsUp(string endpoint) => true;

        public string? ReadModels(string endpoint) => null;

        public string? ReadMetrics(string metricsUrl) => null;
    }
}
