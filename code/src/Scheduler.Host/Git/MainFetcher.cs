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
        Feed?.Gate.WorkerEnded();
    }

    /// <summary>Fetches when this tick needs it; false only when a fetch was made and failed (CARD-3).</summary>
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
        if (wake == TickWake.WorkerExit || _asked)
        {
            feed.Gate.WorkerEnded();   // as before: a worker's exit, or the first read, fetches
        }

        _asked = false;
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
                _running = Task.Run(() => Run(why));
            }

            return !failed;
        }
    }

    private void Run(FetchAsk why)
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

        try
        {
            if (answer == FetchAnswer.Fetched && _feed.Tick())
            {
                Moved?.Invoke();
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // a waker that throws must not end the fetcher; the next tick reads the ref anyway (MM-8)
        }
    }
}
