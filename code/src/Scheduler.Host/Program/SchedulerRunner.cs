// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Tick;

namespace Scheduler.Host.Program;

/// <summary>
/// The program (A1, A3, A12): one scheduler per project (CFG-14), the start-up order (CFG-15), the
/// loop that calls the tick, dry run (DISP-21) and exit when draining with no workers (PAGE-12).
/// </summary>
public sealed class SchedulerRunner(RunnerParts parts)
{
    /// <summary>The longest wait between two looks at the stop flags when nothing wakes the loop.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

    /// <summary>966-B: the wait while the page is in fast mode (about 30 a second).</summary>
    public static readonly TimeSpan FastStep = TimeSpan.FromMilliseconds(33);

    /// <summary>966-B: a tick longer than this is logged with its slowest step at once.</summary>
    public static readonly TimeSpan SlowTick = TimeSpan.FromSeconds(2);

    /// <summary>966-B: otherwise the tick cost is logged once a minute.</summary>
    public static readonly TimeSpan CostEvery = TimeSpan.FromMinutes(1);

    private readonly TickCost _cost = new();
    private readonly Dictionary<TickWake, int> _wakes = [];
    private volatile bool _tickAsked;
    private CancellationToken _stop;
    private DateTimeOffset? _costLogged;
    private (long Git, double ModelMs)? _tickCounters;

    /// <summary>Ask for a tick at once (the page's tick control and its actions).</summary>
    public void RequestTick()
    {
        _tickAsked = true;
        parts.Wake?.Invoke();
    }

    /// <summary>
    /// 966-B: called by the tick before each of its steps (<c>SchedulerTick.StepStarting</c>). It times the
    /// steps, and is the tick's safe point: between steps no git write is under way, so an exit asked for
    /// during a tick ends the tick here rather than after its last step.
    /// </summary>
    public void StepStarting(string step)
    {
        _cost.Step(step, parts.Clock.Now);
        if (ExitAsked())
        {
            throw new TickStoppedException(step);
        }
    }

    /// <summary>
    /// Run until <paramref name="stop"/>, the end of a dry run's first tick, or a drained scheduler.
    /// Throws <see cref="SchedulerStartException"/> when it cannot start.
    /// </summary>
    public void Run(CancellationToken stop = default)
    {
        var config = parts.Config;
        using var instance = SingleInstance.Acquire(config.Project, parts.MutexWait);
        IPageListener? listener = null;
        try
        {
            parts.AdoptWorkers();
            parts.SeedFromAccounting();
            listener = parts.StartListener();
            var url = $"http://localhost:{listener.Port}/";
            parts.Log.Write(
                $"scheduler-next {parts.Version} started for {config.Project}: {config.Slots} slot(s), page {url}"
                + (config.DryRun ? " (dry run)" : ""));
            if (!config.DryRun && !parts.NoBrowser)
            {
                parts.OpenBrowser(url);
            }

            Loop(stop);
        }
        finally
        {
            listener?.Stop();
            listener?.Dispose();
            parts.WriteStatusFile();
        }
    }

    private bool ExitAsked() =>
        _stop.IsCancellationRequested
        || parts.ShutdownRequested?.Invoke() == true
        || (parts.Draining() && parts.WorkersRunning() == 0);

    private void Loop(CancellationToken stop)
    {
        _stop = stop;
        DateTimeOffset? lastTick = null;
        var signalled = false;
        while (!stop.IsCancellationRequested)
        {
            // 670-B: the tray's Restart and "Exit, leave workers running" ask for this rather than send a
            // Ctrl+C, which would go to every process on the console - the workers among them.
            if (parts.ShutdownRequested?.Invoke() == true)
            {
                parts.Log.Write("shutdown asked for: the scheduler ends; its workers keep running and the next one adopts them.");
                return;
            }

            var poll = TimeSpan.FromSeconds(Math.Max(1, parts.PollSeconds?.Invoke() ?? parts.Config.PollSeconds));
            var due = lastTick is null || parts.Clock.Now - lastTick >= poll;
            var stopped = false;
            var asked = _tickAsked;
            var pending = parts.TakeWake?.Invoke();
            if (due || asked || pending is not null || (signalled && parts.TakeWake is null))
            {
                _tickAsked = false;
                signalled = false;
                var wake = Kind(due, asked, pending);
                _wakes[wake] = _wakes.GetValueOrDefault(wake) + 1;
                _cost.Begin(parts.Clock.Now);
                var counters = parts.Counters?.Invoke();
                try
                {
                    parts.Tick(wake);
                }
                catch (TickStoppedException e)
                {
                    parts.Log.Write($"exit asked for during a tick: it stopped before its step {e.Step}.");
                    stopped = true;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    parts.Log.Write($"tick error: {e.Message}");
                }

                lastTick = parts.Clock.Now;
                _tickCounters = counters is { } before && parts.Counters?.Invoke() is { } after
                    ? (after.Git - before.Git, after.ModelMs - before.ModelMs)
                    : null;
                LogCost(_cost.End(lastTick.Value));
                if (parts.Config.DryRun)
                {
                    return; // DISP-21
                }
            }

            if (parts.Draining() && parts.WorkersRunning() == 0)
            {
                parts.Log.Write("drained: no workers left; scheduler exiting.");
                return; // PAGE-12
            }

            if (stopped)
            {
                continue;
            }

            signalled = parts.WaitForTick(parts.Fast?.Invoke() == true ? FastStep : Step, stop);
        }
    }

    /// <summary>
    /// 982-B: the kind of this tick: the signalled kinds, merged with a click when the page asked and
    /// with the poll when it is due, so any full kind makes the tick full; a signal of no known kind is the poll.
    /// </summary>
    private static TickWake Kind(bool due, bool asked, TickWake? pending)
    {
        var kind = pending;
        if (asked)
        {
            kind = TickWakes.Merge(kind, TickWake.Click);
        }

        if (due)
        {
            kind = TickWakes.Merge(kind, TickWake.Poll);
        }

        return kind ?? TickWake.Poll;
    }

    private void LogCost(TickCost.Summary cost)
    {
        var now = parts.Clock.Now;
        if (cost.Took <= SlowTick && _costLogged is { } last && now - last < CostEvery)
        {
            return;
        }

        _costLogged = now;
        _cost.Logged();
        var wakes = string.Join(", ", _wakes.OrderBy(w => w.Key).Select(w => $"{w.Key} {w.Value}"));
        _wakes.Clear();
        parts.Log.Write(
            $"tick took {cost.Took.TotalSeconds:0.00}s ({cost.Ticks} tick(s) since the last such line, longest {cost.Longest.TotalSeconds:0.00}s)"
            + (cost.Slowest.Length > 0 ? $"; slowest step {cost.Slowest} {cost.SlowestTook.TotalSeconds:0.00}s" : "")
            + (_tickCounters is { } c ? string.Create(CultureInfo.InvariantCulture, $"; git {c.Git}, model {c.ModelMs:0.0}ms") : "")
            + (wakes.Length > 0 ? $"; woken by {wakes}" : "") + ".");
    }

    /// <summary>The tick was ended at a safe point because the scheduler is exiting.</summary>
    private sealed class TickStoppedException(string step) : Exception($"stopped before {step}")
    {
        public string Step { get; } = step;
    }
}
