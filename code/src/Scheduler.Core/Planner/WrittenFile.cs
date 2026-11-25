// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Planner;

/// <summary>A file a batch wrote: where it ends up on main, and its text (PLAN-7).</summary>
public sealed record WrittenFile(string Path, string Text);
