// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Core.Tick;
using Scheduler.Host.Program;

namespace Scheduler.Host.Processes;

/// <summary>
/// Notices a worker process ending at once (TIME-21): the wait for the next tick wakes when a
/// watched process exits, not only at the poll interval. Works for processes this program
/// started and for adopted ones alike, as it watches by pid.
/// </summary>
public sealed class WorkerExitWatcher : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Dictionary<int, Process> _watched = new();
    private readonly object _kindLock = new();
    private TickWake? _pending;

    /// <summary>Watch the process with this pid; one that has already ended signals at once (982-B: a worker exit).</summary>
    public void Watch(int pid) => Watch(pid, once: false);

    /// <summary>966-B: wake the loop when this process (the health check) ends, and forget it then.</summary>
    public void WatchOnce(int pid) => Watch(pid, once: true);

    /// <summary>966-B: wake the loop now for a cause that is not known (982-B: the full tick).</summary>
    public void Wake() => Signal(TickWake.Poll);

    /// <summary>982-B: wake the loop now for <paramref name="kind"/> (a page action, a move request, a planner batch).</summary>
    public void Wake(TickWake kind) => Signal(kind);

    /// <summary>
    /// 982-B: the kinds signalled since the last take, merged (<see cref="TickWakes.Merge"/>), which it
    /// then forgets; null when nothing was signalled.
    /// </summary>
    public TickWake? TakeWake()
    {
        lock (_kindLock)
        {
            var kind = _pending;
            _pending = null;
            return kind;
        }
    }

    private void Watch(int pid, bool once)
    {
        var kind = once ? TickWake.HealthCheck : TickWake.WorkerExit;
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
            process.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Signal(kind); // gone already: the next tick will see it
            return;
        }
        lock (_watched)
        {
            _watched[pid] = process;
        }
        process.Exited += (_, _) =>
        {
            Signal(kind);
            if (once)
            {
                Forget(pid);
            }
        };
        if (process.HasExited)
        {
            Signal(kind);
        }
    }

    /// <summary>Stop watching a pid.</summary>
    public void Forget(int pid)
    {
        lock (_watched)
        {
            if (_watched.Remove(pid, out var p))
            {
                p.Dispose();
            }
        }
    }

    /// <summary>
    /// Wait until <paramref name="poll"/> has passed or a watched process has exited, whichever is
    /// first. True if it was an exit.
    /// </summary>
    public bool WaitForTick(TimeSpan poll, CancellationToken cancel = default) => _signal.Wait(poll, cancel);

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_watched)
        {
            foreach (var p in _watched.Values)
            {
                p.Dispose();
            }
            _watched.Clear();
        }
        _signal.Dispose();
    }

    private void Signal(TickWake kind)
    {
        lock (_kindLock)
        {
            _pending = TickWakes.Merge(_pending, kind);
        }

        try
        {
            _ = _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A tick is already due.
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
