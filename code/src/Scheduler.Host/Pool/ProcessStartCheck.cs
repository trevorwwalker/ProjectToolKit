using System.Diagnostics;
using System.Globalization;
using Scheduler.Core.Contracts;

namespace Scheduler.Host.Pool;

/// <summary>
/// 1078-B: how fast this PC starts processes, measured at the start and then once an hour on a thread of its own
/// (never inside a tick): <see cref="Loops"/> <c>cmd.exe</c> processes at once, each starting its share of <c>whoami.exe</c>. The loops are
/// processes, not threads or tasks: .NET starts processes one at a time within a process (about 50 a second on any PC),
/// so loops inside this process would measure that queue and not Windows (1106-B). One line to the pool log,
/// <c>process starts: &lt;n&gt;/s</c>; under <see cref="WarnBelow"/> the line says so and <see cref="Warning"/> holds
/// the text the pool's status line shows. A healthy PC of this size starts about 470 a second; on 2026-11-22 it
/// started 30 and everything that starts processes ran 6 to 25 times slower.
/// </summary>
public sealed class ProcessStartCheck
{
    /// <summary>How long after a check the next one runs.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromHours(1);

    /// <summary>Warn below this many starts a second: the developer's value (2026-11-22), a degenerate PC (25 at its worst).</summary>
    public const double WarnBelow = 25;

    /// <summary>How many processes one check starts.</summary>
    public const int Starts = 400;

    /// <summary>How many of them run side by side.</summary>
    public const int Loops = 8;

    private readonly IClock _clock;
    private readonly ISchedulerLog _log;
    private readonly Func<int, double> _rate;
    private readonly ForegroundLockFix? _foreground;
    private readonly ITokenCount? _tokens;
    private readonly Lock _lock = new();
    private DateTimeOffset? _last;
    private Thread? _thread;
    private string? _warning;

    /// <param name="clock">The clock.</param>
    /// <param name="log">The pool log.</param>
    /// <param name="rate">Starts the given number of processes side by side and returns starts per second; null = <c>whoami.exe</c>.</param>
    /// <param name="foreground">1161-B: the foreground lock workaround, checked again with each check; null = none.</param>
    /// <param name="tokens">1161-B: the live token count logged next to the rate; null = none.</param>
    public ProcessStartCheck(IClock clock, ISchedulerLog log, Func<int, double>? rate = null, ForegroundLockFix? foreground = null, ITokenCount? tokens = null)
    {
        _clock = clock;
        _log = log;
        _foreground = foreground;
        _tokens = tokens;
        _rate = rate ?? Measure;
    }

    /// <summary>The thread the last check ran on, or null before one ran; tests compare it with the tick's.</summary>
    public int? ThreadId { get; private set; }

    /// <summary>The text for the pool's status line while the last rate was under <see cref="WarnBelow"/>; null otherwise.</summary>
    public string? Warning
    {
        get
        {
            lock (_lock)
            {
                return _warning;
            }
        }
    }

    /// <summary>The check under way, or null; tests wait on it.</summary>
    public Thread? Running
    {
        get
        {
            lock (_lock)
            {
                return _thread;
            }
        }
    }

    /// <summary>Called from each tick: starts a check on a thread of its own when none has run since the start or the last was an hour ago. Returns at once.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            var now = _clock.Now;
            if (_thread is not null || (_last is { } l && now - l < Every))
            {
                return;
            }

            _last = now;
            _thread = new Thread(Run) { IsBackground = true, Name = "process start check" };
            _thread.Start();
        }
    }

    private void Run()
    {
        try
        {
            ThreadId = Environment.CurrentManagedThreadId;
            _foreground?.Apply(quietWhenZero: true); // 1161-B: Windows may have put the value back
            var live = _tokens?.Live();
            var tokens = live is { } t ? $", tokens: {t.ToString("0", CultureInfo.InvariantCulture)} live" : "";
            var rate = _rate(Starts);
            var n = rate.ToString("0", CultureInfo.InvariantCulture);
            var slow = rate < WarnBelow;
            lock (_lock)
            {
                _warning = slow ? $"process starts slow: {n}/s (under {WarnBelow.ToString("0", CultureInfo.InvariantCulture)}/s)" : null;
            }

            _log.Write(slow
                ? $"process starts: {n}/s - warning: under {WarnBelow.ToString("0", CultureInfo.InvariantCulture)}/s, everything that starts processes runs slowly{tokens}"
                : $"process starts: {n}/s{tokens}");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _log.Write($"process starts: could not be measured ({e.Message})");
        }
        finally
        {
            lock (_lock)
            {
                _thread = null;
            }
        }
    }

    // Loops cmd.exe processes at once, each running a for /L loop that starts its share of whoami.exe; the rate is the
    // starts over the wall time of the round. Same method as bin\process-start-check.ps1.
    private static double Measure(int starts)
    {
        var each = Math.Max(1, starts / Loops);
        var comSpec = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var watch = Stopwatch.StartNew();
        var procs = new List<Process>();
        try
        {
            for (var loop = 0; loop < Loops; loop++)
            {
                var info = new ProcessStartInfo(comSpec)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                info.Arguments = $"/d /c for /L %i in (1,1,{each}) do @whoami.exe >nul 2>nul";
                procs.Add(Process.Start(info) ?? throw new InvalidOperationException("cmd.exe did not start"));
            }

            foreach (var p in procs)
            {
                p.WaitForExit();
            }
        }
        finally
        {
            foreach (var p in procs)
            {
                p.Dispose();
            }
        }

        return each * Loops / Math.Max(watch.Elapsed.TotalSeconds, 0.001);
    }
}
