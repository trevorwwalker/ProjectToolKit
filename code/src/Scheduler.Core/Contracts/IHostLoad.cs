// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>
/// The host's CPU and memory, sampled first in each tick (A3's tick order). Used by host load
/// (ACCT-9), the page's top bar (PAGE-5) and threads per worker (WRK-7).
/// </summary>
public interface IHostLoad
{
    /// <summary>Sample CPU and memory now.</summary>
    HostLoadSample Sample();
}
