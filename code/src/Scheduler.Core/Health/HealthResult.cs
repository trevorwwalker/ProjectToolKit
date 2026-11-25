// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Health;

/// <summary>What the last health check of main ended as (HLTH-5).</summary>
public enum HealthResult
{
    /// <summary>No check has finished yet.</summary>
    None,

    /// <summary>The command exited 0 (L1733).</summary>
    Green,

    /// <summary>Non-zero exit, or killed at maxMinutes (HLTH-4, L1728).</summary>
    Red,
}
