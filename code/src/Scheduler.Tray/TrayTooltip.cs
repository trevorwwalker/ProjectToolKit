namespace Scheduler.Tray;

/// <summary>The icon's tooltip: the project and the scheduler's state, cut to what Windows shows.</summary>
public static class TrayTooltip
{
    /// <summary>The longest tooltip a notification icon takes.</summary>
    public const int MaxLength = 127;

    /// <summary>
    /// The text for <paramref name="project"/> in <paramref name="state"/>; <paramref name="workers"/>
    /// is from <c>/live.json</c>, null when the page did not answer, and then the last console line stands in.
    /// </summary>
    public static string For(string project, SchedulerState state, int? workers, string lastLine)
    {
        var head = $"{project} scheduler";
        var text = state switch
        {
            SchedulerState.Starting => $"{head}: starting",
            SchedulerState.Stopping => $"{head}: stopping, waiting for the workers",
            SchedulerState.Restarting => $"{head}: restarting; the workers keep running",
            SchedulerState.Leaving => $"{head}: ending; the workers keep running",
            SchedulerState.Exited => WithLine($"{head}: stopped", lastLine),
            _ when workers is { } n => n switch
            {
                0 => $"{head}: running, no workers busy",
                1 => $"{head}: running, 1 worker",
                _ => $"{head}: running, {n} workers",
            },
            _ => WithLine($"{head}: page not answering", lastLine),
        };
        return Cut(text);
    }

    /// <summary>
    /// MP-H9: the tooltip of a pool - it names "pool" and each project's state ("pool scheduler: AmigaX running,
    /// ProjectToolkit paused-red; 2 workers"). Before the pool has written its status, or when it is not running,
    /// it reads as <see cref="For"/> does for the project "pool".
    /// </summary>
    public static string ForPool(SchedulerState state, int? workers, string lastLine, IReadOnlyList<(string Name, string State)> projects)
    {
        if (projects.Count == 0 || state is not (SchedulerState.Running or SchedulerState.Starting))
        {
            return For("pool", state, workers, lastLine);
        }

        var states = string.Join(", ", projects.Select(p => $"{p.Name} {p.State}"));
        var busy = workers switch
        {
            null => "",
            0 => "; no workers busy",
            1 => "; 1 worker",
            { } n => $"; {n} workers",
        };
        return Cut($"pool scheduler: {states}{busy}");
    }

    private static string WithLine(string text, string lastLine)
    {
        var line = lastLine.Trim();
        return line.Length == 0 ? text : $"{text} - {line}";
    }

    private static string Cut(string text) =>
        text.Length <= MaxLength ? text : string.Concat(text.AsSpan(0, MaxLength - 3), "...");
}
