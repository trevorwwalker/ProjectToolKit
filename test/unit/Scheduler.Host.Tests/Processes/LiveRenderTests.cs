using Scheduler.Core.Contracts;
using Scheduler.Host.PageActions;
using Scheduler.Host.Processes;
using Scheduler.Host.Tests.PageActions;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;
using Scheduler.Host.Tick;

namespace Scheduler.Host.Tests.Processes;

/// <summary>
/// 964-B (PAGE-3, PAGE-10): a worker's window grows as the worker writes. The live feed renders the
/// raw log's new lines on each build instead of waiting for the tick's watch, and the two share one
/// offset, so nothing is rendered twice.
/// </summary>
public class LiveRenderTests
{
    private const string First =
        "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Checking the build.\"}],\"usage\":{\"input_tokens\":2000,\"cache_read_input_tokens\":1000}}}\n";

    private const string Second =
        "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"All green.\"}]}}\n";

    private const string Third =
        "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Pushing.\"}]}}\n";

    // PAGE-3: appended events show in the next live read, with no tick in between.
    [Fact]
    public void Live_CatchUp_RendersNewLinesWithoutTick()
    {
        using var dir = new TempDir();
        var w = Worker(dir);
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));
        File.WriteAllText(w.RawLog, First);
        r.Advance(w, AgentFormat.Claude, false);

        File.AppendAllText(w.RawLog, Second);
        r.CatchUp();

        Assert.Equal(["* Checking the build.", "* All green."], File.ReadAllLines(w.Log));
    }

    // PAGE-3: a line still being written waits for its line end, then shows whole.
    [Fact]
    public void Live_CatchUp_PartialLineWaits()
    {
        using var dir = new TempDir();
        var w = Worker(dir);
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));
        File.WriteAllText(w.RawLog, First);
        r.Advance(w, AgentFormat.Claude, false);

        File.AppendAllText(w.RawLog, Second[..30]);
        r.CatchUp();
        Assert.Equal(["* Checking the build."], File.ReadAllLines(w.Log));

        File.AppendAllText(w.RawLog, Second[30..]);
        r.CatchUp();
        Assert.Equal(["* Checking the build.", "* All green."], File.ReadAllLines(w.Log));
    }

    // PAGE-3: the tick's watch, holding the position it stored a tick ago, and the live read never
    // render the same line twice, whichever comes first; the position the tick stores is the shared one.
    [Fact]
    public void Live_TickAndCatchUp_NeverDuplicate()
    {
        using var dir = new TempDir();
        var w = Worker(dir);
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));
        File.WriteAllText(w.RawLog, First);
        var stored = r.Advance(w, AgentFormat.Claude, false);

        File.AppendAllText(w.RawLog, Second);
        r.CatchUp();
        var tick = r.Advance(w with { RawPosition = stored }, AgentFormat.Claude, false);
        Assert.Equal(new FileInfo(w.RawLog).Length, tick);

        File.AppendAllText(w.RawLog, Third);
        tick = r.Advance(w with { RawPosition = tick }, AgentFormat.Claude, false);
        r.CatchUp();
        r.Advance(w with { RawPosition = tick }, AgentFormat.Claude, true);

        Assert.Equal(["* Checking the build.", "* All green.", "* Pushing."], File.ReadAllLines(w.Log));
    }

    // PAGE-10: the stats move with the text in the same read; a finished run is no longer read.
    [Fact]
    public void Live_CatchUp_StatsMoveWithText()
    {
        using var dir = new TempDir();
        var w = Worker(dir);
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));
        File.WriteAllText(w.RawLog, "");
        r.Advance(w, AgentFormat.Claude, false);
        Assert.Equal(0, r.Stats(1)!.Calls);

        File.AppendAllText(w.RawLog, "{\"type\":\"stream_event\",\"event\":{\"type\":\"message_start\"}}\n" + First);
        r.CatchUp();
        Assert.Equal(1, r.Stats(1)!.Calls);
        Assert.Equal(3000 * 3.5, r.Stats(1)!.CtxChars);

        r.Forget(1);
        File.AppendAllText(w.RawLog, Second);
        r.CatchUp();
        Assert.Null(r.Stats(1));
        Assert.Equal(["* Checking the build."], File.ReadAllLines(w.Log));
    }

    // PAGE-3, PAGE-10: the page's running list renders first and makes the stats line again, so the
    // live feed's build carries the new text and stats between ticks.
    [Fact]
    public void Live_PageData_RunningWorkersRenderAndRestat()
    {
        using var h = new PageActionHarness();
        var w = h.Running(1, log: null) with { Model = "opus" };
        h.Tick.Adopt(w);
        var r = new WorkerLogRenderer(h.Clock);
        File.WriteAllText(w.RawLog, "");
        r.Advance(w, AgentFormat.Claude, false);
        var data = new HostPageData(
            h.Config,
            h.Clock,
            h.Tick,
            new PageReviewRefreshTests.InMemoryMain(),
            h.Files,
            () => null,
            new PageSnapshotSources { Stats = r.Stats, LiveRender = r.CatchUp });
        data.Refresh();
        Assert.DoesNotContain("context ~3k", data.RunningWorkers().Single().Stats, StringComparison.Ordinal);

        File.AppendAllText(w.RawLog, First);
        var live = data.RunningWorkers().Single();

        Assert.Equal(["* Checking the build."], File.ReadAllLines(w.Log));
        Assert.StartsWith("context ~3k", live.Stats, StringComparison.Ordinal);
        Assert.EndsWith("opus", live.Stats, StringComparison.Ordinal);
    }

    // PAGE-3 (1093-C): the slot's next card is another run. Its readable log is its own file, and the
    // render starts from nothing: what the worker before it wrote, and the numbers scanned off it, are
    // neither drawn onto the new log nor answered for the new run.
    [Fact]
    public void Live_SlotTakenByAnotherCard_RendersOnlyIntoItsOwnLog()
    {
        using var dir = new TempDir();
        var before = Worker(dir);
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));
        File.WriteAllText(before.RawLog, First);
        r.Advance(before, AgentFormat.Claude, false);
        Assert.True(r.Stats(1) is { CtxChars: > 0 }, "the first run has numbers of its own");

        // The card finished and the slot took the next one: another pid, another pair of log files.
        var nextRaw = Path.Combine(dir.Path, "1094-C-next-card.jsonl");
        var next = before with { Pid = 200, RawLog = nextRaw, Log = Path.ChangeExtension(nextRaw, ".log"), RawPosition = 0 };
        File.WriteAllText(next.RawLog, Second);
        r.Advance(next, AgentFormat.Claude, false);

        Assert.Equal(["* All green."], File.ReadAllLines(next.Log));
        Assert.Equal(["* Checking the build."], File.ReadAllLines(before.Log));
        Assert.True(r.Stats(1) is { CtxChars: 0 }, "the new run starts with no numbers of the last one's");
    }

    // PAGE-3 (1093-C): a finished worker leaves nothing behind in its slot. The tick forgets it as it
    // settles the card, so a build of the slot's next worker cannot read the last one's stats.
    [Fact]
    public void Live_FinishedWorker_LeavesNoStatsInTheSlot()
    {
        using var dir = new TempDir();
        var w = Worker(dir);
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));
        File.WriteAllText(w.RawLog, First);
        r.Advance(w, AgentFormat.Claude, false);
        Assert.NotNull(r.Stats(1));

        r.Forget(w.Slot);
        Assert.Null(r.Stats(1));
    }

    private static RunningWorker Worker(TempDir dir)
    {
        var raw = Path.Combine(dir.Path, "010-A-x.jsonl");
        return ProcessKit.Worker(1, 100) with { RawLog = raw, Log = Path.ChangeExtension(raw, ".log"), RawPosition = 0 };
    }
}
