// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Program;

/// <summary>Start-up cannot go on (CFG-14: another scheduler runs; CFG-17: no address to listen on).</summary>
public sealed class SchedulerStartException(string message) : Exception(message);
