namespace Scheduler.Web.Server;

/// <summary>
/// One page action as the queue holds it (957-B): what was clicked, which card or worker it names,
/// and how far it got - <c>queued</c>, <c>running</c>, <c>done</c> or <c>failed</c> with the
/// scheduler's own words about the failure. <c>/live.json</c> carries these so the page can replace
/// the marks it made at the click with what really happened.
/// </summary>
/// <param name="Id">The id the click was answered with (PAGE-1).</param>
/// <param name="Kind">What was clicked: answer, done, queue, stop, send, ...</param>
/// <param name="Card">The card or worker the action names, when it names one.</param>
/// <param name="State">queued, running, done or failed.</param>
/// <param name="Message">The failure to show in the notice line, when it failed.</param>
public sealed record PageActionState(
    int Id,
    string Kind,
    string? Card,
    string State,
    string Message);
