using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;

namespace Scheduler.Host.Program;

/// <summary>Everything <see cref="SchedulerRunner"/> calls, so the start-up order and the loop run without a real git, process or socket.</summary>
public sealed class RunnerParts
{
    /// <summary>The configuration.</summary>
    public required SchedulerConfig Config { get; init; }

    /// <summary>The clock.</summary>
    public required IClock Clock { get; init; }

    /// <summary>The scheduler log.</summary>
    public required ISchedulerLog Log { get; init; }

    /// <summary>The version shown in the start line (CFG-18).</summary>
    public string Version { get; init; } = "";

    /// <summary>How long to wait for the mutex (CFG-14); the spec's 60 s by default.</summary>
    public TimeSpan MutexWait { get; init; } = SingleInstance.DefaultWait;

    /// <summary>1015-B: the poll in seconds, asked at each wait; null = <see cref="Config"/>'s (a pool's changes as its projects join).</summary>
    public Func<int>? PollSeconds { get; init; }

    /// <summary>Adopt the workers in running.json (CFG-15, A9).</summary>
    public required Action AdoptWorkers { get; init; }

    /// <summary>Seed recent events and run stats from accounting.csv (CFG-15, A8).</summary>
    public required Action SeedFromAccounting { get; init; }

    /// <summary>Start the page listener (CFG-15, CFG-17).</summary>
    public required Func<IPageListener> StartListener { get; init; }

    /// <summary>Open the page in the browser; not called in a dry run (CFG-15).</summary>
    public required Action<string> OpenBrowser { get; init; }

    /// <summary><c>-NoBrowser</c> was given (670-B): the page is not opened at start (CFG-15).</summary>
    public bool NoBrowser { get; init; }

    /// <summary>
    /// Whether something (the tray, 670-B) has asked this scheduler to end by itself and leave its
    /// workers running - the clean shutdown path, as opposed to a console Ctrl+C, which would reach the
    /// workers too because they share the scheduler's console. Null when nothing can ask.
    /// </summary>
    public Func<bool>? ShutdownRequested { get; init; }

    /// <summary>One tick (A3), for the kind of wake that started it (982-B).</summary>
    public required Action<TickWake> Tick { get; init; }

    /// <summary>
    /// 982-B: the kinds signalled since the last call, merged; null when none. When set, a tick runs
    /// only when something is pending, the poll is due or the page asked, so a signal whose kind an
    /// earlier tick already took does not start another. Null: every signal ticks, as the poll.
    /// </summary>
    public Func<TickWake?>? TakeWake { get; init; }

    /// <summary>The scheduler is draining (PAGE-12).</summary>
    public required Func<bool> Draining { get; init; }

    /// <summary>The number of workers running (PAGE-12).</summary>
    public required Func<int> WorkersRunning { get; init; }

    /// <summary>Write the status file (PAGE-12).</summary>
    public required Action WriteStatusFile { get; init; }

    /// <summary>
    /// Wait up to the given time for something that wants a tick at once (a worker exit, TIME-21);
    /// true when it was signalled.
    /// </summary>
    public required Func<TimeSpan, CancellationToken, bool> WaitForTick { get; init; }

    /// <summary>966-B: ends <see cref="WaitForTick"/> at once (a page action asking for a tick).</summary>
    public Action? Wake { get; init; }

    /// <summary>966-B: true while the page is in fast mode; the loop then waits <see cref="SchedulerRunner.FastStep"/>.</summary>
    public Func<bool>? Fast { get; init; }

    /// <summary>
    /// MM-18 (1063-B): the process's git processes started and the model's advance time so far, in milliseconds;
    /// the tick's cost line reports what one tick added. Null leaves them out.
    /// </summary>
    public Func<(long Git, double ModelMs)>? Counters { get; init; }
}
