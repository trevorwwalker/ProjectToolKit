using Scheduler.Core.Contracts;
using Scheduler.Core.Tick;
using Scheduler.Host.Tick;

namespace Scheduler.Host.Observe;

/// <summary>
/// After each observe tick (roadmap item 1): the queue notes that changed (DISP-12, DISP-19) as
/// <c>note card: text</c> (<c>none</c> when a note went away), then the tick's closing line
/// <c>tick N: state S, running 1=card, ...</c>, which <see cref="LogComparison"/> uses to cut the
/// log into ticks.
/// </summary>
public sealed class ObserveRecorder(ISchedulerLog log, IClock clock)
{
    private Dictionary<string, string> _notes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The ticks recorded so far.</summary>
    public int Ticks { get; private set; }

    /// <summary>Log what <paramref name="tick"/> decided.</summary>
    public void Record(SchedulerTick tick)
    {
        var now = clock.Now;
        var notes = (tick.LastDispatch?.Notes ?? [])
            .GroupBy(n => n.Card, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join("; ", g.Select(n => n.Note)), StringComparer.OrdinalIgnoreCase);
        foreach (var (card, note) in notes.OrderBy(n => n.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!_notes.TryGetValue(card, out var was) || was != note)
            {
                log.Write(ObserveLine.Format(now, $"note {card}: {note}"));
            }
        }

        foreach (var gone in _notes.Keys.Where(k => !notes.ContainsKey(k)).Order(StringComparer.OrdinalIgnoreCase))
        {
            log.Write(ObserveLine.Format(now, $"note {gone}: none"));
        }

        _notes = notes;
        Ticks++;
        var running = tick.Running.Count == 0
            ? "none"
            : string.Join(", ", tick.Running.Values.OrderBy(w => w.Slot).Select(w => $"{w.Slot}={w.Card}"));
        log.Write(ObserveLine.Format(now, $"tick {Ticks}: state {TickHost.StateName(tick.State)}, running {running}"));
    }
}
