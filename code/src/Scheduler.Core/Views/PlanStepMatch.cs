// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text.RegularExpressions;

namespace Scheduler.Core.Views;

/// <summary>Which step of its plan a card's <c>Plan:</c> step text belongs to.</summary>
public static class PlanStepMatch
{
    private static readonly Regex Number = new(@"\d+", RegexOptions.Compiled);

    /// <summary>
    /// The first number in the text picks step n, a second number picks n.m (step n when it has no such sub-step).
    /// Null when the text has no number or the plan has no such step: the card belongs under "Other cards".
    /// </summary>
    public static PlanStep? Of(string? planStepText, IReadOnlyList<PlanStep> steps)
    {
        var numbers = Number.Matches(planStepText ?? "");
        if (numbers.Count == 0 || !TryParse(numbers[0].Value, out var n))
        {
            return null;
        }

        var step = steps.FirstOrDefault(s => s.Number == n);
        if (step is null)
        {
            return null;
        }

        if (numbers.Count > 1 && TryParse(numbers[1].Value, out var m))
        {
            return step.SubSteps.FirstOrDefault(s => s.Sub == m) ?? step;
        }

        return step;
    }

    private static bool TryParse(string digits, out int n) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out n);
}
