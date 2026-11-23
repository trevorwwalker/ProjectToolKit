using System.Diagnostics;

namespace Scheduler.Host.Slots;

/// <summary>
/// 1073-B: git started by the scheduler, or by anything a worker starts, never opens an editor or waits for a
/// prompt: an unattended <c>git commit</c> with no message must fail at once, not leave <c>vi</c> running for days.
/// </summary>
public static class GitNonInteractive
{
    /// <summary>The settings, beside <c>GIT_TERMINAL_PROMPT=0</c>.</summary>
    public static IReadOnlyDictionary<string, string> Settings { get; } = new Dictionary<string, string>
    {
        ["GIT_EDITOR"] = "true",
        ["GIT_SEQUENCE_EDITOR"] = "true",
        ["GIT_MERGE_AUTOEDIT"] = "no",
    };

    /// <summary>Set the editor settings on <paramref name="psi"/> and return it.</summary>
    public static ProcessStartInfo Apply(ProcessStartInfo psi)
    {
        foreach (var (key, value) in Settings)
        {
            psi.Environment[key] = value;
        }
        return psi;
    }
}
