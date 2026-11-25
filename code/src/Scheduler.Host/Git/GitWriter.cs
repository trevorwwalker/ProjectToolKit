// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Runtime.ExceptionServices;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;

namespace Scheduler.Host.Git;

/// <summary>
/// 987-B: the one writer of commits and pushes to main. Planner batches, move requests, claims and
/// page actions are handed to it and run on its own thread, one at a time, in the order they came,
/// so the tick never waits on a push. <see cref="Post"/> hands work over and returns; <see cref="Run{T}"/>
/// is for a caller that needs the result (a page action, a finish) and waits its turn. A write that
/// returns true moved main, and <see cref="Moved"/> says so (the tick re-reads the cards). A write that
/// throws is logged and dropped; whoever posted it posts it again (the tick does so every tick).
/// 1066-B (state-cache.md MM-13): <see cref="PushStep"/>s of one owner (one repository) that are waiting together are one
/// commit each and one push for all, in their order; a rejected push retries them one by one (<see cref="PushBatch"/>).
/// </summary>
public sealed class GitWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<Item> _todo = new();
    private readonly HashSet<string> _waiting = new(StringComparer.Ordinal);
    private readonly ISchedulerLog _log;
    private Thread? _thread;
    private bool _busy;
    private bool _stopping;

    /// <summary>A writer that logs failed writes to <paramref name="log"/>.</summary>
    public GitWriter(ISchedulerLog log) => _log = log;

    /// <summary>Called on the writer's thread after a write that moved main.</summary>
    public Action? Moved { get; set; }

    /// <summary>Nothing waiting and nothing running.</summary>
    public bool Idle
    {
        get
        {
            lock (_gate)
            {
                return !_busy && _todo.Count == 0;
            }
        }
    }

    /// <summary>
    /// Hands <paramref name="work"/> to the writer and returns at once. False, and nothing queued, when a
    /// write with the same <paramref name="key"/> is still waiting: that one does the same work.
    /// </summary>
    public bool Post(string key, Func<bool> work)
    {
        lock (_gate)
        {
            if (_stopping || !_waiting.Add(key))
            {
                return false;
            }

            _todo.Enqueue(new Item(key, work, null));
            StartLocked();
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> in its turn and returns its result; an exception reaches the caller.
    /// On the writer's own thread, or once the writer has stopped, it runs at once.
    /// </summary>
    public T Run<T>(string key, Func<T> work)
    {
        if (Thread.CurrentThread == _thread)
        {
            return work();
        }

        T result = default!;
        using var done = new ManualResetEventSlim(false);
        var item = new Item(key, () =>
        {
            result = work();
            return false;
        }, done);
        lock (_gate)
        {
            if (_stopping)
            {
                return work();
            }

            _todo.Enqueue(item);
            StartLocked();
            Monitor.PulseAll(_gate);
        }

        done.Wait();
        item.Error?.Throw();
        return result;
    }

    /// <summary>
    /// MM-13: runs <paramref name="step"/> in its turn and waits for it. Steps of the same owner that are waiting together
    /// share one push; a step on the writer's own thread (or after the writer stopped) runs at once, alone.
    /// </summary>
    public void RunPushed(string key, PushStep step)
    {
        if (Thread.CurrentThread == _thread)
        {
            RunGroup([step]);
            return;
        }

        using var done = new ManualResetEventSlim(false);
        var item = new Item(key, () => false, done) { Step = step };
        lock (_gate)
        {
            if (_stopping)
            {
                RunGroup([step]);
                return;
            }

            _todo.Enqueue(item);
            StartLocked();
            Monitor.PulseAll(_gate);
        }

        done.Wait();
        item.Error?.Throw();
    }

    /// <summary>
    /// MM-13: one commit per step, in order, then one push for all; if it is rejected the steps are redone one by one from
    /// the new main, two more tries each (three pushes in all, as a lone commit always had). Each step's
    /// <see cref="PushStep.Finish"/> gets its own outcome, in order. Returns the outcomes.
    /// </summary>
    public static IReadOnlyList<PushOutcome> RunGroup(IReadOnlyList<PushStep> steps)
    {
        var first = steps[0];
        first.Reset();
        var outcomes = PushBatch.Run(steps, s => s.Commit(), first.Push, first.Reset, tries: 2);
        for (var i = 0; i < steps.Count; i++)
        {
            steps[i].Finish(outcomes[i]);
        }

        return outcomes;
    }

    /// <summary>Waits until <see cref="Idle"/>; false when <paramref name="timeout"/> passed first.</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (_busy || _todo.Count > 0)
            {
                var left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero)
                {
                    return false;
                }

                Monitor.Wait(_gate, left);
            }

            return true;
        }
    }

    /// <summary>Lets the write that is running finish, runs nothing more, and stops the thread.</summary>
    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            _stopping = true;
            thread = _thread;
            Monitor.PulseAll(_gate);
        }

        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(30));
        }

        // Callers of Run still waiting are let go: the writer that would have run them is gone.
        lock (_gate)
        {
            while (_todo.TryDequeue(out var left))
            {
                left.Error = ExceptionDispatchInfo.Capture(new ObjectDisposedException(nameof(GitWriter)));
                left.Done?.Set();
            }
        }
    }

    private void StartLocked()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Loop) { IsBackground = true, Name = "scheduler-git-writer" };
        _thread.Start();
    }

    private void Loop()
    {
        while (true)
        {
            Item item;
            var group = new List<Item>();
            lock (_gate)
            {
                while (_todo.Count == 0 && !_stopping)
                {
                    Monitor.Wait(_gate);
                }

                if (_stopping)
                {
                    return;
                }

                item = _todo.Dequeue();
                if (item.Done is null)
                {
                    _waiting.Remove(item.Key);
                }

                group.Add(item);
                while (item.Step is { } owner && _todo.Count > 0 && _todo.Peek().Step is { } next
                    && ReferenceEquals(next.Owner, owner.Owner))
                {
                    group.Add(_todo.Dequeue());
                }

                _busy = true;
            }

            var moved = false;
            try
            {
                if (item.Step is not null)
                {
                    moved = RunGroup([.. group.Select(g => g.Step!)]).Contains(PushOutcome.Pushed);
                }
                else
                {
                    moved = item.Work();
                }
            }
            catch (Exception e)
            {
                if (item.Done is null)
                {
                    _log.Write($"git writer: {item.Key} failed: {e.Message}; it is tried again when next asked for");
                }
                else
                {
                    foreach (var g in group)
                    {
                        g.Error = ExceptionDispatchInfo.Capture(e);
                    }
                }
            }

            foreach (var g in group.Skip(1))
            {
                g.Done?.Set();
            }

            item.Done?.Set();
            if (moved)
            {
                try
                {
                    Moved?.Invoke();
                }
                catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
                {
                    _log.Write($"git writer: could not wake the tick: {e.Message}");
                }
            }

            lock (_gate)
            {
                _busy = false;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private sealed class Item(string key, Func<bool> work, ManualResetEventSlim? done)
    {
        public string Key { get; } = key;

        public Func<bool> Work { get; } = work;

        public ManualResetEventSlim? Done { get; } = done;

        public ExceptionDispatchInfo? Error { get; set; }

        public PushStep? Step { get; init; }
    }

    /// <summary>
    /// MM-13: one item of a group that goes in one push. <see cref="Owner"/> says which items may share a push (one
    /// repository's committer); the delegates are the owner's git steps (see <see cref="PushBatch.Run{T}"/>).
    /// </summary>
    public sealed class PushStep(object owner, Func<bool> commit, Func<bool> push, Action reset, Action<PushOutcome> finish)
    {
        /// <summary>Steps with the same owner (by reference) may share a push.</summary>
        public object Owner { get; } = owner;

        /// <summary>Makes this item's commit on the current state; false when it cannot be made.</summary>
        public Func<bool> Commit { get; } = commit;

        /// <summary>Pushes everything committed; false when rejected or failed.</summary>
        public Func<bool> Push { get; } = push;

        /// <summary>Takes the remote's main and drops commits not pushed.</summary>
        public Action Reset { get; } = reset;

        /// <summary>Told this item's own outcome, in order, once the group is done.</summary>
        public Action<PushOutcome> Finish { get; } = finish;
    }
}
