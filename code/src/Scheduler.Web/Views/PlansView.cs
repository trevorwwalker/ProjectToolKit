using System.Text;
using Scheduler.Core.Contracts;
using Scheduler.Core.Views;
using Scheduler.Core.WebContract;

namespace Scheduler.Web.Views;

/// <summary>
/// The Plans view's window (1114-C, views.md second pass): per project each plan as a checklist tree. Every node
/// starts with a mark (green check, empty box, red X); a plan is a link to its document with its steps under it,
/// each step a link to its heading and a node opening to its cards. "Archived plans" folded; "No plan" last.
/// Built only when the view is asked for (VW-23).
/// </summary>
internal static class PlansView
{
    /// <summary>The window's HTML for every project's views data.</summary>
    /// <param name="views">One per project, from the pool snapshot.</param>
    /// <param name="pages">The projects' pages, for their category chips.</param>
    /// <param name="names">Every project's name, for the project chips.</param>
    public static string Html(IReadOnlyList<ProjectViews> views, IReadOnlyList<PageSnapshot> pages, IReadOnlyList<string> names)
    {
        var sb = new StringBuilder();
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

        // VW-11: a plan with no card naming it is still listed, with nothing under it.
        var tree = PlanTree.Of(
            view.Snapshot.Cards,
            new HashSet<string>(view.OnMain, StringComparer.Ordinal),
            view.Snapshot.PlanTexts,
            view.Snapshot.Plans.Select(p => new PlanRef(p.Kind, p.Path)));

        foreach (var node in tree.Where(n => Group(n.Plan.Kind) < 2))
        {
            AppendPlan(sb, view, node, categories, chip);
        }

        var archived = tree.Where(n => Group(n.Plan.Kind) == 2).ToList();
        if (archived.Count > 0)
        {
            sb.Append("<details class='plan-archived' data-key='").Append(Enc("a|" + view.Project)).Append("'><summary>Archived plans (").Append(archived.Count).Append(")</summary>");
            foreach (var node in archived)
            {
                // 1122-C: one step inside the group, the same indent every nested level of a plan uses.
                AppendPlan(sb, view, node, categories, chip, indent: true);
            }

            sb.Append("</details>");
        }

        foreach (var node in tree.Where(n => Group(n.Plan.Kind) == 3))
        {
            AppendPlan(sb, view, node, categories, chip);
        }

        sb.Append("</div>");
    }

    private static int Group(PlanKind kind) => kind switch
    {
        PlanKind.Plans => 0,
        PlanKind.Elsewhere or PlanKind.Missing => 1,
        PlanKind.Archived => 2,
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
        sb.Append("<details class='plan").Append(indent ? " plan-step" : "").Append("' data-project='").Append(Enc(view.Project)).Append('\'').Append(node.Complete ? "" : " open").Append("><summary>")
          .Append(MarkHtml(node.Mark)).Append(chip).Append(' ').Append(title).Append(Count(done, total)).Append("</summary>");
        foreach (var step in node.Steps)
        {
            AppendStep(sb, view, plan, step, categories);
        }

        if (node.Other is { } other)
        {
            sb.Append("<details class='plan-step' data-key='").Append(Enc("o|" + view.Project + "|" + name)).Append("'").Append(other.Checked ? "" : " open").Append("><summary>").Append(MarkHtml(other.Mark)).Append("Other cards").Append(Count(other.Done, other.Total)).Append("</summary>");
            AppendCards(sb, view, other.Cards, categories);
            sb.Append("</details>");
        }

        sb.Append("</details>");
    }

    private static void AppendStep(StringBuilder sb, ProjectViews view, PlanRef plan, PlanStepNode node, IReadOnlyList<PageCategory> categories)
    {
        var step = node.Step!;
        var label = "<a href='" + PlanHref(view.Project, plan.Path, step.Anchor) + "'>" + Enc(step.Label + ". " + step.Title) + "</a>";
        var head = MarkHtml(node.Mark) + label + Count(node.Done, node.Total);
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

    private static void AppendCards(StringBuilder sb, ProjectViews view, IReadOnlyList<ViewCard> cards, IReadOnlyList<PageCategory> categories)
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
              .Append(Enc(card.Id)).Append(' ').Append(Enc(card.Title)).Append("</a></li>");
        }

        sb.Append("</ul>");
    }

    private static string PlanHref(string project, string path, string anchor) =>
        "/plan?project=" + Url(project) + "&path=" + Url(path) + (anchor.Length == 0 ? "" : "#" + Url(anchor));

    private static string Count(int done, int total) =>
        total == 0 ? "" : " &nbsp;<span class='mute'>" + done + "/" + total + "</span>";

    private static string MarkHtml(PlanMark mark) => mark switch
    {
        PlanMark.Complete => "<span class='pm pm-ok' title='complete'>&#10003;</span> ",
        PlanMark.Cancelled => "<span class='pm pm-bad' title='cancelled or errored'>&#10007;</span> ",
        _ => "<span class='pm' title='not complete'>&#9744;</span> ",
    };

    private static string Enc(string text) => System.Net.WebUtility.HtmlEncode(text);

    private static string Url(string text) => Uri.EscapeDataString(text);
}
