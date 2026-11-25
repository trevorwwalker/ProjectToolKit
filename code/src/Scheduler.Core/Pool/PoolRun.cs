// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Pool;

/// <summary>
/// One worker's run on a project's card, for the shares (MP-6, spec B3 Q2): when it started and,
/// once it has, when it ended. A run still going counts up to now.
/// </summary>
/// <param name="Project">The project whose card it ran.</param>
/// <param name="Start">When the worker started.</param>
/// <param name="End">When it ended; null while it is still running.</param>
public sealed record PoolRun(string Project, DateTimeOffset Start, DateTimeOffset? End);
