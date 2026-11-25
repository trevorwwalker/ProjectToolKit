// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;
using Scheduler.Host.PageActions;
using Scheduler.Host.Processes;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;

namespace Scheduler.Host.Tests.Processes;

/// <summary>1191-B: a fresh page shows a worker's last log lines at once; an adopted worker keeps its saved stats.</summary>
public class WorkerWindowLogTailTests
{
    [Fact]
    public void LogLines_GivesTheLastLinesOnly()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "w.log");
        File.WriteAllLines(path, Enumerable.Range(1, HostPageSnapshot.LogTailLines + 20).Select(i => $"line {i}"));

        var lines = HostPageSnapshot.LogLines(path);

        Assert.Equal(HostPageSnapshot.LogTailLines, lines.Count);
        Assert.Equal("line 21", lines[0]);
        Assert.Equal($"line {HostPageSnapshot.LogTailLines + 20}", lines[^1]);
    }

    [Fact]
    public void LogLines_MissingOrUnsetLog_IsEmpty()
    {
        using var dir = new TempDir();
        Assert.Empty(HostPageSnapshot.LogLines(Path.Combine(dir.Path, "none.log")));
        Assert.Empty(HostPageSnapshot.LogLines(""));
    }

    [Fact]
    public void Seed_AdoptedWorkerKeepsSavedStats()
    {
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));
        var w = ProcessKit.Worker(2, 200);

        r.Seed(w, new WorkerStats { Calls = 7, Tools = 5, ToolFails = 1, CtxChars = 1234, GenTokens = 99 });

        var s = r.Stats(2);
        Assert.NotNull(s);
        Assert.Equal(7, s.Calls);
        Assert.Equal(5, s.Tools);
        Assert.Equal(1, s.ToolFails);
        Assert.Equal(1234, s.CtxChars);
        Assert.Equal(99, s.GenTokens);
    }

    [Fact]
    public void Seed_ScanAddsToTheSavedStats()
    {
        using var dir = new TempDir();
        var raw = Path.Combine(dir.Path, "010-A-x.jsonl");
        var w = ProcessKit.Worker(1, 100) with { RawLog = raw, Log = Path.ChangeExtension(raw, ".log"), RawPosition = 0 };
        File.WriteAllText(raw, "{\"type\":\"stream_event\",\"event\":{\"type\":\"message_start\"}}\n");
        var r = new WorkerLogRenderer(new FakeClock(ProcessKit.T0));
        r.Seed(w, new WorkerStats { Calls = 10 });

        r.Advance(w, AgentFormat.Claude, false);

        Assert.Equal(11, r.Stats(1)!.Calls);
    }
}
