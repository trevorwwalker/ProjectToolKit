using System.Text.RegularExpressions;

namespace Scheduler.Tray;

/// <summary>
/// The scheduler's start line, <c>scheduler-next &lt;version&gt; started for Proj: 4 slot(s), page
/// http://localhost:8765/</c> - the version is the build's own, CFG-18's - possibly behind a log
/// time stamp: it gives the project and the page address.
/// </summary>
public static partial class StartLine
{
    [GeneratedRegex(@"started for (?<project>.+?): .*\bpage (?<url>https?://\S+)")]
    private static partial Regex Pattern();

    /// <summary>The project and page address, when <paramref name="line"/> is the start line.</summary>
    public static bool TryParse(string? line, out string project, out Uri? page)
    {
        project = "";
        page = null;
        if (line is null)
        {
            return false;
        }

        var match = Pattern().Match(line);
        if (!match.Success || !Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        project = match.Groups["project"].Value.Trim();
        page = uri;
        return true;
    }
}
