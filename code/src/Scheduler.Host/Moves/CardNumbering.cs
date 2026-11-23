using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Host.Git;

namespace Scheduler.Host.Moves;

/// <summary>
/// 1111-B (planner-outbox.md "Card numbers"): the pool's one counter of card numbers. The next number is one
/// more than the highest card number in any folder of any project in the pool, read from the models of main,
/// and never one handed out before in this run, so a number is unique across every project. Thread-safe: each
/// repository has its own git writer, and they all draw from here.
/// </summary>
public sealed class CardNumbering
{
    private readonly Func<long?> _highest;
    private readonly object _gate = new();
    private long _last;

    /// <param name="highest">The highest card number on main in the whole pool, or null while a model of main is not loaded.</param>
    public CardNumbering(Func<long?> highest) => _highest = highest;

    /// <summary>The process's counter, over <see cref="MainModels.Shared"/> (a pool's projects share it).</summary>
    public static CardNumbering Shared { get; } = new(() => Highest(MainModels.Shared.Repositories));

    /// <summary>Whether every model of main is loaded, so the counter can start above all of them.</summary>
    public bool Ready => _highest() is not null;

    /// <summary>The last number handed out, 0 before the first.</summary>
    public long Last
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    /// <summary>The next number: above the highest on main and above the last handed out.</summary>
    public long Next()
    {
        lock (_gate)
        {
            _last = Math.Max(_last, _highest() ?? 0) + 1;
            return _last;
        }
    }

    /// <summary>The highest card number in every jobs folder of <paramref name="repositories"/>, or null when one has no model yet.</summary>
    public static long? Highest(IEnumerable<MainModelsRepository> repositories)
    {
        long high = 0;
        foreach (var repo in repositories)
        {
            if (repo.Model?.Snapshot is not { } at)
            {
                return null;
            }

            foreach (var jobsDir in repo.JobsDirs)
            {
                high = Math.Max(high, CardNumbers.Highest(jobsDir, at.Files.Keys));
            }
        }

        return high;
    }

    /// <summary>The highest card number in <paramref name="snapshots"/> (each with its jobs folders); for tests and callers without a pool.</summary>
    public static long Highest(IEnumerable<(MainSnapshot Snapshot, string JobsDir)> snapshots) =>
        snapshots.Select(s => CardNumbers.Highest(s.JobsDir, s.Snapshot.Files.Keys)).DefaultIfEmpty(0).Max();
}

/// <summary>
/// 1111-B (b): numbers the <c>new-{L}-{slug}.md</c> cards a commit put on main (by hand or by another session).
/// Runs on the repository's git writer beside the planner step: reads the model of main, and when it holds a new
/// card, commits <c>jobs: number new-{L}-{slug} as {n}</c> moving it to <c>{n}-{L}-{slug}.md</c> with its title and
/// the queue's references to it rewritten. A new card is never a card (CARD-4), so it is never dispatched before.
/// </summary>
public sealed class NewCardStep
{
    private readonly AnswersCommitter _committer;
    private readonly CardNumbering _numbering;
    private readonly string _jobsDir;
    private readonly string _project;
    private readonly ISchedulerLog _log;
    private string? _failedAt;
    private string? _waitLogged;

    /// <param name="committer">The answers worktree the commit goes through; its model of main is read.</param>
    /// <param name="numbering">The pool's counter.</param>
    /// <param name="jobsDir">The project's jobs folder.</param>
    /// <param name="project">The project's name for the log, "" outside a pool.</param>
    /// <param name="log">The scheduler's log.</param>
    public NewCardStep(AnswersCommitter committer, CardNumbering numbering, string jobsDir, string project, ISchedulerLog log)
    {
        _committer = committer;
        _numbering = numbering;
        _jobsDir = jobsDir.Trim('/');
        _project = project;
        _log = log;
        Model = () => committer.MainModel();
    }

    /// <summary>The repository's model of main (the committer's unless a test sets one).</summary>
    public Func<RepositoryModel?> Model { get; set; }

    /// <summary>One look at main; true when a numbering commit was pushed.</summary>
    public bool Run()
    {
        if (Model() is not { Snapshot: { } at } model)
        {
            return false;
        }

        var news = at.Files.Keys.Where(p => CardNumbers.NewFromPath(_jobsDir, p) is not null).Order(StringComparer.Ordinal).ToList();
        if (news.Count == 0 || string.Equals(_failedAt, at.Commit, StringComparison.Ordinal))
        {
            return false;
        }

        if (!_numbering.Ready)
        {
            if (!string.Equals(_waitLogged, at.Commit, StringComparison.Ordinal))
            {
                _log.Write($"card numbers: {news.Count} new card(s) wait until every model of main has loaded");
                _waitLogged = at.Commit;
            }

            return false;
        }

        var cards = news.Select(p => KeyValuePair.Create(p, model.Text(p) ?? "")).ToList();
        var queue = _jobsDir + "/queue/";
        var others = at.Files.Keys
            .Where(p => p.StartsWith(queue, StringComparison.Ordinal) && p.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !news.Contains(p))
            .Order(StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p, model.Text(p) ?? ""))
            .Where(kv => kv.Value.Contains("new-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var (change, numbering) = CardNumbers.OnMain(_jobsDir, cards, others, _numbering.Next);
        var pushed = _committer.Apply(change);
        if (!pushed.Ok)
        {
            _failedAt = at.Commit;
            _log.Write($"card numbers: could not commit {change.Message}: {pushed.Error}");
            return false;
        }

        foreach (var n in numbering.Numbered)
        {
            _log.Write(CardNumbers.LogLine(_project, n));
        }

        foreach (var name in numbering.Unknown)
        {
            _log.Write($"card numbers: {(_project.Length > 0 ? _project + "/" : "")}{name} is named but not on main as a new card: left as it is");
        }

        return true;
    }
}
