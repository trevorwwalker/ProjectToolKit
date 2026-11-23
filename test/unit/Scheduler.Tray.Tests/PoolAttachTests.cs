using Scheduler.Tray;
using Xunit;

namespace Scheduler.Tray.Tests;

/// <summary>
/// 1013-B, MP-H1, 975-B: a <c>-Pool</c> tray started while a pool runs (the deploy's) attaches to it by the pool
/// file's shutdown event and starts no second scheduler; with none running it starts one as before.
/// </summary>
public sealed class PoolAttachTests : IDisposable
{
    private readonly string _dir = Scheduler.TestSupport.TestTmp.NewPath("pat");

    public PoolAttachTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void PoolRunning_IsAttachedTo_NotStartedAgain()
    {
        var poolFile = Path.Combine(_dir, "pool.json");
        // What a running pool holds (Scheduler.Host's PoolComposition.ShutdownName: RepoShutdown of the pool file).
        using var running = new EventWaitHandle(false, EventResetMode.ManualReset, RepoShutdown.Name(poolFile));
        var args = TrayArguments.Parse(["-Pool", poolFile]);

        using var attached = AttachedScheduler.TryAttachPool(AttachedScheduler.PoolFileOf(args, _dir), Path.Combine(_dir, "scheduler-next.exe"), new LogBuffer());

        Assert.NotNull(attached);
    }

    [Fact]
    public void NoPoolRunning_NothingToAttachTo()
    {
        var args = TrayArguments.Parse(["-Pool", Path.Combine(_dir, "pool.json")]);

        Assert.Null(AttachedScheduler.TryAttachPool(AttachedScheduler.PoolFileOf(args, _dir), Path.Combine(_dir, "scheduler-next.exe"), new LogBuffer()));
    }

    // MP-C1: -Pool without a file means the default pool file, the one the scheduler reads and names its event by.
    [Fact]
    public void PoolFileOf_NoFileGiven_IsTheDefault()
    {
        var args = TrayArguments.Parse(["-Pool"]);

        Assert.Equal(Path.Combine(_dir, "ProjectToolkit", "pool.json"), AttachedScheduler.PoolFileOf(args, _dir));
        Assert.Equal(@"C:\p.json", AttachedScheduler.PoolFileOf(TrayArguments.Parse(["-Pool", @"C:\p.json"]), _dir));
    }
}
