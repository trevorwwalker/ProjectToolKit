using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;
using Scheduler.Core.WebContract;

namespace Scheduler.Core.Tests.PageModel;

/// <summary>
/// Rendering a worker's raw log into readable lines (PAGE-10; <c>Render-Event</c> L1899,
/// <c>Render-Claude</c> L1854 and the <c>LiveScan</c> counters L1958-2163): Copilot events and
/// Claude stream-json get the same marks - reasoning <c>~</c>, message <c>*</c>, tool call
/// <c>&gt;</c>, failed tool <c>x</c>, a model call over 30 s, errors <c>!</c> - Claude adds the
/// session end with its cost, and the stats are estimated at 3.5 characters a token with the
/// context reset when the session is compacted.
/// Test plan: scheduler-next/test-plans/page.md.
/// </summary>
public class PageEventsTests
{
    private const string Streamed = "aaaaaaaaabbbbbbbbbcccccccccddddddddd";
    private const string LongTool = "012345678901234567890123456789012345678901234567890123456789012345678901234567890"
        + "012345678901234567890123456789012345678901234567890123456789012345678901234567890"
        + "012345678901234567890123456789012345678901234567890123456789012345678901234567890"
        + "012345678901234567890123456789012345678901234567890123456789012345678901234567890";

    private static readonly string[] Copilot =
    [
        @"{""type"":""model.call_start"",""timestamp"":""2026-11-18T10:00:00"",""data"":{}}",
        @"{""type"":""assistant.message_delta"",""timestamp"":""2026-11-18T10:00:01"",""data"":{""deltaContent"":""" + Streamed + @"""}}",
        @"{""type"":""model.call_finished"",""timestamp"":""2026-11-18T10:00:10"",""data"":{""dispatchDurationMs"":40000}}",
        @"{""type"":""assistant.reasoning"",""timestamp"":""2026-11-18T10:00:11"",""data"":{""content"":""First paragraph.\n\nSecond paragraph.""}}",
        @"{""type"":""assistant.message"",""timestamp"":""2026-11-18T10:00:12"",""data"":{""content"":""I will build it."",""toolRequests"":[{""name"":""powershell"",""intentionSummary"":""Run the build"",""arguments"":{""command"":""dotnet build   -m:8""}}]}}",
        @"{""type"":""tool.execution_complete"",""timestamp"":""2026-11-18T10:00:13"",""data"":{""success"":true,""result"":{""content"":""" + LongTool + @"""}}}",
        @"{""type"":""tool.execution_complete"",""timestamp"":""2026-11-18T10:00:14"",""data"":{""success"":false,""result"":{""content"":""bad thing happened\nmore detail\nnever mind""}}}",
        @"{""type"":""session.error"",""data"":{""message"":""boom""}}",
        @"{""type"":""context.compacted"",""data"":{}}",
        "the CLI said this",
    ];

    private static readonly string[] Claude =
    [
        @"{""type"":""system"",""subtype"":""compact_boundary"",""session_id"":""s1""}",
        @"{""type"":""stream_event"",""event"":{""type"":""message_start""},""message"":{""usage"":{""input_tokens"":10}}}",
        @"{""type"":""stream_event"",""event"":{""type"":""content_block_delta"",""index"":0},""delta"":{""type"":""text_delta"",""text"":""" + Streamed + @"""}}",
        @"{""type"":""stream_event"",""event"":{""type"":""message_stop""}}",
        @"{""type"":""assistant"",""message"":{""content"":[{""type"":""text"",""text"":""Hello there.""},{""type"":""thinking"",""thinking"":""I should look first.""},{""type"":""tool_use"",""name"":""Bash"",""input"":{""command"":""npm test"",""description"":""Run the tests""}}],""usage"":{""input_tokens"":100,""cache_creation_input_tokens"":0,""cache_read_input_tokens"":400}}}",
        @"{""type"":""user"",""message"":{""content"":[{""type"":""tool_result"",""is_error"":true,""content"":""tests failed\n2 failing""}]}}",
        @"{""type"":""result"",""subtype"":""success"",""num_turns"":12,""is_error"":false,""total_cost_usd"":0.42}",
        "claude said this",
    ];

    /// <summary>A clock that moves on a second each time it is read, so a streamed Claude message
    /// has a measurable first token and generation time (its stream-json carries no timestamps).</summary>
    private sealed class SteppingClock : IClock
    {
        private DateTimeOffset at = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Now
        {
            get
            {
                var reading = at;
                at = at.AddSeconds(1);
                return reading;
            }
        }
    }

    private static IReadOnlyList<LiveEvent> RenderAll(AgentFormat format, IEnumerable<string> lines)
    {
        var outList = new List<LiveEvent>();
        foreach (var line in lines)
        {
            outList.AddRange(WorkerEvents.Render(format, line));
        }

        return outList;
    }

    /// <summary>The nth line rendered with this mark.</summary>
    private static LiveEvent Line(IReadOnlyList<LiveEvent> events, LiveMark mark, int nth)
    {
        var withMark = events.Where(e => e.Mark == mark).ToList();
        Assert.True(nth < withMark.Count, $"no {mark} line number {nth} among: {string.Join(" / ", withMark.Select(e => e.Text))}");
        return withMark[nth];
    }

    // PAGE-10. The marks the two formats share, Claude's session end and cost, the 3.5 characters a
    // token estimate, and the context count cleared by a compaction.
    [Fact]
    public void Page_RenderEvents_CopilotAndClaude()
    {
        // --- Copilot jsonl -------------------------------------------------------------
        var copilotStats = new WorkerStats();
        var scan = WorkerLogScan.Copilot(string.Join("\n", Copilot) + "\n", copilotStats, "");
        var copilot = RenderAll(AgentFormat.Copilot, scan.Lines);

        // The streamed piece is not a line of its own, but it is the line being written (PAGE-3),
        // until the finished message arrives and the worker starts a new one.
        var streamed = WorkerLogScan.Copilot(string.Join("\n", Copilot[..3]) + "\n", new WorkerStats(), "");
        Assert.Contains(Streamed, streamed.Partial, StringComparison.Ordinal);
        Assert.DoesNotContain(scan.Lines, l => l.Contains("message_delta", StringComparison.Ordinal));

        Assert.Equal("First paragraph.", Line(copilot, LiveMark.Reasoning, 0).Text);
        Assert.Equal("Second paragraph.", Line(copilot, LiveMark.Reasoning, 1).Text);
        Assert.Equal("I will build it.", Line(copilot, LiveMark.Message, 0).Text);
        Assert.Equal("Run the build: dotnet build -m:8", Line(copilot, LiveMark.ToolCall, 0).Text);
        Assert.Equal("bad thing happened | more detail", Line(copilot, LiveMark.FailedTool, 0).Text);
        Assert.Equal("(the model took 40 s to answer)", Line(copilot, LiveMark.SlowModelCall, 0).Text);
        Assert.Equal(@"session.error: {""message"":""boom""}", Line(copilot, LiveMark.Error, 0).Text);
        Assert.Equal("the CLI said this", Line(copilot, LiveMark.Error, 1).Text);

        // The 35 streamed characters are 10 tokens at 3.5 a token, from the one model call.
        Assert.Equal(1, copilotStats.Calls);
        Assert.Equal(Streamed.Length / 3.5, copilotStats.GenTokens, 3);
        Assert.Equal(1.0, copilotStats.LastTtft, 1);

        // The long tool result counts towards the context; the compaction clears the count.
        var onlyTool = new WorkerStats();
        WorkerLogScan.Copilot(Copilot[5] + "\n", onlyTool, "");
        Assert.True(onlyTool.CtxChars > 0, "a long tool result counts towards the context");
        Assert.Equal(2, copilotStats.Tools);
        Assert.Equal(1, copilotStats.ToolFails);
        Assert.Equal(1, copilotStats.Compactions);
        Assert.Equal(0, copilotStats.CtxChars);

        // --- Claude stream-json --------------------------------------------------------
        var claudeStats = new WorkerStats();
        var claudeScan = WorkerLogScan.Claude(string.Join("\n", Claude) + "\n", claudeStats, "", new SteppingClock());
        var claude = RenderAll(AgentFormat.Claude, claudeScan.Lines);

        Assert.Equal("Hello there.", Line(claude, LiveMark.Message, 0).Text);
        Assert.Equal("I should look first.", Line(claude, LiveMark.Reasoning, 0).Text);
        Assert.Equal("Run the tests: npm test", Line(claude, LiveMark.ToolCall, 0).Text);
        Assert.Equal("tests failed | 2 failing", Line(claude, LiveMark.FailedTool, 0).Text);
        Assert.Equal("(session ended: success, 12 turns, $0.42)", Line(claude, LiveMark.SessionEnd, 0).Text);
        Assert.Equal("claude said this", Line(claude, LiveMark.Error, 0).Text);
        var claudeStreamed = WorkerLogScan.Claude(string.Join("\n", Claude[1..4]) + "\n", new WorkerStats(), "", new SteppingClock());
        Assert.Contains(Streamed, claudeStreamed.Partial, StringComparison.Ordinal);

        // The same estimate: 35 streamed characters at 3.5 a token, over the one streamed message.
        Assert.Equal(1, claudeStats.Calls);
        Assert.Equal(Streamed.Length / 3.5, claudeStats.GenTokens, 3);
        // Claude says how full its own context is: 500 of its tokens at 3.5 characters a token.
        Assert.Equal(500 * 3.5, claudeStats.CtxChars, 3);
        Assert.Equal(1, claudeStats.Compactions);
        Assert.Equal(1, claudeStats.Tools);
        Assert.Equal(1, claudeStats.ToolFails);
    }
}
