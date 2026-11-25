// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Results;

/// <summary>What a finished run means (TIME-14 to TIME-19): the result and the retry counts after it.</summary>
public sealed record RunOutcome
{
    /// <summary>The result as logged and accounted: a LANE-RESULT word or the end reason's text.</summary>
    public required string Result { get; init; }

    /// <summary>The end reason when the run had no result word; null otherwise.</summary>
    public EndReason? EndReason { get; init; }

    /// <summary>The retry counts by card id after this run.</summary>
    public required IReadOnlyDictionary<string, int> Retries { get; init; }

    /// <summary>Dispatching must be paused: the account is out of quota (TIME-15).</summary>
    public bool PauseForQuota { get; init; }
}
