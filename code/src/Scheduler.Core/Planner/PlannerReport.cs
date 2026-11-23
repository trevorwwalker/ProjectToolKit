namespace Scheduler.Core.Planner;

/// <summary>
/// What the scheduler says about a batch it has dealt with (PLAN-8, <c>Planner-Step</c> L1483): the
/// page's last-result text and the log lines. Spec C1: a failed batch is also reported as sent,
/// as today.
/// </summary>
public sealed record PlannerReport
{
    /// <summary>The batch goes to <c>failed/</c> (with <see cref="Reason"/> in reason.txt), else <c>sent/</c>.</summary>
    public required bool Failed { get; init; }

    /// <summary>Why it failed, else "".</summary>
    public string Reason { get; init; } = "";

    /// <summary>The page's last result: <c>sent HH:mm message</c>.</summary>
    public required string Last { get; init; }

    /// <summary>The log lines, in order.</summary>
    public required IReadOnlyList<string> LogLines { get; init; }

    /// <summary>The report for a batch folder's plan.</summary>
    public static PlannerReport For(string batchName, PlannerPlan plan, DateTimeOffset now)
    {
        var at = now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var lines = new List<string>();
        if (!plan.Ok)
        {
            lines.Add($"planner: batch {batchName} failed: {plan.Error}");
        }

        // Spec C1: today's behaviour, kept until the developer decides (003-D review).
        lines.Add($"planner: {plan.Message}{(plan.Note.Length > 0 ? $" ({plan.Note})" : "")}");
        return new PlannerReport
        {
            Failed = !plan.Ok,
            Reason = plan.Ok ? "" : plan.Error,
            Last = $"sent {at} {plan.Message}",
            LogLines = lines,
        };
    }
}
