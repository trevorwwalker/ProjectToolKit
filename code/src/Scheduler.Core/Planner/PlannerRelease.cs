// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Planner;

/// <summary>
/// A card the batch wrote with <c>Status: ready</c> has its run count cleared, as the page's Retry
/// does (PLAN-7, <c>Planner-Release</c> L1456). A card the batch did not write, or wrote with another
/// status, is left alone.
/// </summary>
public static class PlannerRelease
{
    /// <summary>
    /// Clears the run counts after the batch is pushed and logs each one. True when any was
    /// cleared: the caller saves the retries and ticks now.
    /// </summary>
    public static bool Clear(PlannerPlan plan, IDictionary<string, int> retries, ISchedulerLog log)
    {
        var cleared = false;
        foreach (var file in plan.Written)
        {
            var m = Text.Match(file.Path, @"/(\d+-[A-Z])-[^/]+\.md$");
            if (!m.Success)
            {
                continue;
            }

            var id = m.Groups[1].Value;
            if (!retries.ContainsKey(id) || !IsReady(file.Text))
            {
                continue;
            }

            retries.Remove(id);
            cleared = true;
            log.Write($"planner: {id} set ready, run count cleared");
        }

        return cleared;
    }

    private static bool IsReady(string text)
    {
        foreach (var line in System.Text.RegularExpressions.Regex.Split(text, "\r?\n"))
        {
            var m = Text.Match(line, @"^Status:\s*(.*)$");
            if (m.Success)
            {
                return Text.IsMatch(m.Groups[1].Value.Trim(), "^ready");
            }
        }

        return false;
    }
}
