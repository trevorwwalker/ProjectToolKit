// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Host.Pool;
using Xunit;

namespace Scheduler.Host.Tests.Pool;

/// <summary>1078-B: the hourly process-start check: the log line, the threshold, once an hour, and never on the tick's thread.</summary>
public class ProcessStartCheckTests
{
    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }

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

    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 14, 0, 0, TimeSpan.Zero);

    private static void Finish(ProcessStartCheck c) => c.Running?.Join(TimeSpan.FromSeconds(90));

    [Fact]
    public void AtTheStart_LogsTheRate_AndDoesNotWarn()
    {
        var log = new Log();
        var check = new ProcessStartCheck(new Clock(T0), log, n => { Assert.Equal(ProcessStartCheck.Starts, n); return 470.4; });
        check.Tick();
        Finish(check);
        Assert.Equal(["process starts: 470/s"], log.Lines);
        Assert.Null(check.Warning);
    }

    private sealed class Tokens(long? live) : ITokenCount
    {
        public long? Value { get; set; } = live;

        public long? Live() => Value;
    }

    [Fact]
    public void SlowRate_AloneIsNoWarning()
    {
        var log = new Log();
        var check = new ProcessStartCheck(new Clock(T0), log, _ => 20, tokens: new Tokens(5000));
        check.Tick();
        Finish(check);
        Assert.Equal(["process starts: 20/s, tokens: 5000 live"], log.Lines);
        Assert.Null(check.Warning);
    }

    [Fact]
    public void AtTheTokenThreshold_WarnsInTheLogAndTheStatusText()
    {
        var log = new Log();
        var check = new ProcessStartCheck(new Clock(T0), log, _ => 456, tokens: new Tokens(ProcessStartCheck.WarnTokensAt));
        check.Tick();
        Finish(check);
        const string text = "Windows has leaked 250,000 process tokens; process starts get slower as they pile up, and a Restart (not Shut down) clears them";
        Assert.Equal(text, check.Warning);
        Assert.Equal($"process starts: 456/s, tokens: 250000 live - warning: {text}", log.Lines.Single());
    }

    [Fact]
    public void JustUnderTheTokenThreshold_DoesNotWarn()
    {
        var log = new Log();
        var check = new ProcessStartCheck(new Clock(T0), log, _ => 456, tokens: new Tokens(ProcessStartCheck.WarnTokensAt - 1));
        check.Tick();
        Finish(check);
        Assert.Null(check.Warning);
        Assert.DoesNotContain("warning", log.Lines.Single());
    }

    [Fact]
    public void NoTokenCount_NoWarning_NoError()
    {
        var log = new Log();
        var check = new ProcessStartCheck(new Clock(T0), log, _ => 456, tokens: new Tokens(null));
        check.Tick();
        Finish(check);
        Assert.Null(check.Warning);
        Assert.Equal(["process starts: 456/s"], log.Lines);
    }

    [Fact]
    public void OnceAnHour_NotEachTick()
    {
        var clock = new Clock(T0);
        var log = new Log();
        var runs = 0;
        var check = new ProcessStartCheck(clock, log, _ => { runs++; return 10; });
        check.Tick();
        Finish(check);
        clock.Now = T0.AddMinutes(59);
        check.Tick();
        Finish(check);
        Assert.Equal(1, runs);
        clock.Now = T0.AddMinutes(61);
        check.Tick();
        Finish(check);
        Assert.Equal(2, runs);
        Assert.Equal(2, log.Lines.Count);
    }

    [Fact]
    public void CountBackUnderTheThreshold_ClearsTheStatusText()
    {
        var clock = new Clock(T0);
        var tokens = new Tokens(300_000);
        var check = new ProcessStartCheck(clock, new Log(), _ => 450, tokens: tokens);
        check.Tick();
        Finish(check);
        Assert.NotNull(check.Warning);
        tokens.Value = 9000; // after a Restart
        clock.Now = T0.AddHours(2);
        check.Tick();
        Finish(check);
        Assert.Null(check.Warning);
    }

    [Fact]
    public void TheCheck_RunsOnAThreadOfItsOwn_AndTickReturnsAtOnce()
    {
        using var gate = new ManualResetEventSlim();
        var check = new ProcessStartCheck(new Clock(T0), new Log(), _ => { gate.Wait(TimeSpan.FromSeconds(10)); return 100; });
        check.Tick();   // returns while the measure still waits
        Assert.NotNull(check.Running);
        gate.Set();
        Finish(check);
        Assert.NotNull(check.ThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, check.ThreadId);
    }

    [Fact]
    public void ARealMeasure_GivesAPositiveRate()
    {
        var log = new Log();
        var check = new ProcessStartCheck(new Clock(T0), log);
        check.Tick();
        Finish(check);
        Assert.StartsWith("process starts: ", log.Lines.Single());
    }
}
