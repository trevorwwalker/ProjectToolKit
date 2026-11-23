namespace Scheduler.Core.Accounting;

/// <summary>
/// The words the Queue window shows beside a queued card about its time box (roadmap item 4,
/// card 640-A): a warning when its <c>Time box:</c> is shorter than its area's 90th percentile for
/// the worker type it is headed for, and a suggestion where the card has no time box at all.
/// Nothing here ever changes a card: the planner or developer edits the time box (item 3).
/// </summary>
public static class TimeBoxAdvice
{
    /// <summary>
    /// The words for a card with this <paramref name="timeBox"/> (its <c>Time box:</c>, or null for
    /// none) and these typical figures for the area and model it will run on, or empty when there
    /// is nothing to say.
    /// </summary>
    public static string Text(int? timeBox, AreaTypical? typical) => typical switch
    {
        null => "",
        _ when timeBox is null => $"suggested time box: {typical.P90} min",
        _ when timeBox < typical.P90 =>
            $"time box {timeBox} min; area {typical.Area} on {typical.Model} usually needs up to {typical.P90}",
        _ => "",
    };
}
