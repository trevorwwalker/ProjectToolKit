namespace Scheduler.Core.Views;

/// <summary>Where a plan was found (VW-11, VW-12). The order is the Plans view's order.</summary>
public enum PlanKind
{
    /// <summary>In <c>jobs/plans/</c>.</summary>
    Plans,

    /// <summary>A document elsewhere (a design document, the roadmap).</summary>
    Elsewhere,

    /// <summary>In <c>jobs/archive/</c>: shown flagged, under "Archived plans".</summary>
    Archived,

    /// <summary>Named by a card but not on main: shown under its own name, marked missing.</summary>
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

        if (path.StartsWith("jobs/plans/", StringComparison.OrdinalIgnoreCase) && !onMain.Contains(path))
        {
            var archived = "jobs/archive/" + path["jobs/plans/".Length..];
            if (onMain.Contains(archived))
            {
                return new PlanRef(PlanKind.Archived, archived);
            }
        }

        if (!onMain.Contains(path))
        {
            return new PlanRef(PlanKind.Missing, path);
        }

        var kind = path.StartsWith("jobs/plans/", StringComparison.OrdinalIgnoreCase) ? PlanKind.Plans
            : path.StartsWith("jobs/archive/", StringComparison.OrdinalIgnoreCase) ? PlanKind.Archived
            : PlanKind.Elsewhere;
        return new PlanRef(kind, path);
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
