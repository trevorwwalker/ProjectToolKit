namespace Scheduler.Core.WebContract;

/// <summary>What a Waiting on you entry is (PAGE-5).</summary>
public enum PageWaitingKind
{
    /// <summary>A category held until the developer accepts a change.</summary>
    HeldCategory,

    /// <summary>A card that is stuck.</summary>
    Stuck,

    /// <summary>A card with open questions.</summary>
    Question,

    /// <summary>A card waiting on the developer.</summary>
    Waiting,

    /// <summary>A card in review (hidden while its worker runs).</summary>
    Review,

    /// <summary>
    /// A generic file whose copy was edited in the project, which the scheduler will not overwrite
    /// silently (roadmap item 1, job card 580-A). Its <c>Name</c> is the copy's path, not a card, and
    /// its actions are Overwrite and Keep.
    /// </summary>
    TemplateEdited,

    /// <summary>
    /// A card whose run ended done or review with its text unchanged (395-B, job card 410-A). A kind
    /// of its own rather than <see cref="Stuck"/>, whose row only offers Retry - this card's run
    /// finished, so what the developer owes it is a filled-in card and Done.
    /// </summary>
    FinishedWithoutUpdate,
}
