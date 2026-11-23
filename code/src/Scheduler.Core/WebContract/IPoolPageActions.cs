namespace Scheduler.Core.WebContract;

/// <summary>
/// The pool's page actions (MP-P8): every route that names a card or a slot carries <c>project=</c>
/// and goes to that project's <see cref="IPageActions"/>; the pool-wide ones act on one project or,
/// with none, on all of them (MP-H8).
/// </summary>
public interface IPoolPageActions
{
    /// <summary>The projects in the pool, in the pool file's order.</summary>
    IReadOnlyList<string> Projects { get; }

    /// <summary>The actions of <paramref name="project"/> (names compare without case), or null when it is not in the pool ("which project?").</summary>
    IPageActions? For(string project);

    /// <summary><c>/pause</c>: one project, or every project when <paramref name="project"/> is null.</summary>
    ActionResult Pause(string? project);

    /// <summary><c>/drain</c>: one project, or every project when <paramref name="project"/> is null.</summary>
    ActionResult Drain(string? project);

    /// <summary><c>/resume</c>: one project, or every project when <paramref name="project"/> is null.</summary>
    ActionResult Resume(string? project);

    /// <summary><c>/stopall</c>: stop every project's workers and exit (MP-H8).</summary>
    ActionResult StopAll();

    /// <summary><c>/stop-project</c> (card 1004-C): stop <paramref name="project"/>'s workers and pause it; the scheduler and every other project carry on.</summary>
    ActionResult Stop(string project);

    /// <summary>
    /// <c>/workspaces</c> (1033-C): set <paramref name="project"/>'s workspaces to <paramref name="value"/>, a whole
    /// number 1 to 64 as typed; anything else is refused with a reason naming the project.
    /// </summary>
    ActionResult SetWorkspaces(string project, string value) => ActionResult.Failed("workspaces cannot be set here");
}
