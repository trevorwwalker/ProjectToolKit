namespace Scheduler.Core.WebContract;

/// <summary>One file a commit changed, with its line counts (roadmap item 6, card 650-C).</summary>
public sealed record ReviewFile(string Path, int Added, int Removed);
