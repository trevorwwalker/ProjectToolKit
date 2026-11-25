// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>The state files in the log folder (<see cref="IStateFiles"/>).</summary>
public enum StateFile
{
    /// <summary><c>retries.json</c> (TIME-22).</summary>
    Retries,

    /// <summary><c>categories-accepted.json</c> (LANE-2, LANE-3).</summary>
    CategoriesAccepted,

    /// <summary><c>workers-state.json</c> (WRK-3).</summary>
    WorkersState,

    /// <summary>1.6's log-folder <c>workers.json</c>, read for enabled flags (WRK-3).</summary>
    LegacyWorkers,

    /// <summary><c>running.json</c> (ADOPT-1).</summary>
    Running,

    /// <summary><c>status.txt</c> (ADOPT-3).</summary>
    Status,
}
