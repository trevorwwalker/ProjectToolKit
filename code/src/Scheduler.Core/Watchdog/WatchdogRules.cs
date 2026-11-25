// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Watchdog;

/// <summary>
/// The watchdog (A6), pure: one decision per running worker per tick, from the clock and the raw
/// log's size. Ports <c>Card-Limit</c> L697, <c>Warn-Lead</c> L701, <c>Request-Stop</c> L708,
/// <c>Kill-Worker</c> L746 and <c>Watch</c> L780-803. The host writes the files and kills.
/// </summary>
public static class WatchdogRules
{
    private const double AbandonMinutes = 2;

    /// <summary>The card's limit in minutes: its Time box, else maxMinutes; 0 is unset (TIME-1).</summary>
    public static int Limit(int? timeBox, SchedulerConfig cfg) =>
        timeBox is > 0 ? timeBox.Value : cfg.MaxMinutes;

    /// <summary>The warning lead: min(warnMinutes, floor(limit / 4)) (TIME-2).</summary>
    public static int WarnLead(int limit, SchedulerConfig cfg) => Math.Min(cfg.WarnMinutes, limit / 4);

    /// <summary>One watchdog step for a worker that is alive (TIME-3 to TIME-9).</summary>
    public static WatchResult Watch(RunningWorker w, SchedulerConfig cfg, DateTimeOffset now, long logSize)
    {
        if (logSize != w.LastSize) w = w with { LastSize = logSize, LastProgress = now };
        var quiet = (now - w.LastProgress).TotalMinutes;
        var age = (now - w.Start).TotalMinutes;
        var stopping = w.StopAt is not null;
        var grace = w.StopGraceMinutes != 0 ? w.StopGraceMinutes : cfg.StopGraceMinutes;
        var limit = Limit(w.TimeBox, cfg);
        var lead = WarnLead(limit, cfg);
        var log = new List<string>();
        string? warn = null;

        if (!stopping && age >= limit - lead && age < limit && !w.Warned)
        {
            var deadline = w.Start.AddMinutes(limit).ToString("HH:mm", CultureInfo.InvariantCulture);
            var left = (int)Math.Ceiling(limit - age);
            warn = $"About {left} minutes are left of this card's {limit}-minute limit (deadline {deadline}). " +
                   "Stop investigating now: journal what you have in the card's Result, push, and finish or park the card as the README says. " +
                   $"At {deadline} the stop file appears, and the session is ended {cfg.StopGraceMinutes} minutes after that.";
            log.Add($"slot {w.Slot} warned ({w.Card}): {left} min left of {limit}");
            w = w with { Warned = true };
        }

        WatchResult Done(WatchAction action, string reason, RunningWorker worker, IEnumerable<string>? more = null) => new()
        {
            Worker = worker,
            Action = action,
            Reason = reason,
            WarnText = warn,
            Quiet = quiet,
            Log = [.. log, .. more ?? []],
        };

        if (stopping && (now - w.StopAt!.Value).TotalMinutes >= grace)
            return Done(WatchAction.Kill, $"still running {grace} min after a stop request ({w.StopWhy})", w);
        if (age >= limit + cfg.StopGraceMinutes)
            return Done(WatchAction.Kill, $"over the {limit} min limit", w);
        if (!stopping && age >= limit)
        {
            var s = RequestStop(w, $"over the {limit} min limit", cfg.StopGraceMinutes, now);
            return Done(WatchAction.RequestStop, s.Reason, s.Worker, s.Log);
        }
        if (!stopping && quiet >= cfg.StallMinutes)
        {
            var s = RequestStop(w, $"no output for {(int)quiet} min", cfg.StopGraceMinutes, now);
            return Done(WatchAction.RequestStop, s.Reason, s.Worker, s.Log);
        }
        return Done(WatchAction.None, "", w);
    }

    /// <summary>
    /// A stop request (TIME-9): records the time, reason and grace; on a worker already stopping it
    /// only shortens the grace.
    /// </summary>
    public static WatchResult RequestStop(RunningWorker w, string why, int graceMinutes, DateTimeOffset now)
    {
        if (w.StopAt is not null)
        {
            if (graceMinutes < w.StopGraceMinutes)
                return new()
                {
                    Worker = w with { StopGraceMinutes = graceMinutes },
                    Log = [$"slot {w.Slot} stop deadline moved to {graceMinutes} min ({why})"],
                };
            return new() { Worker = w };
        }
        return new()
        {
            Worker = w with { StopAt = now, StopWhy = why, StopGraceMinutes = graceMinutes },
            Action = WatchAction.RequestStop,
            Reason = why,
        };
    }

    /// <summary>
    /// A kill (TIME-11): the first records the time and keeps an existing reason; a later one, two
    /// minutes on, abandons a worker that did not end.
    /// </summary>
    public static WatchResult Kill(RunningWorker w, string why, DateTimeOffset now)
    {
        if (w.KillTime is { } at)
        {
            if ((now - at).TotalMinutes >= AbandonMinutes && !w.Abandoned)
                return new()
                {
                    Worker = w with { Abandoned = true },
                    Log = [$"slot {w.Slot}: pid {w.Pid} ({w.Card}) did not end when killed; slot released. Check it with: Get-Process -Id {w.Pid}"],
                };
            return new() { Worker = w };
        }
        return new()
        {
            Worker = w with { KillTime = now, StopWhy = w.StopWhy.Length == 0 ? why : w.StopWhy },
            Action = WatchAction.Kill,
            Reason = why,
            Log = [$"slot {w.Slot} killed ({w.Card}): {why}; its card stays in active/ and will be resumed"],
        };
    }
}
