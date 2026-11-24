using System.Globalization;
using System.Text;
using Scheduler.Core.Accounting;
using Scheduler.Core.Contracts;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Views;

/// <summary>
/// The Gantt view (1132-C, VW-18; gantt_plan.md section 4): on the left the timeline - the calendar across the
/// top, zoomed by the page's range, a now line, rows from <see cref="GanttRows"/> grouped by category or by plan,
/// folding like the Plans tree - and on the right the Details of what was clicked. One range and one grouping per
/// request (the address's <c>range</c> and <c>group</c>), so the page stays the size of one chart (VW-23).
/// </summary>
internal static class GanttView
{
    public const string CategoryGroup = "category";
    public const string PlanGroup = "plan";

    // The tick steps the calendar picks from, shortest first: the first that gives at most MaxTicks labels.
    private static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
        TimeSpan.FromDays(1), TimeSpan.FromDays(2),
    ];

    private const int MaxTicks = 12;

    /// <summary>The range key the address names, or the page's default when it names none or an unknown one.</summary>
    public static string RangeKey(string? key) => CardStats.RangeOf(key ?? "")?.Key ?? CardStats.DefaultRangeKey;

    /// <summary>The grouping the address names: plan, or category for anything else.</summary>
    public static GanttGroupBy GroupBy(string? group) =>
        string.Equals(group?.Trim(), PlanGroup, StringComparison.OrdinalIgnoreCase) ? GanttGroupBy.Plan : GanttGroupBy.Category;

    /// <summary>
    /// The shown window for a range: the range back from now, and an eighth of it ahead, so a running card's
    /// time-box end has room to be drawn.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) Window(string rangeKey, DateTimeOffset now)
    {
        var span = CardStats.RangeOf(RangeKey(rangeKey))!.Window;
        return (now - span, now + (span / 8));
    }

    /// <summary>Where a moment sits across the window, in percent of its width, held inside 0..100.</summary>
    public static double X(DateTimeOffset t, DateTimeOffset from, DateTimeOffset to)
    {
        var total = (to - from).TotalSeconds;
        var x = total <= 0 ? 0 : (t - from).TotalSeconds / total * 100;
        return Math.Clamp(x, 0, 100);
    }

    /// <summary>The Gantt window's body: the toolbar's choices are in <see cref="Bar"/>.</summary>
    public static string Html(IReadOnlyList<ProjectViews> views, IReadOnlyList<PageSnapshot> pages, IReadOnlyList<string> names, string rangeKey, GanttGroupBy groupBy, DateTimeOffset now)
    {
        var (from, to) = Window(rangeKey, now);
        var ordered = views.OrderBy(v => v.Project, StringComparer.OrdinalIgnoreCase).ToList();
        var aheads = ordered.Select(v => Ahead.Of(v, PageOf(pages, v.Project), now)).ToList();
        var sb = new StringBuilder("<div class='gantt'>").Append(HatchDefs);
        AppendAxis(sb, from, to, now, aheads.Select(a => a.LastEnd).DefaultIfEmpty(null).Max());
        if (views.Count == 0)
        {
            sb.Append("<p class='mute'>Nothing to draw yet: the scheduler has not read any project's cards.</p>");
        }

        foreach (var (view, ahead) in ordered.Zip(aheads))
        {
            var categories = CategoriesOf(pages, view.Project);
            var chip = names.Count < 2 ? "" : ProjectChip.Html(view.Project, names) + " ";
            var root = GanttRows.Of(view.Snapshot, groupBy, from, to, now, ahead.Listed);
            sb.Append("<div class='gproject' data-project='").Append(Enc(view.Project)).Append("'>");
            sb.Append("<div class='grow ghead' data-gd='").Append(Enc(GroupKey(view.Project, groupBy, root.Key))).Append("'><span class='glabel'>")
              .Append(chip).Append("<b>").Append(Enc(view.Project)).Append("</b>").Append(Count(root.Done, root.Total))
              .Append(view.Note.Length == 0 ? "" : " <span class='mute'>" + Enc(view.Note) + "</span>")
              .Append("</span>").Append(Track(root.Summary is { } s ? [s] : [], from, to, now)).Append("</div>");
            if (root.Children.Count == 0)
            {
                sb.Append("<div class='mute gempty'>No runs in this range and nothing running.</div>");
            }

            foreach (var child in root.Children)
            {
                AppendRow(sb, view, ahead, child, groupBy, categories, from, to, now);
            }

            sb.Append("</div>");
        }

        return sb.Append("</div>").ToString();
    }

    /// <summary>The Gantt window's title-bar choices: the range and the Group by dropdown.</summary>
    public static string Bar(string rangeKey, GanttGroupBy groupBy)
    {
        var range = RangeKey(rangeKey);
        var sb = new StringBuilder(" <select id='rangesel' title='The time range the Gantt shows' aria-label='Time range' style='font: inherit'>");
        foreach (var r in CardStats.Ranges)
        {
            sb.Append("<option value='").Append(r.Key).Append('\'').Append(r.Key == range ? " selected" : "").Append('>').Append(Enc(r.Name)).Append("</option>");
        }

        sb.Append("</select> <label class='mute'>Group by <select id='ganttgroup' aria-label='Group by' style='font: inherit'>");
        foreach (var (value, label) in new[] { (CategoryGroup, "Category"), (PlanGroup, "Plan") })
        {
            var selected = (value == PlanGroup) == (groupBy == GanttGroupBy.Plan);
            sb.Append("<option value='").Append(value).Append('\'').Append(selected ? " selected" : "").Append('>').Append(label).Append("</option>");
        }

        return sb.Append("</select></label>").ToString();
    }

    /// <summary>
    /// The Details window's body: the shown box (what the page script copies the clicked row's block into) and one
    /// hidden block per row the Gantt shows, keyed as the row's <c>data-gd</c>.
    /// </summary>
    public static string DetailsHtml(IReadOnlyList<ProjectViews> views, IReadOnlyList<PageSnapshot> pages, IReadOnlyList<string> names, string rangeKey, GanttGroupBy groupBy, DateTimeOffset now)
    {
        var (from, to) = Window(rangeKey, now);
        var sb = new StringBuilder("<div id='gantt-detail'><p class='mute'>Click a card or a group in the Gantt to see its details here.</p></div><div class='gdets' hidden>");
        foreach (var view in views.OrderBy(v => v.Project, StringComparer.OrdinalIgnoreCase))
        {
            var page = pages.FirstOrDefault(p => string.Equals(p.Project, view.Project, StringComparison.OrdinalIgnoreCase));
            var categories = page?.Categories ?? [];
            var ahead = Ahead.Of(view, page, now);
            var root = GanttRows.Of(view.Snapshot, groupBy, from, to, now, ahead.Listed);
            var members = Members(view, groupBy);
            AppendDetails(sb, view, page, ahead, root, groupBy, categories, names, members);
        }

        return sb.Append("</div>").ToString();
    }

    /// <summary>The key a row's Details block is found by: project, grouping and the row's own key.</summary>
    public static string GroupKey(string project, GanttGroupBy groupBy, string rowKey) =>
        project + "|" + (groupBy == GanttGroupBy.Plan ? PlanGroup : CategoryGroup) + "|" + rowKey;

    private static void AppendAxis(StringBuilder sb, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, DateTimeOffset? lastEnd)
    {
        var step = Steps.FirstOrDefault(s => (to - from).Ticks / s.Ticks <= MaxTicks);
        if (step == default)
        {
            step = Steps[^1];
        }

        sb.Append("<div class='grow gaxis'><span class='glabel'></span><div class='gtrack gcal'>");
        var local = from.ToOffset(now.Offset);
        var first = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, now.Offset);
        while (first < local)
        {
            first += step;
        }

        for (var t = first; t <= to; t += step)
        {
            var label = step >= TimeSpan.FromDays(1)
                ? t.ToString("ddd d MMM", CultureInfo.InvariantCulture)
                : t.Hour == 0 && t.Minute == 0 ? t.ToString("ddd HH:mm", CultureInfo.InvariantCulture) : t.ToString("HH:mm", CultureInfo.InvariantCulture);
            sb.Append("<span class='gtick' style='left: ").Append(N(X(t, from, to))).Append("%'>").Append(label).Append("</span>");
        }

        sb.Append("<span class='gnowtag' style='left: ").Append(N(X(now, from, to))).Append("%' title='now, ")
          .Append(now.ToString("HH:mm", CultureInfo.InvariantCulture)).Append("'>now</span>");
        if (lastEnd is { } last && last > to)
        {
            // VW-19: the range stays as chosen; the schedule ahead goes on past its right edge.
            sb.Append("<span class='gmore' style='position: absolute; right: 0; top: 0; font-size: 0.8em; white-space: nowrap; color: var(--muted); background: var(--panel, inherit)' title='the last estimated finish is ")
              .Append(Stamp(last.ToOffset(now.Offset))).Append("'>more ahead -&gt; ").Append(Stamp(last.ToOffset(now.Offset))).Append("</span>");
        }

        sb.Append("</div></div>");
    }

    private static void AppendRow(StringBuilder sb, ProjectViews view, Ahead ahead, GanttRow row, GanttGroupBy groupBy, IReadOnlyList<PageCategory> categories, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        var key = GroupKey(view.Project, groupBy, row.Key);
        if (row.Kind == GanttRowKind.Card && row.Card is { } card)
        {
            var (svg, labels) = AheadMarks(ahead, card, row.Bars, from, to, now);
            sb.Append("<div class='grow gcard' data-gd='").Append(Enc(key)).Append("'><span class='glabel'>")
              .Append(CategoryChip.Html(categories, card.Category)).Append(' ').Append(CardLink(view.Project, card))
              .Append(PlanTag(view, card)).Append("</span>").Append(Track(row.Bars, from, to, now, "", svg, labels)).Append("</div>");
            return;
        }

        // A group folds like a Plans node (1123-C remembers it under data-key); folded, its summary span shows.
        sb.Append("<details class='ggroup' open data-key='").Append(Enc("g|" + key)).Append("'><summary class='grow' data-gd='").Append(Enc(key)).Append("'><span class='glabel'>")
          .Append(row.Kind == GanttRowKind.Category ? CategoryChip.Html(categories, row.Label) + " " : "")
          .Append(Enc(row.Kind == GanttRowKind.Category ? CategoryName(categories, row.Label) : row.Label)).Append(Count(row.Done, row.Total))
          .Append("</span>").Append(Track(row.Summary is { } s ? [s] : [], from, to, now, "gsum")).Append("</summary>");
        foreach (var child in row.Children)
        {
            AppendRow(sb, view, ahead, child, groupBy, categories, from, to, now);
        }

        sb.Append("</details>");
    }

    // The schedule ahead on one card row (VW-19): its estimated bar or waiting stub, and an arrow from each of its
    // dependencies' last bar (or estimate) to its own first bar, or a stub with the dependency's id when that is
    // outside the window. SVG marks go inside the row's SVG; the text labels are HTML over the track.
    private static (string Svg, string Labels) AheadMarks(Ahead ahead, ViewCard card, IReadOnlyList<GanttBar> bars, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        var svg = new StringBuilder();
        var labels = new StringBuilder();
        var est = ahead.Estimates.GetValueOrDefault(card.Id);
        if (est is { Waiting: true })
        {
            var nx = X(now, from, to);
            var text = WaitingText(est, card);
            svg.Append("<rect class='gest gwait' x='").Append(N(Math.Min(nx, 98.5))).Append("' y='2.5' width='1.5' height='5' fill='url(#ghatch)' stroke='var(--warn)' stroke-dasharray='2 2' vector-effect='non-scaling-stroke'>")
               .Append("<title>").Append(Enc(text)).Append("</title></rect>");
            Label(labels, "gwaitlab", Math.Min(nx + 1.7, 100), text, "var(--warn)");
        }
        else if (est is { Start: { } start, End: { } end } && end > from && start < to)
        {
            var x = X(start, from, to);
            var w = Math.Max(X(end, from, to) - x, 0.15);
            var model = ModelOf(est, card);
            svg.Append("<rect class='gest' x='").Append(N(x)).Append("' y='1.5' width='").Append(N(w)).Append("' height='7' fill='url(#ghatch)' stroke='var(--muted)' stroke-dasharray='3 2' vector-effect='non-scaling-stroke' data-card='")
               .Append(Enc(card.Id)).Append("'><title>").Append(Enc(EstimateTip(est, now))).Append("</title></rect>");
            Label(labels, "gestlab", x, "est." + (model.Length == 0 ? "" : " " + model), "var(--muted)");
        }

        // The card's first bar: its first run in the window, else its estimate.
        var first = bars.Select(b => (DateTimeOffset?)b.Start).Append(est?.Start).Where(t => t is not null).DefaultIfEmpty(null).Min();
        if (first is not { } sxAt || sxAt < from || sxAt > to)
        {
            return (svg.ToString(), labels.ToString());
        }

        var sx = X(sxAt, from, to);
        foreach (var dep in card.DependsOn)
        {
            var depEnd = ahead.Ends.GetValueOrDefault(dep);
            if (depEnd is { } de && de >= from && de <= to)
            {
                if (de > sxAt)
                {
                    continue;   // ran again after it: no arrow back in time.
                }

                var dx = X(de, from, to);
                svg.Append("<g class='garrow' data-from='").Append(Enc(dep)).Append("' data-to='").Append(Enc(card.Id)).Append("'><title>after ").Append(Enc(dep)).Append("</title>")
                   .Append("<path d='M").Append(N(dx)).Append(",0 L").Append(N(dx)).Append(",5 L").Append(N(sx)).Append(",5' fill='none' stroke='var(--muted)' stroke-width='1' vector-effect='non-scaling-stroke'/>")
                   .Append(Head(sx)).Append("</g>");
            }
            else
            {
                var bx = Math.Max(sx - 2, 0);
                svg.Append("<g class='garrow gstub' data-from='").Append(Enc(dep)).Append("' data-to='").Append(Enc(card.Id)).Append("'><title>after ").Append(Enc(dep)).Append(", outside the range</title>")
                   .Append("<line x1='").Append(N(bx)).Append("' x2='").Append(N(sx)).Append("' y1='5' y2='5' stroke='var(--muted)' stroke-width='1' vector-effect='non-scaling-stroke'/>")
                   .Append(Head(sx)).Append("</g>");
                labels.Append("<span class='gdeplab' style='position: absolute; top: 0; left: ").Append(N(bx)).Append("%; transform: translateX(-100%); font-size: 0.7em; white-space: nowrap; color: var(--muted); pointer-events: none'>")
                      .Append(Enc(dep)).Append("</span>");
            }
        }

        return (svg.ToString(), labels.ToString());

        static string Head(double x) =>
            "<polygon points='" + N(Math.Max(x - 0.6, 0)) + ",3.2 " + N(x) + ",5 " + N(Math.Max(x - 0.6, 0)) + ",6.8' fill='var(--muted)'/>";
    }

    private static void Label(StringBuilder sb, string cls, double x, string text, string colour) =>
        sb.Append("<span class='").Append(cls).Append("' style='position: absolute; top: 0; left: ").Append(N(x)).Append("%; font-size: 0.75em; white-space: nowrap; color: ")
          .Append(colour).Append("; pointer-events: none'>").Append(Enc(text)).Append("</span>");

    // What a waiting card waits on: the cards that hold it, else the planner when its status says so, else you.
    private static string WaitingText(EstimatedBar est, ViewCard card) =>
        est.WaitedFor.Count > 0 ? "waiting on " + string.Join(", ", est.WaitedFor)
        : System.Text.RegularExpressions.Regex.IsMatch(card.Status, @"^waiting\b.*\bplanner", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "waiting on the planner"
        : "waiting on you";

    // The model an estimate is for: the typical's model, else the card's Worker line.
    private static string ModelOf(EstimatedBar est, ViewCard card) =>
        est.Basis.StartsWith("typical ", StringComparison.Ordinal) ? est.Basis["typical ".Length..] : card.Worker;

    private static string EstimateTip(EstimatedBar est, DateTimeOffset now)
    {
        var minutes = (int)Math.Round((est.End!.Value - est.Start!.Value).TotalMinutes);
        return "estimated " + Stamp(est.Start.Value.ToOffset(now.Offset)) + "-" + Stamp(est.End.Value.ToOffset(now.Offset)) + ", " + minutes + " min (" + est.Basis + ")"
            + (est.WaitedFor.Count == 0 ? "" : "; waited for " + string.Join(", ", est.WaitedFor));
    }

    // One row's strip: an SVG across the window, in percent, with its bars, a running bar's time-box end and the now line.
    private static string Track(IEnumerable<GanttBar> bars, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, string extra = "", string svgExtra = "", string labels = "")
    {
        var sb = new StringBuilder("<div class='gtrack").Append(extra.Length == 0 ? "" : " " + extra).Append("'><svg class='gsvg' viewBox='0 0 100 10' preserveAspectRatio='none'>");
        foreach (var bar in bars)
        {
            var x = X(bar.Start, from, to);
            var w = Math.Max(X(bar.End, from, to) - x, 0.15);
            sb.Append("<rect class='gbar gb-").Append(bar.ColorKey).Append("' x='").Append(N(x)).Append("' y='").Append(bar.Summary ? "3.5" : "1.5")
              .Append("' width='").Append(N(w)).Append("' height='").Append(bar.Summary ? "3" : "7").Append("' fill='").Append(Colour(bar.ColorKey)).Append("'>")
              .Append("<title>").Append(Enc(Tip(bar, now))).Append("</title></rect>");
            if (bar.Running && bar.TimeBoxEnd is { } end && end > bar.End)
            {
                var bx = X(bar.End, from, to);
                var ex = X(end, from, to);
                sb.Append("<rect class='gtimebox' x='").Append(N(bx)).Append("' y='1.5' width='").Append(N(Math.Max(ex - bx, 0))).Append("' height='7' fill='none' stroke='var(--accent)' stroke-dasharray='3 2' vector-effect='non-scaling-stroke'>")
                  .Append("<title>time box ends ").Append(end.ToOffset(now.Offset).ToString("HH:mm", CultureInfo.InvariantCulture)).Append("</title></rect>");
                if (end <= to)
                {
                    sb.Append("<line class='gtbend' x1='").Append(N(ex)).Append("' x2='").Append(N(ex)).Append("' y1='0' y2='10' stroke='var(--accent)' stroke-width='2' vector-effect='non-scaling-stroke'/>");
                }
            }
        }

        sb.Append(svgExtra);
        var nx = N(X(now, from, to));
        sb.Append("<line class='gnow' x1='").Append(nx).Append("' x2='").Append(nx).Append("' y1='0' y2='10' stroke='var(--bad)' stroke-width='1' vector-effect='non-scaling-stroke'/>");
        return sb.Append("</svg>").Append(labels).Append("</div>").ToString();
    }

    private static string Tip(GanttBar bar, DateTimeOffset now)
    {
        var start = bar.Start.ToOffset(now.Offset).ToString("HH:mm", CultureInfo.InvariantCulture);
        var end = bar.End.ToOffset(now.Offset).ToString("HH:mm", CultureInfo.InvariantCulture);
        var minutes = Math.Max(0, (int)Math.Round((bar.End - bar.Start).TotalMinutes));
        if (bar.Summary)
        {
            return start + "-" + end;
        }

        var head = bar.Running ? "running since " + start : bar.ColorKey + ", " + start + "-" + end;
        return head + ", " + minutes + " min" + (bar.Model.Length == 0 ? "" : ", " + bar.Model);
    }

    // The colours of TrendChart's palette: green done, accent review, amber waiting on someone, red stopped or failed.
    private static string Colour(string key) => key switch
    {
        "done" => "var(--ok)",
        "review" => "var(--accent)",
        "needs-human" or "waiting" => "var(--warn)",
        "running" => "var(--text)",
        "summary" => "var(--muted)",
        _ => "var(--bad)",
    };

    private static void AppendDetails(StringBuilder sb, ProjectViews view, PageSnapshot? page, Ahead ahead, GanttRow row, GanttGroupBy groupBy, IReadOnlyList<PageCategory> categories, IReadOnlyList<string> names, IReadOnlyDictionary<string, List<ViewCard>> members)
    {
        var key = GroupKey(view.Project, groupBy, row.Key);
        sb.Append("<div class='gdet' data-gdet='").Append(Enc(key)).Append("'>");
        if (row.Kind == GanttRowKind.Card && row.Card is { } card)
        {
            CardDetails(sb, view, page, ahead, card, categories, names);
        }
        else
        {
            GroupDetails(sb, view, ahead, row, categories, members.GetValueOrDefault(row.Key) ?? []);
        }

        sb.Append("</div>");
        foreach (var child in row.Children)
        {
            AppendDetails(sb, view, page, ahead, child, groupBy, categories, names, members);
        }
    }

    private static void CardDetails(StringBuilder sb, ProjectViews view, PageSnapshot? page, Ahead ahead, ViewCard card, IReadOnlyList<PageCategory> categories, IReadOnlyList<string> names)
    {
        var chip = names.Count < 2 ? "" : ProjectChip.Html(view.Project, names) + " ";
        sb.Append("<div class='gdtitle'>").Append(chip).Append(CardLink(view.Project, card)).Append("</div><table class='gdtab'>");
        Line(sb, "Category", CategoryChip.Html(categories, card.Category) + " " + Enc(CategoryName(categories, card.Category)));
        Line(sb, "Folder", Enc(card.Folder.ToString().ToLowerInvariant()) + (card.Status.Length == 0 ? "" : " &nbsp;<span class='mute'>" + Enc(card.Status) + "</span>"));
        var plan = PlanPaths.Resolve(card.PlanPath, new HashSet<string>(view.OnMain, StringComparer.Ordinal));
        if (plan.Kind != PlanKind.NoPlan)
        {
            var steps = PlanSteps.Of(view.Snapshot.PlanTexts.GetValueOrDefault(plan.Path));
            var step = PlanStepMatch.Of(card.PlanStep, steps);
            var html = "<a href='" + PlanHref(view.Project, plan.Path, "") + "'>" + Enc(plan.Path) + "</a>";
            if (step is not null)
            {
                html += " &nbsp;<a href='" + PlanHref(view.Project, plan.Path, step.Anchor) + "'>" + Enc(step.Title.StartsWith(step.Label, StringComparison.Ordinal) ? step.Title : step.Label + ". " + step.Title) + "</a>";
            }
            else if (card.PlanStep.Length > 0)
            {
                html += " &nbsp;" + Enc(card.PlanStep);
            }

            Line(sb, "Plan", html);
        }
        else
        {
            Line(sb, "Plan", "<span class='mute'>none</span>");
        }

        var runs = view.Snapshot.Runs.Where(r => string.Equals(r.CardId, card.Id, StringComparison.Ordinal)).OrderBy(r => r.Start).ToList();
        var last = runs.LastOrDefault();
        var worker = card.Worker.Length > 0 ? card.Worker : last?.WorkerType ?? "";
        Line(sb, "Worker", Enc(worker.Length == 0 ? "any" : worker) + (last is null || last.Model.Length == 0 ? "" : " &nbsp;<span class='mute'>" + Enc(last.Model) + "</span>"));
        Line(sb, "Depends on", card.DependsOn.Count == 0 ? "<span class='mute'>none</span>" : string.Join(", ", card.DependsOn.Select(d => DependsLink(view, d))));
        if (card.TimeBoxMinutes is { } box)
        {
            Line(sb, "Time box", box + " min");
        }

        if (ahead.Estimates.GetValueOrDefault(card.Id) is { } est)
        {
            var offset = ahead.Offset;
            Line(sb, "Estimate", est is { Start: { } s, End: { } e }
                ? "estimated start " + Stamp(s.ToOffset(offset)) + " end " + Stamp(e.ToOffset(offset)) + " <span class='mute'>(" + Enc(est.Basis) + ")</span>"
                : "<span class='mute'>" + Enc(WaitingText(est, card)) + "</span>");
            if (!est.Waiting && est.WaitedFor.Count > 0)
            {
                Line(sb, "Waits for", string.Join(", ", est.WaitedFor.Select(d => DependsLink(view, d))));
            }
        }

        sb.Append("</table>");
        sb.Append("<div class='gdsub'>Runs (").Append(runs.Count).Append(")</div>");
        if (runs.Count > 0)
        {
            sb.Append("<table class='gdruns'><tr><th>Start</th><th>End</th><th>Result</th><th>Min</th><th>Model</th></tr>");
            foreach (var run in runs)
            {
                var minutes = (int)Math.Round(((run.End ?? page?.Built ?? run.Start) - run.Start).TotalMinutes);
                sb.Append("<tr><td class='nw'>").Append(Stamp(run.Start)).Append("</td><td class='nw'>").Append(run.End is { } e ? Stamp(e) : "running")
                  .Append("</td><td>").Append(Enc(run.Running ? "running" : run.Result.Length == 0 ? "no result" : run.Result))
                  .Append("</td><td>").Append(Math.Max(0, minutes)).Append("</td><td>").Append(Enc(run.Model)).Append("</td></tr>");
            }

            sb.Append("</table>");
        }

        var questions = (page?.Waiting ?? [])
            .Where(w => w.Questions.Count > 0 && SameCard(w.Name, card.Id))
            .SelectMany(w => w.Questions)
            .ToList();
        sb.Append("<div class='gdsub'>Open questions</div>");
        if (questions.Count == 0)
        {
            sb.Append("<div class='mute'>none</div>");
        }
        else
        {
            sb.Append("<ul class='gdq'>");
            foreach (var q in questions)
            {
                sb.Append("<li>Q").Append(q.Number).Append(": ").Append(Enc(q.Text)).Append("</li>");
            }

            sb.Append("</ul>");
        }
    }

    private static void GroupDetails(StringBuilder sb, ProjectViews view, Ahead ahead, GanttRow row, IReadOnlyList<PageCategory> categories, IReadOnlyList<ViewCard> cards)
    {
        var title = row.Kind switch
        {
            GanttRowKind.Category => CategoryChip.Html(categories, row.Label) + " " + Enc(CategoryName(categories, row.Label)),
            GanttRowKind.Project => "<b>" + Enc(row.Label) + "</b>",
            _ => Enc(row.Label),
        };
        var done = cards.Count(CardMark.IsComplete);
        var total = cards.Count(c => !CardMark.IsCancelled(c));
        sb.Append("<div class='gdtitle'>").Append(title).Append("</div>")
          .Append("<div class='gddone'>done ").Append(done).Append(" of ").Append(total).Append("</div>");
        var finish = cards.Select(c => ahead.Estimates.GetValueOrDefault(c.Id)?.End).Where(e => e is not null).DefaultIfEmpty(null).Max();
        if (finish is { } f)
        {
            // A group finishes when the latest of its cards' estimates does.
            sb.Append("<div class='gdest'>estimated finish ").Append(Stamp(f.ToOffset(ahead.Offset))).Append("</div>");
        }
        var running = cards.Where(c => c.Folder == ViewFolder.Active || view.Running.Contains(c.Id)).ToList();
        var queued = cards.Where(c => c.Folder == ViewFolder.Queue).ToList();
        foreach (var (label, list) in new[] { ("Running", running), ("Queued", queued) })
        {
            sb.Append("<div class='gdsub'>").Append(label).Append(" (").Append(list.Count).Append(")</div>");
            if (list.Count == 0)
            {
                sb.Append("<div class='mute'>none</div>");
                continue;
            }

            sb.Append("<ul class='gdcards'>");
            foreach (var card in list)
            {
                sb.Append("<li>").Append(CategoryChip.Html(categories, card.Category)).Append(' ').Append(CardLink(view.Project, card)).Append("</li>");
            }

            sb.Append("</ul>");
        }
    }

    // Every card under each group row's key, whatever the window, mirroring GanttRows' keys: a group's Details
    // counts all its cards, not only those with a bar in the range.
    private static Dictionary<string, List<ViewCard>> Members(ProjectViews view, GanttGroupBy groupBy)
    {
        var snapshot = view.Snapshot;
        var result = new Dictionary<string, List<ViewCard>>(StringComparer.Ordinal)
        {
            ["project:" + snapshot.Project] = [.. snapshot.Cards],
        };
        if (groupBy == GanttGroupBy.Category)
        {
            foreach (var group in snapshot.Cards.GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase))
            {
                result["category:" + group.Key] = [.. group];
            }

            return result;
        }

        var onMain = new HashSet<string>(snapshot.Plans.Select(p => p.Path), StringComparer.Ordinal);
        foreach (var plan in PlanTree.Of(snapshot.Cards, onMain, snapshot.PlanTexts))
        {
            if (plan.Plan.Kind == PlanKind.NoPlan)
            {
                var own = plan.Other?.Cards ?? [];
                result["bugs"] = [.. own.Where(IsBug)];
                result["requests"] = [.. own.Where(c => !IsBug(c))];
                continue;
            }

            var key = "plan:" + plan.Plan.Path;
            var all = new List<ViewCard>();
            foreach (var step in plan.Steps)
            {
                all.AddRange(StepMembers(result, key, step));
            }

            if (plan.Other is { } other)
            {
                result[key + "/other"] = [.. other.Cards];
                all.AddRange(other.Cards);
            }

            result[key] = all;
        }

        return result;
    }

    private static List<ViewCard> StepMembers(Dictionary<string, List<ViewCard>> result, string parentKey, PlanStepNode node)
    {
        var key = parentKey + "/" + node.Step!.Label;
        var all = new List<ViewCard>(node.Cards);
        foreach (var sub in node.SubSteps)
        {
            all.AddRange(StepMembers(result, key, sub));
        }

        result[key] = all;
        return all;
    }

    private static bool IsBug(ViewCard card) => string.Equals(card.CardType, "bug", StringComparison.OrdinalIgnoreCase);

    // A waiting item names its card by id or by file name.
    private static bool SameCard(string name, string id) =>
        string.Equals(name, id, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase);

    private static string CardLink(string project, ViewCard card)
    {
        // VW-5: a review card opens its review panel, any other its text.
        var path = card.Folder == ViewFolder.Review ? "/review" : "/card";
        return "<a href='" + path + "?project=" + Url(project) + "&id=" + Url(card.Id) + "'>" + Enc(card.Id) + (card.Title.Length == 0 ? "" : " " + Enc(card.Title)) + "</a>";
    }

    private static string PlanTag(ProjectViews view, ViewCard card)
    {
        var plan = PlanPaths.Resolve(card.PlanPath, new HashSet<string>(view.OnMain, StringComparer.Ordinal));
        if (plan.Kind == PlanKind.NoPlan || plan.Path.Length == 0)
        {
            return "";
        }

        var name = Path.GetFileNameWithoutExtension(plan.Path);
        if (name.EndsWith("_plan", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^5];
        }

        return " <a class='gplan' href='" + PlanHref(view.Project, plan.Path, "") + "' title='" + Enc(plan.Path) + "'>" + Enc(name) + "</a>";
    }

    private static string DependsLink(ProjectViews view, string id)
    {
        var card = view.Snapshot.Cards.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        return card is null ? Enc(id) : CardLink(view.Project, card with { Title = "" });
    }

    // The hatching every estimated bar is filled with, defined once for the whole Gantt.
    private const string HatchDefs =
        "<svg width='0' height='0' style='position: absolute' aria-hidden='true'><defs><pattern id='ghatch' patternUnits='userSpaceOnUse' width='1.2' height='10'>"
        + "<path d='M0,10 L1.2,0' stroke='var(--muted)' stroke-width='1' vector-effect='non-scaling-stroke'/></pattern></defs></svg>";

    // The maxMinutes an estimate falls back to: the config's default (TIME-1), as the page is not given the project's own.
    private const int DefaultMaxMinutes = 240;

    private static PageSnapshot? PageOf(IReadOnlyList<PageSnapshot> pages, string project) =>
        pages.FirstOrDefault(p => string.Equals(p.Project, project, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// One project's schedule ahead (VW-19): <see cref="GanttEstimate"/>'s bars by card, every card's last finish (its
    /// estimate's end, else its last run's end, now while it runs) for the dependency arrows, and the cards to list.
    /// </summary>
    private sealed record Ahead(IReadOnlyDictionary<string, EstimatedBar> Estimates, IReadOnlyDictionary<string, DateTimeOffset> Ends, IReadOnlySet<string> Listed, DateTimeOffset? LastEnd, TimeSpan Offset)
    {
        public static Ahead Of(ProjectViews view, PageSnapshot? page, DateTimeOffset now)
        {
            var estimates = new Dictionary<string, EstimatedBar>(StringComparer.OrdinalIgnoreCase);
            foreach (var bar in GanttEstimate.Of(view.Snapshot, now, DefaultMaxMinutes, page?.SharedAddOnly))
            {
                estimates.TryAdd(bar.CardId, bar);
            }

            var ends = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
            foreach (var run in view.Snapshot.Runs)
            {
                var end = run.End ?? now;
                if (!ends.TryGetValue(run.CardId, out var had) || end > had)
                {
                    ends[run.CardId] = end;
                }
            }

            foreach (var bar in estimates.Values)
            {
                if (bar.End is { } e)
                {
                    ends[bar.CardId] = e;
                }
            }

            var last = estimates.Values.Select(b => b.End).Where(e => e is not null).DefaultIfEmpty(null).Max();
            return new Ahead(estimates, ends, new HashSet<string>(estimates.Keys, StringComparer.Ordinal), last, now.Offset);
        }
    }

    private static IReadOnlyList<PageCategory> CategoriesOf(IReadOnlyList<PageSnapshot> pages, string project) =>
        pages.FirstOrDefault(p => string.Equals(p.Project, project, StringComparison.OrdinalIgnoreCase))?.Categories ?? [];

    private static string CategoryName(IReadOnlyList<PageCategory> categories, string letter)
    {
        var name = categories.FirstOrDefault(c => string.Equals(c.Letter, letter, StringComparison.OrdinalIgnoreCase))?.Name ?? "";
        return name.Length == 0 || string.Equals(name, letter, StringComparison.OrdinalIgnoreCase) ? letter : letter + " " + name;
    }

    private static void Line(StringBuilder sb, string label, string html) =>
        sb.Append("<tr><th>").Append(Enc(label)).Append("</th><td>").Append(html).Append("</td></tr>");

    private static string Stamp(DateTimeOffset t) => t.ToString("ddd HH:mm", CultureInfo.InvariantCulture);

    private static string PlanHref(string project, string path, string anchor) =>
        "/plan?project=" + Url(project) + "&path=" + Url(path) + (anchor.Length == 0 ? "" : "#" + Url(anchor));

    private static string Count(int done, int total) =>
        total == 0 ? "" : " &nbsp;<span class='mute'>" + done + "/" + total + "</span>";

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Enc(string text) => System.Net.WebUtility.HtmlEncode(text);

    private static string Url(string text) => Uri.EscapeDataString(text);
}
