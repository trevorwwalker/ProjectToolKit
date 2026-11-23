using Scheduler.Core.Contracts;

namespace Scheduler.Web.Views;

/// <summary>
/// The notice at the top of the page for a page action that could not be done (MOVE-11): logged,
/// and shown for 10 minutes or until a later action succeeds, since the log is not where anyone looks.
/// </summary>
public sealed class PageNotice(IClock clock, ISchedulerLog? log = null)
{
    /// <summary>How long a failure stays at the top of the page.</summary>
    public static readonly TimeSpan ShowFor = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private (DateTimeOffset At, string Text)? _current;

    /// <summary>A page action failed: log it and show it at the top.</summary>
    public void Failed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        log?.Write(text);
        lock (_gate)
        {
            _current = (clock.Now, text);
        }
    }

    /// <summary>A page action worked: the last failure was this one, so stop showing it.</summary>
    public void Succeeded()
    {
        lock (_gate)
        {
            _current = null;
        }
    }

    /// <summary>The notice to show now, or null when there is none left to show.</summary>
    public (DateTimeOffset At, string Text)? Current
    {
        get
        {
            lock (_gate)
            {
                if (_current is not { } current || clock.Now - current.At >= ShowFor)
                {
                    return null;
                }

                return current;
            }
        }
    }

    /// <summary>The notice's line: the time it happened, then what failed.</summary>
    public string Html()
    {
        if (Current is not { } current)
        {
            return "";
        }

        return $"<div class='notice'>{current.At:HH:mm:ss} &nbsp; {System.Net.WebUtility.HtmlEncode(current.Text)}</div>";
    }
}
