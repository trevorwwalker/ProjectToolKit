using System.Globalization;
using System.Text;
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;
using Scheduler.Web.Server;

namespace Scheduler.Web.Views;

/// <summary>
/// The one page for every project in the pool (MP-8): one look, a project filter on each view,
/// one Waiting on you across projects, and stats per project and per model. It is drawn from the
/// projects' own <see cref="PageSnapshot"/>s alone, so the page thread still never asks the
/// scheduler anything (PAGE-1), and each row carries the project it came from so a filter can
/// hide the others. The theme is the pool config's own (spec B3 Q12 as answered on 330-B Q3);
/// listen and port belong to the server and the host, not to this file.
/// </summary>
public sealed partial class CombinedPageRenderer(PageThemes? themes = null, string theme = "") : IPoolPageRenderer
{
    private const string RemoteReason = "(Only on the PC running the scheduler.)";

    // 1015-B: the row buttons of a project that has not joined yet, in a joined row's order.
    private static readonly string[] StartingButtons = ["Check main", "Categories", "Planner push", "Pause", "Drain", "Stop"];

    private readonly PageThemes _themes = themes ?? PageThemes.BuiltIn;
    private readonly Func<PageThemes>? _themesOf;
    private readonly Func<IReadOnlyList<string>> _starting = () => [];

    /// <summary>
    /// 1015-B: the pool's page, served before its projects have joined. <paramref name="themes"/> gives the
    /// themes at each render (a project's own theme is added once it has joined); <paramref name="starting"/>
    /// names the projects still joining, in the pool file's order, shown in the Projects table as starting.
    /// </summary>
    public CombinedPageRenderer(Func<PageThemes> themes, string theme, Func<IReadOnlyList<string>> starting)
        : this(null, theme)
    {
        _themesOf = themes;
        _starting = starting;
    }

    private PageThemes Themes => _themesOf?.Invoke() ?? _themes;

    // The pool's own theme name (Q12), which the pool page is drawn with.
    private string PoolTheme { get; } = theme;

    /// <summary>The pool page from a pool snapshot: its projects' pages plus the pool part (section 4).</summary>
    public string Render(PoolPageSnapshot pool, bool remote, bool controlsAllowed) => Render(pool, remote, controlsAllowed, null);

    /// <summary>VW-2: the pool page in <paramref name="view"/> (a name of <see cref="ViewList"/>; no or an unknown view is the first of them).</summary>
    public string Render(PoolPageSnapshot pool, bool remote, bool controlsAllowed, string? view)
    {
        ArgumentNullException.ThrowIfNull(pool);
        return Render(pool.Pages, PoolTheme, remote, controlsAllowed, pool, SelectedView(view), view);
    }

    /// <summary>
    /// The combined page for a set of projects, with no pool part (no weights or shares).
    /// </summary>
    /// <param name="snapshots">One snapshot per project, in the pool config's order.</param>
    /// <param name="theme">The pool's own theme name (Q12); empty when the config gives none.</param>
    /// <param name="remote">Whether the reader is on another PC.</param>
    /// <param name="controlsAllowed">Whether this reader's buttons work; when not, they are disabled with the reason.</param>
    public string Render(IReadOnlyList<PageSnapshot> snapshots, string theme, bool remote, bool controlsAllowed) =>
        Render(snapshots, theme, remote, controlsAllowed, null, DefaultView);

    private string Render(IReadOnlyList<PageSnapshot> snapshots, string theme, bool remote, bool controlsAllowed, PoolPageSnapshot? pool, string view, string? address = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var plans = view == PlansViewName;   // 1129-C: the drawing below is still by view; the name drives the dropdown.
        var gantt = view == GanttViewName;

        var projects = snapshots.Where(snapshot => snapshot is not null).ToList();
        var names = projects.Select(snapshot => snapshot.Project).ToList();
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'>")
          .Append("<meta name='viewport' content='width=device-width, initial-scale=1'>")
          .Append(Scheduler.Web.Server.PageFavicon.Link)
          .Append("<title>Switchyard</title>")
          .Append("<meta name='page-version' content='").Append(PageVersion.Of(projects)).Append("'>")
          .Append("<meta name='build-version' content='").Append(System.Net.WebUtility.HtmlEncode(projects.Select(p => p.Version).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "")).Append("'>")
          .Append(plans || gantt ? "<meta name='views-version' content='" + ProjectViews.VersionOf(pool?.Views ?? []) + "'>" : "")
          .Append(Themes.Head())
          .Append(FilterScript)
          .Append("</head><body")
          .Append(" data-pool-theme='").Append(Enc(theme)).Append("'>");

        // 520-A: no top bar here either; what it carried is in the Projects window's title bar.
        // 500-C: the same columns as the single page (PageRenderer), laid out by the common style.
        // 1098-C: the range rules ride inside the desk, which the live feed replaces whole.
        // 1162-C: the Plans view takes the full-width desk the Gantt view had (the class was renamed from
        // gantt-desk, which is why it is shared rather than copied).
        sb.Append(gantt || plans ? "<div class='desk wide-desk' id='desk'>" : "<div class='desk' id='desk'>").Append(RangeStyle()).Append(ProjectChipStyle()).Append("<div class='col' id='col-main'>");
        // 1004-C (the developer, 2026-11-20): one window holds the Projects table, the statistics and
        // the Worker types; the running workers' windows follow Waiting on you, as on the single page.
        AppendProjects(sb, projects, theme, remote, controlsAllowed, pool, view);
        if (plans)
        {
            // 1114-C: the Plans view shows only its own window under the title bar (no Waiting on you, no Projects
            // table), and no HTML of the other view.
            // 1127-C: unfinished plans on the left, completed ones on the right (below it on a narrow screen).
            sb.Append("<div class='plans-pair'>");
            Window(sb, "Plans", PlansView.Html(pool?.Views ?? [], projects, names), "", "id='w-plans' data-view='plans'");
            Window(sb, "Completed plans", PlansView.Html(pool?.Views ?? [], projects, names, completed: true), "", "id='w-plans-completed' data-view='plans-completed'");
            sb.Append("</div></div></div>").Append(PageScript.Html).Append("</body></html>");
            return sb.ToString();
        }

        if (gantt)
        {
            // 1132-C: two windows side by side under the title bar, the timeline wide and the Details narrow
            // (wrapping below it on a narrow screen); one range and one grouping, from the address.
            var views = pool?.Views ?? [];
            var range = GanttView.RangeKey(Option(address, "range"));
            var groupBy = GanttView.GroupBy(Option(address, "group"));
            var now = pool?.Built ?? (projects.Count == 0 ? DateTimeOffset.Now : projects.Max(p => p.Built));
            var groupName = groupBy == GanttGroupBy.Plan ? GanttView.PlanGroup : GanttView.CategoryGroup;
            sb.Append("<div class='gantt-pair'>");
            Window(sb, "Gantt", GanttView.Html(views, projects, names, range, groupBy, now), GanttView.Bar(range, groupBy),
                "id='w-gantt' data-view='gantt' data-range='" + range + "' data-group='" + groupName + "'");
            Window(sb, "Details", GanttView.DetailsHtml(views, projects, names, range, groupBy, now), "", "id='w-gantt-details' data-view='gantt-details'");
            sb.Append("</div></div></div>").Append(PageScript.Html).Append("</body></html>");
            return sb.ToString();
        }

        AppendWaiting(sb, projects, names, controlsAllowed);
        AppendWorkerWindows(sb, projects, names, controlsAllowed);
        sb.Append("</div><div class='col' id='col-side'><div class='col' id='col-queue'>");
        AppendQueue(sb, projects, names, pool);
        sb.Append("</div><div class='col' id='col-events'>");
        AppendEvents(sb, projects, names, controlsAllowed);
        AppendTrends(sb, projects);
        sb.Append("</div></div></div>").Append(PageScript.Html).Append("</body></html>");
        return sb.ToString();
    }

    // 1004-C: the window's title after the name and version: the PC's load, 470-C's not-responding
    // state, the pool's theme and read only. The developer (2026-11-20 18:51) took out the pool's
    // state, the health summary and "N of M workers": each project's row shows its own.
    private static string PoolTitle(IReadOnlyList<PageSnapshot> projects, string theme, bool remote, bool controlsAllowed)
    {
        var sb = new StringBuilder();
        var load = projects.OrderByDescending(snapshot => snapshot.Built).Select(snapshot => snapshot.HostLoad).FirstOrDefault(l => l is not null);
        if (load is not null)
        {
            // 1103-C: the load text is the server's, in the element the page script then moves with the
            // stream's frames; the id is where it writes the next value.
            sb.Append(" &nbsp;-- &nbsp;<span id='hostload'>").Append(Enc(PageRenderer.HostLoadText(load))).Append("</span>");
        }
        else
        {
            sb.Append(" <span id='hostload'></span>");
        }

        sb.Append(" <span id='live-state'></span>");
        if (!string.IsNullOrWhiteSpace(theme))
        {
            sb.Append(" &nbsp; theme ").Append(Enc(theme));
        }

        if (remote && !controlsAllowed)
        {
            sb.Append(" &nbsp; <span class='mute'>read only</span>");
        }

        return sb.ToString();
    }

    // 1004-C: the title bar's buttons act on every project and every worker - Pause all or Resume
    // all (one button, by state), Drain and exit, Stop all and exit - then the project dropdown
    // (MP-P2), the views dropdown (1129-C, VW-2), the theme menu and the newest tick.
    private string PoolBar(IReadOnlyList<PageSnapshot> projects, bool controlsAllowed, string view)
    {
        var newest = projects.Count == 0 ? DateTimeOffset.MinValue : projects.Max(snapshot => snapshot.Built);
        var anyRunning = projects.Any(snapshot => snapshot.State.State == SchedulerState.Running);
        var sb = new StringBuilder()
            .Append(anyRunning || projects.Count == 0
                ? Post("/pause", [], "Pause all", "Start no new workers in any project; running ones finish.", controlsAllowed, "warn")
                : Post("/resume", [], "Resume all", "No project is taking cards: click to start workers in all of them again.", controlsAllowed, "pressed ok"))
            .Append(Post("/drain", [], "Drain and exit", "Every project starts nothing new; running cards finish, then the scheduler exits.", controlsAllowed, "bad"))
            .Append(Post("/stopall", [], "Stop all and exit", "Ask every project's workers to stop cleanly, then end the scheduler.", controlsAllowed, "bad"));
        return sb.Append(ProjectSelector(projects)).Append(ViewSwitch(view)).Append(Themes.Selector())
            .Append("<span class='updated'>last update ").Append(newest.ToString("HH:mm:ss")).Append("</span>")
            .ToString();
    }

    // 1129-C (the developer, 2026-11-22 22:52): the page's views, named in this one list. The title bar's
    // dropdown, the address's ?view= and the view a bare "/" remembers are all made from it, so the Gantt
    // of a later card is one more entry here and nothing else has to know about it.
    private const string WorkersViewName = "workers";
    private const string PlansViewName = "plans";
    private const string GanttViewName = "gantt";

    private static readonly (string Name, string Label, string Title)[] ViewList =
    [
        (WorkersViewName, "Workers", "Workers: the running workers and the queue"),
        (PlansViewName, "Plans", "Plans: each plan, its steps and cards"),
        (GanttViewName, "Gantt", "Gantt: the past and running work on a timeline, with Details"),
    ];

    // The view of a bare address with nothing remembered: the first of the list.
    private static string DefaultView => ViewList[0].Name;

    // The address's view, matched against the list; no or an unknown view is the default (VW-2).
    private static string SelectedView(string? view)
    {
        // 1132-C: the router may hand the view with its own options after a '?' (gantt?range=3h&group=plan).
        var want = view?.Split('?', 2)[0].Trim();
        foreach (var entry in ViewList)
        {
            if (string.Equals(entry.Name, want, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Name;
            }
        }

        return DefaultView;
    }

    // 1132-C: one option of the view the router handed over after its name ("gantt?range=3h&group=plan"), or null.
    private static string? Option(string? view, string name)
    {
        var at = view?.IndexOf('?') ?? -1;
        if (at < 0)
        {
            return null;
        }

        foreach (var part in view![(at + 1)..].Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && string.Equals(part[..eq], name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(part[(eq + 1)..]);
            }
        }

        return null;
    }

    // 1129-C (VW-2): one dropdown where the Workers and Plans buttons were, after the project dropdown
    // and before the theme menu; the current view is its selected option. The page script goes to
    // /?view=<name> when the choice changes, as the buttons did.
    private static string ViewSwitch(string view)
    {
        var current = SelectedView(view);
        var sb = new StringBuilder("<select id='viewsel' title='Which view of the pool this page shows' aria-label='View' style='font: inherit'>");
        foreach (var entry in ViewList)
        {
            sb.Append("<option value='").Append(Enc(entry.Name)).Append("'");
            if (entry.Name == current)
            {
                sb.Append(" selected");
            }

            sb.Append(" title='").Append(Enc(entry.Title)).Append("'>").Append(Enc(entry.Label)).Append("</option>");
        }

        return sb.Append("</select>").ToString();
    }

    // MP-P2 (developer, 2026-11-20): one dropdown for the whole page, shown only while two or more
    // projects are active - with one the page looks as today. All projects, then each project by name;
    // the script keeps the choice across refreshes and in the browser, and filters every window by it.
    private static string ProjectSelector(IReadOnlyList<PageSnapshot> projects)
    {
        if (projects.Count < 2)
        {
            return "";
        }

        var sb = new StringBuilder("<select id='projsel' title='Show one project in every window, or all of them' aria-label='Project' style='font: inherit'>")
            .Append("<option value='all'>All projects</option>");
        foreach (var snapshot in projects)
        {
            sb.Append("<option value='").Append(Enc(snapshot.Project)).Append("'>").Append(Enc(snapshot.Project)).Append("</option>");
        }

        return sb.Append("</select>").ToString();
    }

    // 1004-C (the developer, 2026-11-20): one window. The Projects table first - the project, its
    // health, and its own buttons in their own columns so they line up from row to row: Check main,
    // Categories, Planner push, Pause or Resume, Drain, Stop (that project's workers; the scheduler
    // stays). A project's notice follows its row. Under the table the statistics, which follow the
    // dropdown, and the pool's Worker types panel, which does not. The title bar carries what the
    // page's top bar used to (520-A) and the buttons for every project.
    private void AppendProjects(StringBuilder sb, IReadOnlyList<PageSnapshot> projects, string theme, bool remote, bool controlsAllowed, PoolPageSnapshot? pool, string viewName = WorkersViewName)
    {
        var plans = viewName != WorkersViewName;
        var starting = _starting().Where(n => !projects.Any(p => string.Equals(p.Project, n, StringComparison.OrdinalIgnoreCase))).ToList();
        var names = projects.Select(snapshot => snapshot.Project).Concat(starting).ToList();
        var notices = new StringBuilder();
        var table = new StringBuilder("<table class='projects'>");
        // 1029-B: a pool that knows each project's workspaces shows how many are busy, in a column after Health.
        var workspaces = pool?.Projects.Any(p => p.Workspaces > 0) == true;
        table.Append("<tr><th>Project</th><th>Health</th>").Append(workspaces ? "<th>Workspaces</th>" : "").Append("<th colspan='6'></th></tr>");
        // 1055-C (the developer, 2026-11-21): the rows go by name, A-Z without case, joining projects among
        // them; the chips keep the pool file's colours (names) and the notices follow the rows.
        var rows = new List<(string Name, string Html)>();
        foreach (var snapshot in projects.OrderBy(s => s.Project, StringComparer.OrdinalIgnoreCase))
        {
            var view = pool?.Projects.FirstOrDefault(p => string.Equals(p.Name, snapshot.Project, StringComparison.OrdinalIgnoreCase));
            var project = snapshot.Project;
            var row = new StringBuilder();
            row.Append("<tr data-project='").Append(Enc(project)).Append("'>")
                 .Append("<td class='nw'>").Append(ProjectCell(project, names, view?.Repo ?? "")).Append("</td>")
                 .Append("<td class='wrap'>").Append(HealthCell(snapshot)).Append("</td>");
            if (workspaces)
            {
                row.Append("<td class='nw'>").Append(WorkspacesCell(view, project, controlsAllowed)).Append("</td>");
            }

            foreach (var button in RowButtons(snapshot, controlsAllowed))
            {
                // 1004-C (18:49 item 2): a button column is as wide as its button (RowStyle), so the
                // health column takes the rest of the width and the buttons sit at normal spacing.
                row.Append("<td class='nw rowbtn'>").Append(button).Append("</td>");
            }

            row.Append("</tr>");
            rows.Add((project, row.ToString()));
            if (snapshot.Notice.Length > 0)
            {
                // 1007-C: the project's notice goes where the page's notices go, above the table with
                // the project's chip in front, so the table keeps one row per project.
                notices.Append("<div class='notice' data-project='").Append(Enc(project)).Append("'>").Append(ProjectCell(project, names))
                       .Append(' ').Append(Enc(snapshot.Notice)).Append("</div>");
            }
        }

        // 1015-B: a project still joining has its row at once, its chip and "starting..." in grey, with its
        // buttons there but disabled until it has joined and its own data fills the row.
        foreach (var project in starting)
        {
            var row = new StringBuilder();
            row.Append("<tr data-project='").Append(Enc(project)).Append("' data-starting='1'>")
                 .Append("<td class='nw'>").Append(ProjectCell(project, names)).Append("</td>")
                 .Append("<td class='wrap'><span class='mute'>starting...</span></td>")
                 .Append(workspaces ? "<td class='nw'></td>" : "");
            foreach (var label in StartingButtons)
            {
                row.Append("<td class='nw rowbtn'>")
                     .Append(Post("/", [], label, "This project is still joining the pool; its buttons work once it has joined.", controlsAllowed, disabled: true))
                     .Append("</td>");
            }

            row.Append("</tr>");
            rows.Add((project, row.ToString()));
        }

        foreach (var (_, html) in rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            table.Append(html);
        }

        // VW-3: the other views keep the title bar and drop the Projects table (notices stay).
        var body = new StringBuilder(notices.ToString());
        if (!plans)
        {
            body.Append(table).Append("</table>");
            AppendStatistics(body, projects, names, pool);
            body.Append(PoolWorkerTypes(projects, controlsAllowed, pool));
        }

        // The pool's own window leads the main column in every layout (520-A item 5), the same way the
        // single page's Control window does; PageThemes.cs is category C's, so the order is set here.
        var version = projects.Select(snapshot => snapshot.Version).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
        Window(
            sb,
            version.Length == 0 ? "Switchyard" : $"Switchyard v{version}",
            body.ToString(),
            PoolBar(projects, controlsAllowed, viewName),
            "id='w-projects' data-view='projects' style='order: -2'",
            headExtra: PoolTitle(projects, theme, remote, controlsAllowed));
    }

    // 1029-B: "workspaces 3 of 4 busy": the project's running workers over its workspaces;
    // amber when all are busy (its cards wait for one), grey otherwise. Empty for a project the pool has no count for.
    private static string WorkspacesCell(PoolProjectView? view, string project, bool controlsAllowed)
    {
        if (view is null || view.Workspaces <= 0)
        {
            return "";
        }

        // A worker on a workspace above a lowered count keeps it until it ends, so busy can be more than the count.
        var tone = view.Running >= view.Workspaces ? "warn" : "mute";
        var tip = $"At most {view.Workspaces} workers of this project run at once, one in each workspace.";
        var busy = $"<span class='{tone}' title='{Enc(tip)}'>{view.Running} busy</span>";
        // 1033-C: a number box (1..64) that posts on Enter or leaving it; read-only without control.
        if (!controlsAllowed)
        {
            return $"<span title='{Enc(tip + " " + RemoteReason)}'>{view.Workspaces}</span> {busy}";
        }

        return $"<form method='post' action='/workspaces' class='g wsform' data-busy='{view.Running}' data-stored='{view.Workspaces}'>"
             + $"<input type='hidden' name='project' value='{Enc(project)}'>"
             + $"<input type='number' name='workspaces' class='wsbox' min='1' max='64' step='1' value='{view.Workspaces}' "
             + $"aria-label='Workspaces of {Enc(project)}' title='{Enc(tip)}'></form> {busy}";
    }

    // The project's health, one state at a time (1004-C, 18:49 item 6): while a check runs only
    // "checking <sha> (n min)", grey; otherwise the last result, green or red, or grey when main has
    // not been checked yet. With its log when it has one (510-B).
    private static string HealthCell(PageSnapshot snapshot)
    {
        var health = snapshot.Health;
        var text = health.Text.Length == 0 ? "main not checked yet" : health.Text;
        var tone = PageRenderer.HealthTone(health);
        var checking = HealthChecking.Sha(health);
        var at = text.IndexOf("checking " + checking, StringComparison.OrdinalIgnoreCase);
        if (checking.Length > 0 && at >= 0)
        {
            text = text[at..];
            tone = "mute";
        }

        return "<span class='" + tone + "'>" + Enc(text) + "</span>"
             + (health.HasLog ? " &nbsp; <a href='/health?project=" + Url(snapshot.Project) + "'>log</a>" : "");
    }

    // A row's six buttons, in order, each acting on this project only; a project with no planner has
    // an empty place where Planner push would be, so the columns still line up.
    private static IEnumerable<string> RowButtons(PageSnapshot snapshot, bool controlsAllowed)
    {
        (string, string)[] fields = [("project", snapshot.Project)];

        // 930-C: while a check runs the button is pressed and cannot be pressed again. 1004-C (18:49
        // item 1): it still reads Check main, so every row shows the button; the health cell beside
        // it says which sha is being checked.
        var checking = HealthChecking.Sha(snapshot.Health);
        yield return checking.Length > 0
            ? Post("/health-now", fields, "Checking...", $"A health check of {checking} is running; it cannot be started again until it finishes.", controlsAllowed, "checking", disabled: true)
            : Post("/health-now", fields, "Check main", "Build and test this project's main branch now. A red result pauses this project.", controlsAllowed);

        yield return Post("/open-lanes", fields, "Categories", "Open this project's categories file in this PC's editor.", controlsAllowed);

        var push = snapshot.PlannerPush
            ?? (snapshot.PlannerLine.Length == 0 ? null : snapshot.PlannerLine.Contains("push: on", StringComparison.OrdinalIgnoreCase));
        var planner = snapshot.PlannerLine.Length == 0 ? "" : snapshot.PlannerLine + ". ";
        yield return push switch
        {
            true => Post("/planner-toggle", fields, "Planner push: on", planner + "This project's planner outbox is on. Click to turn it off.", controlsAllowed, "pressed ok"),
            false => Post("/planner-toggle", fields, "Planner push: off", planner + "This project's planner outbox is off. Click to turn it on.", controlsAllowed),
            _ => "",
        };

        yield return snapshot.State.State == SchedulerState.Running
            ? Post("/pause", fields, "Pause", "Start no new workers in this project; running ones finish.", controlsAllowed, "warn")
            : Post("/resume", fields, "Resume", "This project is not taking cards. Click to start its workers again.", controlsAllowed, "pressed ok");
        // 1004-C (18:49 item 3): Drain has Stop's red look.
        yield return Post("/drain", fields, "Drain", "Let this project's running workers finish, start none, then stay paused.", controlsAllowed, "bad");
        yield return Post("/stop-project", fields, "Stop", "Ask this project's workers to stop cleanly and pause it; the scheduler and the other projects carry on.", controlsAllowed, "bad");
    }

    // MP-P9: the project's chip under All projects with two or more projects; its plain name when
    // the pool has one project, so a lone project's rows still say whose they are.
    // With a repo folder the chip's hover reads "<name> - <repo>"; ProjectChip itself sets the name alone.
    private static string ProjectCell(string project, IReadOnlyList<string> names, string repo = "")
    {
        if (names.Count < 2)
        {
            return "<span class='proj'>" + Enc(project) + "</span>";
        }

        var chip = ProjectChip.Html(project, names);
        return repo.Length == 0
            ? chip
            : chip.Replace("title='" + Enc(project) + "'", "title='" + Enc(project + " - " + repo) + "'", StringComparison.Ordinal);
    }

    // One list across every project (MP-P3); each row names the project it came from so the dropdown
    // can filter it (MP-P2), and every button posts project= beside card=.
    private void AppendWaiting(StringBuilder sb, IReadOnlyList<PageSnapshot> projects, IReadOnlyList<string> names, bool controlsAllowed)
    {
        var rows = projects.SelectMany(snapshot => WaitingRows(snapshot, names, controlsAllowed)).ToList();
        if (rows.Count == 0)
        {
            // 520-A item 5 (the developer, 2026-11-18): an empty Waiting on you is no window at all.
            return;
        }

        var table = new StringBuilder("<table class='waiting'>");
        table.Append("<tr><th></th><th>Project</th><th>Card</th><th>Actions</th><th>Notes</th></tr>");
        foreach (var row in rows)
        {
            table.Append(row);
        }

        Window(sb, "Waiting on you", table.Append("</table>").ToString(), "", "id='w-waiting' data-view='waiting'");
    }

    // A project's waiting rows, whole. Each card's row carries data-card and data-project, and each
    // question row is a qrow whose forms carry the project, card and question, so the page's pending
    // mask (957-B, 990-B) hides what a click took away in that project only.
    private static IEnumerable<string> WaitingRows(PageSnapshot snapshot, IReadOnlyList<string> names, bool controlsAllowed)
    {
        var running = snapshot.Slots
            .Where(slot => slot.Card is { Length: > 0 })
            .Select(slot => CardId(slot.Card))
            .ToHashSet();

        var project = snapshot.Project;
        foreach (var item in snapshot.Waiting)
        {
            // A review card is hidden while its own worker still runs it (PAGE-5).
            if (item.Kind == PageWaitingKind.Review && running.Contains(CardId(item.Name)))
            {
                continue;
            }

            var (word, tone) = item.Kind switch
            {
                PageWaitingKind.HeldCategory => ("held category", "bad"),
                PageWaitingKind.Stuck => ("stuck", "bad"),
                PageWaitingKind.Question => ("question", "bad"),
                PageWaitingKind.Waiting => ("waiting", "warn"),
                PageWaitingKind.TemplateEdited => ("template", "warn"),
                _ => ("review", "warn"),
            };

            // An edited template copy is not a card, so it is not linked (580-A).
            var isCard = item.Kind is not (PageWaitingKind.TemplateEdited or PageWaitingKind.HeldCategory);
            var name = isCard ? CardLink(item.Name, project) : Enc(item.Name);
            var row = new StringBuilder("<tr data-project='").Append(Enc(project)).Append('\'')
                .Append(isCard ? " data-card='" + Enc(CardId(item.Name)) + "'" : "").Append('>')
                // 968-C: the status word, project and actions are short; the card ellipsises; the notes wrap.
                .Append("<td class='nw'><span class='").Append(tone).Append("'>").Append(word).Append("</span></td>")
                .Append("<td class='nw'>").Append(ProjectCell(project, names)).Append("</td>")
                .Append("<td class='card'>").Append(name).Append("</td>")
                .Append("<td class='nw'>").Append(WaitingActions(item, project, controlsAllowed)).Append("</td>")
                .Append("<td class='wrap'>").Append(Enc(item.Note)).Append("</td></tr>");
            yield return row.ToString();

            foreach (var question in item.Questions)
            {
                var dis = controlsAllowed ? "" : " disabled";
                // 1099-C: the card has one note box (in its Actions cell); the answers are buttons of that form.
                var formId = NoteId(project, item.Name);
                var choices = new StringBuilder();
                for (var i = 0; i < question.Choices.Count; i++)
                {
                    var choice = question.Choices[i];
                    var tip = choice.Destination is { } to
                        ? $"{choice.Label}; the card then goes to {to.ToString().ToLowerInvariant()}/"
                        : choice.Label;
                    choices.Append("<button form='").Append(formId).Append("' formaction='/answer?q=").Append(question.Number)
                           .Append("' name='a' value='").Append(i.ToString(CultureInfo.InvariantCulture))
                           .Append("' title='").Append(Enc(tip)).Append('\'').Append(dis).Append('>')
                           .Append(Enc(choice.Label)).Append("</button> ");
                }

                // The marks the page's pending mask reads to hide this question once it is answered.
                var noteForm = $"<span class='q'><input type='hidden' name='card' value='{Enc(item.Name)}'><input type='hidden' name='q' value='{question.Number}'>"
                             + $"{Enc(question.Text)}</span>";
                yield return "<tr class='qrow' data-project='" + Enc(project) + "'><td></td><td class='nw'>" + ProjectCell(project, names) + "</td>"
                           + $"<td class='mute nw'>Q{question.Number}</td><td colspan='2' class='wrap'>{noteForm}<div class='qchoices'>{choices.ToString().TrimEnd()}</div></td></tr>";
            }
        }
    }

    // 1010-C: the single page's Open button, carrying the project so the card opens in its own folder.
    private static string OpenCard(IReadOnlyList<(string Name, string Value)> card, bool controlsAllowed) =>
        Post("/open", card, "Open", $"Open {card[1].Value} in the default editor on the PC running the scheduler.", controlsAllowed);

    private static string WaitingActions(PageWaitingItem item, string project, bool controlsAllowed)
    {
        (string, string)[] card = [("project", project), ("card", item.Name)];
        (string, string)[] file = [("project", project), ("file", item.Name)];
        // 1099-C: every card button sends the one note box of its card, whichever it is.
        var note = NoteId(project, item.Name);
        var actions = item.Kind switch
        {
            // A held category does not happen in a pool: categories are accepted at once (AREA-2), so
            // /accept-lane is not carried over (MP-P8); the categories file can still be opened.
            PageWaitingKind.HeldCategory =>
                Post("/open-lanes", [("project", project)], "Open", "Open the categories file in the editor.", controlsAllowed),
            // Roadmap item 1 (580-A): Overwrite takes the toolkit's template; Keep leaves the project's
            // copy and stops asking until the toolkit's template changes again.
            PageWaitingKind.TemplateEdited =>
                Post("/template-overwrite", file, "Overwrite", "Take the toolkit's template for this file, in one commit to main.", controlsAllowed)
                + Post("/template-keep", file, "Keep", "Leave the project's copy and stop asking until the toolkit's template changes again.", controlsAllowed),
            // 946-B: the log of the run that stuck it, beside the button that clears the count; the
            // log is the project's own (MP-P8).
            PageWaitingKind.Stuck =>
                OpenCard(card, controlsAllowed)
                + NoteButton(note, "/retry", "Retry", "Clear the run count so the scheduler resumes it. The note, if any, goes into the card's ## Dev section.", controlsAllowed)
                + LogLink(item.Log, project, "The last run&#39;s log"),
            PageWaitingKind.Question =>
                OpenCard(card, controlsAllowed)
                + NoteButton(note, "/resume-card", "Resume", "Back to work: set it ready so a worker resumes it now. The note, if any, goes into the card's ## Dev section.", controlsAllowed),
            PageWaitingKind.Waiting =>
                OpenCard(card, controlsAllowed)
                + NoteButton(note, "/resume-card", "Ready", "Done what it waits for: set it ready. The note, if any, goes into the card's ## Dev section.", controlsAllowed),
            _ =>
                OpenCard(card, controlsAllowed)
                + NoteButton(note, "/done", "Done", "Reviewed: mark this card done and move it to done/. The note, if any, goes into the card's ## Dev section.", controlsAllowed)
                + NoteButton(note, "/resume-card", "Send back", "Not finished: back to active/, ready. The note, if any, goes into the card's ## Dev section.", controlsAllowed),
        };
        return item.Kind is PageWaitingKind.HeldCategory or PageWaitingKind.TemplateEdited
            ? actions
            : actions + NoteBox(note, project, item.Name, controlsAllowed);
    }

    // 1099-C: a card's note box and its Save note button, one form per project and card; the card's other
    // buttons (and its question answers) are buttons of this form, so what is typed goes with whichever is pressed.
    private static string NoteId(string project, string card) =>
        System.Text.RegularExpressions.Regex.Replace($"n-{project}-{CardId(card)}", "[^A-Za-z0-9-]", "-");

    private static string NoteBox(string id, string project, string card, bool controlsAllowed) =>
        controlsAllowed
            ? $"<form id='{id}' method='post' action='/note' class='g' data-tip='Type a note, then press any button of this card to send it with that, or Save note to file it in the card&#39;s ## Dev section without moving it.'>"
              + $"<input type='hidden' name='project' value='{Enc(project)}'><input type='hidden' name='card' value='{Enc(card)}'>"
              + "<input name='note' maxlength='500' placeholder='note (optional)'> <button>Save note</button></form>"
            : "";

    private static string NoteButton(string id, string action, string label, string tip, bool controlsAllowed) =>
        controlsAllowed
            ? $"<span class='g' data-tip='{Enc(tip)}'><button form='{id}' formaction='{action}' aria-label='{Enc(label + ". " + tip)}'>{Enc(label)}</button></span>"
            : $"<span class='g' data-tip='{Enc(tip + " " + RemoteReason)}'><button disabled>{Enc(label)}</button></span>";

    // 1004-C: the statistics under the Projects table, as the single page writes them (520-A): the
    // "Last 24 h (...)" line and one line per model, each on a line of its own with its indent kept.
    // They follow the dropdown: under All projects only the lines summed over every project show
    // (MP-P5), and with one project picked only that project's own (the style below the filter
    // script hides the other set). A pool of one project shows that project's lines alone.
    private static void AppendStatistics(StringBuilder body, IReadOnlyList<PageSnapshot> projects, IReadOnlyList<string> names, PoolPageSnapshot? pool)
    {
        var several = names.Count >= 2;
        body.Append("<div class='poolstats'>");
        body.Append(RangeSelect());
        // 1095-C: each range's lines sit in a wrapper the style beside the filter script shows or hides.
        // 1098-C: the non-default ones are also rendered hidden, so only the chosen range shows even
        // before the script runs and even if the style block ever fails to apply; applyRange toggles it.
        foreach (var range in CardStats.Ranges)
        {
            body.Append("<div class='rng' data-range='").Append(range.Key).Append('\'').Append(HiddenUnless(range.Key)).Append('>');
            foreach (var snapshot in projects)
            {
                foreach (var line in StatsLines(RangeText(snapshot, range.Key)))
                {
                    body.Append("<div class='stats").Append(several ? " projonly" : "").Append("' data-project='").Append(Enc(snapshot.Project))
                        .Append("' style='white-space: pre-wrap'>").Append(Enc(line)).Append("</div>");
                }
            }

            if (several)
            {
                foreach (var line in SummedStats(projects, range.Key))
                {
                    body.Append("<div class='stats allonly' style='white-space: pre-wrap'>").Append(Enc(line)).Append("</div>");
                }
            }

            body.Append("</div>");
        }

        if (projects.Count == 0)
        {
            body.Append("<div class='mute'>No projects in the pool.</div>");
        }

        AppendLockCounts(body, pool, several);
        body.Append("</div>");
    }

    // DL-26 (1022-C): the day's lock events, one line per project that has counts (locks on), and under All
    // projects the sum. Nothing shows while locks are off: the pool then gives no counts.
    private static void AppendLockCounts(StringBuilder body, PoolPageSnapshot? pool, bool several)
    {
        var counted = (pool?.Projects ?? []).Where(p => p.LockCounts is not null).ToList();
        foreach (var view in counted)
        {
            body.Append("<div class='stats locks").Append(several ? " projonly" : "").Append("' data-project='").Append(Enc(view.Name))
                .Append("'>").Append(Enc(LockCountsText(view.LockCounts!))).Append("</div>");
        }

        if (several && counted.Count > 0)
        {
            var all = new PageLockCounts
            {
                Day = counted.Max(v => v.LockCounts!.Day),
                Grants = counted.Sum(v => v.LockCounts!.Grants),
                Waits = counted.Sum(v => v.LockCounts!.Waits),
                Yields = counted.Sum(v => v.LockCounts!.Yields),
                Refusals = counted.Sum(v => v.LockCounts!.Refusals),
                PushesRefused = counted.Sum(v => v.LockCounts!.PushesRefused),
                Strays = counted.Sum(v => v.LockCounts!.Strays),
            };
            body.Append("<div class='stats locks allonly'>").Append(Enc(LockCountsText(all))).Append("</div>");
        }
    }

    internal static string LockCountsText(PageLockCounts c) =>
        $"Locks today: {c.Grants} granted, {c.Waits} waited, {c.Yields} yielded, {c.Refusals} refused, {c.PushesRefused} pushes refused, {c.Strays} strays";

    // DL-24, DL-25 (1022-C): a running card's held set - the claim on one line, what it was granted on another,
    // each with its age in minutes - and its wait. Empty when locks are off (no Holds) or it holds nothing.
    internal static string HoldsHtml(PageHolds? holds)
    {
        if (holds is null || (holds.Claim.Count == 0 && holds.Also.Count == 0 && holds.Wait.Length == 0))
        {
            return "";
        }

        var sb = new StringBuilder("<div class='holds mute'>");
        if (holds.Claim.Count > 0)
        {
            sb.Append("<div>holds: ").Append(Enc(string.Join(", ", holds.Claim))).Append(" (").Append(holds.ClaimMinutes).Append(" min)</div>");
        }

        if (holds.Also.Count > 0)
        {
            sb.Append("<div>also holds: ").Append(Enc(string.Join(", ", holds.Also.Select(e => $"{e.Path} ({e.Minutes} min)")))).Append("</div>");
        }

        if (holds.Wait.Length > 0)
        {
            sb.Append("<div class='warn'>").Append(Enc(holds.Wait)).Append("</div>");
        }

        return sb.Append("</div>").ToString();
    }

    // 1098-C: the wrapper attribute that keeps a range out of the page until the script or the
    // reader picks it: every range but the default renders hidden.
    private static string HiddenUnless(string key) => key == CardStats.DefaultRangeKey ? "" : " hidden";

    // 1095-C: the one dropdown that drives the runs summary and the Trends chart (the script beside
    // the project filter remembers it, and the style beside it shows the chosen range's elements).
    private static string RangeSelect()
    {
        var sb = new StringBuilder("<div class='statsrange'><select id='rangesel' title='The time range of the summary and the Trends chart' aria-label='Time range' style='font: inherit'>");
        foreach (var range in CardStats.Ranges)
        {
            sb.Append("<option value='").Append(range.Key).Append("'")
              .Append(range.Key == CardStats.DefaultRangeKey ? " selected" : "").Append('>').Append(Enc(range.Name)).Append("</option>");
        }

        return sb.Append("</select></div>").ToString();
    }

    // A project's summary for a range: its own, or on a snapshot built without ranges the 24 h text.
    private static string RangeText(PageSnapshot snapshot, string key) =>
        snapshot.Stats.Ranges.FirstOrDefault(r => r.Key == key)?.Text
        ?? (key == CardStats.DefaultRangeKey ? snapshot.Stats.Text : "");

    private static IEnumerable<string> StatsLines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0);

    /// <summary>
    /// MP-P5, 1004-C: every project's runs summed, in the lines ACCT-6 writes for one project -
    /// "Last 24 h (5 h 5 min): 4 cards (5 runs): done 3, ...; 0.17 cards/hr", then each model's
    /// line summed over the projects that ran it, in the order they first appear.
    /// </summary>
    public static IReadOnlyList<string> SummedStats(IReadOnlyList<PageSnapshot> projects, string rangeKey = CardStats.DefaultRangeKey)
    {
        var range = CardStats.RangeOf(rangeKey) ?? CardStats.RangeOf(CardStats.DefaultRangeKey)!;
        Tally? head = null;
        var models = new List<(string Model, Tally Sum)>();
        foreach (var line in projects.SelectMany(snapshot => StatsLines(RangeText(snapshot, range.Key))))
        {
            var match = StatsLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var tally = Tally.Of(match);
            var label = match.Groups["label"].Value;
            if (match.Groups["indent"].Length == 0)
            {
                if (string.Equals(label, "Last " + range.Label, StringComparison.Ordinal))
                {
                    head = head is null ? tally : head.Add(tally);
                }

                continue;
            }

            var at = models.FindIndex(m => string.Equals(m.Model, label, StringComparison.OrdinalIgnoreCase));
            if (at < 0)
            {
                models.Add((label, tally));
            }
            else
            {
                models[at] = (models[at].Model, models[at].Sum.Add(tally));
            }
        }

        if (head is null)
        {
            return ["Last " + range.Label + ": no runs finished."];
        }

        return ["Last " + range.Label + " " + head.Text(range.Window.TotalHours), .. models.Select(m => "  " + m.Model + " " + m.Sum.Text(range.Window.TotalHours))];
    }

    // One stats line's numbers (CardStats.StatsText's format), and their sum over projects.
    private sealed record Tally(int Minutes, int Cards, int Runs, int Done, int Review, int Questions, int Open)
    {
        public static Tally Of(System.Text.RegularExpressions.Match match)
        {
            int Of(string group) => match.Groups[group].Success ? Int(match.Groups[group].Value) : 0;
            return new Tally((Of("h") * 60) + Of("m"), Of("cards"), Of("runs"), Of("done"), Of("review"), Of("q"), Of("open"));
        }

        public Tally Add(Tally other) => new(
            Minutes + other.Minutes, Cards + other.Cards, Runs + other.Runs, Done + other.Done,
            Review + other.Review, Questions + other.Questions, Open + other.Open);

        // As CardStats writes it: the run time, the counts, and (done + review) / 24 to two decimals.
        public string Text(double hours = 24)
        {
            var time = Minutes >= 60
                ? string.Format(CultureInfo.InvariantCulture, "{0} h {1} min", Minutes / 60, Minutes % 60)
                : string.Format(CultureInfo.InvariantCulture, "{0} min", Minutes);
            var rate = Math.Round((Done + Review) / hours, 2, MidpointRounding.ToEven).ToString(CultureInfo.InvariantCulture);
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0}): {1} cards ({2} runs): done {3}, {4} in review, {5} waiting on questions, {6} open; {7} cards/hr",
                time,
                Cards,
                Runs,
                Done,
                Review,
                Questions,
                Open,
                rate);
        }
    }

    private static int Int(string text) => int.Parse(text, CultureInfo.InvariantCulture);

    // 1004-C: the Worker types panel exactly as the single page has it (WT-P3, 991-C): collapsible,
    // its one-line summary beside the heading, Save posting to /worker-types. The types are the
    // pool's, global to every project (MP-C4), so there is one panel, its running counts summed over
    // the projects, and its Save writes the pool file (1000-B, WT-C8); the dropdown leaves it alone.
    private static string PoolWorkerTypes(IReadOnlyList<PageSnapshot> projects, bool controlsAllowed, PoolPageSnapshot? pool)
    {
        if (projects.Count == 0)
        {
            return "";
        }

        var source = projects.FirstOrDefault(snapshot => snapshot.WorkerTypes.Count > 0)?.WorkerTypes;
        IReadOnlyList<PageWorkerTypeView> types = source is not null
            ? [.. source.Select(type => type with { Running = projects.Sum(snapshot => RunningOf(snapshot, type.Name)) })]
            : [.. (pool?.Types ?? []).Select(type => new PageWorkerTypeView
            {
                Name = type.Name,
                Max = type.Max,
                Agent = type.Agent,
                // 1087-C (WT-P3): the pool file's own settings travel with the type now, so the
                // boxes are not empty on the pool page; the effective model is the Host's to fill.
                Model = type.Model,
                EffectiveModel = type.EffectiveModel,
                Endpoint = type.Endpoint,
                MetricsUrl = type.MetricsUrl,
                IsDefault = type.IsDefault,
                Projects = type.Projects,
                Running = type.RunningByProject.Values.Sum(),
            })];
        var total = PoolTotal(projects, pool);
        return PageRenderer.WorkerTypesPanel(projects[0] with { WorkerTypes = types }, controlsAllowed, null, total, null);
    }

    // The pool's total (WT-C1, MP-11): the pool file's, or, without a pool part, what the single
    // page shows for each project (its slots or its types), added up.
    private static int PoolTotal(IReadOnlyList<PageSnapshot> projects, PoolPageSnapshot? pool) =>
        pool is { MaxWorkers: > 0 } ? pool.MaxWorkers : projects.Sum(snapshot => Math.Max(snapshot.Slots.Count, snapshot.WorkerTypes.Count));

    private static int RunningOf(PageSnapshot snapshot, string type) =>
        snapshot.WorkerTypes.Where(t => string.Equals(t.Name, type, StringComparison.OrdinalIgnoreCase)).Sum(t => t.Running);

    // CardStats' line: "Last 24 h (5 h 5 min): 4 cards (5 runs): done 3, 1 in review, 0 waiting on
    // questions, 0 open; 0.17 cards/hr", or a model's, indented, with its name for the label.
    [System.Text.RegularExpressions.GeneratedRegex(@"^(?<indent>\s*)(?<label>\S.*?) \((?:(?<h>\d+) h )?(?<m>\d+) min\): (?<cards>\d+) cards \((?<runs>\d+) runs\): done (?<done>\d+), (?<review>\d+) in review, (?<q>\d+) waiting on questions, (?<open>\d+) open;")]
    private static partial System.Text.RegularExpressions.Regex StatsLine();

    // WT-P1: one window per running worker across the pool, each carrying the project it runs a card
    // of so the dropdown can hide the others (MP-8); the same place the single page gives them.
    private void AppendWorkerWindows(StringBuilder sb, IReadOnlyList<PageSnapshot> projects, IReadOnlyList<string> names, bool controlsAllowed)
    {
        foreach (var snapshot in projects)
        {
            foreach (var slot in snapshot.Slots.Where(slot => !string.IsNullOrEmpty(slot.Card)))
            {
                sb.Append(WorkerWindow(snapshot, slot, names, controlsAllowed));
            }
        }
    }

    // WT-P1's window: "<type> (<model>) - <card>", its log and stats, the message box, and
    // Minimize, Stop and Kill. A pool worker is named project:slot everywhere the single page's is
    // named by its number, which is how the live view tells two projects' slot 1 apart (WT-W3).
    private string WorkerWindow(PageSnapshot snapshot, PageSlotView slot, IReadOnlyList<string> names, bool controlsAllowed)
    {
        var project = snapshot.Project;
        var key = project + ":" + slot.Slot.ToString(CultureInfo.InvariantCulture);
        var at = "?project=" + Url(project) + "&slot=" + slot.Slot.ToString(CultureInfo.InvariantCulture);
        (string, string)[] slotFields = [("project", project), ("slot", slot.Slot.ToString(CultureInfo.InvariantCulture))];
        var model = string.IsNullOrEmpty(slot.ModelShort) ? slot.Model : slot.ModelShort;
        var type = !string.IsNullOrEmpty(slot.Type) ? slot.Type
            : !string.IsNullOrEmpty(slot.WorkerName) ? slot.WorkerName
            : "worker";
        var gone = snapshot.WorkerTypes.Count > 0
            && !snapshot.WorkerTypes.Any(t => string.Equals(t.Name, slot.Type, StringComparison.OrdinalIgnoreCase));
        // 1004-C (18:57 item 13, 18:59 item 14): "<chip> <type> (<model>) - <card>", as the single page
        // names its workers by type; a slot number means nothing to the developer. The project's chip
        // (MP-P9) is always there - with one project too, and while one project is picked (wchip) - and
        // the card links to its file view in its own project.
        var title = WorkerChip(project, names) + " " + Enc((gone ? type + " (removed)" : type)
            + (string.IsNullOrEmpty(model) ? "" : $" ({model})")
            + " - ") + CardLink(slot.Card, project);

        var body = new StringBuilder();
        if (!string.IsNullOrEmpty(slot.StopWhy))
        {
            body.Append("<div class='warn'>Stopping: ").Append(Enc(slot.StopWhy))
                .Append(slot.KillAt is { } killAt ? $". Killed at {killAt:HH:mm} if still running." : "")
                .Append("</div>");
        }

        // The model and the model server's address come off the end of the stats line here too
        // (490-C, 971-C): the title bar above names the model, and the address belongs to the Worker
        // types panel, where it is set.
        body.Append("<div class='stats' data-slot='").Append(Enc(key)).Append("' data-model='").Append(Enc(slot.Model))
            .Append("'>").Append(Enc(PageRenderer.StatsLine(slot.Stats, slot.Model))).Append("</div>")
            .Append(HoldsHtml(slot.Holds))
            .Append("<pre class='log' data-slot='").Append(Enc(key)).Append("'>")
            .Append(Enc(string.Join('\n', slot.LogTail))).Append("</pre>")
            .Append("<form method='post' action='/send").Append(at).Append("' class='send' data-project='").Append(Enc(project))
            .Append("'><input name='text' maxlength='2000' placeholder='Message to this worker'")
            .Append(controlsAllowed ? "" : " disabled").Append("><button").Append(controlsAllowed ? "" : " disabled")
            .Append(">Send</button></form>");

        var tools = new StringBuilder()
            .Append($"<button type='button' class='logtoggle' data-slot='{Enc(key)}' title='Show only the title bar'>Minimize</button>")
            .Append(string.IsNullOrEmpty(slot.StopWhy)
                ? Post("/stop", slotFields, "Stop", "Ask this worker to stop at its next safe point; the card then waits under Waiting on you with Resume.", controlsAllowed, "bad")
                : "")
            .Append(Post("/kill", slotFields, "Kill", "End this worker and everything it started, now. The card waits under Waiting on you with Resume.", controlsAllowed, "bad"));

        // 1008-C: the summary the single page's title bar carries at every size, Minimize included: the
        // time-limit bar, the spinner with the average T/s, and the minutes run (PageRenderer's window is
        // the reference). Only the inner part, the log, collapses; the live script updates these by key.
        var run = slot.Start is { } start ? Math.Max(0, (int)(snapshot.Built - start).TotalMinutes) : (int?)null;
        var minutes = (run is null ? "" : slot.TimeLimit is { } limit and > 0 ? $"{run}/{limit} min" : $"{run} min")
            + (slot.QuietMinutes is { } quiet ? $", quiet {quiet} min" : "");
        var fill = run is { } r && slot.TimeLimit is { } l && l > 0 ? Math.Min(100, 100 * r / l) : (int?)null;
        var k = Enc(key);
        var summary = (fill is { } pct ? $"<span class='prog{(pct >= 90 ? " late" : "")}' data-slot='{k}' style='width:{pct}%'></span>" : $"<span class='prog' data-slot='{k}'></span>")
            + $"<span class='mini{(slot.QuietMinutes is null ? "" : " idle")}' data-slot='{k}'><span class='spin'></span><span class='tps'>{Enc(TpsText(slot.Stats))}</span></span>"
            + $"<span class='tag' data-slot='{k}'>{Enc(minutes)}</span>";

        return "<section class='win' data-project='" + Enc(project) + "' data-card='" + Enc(CardId(slot.Card)) + "'>"
             + "<div class='bar'><b>" + title + "</b>" + summary + tools + "</div>"
             + "<div class='inner'>" + body + "</div></section>";
    }

    // The title bar's T/s, as PageRenderer reads it from the stats line.
    private static string TpsText(string stats)
    {
        var match = System.Text.RegularExpressions.Regex.Match(stats ?? "", @"([\d.]+) avg");
        return match.Success ? match.Groups[1].Value + " T/s avg" : "- T/s";
    }

    // 1004-C (18:59 item 14): a worker window's chip, always shown: with one project too (its own colour,
    // as the first of the list), and while one project is picked, which hides the rows' chips (wchip).
    internal static string WorkerChip(string project, IReadOnlyList<string> names) =>
        ProjectChip.Html(project, names.Count >= 2 ? names : [project, ""])
            .Replace("class='chip projchip'", "class='chip projchip wchip'", StringComparison.Ordinal);

    private void AppendQueue(StringBuilder sb, IReadOnlyList<PageSnapshot> projects, IReadOnlyList<string> names, PoolPageSnapshot? pool)
    {
        var table = new StringBuilder("<table class='queue'>");
        var any = false;
        // 1055-C: with a pool, the rows go in the order the pool will start them (PoolQueueOrder); picking
        // one project still shows its own order, which the pool order keeps within each project.
        foreach (var (snapshot, item) in QueueRows(projects, pool))
        {
            any = true;
            // 968-C: the project is short and never broken; the card ellipsises with the whole name
            // in its tooltip; the title and the note are the columns that wrap.
            table.Append("<tr data-project='").Append(Enc(snapshot.Project)).Append("' data-card='").Append(Enc(CardId(item.Name))).Append("'>")
                 .Append("<td class='nw'>").Append(ProjectCell(snapshot.Project, names)).Append("</td>")
                 .Append("<td class='card'>").Append(CardLink(item.Name, snapshot.Project, CardTip(item.Name, snapshot.Project))).Append("</td>")
                 .Append("<td class='wrap'>").Append(Enc(item.Title)).Append("</td>")
                 .Append("<td class='mute wrap'>").Append(Enc(item.Note));
            // DL-25: a yielded card's note; DL-24: a running card's held set, and its wait.
            if (item.Yielded.Length > 0)
            {
                table.Append(" <span class='yielded'>").Append(Enc(item.Yielded)).Append("</span>");
            }

            var running = snapshot.Slots.FirstOrDefault(slot => string.Equals(CardId(slot.Card), CardId(item.Name), StringComparison.OrdinalIgnoreCase));
            table.Append(HoldsHtml(running?.Holds));
            // Roadmap item 4 (640-A): the time-box warning or suggestion joins the note column.
            if (item.TimeBoxNote.Length > 0)
            {
                table.Append(" <span class='warn'>").Append(Enc(item.TimeBoxNote)).Append("</span>");
            }

            table.Append("</td></tr>");
        }

        // 1004-C (the developer, 2026-11-20): no header row; it drew wrongly and the columns read
        // clearly without it.
        if (!any)
        {
            table.Append("<tr><td class='mute'>Nothing queued.</td></tr>");
        }

        Window(sb, "Queue", table.Append("</table>").ToString(), "", "id='w-queue' data-view='queue'");
    }

    // 1055-C: the Queue's rows: first those the pool's order names, in that order, then any it does not
    // (a page built a moment after the pool part), project after project in each one's own order.
    // Linear in the rows; with no pool part, each project's queue in turn as before.
    private static List<(PageSnapshot Snapshot, PageQueueItem Item)> QueueRows(IReadOnlyList<PageSnapshot> projects, PoolPageSnapshot? pool)
    {
        var rows = projects.SelectMany(snapshot => snapshot.Queue.Select(item => (Snapshot: snapshot, Item: item))).ToList();
        var order = pool?.QueueOrder ?? [];
        if (order.Count == 0)
        {
            return rows;
        }

        var byKey = new Dictionary<string, (PageSnapshot Snapshot, PageQueueItem Item)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            byKey.TryAdd(row.Snapshot.Project + "\n" + row.Item.Name, row);
        }

        var outRows = new List<(PageSnapshot Snapshot, PageQueueItem Item)>(rows.Count);
        foreach (var entry in order)
        {
            if (byKey.Remove(entry.Project + "\n" + entry.Card, out var row))
            {
                outRows.Add(row);
            }
        }

        outRows.AddRange(rows.Where(row => byKey.ContainsKey(row.Snapshot.Project + "\n" + row.Item.Name)));
        return outRows;
    }

    // The events of every project, newest first.
    private void AppendEvents(StringBuilder sb, IReadOnlyList<PageSnapshot> projects, IReadOnlyList<string> names, bool controlsAllowed)
    {
        var table = new StringBuilder("<table class='events'>");
        var newest = projects
            .SelectMany(snapshot => snapshot.Events.Select(entry => (snapshot.Project, entry)))
            .OrderByDescending(pair => pair.entry.Time)
            .Take(10)
            .ToList();
        if (newest.Count == 0)
        {
            table.Append("<tr><td class='mute'>Nothing yet.</td></tr>");
        }

        foreach (var (project, entry) in newest)
        {
            // 968-C: the time and the project are short; the card ellipsises with its whole name in the
            // tooltip. 1004-C (the developer, 2026-11-20): no worker or slot column and no run time; a
            // run's card links to its file view as on the single page, then its result and its log. An
            // event that is not a run keeps its line of text.
            table.Append("<tr data-project='").Append(Enc(project)).Append("'>")
                 .Append("<td class='mute nw'>").Append(entry.Time.ToString("HH:mm:ss")).Append("</td>")
                 .Append("<td class='nw'>").Append(ProjectCell(project, names)).Append("</td>");
            if (string.IsNullOrEmpty(entry.Card))
            {
                table.Append("<td class='").Append(Tone(entry.Tone)).Append(" wrap' colspan='4'>").Append(Enc(entry.Text)).Append("</td></tr>");
                continue;
            }

            table.Append("<td class='card'>").Append(CardLink(entry.Card, project, CardTip(entry.Card, project))).Append("</td>")
                 .Append("<td class='nw'><span class='").Append(Tone(entry.Tone)).Append("'>").Append(Enc(entry.Result)).Append("</span></td>")
                 .Append("<td class='nw'>").Append(LogLink(entry.Log, project, "This run&#39;s log")).Append("</td>")
                 .Append("<td class='nw'>").Append(OpenCard([("project", project), ("card", entry.Card)], controlsAllowed)).Append("</td></tr>");
        }

        Window(sb, "Recent events", table.Append("</table>").ToString(), "", "id='w-events' data-view='events'");
    }

    // The Trends window (roadmap item 3, card 630-A) under Recent events. 1004-C (the developer,
    // 2026-11-20): it follows the dropdown - under All projects one chart of every project's days
    // summed, with one project picked that project's own chart (the style beside the filter script
    // hides the other set). A pool of one shows its project's chart. Hidden while no project has a
    // day of data before today.
    private void AppendTrends(StringBuilder sb, IReadOnlyList<PageSnapshot> projects)
    {
        var several = projects.Count >= 2;
        var inner = new StringBuilder();
        var types = projects.FirstOrDefault(snapshot => snapshot.WorkerTypes.Count > 0)?.WorkerTypes;
        var shown = projects.Where(snapshot => Trends.HasTrend(snapshot.Trends)).ToList();
        if (shown.Count == 0)
        {
            return;
        }

        // 1095-C: one wrapper per range, which the style beside the filter script shows or hides, holding
        // what the window held for 14 days: the All projects chart, then each project's.
        // Snapshots built without ranges carry their 14 days alone: drawn once, whatever the range.
        var ranged = shown.Any(snapshot => snapshot.Stats.Ranges.Count > 0);
        foreach (var range in ranged ? CardStats.Ranges : [CardStats.RangeOf("2w")!])
        {
            if (ranged)
            {
                inner.Append("<div class='rng' data-range='").Append(range.Key).Append('\'').Append(HiddenUnless(range.Key)).Append('>');
            }

            if (several)
            {
                var sums = SummedTrends(shown.Select(snapshot => SeriesFor(snapshot, range.Key)));
                inner.Append("<div class='trendproj allonly'><div class='trendname'>All projects</div>")
                     .Append(TrendChart.Html(sums, types)).Append("</div>");
            }

            foreach (var snapshot in shown)
            {
                inner.Append("<div class='trendproj").Append(several ? " projonly" : "").Append("' data-project='").Append(Enc(snapshot.Project)).Append("'>")
                     .Append("<div class='trendname'>").Append(Enc(snapshot.Project)).Append("</div>")
                     .Append(TrendChart.Html(SeriesFor(snapshot, range.Key), snapshot.WorkerTypes))
                     .Append("</div>");
            }

            if (ranged)
            {
                inner.Append("</div>");
            }
        }

        var heading = new StringBuilder();
        foreach (var range in ranged ? CardStats.Ranges : [CardStats.RangeOf("2w")!])
        {
            heading.Append("<span class='mute").Append(ranged ? " rng" : "").Append("'").Append(ranged ? " data-range='" + range.Key + "'" + HiddenUnless(range.Key) : "")
                   .Append(">cards finished, last ").Append(ranged ? Enc(range.Label) : Trends.WindowDays + " days").Append("</span>");
        }

        Window(
            sb,
            "Trends",
            inner.ToString(),
            heading.ToString(),
            "id='w-trends' data-view='trends'");
    }

    // 1095-C: a project's Trends buckets for a range; a snapshot built without ranges has its 14 days
    // for the 2 week range alone.
    private static IReadOnlyList<TrendDay> SeriesFor(PageSnapshot snapshot, string key) =>
        snapshot.Stats.Ranges.Count == 0
            ? snapshot.Trends
            : snapshot.Stats.Ranges.FirstOrDefault(r => r.Key == key)?.Trend ?? [];

    /// <summary>
    /// 1004-C: every project's trend days added up day by day - each model's cards and runs summed
    /// across the projects, in the order the models first appear - for the All projects chart.
    /// </summary>
    public static IReadOnlyList<TrendDay> SummedTrends(IReadOnlyList<PageSnapshot> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        return SummedTrends(projects.Select(snapshot => snapshot.Trends));
    }

    /// <summary>1095-C: the same sum over series of buckets, matched by date and start time.</summary>
    public static IReadOnlyList<TrendDay> SummedTrends(IEnumerable<IReadOnlyList<TrendDay>> series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return
        [
            .. series.SelectMany(days => days)
                .GroupBy(day => (day.Date, day.Time))
                .OrderBy(group => group.Key)
                .Select(group => new TrendDay
                {
                    Date = group.Key.Date,
                    Time = group.Key.Time,
                    Label = group.First().Label,
                    Segments =
                    [
                        .. group.SelectMany(day => day.Segments)
                            .GroupBy(segment => segment.Model, StringComparer.OrdinalIgnoreCase)
                            .Select(model => new TrendSegment
                            {
                                Model = model.First().Model,
                                Label = model.First().Label,
                                CardsDone = model.Sum(segment => segment.CardsDone),
                                Runs = model.Sum(segment => segment.Runs),
                            }),
                    ],
                }),
        ];
    }

    /// <summary>
    /// 1098-C: the rules that keep one range's elements showing and the rest of them out. They ride
    /// inside #desk rather than in the head beside the script, because the live feed replaces the desk
    /// whole and never the head: a page opened before a deploy therefore gets the current rules with
    /// the current markup instead of hiding the chosen range for ever. The non-default wrappers are
    /// rendered hidden, so a reader with no script sees the default range alone; once a script has
    /// marked the body with the range it picked, the hidden attribute is let go (the second rule) and
    /// the per-range rules after it decide, which is what shows the chosen range for a page whose
    /// script predates the toggle. Order matters: the letting-go rule must precede the hiding ones.
    /// </summary>
    internal static string RangeStyle()
    {
        var sb = new StringBuilder("<style data-range-rules>");
        sb.Append("body:not([data-range]) .rng:not([data-range='").Append(CardStats.DefaultRangeKey).Append("']) { display: none; } ");
        sb.Append("body[data-range] .rng[hidden] { display: block; } ");
        foreach (var range in CardStats.Ranges)
        {
            sb.Append("body[data-range='").Append(range.Key).Append("'] .rng:not([data-range='").Append(range.Key).Append("']) { display: none; } ");
        }

        return sb.Append("</style>").ToString();
    }

    /// <summary>
    /// 1105-C (the developer, 2026-11-22 17:42: the PROJECT cell was still empty after the exemption
    /// shipped): MP-P9/VW-4's rule that takes the project chips out while the dropdown names one
    /// project, and the Projects table's exemption from it - there the chip is the row's name. They
    /// ride inside #desk beside the range rules for the same reason (1098-C): the live feed replaces
    /// the desk whole and never the head, so a page opened before a deploy keeps the head it has.
    /// Rules that decide the visibility of the desk's own markup must therefore come with that markup,
    /// which is how a page whose head still only hides the chips gets its project name back.
    /// </summary>
    internal static string ProjectChipStyle() =>
        "<style data-projchip-rules>"
        + "body[data-projsel]:not([data-projsel='all']) .projchip { display: none; } "
        + "body[data-projsel]:not([data-projsel='all']) table.projects .projchip { display: inline-block; }"
        + "</style>";

    /// <summary>
    /// The project dropdown's script (MP-P2): one choice for the whole page, kept in the browser
    /// (localStorage) and so across refreshes and views; every element with another project's
    /// data-project is hidden, the statistics show the summed lines under All and a project's own
    /// otherwise (1004-C); a field marked projsel-in still posts the picked project, or none for All
    /// (MP-H8). It hides by
    /// class, so the pending mask's own hiding (957-B) is never undone by a change of project. The
    /// project chips of MP-P9/VW-4 are not its business: those rules ride inside the desk
    /// (<see cref="ProjectChipStyle"/>), so a page keeps them in step with the markup they govern.
    /// </summary>
    // The view names as a JavaScript array literal, made from ViewList (1129-C), so the page's dropdown,
    // the address and what the browser remembers all read the same one list.
    private static string ViewNames => "[" + string.Join(",", ViewList.Select(v => "'" + v.Name + "'")) + "]";

    internal static readonly string FilterScript = $$"""
        <style>.projhide { display: none !important; }
        .statsrange { margin-bottom: 2px; }
        body:not([data-projsel]) .projonly, body[data-projsel='all'] .projonly, body[data-projsel]:not([data-projsel='all']) .allonly { display: none; }
        table.projects input.wsbox { width: 3.5em; } table.projects form.wsform { display: inline; margin: 0; }
        table.projects td.rowbtn { width: 1%; padding: 2px 3px; } table.projects td.rowbtn .g { margin: 0; }
        #w-projects button.warn, #w-projects button.ok { background: transparent; border: 1px solid currentColor; }
        #w-projects button.warn { color: var(--warn); } #w-projects button.ok { color: var(--ok); }
        #w-projects button.checking[disabled] { opacity: 1; cursor: default; }
        @media (min-width: 1400px) { #col-side { top: 0; max-height: 100vh; } }
        .wchip { display: inline-block !important; }
        .plans-project { margin-bottom: 1em; } .plan { margin: 0.6em 0; } .plan-bar { font-family: monospace; }
        .plan-step { margin-left: 1.5em; } .plan-step ul { list-style: none; margin: 0.2em 0 0.4em 1em; padding: 0; }
        .plan-archived { margin: 0.6em 0; } .pm { display: inline-block; width: 1.3em; text-align: center; }
        .pm-click { cursor: pointer; } .pm-click .pm { cursor: pointer; } .pm-set { color: var(--accent, #36c); font-size: 0.7em; vertical-align: super; }
        .pm-ok { color: var(--ok, #2a2); font-weight: bold; } .pm-bad { color: var(--bad, #c22); font-weight: bold; }
        .plan summary { cursor: pointer; } .plan-card { list-style: none; margin: 0.15em 0; }
        @media (min-width: 1400px) { .desk.wide-desk { grid-template-columns: minmax(0, 1fr); grid-template-areas: "top" "main"; } }
        .plans-pair, .gantt-pair { display: flex; flex-wrap: wrap; gap: 12px; align-items: flex-start; }
        #w-gantt, #w-plans { flex: 3 1 640px; min-width: 0; } #w-gantt-details, #w-plans-completed { flex: 1 1 300px; min-width: 0; }
        .gantt .grow { display: flex; align-items: center; min-height: 1.5em; border-bottom: 1px solid var(--line); cursor: pointer; }
        .gantt .glabel { flex: 0 0 34%; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; padding-right: 6px; }
        .gantt .gtrack { flex: 1 1 auto; position: relative; height: 1.2em; min-width: 0; } .gantt .gsvg { width: 100%; height: 100%; display: block; }
        .gantt .gcal { height: 1.4em; } .gantt .gtick, .gantt .gnowtag { position: absolute; top: 0; transform: translateX(-50%); font-size: 0.8em; white-space: nowrap; }
        .gantt .gtick { color: var(--muted); } .gantt .gnowtag { color: var(--bad); }
        .gantt .gaxis { cursor: default; position: sticky; top: 0; background: var(--panel, inherit); z-index: 1; }
        .gantt details > details, .gantt details > .gcard { margin-left: 1em; } .gantt summary { list-style: none; } .gantt summary::-webkit-details-marker { display: none; }
        .gantt summary .glabel::before { content: 'B8  '; } .gantt details[open] > summary .glabel::before { content: 'BE  '; }
        .gantt details[open] > summary .gsum rect { display: none; }
        .gantt .gplan { font-size: 0.8em; border: 1px solid var(--line); border-radius: 3px; padding: 0 3px; margin-left: 4px; }
        .gantt .gsel { outline: 1px solid var(--accent); }
        #w-gantt-details .gdtab th, #w-gantt-details .gdruns th { text-align: left; padding-right: 8px; } .gdsub { margin-top: 0.6em; font-weight: bold; }</style>
        <script>
        // VW-2 (1129-C): the browser remembers the last view for a bare address; the address names it
        // otherwise. The names are the renderer's one list of views, so a new view needs no change here.
        var viewNames = {{ViewNames}};
        function knownView(v) { v = String(v == null ? '' : v).toLowerCase(); for (var i = 0; i < viewNames.length; i++) { if (viewNames[i] === v) return v; } return viewNames[0]; }
        (function () {
          try {
            var m = /[?&]view=([^&]*)/.exec(location.search);
            if (m) localStorage.setItem('ptk-view', knownView(decodeURIComponent(m[1])));
            else if (location.pathname === '/' && !location.search) { var p = knownView(localStorage.getItem('ptk-view')); if (p !== viewNames[0]) location.replace('/?view=' + p); }
          } catch (x) {}
        })();
        var projKey = 'ptk-project';
        function projSel() { var v = null; try { v = localStorage.getItem(projKey); } catch (x) {} return v || 'all'; }
        // The page script calls it after each refresh, which replaces the desk.
        function applyFilters() {
          var sel = document.getElementById('projsel'), v = projSel();
          if (!sel || !Array.prototype.some.call(sel.options, function (o) { return o.value === v; })) v = 'all';
          if (sel) sel.value = v;
          document.body.setAttribute('data-projsel', v);
          var rows = document.querySelectorAll('#desk [data-project]');
          for (var i = 0; i < rows.length; i++) {
            var mine = rows[i].getAttribute('data-project');
            rows[i].classList.toggle('projhide', v !== 'all' && mine !== v);
          }
          var fields = document.querySelectorAll('input.projsel-in');
          for (var k = 0; k < fields.length; k++) fields[k].value = v === 'all' ? '' : v;
          applyRange();
        }
        // 1095-C: the time range of the summary and the Trends chart, one choice for the page, kept in the
        // browser as the project is; the style above shows the elements of the chosen range alone.
        var rangeKey = 'ptk-range';
        function applyRange() {
          var v = null; try { v = localStorage.getItem(rangeKey); } catch (x) {}
          var sel = document.getElementById('rangesel');
          if (!v || (sel && !Array.prototype.some.call(sel.options, function (o) { return o.value === v; }))) v = '24h';
          if (sel) sel.value = v;
          document.body.setAttribute('data-range', v);
          // 1098-C: the wrappers other than the default come rendered hidden; show the chosen one
          // and hide the rest here, so the page needs only the script (not the style block) to be right.
          var rngs = document.querySelectorAll('.rng');
          for (var t = 0; t < rngs.length; t++) {
            if (rngs[t].getAttribute('data-range') === v) rngs[t].removeAttribute('hidden'); else rngs[t].setAttribute('hidden', '');
          }
        }
        // Bound on the document: this runs in the head, before the dropdown exists, and a refresh
        // replaces it (470-C).
        document.addEventListener('change', function (e) {
          if (!e.target || e.target.id !== 'projsel') return;
          try { localStorage.setItem(projKey, e.target.value); } catch (x) {}
          applyFilters();
        });
        document.addEventListener('change', function (e) {
          if (!e.target || e.target.id !== 'rangesel') return;
          try { localStorage.setItem(rangeKey, e.target.value); } catch (x) {}
          applyRange();
        });
        document.addEventListener('DOMContentLoaded', applyFilters);
        document.addEventListener('DOMContentLoaded', applyRange);
        </script>
        """;

    private static string Tone(PageTone tone) => tone switch
    {
        PageTone.Ok => "ok",
        PageTone.Warn => "warn",
        _ => "bad",
    };

    // 968-C: the card cell can run out of room and ellipsise, so a table may pass a tooltip that
    // carries the whole name.
    private static string CardLink(string name, string project, string title = "") =>
        $"<a href='/card?project={Url(project)}&name={Url(name)}'{(string.IsNullOrEmpty(title) ? "" : $" title='{Enc(title)}'")}>{Enc(name)}</a>";

    // 968-C: the whole card name, after what the cell leads with (the project here, the title on the
    // single page), so an ellipsised name is still readable in full.
    private static string CardTip(string name, string lead) =>
        lead.Length > 0 ? $"{lead} - {name}" : name;

    // 946-B: a link to a run's log by its file name, as the page's own queue and event links do (510-B);
    // the /log route takes the file name alone (PAGE-9).
    private static string LogLink(string log, string project, string tip) =>
        string.IsNullOrEmpty(log) ? ""
        : $"<a href='/log?project={Url(project)}&file={Url(System.IO.Path.GetFileName(log))}' title='{tip}'>log</a>";

    // The card's number and category letter, as the single page keys its rows (the pending mask's cardOf).
    private static string CardId(string name) =>
        System.Text.RegularExpressions.Regex.Match(name, @"^\d+-[A-Za-z]") is { Success: true } match ? match.Value : name;

    // A button that posts its fields as hidden inputs, so the pending mask (957-B) reads the project,
    // card, question and slot from the form as the single page's do. A project field left empty is the
    // dropdown's: the page script fills it with the picked project, or none for All (MP-H8).
    // 1004-C: cls is the single page's look for a toggle or a dangerous button ("pressed", "bad"), and
    // disabled a button that says why it cannot be pressed now (930-C's running check).
    private static string Post(string action, IReadOnlyList<(string Name, string Value)> fields, string label, string tip, bool controlsAllowed, string cls = "", bool disabled = false)
    {
        var text = Enc(label);
        if (!controlsAllowed)
        {
            return $"<span class='g' data-tip='{Enc(tip + " " + RemoteReason)}'><button class='{cls}' disabled>{text}</button></span>";
        }

        if (disabled)
        {
            return $"<span class='g' data-tip='{Enc(tip)}'><button class='{cls}' disabled>{text}</button></span>";
        }

        var inputs = new StringBuilder();
        foreach (var (name, value) in fields)
        {
            var bound = name == "project" && value.Length == 0 ? " class='projsel-in'" : "";
            inputs.Append("<input type='hidden'").Append(bound).Append(" name='").Append(Enc(name))
                  .Append("' value='").Append(Enc(value)).Append("'>");
        }

        return $"<form method='post' action='{action}' class='g' data-tip='{Enc(tip)}'>{inputs}"
             + $"<button class='{cls}' aria-label='{Enc(label + ". " + tip)}'>{text}</button></form>";
    }

    private static void Window(StringBuilder sb, string title, string body, string extra, string attrs = "", string headExtra = "")
    {
        sb.Append("<section class='win'")
          .Append(string.IsNullOrEmpty(attrs) ? "" : " " + attrs)
          .Append("><div class='bar'><b>").Append(Enc(title)).Append(headExtra).Append("</b>").Append(extra)
          .Append("</div><div class='inner'>").Append(body).Append("</div></section>");
    }

    private static string Enc(string text) => System.Net.WebUtility.HtmlEncode(text);

    private static string Url(string text) => Uri.EscapeDataString(text);
}
