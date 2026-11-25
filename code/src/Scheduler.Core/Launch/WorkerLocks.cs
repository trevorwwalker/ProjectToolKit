// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Launch;

/// <summary>
/// Dynamic locks (design <c>dynamic-locks.md</c>, DL-18, section 3): what a worker's environment carries when
/// locks are on - its card, the exe its <c>lock</c> command runs, and the scheduler's hooks folder.
/// </summary>
/// <param name="Card">The card's file name without <c>.md</c> (the lock table's key, and the commits' <c>Card:</c> trailer).</param>
/// <param name="Exe">The full path of <c>scheduler-next.exe</c>.</param>
/// <param name="HooksDir">The scheduler's hooks folder (<see cref="WorkerFiles.HooksDir"/>).</param>
public sealed record WorkerLocks(string Card, string Exe, string HooksDir)
{
    /// <summary>The card the worker runs, for the <c>commit-msg</c> hook's trailer.</summary>
    public const string CardVariable = "PTK_CARD";

    /// <summary>The exe that answers <c>lock</c> and <c>check-push</c>.</summary>
    public const string ExeVariable = "PTK_SCHEDULER_EXE";

    /// <summary>
    /// The variables, set after the agent's own so the scheduler's win: git reads <c>core.hooksPath</c>
    /// from <c>GIT_CONFIG_COUNT</c>/<c>GIT_CONFIG_KEY_0</c>/<c>GIT_CONFIG_VALUE_0</c> (git 2.31 and later).
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Variables() =>
    [
        new(CardVariable, Card),
        new(ExeVariable, Exe),
        new("GIT_CONFIG_COUNT", "1"),
        new("GIT_CONFIG_KEY_0", "core.hooksPath"),
        new("GIT_CONFIG_VALUE_0", HooksDir),
    ];
}
