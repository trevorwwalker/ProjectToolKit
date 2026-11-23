using System.Text.Json.Nodes;
using Scheduler.Core.Contracts;
using Scheduler.Core.Config;
using Scheduler.Core.WebContract;
using Scheduler.Host.Pool;

namespace Scheduler.Host.PageActions;

/// <summary>What <see cref="PoolPageActions"/> needs from the pool: its projects' actions, its wake, its types and its quota.</summary>
public sealed class PoolPageActionParts
{
    /// <summary>The projects in the pool in the pool file's order, each with its own actions (MP-P8).</summary>
    public required Func<IReadOnlyList<(string Name, IPageActions Actions)>> Projects { get; init; }

    /// <summary>Wakes the pool's loop.</summary>
    public required Action Wake { get; init; }

    /// <summary>Clears an agent out of quota in the pool's shared quota; false when it was not out (WT-9).</summary>
    public required Func<string, bool> ClearAgent { get; init; }

    /// <summary>The pool's worker types are written to the pool file (WT-C8, no commit): the <c>pool</c> section as JSON; false when it could not be.</summary>
    public required Func<string, bool> WritePool { get; init; }

    /// <summary>1033-C: a project's workspaces are written to the pool file (project, count); false when they could not be. Null: not offered.</summary>
    public Func<string, int, bool>? WriteWorkspaces { get; init; }
}

/// <summary>
/// The pool's actions (MP-P8): the project-bound ones are each project's own <see cref="HostPageActions"/>
/// (the router sends <c>project=</c> to <see cref="For"/>); Pause, Drain and Resume act on one project or,
/// with none, on every project (MP-H8); Stop all asks every project's workers to stop; the Worker types
/// panel saves to the pool file (MP-P7, WT-C8). It is also the router's one-project table for the
/// pool-wide routes <c>/tick</c>, <c>/worker-types</c> and <c>/agent-resume</c>; every other one-project
/// route answers "which project?".
/// </summary>
public sealed class PoolPageActions(PoolPageActionParts parts) : IPoolPageActions, IPageActions
{
    private const string WhichProject = "which project? This route needs project= in a pool.";

    private static readonly string[] HttpPrefixes = ["http://", "https://"];

    private int _seq;

    /// <inheritdoc />
    public IReadOnlyList<string> Projects => [.. parts.Projects().Select(p => p.Name)];

    /// <inheritdoc />
    public IPageActions? For(string project) =>
        parts.Projects().Where(p => Text.Same(p.Name, project)).Select(p => p.Actions).FirstOrDefault();

    /// <inheritdoc />
    public ActionResult Pause(string? project) => Each(project, a => a.Pause());

    /// <inheritdoc />
    public ActionResult Drain(string? project) => Each(project, a => a.Drain());

    /// <inheritdoc />
    public ActionResult Resume(string? project) => Each(project, a => a.Resume());

    /// <inheritdoc cref="IPoolPageActions.StopAll" />
    public ActionResult StopAll()
    {
        foreach (var (_, actions) in parts.Projects())
        {
            _ = actions.StopAll();
        }

        return Accepted();
    }

    /// <inheritdoc cref="IPoolPageActions.Stop" />
    public ActionResult Stop(string project) =>
        string.IsNullOrWhiteSpace(project) ? Failed() : Each(project, a => a.StopWorkers());

    /// <inheritdoc />
    public ActionResult SetWorkspaces(string project, string value)
    {
        var entry = parts.Projects().Select(p => p.Name).FirstOrDefault(n => Text.Same(n, project));
        if (entry is null)
        {
            return ActionResult.Failed($"workspaces not saved: there is no project named {project}");
        }

        if (!int.TryParse(value?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            || n < PoolProjectEntry.MinWorkspaces || n > PoolProjectEntry.MaxWorkspaces)
        {
            return ActionResult.Failed($"{entry}: workspaces not saved: {value} is not a whole number "
                + $"{PoolProjectEntry.MinWorkspaces} to {PoolProjectEntry.MaxWorkspaces}");
        }

        if (parts.WriteWorkspaces?.Invoke(entry, n) != true)
        {
            return ActionResult.Failed($"{entry}: workspaces not saved: the pool file could not be written");
        }

        parts.Wake();
        return Accepted();
    }

    /// <inheritdoc />
    public ActionResult Tick()
    {
        parts.Wake();
        return Accepted();
    }

    /// <inheritdoc />
    public ActionResult ResumeAgent(string agent)
    {
        if (!parts.ClearAgent(agent))
        {
            return ActionResult.Failed($"agent {agent} is not out of quota");
        }

        parts.Wake();
        return Accepted();
    }

    /// <summary>MP-P7, WT-C8: the Worker types panel's Save writes the pool file's <c>pool</c> section; the next tick re-reads it (MP-C7).</summary>
    public ActionResult SaveWorkerTypes(int maxWorkers, IReadOnlyList<PageWorkerTypeView> types)
    {
        if (maxWorkers < 1 || types.Count == 0)
        {
            return ActionResult.Failed("worker types not saved: there must be at least one worker and one type");
        }

        var bad = types.SelectMany(t => new[] { t.Endpoint, t.MetricsUrl })
            .Where(url => url.Length > 0 && !HttpPrefixes.Any(prefix => url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (bad.Count > 0)
        {
            return ActionResult.Failed($"worker types not saved: a server or metrics address must start with http:// or https:// ({string.Join(", ", bad)})");
        }

        var names = types.Select(t => t.Name.Trim()).ToList();
        if (names.Any(n => n.Length == 0 || Text.Same(n, "premium") || n.All(char.IsDigit)) || names.Distinct(Text.Comparer).Count() != names.Count)
        {
            return ActionResult.Failed("worker types not saved: a type name is empty, repeated, Premium or only digits");
        }

        var pool = new WorkerPool
        {
            MaxWorkers = maxWorkers,
            Types =
            [
                .. types.Select(t => new WorkerType
                {
                    Name = t.Name.Trim(),
                    Max = Math.Max(0, t.Max),
                    Agent = t.Agent,
                    Model = t.Model,
                    Endpoint = t.Endpoint,
                    MetricsUrl = t.MetricsUrl,
                    Premium = t.Premium,
                    Projects = t.Projects,
                }),
            ],
        };
        var section = (JsonNode.Parse(WorkerTypesJson.BuildPool(pool)) as JsonObject)?["pool"];
        if (section is null || !parts.WritePool(section.ToJsonString()))
        {
            return ActionResult.Failed("worker types not saved: the pool file could not be written");
        }

        parts.Wake();
        return Accepted();
    }

    // The one-project table: these are one project's, so in a pool they need project= (MP-P8).
    ActionResult IPageActions.Pause() => Pause(null);

    ActionResult IPageActions.Resume() => Resume(null);

    ActionResult IPageActions.Drain() => Drain(null);

    /// <inheritdoc />
    public ActionResult HealthNow() => Failed();

    /// <inheritdoc />
    public ActionResult PlannerToggle() => Failed();

    /// <inheritdoc />
    public ActionResult Send(int slot, string message) => Failed();

    /// <inheritdoc />
    public ActionResult Answer(string card, int question, string choice, string note) => Failed();

    /// <inheritdoc />
    public ActionResult OpenCategories() => Failed();

    /// <inheritdoc />
    public ActionResult OpenWorkers() => Failed();

    /// <inheritdoc />
    public ActionResult AcceptCategory(string category) => Failed();

    /// <inheritdoc />
    public ActionResult Open(string card) => Failed();

    /// <inheritdoc />
    public ActionResult ResumeCard(string card) => Failed();

    /// <inheritdoc />
    public ActionResult Done(string card) => Failed();

    /// <inheritdoc />
    public ActionResult Retry(string card) => Failed();

    /// <inheritdoc />
    public ActionResult Stop(int slot) => Failed();

    /// <inheritdoc />
    public ActionResult Kill(int slot) => Failed();

    /// <inheritdoc />
    public ActionResult TemplateOverwrite(string project, string file) =>
        For(project)?.TemplateOverwrite(project, file) ?? Failed();

    /// <inheritdoc />
    public ActionResult TemplateKeep(string project, string file) =>
        For(project)?.TemplateKeep(project, file) ?? Failed();

    // MP-H8: one project, or all when none is named; a name that is no project is refused.
    private ActionResult Each(string? project, Func<IPageActions, ActionResult> act)
    {
        var all = parts.Projects();
        if (project is null)
        {
            foreach (var (_, actions) in all)
            {
                _ = act(actions);
            }

            return Accepted();
        }

        var one = all.Where(p => Text.Same(p.Name, project)).Select(p => p.Actions).FirstOrDefault();
        return one is null ? ActionResult.Failed(WhichProject) : act(one);
    }

    private static ActionResult Failed() => ActionResult.Failed(WhichProject);

    private ActionResult Accepted() => ActionResult.Queued(Interlocked.Increment(ref _seq));
}
