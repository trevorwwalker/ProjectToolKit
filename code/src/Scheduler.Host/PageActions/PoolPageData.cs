// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;
using Scheduler.Core;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;

namespace Scheduler.Host.PageActions;

/// <summary>
/// The pool's page data (MP-P8, design section 6): the pool snapshot - Core's pool part with each
/// project's own page snapshot - built on the scheduler thread after a tick (PAGE-1) and served as is;
/// the pool-wide texts; every project's running workers keyed by project and slot (WT-W3) for the live
/// feed; and each project's own <see cref="IPageData"/> for the per-project routes.
/// </summary>
/// <param name="clock">The clock.</param>
/// <param name="logDir">The pool's log folder: the only place <c>/log</c> may read in a pool-wide request.</param>
/// <param name="projects">The projects in the pool in the pool file's order, each with its own page data.</param>
/// <param name="pool">Core's pool part (<c>PoolTick.Snapshot</c>), without the projects' pages.</param>
public sealed class PoolPageData(IClock clock, string logDir, Func<IReadOnlyList<(string Name, IPageData Data)>> projects, Func<PoolPageSnapshot> pool, Func<string, DateTimeOffset, PageLockCounts?>? lockCounts = null) : IPoolPageData
{
    private volatile PoolPageSnapshot _snapshot = Build(clock, projects, pool, lockCounts);

    /// <summary>Rebuild the snapshot; called on the scheduler thread after a tick, once each project's own data has been refreshed.</summary>
    public void Refresh() => _snapshot = Build(clock, projects, pool, lockCounts);

    /// <inheritdoc />
    public PoolPageSnapshot PoolSnapshot() => _snapshot;

    /// <inheritdoc />
    public IPageData? For(string project) =>
        projects().Where(p => Text.Same(p.Name, project)).Select(p => p.Data).FirstOrDefault();

    /// <summary>
    /// The one-project snapshot a pool-wide caller (the live feed's "has the page caught up") asks for: the
    /// newest project's, or an empty one while no project is in the pool.
    /// </summary>
    public PageSnapshot Snapshot() =>
        _snapshot.Pages.OrderByDescending(p => p.Built).FirstOrDefault()
        ?? new PageSnapshot { Built = _snapshot.Built, State = new PageStateView { State = SchedulerState.Running, Text = "pool" } };

    /// <inheritdoc />
    public string? FontPath => null;

    /// <inheritdoc />
    public string LogDirectory => logDir;

    /// <inheritdoc />
    public string? Card(string name) => null;

    /// <inheritdoc />
    public string? Request(string name) => null;

    /// <inheritdoc />
    public string? HealthText() => null;

    /// <summary>Each project's running.json under its name.</summary>
    public string StatusJson()
    {
        var text = new StringBuilder("{");
        var first = true;
        foreach (var (name, data) in projects())
        {
            _ = text.Append(first ? "" : ",").Append(System.Text.Json.JsonSerializer.Serialize(name)).Append(':').Append(data.StatusJson());
            first = false;
        }

        return text.Append('}').ToString();
    }

    /// <summary>The pool's own estimates report (EST-31); null until it is built or when the pool has none.</summary>
    public State.EstimatesPool? Estimates { get; init; }

    /// <inheritdoc />
    public string? EstimatesJson() => Estimates?.Json;

    /// <summary>Every project's running workers, each marked with its project (WT-W3).</summary>
    public IReadOnlyList<LiveWorker> RunningWorkers() =>
        [.. projects().SelectMany(p => p.Data.RunningWorkers().Select(w => w with { Project = p.Name }))];

    private static PoolPageSnapshot Build(IClock clock, Func<IReadOnlyList<(string Name, IPageData Data)>> projects, Func<PoolPageSnapshot> pool, Func<string, DateTimeOffset, PageLockCounts?>? lockCounts)
    {
        var part = pool();
        var pages = projects().ToDictionary(p => p.Name, p => p.Data.Snapshot(), Text.Comparer);
        return part with
        {
            Built = clock.Now,
            // DL-26: each project's lock events of today, null where its locks are off.
            Projects = lockCounts is null ? part.Projects : [.. part.Projects.Select(v => v with { LockCounts = lockCounts(v.Name, clock.Now) })],
            Pages = [.. part.Projects.Where(v => pages.ContainsKey(v.Name)).Select(v => pages[v.Name])],
        };
    }
}
