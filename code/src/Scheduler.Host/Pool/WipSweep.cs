using Scheduler.Core.Contracts;
using Scheduler.Host.Git;

namespace Scheduler.Host.Pool;

/// <summary>One repository the sweep visits.</summary>
/// <param name="Name">The project's name, for the log.</param>
/// <param name="Dir">The repository.</param>
/// <param name="Remote">The remote.</param>
/// <param name="Branch">The main branch.</param>
/// <param name="DoneCards">The cards now in <c>done/</c> (file names without <c>.md</c>), or null when they cannot be read.</param>
/// <param name="Log">The project's log.</param>
public sealed record SweepRepo(string Name, string Dir, string Remote, string Branch, Func<IReadOnlyList<string>?> DoneCards, ISchedulerLog Log);

/// <summary>
/// 1053-B: wip branches of cards that are done, swept with 1049-B's rule (delete only when every changed file equals
/// main or main changed it later; keep and log otherwise) once after each start, once every pool member has joined,
/// and then once a day. It runs on a task of its own, so it never delays the loop or the start. One <c>ls-remote</c>
/// and one push per repository per sweep; a branch it keeps is logged once per day.
/// </summary>
public sealed class WipSweep
{
    /// <summary>How long after a sweep the next one runs.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromDays(1);

    private readonly IClock _clock;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, DateOnly> _keptLogged = new(StringComparer.Ordinal);
    private DateTimeOffset? _last;
    private Task _current = Task.CompletedTask;

    /// <param name="clock">The clock.</param>
    public WipSweep(IClock clock) => _clock = clock;

    /// <summary>The sweep under way, or a completed task; tests wait on it.</summary>
    public Task Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Called from each tick once every project has joined: starts a sweep on its own task when none has run since the
    /// start or the last was a day ago, and none is running. Returns at once.
    /// </summary>
    /// <returns>True when a sweep was started.</returns>
    public bool Tick(Func<IReadOnlyList<SweepRepo>> repos)
    {
        var now = _clock.Now;
        lock (_lock)
        {
            if (!_current.IsCompleted || (_last is { } last && now - last < Every))
            {
                return false;
            }

            _last = now;
            _current = Task.Run(() => Sweep(repos(), now));
            return true;
        }
    }

    /// <summary>Sweeps each repository; one that fails is logged and the next goes on.</summary>
    public void Sweep(IReadOnlyList<SweepRepo> repos, DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.DateTime);
        foreach (var repo in repos)
        {
            try
            {
                var done = repo.DoneCards();
                if (done is null || done.Count == 0)
                {
                    continue;
                }

                var wip = new WipBranches(new GitRunner(), repo.Remote, repo.Branch, repo.Log);
                if (wip.ListRemote(repo.Dir) is not { Count: > 0 } existing)
                {
                    continue;
                }

                // only the done cards that have a branch cost a fetch and a comparison
                var cards = done.Where(c => existing.Contains(WipBranches.BranchOf(c))).ToList();
                _ = wip.Clean(repo.Dir, cards, existing, branch => LogKept(repo.Name + " " + branch, day));
            }
#pragma warning disable CA1031 // 1053-B: a sweep never fails the pool; the next repository and the next day go on
            catch (Exception e)
#pragma warning restore CA1031
            {
                repo.Log.Write($"wip sweep of {repo.Name} failed: {e.Message}");
            }
        }
    }

    private bool LogKept(string key, DateOnly day)
    {
        lock (_lock)
        {
            if (_keptLogged.TryGetValue(key, out var was) && was == day)
            {
                return false;
            }

            _keptLogged[key] = day;
            return true;
        }
    }
}
