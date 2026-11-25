// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Core.Health;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// 1308-B: every result line of a health check of main ends with the highest CPU, the lowest free memory and the
/// most workers seen while it ran; the started line has the values at the start; an unreadable load reads
/// "not known" and changes nothing else. A fake load source, a fake clock and a fake runner: no process.
/// </summary>
public sealed class HealthLoadLineTests
{
    private const long Gb = 1073741824;

    [Fact]
    public void GreenLine_EndsWithHighestCpu_LowestFree_MostWorkers()
    {
        var rig = new Rig();
        rig.Start();
        rig.Tick(cpu: 90, freeGb: 3, workers: 4);
        rig.Tick(cpu: 20, freeGb: 9, workers: 1);
        rig.Runner.Finish(0);
        rig.Load = new HealthLoadReading(5, 12 * Gb, 0);   // a quiet instant at the end hides nothing
        rig.Reap();

        Assert.Equal("health check of main abc1234: green in 2 s; load: CPU 90 %, 3.0 GB free, 4 workers", rig.Log[^1]);
        Assert.Equal(HealthResult.Green, rig.Health.LastResult);
    }

    [Fact]
    public void RedLine_EndsWithTheLoad_AndStaysRed()
    {
        var rig = new Rig();
        rig.Start();
        rig.Tick(cpu: 97.4, freeGb: 1.5, workers: 6);
        rig.Runner.Finish(1);
        rig.Reap();

        Assert.Equal(
            @"health check of main abc1234: RED in 1 s, see C:\logs\h.log; dispatching paused; load: CPU 97 %, 1.5 GB free, 6 workers",
            rig.Log[^1]);
        Assert.Equal(SchedulerState.PausedRed, rig.State);
    }

    [Fact]
    public void StartedLine_HasTheValuesAtTheStart()
    {
        var rig = new Rig();
        rig.Load = new HealthLoadReading(33, 8 * Gb, 2);
        rig.Start();

        Assert.Equal(@"health check of main abc1234 started, log C:\logs\h.log; load: CPU 33 %, 8.0 GB free, 2 workers", rig.Log[0]);
    }

    [Fact]
    public void WarningLine_EndsWithTheLoad_AndThePageWarningDoesNot()
    {
        var rig = new Rig { WarnMinutes = 1 };
        rig.Start();
        rig.Clock.Now += TimeSpan.FromMinutes(2);
        rig.Load = new HealthLoadReading(80, 2 * Gb, 3);
        rig.Reap();

        Assert.Equal("health check of main abc1234: still running after 2 min (warn at 1 min); load: CPU 80 %, 2.0 GB free, 3 workers", rig.Log[^1]);
        Assert.Equal("still running after 2 min (warn at 1 min)", rig.Health.Warning);
    }

    [Fact]
    public void UnreadableLoad_ReadsNotKnown_AndTheResultIsUnchanged()
    {
        var rig = new Rig();
        rig.Load = null;
        rig.Start();
        rig.Runner.Finish(0);
        rig.Reap();

        Assert.Equal(@"health check of main abc1234 started, log C:\logs\h.log; load: not known", rig.Log[0]);
        Assert.Equal("health check of main abc1234: green in 0 s; load: not known", rig.Log[^1]);
        Assert.Equal(HealthResult.Green, rig.Health.LastResult);
    }

    [Fact]
    public void ALoadSourceThatThrows_NeverFailsTheCheck()
    {
        var rig = new Rig { Throw = true };
        rig.Start();
        rig.Runner.Finish(1);
        rig.Reap();

        Assert.EndsWith("dispatching paused; load: not known", rig.Log[^1]);
        Assert.Equal(HealthResult.Red, rig.Health.LastResult);
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private sealed class Lines : ISchedulerLog
    {
        public List<string> All { get; } = [];

        public string this[Index i] => All[i];

        public void Write(string line) => All.Add(line);
    }

    private sealed class Machine : IHealthRunner
    {
        private int? _exit;

        public string? MainShortSha() => "abc1234";

        public string? Start(string sha, string command) => @"C:\logs\h.log";

        public bool IsRunning() => _exit is null;

        public int? ExitCode() => _exit;

        public void Kill() => _exit = -1;

        public void Finish(int code) => _exit = code;
    }

    private sealed class Rig
    {
        private HealthCheck? _health;

        public Clock Clock { get; } = new();

        public Lines Log { get; } = new();

        public Machine Runner { get; } = new();

        public HealthCheck Health => _health!;

        public HealthLoadReading? Load { get; set; } = new HealthLoadReading(10, 10 * Gb, 1);

        public bool Throw { get; init; }

        public int WarnMinutes { get; init; }

        public SchedulerState State { get; private set; } = SchedulerState.Running;

        public void Start()
        {
            _health = new HealthCheck(
                new SchedulerConfig { HealthCommand = "gate", HealthWarnMinutes = WarnMinutes },
                Clock,
                Log,
                Runner,
                () => Throw ? throw new InvalidOperationException("no counters") : Load);
            _health.RunFinished();
            Assert.True(_health.StartIfWanted());
        }

        public void Tick(double cpu, double freeGb, int workers)
        {
            Clock.Now += TimeSpan.FromSeconds(1);
            Load = new HealthLoadReading(cpu, (long)(freeGb * Gb), workers);
            Reap();
        }

        public void Reap() => State = Health.Reap(State);
    }
}
