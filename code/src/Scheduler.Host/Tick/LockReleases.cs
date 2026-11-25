// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Locks;
using Scheduler.Core.Results;

namespace Scheduler.Host.Tick;

/// <summary>
/// Dynamic locks in the host (design <c>dynamic-locks.md</c>, DL-8, DL-9; card 1020-B): when a card's held
/// set is released. A worker's move request applied on main releases it - To: done or review as finished,
/// To: active or queue as parked - and a run's end releases it as yielded or ended, unless a move request of
/// the card is still waiting to be applied: a finish that could not be pushed keeps its locks until it is on
/// main. The move step reports from the git writer's thread; the table is changed only on the tick's thread.
/// </summary>
public sealed class LockReleases
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Card, CardFolder To, bool Applied)> _moves = new();
    private readonly LockTableFile _file;
    private readonly Func<string, bool> _running;
    private readonly Func<string, bool> _pendingMove;
    private readonly ISchedulerLog _log;

    /// <param name="file">The repository's lock table.</param>
    /// <param name="running">True when a worker runs the card now.</param>
    /// <param name="pendingMove">True when a move request of the card is still waiting in the moves folder.</param>
    /// <param name="log">The scheduler's log.</param>
    public LockReleases(LockTableFile file, Func<string, bool> running, Func<string, bool> pendingMove, ISchedulerLog log)
    {
        _file = file;
        _running = running;
        _pendingMove = pendingMove;
        _log = log;
    }

    /// <summary>A worker's move request was filed: applied on main, or failed. Any thread.</summary>
    public void MoveFiled(string card, CardFolder to, bool applied) => _moves.Enqueue((card, to, applied));

    /// <summary>
    /// DL-8 (a, b): each move filed since the last call. A move that failed releases a card no worker runs
    /// any more as ended (its run is over and nothing else would release it). Tick's thread.
    /// </summary>
    public void ReleaseMoved()
    {
        while (_moves.TryDequeue(out var m))
        {
            var running = _running(m.Card);
            Update(t => m.Applied ? t.OnMove(m.Card, m.To, applied: true)
                : running ? t : t.Release(m.Card, LockRelease.Ended));
        }
    }

    /// <summary>
    /// DL-8 (c, d), DL-9: the run of <paramref name="card"/> ended with <paramref name="result"/> (after its
    /// move requests were applied). Tick's thread.
    /// </summary>
    public void RunEnded(string card, string result)
    {
        ReleaseMoved();
        if (_pendingMove(card))
        {
            _log.Write($"locks: {card} keeps its locks: its move request is not on main yet (DL-9)");
            return;
        }

        Update(t => t.Release(card, result == RunResults.Yielded ? LockRelease.Yielded : LockRelease.Ended));
    }

    private void Update(Func<LockTable, LockTable> change)
    {
        var before = _file.Read();
        var after = change(before);
        if (LockTableText.Write(after) == LockTableText.Write(before))
        {
            return;
        }

        _file.Save(after);
        foreach (var gone in before.Holding.Where(c => after.Find(c.Card) is not { Holds: true }))
        {
            _log.Write($"locks: {gone.Card} released");   // DL-26
        }
    }
}
