using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Host.PageActions;
using Scheduler.Host.Processes;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Processes;

/// <summary>
/// A json-format worker's raw log rendered into its readable log as it runs (460-B; schedule.ps1's
/// Render-New, Render-Event, Render-Claude), and the live stats the scan gives the page (PAGE-10).
/// </summary>
public class LogRenderTests
{
    private const string CopilotLines =
        "{\"type\":\"assistant.reasoning\",\"data\":{\"content\":\"First I look.\\n\\nThen   I act.\"}}\n"
        + "{\"type\":\"assistant.message_delta\",\"data\":{\"deltaContent\":\"Read\"}}\n"
        + "{\"type\":\"assistant.message\",\"data\":{\"content\":\"Reading the card.\\nTwo lines.\",\"toolRequests\":[{\"name\":\"powershell\",\"intentionSummary\":\"Read the card\",\"arguments\":{\"command\":\"Get-Content   jobs/x.md\"}}]}}\n"
        + "{\"type\":\"tool.execution_complete\",\"data\":{\"success\":true,\"result\":{\"content\":\"fine\"}}}\n"
        + "{\"type\":\"tool.execution_complete\",\"data\":{\"success\":false,\"result\":{\"content\":\"\\nbad thing\\nmore\\nrest\"}}}\n"
        + "{\"type\":\"model.call_finished\",\"data\":{\"dispatchDurationMs\":45000}}\n"
        + "{\"type\":\"session.mcp_status\",\"data\":{}}\n"
        + "error: the CLI itself\n";

    private const string ClaudeLines =
        "{\"type\":\"system\",\"subtype\":\"init\"}\n"
        + "{\"type\":\"stream_event\",\"event\":{\"type\":\"message_start\"}}\n"
        + "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"thinking\",\"thinking\":\"Plan it.\"},{\"type\":\"text\",\"text\":\"Checking the build.\"},{\"type\":\"tool_use\",\"name\":\"Bash\",\"input\":{\"command\":\"dotnet build\",\"description\":\"Build the solution\"}}],\"usage\":{\"input_tokens\":2000,\"cache_read_input_tokens\":1000}}}\n"
        + "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"is_error\":true,\"content\":\"exit 1\\nerror CS0001\"}]}}\n"
        + "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"content\":\"ok\"}]}}\n";

    private const string ClaudeLast =
        "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"All green.\\nLANE-RESULT: done\"}]}}\n";

    private const string ClaudeEnd =
        "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"num_turns\":7,\"total_cost_usd\":1.234,\"result\":\"LANE-RESULT: done\"}\n";

    // 460-B, PAGE-3: Copilot events become the old marks; a half-written last line waits for the next tick.
    [Fact]
    public void Render_Copilot_EventsToReadableLog()
    {
        using var dir = new TempDir();
        var w = Worker(dir, "jsonl");
        const string last = "{\"type\":\"assistant.message\",\"data\":{\"content\":\"LANE-RESULT: done\"}}\n";
        File.WriteAllText(w.RawLog, CopilotLines + last[..20]);
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));

        var pos = r.Advance(w, AgentFormat.Copilot, false);

        Assert.Equal(Encoding.UTF8.GetByteCount(CopilotLines), pos);
        var lines = File.ReadAllLines(w.Log);
        Assert.Equal(
            [
                "  ~ First I look.",
                "  ~ Then I act.",
                "* Reading the card.",
                "* Two lines.",
                "> Read the card: Get-Content jobs/x.md",
                "  x bad thing | more",
                "  (the model took 45 s to answer)",
                "! error: the CLI itself",
            ],
            lines);

        File.AppendAllText(w.RawLog, last[20..]);
        pos = r.Advance(w with { RawPosition = pos }, AgentFormat.Copilot, false);

        Assert.Equal(new FileInfo(w.RawLog).Length, pos);
        Assert.Equal("* LANE-RESULT: done", File.ReadAllLines(w.Log)[^1]);
    }

    // 460-B, PAGE-3: Claude stream-json becomes the same marks; the last line is taken at the finish even with no line end.
    [Fact]
    public void Render_Claude_StreamJsonToReadableLog()
    {
        using var dir = new TempDir();
        var w = Worker(dir, "jsonl");
        File.WriteAllText(w.RawLog, ClaudeLines + ClaudeLast + ClaudeEnd.TrimEnd('\n'));
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));

        var pos = r.Advance(w, AgentFormat.Claude, false);

        Assert.Equal(Encoding.UTF8.GetByteCount(ClaudeLines + ClaudeLast), pos);
        Assert.Equal(
            [
                "  ~ Plan it.",
                "* Checking the build.",
                "> Build the solution: dotnet build",
                "  x exit 1 | error CS0001",
                "* All green.",
                "* LANE-RESULT: done",
            ],
            File.ReadAllLines(w.Log));

        pos = r.Advance(w with { RawPosition = pos }, AgentFormat.Claude, true);

        Assert.Equal(new FileInfo(w.RawLog).Length, pos);
        Assert.Equal("  (session ended: success, 7 turns, $1.23)", File.ReadAllLines(w.Log)[^1]);
    }

    // TIME-14: a text agent's raw log is its readable log; nothing is rendered or written.
    [Fact]
    public void Finish_TextAgent_RawIsReadable()
    {
        using var dir = new TempDir();
        var raw = Path.Combine(dir.Path, "010-A-x.log");
        File.WriteAllText(raw, "* LANE-RESULT: done\n");
        var w = ProcessKit.Worker(1, 100) with { Log = raw, RawLog = raw };
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));

        Assert.Equal(w.RawPosition, r.Advance(w, AgentFormat.Text, true));
        Assert.Equal("* LANE-RESULT: done\n", File.ReadAllText(raw));
        Assert.Null(r.Stats(1));
        Assert.Equal("done", Scheduler.Core.Results.RunResults.ResultOf(File.ReadAllLines(raw), false));
    }

    // PAGE-10: the scan's stats (context, calls, tools, tokens/s) make the worker window's stats line.
    [Fact]
    public void Live_Stats_FromScan()
    {
        using var dir = new TempDir();
        var w = Worker(dir, "jsonl");
        var clock = new FakeClock(ProcessKit.T0);
        var r = new WorkerLogRenderer(clock);
        File.WriteAllText(w.RawLog, ClaudeLines);

        var pos = r.Advance(w, AgentFormat.Claude, false);

        var s = r.Stats(1);
        Assert.NotNull(s);
        Assert.Equal(1, s.Calls);
        Assert.Equal(2, s.Tools);
        Assert.Equal(1, s.ToolFails);
        Assert.Equal(3000 * 3.5, s.CtxChars);

        File.AppendAllText(w.RawLog,
            "{\"type\":\"stream_event\",\"event\":{\"type\":\"message_start\"}}\n"
            + "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"" + new string('a', 70) + "\"}}}\n");
        pos = r.Advance(w with { RawPosition = pos }, AgentFormat.Claude, false);
        clock.Advance(TimeSpan.FromSeconds(2));
        File.AppendAllText(w.RawLog, "{\"type\":\"stream_event\",\"event\":{\"type\":\"message_stop\"}}\n");
        r.Advance(w with { RawPosition = pos }, AgentFormat.Claude, false);

        var line = WorkerStatsText.Line(r.Stats(1), "opus", "");
        Assert.Equal("context ~3k  |  ~10 T/s now, 10 avg  |  2 model calls  |  2 tools, 1 failed  |  opus", line);
        Assert.Equal("opus  |  http://x", WorkerStatsText.Line(null, "opus", "http://x"));
    }

    private static RunningWorker Worker(TempDir dir, string ext)
    {
        var raw = Path.Combine(dir.Path, "010-A-x." + ext);
        return ProcessKit.Worker(1, 100) with { RawLog = raw, Log = Path.ChangeExtension(raw, ".log"), RawPosition = 0 };
    }
}
