using Scheduler.Core.Contracts;
using Scheduler.Core.Launch;

namespace Scheduler.Core.Tests.Launch;

/// <summary>
/// What restarting the scheduler does with a worker still running (ADOPT-2; <c>Adopt-Running</c>
/// L2967): the slot count follows a slot above it, and a worker keeps the stop deadline and the
/// quiet time it had. Test plan: test-plans/workers.md.
/// </summary>
public class AdoptionTests
{
    private const string Log = @"C:\logs\010-A-20261118-112039.jsonl";

    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static RunningWorker Entry(int slot) => new()
    {
        Slot = slot,
        Pid = 4242,
        Start = Now.AddMinutes(-30),
        Card = "010-A-core-contracts",
        Category = "A",
        Id = "010-A",
        Log = Log,
        RawLog = Log,
        Msgs = true,
    };

    // ADOPT-2 (L2985): a worker on a slot above the count raises the count, and that slot is enabled.
    [Fact]
    public void Adopt_SlotAboveCount_RaisesCount()
    {
        var result = Adoption.Adopt(Entry(4), slots: 2, Now, logSize: 100, logLastWrite: Now.AddMinutes(-1));

        Assert.Equal(4, result.Slots);
        Assert.Equal([4], result.NewlyEnabled);
        Assert.Equal(4, result.Worker.Slot);

        var same = Adoption.Adopt(Entry(2), slots: 3, Now, logSize: 100, logLastWrite: Now.AddMinutes(-1));
        Assert.Equal(3, same.Slots);
        Assert.Empty(same.NewlyEnabled);
    }

    // ADOPT-2 (L2987-2993): the stop asked for before the restart keeps its deadline instead of
    // starting again from zero, and quiet time counts from the log's last write, not the restart.
    [Fact]
    public void Adopt_StopDeadlineCarriesOver_QuietFromLogWrite()
    {
        var entry = Entry(2) with
        {
            StopAt = Now.AddMinutes(-10),
            StopWhy = "over the 120 min limit",
            StopGraceMinutes = 20,
        };

        var result = Adoption.Adopt(entry, slots: 2, Now, logSize: 2048, logLastWrite: Now.AddMinutes(-25));
        var worker = result.Worker;

        // Asked to stop 10 min ago with 20 min of grace: it is killed 10 min after the restart, not 20.
        Assert.Equal(Now.AddMinutes(10), Adoption.StopDeadline(worker));
        Assert.Equal(Now.AddMinutes(-10), worker.StopAt);
        Assert.Equal(20, worker.StopGraceMinutes);
        Assert.Equal("over the 120 min limit", worker.StopWhy);
        Assert.Equal(25, (Now - worker.LastProgress).TotalMinutes);
        Assert.Equal(2048, worker.LastSize);

        // With no log to go by, quiet time starts at the restart.
        var noLog = Adoption.Adopt(entry, slots: 2, Now, logSize: -1, logLastWrite: null).Worker;
        Assert.Equal(Now, noLog.LastProgress);
        Assert.Equal(-1, noLog.LastSize);
        Assert.Equal(Now.AddMinutes(10), Adoption.StopDeadline(noLog));
    }

    // WT-W5: an entry written before types has no type; it gets the type its slot was migrated from.
    [Fact]
    public void Adoption_EntryWithoutType_GetsMigratedType()
    {
        var pool = new WorkerPool
        {
            MaxWorkers = 4,
            Migrated = true,
            Types =
            [
                new WorkerType { Name = "worker", Max = 2, Slots = [1, 2] },
                new WorkerType { Name = "Opus", Max = 1, Slots = [4] },
            ],
        };

        Assert.Equal("Opus", Adoption.AdoptTyped(Entry(4), pool, Now, 100, Now).Type);
        Assert.Equal("worker", Adoption.AdoptTyped(Entry(2), pool, Now, 100, Now).Type);
        Assert.Equal("", Adoption.AdoptTyped(Entry(3), pool, Now, 100, Now).Type);   // no type from slot 3: counts toward the total only
        Assert.Equal("Sonnet", Adoption.AdoptTyped(Entry(2) with { Type = "Sonnet" }, pool, Now, 100, Now).Type);   // a type it wrote itself wins
        Assert.Equal(6, Adoption.AdoptTyped(Entry(6), pool, Now, 100, Now).Slot);   // the folder number is kept above maxWorkers
    }
}
