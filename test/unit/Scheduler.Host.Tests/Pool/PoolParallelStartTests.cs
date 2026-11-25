// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Diagnostics;
using Scheduler.Host.Config;
using Scheduler.Core.Tick;
using Scheduler.Host.Pool;
using Scheduler.Host.Tests.Tick;
using Scheduler.Web.Views;
using Xunit;

namespace Scheduler.Host.Tests.Pool;

/// <summary>
/// 1015-B (MP-H1, MP-H2): a pool start where the projects join together, each on a task of its own, the page
/// is served before any has joined (each shown as starting), a project that fails to join holds up no other,
/// and each project's first snapshot is published as soon as its first tick has read the cards.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[ParallelMethods]
public class PoolParallelStartTests
{
    // 1026-B: a fallback that turns a hang into a failing assert; 30 s was within reach of a loaded PC.
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(10); // 1051-B: hang caps only; every use waits on an event

    // 1015-B: two projects with a slow join step join in about the time of the slower one, not the sum: each
    // project's slow step waits until the other's has started too, which only joins that run together pass.
    [Fact]
    public void MP_H1_TwoSlowJoins_RunTogether()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var both = new CountdownEvent(2);
        var met = 0;
        ConfigLoadResult Slow(string repo, Func<string, ConfigLoadResult> load)
        {
            both.Signal();
            if (both.Wait(Long))
            {
                _ = Interlocked.Increment(ref met);
            }

            Thread.Sleep(TimeSpan.FromSeconds(1));
            return load(repo);
        }

        var watch = Stopwatch.StartNew();
        using var pool = rig.Compose(load: Slow);
        watch.Stop();

        Assert.Equal(2, met);
        Assert.Equal([rig.A, rig.B], pool.Tick.Projects.Select(p => p.Name));
        Assert.True(watch.Elapsed < Long, $"the start took {watch.Elapsed}");
        Assert.Contains(rig.Log.Lines, l => l.Contains($"pool: {rig.A} joined", StringComparison.Ordinal) && l.EndsWith('s'));
        Assert.Contains(rig.Log.Lines, l => l.Contains("pool: start took", StringComparison.Ordinal));
    }

    // 1015-B: the page answers before either project has joined, and shows both as starting; once they join
    // their rows are their own.
    [Fact]
    public void MP_P1_PageAnswersBeforeAnyJoin_ShowsThemStarting()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var gate = new ManualResetEventSlim();
        ConfigLoadResult Held(string repo, Func<string, ConfigLoadResult> load)
        {
            _ = gate.Wait(Long);
            return load(repo);
        }

        // The pool is composed, run and let go on one thread, as the program does (its mutexes are that thread's).
        using var stop = new CancellationTokenSource();
        using var composed = new ManualResetEventSlim();
        PoolComposition? pool = null;
        IReadOnlyList<string> startingAtFirst = [];
        Exception? failed = null;
        var runner = new Thread(() =>
        {
            try
            {
                using var p = rig.Compose(wait: false, load: Held, dryRun: false);
                startingAtFirst = p.Starting;
                pool = p;
                composed.Set();
                p.Runner.Run(stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // 1026-B: the runner's wait ends a cancelled run this way; whether the
                // test's stop lands in a tick or in that wait is down to timing, so a loaded PC failed it.
            }
            catch (Exception e)
            {
                failed = e;
                composed.Set();
            }
        });
        runner.Start();
        try
        {
            Assert.True(composed.Wait(Long));
            Assert.Null(failed);
            Assert.Equal([rig.A, rig.B], startingAtFirst);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            // 1279-B: "starting..." and the project rows are the Workers view's; since 3.4.28 (1269-C) a bare "/" is Plans.
            var url = $"http://localhost:{PoolCompositionTests.PoolRig.PagePort(pool!, () => failed)}/?view=workers";
            var page = Get(http, url, html => html.Contains("starting...", StringComparison.Ordinal));
            Assert.Contains($"data-project='{rig.A}' data-starting='1'", page, StringComparison.Ordinal);
            Assert.Contains($"data-project='{rig.B}' data-starting='1'", page, StringComparison.Ordinal);

            gate.Set();
            // 1051-B: waits for both joined rows (the event this step tests), not only for "starting..." to go; on a
            // loaded PC a read in between failed. The pool's log is in the message if a join was left out.
            bool Joined(string html) => !html.Contains("starting...", StringComparison.Ordinal)
                && html.Contains($"data-project='{rig.A}'", StringComparison.Ordinal)
                && html.Contains($"data-project='{rig.B}'", StringComparison.Ordinal);
            page = Get(http, url, Joined, () => string.Join(" | ", rig.Log.Lines.Where(l => l.Contains("pool", StringComparison.Ordinal))));
            Assert.True(Joined(page));
        }
        finally
        {
            gate.Set();
            stop.Cancel();
            Assert.True(runner.Join(Long));
        }

        Assert.Null(failed);
    }

    // 1015-B, 1013-B: a project whose join hangs and then fails holds up no other: the other joins and ticks,
    // and the failed one is left out and tried again as before.
    [Fact]
    public void MP_H1_FailedJoin_DoesNotHoldUpTheOther()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var gate = new ManualResetEventSlim();
        ConfigLoadResult FailA(string repo, Func<string, ConfigLoadResult> load)
        {
            if (!rig.IsA(repo))
            {
                return load(repo);
            }

            _ = gate.Wait(Long);
            return new ConfigLoadResult { Ok = false, Error = "broken on purpose" };
        }

        using var pool = rig.Compose(wait: false, load: FailA);
        var watch = Stopwatch.StartNew();
        while (!pool.Tick.Projects.Any(p => p.Name == rig.B) && watch.Elapsed < Long)
        {
            _ = pool.WaitForJoins(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal([rig.B], pool.Tick.Projects.Select(p => p.Name));
        Assert.Equal([rig.A], pool.Starting);

        gate.Set();
        Assert.True(pool.WaitForJoins(Long));
        Assert.Equal([rig.B], pool.Tick.Projects.Select(p => p.Name));
        Assert.Contains("broken on purpose", pool.LeftOut[rig.A], StringComparison.Ordinal);
        Assert.Empty(pool.Starting);
    }

    // 1050-B (MP-H1): while the start's joins are under way, a tick takes in the finished ones but runs no project's
    // tick, so a slow first tick (thousands of cards on a busy PC) never keeps a finished join waiting to come in.
    [Fact]
    public void MP_H1_JoinedProject_DoesNotTick_WhileTheStartsJoinsRun()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var gate = new ManualResetEventSlim();
        using var pool = rig.Compose(wait: false, load: (repo, load) => Held(rig, gate, repo, load));
        var b = WaitForB(rig, pool);
        var steps = 0;
        b.Tick.StepStarting += _ => Interlocked.Increment(ref steps);

        pool.RunTick(TickWake.Click);
        Assert.Equal(0, steps);

        gate.Set();
        Assert.True(pool.WaitForJoins(Long));
        pool.RunTick(TickWake.Click);
        Assert.True(steps > 0, "the projects tick once every join is in");
        Assert.Contains(rig.Log.Lines, l => l.Contains("pool: start took", StringComparison.Ordinal));
    }

    // 1050-B: the hold is bounded: a join that hangs past it no longer holds up the projects that joined.
    [Fact]
    public void MP_H1_StartHold_EndsAfterItsTime()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var gate = new ManualResetEventSlim();
        using var pool = rig.Compose(wait: false, load: (repo, load) => Held(rig, gate, repo, load), startHold: TimeSpan.Zero);
        var b = WaitForB(rig, pool);
        var steps = 0;
        b.Tick.StepStarting += _ => Interlocked.Increment(ref steps);

        pool.RunTick(TickWake.Click);

        Assert.True(steps > 0, "past the hold a joined project ticks while another still joins");
        gate.Set();
        Assert.True(pool.WaitForJoins(Long));
    }

    // 1050-B: each join logs where its time went, step by step, including the wait for the loop to take it in.
    [Fact]
    public void MP_H1_Join_LogsItsSteps()
    {
        using var rig = new PoolCompositionTests.PoolRig();
        using var pool = rig.Compose();

        var line = Assert.Single(rig.Log.Lines, l => l.Contains($"pool: {rig.A} join steps:", StringComparison.Ordinal));
        foreach (var step in new[] { "config", "log", "parts", "waited for the loop", "workers adopted", "accounting", "snapshot" })
        {
            Assert.Contains(step, line, StringComparison.Ordinal);
        }
    }

    private static ConfigLoadResult Held(PoolCompositionTests.PoolRig rig, ManualResetEventSlim gate, string repo, Func<string, ConfigLoadResult> load)
    {
        if (rig.IsA(repo))
        {
            _ = gate.Wait(Long);
        }

        return load(repo);
    }

    private static Scheduler.Host.Program.ProjectParts WaitForB(PoolCompositionTests.PoolRig rig, PoolComposition pool)
    {
        var watch = Stopwatch.StartNew();
        while (!pool.Tick.Projects.Any(p => p.Name == rig.B) && watch.Elapsed < Long)
        {
            _ = pool.WaitForJoins(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal([rig.A], pool.Starting);
        return pool.Projects.Single();
    }

    // 1015-B: the first snapshot is published as soon as the first tick has read the cards, before the tick's
    // later (slow) steps finish, not when the whole pool tick ends.
    [Fact]
    public void MP_P8_FirstSnapshot_PublishedBeforeTheTicksSlowSteps()
    {
        using var rig = new PoolCompositionTests.PoolRig(onlyA: true);
        using var pool = rig.Compose();
        var parts = pool.Projects.Single();
        string? seen = null;
        parts.Tick.StepStarting += step =>
        {
            if (step == TickStep.WriteStatus && seen is null)
            {
                seen = new CombinedPageRenderer().Render(pool.PageData.PoolSnapshot(), remote: false, controlsAllowed: true, "workers");
            }
        };
        Assert.DoesNotContain(TickRig.CardName, new CombinedPageRenderer().Render(pool.PageData.PoolSnapshot(), false, true, "workers"), StringComparison.Ordinal);

        pool.RunTick(TickWake.Click);

        Assert.NotNull(seen);
        Assert.Contains(TickRig.CardName, seen, StringComparison.Ordinal);
    }

    // The page, read until it says what the test waits for (the server and the joins run on their own threads).
    private static string Get(HttpClient http, string url, Func<string, bool> until, Func<string>? why = null)
    {
        var watch = Stopwatch.StartNew();
        var last = "";
        while (watch.Elapsed < Long)
        {
            try
            {
                last = http.GetStringAsync(new Uri(url)).GetAwaiter().GetResult();
                if (until(last))
                {
                    return last;
                }
            }
            catch (HttpRequestException)
            {
                // not listening yet
            }
            catch (TaskCanceledException)
            {
                // 1026-B: one read slower than the client's timeout on a loaded PC; read again until Long
            }

            Thread.Sleep(100);
        }

        Assert.Fail($"the page never showed what was waited for; last read {last.Length} chars; {why?.Invoke()}");
        return last;
    }
}
