// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Pool;
using Xunit;

namespace Scheduler.Host.Tests.Pool;

/// <summary>1161-B: the foreground lock workaround, on a fake: no test calls the real set.</summary>
public class ForegroundLockFixTests
{
    private sealed class Log : ISchedulerLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string line)
        {
            lock (Lines)
            {
                Lines.Add(line);
            }
        }
    }

    private sealed class Fake(uint value, bool accepts = true, int error = 5) : IForegroundLockApi
    {
        public uint Value { get; private set; } = value;

        public int Sets { get; private set; }

        public uint? Get(out int e)
        {
            e = 0;
            return Value;
        }

        public bool Set(uint v, out int e)
        {
            Sets++;
            e = accepts ? 0 : error;
            if (accepts)
            {
                Value = v;
            }

            return accepts;
        }
    }

    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }

    private sealed class Tokens(long? n) : ITokenCount
    {
        public long? Live() => n;
    }

    [Fact]
    public void SetsTheValueToZero_WhenItIsNot()
    {
        var api = new Fake(2147483647);
        var log = new Log();
        new ForegroundLockFix(api, log).Apply();
        Assert.Equal(0u, api.Value);
        Assert.Equal(["foreground lock timeout: 2147483647 -> 0 (live only, nothing written to the registry)"], log.Lines);
    }

    [Fact]
    public void LeavesItAlone_WhenAlreadyZero()
    {
        var api = new Fake(0);
        var log = new Log();
        new ForegroundLockFix(api, log).Apply();
        Assert.Equal(0, api.Sets);
        Assert.Equal(["foreground lock timeout: already 0"], log.Lines);
    }

    [Fact]
    public void LogsARefusal_AndGoesOn()
    {
        var api = new Fake(200000, accepts: false, error: 5);
        var log = new Log();
        new ForegroundLockFix(api, log).Apply();
        Assert.Equal(200000u, api.Value);
        Assert.Contains("refused (Win32 error 5)", log.Lines.Single());
    }

    [Fact]
    public void WhenSwitchedOff_ReadsAndLogs_ChangesNothing()
    {
        var api = new Fake(2147483647);
        var log = new Log();
        new ForegroundLockFix(api, log, enabled: false).Apply();
        Assert.Equal(0, api.Sets);
        Assert.Contains("left alone", log.Lines.Single());
    }

    [Fact]
    public void TheHourlyCheck_ReappliesAfterWindowsPutsTheValueBack_AndIsQuietOtherwise()
    {
        var api = new Fake(0);
        var log = new Log();
        var check = new ProcessStartCheck(new Clock(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero)), log, _ => 400, new ForegroundLockFix(api, log), new Tokens(1234));
        check.Tick();
        check.Running?.Join(TimeSpan.FromSeconds(90));
        Assert.Equal(["process starts: 400/s, tokens: 1234 live"], log.Lines);

        log.Lines.Clear();
        var again = new Fake(2147483647);
        var second = new ProcessStartCheck(new Clock(new DateTimeOffset(2026, 11, 23, 13, 0, 0, TimeSpan.Zero)), log, _ => 400, new ForegroundLockFix(again, log), new Tokens(null));
        second.Tick();
        second.Running?.Join(TimeSpan.FromSeconds(90));
        Assert.Equal(0u, again.Value);
        Assert.Equal(2, log.Lines.Count);
        Assert.StartsWith("foreground lock timeout: 2147483647 -> 0", log.Lines[0]);
        Assert.Equal("process starts: 400/s", log.Lines[1]);
    }

    [Fact]
    public void PoolFile_SwitchDefaultsOn_AndFalseTurnsItOff()
    {
        Assert.True(PoolConfig.Parse("{}").ForegroundLockFix);
        Assert.False(PoolConfig.Parse("{\"foregroundLockFix\": false}").ForegroundLockFix);
    }
}
