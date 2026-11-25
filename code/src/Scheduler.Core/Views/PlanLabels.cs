// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Views;

/// <summary>1318-A: the step numbers and card titles the Plans view and the Gantt view show, worked out in one place.</summary>
public static class PlanLabels
{
    /// <summary>
    /// 1289-C: a plan written with numbered headings ("## 2. Build the job system") carries the number in the title
    /// already, so the step's label was drawn twice. When the title starts with the label followed by ".", ")" or a
    /// space, the rest of it is what is shown beside the label; anything else is shown as it is.
    /// </summary>
    public static string StepTitle(string label, string title)
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
    public static string CardTitle(string id, string title)
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

    /// <summary>1299-C: the number shown for a step is its whole path in the plan tree (2.7, 2.10.8): the enclosing step's shown number, a dot, the step's own label.</summary>
    public static string Shown(string prefix, string label) => prefix.Length == 0 ? label : prefix + "." + label;

    /// <summary>A step's text as shown: its shown number, a dot and its title without the number it already starts with.</summary>
    public static string StepText(string prefix, string label, string title) => Shown(prefix, label) + ". " + StepTitle(label, title);

    /// <summary>A card's text as shown: its id once, then a colon and its title without the id; the id alone when nothing is left.</summary>
    public static string CardText(string id, string title) => CardTitle(id, title) is { Length: > 0 } t ? id + ": " + t : id;
}
