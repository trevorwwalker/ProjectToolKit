// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Processes;

namespace Scheduler.Host.Tests.Processes;

/// <summary>running.json and status.txt under worker types (WT-W5, WT-P5). Test plan: worker-types.md.</summary>
public class WorkerTypeFileTests
{
    private static readonly DateTimeOffset T0 = new(new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Local));

    private static RunningWorker Worker(int slot, string card, string type) => new()
    {
        Slot = slot, Pid = 100 + slot, Start = T0, Card = card, Category = "B", Id = card[..5], Type = type,
    };

    // WT-W5: running.json carries each worker's type, and an entry from before types reads back without one.
    [Fact]
    public void Adoption_RunningJson_KeepsType_OldEntryHasNone()
    {
        var back = RunningJson.Read(RunningJson.Write([Worker(2, "880-B-x", "Sonnet")]));
        Assert.Equal("Sonnet", Assert.Single(back).Worker.Type);

        var old = RunningJson.Read("""[{"slot":1,"pid":7,"card":"880-B-x","started":"2026-11-19T10:00:00"}]""");
        Assert.Equal("", Assert.Single(old).Worker.Type);
    }

    // WT-P5: one line per running worker with its type, a "types:" line with each type's running of its
    // cap (off at 0), then the lines it has today. A folder above maxWorkers is listed while it runs.
    [Fact]
    public void StatusFile_TypesLine()
    {
        var running = new Dictionary<int, RunningWorker>
        {
            [1] = Worker(1, "880-B-x", "worker"),
            [2] = Worker(2, "890-C-y", "worker"),
            [5] = Worker(5, "900-B-z", "Opus"),
        };
        WorkerType[] types =
        [
            new() { Name = "worker", Max = 2 },
            new() { Name = "Sonnet", Max = 0 },
            new() { Name = "Opus", Max = 1 },
        ];

        var lines = StatusFile.Build(T0.AddMinutes(5), "running", new SchedulerConfig(), 4, _ => true, running, [], types)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("slot 1 (worker): 880-B-x (5/", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("slot 2 (worker): 890-C-y (5/", lines[2], StringComparison.Ordinal);
        Assert.Equal("slot 3: idle", lines[3]);
        Assert.Equal("slot 4: idle", lines[4]);
        Assert.StartsWith("slot 5 (Opus): 900-B-z (5/", lines[5], StringComparison.Ordinal);
        Assert.Equal("types: worker 2/2, Sonnet 0/0 (off), Opus 1/1", lines[6]);
    }
}
