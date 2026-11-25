// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Net;
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.MainModel;
using Scheduler.Core.Planner;
using Scheduler.Host.Git;
using Scheduler.Host.Moves;

namespace Scheduler.Host.Planner;

/// <summary>
/// The planner outbox's step (PLAN-2, PLAN-6, PLAN-8; <c>Planner-Step</c> L1483): once a minute
/// while the switch is on, <c>git fetch</c> in the developer's copy (never a pull or a checkout),
/// then each complete batch in name order is applied to a fresh main through the answers worktree
/// (Core works out the change, <see cref="AnswersCommitter"/> commits and pushes it, three tries)
/// and the batch folder is moved to <c>sent/</c> or <c>failed/</c>. 1064-B (state-cache.md MM-9, MM-14): once the
/// repository's model of main has loaded, the fetch is its gate's (shared with the ticks, at most one a poll) and a
/// batch is worked out on one snapshot of the model, read in process, with no reset of the answers worktree just
/// to read main; the commit that follows still goes through it.
/// </summary>
public sealed class PlannerStep
{
    /// <summary>The least time between two steps (PLAN-2).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly GitRunner _git;
    private readonly AnswersCommitter _committer;
    private readonly IClock _clock;
    private readonly ISchedulerLog _log;
    private readonly Func<bool> _pushOn;
    private readonly IDictionary<string, int> _retries;
    private readonly PlannerSettings _settings;
    private readonly PlannerOutboxFolder _outbox;
    private readonly string _jobsDir;
    private readonly string _remote;
    private readonly string _branch;
    private DateTimeOffset? _at;

    /// <param name="settings">The developer's copy and the outbox (PLAN-1).</param>
    /// <param name="git">Runs git.</param>
    /// <param name="committer">The answers worktree every commit goes through.</param>
    /// <param name="clock">Time, for the minute and the folder names.</param>
    /// <param name="log">The scheduler's log.</param>
    /// <param name="pushOn">The page's "Planner push" switch.</param>
    /// <param name="retries">The run counts, cleared for a card the batch sets ready (PLAN-7).</param>
    /// <param name="jobsDir">The jobs folder in the repository.</param>
    /// <param name="remote">The remote, <c>origin</c>.</param>
    /// <param name="branch">The main branch.</param>
    public PlannerStep(PlannerSettings settings, GitRunner git, AnswersCommitter committer, IClock clock, ISchedulerLog log,
        Func<bool> pushOn, IDictionary<string, int> retries, string jobsDir, string remote, string branch)
    {
        _settings = settings;
        _git = git;
        _committer = committer;
        _clock = clock;
        _log = log;
        _pushOn = pushOn;
        _retries = retries;
        _jobsDir = jobsDir;
        _remote = remote;
        _branch = branch;
        _outbox = new PlannerOutboxFolder(settings.Outbox);
        _feed = () => IMainFeed.Shared(committer.Repo, remote, branch);
    }

    private Func<IMainFeed?> _feed;

    /// <summary>The repository's model of main (<see cref="IMainFeed.Shared"/> unless a test sets one); null reads through git as before.</summary>
    public IMainFeed? Feed
    {
        get => _feed();
        set => _feed = () => value;
    }

    /// <summary>1111-B: the pool's counter of card numbers a batch's <c>new-*</c> cards draw from.</summary>
    public CardNumbering Numbering { get; set; } = CardNumbering.Shared;

    /// <summary>1111-B: the project's name in the numbering log lines, "" outside a pool.</summary>
    public string Project { get; set; } = "";

    /// <summary>The page's last result, "" before the first batch.</summary>
    public string Last { get; private set; } = "";

    /// <summary>
    /// 987-B: runs work that touches the tick's state (its retries) on the tick's thread; at once unless
    /// the host hands it over, as it does when the step runs on the git writer.
    /// </summary>
    public Action<Action> OnTick { get; set; } = action => action();

    /// <summary>
    /// One step if the switch is on and a minute has passed since the last. True when the
    /// scheduler should tick at once (a batch was dealt with).
    /// </summary>
    public bool Run()
    {
        if (!_pushOn())
        {
            return false;
        }

        var now = _clock.Now;
        if (_at is { } at && now - at < Interval)
        {
            return false;
        }

        _at = now;
        Fetch();
        var dealt = false;
        foreach (var name in _outbox.Complete())
        {
            dealt |= Deal(name);
        }

        if (dealt)
        {
            Fetch();
        }

        return dealt;
    }

    /// <summary>The Control window's line about the outbox (<c>Planner-Line</c> L1509).</summary>
    public string Line()
    {
        var st = _pushOn() ? "<span class='ok'>on</span>" : "<span class='mute'>off</span>";
        var failed = _outbox.FailedCount();
        var last = Last.Length == 0
            ? ""
            : $" &nbsp; last: <span class='{(Last.StartsWith("failed", StringComparison.Ordinal) ? "bad" : "mute")}'>{WebUtility.HtmlEncode(Last)}</span>";
        return $"<div>Planner push: {st} &nbsp; {_outbox.Complete().Count} waiting{(failed > 0 ? $", <span class='bad'>{failed} failed</span>" : "")}{last}</div>";
    }

    // MM-9: the gate's fetch when the model follows the repository the planner fetches in, else the planner's own.
    private void Fetch()
    {
        if (Feed is { Model: not null } feed && SameRepository(_settings.FetchRepo, _committer.Repo))
        {
            feed.Fetch(FetchAsk.Planner);
            return;
        }

        _git.Run(_settings.FetchRepo, "fetch", "-q", _remote);
    }

    private static bool SameRepository(string a, string b) =>
        a.Length > 0 && b.Length > 0 && string.Equals(MainModels.Key(a), MainModels.Key(b), StringComparison.OrdinalIgnoreCase);

    // Main as the batch is worked out on: one snapshot of the model, after a ref read (MM-8), else the answers worktree reset to main.
    private IPlannerMain PlannerMain()
    {
        if (Feed is { Model: { Snapshot: not null } } feed)
        {
            feed.Tick();
            if (feed.Model is { Snapshot: { } at } model)
            {
                return new AnswersPlannerMain(model, at, _git, _committer.Repo, _jobsDir);
            }
        }

        return new AnswersPlannerMain(_git, _committer.AnswersTree(), _jobsDir);
    }

    /// <summary>Applies one batch and moves its folder out of the outbox; false when it waits in the outbox for card numbers.</summary>
    private bool Deal(string name)
    {
        PlannerPlan plan;
        CommitResult? pushed = null;
        NumberedFiles? numbered = null;
        try
        {
            plan = PlannerApply.Plan(_outbox.Files(name), PlannerMain(), _jobsDir, _branch);
            if (plan.Ok && !plan.NothingToCommit)
            {
                // 1111-B (a): the batch's new-* cards get the pool's next numbers in the same commit.
                if (plan.Change!.Writes.Keys.Any(p => CardNumbers.NewFromPath(_jobsDir, p) is not null))
                {
                    if (!Numbering.Ready)
                    {
                        _log.Write($"planner: batch {name} waits for card numbers until every model of main has loaded");
                        return false;
                    }

                    var (change, files) = CardNumbers.Number(plan.Change, _jobsDir, Numbering.Next);
                    numbered = files;
                    plan = plan with
                    {
                        Change = change,
                        Written = [.. plan.Written.Select(w => files.Where(w.Path) is var to && to != w.Path
                            ? new WrittenFile(to, files.Texts.GetValueOrDefault(to, w.Text)) : w)],
                    };
                }

                pushed = _committer.Apply(plan.Change! with { Message = plan.CommitMessage });
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            plan = new PlannerPlan { Ok = false, Error = "error: " + e.Message };
        }

        if (pushed is { Ok: false })
        {
            plan = new PlannerPlan { Ok = false, Error = pushed.Error, Message = plan.Message };
        }

        var now = _clock.Now;
        var report = PlannerReport.For(name, plan, now);
        if (_outbox.MoveOut(name, report.Failed, report.Reason, now, out var moveError) is null)
        {
            _log.Write($"planner: could not move batch {name} out of the outbox: {moveError}");
        }

        Last = report.Last;
        foreach (var line in report.LogLines)
        {
            _log.Write(line);
        }

        if (pushed is { Ok: true })
        {
            var released = plan;
            OnTick(() => PlannerRelease.Clear(released, _retries, _log));   // 987-B: the retries are the tick's
            foreach (var n in numbered?.Numbered ?? [])
            {
                _log.Write(CardNumbers.LogLine(Project, n));
            }

            foreach (var unknown in numbered?.Unknown ?? [])
            {
                _log.Write($"card numbers: {(Project.Length > 0 ? Project + "/" : "")}{unknown} is named but not in batch {name}: left as it is");
            }
        }

        return true;
    }
}
