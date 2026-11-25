// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Views;

namespace Scheduler.Core.WebContract;

/// <summary>
/// One project's views data for the page (views.md section 1, VW-20 to VW-22): the views snapshot
/// the Host rebuilt when something moved, with what the Plans view needs beside it - the cards
/// running now, the paths that are on main (for <see cref="PlanCounts.Of"/>), which roadmap item
/// each design document names, and whether the done cards have been read yet. Immutable; never part
/// of <see cref="PageSnapshot"/> or its version.
/// </summary>
public sealed record ProjectViews
{
    /// <summary>What the views say while the done cards are still being read (VW-21).</summary>
    public const string ReadingDone = "reading done cards...";

    /// <summary>What the views say while the project's model of main has not loaded.</summary>
    public const string Starting = "starting...";

    /// <summary>The project's name.</summary>
    public required string Project { get; init; }

    /// <summary>The cards and plans (1089-A's snapshot).</summary>
    public required ViewsSnapshot Snapshot { get; init; }

    /// <summary>The ids (<c>1090-B</c>) of the cards with a worker running, from the page's slots.</summary>
    public IReadOnlySet<string> Running { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The paths on main a card's <c>Plan:</c> may resolve to (VW-12): the jobs folder's files and the plan documents read.</summary>
    public IReadOnlySet<string> OnMain { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Whether the done and archive headers are in the snapshot (VW-21).</summary>
    public bool DoneLoaded { get; init; }

    /// <summary>"" when complete, else <see cref="ReadingDone"/> or <see cref="Starting"/>.</summary>
    public string Note { get; init; } = "";

    /// <summary>The model's commit the snapshot was built at, or "" before the first load.</summary>
    public string Commit { get; init; } = "";

    /// <summary>The version the page compares (VW-20): the snapshot's, marked while it is incomplete.</summary>
    public string Version => Note.Length == 0 ? Snapshot.Version : Snapshot.Version + "-" + (DoneLoaded ? "s" : "r");

    /// <summary>An empty one for <paramref name="project"/>, saying <paramref name="note"/>.</summary>
    public static ProjectViews Empty(string project, string note) =>
        new() { Project = project, Snapshot = ViewsSnapshot.Build(project, [], []), Note = note };

    /// <summary>The views version of every project together, for <c>/live.json</c>'s <c>views</c> (VW-20); "" when there are none.</summary>
    public static string VersionOf(IEnumerable<ProjectViews> views)
    {
        var joined = string.Join(",", views.Select(v => v.Project + ":" + v.Version));
        return joined.Length == 0
            ? ""
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(joined)))[..16].ToLowerInvariant();
    }
}
