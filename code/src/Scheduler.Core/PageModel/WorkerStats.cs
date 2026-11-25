// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.PageModel;

/// <summary>
/// What a worker's raw log says about its model (PAGE-10): the counts the live view shows, kept
/// across restarts and updated as the log is scanned. The CLI reports no token counts, so they are
/// estimates - streamed text at 3.5 characters a token, and the context the text the session has
/// sent and received at the same rate. A faithful port of <c>LiveStats</c> (schedule.ps1 L1963).
/// </summary>
public sealed class WorkerStats
{
    /// <summary>Model calls that started.</summary>
    public int Calls { get; set; }

    /// <summary>Tools that ran.</summary>
    public int Tools { get; set; }

    /// <summary>Tools that failed.</summary>
    public int ToolFails { get; set; }

    /// <summary>The estimated size of the context in characters.</summary>
    public double CtxChars { get; set; }

    /// <summary>How often the session was compacted.</summary>
    public int Compactions { get; set; }

    /// <summary>Estimated generated tokens, at 3.5 characters a token.</summary>
    public double GenTokens { get; set; }

    /// <summary>Seconds spent generating.</summary>
    public double GenSeconds { get; set; }

    /// <summary>The last call's estimated tokens a second.</summary>
    public double LastTps { get; set; }

    /// <summary>The last call's seconds to its first streamed token.</summary>
    public double LastTtft { get; set; }

    // The call being scanned: when it started, when its first piece arrived, and how much has
    // streamed since (in estimated tokens).
    internal DateTimeOffset? CallStart { get; set; }

    internal DateTimeOffset? FirstDelta { get; set; }

    internal double Deltas { get; set; }
}
