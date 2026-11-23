using Scheduler.Core.WebContract;

namespace Scheduler.Web.Server;

/// <summary>The pool's half of the route table (MP-P8).</summary>
public sealed partial class PageRouter
{
    /// <summary>What a per-project route without <c>project=</c> answers in a pool (MP-P8).</summary>
    public const string WhichProject = "which project? This route needs project= in a pool.";

    // MP-P8: every route that names a card or a slot, which in a pool goes to one project. /categories
    // and /open-workers write one project's files, so they are its too; /accept-lane is dead (AREA-2).
    private static readonly HashSet<string> PerProject = new(StringComparer.Ordinal)
    {
        "/answer", "/card", "/done", "/health", "/health-now", "/kill", "/log", "/open", "/open-lanes",
        "/open-workers", "/plan", "/planner-toggle", "/repo-file", "/request", "/resume-card", "/retry", "/review",
        "/send", "/stop", "/template-keep", "/template-overwrite", "/categories",
    };

    // A pool's own answer to a request, or null when the one-project table answers it (the pool-wide
    // GETs: /favicon.ico, /font, /live.json, /status.json; and /tick, /worker-types, /agent-resume,
    // which the pool-wide actions take).
    private PageResponse? PoolRoute(PoolRouting pool, PageRequest request, bool isGet)
    {
        if (!isGet && !string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = ProjectOf(request);
        if (PerProject.Contains(request.Path))
        {
            if (name is null)
            {
                return PageResponse.Text(400, WhichProject);
            }

            var data = pool.Data.For(name);
            var actions = pool.Actions.For(name);
            if (data is null || actions is null)
            {
                return PageResponse.Text(400, NoSuchProject(name));
            }

            // That project's own router over the same live feed and action queue: the click still goes
            // in click order behind every other project's (957-B).
            var own = new PageRouter(data, actions, Renderer, Live, RemoteControl, Queue, landed: false);
            return isGet ? own.Get(request) : own.Post(request);
        }

        if (name is not null && !pool.Actions.Projects.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return PageResponse.Text(400, NoSuchProject(name));
        }

        return (isGet, request.Path) switch
        {
            (true, "/") => PageResponse.Html(pool.Renderer.Render(
                pool.Data.PoolSnapshot(),
                remote: !request.IsLocal,
                controlsAllowed: request.IsLocal || RemoteControl,
                view: request.Query("view"))),
            // MP-H8: the project the page's dropdown picked, or every project for All.
            (false, "/pause") => Now(request, "pause", name, () => pool.Actions.Pause(name)),
            (false, "/drain") => Now(request, "drain", name, () => pool.Actions.Drain(name)),
            (false, "/resume") => Now(request, "resume", name, () => pool.Actions.Resume(name)),
            (false, "/stopall") => Queued(request, "stopall", null, pool.Actions.StopAll),
            // 1004-C: a row's Stop is that project's alone, so it needs one; the scheduler does not exit.
            (false, "/stop-project") => name is null
                ? PageResponse.Text(400, WhichProject)
                : Queued(request, "stop-project", null, () => pool.Actions.Stop(name)),
            // 1033-C: a project's workspaces box; the value is judged by the actions (a refusal reaches the notice line).
            (false, "/workspaces") => name is null
                ? PageResponse.Text(400, WhichProject)
                : Queued(request, "workspaces", null, () => pool.Actions.SetWorkspaces(name, request.Form("workspaces") ?? "")),
            (false, "/accept-lane") => PageResponse.NotFound("not found"),
            _ => null,
        };
    }

    // The project a request names, from its query or its form; "all" and blank are no project.
    private static string? ProjectOf(PageRequest request)
    {
        var name = request.Query("project") ?? request.Form("project");
        return string.IsNullOrWhiteSpace(name) || string.Equals(name, "all", StringComparison.OrdinalIgnoreCase)
            ? null
            : name.Trim();
    }

    private static string NoSuchProject(string name) => "which project? There is no project named " + name + " in the pool.";
}
