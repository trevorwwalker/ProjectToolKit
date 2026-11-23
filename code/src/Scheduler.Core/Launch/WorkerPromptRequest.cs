using Scheduler.Core.Contracts;

namespace Scheduler.Core.Launch;

/// <summary>What the worker's prompt is written for (WRK-10).</summary>
public sealed record WorkerPromptRequest
{
    /// <summary>The card the session is to work.</summary>
    public required Card Card { get; init; }

    /// <summary>The slot's worktree, where the session runs.</summary>
    public required string Worktree { get; init; }

    /// <summary>The slot number, which names the three flag files.</summary>
    public required int Slot { get; init; }

    /// <summary>When the session starts: the time its limit is measured from.</summary>
    public required DateTimeOffset Start { get; init; }

    /// <summary>Threads the worker may use (WRK-7).</summary>
    public int Jobs { get; init; } = 1;

    /// <summary>True for an orphan already in active/, false for a card still in queue/.</summary>
    public bool Resume { get; init; }

    /// <summary>The category brief from categories.json (LANE-5); "" until categories have been read.</summary>
    public string CategoryBrief { get; init; } = "";

    /// <summary>
    /// The card's scope brief (<see cref="Scopes.ScopeBrief.For"/>, SC-4, job card 560-A); when set
    /// it replaces the category brief and the prompt speaks of the card's scope, not its category's files.
    /// </summary>
    public string ScopeBrief { get; init; } = "";

    /// <summary>
    /// The category's name as the scheduler knows it now - from categories.json once it has been read, from
    /// the config's <c>cfg.categories</c> before that (CFG-13); "" to fall back to the config's.
    /// </summary>
    public string CategoryName { get; init; } = "";

    /// <summary>
    /// True when the agent's hook hands the developer's messages over by itself
    /// (<see cref="MessageHook.Delivers"/>, WRK-14, job card 570-B): the prompt then drops the
    /// "check the message file" steps, which cost a tool call each time.
    /// </summary>
    public bool Hooks { get; init; }

    /// <summary>
    /// Dynamic locks (design <c>dynamic-locks.md</c>, section 3): the full path of <c>scheduler-next.exe</c>,
    /// whose <c>lock</c> command asks for a file; "" when locks are off. Set, a scope-mode prompt tells the
    /// worker how to ask for a file outside its scope, and how to yield, in place of "stay inside the scope".
    /// </summary>
    public string LockExe { get; init; } = "";
}
