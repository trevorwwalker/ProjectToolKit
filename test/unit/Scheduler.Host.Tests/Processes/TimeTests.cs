// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.Health;
using Scheduler.Host.Processes;
using Scheduler.Host.Slots;
using Scheduler.Host.State;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Host.Tests.State;
using static Scheduler.Host.Tests.Processes.ProcessKit;

namespace Scheduler.Host.Tests.Processes;

public class TimeTests
{
    // TIME-20: after a finish: a log line, one accounting row, a health check wanted, the recent
    // list (newest first, 10 long), the job ended, the slot free and its files cleared.
    [Fact]
    public void Time_AfterFinish_Bookkeeping()
    {
        using var dir = new TempDir();
        var clock = new FakeClock(T0.AddMinutes(12));
        var log = new ListLog();
        var accounting = new AccountingCsv(dir.Path, clock, log);
        for (var i = 0; i < 11; i++)
        {
            accounting.Add(new AccountingRow
            {
                Finished = T0.AddMinutes(i - 20), Card = $"0{i:00}-Z-old", Category = "Z", Slot = 1, Result = "done",
                Minutes = 1, LogBytes = 1, StopReason = "", Model = "m",
            }, null);
        }
        Assert.Equal(10, accounting.Recent.Count);
        var health = new HealthCheck(new SchedulerConfig(), clock, log, new NoHealthRunner());
        Assert.False(health.Wanted);
        var processes = new RecordingProcesses();
        var slotFiles = new SlotFiles(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "stop-slot2"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "msg-slot2.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "stop-slot1"), "x");
        var w = Worker(2, 777, "010-A", T0);
        var running = new Dictionary<int, RunningWorker> { [2] = w, [1] = Worker(1, 5, "020-B") };
        var rawClosed = new List<int>();

        new WorkerFinisher(clock, log, accounting, health, processes, slotFiles)
            .Finish(w, "done", "m1", running, x => rawClosed.Add(x.Slot));

        Assert.Contains("slot 2 finished: 010-A-card -> done after 12 min", log.Lines);
        var rows = File.ReadAllLines(accounting.FilePath);
        Assert.Equal(AccountingCsv.Header, rows[0]);
        Assert.Equal(13, rows.Length); // header, 11 earlier rows, this one
        Assert.EndsWith(",010-A-card,A,2,done,12,4096,\"over the 90 min limit\",m1,,,,", rows[^1], StringComparison.Ordinal);
        Assert.True(health.Wanted);
        Assert.Equal(10, accounting.Recent.Count);
        Assert.Equal("010-A-card", accounting.Recent[0].Card);
        Assert.Equal(2, accounting.Recent[0].Slot);
        Assert.Equal(w.Log, accounting.Recent[0].Log);
        Assert.Equal([("job-2-777", 777)], processes.Ended);
        Assert.False(running.ContainsKey(2));
        Assert.True(running.ContainsKey(1));
        Assert.Equal([2], rawClosed);
        Assert.False(File.Exists(Path.Combine(dir.Path, "stop-slot2")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "msg-slot2.txt")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "stop-slot1")), "another slot's files stay");
    }

    // TIME-21: with a 15 s poll, a worker that exits after about a second makes the next tick
    // run at once (within 250 ms; 1 s allowed on a loaded machine), not at the next poll.
    [Fact]
    public async Task Time_ProcessExit_NoticedWithin250ms()
    {
        using var dir = new TempDir();
        var script = ExeCmd(dir.Path, "short.cmd", "ping -n 2 127.0.0.1 >nul");
        var processes = new WorkerProcesses(Jobs.Objects);
        using var watcher = new WorkerExitWatcher();
        var job = JobObjects.Template("ProjectToolkit", "exit", 1);
        var started = processes.Start($"\"{script}\"", job, dir.Path, new Dictionary<string, string?>())
            ?? throw new InvalidOperationException("could not start the fake worker");
        try
        {
            // A 1 ms poller tells when the process really ended.
            var exitSeen = new TaskCompletionSource<DateTime>();
            _ = Task.Run(() =>
            {
                while (processes.ExitCode(started.Pid) is null)
                {
                    Thread.Sleep(1);
                }
                exitSeen.TrySetResult(DateTime.Now);
            });
            watcher.Watch(started.Pid);
            var waited = Stopwatch.StartNew();

            var exited = watcher.WaitForTick(TimeSpan.FromSeconds(15));
            var woke = DateTime.Now;

            Assert.True(exited, "the wait ended by the exit, not by the poll");
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(10), $"woke after {waited.Elapsed}");
            Assert.True(processes.ExitCode(started.Pid) is not null, "the worker has exited");
            var exitAt = await exitSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var late = woke - exitAt;
            Assert.True(late < TimeSpan.FromSeconds(1), $"woke {late} after the exit");
        }
        finally
        {
            processes.EndJob(job, started.Pid);
        }
    }

    // TIME-22: retries.json is written each tick; after a restart the counts are back.
    [Fact]
    public void Time_Retries_PersistAcrossRestart()
    {
        using var dir = new TempDir();
        var files = new StateFiles(dir.Path);
        var retries = new Dictionary<string, int> { ["010-A"] = 1 };

        new TickStateWriter(files, new FakeClock(T0)).Write("running", new SchedulerConfig(), 1, _ => true,
            new Dictionary<int, RunningWorker>(), null, retries, []);

        Assert.True(File.Exists(Path.Combine(dir.Path, "retries.json")));
        var afterRestart = RetriesFile.Load(new StateFiles(dir.Path));
        Assert.Equal(1, afterRestart["010-A"]);
        Assert.Single(afterRestart);

        // Every tick writes it: a changed count replaces the old one.
        retries["010-A"] = 2;
        new TickStateWriter(files, new FakeClock(T0)).Write("running", new SchedulerConfig(), 1, _ => true,
            new Dictionary<int, RunningWorker>(), null, retries, []);
        Assert.Equal(2, RetriesFile.Load(new StateFiles(dir.Path))["010-A"]);
        Assert.Empty(RetriesFile.Load(new FakeStateFiles()));
    }
}
