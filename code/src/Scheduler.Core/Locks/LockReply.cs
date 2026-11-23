using System.Globalization;

namespace Scheduler.Core.Locks;

/// <summary>
/// The text of a reply to a lock request, its exit code for the <c>lock</c> command, and reading it
/// back (design <c>dynamic-locks.md</c>, section 3): one line, so every worker (Claude or spark) reads
/// it the same way (1018-D).
/// </summary>
public static class LockReply
{
    /// <summary>
    /// <c>granted</c>; <c>waiting: held by &lt;card&gt;, gives up at HH:mm</c>; <c>yield: &lt;path&gt; held by
    /// &lt;card&gt; (older)</c> or <c>(wait limit)</c>; <c>refused: &lt;reason&gt;</c>. The time is shown as
    /// the decision's offset has it (Host passes local time).
    /// </summary>
    public static string Text(LockDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return decision.Answer switch
        {
            LockAnswer.Granted => "granted",
            LockAnswer.Waiting => $"waiting: held by {decision.Holder}, gives up at {decision.GivesUpAt?.ToString("HH:mm", CultureInfo.InvariantCulture)}",
            LockAnswer.Yield => $"yield: {decision.Path} held by {decision.Holder} ({decision.Reason})",
            _ => $"refused: {decision.Reason}",
        };
    }

    /// <summary>The <c>lock</c> command's exit code: 0 granted, 2 waiting, 3 yield, 4 refused.</summary>
    public static int ExitCode(LockAnswer answer) => answer switch
    {
        LockAnswer.Granted => 0,
        LockAnswer.Waiting => 2,
        LockAnswer.Yield => 3,
        _ => 4,
    };

    /// <summary>The answer a reply's text gives, or null when it is none of them.</summary>
    public static LockAnswer? Parse(string? text)
    {
        var t = (text ?? "").Trim().TrimStart('﻿');
        if (t == "granted")
        {
            return LockAnswer.Granted;
        }

        return t.StartsWith("waiting:", StringComparison.Ordinal) ? LockAnswer.Waiting
            : t.StartsWith("yield:", StringComparison.Ordinal) ? LockAnswer.Yield
            : t.StartsWith("refused:", StringComparison.Ordinal) ? LockAnswer.Refused
            : null;
    }
}
