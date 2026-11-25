// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text.RegularExpressions;
using Scheduler.Core.Cards;

namespace Scheduler.Core.Views;

/// <summary>A plan's <c>Parent:</c> line, cleaned but not yet resolved (VW-25): the path, and the step text after it.</summary>
public sealed record PlanParentLine(string Path, string StepText);

/// <summary>VW-25: reading a plan's parent from its header.</summary>
public static class PlanParent
{
    private static readonly Regex ParentLine = new(@"^\s*\*{0,2}parent\*{0,2}\s*:\s*\*{0,2}\s*(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Fence = new(@"^ {0,3}(```|~~~)", RegexOptions.Compiled);
    private static readonly Regex H2 = new(@"^ {0,3}##[ \t]", RegexOptions.Compiled);

    /// <summary>
    /// The first <c>Parent:</c> line before the plan's first <c>## </c> heading (outside code fences); null when there is
    /// none, it is empty or it starts <c>none</c>: the plan is a root.
    /// </summary>
    public static PlanParentLine? Of(string? planText)
    {
        var fence = false;
        foreach (var raw in (planText ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (Fence.IsMatch(line))
            {
                fence = !fence;
                continue;
            }

            if (fence)
            {
                continue;
            }

            if (H2.IsMatch(line))
            {
                return null;
            }

            var m = ParentLine.Match(line);
            if (!m.Success)
            {
                continue;
            }

            var value = m.Groups[1].Value.Replace("`", "").Replace('\\', '/').Trim().TrimEnd('*').Trim();
            var (path, step) = CardFiles.SplitPlan(value);
            return path.Length == 0 ? null : new PlanParentLine(path, step);
        }

        return null;
    }
}

/// <summary>Why a plan is not placed under its parent (VW-28), in the order of precedence.</summary>
public enum PlanFlag
{
    None,
    ParentNotFound,
    ParentNotAPlan,
    StepNotFound,
    Cycle,
}

/// <summary>The words for a <see cref="PlanFlag"/>.</summary>
public static class PlanFlags
{
    public static string Text(PlanFlag flag, PlanParentLine? line) => flag switch
    {
        PlanFlag.ParentNotFound => "parent not found: " + line?.Path,
        PlanFlag.ParentNotAPlan => "parent is not a plan: " + line?.Path,
        PlanFlag.StepNotFound => "step not found: " + line?.Path + ", " + (string.IsNullOrWhiteSpace(line?.StepText) ? "no step given" : line!.StepText),
        PlanFlag.Cycle => "in a cycle: its parents lead back to it",
        _ => "",
    };
}
