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
    /// <summary>The window's HTML for every project's views data.</summary>
    /// <param name="views">One per project, from the pool snapshot.</param>
    /// <param name="pages">The projects' pages, for their category chips.</param>
    /// <param name="names">Every project's name, for the project chips.</param>
    /// <param name="completed">1127-C: false draws the unfinished plans and "No plan"; true draws the plans whose top-level mark is a green check.</param>
    public static string Html(IReadOnlyList<ProjectViews> views, IReadOnlyList<PageSnapshot> pages, IReadOnlyList<string> names, bool completed = false)
    {
        var sb = new StringBuilder();
        if (views.Count == 0)
        {
            sb.Append(completed
                ? "<p class='mute'>No completed plans yet.</p>"
                : "<p class='mute'>No plans yet: the scheduler has not read any project's cards.</p>");
        }

        foreach (var view in views.OrderBy(v => v.Project, StringComparer.OrdinalIgnoreCase))
        {
            var categories = pages.FirstOrDefault(p => string.Equals(p.Project, view.Project, StringComparison.OrdinalIgnoreCase))?.Categories ?? [];
            AppendProject(sb, view, categories, names, completed);
        }

        return sb.ToString();
    }

    private static void AppendProject(StringBuilder sb, ProjectViews view, IReadOnlyList<PageCategory> categories, IReadOnlyList<string> names, bool completed)
    {
        var chip = names.Count < 2 ? "" : ProjectChip.Html(view.Project, names);
        sb.Append("<div class='plans-project' data-project='").Append(Enc(view.Project)).Append("'>");
        if (view.Note.Length > 0 && !completed)
        {
            sb.Append("<div class='mute'>").Append(chip).Append(' ').Append(Enc(view.Project)).Append(": ").Append(Enc(view.Note)).Append("</div>");
        }

        // VW-11: a plan with no card naming it is still listed, with nothing under it.
        var tree = PlanTree.Of(
            view.Snapshot.Cards,
            new HashSet<string>(view.OnMain, StringComparer.Ordinal),
            view.Snapshot.PlanTexts,
            view.Snapshot.Plans.Select(p => new PlanRef(p.Kind, p.Path)),
            view.Snapshot.Marks);

        // 1127-C: a plan whose top-level mark is a green check (worked out or set by a click) is in the Completed plans window.
        // 1128-C: cards with no plan are not a plan node: they are the Bugs, Requests and Plan not found groups, always in the left window.
        var shown = tree.Where(n => n.Plan.Kind != PlanKind.NoPlan && (n.Mark == PlanMark.Complete) == completed).ToList();
        if (completed && shown.Count == 0)
        {
            return;
        }

        foreach (var node in shown)
        {
            AppendPlan(sb, view, node, categories, chip);
        }

        if (!completed)
        {
            var groups = PlanGroups.Of(view.Snapshot.Cards, new HashSet<string>(view.OnMain, StringComparer.Ordinal), view.Snapshot.Runs);
            AppendGroup(sb, view, groups.Bugs, categories, chip);
            AppendGroup(sb, view, groups.Requests, categories, chip);
            AppendNotFound(sb, view, groups.NotFound, categories, chip);
        }

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

    private static void AppendPlan(StringBuilder sb, ProjectViews view, PlanNode node, IReadOnlyList<PageCategory> categories, string chip, bool indent = false)
    {
        var plan = node.Plan;
        var noPlan = plan.Kind == PlanKind.NoPlan;
        var name = noPlan ? "No plan" : plan.Path;
        var title = noPlan
            ? "<b>No plan</b>"
            : "<a href='" + PlanHref(view.Project, plan.Path, "") + "'><b>" + Enc(name) + "</b></a>" + (plan.Kind == PlanKind.Missing ? " <span class='mute'>missing: not on main</span>" : "");
        var done = node.Steps.Sum(s => s.Done) + (node.Other?.Done ?? 0);
        var total = node.Steps.Sum(s => s.Total) + (node.Other?.Total ?? 0);

        // A complete plan is collapsed; an incomplete one is open. An indented one carries the step class, which is the tree's one-step indent.
        // 1123-C: the key the page script remembers this node's open state by; the plan of a project is named once, so its path is the key.
        sb.Append("<details class='plan").Append(indent ? " plan-step" : "").Append("' data-project='").Append(Enc(view.Project)).Append('\'')
          .Append(" data-key='").Append(Enc("p|" + view.Project + "|" + name)).Append("'")
          .Append(node.Complete ? "" : " open").Append("><summary>")
          .Append(noPlan ? MarkHtml(node.Mark) : ClickMark(view.Project, plan.Path, PlanMarkFile.PlanKey, node.Mark, node.Manual)).Append(chip).Append(' ').Append(title).Append(Count(done, total)).Append("</summary>");
        foreach (var step in node.Steps)
        {
            AppendStep(sb, view, plan, step, categories);
        }

        if (node.Other is { } other)
        {
            sb.Append("<details class='plan-step' data-key='").Append(Enc("o|" + view.Project + "|" + name)).Append("'").Append(other.Checked ? "" : " open").Append("><summary>").Append(MarkHtml(other.Mark)).Append("Other cards").Append(Count(other.Done, other.Total)).Append("</summary>");
            AppendCards(sb, view, other.Cards, categories, showPath: noPlan);
            sb.Append("</details>");
        }

        sb.Append("</details>");
    }

    private static void AppendStep(StringBuilder sb, ProjectViews view, PlanRef plan, PlanStepNode node, IReadOnlyList<PageCategory> categories)
    {
        var step = node.Step!;
        var label = "<a href='" + PlanHref(view.Project, plan.Path, step.Anchor) + "'>" + Enc(step.Label + ". " + step.Title) + "</a>";
        var head = ClickMark(view.Project, plan.Path, step.Label, node.Mark, node.Manual) + label + Count(node.Done, node.Total);
        if (node.Cards.Count == 0 && node.SubSteps.Count == 0)
        {
            sb.Append("<div class='plan-step'>").Append(head).Append("</div>");
            return;
        }

        // A checked step is collapsed; any other opens to its cards.
        sb.Append("<details class='plan-step' data-key='").Append(Enc("s|" + view.Project + "|" + plan.Path + "|" + step.Label)).Append("'").Append(node.Checked ? "" : " open").Append("><summary>").Append(head).Append("</summary>");
        AppendCards(sb, view, node.Cards, categories);
        foreach (var sub in node.SubSteps)
        {
            AppendStep(sb, view, plan, sub, categories);
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
            sb.Append("<li class='plan-card'>").Append(MarkHtml(CardMark.Of(card))).Append(CategoryChip.Html(categories, card.Category))
              .Append(" <a href='").Append(path).Append("?project=").Append(Url(view.Project)).Append("&id=").Append(Url(card.Id)).Append("'>")
              .Append(Enc(card.Id)).Append(' ').Append(Enc(card.Title)).Append("</a>");
            if (showPath && PlanPaths.Named(card.PlanPath) is { Length: > 0 } named)
            {
                sb.Append(" <span class='mute'>Plan: ").Append(Enc(named)).Append("</span>");
            }

            sb.Append("</li>");
        }

        sb.Append("</ul>");
    }

    private static string PlanHref(string project, string path, string anchor) =>
        "/plan?project=" + Url(project) + "&path=" + Url(path) + (anchor.Length == 0 ? "" : "#" + Url(anchor));

    private static string Count(int done, int total) =>
        total == 0 ? "" : " &nbsp;<span class='mute'>" + done + "/" + total + "</span>";

    // 1125-C: the mark of a plan or step is a button that cycles automatic, checked, cleared; a manual one carries a dot.
    private static string ClickMark(string project, string plan, string step, PlanMark mark, ManualMark manual)
    {
        var look = MarkHtml(mark);
        var state = manual switch
        {
            ManualMark.Checked => "set by you: checked",
            ManualMark.Cleared => "set by you: cleared",
            _ => "worked out from its cards and steps",
        };
        var set = manual == ManualMark.Automatic ? "" : "<span class='pm-set'>&bull;</span>";
        return "<span class='pm-click' role='button' tabindex='0' data-project='" + Enc(project) + "' data-plan='" + Enc(plan) + "' data-step='" + Enc(step)
            + "' title='" + Enc(state + "; click to change") + "'>" + look.TrimEnd() + set + "</span> ";
    }

    private static string MarkHtml(PlanMark mark) => mark switch
    {
        PlanMark.Complete => "<span class='pm pm-ok' title='complete'>&#10003;</span> ",
        PlanMark.Cancelled => "<span class='pm pm-bad' title='cancelled or errored'>&#10007;</span> ",
        _ => "<span class='pm' title='not complete'>&#9744;</span> ",
    };

    private static string Enc(string text) => System.Net.WebUtility.HtmlEncode(text);

    private static string Url(string text) => Uri.EscapeDataString(text);
}
