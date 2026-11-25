// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Host.Templates;

/// <summary>
/// Where ProjectToolkit is (card 540-B): the <c>toolkit</c> setting when it is given (a path relative
/// to the project's repository is allowed), else the nearest folder above the exe that has
/// <c>assets/templates/</c> (the exe runs from the toolkit). Null when neither has the templates.
/// </summary>
public static class ToolkitLocator
{
    /// <param name="configured">The <c>toolkit</c> setting, or null or empty when none.</param>
    /// <param name="repo">The project's repository, which a relative setting is taken from.</param>
    /// <param name="exeDir">The exe's folder, or null to not look above it.</param>
    public static string? Find(string? configured, string repo, string? exeDir)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var dir = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(repo, configured));
            return HasTemplates(dir) ? dir : null;
        }

        for (var dir = string.IsNullOrEmpty(exeDir) ? null : new DirectoryInfo(Path.GetFullPath(exeDir)); dir is not null; dir = dir.Parent)
        {
            if (HasTemplates(dir.FullName))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    private static bool HasTemplates(string dir) => Directory.Exists(Path.Combine(dir, "assets", "templates"));
}
