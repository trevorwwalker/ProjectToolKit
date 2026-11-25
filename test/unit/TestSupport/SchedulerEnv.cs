// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Runtime.CompilerServices;

namespace Scheduler.TestSupport;

/// <summary>
/// Card 1070-B: a worker session runs with the scheduler's git settings in its environment -
/// <c>core.hooksPath</c> pointing at the scheduler's hooks folder through <c>GIT_CONFIG_COUNT</c> /
/// <c>GIT_CONFIG_KEY_*</c> / <c>GIT_CONFIG_VALUE_*</c> (<c>Scheduler.Core/Launch/WorkerFiles.cs</c>),
/// <c>GIT_CONFIG_PARAMETERS</c>, and the <c>PTK_*</c> variables the locks use (<c>WorkerLocks.cs</c>).
/// Every git process a test starts inherits them, so every commit and push in a test repository runs
/// the scheduler's commit-msg and pre-push hooks: 1041-B measured about 9 s per commit in a temporary
/// repository with the hooks and 1.7 s without. Tests never run those hooks unless the test itself sets
/// them up, so at load this removes the variables from the test process. A test that wants them sets
/// them on the process it starts (HookFilesTests, LockCommandTests and WorkerEnvTests all do).
/// The values found at load are kept in <see cref="RemovedAtLoad"/> for the tests that prove the strip.
/// This file is linked into every test project by test/unit/Directory.Build.props, like TestTmp.cs.
/// </summary>
internal static class SchedulerEnv
{
    private static readonly Dictionary<string, string> Found = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The scheduler variables the test process was started with, as they were before <see cref="RemoveFromProcess"/> took them out.</summary>
    internal static IReadOnlyDictionary<string, string> RemovedAtLoad => Found;

    /// <summary>Runs before any test in this assembly: strips the scheduler's variables out of the process environment.</summary>
    [ModuleInitializer]
    internal static void StripAtLoad()
    {
        foreach (var (name, value) in RemoveFromProcess())
        {
            Found[name] = value;
        }
    }

    /// <summary>
    /// True for a variable only the scheduler's worker environment sets: every <c>PTK_*</c>, and git's
    /// own <c>GIT_CONFIG_COUNT</c>, <c>GIT_CONFIG_PARAMETERS</c> and indexed <c>GIT_CONFIG_KEY_*</c> /
    /// <c>GIT_CONFIG_VALUE_*</c>. Nothing else git reads (<c>GIT_DIR</c>, <c>GIT_INDEX_FILE</c>, <c>GIT_CONFIG</c>,
    /// <c>GIT_CONFIG_GLOBAL</c>, <c>GIT_ASKPASS</c>, <c>HOME</c>, <c>PATH</c>) is a scheduler variable.
    /// </summary>
    internal static bool IsSchedulerVariable(string name) =>
        name.StartsWith("PTK_", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "GIT_CONFIG_COUNT", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "GIT_CONFIG_PARAMETERS", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes every scheduler variable from this process's environment and returns the ones that were set.</summary>
    internal static IReadOnlyDictionary<string, string> RemoveFromProcess()
    {
        var removed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string name || entry.Value is not string value || !IsSchedulerVariable(name))
            {
                continue;
            }

            removed[name] = value;
            Environment.SetEnvironmentVariable(name, null);
        }

        return removed;
    }
}
