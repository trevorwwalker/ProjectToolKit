namespace Scheduler.Core.Contracts;

/// <summary>The folder a card is in under the jobs folder (CARD-4).</summary>
public enum CardFolder
{
    /// <summary><c>queue/</c>: waiting to be taken.</summary>
    Queue,

    /// <summary><c>active/</c>: claimed by a worker.</summary>
    Active,

    /// <summary><c>review/</c>: finished, needs the developer.</summary>
    Review,

    /// <summary><c>done/</c>: finished and verified.</summary>
    Done,
}
