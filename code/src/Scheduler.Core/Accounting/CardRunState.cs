namespace Scheduler.Core.Accounting;

/// <summary>
/// Where a card whose runs are in the figures is now (ACCT-6, <c>Card-Stats</c> L891). The
/// figures follow the card, not the run: one that parked a question, was answered and finished
/// later counts as done.
/// </summary>
public enum CardRunState
{
    /// <summary>In <c>done/</c>.</summary>
    Done,

    /// <summary>In <c>review/</c>: finished, waiting for the developer to check it.</summary>
    Review,

    /// <summary>Parked in <c>active/</c> as needs-human, or waiting on the developer or the planner.</summary>
    Questions,

    /// <summary>Anything else: back in the queue, being worked on again, or stuck.</summary>
    Open,
}
