using Scheduler.Core.Contracts;
using Scheduler.Host.Processes;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Processes;

/// <summary>A Codex worker's raw log rendered into its readable log (agents.md AG-3 to AG-7, AG-16).</summary>
public class CodexLogRenderTests
{
    private const string Raw =
        "Reading additional input from stdin...\n"
        + "{\"type\":\"thread.started\",\"thread_id\":\"t\"}\n"
        + "{\"type\":\"turn.started\"}\n"
        + "{\"type\":\"item.completed\",\"item\":{\"id\":\"i0\",\"type\":\"agent_message\",\"text\":\"Looking.\"}}\n"
        + "{\"type\":\"item.started\",\"item\":{\"id\":\"i1\",\"type\":\"command_execution\",\"command\":\"x\",\"status\":\"in_progress\"}}\n"
        + "{\"type\":\"item.completed\",\"item\":{\"id\":\"i1\",\"type\":\"command_execution\",\"command\":\"\\\"C:\\\\\\\\u\\\\\\\\pwsh.exe\\\" -Command 'dotnet --no-such-switch'\",\"aggregated_output\":\"Could not execute\\nsecond\\nthird\",\"exit_code\":1,\"status\":\"failed\"}}\n"
        + "{\"type\":\"item.completed\",\"item\":{\"id\":\"i2\",\"type\":\"agent_message\",\"text\":\"LANE-RESULT: done\"}}\n"
        + "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":100,\"output_tokens\":7}}\n";

    [Fact]
    public void Render_Codex_EventsToReadableLog()
    {
        using var dir = new TempDir();
        var raw = Path.Combine(dir.Path, "010-A-x.jsonl");
        var w = ProcessKit.Worker(1, 100) with { RawLog = raw, Log = Path.ChangeExtension(raw, ".log"), RawPosition = 0 };
        File.WriteAllText(w.RawLog, Raw);
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));

        r.Advance(w, AgentFormat.Codex, false);

        Assert.Equal(
            [
                "* Looking.",
                "> shell: dotnet --no-such-switch",
                "  x Could not execute | second",
                "* LANE-RESULT: done",
                "  (session ended: 100 tokens in, 7 out)",
            ],
            File.ReadAllLines(w.Log));
    }
}
