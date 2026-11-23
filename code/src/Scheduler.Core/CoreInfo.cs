using System.Reflection;

namespace Scheduler.Core;

/// <summary>Identity of the scheduler-next program.</summary>
public static class CoreInfo
{
    /// <summary>
    /// Major.Minor.Rev: Major = major functionality change; Minor = incompatible interface change;
    /// Rev = bug fixes. CFG-18 and 951-A: the number is written once, as <c>&lt;Version&gt;</c> in
    /// code/src/Directory.Build.props, and read back out of this build, so the page, the log and
    /// <c>--version</c> cannot disagree with what was built.
    /// </summary>
    public static string Version { get; } = Read();

    private static string Read()
    {
        // The informational version is the built version plus the source sha the SDK appends after a
        // '+'; the page, the log and --version show Major.Minor.Rev alone.
        var informational = typeof(CoreInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            return (plus < 0 ? informational : informational[..plus]).Trim();
        }

        var v = typeof(CoreInfo).Assembly.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Revision}";
    }
}
