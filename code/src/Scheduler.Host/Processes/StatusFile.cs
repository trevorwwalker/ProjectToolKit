// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using System.Text;
using Scheduler.Core.Accounting;
using Scheduler.Core.Cards;
using Scheduler.Core.Contracts;
using Scheduler.Core.Watchdog;

namespace Scheduler.Host.Processes;

/// <summary>status.txt (ADOPT-3): when and in which state, a line per slot, then the cards that need someone.</summary>
public static class StatusFile
{
    /// <summary>The file's text.</summary>
    public static string Build(
        DateTimeOffset now,
        string state,
        SchedulerConfig config,
        int slots,
        Func<int, bool> slotEnabled,
        IReadOnlyDictionary<int, RunningWorker> running,
        IReadOnlyList<Card> cards,
        IReadOnlyList<WorkerType>? types = null)
    {
        var lines = new List<string> { $"updated {now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}, scheduler {state}" };
        foreach (var s in Enumerable.Range(1, slots).Union(running.Keys).Order())   // WT-W5: a folder above maxWorkers is listed while it runs
        {
            lines.Add(running.TryGetValue(s, out var w)
                ? string.Create(CultureInfo.InvariantCulture,
                    $"slot {s}{(w.Type.Length > 0 ? $" ({w.Type})" : "")}: {w.Card} ({AccountingRow.MinutesOf(now - w.Start)}/{WatchdogRules.Limit(w.TimeBox, config)} min){(w.StopAt is null ? "" : " stopping")}")
                : $"slot {s}: {(slotEnabled(s) ? "idle" : "disabled")}");
        }

        if (types is { Count: > 0 })
        {
            // WT-P5: "types: worker 2/2, Sonnet 0/1 (off), Opus 1/1" - running of cap, off at cap 0.
            lines.Add("types: " + string.Join(", ", types.Select(t => string.Create(CultureInfo.InvariantCulture,
                $"{t.Name} {running.Values.Count(w => Text.Same(w.Type, t.Name))}/{t.Max}{(t.Max == 0 ? " (off)" : "")}"))));
        }

        foreach (var c in cards.Where(c => c.Folder == CardFolder.Active && Text.IsMatch(c.Status, "^needs-human")))
        {
            lines.Add($"needs you: {c.Name}{Why(c)}");
        }
        var finished = new HashSet<string>(
            cards.Where(c => c.Folder == CardFolder.Done).Select(c => c.Id), Text.Comparer);
        foreach (var c in cards.Where(CardWaits.OnPlanner))
        {
            var ready = c.Depends.All(finished.Contains) ? " (its dependencies are done: the planner can act)" : "";
            lines.Add($"waits for the planner: {c.Name}{ready}");
        }
        foreach (var c in cards.Where(CardWaits.OnDeveloper))
        {
            lines.Add($"waits for you: {c.Name}{Why(c)}");
        }
        foreach (var c in cards.Where(c => c.Folder == CardFolder.Review))
        {
            lines.Add($"in review: {c.Name}{Why(c)}");
        }

        var sb = new StringBuilder();
        foreach (var l in lines)
        {
            sb.Append(l).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Why(Card c) => c.Why.Length == 0 ? "" : " - " + CardJournal.ShortText(c.Why, 200);
}
