// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Pool;

/// <summary>
/// MP-6 with spec B3 Q2's answer: look back two hours; count the minutes of workers still running
/// up to now; compare each project's weight share with its used share by subtracting; ties go to
/// the project listed first; so a project with no minutes wins over one that has used its share.
/// Only the projects with an offer take part (MP-5), so an idle project's share goes to the others.
/// </summary>
public static class PoolShares
{
    /// <summary>How far back the shares look (Q2: two hours).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(2);

    /// <summary>
    /// A project's worker-minutes inside the window ending <paramref name="now"/>: each run's part
    /// between the window's start and now, a run still going counting up to now.
    /// </summary>
    public static double Minutes(IEnumerable<PoolRun> runs, string project, DateTimeOffset now)
    {
        var from = now - Window;
        double minutes = 0;
        foreach (var run in runs.Where(r => Text.Comparer.Equals(r.Project, project)))
        {
            var start = run.Start > from ? run.Start : from;
            var end = run.End is DateTimeOffset e && e < now ? e : now;
            if (end > start)
            {
                minutes += (end - start).TotalMinutes;
            }
        }

        return minutes;
    }

    /// <summary>
    /// The offer whose project is furthest below its share: weight / total weight of the offering
    /// projects, minus minutes / total minutes of the offering projects (0 when nobody has any).
    /// The first offer wins a tie; offers come in project order.
    /// </summary>
    public static PoolOffer FurthestBelowShare(
        IReadOnlyList<PoolOffer> offers,
        IEnumerable<PoolProject> projects,
        IEnumerable<PoolRun> runs,
        DateTimeOffset now)
    {
        var weights = new Dictionary<string, int>(Text.Comparer);
        foreach (var p in projects)
        {
            weights.TryAdd(p.Name, p.Weight);
        }

        var runList = runs.ToList();
        var weight = offers.Select(o => (double)Math.Max(0, weights.GetValueOrDefault(o.Project))).ToList();
        var used = offers.Select(o => Minutes(runList, o.Project, now)).ToList();
        var totalWeight = weight.Sum();
        var totalUsed = used.Sum();

        PoolOffer? best = null;
        var bestGap = double.NegativeInfinity;
        for (var i = 0; i < offers.Count; i++)
        {
            var weightShare = totalWeight > 0 ? weight[i] / totalWeight : 1.0 / offers.Count;
            var usedShare = totalUsed > 0 ? used[i] / totalUsed : 0;
            var gap = weightShare - usedShare;
            if (best is null || gap > bestGap + 1e-9)
            {
                best = offers[i];
                bestGap = gap;
            }
        }

        return best ?? throw new ArgumentException("no offers", nameof(offers));
    }
}
