// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Planner;

/// <summary>A folder directly inside the outbox: its name and whether it holds a <c>batch.json</c>.</summary>
public sealed record OutboxFolder(string Name, bool HasBatchJson);
