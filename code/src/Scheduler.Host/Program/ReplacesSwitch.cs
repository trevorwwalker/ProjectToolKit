using Scheduler.Core.Contracts;

namespace Scheduler.Host.Program;

/// <summary>
/// <c>-Replaces:&lt;version&gt;</c> (967-B): the version of the scheduler this one was started in place of,
/// which the tray's <c>Update and restart</c> or <c>bin\deploy-scheduler.ps1</c> passes when it has just
/// swapped a new build in. It is written to the log beside the start line, so the scheduler log says which
/// version replaced which (VER rules). Like <c>-ShutdownEvent</c> it is read at the entry point, because
/// the scheduler's command line leaves a switch it does not bind alone (CFG-7).
/// </summary>
public static class ReplacesSwitch
{
    /// <summary>The switch as it is typed.</summary>
    public const string Name = "-Replaces";

    /// <summary>The version it names, or "" when the switch was not given.</summary>
    public static string Version(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            var name = arg;
            string? value = null;
            var split = arg.IndexOfAny([':', '=']);
            if (split > 1)
            {
                name = arg[..split];
                value = arg[(split + 1)..];
            }

            if (Text.Same(name, Name) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    /// <summary>The log line that names the two builds (967-B).</summary>
    public static string ReplacedLine(string version, string replaces) =>
        $"scheduler-next {version} replaced {replaces}";
}
