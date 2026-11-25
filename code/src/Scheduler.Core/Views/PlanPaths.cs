// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Views;

/// <summary>Where a plan was found (VW-11, VW-12). The order is the Plans view's order.</summary>
public enum PlanKind
{
    /// <summary>In <c>jobs/plans/</c>.</summary>
    Plans,

    /// <summary>Not used: only <c>jobs/plans/</c> holds plans (kept so older callers compile).</summary>
    Elsewhere,

    /// <summary>Not used: plans are never archived (kept so older callers compile).</summary>
    Archived,

    /// <summary>Named by a card in jobs/plans/ but not on main: shown under its own name, marked missing.</summary>
    Missing,

    /// <summary>No plan: <c>none...</c>, an empty or absent Plan line.</summary>
    NoPlan,
}

/// <summary>A card's plan path resolved against main. <see cref="Path"/> is empty for No plan.</summary>
public readonly record struct PlanRef(PlanKind Kind, string Path);

/// <summary>VW-12: resolving a card's plan path. Pure: the caller passes the paths that exist on main.</summary>
public static class PlanPaths
{
    public static readonly PlanRef NoPlan = new(PlanKind.NoPlan, "");

    public static PlanRef Resolve(string? raw, ISet<string> onMain)
    {
        var path = Clean(raw);
        if (path.Length == 0 || path.StartsWith("none", StringComparison.OrdinalIgnoreCase))
        {
            return NoPlan;
        }

        if (path.StartsWith("docs/jobs/", StringComparison.OrdinalIgnoreCase))
        {
            path = path["docs/".Length..];
        }

        // Plans are never archived: an old jobs/archive/X path means jobs/plans/X.
        if (path.StartsWith("jobs/archive/", StringComparison.OrdinalIgnoreCase))
        {
            var moved = "jobs/plans/" + path["jobs/archive/".Length..];
            if (onMain.Contains(moved))
            {
                path = moved;
            }
        }

        // Only a file in jobs/plans/ is a plan; any other path is No plan (the card shows its path).
        if (!path.StartsWith("jobs/plans/", StringComparison.OrdinalIgnoreCase))
        {
            return NoPlan;
        }

        return new PlanRef(onMain.Contains(path) ? PlanKind.Plans : PlanKind.Missing, path);
    }

    /// <summary>The path a card's <c>Plan:</c> names, cleaned; empty for <c>none...</c>, an empty or absent line.</summary>
    public static string Named(string? raw)
    {
        var path = Clean(raw);
        return path.StartsWith("none", StringComparison.OrdinalIgnoreCase) ? "" : path;
    }

    private static string Clean(string? raw)
    {
        var s = (raw ?? "").Replace("`", "").Replace('\\', '/').Trim();
        while (s.Length > 0 && (s[^1] == ';' || s[^1] == ','))
        {
            s = s[..^1].TrimEnd();
        }

        return s;
    }
}
