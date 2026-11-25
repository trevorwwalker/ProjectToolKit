// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Core.WebContract;
using Scheduler.Host.Git;
using Scheduler.Host.PageActions;
using Scheduler.Web.Server;

namespace Scheduler.Host.Tick;

/// <summary>
/// What the page server reads (<see cref="IPageData"/>): the snapshot is rebuilt on the scheduler
/// thread after each tick (<see cref="Refresh"/>) and handed to the server thread as a whole, so
/// the server never reads the tick while it runs. 1064-B: a card's, a request's and the review panel's text come
/// from the repository's model of main once it has loaded (state-cache.md MM-4, MM-14), so opening one starts no
/// git process (MM-15); the review panel's commits keep their one <c>git log</c> (the developer, 2026-11-22).
/// </summary>
public sealed class HostPageData(SchedulerConfig config, IClock clock, SchedulerTick tick, IMainRepository main, IStateFiles files, Func<string?> healthText, PageSnapshotSources? sources = null, ReviewCommits? commits = null) : IPageData
{
    private volatile PageSnapshot _snapshot = HostPageSnapshot.Build(config, clock, tick, sources);
    private volatile LiveRunning _running = new([], new Dictionary<int, (string, string)>());
    private readonly ReviewCommits _commits = commits ?? new ReviewCommits(new GitRunner(), config.Repo, config.Remote + "/" + config.Branch);
    private Func<IMainFeed?> _feed = () => IMainFeed.Shared(config.Repo, config.Remote, config.Branch);

    /// <summary>The repository's model of main (<see cref="IMainFeed.Shared"/> unless a test sets one); null reads through the CLI.</summary>
    public IMainFeed? Feed
    {
        get => _feed();
        set => _feed = () => value;
    }

    /// <summary>Rebuild the snapshot and the running list; called on the scheduler thread after a tick.</summary>
    public void Refresh()
    {
        _snapshot = HostPageSnapshot.Build(config, clock, tick, sources);
        _running = new LiveRunning(
            HostPageSnapshot.Live(config, clock, tick, sources?.Stats),
            tick.Running.Values.ToDictionary(w => w.Slot, w => (w.Model, w.Endpoint)));
    }

    /// <inheritdoc />
    public PageSnapshot Snapshot() => _snapshot;

    /// <inheritdoc />
    public string? FontPath => null;

    /// <inheritdoc />
    public string LogDirectory => config.LogDir;

    /// <inheritdoc />
    public string? Card(string name)
    {
        var card = FindCard(name);
        return card is null ? null : MainRead.Text(Feed, main, card.Path);
    }

    /// <inheritdoc />
    public string? Request(string name)
    {
        var request = tick.Requests.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        return request is null ? null : MainRead.Text(Feed, main, request.Path);
    }

    /// <inheritdoc />
    public string? HealthText() => healthText();

    /// <inheritdoc />
    public string StatusJson() => files.Read(StateFile.Running) ?? "{}";

    /// <inheritdoc />
    /// <remarks>
    /// 964-B: the live feed asks for this on each build, only while a page is polling (PAGE-3). First
    /// the new raw log text is rendered (<see cref="PageSnapshotSources.LiveRender"/>), then each
    /// worker's stats line is made again from the stats that render moved, so text and stats reach the
    /// page within one poll instead of once a tick.
    /// </remarks>
    public IReadOnlyList<LiveWorker> RunningWorkers()
    {
        var running = _running;
        if (sources?.LiveRender is not { } render)
        {
            return running.Workers;
        }

        render();
        return
        [
            .. running.Workers.Select(w => running.Who.TryGetValue(w.Slot, out var who)
                ? w with { Stats = WorkerStatsText.Line(sources.Stats?.Invoke(w.Slot), who.Model, who.Endpoint) }
                : w),
        ];
    }

    /// <inheritdoc />
    public ReviewPanel? Review(string card)
    {
        var found = FindCard(card);
        if (found is null)
        {
            return null;
        }

        var result = ReviewText.ResultSection(MainRead.Text(Feed, main, found.Path) ?? "");
        return new ReviewPanel
        {
            Name = found.Name,
            Title = found.Title,
            Status = found.Status,
            Questions = found.Questions,
            Commits = _commits.For(found.Id),
            Result = result,
            Links = ReviewText.Links(result, p => RepoFile(p) is not null),
        };
    }

    /// <inheritdoc />
    public string? PlanText(string path) =>
        path.Contains("..", StringComparison.Ordinal) ? null : MainRead.Text(Feed, main, path.Replace('\\', '/').TrimStart('/'));

    // 1113-C: a card by file name or short id, open or in done/ and archive/ (the model has their headers).
    private PageCardLookup.Found? FindCard(string name) =>
        PageCardLookup.Find(tick.Cards, MainRead.Model(Feed)?.DoneCards(config.JobsDir) ?? [], name);

    /// <inheritdoc />
    public string? RepoFile(string path)
    {
        var root = Path.GetFullPath(config.Repo).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, path));
        }
        catch (ArgumentException)
        {
            return null;
        }

        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(full) ? full : null;
    }

    // The running list as the last tick left it, and each slot's model and endpoint, which its
    // stats line names; handed over whole so the server thread never sees half of one.
    private sealed record LiveRunning(IReadOnlyList<LiveWorker> Workers, IReadOnlyDictionary<int, (string Model, string Endpoint)> Who);
}
