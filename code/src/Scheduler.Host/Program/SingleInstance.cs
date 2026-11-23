namespace Scheduler.Host.Program;

/// <summary>
/// One scheduler per project (CFG-14, L4004-4008): the named mutex <c>Global\ProjectToolkit-&lt;project&gt;-scheduler</c>,
/// waited for 60 s; an abandoned mutex counts as owned.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>The spec's wait.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(60);

    private Mutex? _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>The mutex's name for the project.</summary>
    public static string MutexName(string project) => $"Global\\ProjectToolkit-{project}-scheduler";

    /// <summary>Take the project's mutex, waiting up to <paramref name="wait"/>; fails with the spec's message.</summary>
    public static SingleInstance Acquire(string project, TimeSpan wait) =>
        TryAcquire(project, wait) ?? throw new SchedulerStartException($"Another {project} scheduler is still running");

    /// <summary>
    /// MP-H1: take the project's mutex within <paramref name="wait"/>, or null when another scheduler of
    /// the project holds it (a pool then leaves the project out and tries again later).
    /// </summary>
    public static SingleInstance? TryAcquire(string project, TimeSpan wait)
    {
        var mutex = new Mutex(false, MutexName(project));
        bool owned;
        try
        {
            owned = mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // its owner died without releasing it: now ours
        }

        if (!owned)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex);
    }

    /// <summary>Release the mutex (PAGE-12); a second instance can start at once.</summary>
    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // not owned by this thread: nothing to release
        }

        mutex.Dispose();
    }
}
