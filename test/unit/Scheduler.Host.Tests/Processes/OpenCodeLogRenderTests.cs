using Scheduler.Core.Contracts;
using Scheduler.Host.Processes;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Processes;

/// <summary>An OpenCode worker's raw log rendered into its readable log (agents.md AG-3 to AG-7, AG-16).</summary>
public class OpenCodeLogRenderTests
{
    private const string Raw =
        "{\"type\":\"step_start\",\"part\":{\"type\":\"step-start\"}}\n"
        + "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"Looking.\"}}\n"
        + "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"shell\",\"state\":{\"status\":\"completed\",\"input\":{\"command\":\"dotnet --no-such-switch\"},\"output\":\"Could not execute\\nsecond\\nthird\",\"metadata\":{\"metadata\":{\"exit\":1}}}}}\n"
        + "{\"type\":\"step_finish\",\"part\":{\"type\":\"step-finish\",\"reason\":\"tool-calls\",\"tokens\":{\"input\":5,\"output\":1}}}\n"
        + "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"LANE-RESULT: done\"}}\n"
        + "{\"type\":\"step_finish\",\"part\":{\"type\":\"step-finish\",\"reason\":\"stop\",\"tokens\":{\"input\":100,\"output\":7}}}\n";

    [Fact]
    public void Render_OpenCode_EventsToReadableLog()
    {
        using var dir = new TempDir();
        var raw = Path.Combine(dir.Path, "010-A-x.jsonl");
        var w = ProcessKit.Worker(1, 100) with { RawLog = raw, Log = Path.ChangeExtension(raw, ".log"), RawPosition = 0 };
        File.WriteAllText(w.RawLog, Raw);
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));

        r.Advance(w, AgentFormat.OpenCode, false);

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
