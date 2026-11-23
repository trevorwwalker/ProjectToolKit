using Scheduler.Core.Contracts;

namespace Scheduler.Host.Observe;

/// <summary>
/// An observe run beside the old scheduler (roadmap item 1, card 390-B): its own log folder
/// (<c>&lt;log folder&gt;\observe</c>), port (the configured one plus one unless <c>-Port</c> was
/// given) and instance name (<c>&lt;project&gt;-observe</c>, so CFG-14's mutex does not stop it),
/// no health check (it would add a worktree to the repository), and no stop after the first tick
/// (DISP-21 is dry run's). The old scheduler's log folder is read, never written.
/// </summary>
public sealed record ObserveSettings
{
    /// <summary>The observe run's folder inside the old scheduler's log folder.</summary>
    public const string Folder = "observe";

    /// <summary>What the project name gets, for the instance and the page title.</summary>
    public const string ProjectSuffix = "-observe";

    /// <summary>The old scheduler's log folder: running.json, retries and the other state files are mirrored from it.</summary>
    public required string OldLogDir { get; init; }

    /// <summary>The configuration of an observe run made from <paramref name="config"/>, and its settings.</summary>
    public static (SchedulerConfig Config, ObserveSettings Settings) Apply(SchedulerConfig config, bool portGiven)
    {
        var logDir = Path.Combine(config.LogDir, Folder);
        return (
            config with
            {
                Project = config.Project + ProjectSuffix,
                LogDir = logDir,
                WorkspaceRoot = Path.Combine(logDir, "slots"),
                Port = portGiven ? config.Port : config.Port + 1,
                DryRun = false,
                HealthCommand = "",
            },
            new ObserveSettings { OldLogDir = config.LogDir });
    }
}
