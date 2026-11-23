using Scheduler.Core.Tick;
using Scheduler.Host.Pool;
using Scheduler.Host.Program;
using Xunit;

namespace Scheduler.Host.Tests.Pool;

/// <summary>
/// 1013-B: a pool start that leaves one scheduler with every project: the pool's mutex is taken before any
/// project's (MP-H1), a project left out joins at the first tick after its lock frees or is abandoned, each retry
/// is logged, and the pool has a shutdown event a tray finds it by (975-B).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[ParallelMethods]
public class PoolStartTests
{
    // MP-H1, 1013-B: the lock freed, the project joins at the next tick, not a minute later.
    [Fact]
    public void MP_H1_LockFreed_JoinsAtTheNextTick()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var held = new Holder(rig.A, abandon: false);
        using var pool = rig.Compose();
        Assert.Equal(PoolComposition.OwnSchedulerRunning, pool.LeftOut[rig.A]);

        held.Let();
        pool.RunTick(TickWake.Poll);
        Assert.True(pool.WaitForJoins(TimeSpan.FromSeconds(60))); // 1015-B: the join started at the tick runs on its own task

        Assert.Equal([rig.A, rig.B], pool.Tick.Projects.Select(p => p.Name));
        Assert.Empty(pool.LeftOut);
        Assert.Contains(rig.Log.Lines, l => l.Contains($"pool: {rig.A} joined", StringComparison.Ordinal));
    }

    // MP-H1, 1013-B: a scheduler that died holding the lock leaves it abandoned, which counts as free.
    [Fact]
    public void MP_H1_LockAbandoned_JoinsAtTheNextTick()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var held = new Holder(rig.A, abandon: true);
        using var pool = rig.Compose();
        Assert.Equal(PoolComposition.OwnSchedulerRunning, pool.LeftOut[rig.A]);

        held.Let();
        pool.RunTick(TickWake.Poll);
        Assert.True(pool.WaitForJoins(TimeSpan.FromSeconds(60))); // 1015-B: the join started at the tick runs on its own task

        Assert.Equal([rig.A, rig.B], pool.Tick.Projects.Select(p => p.Name));
    }

    // MP-H1, 1013-B: while the lock stays held each retry's outcome is logged, once a minute, not at every tick.
    [Fact]
    public void MP_H1_LockStillHeld_RetryOutcomeLoggedEachMinute()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var held = new Holder(rig.A, abandon: false);
        rig.Clock.MoveTo(DateTimeOffset.Now); // 1051-B: pinned, so a tick slower than a minute is not a second retry
        using var pool = rig.Compose();
        int Retries() => rig.Log.Lines.Count(l => l.Contains($"pool: {rig.A} tried again: still", StringComparison.Ordinal));

        pool.RunTick(TickWake.Poll);
        Assert.Equal(0, Retries());

        rig.Clock.MoveTo(rig.Clock.Now + PoolComposition.RetryEvery);
        pool.RunTick(TickWake.Poll);
        pool.RunTick(TickWake.Poll);

        Assert.Equal(1, Retries());
        Assert.Equal([rig.B], pool.Tick.Projects.Select(p => p.Name));
    }

    // MP-H1, 1013-B: a second pool started while the first is up refuses at once, before it takes any project:
    // the 20:27 start where the tray's pool won the pool mutex and the deploy's held both projects.
    [Fact]
    public void MP_H1_SecondPool_RefusesBeforeTakingAnyProject()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var first = rig.Compose();
        Exception? refused = null;
        var leftOutBefore = rig.Log.Lines.Count(l => l.Contains(PoolComposition.OwnSchedulerRunning, StringComparison.Ordinal));

        var other = new Thread(() =>
        {
            try
            {
                using var second = rig.Compose();
            }
            catch (SchedulerStartException e)
            {
                refused = e;
            }
        });
        other.Start();
        Assert.True(other.Join(TimeSpan.FromSeconds(30)));

        Assert.NotNull(refused);
        Assert.Contains("still running", refused.Message, StringComparison.Ordinal);
        Assert.Equal(leftOutBefore, rig.Log.Lines.Count(l => l.Contains(PoolComposition.OwnSchedulerRunning, StringComparison.Ordinal)));
        Assert.Equal([rig.A, rig.B], first.Tick.Projects.Select(p => p.Name));
    }

    // 975-B, 1013-B: while a pool runs, its shutdown event (named from its pool file) is there for a tray to find.
    [Fact]
    public void Pool_ShutdownEvent_ThereWhileItRuns()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        var name = PoolComposition.ShutdownName(rig.PoolFile);
        using (rig.Compose())
        {
            Assert.True(EventWaitHandle.TryOpenExisting(name, out var found));
            found.Dispose();
        }

        Assert.False(EventWaitHandle.TryOpenExisting(name, out _));
    }

    // The project's own scheduler, holding its mutex on a thread of its own; let go by releasing it, or by the
    // thread ending without a release (an abandoned mutex, as when the process dies).
    private sealed class Holder : IDisposable
    {
        private readonly ManualResetEventSlim _taken = new();
        private readonly ManualResetEventSlim _let = new();
        private readonly Thread _thread;

        public Holder(string project, bool abandon)
        {
            _thread = new Thread(() =>
            {
                using var mutex = new Mutex(false, SingleInstance.MutexName(project));
                _ = mutex.WaitOne(TimeSpan.FromSeconds(5));
                _taken.Set();
                _let.Wait();
                if (!abandon)
                {
                    mutex.ReleaseMutex();
                }
            });
            _thread.Start();
            Assert.True(_taken.Wait(TimeSpan.FromSeconds(10)));
        }

        public void Let()
        {
            _let.Set();
            Assert.True(_thread.Join(TimeSpan.FromSeconds(10)));
        }

        public void Dispose()
        {
            _let.Set();
            _ = _thread.Join(TimeSpan.FromSeconds(10));
            _taken.Dispose();
            _let.Dispose();
        }
    }
}
