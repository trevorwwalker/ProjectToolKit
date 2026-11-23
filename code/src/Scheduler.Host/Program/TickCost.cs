namespace Scheduler.Host.Program;

/// <summary>
/// 966-B: what a tick costs - its length and its slowest step - and how many ticks ran since the last
/// summary was logged, so the scheduler log shows where the time goes.
/// </summary>
public sealed class TickCost
{
    private DateTimeOffset _start;
    private DateTimeOffset? _stepStart;
    private string _step = "";
    private string _slowest = "";
    private TimeSpan _slowestTook;
    private int _ticks;
    private TimeSpan _longest;

    /// <summary>One summary: the last tick, and the ticks since the last summary was logged.</summary>
    public readonly record struct Summary(TimeSpan Took, string Slowest, TimeSpan SlowestTook, int Ticks, TimeSpan Longest);

    /// <summary>A tick starts.</summary>
    public void Begin(DateTimeOffset now)
    {
        _start = now;
        _stepStart = null;
        _step = "";
        _slowest = "";
        _slowestTook = TimeSpan.Zero;
    }

    /// <summary>A step starts, which ends the one before it.</summary>
    public void Step(string step, DateTimeOffset now)
    {
        Close(now);
        _step = step;
        _stepStart = now;
    }

    /// <summary>The tick ends: its summary.</summary>
    public Summary End(DateTimeOffset now)
    {
        Close(now);
        _stepStart = null;
        var took = now - _start;
        _ticks++;
        if (took > _longest)
        {
            _longest = took;
        }

        return new Summary(took, _slowest, _slowestTook, _ticks, _longest);
    }

    /// <summary>A summary was logged: count the ticks and the longest one again from here.</summary>
    public void Logged()
    {
        _ticks = 0;
        _longest = TimeSpan.Zero;
    }

    private void Close(DateTimeOffset now)
    {
        if (_stepStart is { } started && now - started > _slowestTook)
        {
            _slowestTook = now - started;
            _slowest = _step;
        }
    }
}
