// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Views;

/// <summary>
/// The Plans view's window (1114-C, views.md second pass): per project each plan as a checklist tree. Every node
/// starts with a mark (green check, empty box, red X); a plan is a link to its document with its steps under it,
/// each step a link to its heading and a node opening to its cards. "No plan" last, its cards showing the path they name.
/// Built only when the view is asked for (VW-23).
/// </summary>
internal static class PlansView
{
    /// <summary>1212-C: the title-bar buttons of a Plans window: Expand all and Collapse all, each acting on the window named by <paramref name="windowId"/> (page script, no server round trip).</summary>
    public static string Bar(string windowId)
    {
        var id = Enc(windowId);
        return " <span class='plans-all'><button type='button' class='plans-all-btn' data-plans-all='expand' data-window='" + id + "' title='Open every plan, step and group in this window'>Expand all</button>"
             + " <button type='button' class='plans-all-btn' data-plans-all='collapse' data-window='" + id + "' title='Close every plan, step and group in this window'>Collapse all</button>"
             + " <button type='button' class='plans-all-btn' data-plans-hide-done='1' data-window='" + id + "' title='Hide or show the finished child plans, steps and sub-steps in this window (counts do not change)'>Show completed</button></span>";
    }

    /// <summary>The window's HTML for every project's views data.</summary>
    /// <param name="views">One per project, from the pool snapshot.</param>
    /// <param name="pages">The projects' pages, for their category chips.</param>
    /// <param name="names">Every project's name, for the project chips.</param>
    public static string Html(IReadOnlyList<ProjectViews> views, IReadOnlyList<PageSnapshot> pages, IReadOnlyList<string> names)
    {
        var sb = new StringBuilder(MarkStyle);
        if (views.Count == 0)
        {
            sb.Append("<p class='mute'>No plans yet: the scheduler has not read any project's cards.</p>");
        }

        foreach (var view in views.OrderBy(v => v.Project, StringComparer.OrdinalIgnoreCase))
        {
            var categories = pages.FirstOrDefault(p => string.Equals(p.Project, view.Project, StringComparison.OrdinalIgnoreCase))?.Categories ?? [];
            AppendProject(sb, view, categories, names);
        }

        return sb.ToString();
    }

    private static void AppendProject(StringBuilder sb, ProjectViews view, IReadOnlyList<PageCategory> categories, IReadOnlyList<string> names)
    {
        var chip = names.Count < 2 ? "" : ProjectChip.Html(view.Project, names);
        sb.Append("<div class='plans-project' data-project='").Append(Enc(view.Project)).Append("'>");
        if (view.Note.Length > 0)
        {
            sb.Append("<div class='mute'>").Append(chip).Append(' ').Append(Enc(view.Project)).Append(": ").Append(Enc(view.Note)).Append("</div>");
        }

        // VW-11, VW-27: the cached forest of the snapshot; child plans are drawn inside their parent's step.
        var forest = PlanTree.Of(view.Snapshot);
        var tree = forest.Top;
        if (tree.Any(n => n.Plan.Kind != PlanKind.NoPlan) && forest.Roots != 1)
        {
            sb.Append("<div class='mute plans-roots'>").Append(forest.Roots).Append(" root plans</div>");
        }

        // 1256-C (VW-34): every top-level plan is drawn, complete or not; the Completed plans window of 1127-C is gone.
        // 1128-C: cards with no plan are not a plan node: they are the Bugs, Requests and Plan not found groups.
        var shown = tree.Where(n => n.Plan.Kind != PlanKind.NoPlan).ToList();

        foreach (var node in shown)
        {
            AppendPlan(sb, view, node, categories, chip);
        }

        var groups = PlanGroups.Of(view.Snapshot.Cards, new HashSet<string>(view.OnMain, StringComparer.Ordinal), view.Snapshot.Runs);
        AppendGroup(sb, view, groups.Bugs, categories, chip);
        AppendGroup(sb, view, groups.Requests, categories, chip);
        AppendNotFound(sb, view, groups.NotFound, categories, chip);

        sb.Append("</div>");
    }

    // 1128-C: Bugs / Requests: category, then month (newest first), then the cards. Marks are automatic only (not clickable).
    private static void AppendGroup(StringBuilder sb, ProjectViews view, CardGroupNode? group, IReadOnlyList<PageCategory> categories, string chip)
    {
        if (group is null)
        {
            return;
        }

        var key = "g|" + view.Project + "|" + group.Name;
        sb.Append("<details class='plan' data-project='").Append(Enc(view.Project)).Append("' data-key='").Append(Enc(key)).Append("'").Append(group.Checked ? "" : " open").Append("><summary>")
          .Append(MarkHtml(group.Mark)).Append(chip).Append(" <b>").Append(Enc(group.Name)).Append("</b>").Append(Count(group.Done, group.Total)).Append("</summary>");
        foreach (var cat in group.Categories)
        {
            var name = categories.FirstOrDefault(c => string.Equals(c.Letter, cat.Category, StringComparison.OrdinalIgnoreCase))?.Name ?? "";
            sb.Append("<details class='plan-step' data-key='").Append(Enc(key + "|" + cat.Category)).Append("'").Append(cat.Checked ? "" : " open").Append("><summary>")
              .Append(MarkHtml(cat.Mark)).Append(CategoryChip.Html(categories, cat.Category)).Append(' ').Append(Enc(name.Length > 0 ? name : cat.Category)).Append(Count(cat.Done, cat.Total)).Append("</summary>");
            foreach (var month in cat.Months)
            {
                sb.Append("<details class='plan-step' data-key='").Append(Enc(key + "|" + cat.Category + "|" + month.Month)).Append("'").Append(month.Checked ? "" : " open").Append("><summary>")
                  .Append(MarkHtml(month.Mark)).Append(month.Month.Length == 0 ? "Not run yet" : Enc(month.Month)).Append(Count(month.Done, month.Total)).Append("</summary>");
                AppendCards(sb, view, month.Cards, categories);
                sb.Append("</details>");
            }

            sb.Append("</details>");
        }

        sb.Append("</details>");
    }

    // 1128-C: a card naming a path that is not a plan stays visible, with the path it names.
    private static void AppendNotFound(StringBuilder sb, ProjectViews view, PlanNotFoundNode? node, IReadOnlyList<PageCategory> categories, string chip)
    {
        if (node is null)
        {
            return;
        }

        sb.Append("<details class='plan' data-project='").Append(Enc(view.Project)).Append("' data-key='").Append(Enc("g|" + view.Project + "|Plan not found")).Append("'").Append(node.Checked ? "" : " open").Append("><summary>")
          .Append(MarkHtml(node.Mark)).Append(chip).Append(" <b>Plan not found</b>").Append(Count(node.Done, node.Total)).Append("</summary>");
        AppendCards(sb, view, node.Cards, categories, showPath: true);
        sb.Append("</details>");
    }

    private static int Group(PlanKind kind) => kind switch
    {
        PlanKind.Plans => 0,
        PlanKind.Missing => 1,
        _ => 3,
    };

    private static void AppendPlan(StringBuilder sb, ProjectViews view, PlanNode node, IReadOnlyList<PageCategory> categories, string chip, bool nested = false, string prefix = "")
    {
        var plan = node.Plan;
        var noPlan = plan.Kind == PlanKind.NoPlan;
        var name = noPlan ? "No plan" : plan.Path;
        var title = noPlan
            ? "<b>No plan</b>"
            : "<a href='" + PlanHref(view.Project, plan.Path, "") + "'><b>" + Enc(name) + "</b></a>" + (plan.Kind == PlanKind.Missing ? " <span class='mute'>missing: not on main</span>" : "");
        var done = node.Done;
        var total = node.Total;

        // A complete plan is collapsed; an incomplete one is open. An indented one carries the step class, which is the tree's one-step indent.
        // 1123-C: the key the page script remembers this node's open state by; the plan of a project is named once, so its path is the key.
        sb.Append("<details class='plan").Append(nested ? " plan-step" : "").Append("' data-project='").Append(Enc(view.Project)).Append('\'')
          .Append(" data-key='").Append(Enc("p|" + view.Project + "|" + name)).Append("'")
          .Append(nested && node.Mark == PlanMark.Complete ? " data-complete='1'" : "")
          .Append(node.Complete || nested ? "" : " open").Append("><summary>")
          .Append(noPlan ? MarkHtml(node.Mark) : ClickMark(view.Project, plan.Path, PlanMarkFile.PlanKey, node.Mark, node.Manual, (node with { Manual = ManualMark.Automatic }).Mark)).Append(chip).Append(' ').Append(title).Append(node.Flag == PlanFlag.None ? "" : " <span class='mute plan-flag'>" + Enc(node.FlagText) + "</span>").Append(Count(done, total)).Append("</summary>");
        foreach (var step in node.Steps)
        {
            AppendStep(sb, view, plan, step, categories, prefix);
        }

        if (node.Other is { } other)
        {
            sb.Append("<details class='plan-step' data-key='").Append(Enc("o|" + view.Project + "|" + name)).Append("'").Append(other.Checked ? "" : " open").Append("><summary>").Append(MarkHtml(other.Mark)).Append("Other cards").Append(Count(other.Done, other.Total)).Append("</summary>");
            AppendCards(sb, view, other.Cards, categories, showPath: noPlan);
            sb.Append("</details>");
        }

        sb.Append("</details>");
    }

    private static void AppendStep(StringBuilder sb, ProjectViews view, PlanRef plan, PlanStepNode node, IReadOnlyList<PageCategory> categories, string prefix = "")
    {
        var step = node.Step!;
        // 1299-C: the number shown is the whole path in the plan tree (2.7, 2.10.8); the plan files, data-keys, marks and links keep the step's own label.
        var shown = prefix.Length == 0 ? step.Label : prefix + "." + step.Label;
        var label = "<a href='" + PlanHref(view.Project, plan.Path, step.Anchor) + "' title='" + Enc("step " + step.Label + " of " + plan.Path) + "'>" + Enc(shown + ". " + StepTitle(step.Label, step.Title)) + "</a>";
        var head = ClickMark(view.Project, plan.Path, step.Label, node.Mark, node.Manual, (node with { Manual = ManualMark.Automatic }).Mark) + label + Count(node.Done, node.Total);
        if (node.Cards.Count == 0 && node.Plans.Count == 0 && node.SubSteps.Count == 0)
        {
            sb.Append("<div class='plan-step'").Append(node.Mark == PlanMark.Complete ? " data-complete='1'" : "").Append('>').Append(head).Append("</div>");
            return;
        }

        // A checked step is collapsed; any other opens to its cards.
        sb.Append("<details class='plan-step' data-key='").Append(Enc("s|" + view.Project + "|" + plan.Path + "|" + step.Label)).Append("'").Append(node.Mark == PlanMark.Complete ? " data-complete='1'" : "").Append(node.Checked ? "" : " open").Append("><summary>").Append(head).Append("</summary>");
        AppendCards(sb, view, node.Cards, categories);
        foreach (var child in node.Plans)
        {
            AppendPlan(sb, view, child, categories, "", nested: true, prefix: shown);
        }

        foreach (var sub in node.SubSteps)
        {
            AppendStep(sb, view, plan, sub, categories, prefix);
        }

        sb.Append("</details>");
    }

    private static void AppendCards(StringBuilder sb, ProjectViews view, IReadOnlyList<ViewCard> cards, IReadOnlyList<PageCategory> categories, bool showPath = false)
    {
        if (cards.Count == 0)
        {
            return;
        }

        sb.Append("<ul>");
        foreach (var card in cards)
        {
            // VW-5: a review card opens its review panel, any other its text.
            var path = card.Folder == ViewFolder.Review ? "/review" : "/card";
            var title = CardTitle(card.Id, card.Title);
            sb.Append("<li class='plan-card'>").Append(MarkHtml(CardMark.Of(card))).Append(CategoryChip.Html(categories, card.Category))
              .Append(" <a href='").Append(path).Append("?project=").Append(Url(view.Project)).Append("&id=").Append(Url(card.Id)).Append("'>")
              .Append(Enc(card.Id)).Append(title.Length == 0 ? "" : ": " + Enc(title)).Append("</a>");
            if (showPath && PlanPaths.Named(card.PlanPath) is { Length: > 0 } named)
            {
                sb.Append(" <span class='mute'>Plan: ").Append(Enc(named)).Append("</span>");
            }

            sb.Append("</li>");
        }

        sb.Append("</ul>");
    }

    /// <summary>
    /// 1289-C: a plan written with numbered headings ("## 2. Build the job system") carries the number in the title
    /// already, so the step's label was drawn twice. When the title starts with the label followed by ".", ")" or a
    /// space, the rest of it is what is shown beside the label; anything else is shown as it is.
    /// </summary>
    private static string StepTitle(string label, string title)
    {
        var t = title.TrimStart();
        if (t.Length <= label.Length || !t.StartsWith(label, StringComparison.Ordinal))
        {
            return title;
        }

        return t[label.Length] is '.' or ')' or ' ' ? t[label.Length..].TrimStart('.', ')', ':', '-', ' ') : title;
    }

    /// <summary>
    /// 1289-C: a card's title line is "# 1274-B: After a restart ...", so the id was drawn twice on the card row.
    /// When the title starts with the id followed by ":" or a space, the rest of it is what is shown beside the id;
    /// anything else is shown as it is, and nothing left means the row is the id alone.
    /// </summary>
    private static string CardTitle(string id, string title)
    {
        var t = title.TrimStart();
        if (!t.StartsWith(id, StringComparison.OrdinalIgnoreCase))
        {
            return title;
        }

        if (t.Length == id.Length)
        {
            return "";
        }

        return t[id.Length] is ':' or ' ' ? t[id.Length..].TrimStart(':', ' ') : title;
    }

    private static string PlanHref(string project, string path, string anchor) =>
        "/plan?project=" + Url(project) + "&path=" + Url(path) + (anchor.Length == 0 ? "" : "#" + Url(anchor));

    private static string Count(int done, int total) =>
        total == 0 ? "" : " &nbsp;<span class='mute'>" + done + "/" + total + "</span>";

    /// <summary>
    /// 1171-C: only a plan's or a step's mark can be clicked; a card's, a group's and Other cards' marks follow their
    /// cards. The developer clicked those and saw a text cursor and nothing happen, so the two now look different: a
    /// clickable mark is boxed under the pointer or the keyboard focus, and every other mark keeps the arrow. The rules
    /// ride in the Plans windows, beside the markup they govern.
    /// </summary>
    internal const string MarkStyle =
        "<style>.plans-project .pm { cursor: default; } .plans-project .pm-click, .plans-project .pm-click .pm { cursor: pointer; }"
        + " .pm-click { display: inline-block; border-radius: 4px; outline: 1px solid transparent; }"
        + " .pm-click:hover, .pm-click:focus-visible { outline-color: currentColor; background: rgba(127, 127, 127, 0.2); }</style>";

    // 1125-C: the mark of a plan or step is a button that toggles automatic, checked (1215-C); a manual one carries a dot.
    // 1213-B: it carries its state (data-manual) and the mark its children give (data-auto), so the page script can show
    // the next state the moment it is clicked.
    private static string ClickMark(string project, string plan, string step, PlanMark mark, ManualMark manual, PlanMark auto)
    {
        var look = MarkHtml(mark);
        var state = manual switch
        {
            ManualMark.Checked => "set by you: checked",
            _ => "worked out from its cards and steps",
        };
        var set = manual == ManualMark.Automatic ? "" : "<span class='pm-set'>&bull;</span>";
        return "<span class='pm-click' role='button' tabindex='0' data-project='" + Enc(project) + "' data-plan='" + Enc(plan) + "' data-step='" + Enc(step)
            + "' data-manual='" + PlanMarkFile.Name(manual) + "' data-auto='" + AutoName(auto) + "' title='" + Enc(state + "; " + (manual == ManualMark.Automatic ? "Click to check" : "Click to return to automatic")) + "'>" + look.TrimEnd() + set + "</span> ";
    }

    private static string AutoName(PlanMark mark) => mark switch
    {
        PlanMark.Complete => "ok",
        PlanMark.Cancelled => "bad",
        _ => "open",
    };

    private static string MarkHtml(PlanMark mark) => mark switch
    {
        PlanMark.Complete => "<span class='pm pm-ok' title='complete'>&#10003;</span> ",
        PlanMark.Cancelled => "<span class='pm pm-bad' title='cancelled or errored'>&#10007;</span> ",
        _ => "<span class='pm' title='not complete'>&#9744;</span> ",
    };

    private static string Enc(string text) => System.Net.WebUtility.HtmlEncode(text);

    private static string Url(string text) => Uri.EscapeDataString(text);
}
