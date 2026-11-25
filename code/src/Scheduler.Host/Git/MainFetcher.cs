// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Core.Tick;

namespace Scheduler.Host.Git;

/// <summary>
/// 1063-B: a repository's model of main as the tick reads it (state-cache.md MM-8, MM-9, MM-14): the model, the
/// in-process read of the tracking ref, and the fetch gate shared by every consumer of the repository.
/// <see cref="MainModelsRepository"/> in production; a test's over <c>FakeMain</c>.
/// </summary>
public interface IMainFeed
{
    /// <summary>The model, or null before its first load (the readers then go through the git CLI as before).</summary>
    IMainModel? Model { get; }

    /// <summary>The fetch gate (MM-9).</summary>
    FetchGate Gate { get; }

    /// <summary>MM-8: reads the tracking ref in process and advances the model when it moved; never throws.</summary>
    bool Tick();

    /// <summary>MM-9: the gate's answer for <paramref name="why"/>; a fetch that ran is followed by the ref read.</summary>
    FetchAnswer Fetch(FetchAsk why);

    /// <summary>The process's model of <paramref name="repo"/> (<see cref="MainModels.Shared"/>) when one follows the same remote and branch, else null.</summary>
    static IMainFeed? Shared(string repo, string remote, string branch) =>
        repo.Length > 0
        && MainModels.Shared.For(repo) is { } model
        && string.Equals(model.Remote, remote, StringComparison.Ordinal)
        && string.Equals(model.Branch, branch, StringComparison.Ordinal)
            ? model
            : null;
}

/// <summary>
/// 1002-B: decides when the card read pays a network fetch. A poll, a worker exit, a planner batch
/// and a health check fetch; a click or a move request reads the local remote ref, which the
/// background git writer (987-B) moves itself when it pushes. When the wake is not known the fetch
/// is made once the poll interval has passed since the last one. A failed fetch, or a worker that
/// finished, makes the next read fetch. 1063-B: once the repository's model is there (<see cref="Feed"/>), the fetch
/// is the model's fetch gate's (MM-9: one per repository per poll, shared), and every read that does not fetch
/// reads the tracking ref in process instead (MM-8), so an idle tick starts no git unless a fetch is due (MM-15).
/// </summary>
public sealed class MainFetcher
{
    private readonly IMainRepository _repo;
    private readonly IClock _clock;
    private readonly TimeSpan _interval;
    private TickWake? _wake;
    private bool _asked = true;   // the first read always fetches
    private bool _ended;          // 1309-B: a worker ended and no fetch since its end has completed
    private long? _need;          // 1309-B: the background fetch run whose success makes the read fresh again
    private DateTimeOffset? _last;

    /// <summary>A fetcher over <paramref name="repo"/>; <paramref name="interval"/> is the poll interval.</summary>
    public MainFetcher(IMainRepository repo, IClock clock, TimeSpan interval)
    {
        _repo = repo;
        _clock = clock;
        _interval = interval;
    }

    /// <summary>1063-B: the repository's model; while it is null, or has not loaded, the fetch is the CLI's as before.</summary>
    public IMainFeed? Feed { get; set; }

    /// <summary>The wake of the tick that is about to read; taken by the next <see cref="Fetch"/>.</summary>
    public void Woke(TickWake wake) => _wake = wake;

    /// <summary>Makes the next <see cref="Fetch"/> fetch (a worker finished: its pushes are on the remote only).</summary>
    public void Ask()
    {
        _asked = true;
        _ended = true;
        if (Feed is { } feed)
        {
            _need ??= BackgroundFetch.For(feed).NextRun;   // a run that started before the end may not have seen it
            feed.Gate.WorkerEnded();
        }
    }

    /// <summary>
    /// Fetches when this tick needs it; false when a fetch was made and failed (CARD-3), and, 1309-B, when a worker
    /// ended and no fetch that started after its end has completed (the read is then older than the run's end).
    /// </summary>
    public bool Fetch()
    {
        var wake = _wake;
        _wake = null;
        if (Feed is { Model: not null } feed)
        {
            return FromGate(feed, wake);
        }

        _ = Feed?.Tick();   // not loaded yet: this starts the model's load in the background (MM-5)

        if (!Wanted(wake))
        {
            return true;
        }

        var now = _clock.Now;
        if (!_repo.Fetch())
        {
            _asked = true;
            return false;
        }

        _asked = false;
        _ended = false;
        _last = now;
        return true;
    }

    /// <summary>MM-9's ask for a tick woken by <paramref name="wake"/>; an unknown wake follows the poll.</summary>
    public static FetchAsk AskFor(TickWake? wake) => wake switch
    {
        TickWake.Click => FetchAsk.Click,
        TickWake.MoveRequest => FetchAsk.MoveRequest,
        TickWake.PlannerBatch => FetchAsk.Planner,
        TickWake.HealthCheck => FetchAsk.Health,
        TickWake.LockRequest => FetchAsk.Audit,
        _ => FetchAsk.Poll,
    };

    private bool FromGate(IMainFeed feed, TickWake? wake)
    {
        var ended = _ended || wake == TickWake.WorkerExit;
        if (ended || _asked)
        {
            feed.Gate.WorkerEnded();   // as before: a worker's exit, or the first read, fetches
        }

        _asked = false;
        if (ended)
        {
            // 1309-B: what a worker pushed reaches the tracking ref only through a fetch when its own update of the
            // ref was lost, so until a fetch that started after the worker's end has completed the read is not fresh:
            // the tick keeps its cards and does not plan (SchedulerTick, DISP-8). Only such a tick waits, for that
            // fetch alone and at most one fetch interval (never for a fetch from before the end, 1124-B); a fetch that
            // completes later wakes a tick (Moved). A click still never fetches nor waits.
            var background = BackgroundFetch.For(feed);
            var need = _need ??= background.NextRun;
            var fetch = AskFor(wake) is not (FetchAsk.Click or FetchAsk.MoveRequest);
            var fresh = background.FreshSince(need, fetch ? _interval : TimeSpan.Zero);
            _ = feed.Tick();
            if (fresh)
            {
                _ended = false;
                _need = null;
            }

            return fresh;
        }

        // 1124-B: the gate's fetch runs on the repository's background fetcher; the tick never waits for it and
        // reads the model as it stands (MM-8: the last completed fetch, and what the writer pushed).
        var ok = BackgroundFetch.For(feed).Start(AskFor(wake));
        _ = feed.Tick();
        return ok;   // CARD-3: false once after a background fetch failed (the gate fetches again on the next ask)
    }

    private bool Wanted(TickWake? wake) => _asked || wake switch
    {
        TickWake.Click or TickWake.MoveRequest => false,
        null => _last is not { } last || _clock.Now - last >= _interval,
        _ => true,
    };
}

/// <summary>
/// 1124-B (state-cache.md MM-9): a repository's fetch off the tick thread. One per feed, so one per repository (the
/// feed is shared by every project in it, <see cref="MainModels"/>); at most one fetch in flight, and the fetch gate
/// still decides whether an ask fetches at all (reused up to one poll interval). A completed fetch that moves the
/// model raises <see cref="Moved"/>, which wakes the ticks that read it. The writer's own fetch before it writes is
/// its own (AnswersCommitter), not this.
/// </summary>
public sealed class BackgroundFetch
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IMainFeed, BackgroundFetch> s_all = new();

    private readonly IMainFeed _feed;
    private readonly Lock _lock = new();
    private Task _running = Task.CompletedTask;
    private bool _failed;
    private long _runs;                    // 1309-B: runs started, numbered from 1
    private long _fresh;                   // 1309-B: the highest run that completed without failing
    private long _wakeAt = long.MaxValue;  // 1309-B: a reader waits for this run or a later one

    private BackgroundFetch(IMainFeed feed) => _feed = feed;

    /// <summary>Raised on the fetch's thread when a completed fetch moved the model.</summary>
    public event Action? Moved;

    /// <summary>The fetch in flight, or the last one (completed); a test waits on it.</summary>
    public Task Running
    {
        get
        {
            lock (_lock)
            {
                return _running;
            }
        }
    }

    /// <summary>The background fetcher of <paramref name="feed"/>'s repository.</summary>
    public static BackgroundFetch For(IMainFeed feed) => s_all.GetValue(feed, f => new BackgroundFetch(f));

    /// <summary>
    /// Asks the gate for <paramref name="why"/> on a background task, unless a fetch is in flight (that one answers
    /// it), and returns at once. False when the last completed fetch failed, reported once (CARD-3).
    /// </summary>
    public bool Start(FetchAsk why)
    {
        lock (_lock)
        {
            var failed = _failed;
            _failed = false;
            if (why is not (FetchAsk.Click or FetchAsk.MoveRequest) && _running.IsCompleted)
            {
                StartLocked(why);
            }

            return !failed;
        }
    }

    /// <summary>1309-B: the number the next run will have; a run with it or a later one starts after this call.</summary>
    public long NextRun
    {
        get
        {
            lock (_lock)
            {
                return _runs + 1;
            }
        }
    }

    /// <summary>
    /// 1309-B: true when a run numbered <paramref name="run"/> or later has completed without failing (it fetched, or
    /// shared a fetch the gate made after <see cref="FetchGate.WorkerEnded"/>). Otherwise, when <paramref name="wait"/>
    /// is above zero, starts a poll's fetch unless one is in flight, and waits up to <paramref name="wait"/> for it when
    /// it is such a run (never for a run from before). Still not fresh: the success of such a run raises
    /// <see cref="Moved"/> even when main did not move, so the reader's next tick comes at once.
    /// </summary>
    public bool FreshSince(long run, TimeSpan wait)
    {
        Task? mine = null;
        lock (_lock)
        {
            if (_fresh >= run)
            {
                return true;
            }

            if (wait > TimeSpan.Zero)
            {
                if (_running.IsCompleted)
                {
                    StartLocked(FetchAsk.Poll);
                }

                if (_runs >= run)
                {
                    mine = _running;   // the run in flight started after the caller's point
                }
            }
        }

        _ = mine?.Wait(wait);
        lock (_lock)
        {
            if (_fresh >= run)
            {
                return true;
            }

            _wakeAt = Math.Min(_wakeAt, run);   // a completion after this wakes a tick
            return false;
        }
    }

    private void StartLocked(FetchAsk why)
    {
        var run = ++_runs;
        _running = Task.Run(() => Run(why, run));
    }

    private void Run(FetchAsk why, long run)
    {
        FetchAnswer answer;
        try
        {
            answer = _feed.Gate.Ask(why);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            answer = FetchAnswer.Failed;
        }

        if (answer == FetchAnswer.Failed)
        {
            lock (_lock)
            {
                _failed = true;
            }

            return;
        }

        bool wanted;
        lock (_lock)
        {
            _fresh = Math.Max(_fresh, run);
            wanted = run >= _wakeAt;
            if (wanted)
            {
                _wakeAt = long.MaxValue;
            }
        }

        try
        {
            if ((answer == FetchAnswer.Fetched && _feed.Tick()) || wanted)
            {
                Moved?.Invoke();   // 1309-B: also when a reader waits for this run, moved or not
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // a waker that throws must not end the fetcher; the next tick reads the ref anyway (MM-8)
        }
    }
}
