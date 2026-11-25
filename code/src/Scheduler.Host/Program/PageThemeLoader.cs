// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using System.Text;
using System.Text.Json;
using Scheduler.Core.Contracts;
using Scheduler.Web.Views;

namespace Scheduler.Host.Program;

/// <summary>
/// The page's themes (PAGE-11), read from the theme files the build copies beside the exe:
/// <c>theme.default.json</c> beside it and the rest in <c>themes/</c>, all from
/// <c>assets/themes/</c> (<c>Scheduler.Host.csproj</c>). The project's own theme, named by
/// <c>page.theme</c> and resolved against the repository, is offered first. A theme file that
/// cannot be read is logged once, at start-up, and the page keeps the built-in style for it,
/// as the old scheduler did: a theme is no reason to fail the page.
/// </summary>
public static class PageThemeLoader
{
    /// <summary>
    /// The themes for <paramref name="config"/>. <paramref name="schedulerFolder"/> is where
    /// <c>theme.default.json</c> and <c>themes/</c> live - the exe's own folder unless a test names one.
    /// </summary>
    public static PageThemes Load(SchedulerConfig config, ISchedulerLog log, string? schedulerFolder = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(log);

        var folder = string.IsNullOrWhiteSpace(schedulerFolder) ? AppContext.BaseDirectory : schedulerFolder;
        var named = config.PageTheme?.Trim() ?? string.Empty;
        var project = ProjectThemeFile(config.Repo, named);
        if (named.Length > 0 && project is null)
        {
            log.Write($"themes: page.theme {named} was not found; the page uses the scheduler's own theme files");
        }

        try
        {
            // The file the page's own style comes from: the project theme, else the scheduler's default.
            var defaultFile = Path.Combine(folder, "theme.default.json");
            var ownFile = project ?? defaultFile;
            var files = new List<string>();
            if (File.Exists(ownFile))
            {
                files.Add(ownFile);
            }

            foreach (var file in SidecarFiles(folder, ownFile))
            {
                files.Add(file);
            }

            if (files.Count == 0)
            {
                log.Write($"themes: no theme files beside the program in {folder}; the page uses the built-in style");
                return PageThemes.BuiltIn;
            }

            // PageThemes.Load skips an unreadable file silently, so name the ones that failed here.
            var broken = files.Where(file => !ReadsAsObject(file)).ToArray();
            if (broken.Length > 0)
            {
                var names = string.Join(", ", broken.Select(Path.GetFileName));
                log.Write($"themes: could not read {names} beside the program; the page uses the built-in style for them");
            }

            return PageThemes.Load(folder, project, config.Repo, log.Write);
        }
        catch (Exception ex)
        {
            log.Write($"themes: reading {folder} failed ({ex.Message}); the page uses the built-in style");
            return PageThemes.BuiltIn;
        }
    }

    /// <summary>
    /// The file <c>page.theme</c> names, resolved against the repository, or null when it is not
    /// set or not there (a project theme that has gone missing is no reason to lose the page).
    /// </summary>
    private static string? ProjectThemeFile(string repo, string named)
    {
        if (named.Length == 0)
        {
            return null;
        }

        var file = Path.IsPathRooted(named) ? named : Path.Combine(repo, named);
        return File.Exists(file) ? file : null;
    }

    /// <summary><c>themes/*.json</c> beside the exe, less the file the page's own style already came from.</summary>
    private static IEnumerable<string> SidecarFiles(string folder, string ownFile)
    {
        var themesFolder = Path.Combine(folder, "themes");
        if (!Directory.Exists(themesFolder))
        {
            return [];
        }

        return Directory.GetFiles(themesFolder, "*.json")
                        .Where(file => !SameFile(file, ownFile));
    }

    private static bool SameFile(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether the file holds a JSON object, which is what a theme file must be.</summary>
    private static bool ReadsAsObject(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(
                File.ReadAllText(file, Encoding.UTF8),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
