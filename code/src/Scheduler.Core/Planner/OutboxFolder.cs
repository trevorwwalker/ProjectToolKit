namespace Scheduler.Core.Planner;

/// <summary>A folder directly inside the outbox: its name and whether it holds a <c>batch.json</c>.</summary>
public sealed record OutboxFolder(string Name, bool HasBatchJson);
