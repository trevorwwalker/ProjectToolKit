// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>
/// The time, so Core needs no real clock. Used by every timed rule: fetch spacing (CARD-2),
/// limits, warnings, stops and kills (TIME-1 to TIME-13), cadences (PAGE-2, PAGE-3) and the
/// dates in journals and logs.
/// </summary>
public interface IClock
{
    /// <summary>The current local time.</summary>
    DateTimeOffset Now { get; }
}
