namespace Scheduler.Core.Planner;

/// <summary>A file a batch wrote: where it ends up on main, and its text (PLAN-7).</summary>
public sealed record WrittenFile(string Path, string Text);
