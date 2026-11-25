// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Host.Program;

namespace Scheduler.Host.Tests.Program;

public class SingleInstanceTests
{
    private static string NewProject() => "test-" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Takes the project's mutex on its own thread, as another scheduler process would.</summary>
    private sealed class Holder : IDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;

        public Holder(string project, bool abandon)
        {
            using var held = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                var mutex = new Mutex(false, SingleInstance.MutexName(project));
                mutex.WaitOne();
                held.Set();
                if (!abandon)
                {
                    _release.Wait();
                    mutex.ReleaseMutex();
                }
                // abandon: the thread ends still owning the mutex
            }) { IsBackground = true };
            _thread.Start();
            held.Wait();
        }

        public void Join() => _thread.Join();

        public void Dispose()
        {
            _release.Set();
            _thread.Join();
        }
    }

    // CFG-14: a second scheduler for the same project fails after the wait (1 s here, 60 s in the spec).
    [Fact]
    public void Config_SecondScheduler_SameProject_FailsAfterWait()
    {
        var project = NewProject();
        using var other = new Holder(project, abandon: false);

        var started = DateTime.UtcNow;
        var e = Assert.Throws<SchedulerStartException>(() => SingleInstance.Acquire(project, TimeSpan.FromSeconds(1)));

        Assert.Equal($"Another {project} scheduler is still running", e.Message);
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(900));
    }

    // CFG-14: an abandoned mutex counts as owned.
    [Fact]
    public void Config_AbandonedMutex_CountsAsOwned()
    {
        var project = NewProject();
        new Holder(project, abandon: true).Join();

        using var mine = SingleInstance.Acquire(project, TimeSpan.FromSeconds(5));

        // and it is a real hold: another one now has to wait
        Assert.Throws<SchedulerStartException>(() => OnAnotherThread(() => SingleInstance.Acquire(project, TimeSpan.FromMilliseconds(100))));
    }

    private static void OnAnotherThread(Action action)
    {
        Exception? caught = null;
        var t = new Thread(() =>
        {
            try { action(); } catch (Exception e) { caught = e; }
        });
        t.Start();
        t.Join();
        if (caught is not null)
        {
            throw caught;
        }
    }
}
