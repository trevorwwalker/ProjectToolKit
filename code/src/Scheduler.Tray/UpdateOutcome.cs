// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray;

/// <summary>
/// What a deploy of the scheduler did, in the words the log window and the card ask for (967-B): which
/// version replaced which, and whether the new one is the one that stayed running.
/// </summary>
/// <param name="Result">Deployed, RolledBack or Failed.</param>
/// <param name="Message">What happened, for the log window.</param>
/// <param name="OldVersion">The version that was running before, or "" when it is not known.</param>
/// <param name="NewVersion">The version that was deployed, or "" when nothing was published.</param>
public sealed record UpdateOutcome(UpdateResult Result, string Message, string OldVersion, string NewVersion)
{
    /// <summary>Whether the new build is the one now running.</summary>
    public bool Ok => Result == UpdateResult.Deployed;

    /// <summary>The one-line answer: "<c>scheduler 3.0.8 replaced 3.0.7</c>" when the deploy held.</summary>
    public string Summary => Result switch
    {
        UpdateResult.Deployed => $"scheduler {Shown(NewVersion)} replaced {Shown(OldVersion)}",
        UpdateResult.RolledBack => $"the new scheduler {Shown(NewVersion)} would not start; {Shown(OldVersion)} is running again",
        _ => Message,
    };

    private static string Shown(string version) => version is { Length: > 0 } v ? v : "an unknown version";
}
