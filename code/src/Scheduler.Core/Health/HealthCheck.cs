// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Globalization;
using Scheduler.Core.Contracts;

namespace Scheduler.Core.Health;

/// <summary>
/// The health check's decisions (HLTH-1, HLTH-2, HLTH-4, HLTH-5, HLTH-7; schedule.ps1
/// <c>Start-Health</c> L1695-1719 and <c>Reap-Health</c> L1721-1743): when a check of main is
/// wanted, when it may start, what a hanging check does, and what green and red do to the
/// scheduler's state. Running the command is Host's, through <see cref="IHealthRunner"/> (270-B);
/// Core starts and kills nothing itself, and never runs a card's gates (HLTH-7).
/// </summary>
public sealed class HealthCheck(SchedulerConfig config, IClock clock, ISchedulerLog log, IHealthRunner runner, Func<HealthLoadReading?>? loadNow = null)
{
    /// <summary>
    /// A check of main is wanted, so the next tick starts one (HLTH-1, L2918). Nothing wants one at
    /// start-up (L1693).
    /// </summary>
    public bool Wanted { get; private set; }

    /// <summary>The short sha the last finished check covered (L1732); "" until then.</summary>
    public string LastSha { get; private set; } = "";

    /// <summary>What the last finished check ended as (HLTH-5).</summary>
    public HealthResult LastResult { get; private set; } = HealthResult.None;

    /// <summary>When the last check finished, for the page's health line (L3543).</summary>
    public DateTimeOffset? LastWhen { get; private set; }

    /// <summary>The short sha the running check covers; "" when none runs.</summary>
    public string Sha { get; private set; } = "";

    /// <summary>When the running check started, which the kill limit is measured from (L1725).</summary>
    public DateTimeOffset? Started { get; private set; }

    /// <summary>The file the running check writes its output to (HLTH-3).</summary>
    public string LogPath { get; private set; } = "";

    /// <summary>One check is running: the ps1's <c>$health.Proc</c> (L1696, L1722).</summary>
    public bool Running => Started is not null;

    /// <summary>
    /// 1100-B (HLTH-11): what the page's health line says while the running check has passed
    /// <c>limits.healthWarnMinutes</c>, else null. Set once per check, cleared when it ends; the
    /// warning never stops or fails the check.
    /// </summary>
    public string? Warning { get; private set; }

    /// <summary>1100-B: whether this check's time-box warning has been logged (once per check).</summary>
    private bool _warned;

    /// <summary>1308-B: the load seen while the running check runs.</summary>
    private HealthLoadTrack _load = new();

    /// <summary>1308-B: takes one reading of the load; no source = no load text (older callers), a failing one reads as not known.</summary>
    private void Observe()
    {
        if (loadNow is null)
        {
            return;
        }

        try
        {
            _load.Add(loadNow());
        }
        catch (Exception)
        {
            // a health check never fails or waits because of the load
        }
    }

    private string LoadText => loadNow is null ? "" : _load.Text;

    /// <summary>HLTH-1 (L2843): every finished run wants a check of main, whatever it ended as.</summary>
    public void RunFinished() => Wanted = true;

    /// <summary>
    /// HLTH-1 (L3856): the page's Check main wants a check and forgets the last checked sha, so
    /// main is checked again even when it has not moved.
    /// </summary>
    public void CheckNow()
    {
        LastSha = "";
        Wanted = true;
    }

    /// <summary>
    /// Start-Health (HLTH-2, L1695-1717): nothing starts when no command is configured, this is a
    /// dry run, one is already running, or main's short sha equals the last checked sha (which
    /// clears wanted, L1698). Otherwise the check starts against the sha and is logged (L1718).
    /// </summary>
    /// <returns>True when a check started.</returns>
    public bool StartIfWanted()
    {
        // The tick asks only when a check is wanted (L2918); the reasons below are L1696.
        if (!Wanted || string.IsNullOrEmpty(config.HealthCommand) || config.DryRun || Running)
        {
            return false;
        }

        var sha = runner.MainShortSha();
        if (string.IsNullOrEmpty(sha))
        {
            return false;   // git gave nothing: nothing is checked, and it is still wanted
        }

        if (Text.Same(sha, LastSha))
        {
            Wanted = false;
            return false;
        }

        var path = runner.Start(sha, config.HealthCommand);
        if (path is null)
        {
            return false;   // it could not be started: it stays wanted for the next tick
        }

        Sha = sha;
        Started = clock.Now;
        LogPath = path;
        Warning = null; // 1100-B: a new check starts with nothing to warn about
        _warned = false;
        Wanted = false;
        _load = new HealthLoadTrack();
        Observe();
        log.Write($"health check of main {sha} started, log {path}{LoadText}");
        return true;
    }

    /// <summary>
    /// Reap-Health (HLTH-4, HLTH-5, L1721-1743): a check still running before maxMinutes is left
    /// alone; at maxMinutes it is killed and treated as red. A finished check sets the last sha,
    /// result and time, and turns <c>running</c> to <c>paused-red</c> on red or <c>paused-red</c>
    /// back to <c>running</c> on green. Workers already running are never touched.
    /// </summary>
    /// <param name="state">The scheduler's state; the decision is made against it.</param>
    /// <returns>The state to keep.</returns>
    public SchedulerState Reap(SchedulerState state)
    {
        if (!Running)
        {
            return state;
        }

        if (runner.IsRunning())
        {
            Observe();
            var ran = clock.Now - Started!.Value;
            WarnIfLate(ran);

            // A gate that hangs must not hold the red/green state for ever (L1724-1729).
            if (ran.TotalMinutes < config.MaxMinutes)
            {
                return state;
            }

            runner.Kill();
            log.Write($"health check of main {Sha} killed after {config.MaxMinutes} min; treated as red");
            return Finish(false, state);
        }

        return Finish(runner.ExitCode() == 0, state);
    }

    /// <summary>
    /// The page's Resume (MOVE-8, L3855) as far as the health check is concerned: a paused state
    /// (including <c>paused-red</c>) goes back to <c>running</c> without a green check, and every
    /// card's retry count is cleared. The red banner stays, because the same sha is not re-checked
    /// (HLTH-2).
    /// Spec C5: today's behaviour, kept until the developer decides (003-D review).
    /// </summary>
    /// <param name="state">The scheduler's state.</param>
    /// <param name="retries">The retry counts by card id, cleared here (L3855).</param>
    /// <returns>The state to keep.</returns>
    public SchedulerState Resume(SchedulerState state, IDictionary<string, int> retries)
    {
        if (state is not (SchedulerState.Paused or SchedulerState.PausedRed or SchedulerState.PausedQuota))
        {
            return state;   // /resume only acts on paused* (Spec C6)
        }

        retries.Clear();
        log.Write("resumed from the page");
        return SchedulerState.Running;
    }

    /// <summary>The end of a check (L1732-1743), shared by the exit and the kill.</summary>
    private SchedulerState Finish(bool ok, SchedulerState state)
    {
        // 1100-B: the result line always carries how long the check took, so the durations are in the log.
        var took = Started is { } start
            ? string.Format(CultureInfo.InvariantCulture, " in {0:0} s", (clock.Now - start).TotalSeconds)
            : "";
        Observe();
        LastSha = Sha;
        LastResult = ok ? HealthResult.Green : HealthResult.Red;
        LastWhen = clock.Now;
        Started = null;
        Warning = null;

        if (ok)
        {
            log.Write($"health check of main {Sha}: green{took}{LoadText}");
            if (state == SchedulerState.PausedRed)
            {
                log.Write("main is green again: dispatching resumed");
                return SchedulerState.Running;
            }

            return state;
        }

        log.Write($"health check of main {Sha}: RED{took}, see {LogPath}; dispatching paused{LoadText}");
        return state == SchedulerState.Running ? SchedulerState.PausedRed : state;
    }

    /// <summary>
    /// 1100-B (HLTH-11): once a running check passes <c>limits.healthWarnMinutes</c>, say so - one log
    /// line, and <see cref="Warning"/> for the page's health line until the check ends. 0 turns it off.
    /// The check is left to run either way; only <c>limits.maxMinutes</c> kills it.
    /// </summary>
    private void WarnIfLate(TimeSpan ran)
    {
        if (_warned || config.HealthWarnMinutes <= 0 || ran.TotalMinutes < config.HealthWarnMinutes)
        {
            return;
        }

        _warned = true;
        Warning = string.Format(
            CultureInfo.InvariantCulture,
            "still running after {0:0} min (warn at {1:0} min)",
            (int)ran.TotalMinutes,
            config.HealthWarnMinutes);
        log.Write($"health check of main {Sha}: {Warning}{LoadText}");
    }
}
