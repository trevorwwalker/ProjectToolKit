// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;
using Scheduler.Core.PageModel;
using Scheduler.Host.Processes;
using Scheduler.Host.Slots;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;
using static Scheduler.Host.Tests.Processes.ProcessKit;

namespace Scheduler.Host.Tests.Processes;

public class AdoptTests
{
    // ADOPT-1: running.json is written on every tick and keeps, per worker, every listed field.
    [Fact]
    public void Adopt_RunningJson_WrittenEveryTick()
    {
        var files = new FakeStateFiles();
        var clock = new FakeClock(T0.AddMinutes(5));
        var writer = new TickStateWriter(files, clock);
        var w = Worker(2, 4242) with { StopAt = T0.AddMinutes(3), KillTime = T0.AddMinutes(4) };
        var running = new Dictionary<int, RunningWorker> { [2] = w };
        var stats = new WorkerStats { Calls = 7, Tools = 5, ToolFails = 1, CtxChars = 100, Compactions = 2, GenTokens = 50, GenSeconds = 10, LastTps = 5, LastTtft = 0.5 };

        writer.Write("running", new SchedulerConfig(), 2, _ => true, running, _ => stats, new Dictionary<string, int>(), []);

        var first = Assert.IsType<JsonArray>(JsonNode.Parse(files.Files[StateFile.Running]));
        var o = Assert.IsType<JsonObject>(Assert.Single(first));
        foreach (var key in new[] { "slot", "pid", "card", "lane", "id", "log", "view", "rawPos", "started", "job", "timebox",
                     "stopping", "stopAsked", "stopWhy", "grace", "killedAt", "msgs", "model", "endpoint", "metricsUrl", "agent", "stats" })
        {
            Assert.True(o.ContainsKey(key), $"running.json lacks {key}");
        }
        Assert.Equal(4242, (int)o["pid"]!);
        Assert.Equal("010-A-card", (string)o["card"]!);
        Assert.Equal(1234, (long)o["rawPos"]!);
        Assert.True((bool)o["stopping"]!);
        Assert.Equal(7, (int)o["stats"]!["Calls"]!);

        // The next tick writes it again, with what changed.
        running[2] = w with { RawPosition = 9999 };
        writer.Write("running", new SchedulerConfig(), 2, _ => true, running, null, new Dictionary<string, int>(), []);
        var second = Assert.IsType<JsonObject>(Assert.Single(JsonNode.Parse(files.Files[StateFile.Running])!.AsArray()));
        Assert.Equal(9999, (long)second["rawPos"]!);

        // And it reads back as the same worker.
        var back = Assert.Single(RunningJson.Read(files.Files[StateFile.Running])).Worker;
        Assert.Equal(running[2], back);
    }

    // ADOPT-2, TIME-13: of four recorded entries only the live cmd process that started when the
    // worker did is adopted; a pid that is not cmd, a pid reused 5 minutes later, and a dead pid are not.
    [Fact]
    public void Adopt_OnlyCmdWithMatchingStartTime()
    {
        using var dir = new TempDir();
        var script = ExeCmd(dir.Path, "wait.cmd", "ping -n 60 127.0.0.1 >nul");
        var processes = new WorkerProcesses(Jobs.Objects);
        var env = new Dictionary<string, string?>();
        var job = JobObjects.Template("ProjectToolkit", "adopt", 1);
        var a = processes.Start($"\"{script}\"", job, dir.Path, env) ?? throw new InvalidOperationException("start a");
        var c = processes.Start($"\"{script}\"", job, dir.Path, env) ?? throw new InvalidOperationException("start c");
        var d = processes.Start("exit 0", job, dir.Path, env) ?? throw new InvalidOperationException("start d");
        try
        {
            Assert.True(JobObjectTests.SpinWait(() => processes.ExitCode(d.Pid) is not null), "d should have exited");
            using var self = Process.GetCurrentProcess();
            Assert.NotEqual("cmd", self.ProcessName);

            var files = new FakeStateFiles();
            var log = new ListLog();
            files.Files[StateFile.Running] = RunningJson.Write(
            [
                Worker(1, a.Pid, "010-A", a.Start.AddMinutes(-1)),                         // (a) live cmd, start within 2 min
                Worker(2, self.Id, "020-B", new DateTimeOffset(self.StartTime)),            // (b) live, not cmd
                Worker(3, c.Pid, "030-C", c.Start.AddMinutes(-5)),                          // (c) live cmd, 5 min off: pid reused
                Worker(4, d.Pid, "040-D", d.Start),                                         // (d) dead
            ]);

            var adopted = new WorkerAdopter(files, processes, new FakeClock(DateTimeOffset.Now), log).Adopt(new WorkerPool { MaxWorkers = 2, Types = [new WorkerType { Name = "worker", Max = 2, Slots = [1, 2] }], Migrated = true });

            var only = Assert.Single(adopted.Entries).Worker;
            Assert.Equal("010-A", only.Id);
            Assert.Equal(a.Pid, only.Pid);
            Assert.Equal("worker", only.Type);   // WT-W5: an entry without a type gets its slot's
            Assert.Contains(log.Lines, l => l.Contains($"not adopting pid {c.Pid}", StringComparison.Ordinal));
            Assert.Contains(log.Lines, l => l.StartsWith("adopted slot 1,", StringComparison.Ordinal));
        }
        finally
        {
            processes.EndJob(job, a.Pid);
            processes.EndJob(job, c.Pid);
        }
    }

    // ADOPT-3: status.txt has the time and state, a line per slot, then the needs-you, planner,
    // you and review lines.
    [Fact]
    public void Adopt_StatusTxt_Lines()
    {
        var now = T0.AddMinutes(30);
        var running = new Dictionary<int, RunningWorker> { [1] = Worker(1, 10, "010-A", T0) };
        CardsFor(out var cards);

        var text = StatusFile.Build(now, "running", new SchedulerConfig(), 2, s => s == 1, running, cards);

        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
        [
            $"updated {now:yyyy-MM-dd HH:mm:ss}, scheduler running",
            "slot 1: 010-A-card (30/90 min)",
            "slot 2: disabled",
            "needs you: 030-C-stuck - Which way? It matters.",
            "waits for the planner: 040-D-plan (its dependencies are done: the planner can act)",
            "waits for you: 050-E-dev - Needs a decision",
            "in review: 060-F-rev - Check it by hand",
        ], lines);

        // The same through a tick: written to the file.
        var files = new FakeStateFiles();
        new TickStateWriter(files, new FakeClock(now)).Write("running", new SchedulerConfig(), 2, s => s == 1, running, null, new Dictionary<string, int>(), cards);
        Assert.Equal(text, files.Files[StateFile.Status]);

        // An idle enabled slot, and a stopping worker.
        var stopping = new Dictionary<int, RunningWorker> { [2] = Worker(2, 11, "020-B", T0) with { StopAt = T0 } };
        var more = StatusFile.Build(now, "draining", new SchedulerConfig(), 2, _ => true, stopping, []).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["slot 1: idle", "slot 2: 020-B-card (30/90 min) stopping"], more[1..]);
    }

    private static void CardsFor(out IReadOnlyList<Card> cards) => cards =
    [
        CardOf(CardFolder.Done, "020-B-done"),
        CardOf(CardFolder.Active, "030-C-stuck", "needs-human: Which way?", "**Which** way?  It matters."),
        CardOf(CardFolder.Queue, "040-D-plan", "waiting: planner (request #3)", "", "020-B"),
        CardOf(CardFolder.Queue, "050-E-dev", "waiting: developer", "Needs a decision"),
        CardOf(CardFolder.Review, "060-F-rev", "review: x", "Check it by hand"),
    ];
}
