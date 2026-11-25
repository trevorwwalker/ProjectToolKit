// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Tray;

/// <summary>How a deploy of the scheduler ended (967-B).</summary>
public enum UpdateResult
{
    /// <summary>The new build is running in the scheduler's folder.</summary>
    Deployed,

    /// <summary>The new build would not start, so the build it replaced is running again.</summary>
    RolledBack,

    /// <summary>Nothing changed: the running scheduler is the one that was running before.</summary>
    Failed,
}
