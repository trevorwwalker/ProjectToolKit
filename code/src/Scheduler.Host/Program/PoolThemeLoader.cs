// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.Contracts;
using Scheduler.Web.Views;

namespace Scheduler.Host.Program;

/// <summary>
/// The pool page's themes (MP-P1): the pool's own theme first, then every theme the loader offers,
/// including each project's own <c>page.theme</c>, so a project's look stays one click away.
/// </summary>
/// <remarks>
/// <see cref="PageThemes"/> reads its themes from a scheduler folder (<c>theme.default.json</c> and
/// <c>themes/*.json</c>), so the pool's set is staged as such a folder under the pool's log folder: the
/// scheduler's own default (which the themes' <c>extends</c> names), the pool's theme as the page's first, the exe's own themes, and each project's theme as <c>project-&lt;name&gt;.json</c>.
/// A file that is not there or cannot be copied is logged and left out; a theme is no reason to fail the page.
/// </remarks>
public static class PoolThemeLoader
{
    /// <summary>The folder under the pool's log folder the set is staged in.</summary>
    public const string StagingFolder = "themes-staged";

    /// <summary>
    /// The themes for a pool. <paramref name="poolTheme"/> is the pool file's <c>theme</c>: a theme file, as
    /// <c>page.theme</c> names one (absolute, or relative to <paramref name="poolFileFolder"/>), else a name that
    /// only labels the page (no file, the scheduler's own default). <paramref name="projects"/> are the
    /// projects' configs, whose <c>page.theme</c> is resolved against each repository.
    /// </summary>
    public static PageThemes Load(string poolTheme, string poolFileFolder, IEnumerable<SchedulerConfig> projects, string logDir, ISchedulerLog log, string? schedulerFolder = null)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(log);

        var exe = string.IsNullOrWhiteSpace(schedulerFolder) ? AppContext.BaseDirectory : schedulerFolder;
        try
        {
            var stage = Path.Combine(logDir, StagingFolder);
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }

            var themes = Path.Combine(stage, "themes");
            _ = Directory.CreateDirectory(themes);

            var named = poolTheme.Trim();
            var poolFile = named.Length == 0 ? null : Resolve(named, poolFileFolder);
            if (named.Length > 0 && poolFile is null)
            {
                log.Write($"themes: the pool's theme {named} is not a theme file; the page uses the scheduler's own theme files");
            }

            // 1027-C: the staged default is always the scheduler's own, as beside the exe, so a theme's
            // "extends": "../theme.default.json" builds on the scheduler's default and not on the pool's theme.
            var ownDefault = Path.Combine(exe, "theme.default.json");
            var stagedDefault = Path.Combine(stage, "theme.default.json");
            if (File.Exists(ownDefault))
            {
                File.Copy(ownDefault, stagedDefault, overwrite: true);
            }

            // The pool's own theme is the page's first; beside the default, so the scheduler's default stays offered.
            var first = stagedDefault;
            if (poolFile is not null)
            {
                first = Path.Combine(stage, "pool-theme.json");
                File.Copy(poolFile, first, overwrite: true);
            }

            var exeThemes = Path.Combine(exe, "themes");
            if (Directory.Exists(exeThemes))
            {
                foreach (var file in Directory.GetFiles(exeThemes, "*.json"))
                {
                    File.Copy(file, Path.Combine(themes, Path.GetFileName(file)), overwrite: true);
                }
            }

            foreach (var project in projects)
            {
                var named2 = project.PageTheme?.Trim() ?? string.Empty;
                if (named2.Length == 0)
                {
                    continue;
                }

                var file = Resolve(named2, project.Repo);
                if (file is null)
                {
                    log.Write($"themes: {project.Project}'s page.theme {named2} was not found; its theme is left out");
                    continue;
                }

                File.Copy(file, Path.Combine(themes, $"project-{Safe(project.Project)}.json"), overwrite: true);
            }

            return PageThemes.Load(stage, first, null, log.Write);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Write($"themes: staging the pool's themes failed ({e.Message}); the page uses the built-in style");
            return PageThemes.BuiltIn;
        }
    }

    private static string? Resolve(string named, string folder)
    {
        var file = Path.IsPathRooted(named) ? named : Path.Combine(folder, named);
        return File.Exists(file) ? file : null;
    }

    private static string Safe(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
