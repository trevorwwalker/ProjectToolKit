// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.WebContract;

/// <summary>
/// What the runs of a stuck card did (940-A): the count dispatch stuck it at, and the last finished
/// run's result and log, so the Waiting on you row can say why the card needs the developer and
/// point at the log that shows it. The host has both from <c>retries.json</c> and the recent runs.
/// </summary>
public sealed record PageStuckRun
{
    /// <summary>The card's file name, or its id.</summary>
    public required string Card { get; init; }

    /// <summary>The runs counted against the card. 0 when the host has no count to give.</summary>
    public int Runs { get; init; }

    /// <summary>The result word of its last run, such as <c>crashed (exit 1)</c>; empty when unknown.</summary>
    public string Result { get; init; } = "";

    /// <summary>The log file of its last run; empty when the run has none.</summary>
    public string Log { get; init; } = "";
}
