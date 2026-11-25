// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Git;

/// <summary>One git call: exit code, stdout and stderr kept apart (CFG-16).</summary>
public sealed record GitResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>True when git exited with 0.</summary>
    public bool Ok => ExitCode == 0;
}
