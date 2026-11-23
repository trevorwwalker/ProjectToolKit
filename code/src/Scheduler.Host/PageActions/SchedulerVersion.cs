using Scheduler.Core;

namespace Scheduler.Host.PageActions;

/// <summary>
/// The version CFG-18 shows in the start log line and on the page: <c>Major.Minor.Rev</c> of this
/// build's assembly, so what the page says is what was built. It is <see cref="CoreInfo.Version"/>,
/// which is written once as <c>&lt;Version&gt;</c> in code/src/Directory.Build.props (951-A).
/// </summary>
public static class SchedulerVersion
{
    /// <summary>The version string, <c>Major.Minor.Rev</c>, of this build.</summary>
    public static string Text => CoreInfo.Version;
}
