// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core;
using Scheduler.Core.Contracts;
using Scheduler.Core.WebContract;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Web.Views;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// PAGE-11 (card 420-B): the page takes its themes from the theme files the build copies beside
/// the exe, the project's own theme first, and falls back to the built-in style when they are not
/// there - logging it once rather than failing.
/// </summary>
public sealed class PageThemeLoaderTests
{
    private static readonly DateTimeOffset Built = new(2026, 11, 18, 12, 30, 0, TimeSpan.Zero);

    // PAGE-11: the theme files beside the exe are what the page offers.
    [Fact]
    public void Page_Themes_LoadedFromExeFolder()
    {
        using var temp = new TempDir();
        var exe = Folder(Path.Combine(temp.Path, "exe"));
        Write(Path.Combine(exe, "theme.default.json"), """
            { "name": "Default", "label": "Modern", "order": 10, "css": [".base { color: black }"] }
            """);
        var themesFolder = Folder(Path.Combine(exe, "themes"));
        Write(Path.Combine(themesFolder, "dark.json"), """
            { "name": "Dark", "label": "Dark", "order": 20, "css": [".d { color: #fff }"] }
            """);

        var log = new ListLog();
        var themes = PageThemeLoader.Load(new SchedulerConfig { Project = "proj", Repo = temp.Path }, log, exe);

        Assert.Equal(new[] { "project", "dark" }, themes.Entries.Select(entry => entry.Key).ToArray());
        Assert.Equal(new[] { "Modern", "Dark" }, themes.Entries.Select(entry => entry.Label).ToArray());

        // The page's selector offers both, and the page carries both styles.
        var html = new PageRenderer(themes).Render(Plain(), remote: false, controlsAllowed: true);
        Assert.Contains("<option value='project'>Modern</option>", html, StringComparison.Ordinal);
        Assert.Contains("<option value='dark'>Dark</option>", html, StringComparison.Ordinal);
        Assert.Contains(".base { color: black }", html, StringComparison.Ordinal);
        Assert.Contains(".d { color: #fff }", html, StringComparison.Ordinal);

        // Nothing was wrong, so nothing was logged.
        Assert.Empty(log.Lines);
    }

    // PAGE-11: page.theme names the project's own theme; it is offered first and its words are used.
    [Fact]
    public void Page_Themes_ProjectThemeFirst()
    {
        using var temp = new TempDir();
        var exe = Folder(Path.Combine(temp.Path, "exe"));
        Write(Path.Combine(exe, "theme.default.json"), """
            { "name": "Default", "label": "Modern", "order": 10, "css": [".base { color: black }"],
              "text": { "redHealth": "the scheduler's own words", "redHealthDetail": "own {sha}" } }
            """);
        var repoThemes = Folder(Path.Combine(temp.Path, "assets", "themes"));
        Write(Path.Combine(repoThemes, "lakers.json"), """
            { "name": "Lakers", "label": "Lakers", "order": 1, "css": [".p { color: gold }"],
              "text": { "redHealth": "the team cannot play", "redHealthDetail": "{sha} is on the bench" } }
            """);

        var log = new ListLog();
        var config = new SchedulerConfig
        {
            Project = "proj",
            Repo = temp.Path,
            PageTheme = @"assets\themes\lakers.json",
        };
        var themes = PageThemeLoader.Load(config, log, exe);

        // The project theme is first in the selector, ahead of the scheduler's own files.
        Assert.Equal("project", themes.Entries[0].Key);
        Assert.Equal("Lakers", themes.Entries[0].Label);
        Assert.Equal(new[] { "project", "default" }, themes.Entries.Select(entry => entry.Key).ToArray());

        // Its banner words are the ones the page shows while main is red (HLTH-6).
        Assert.Equal("the team cannot play", themes.RedHealthText);
        Assert.Equal("{sha} is on the bench", themes.RedHealthDetail);
        var snapshot = Plain() with
        {
            Health = new PageHealthView { Green = false, Text = "main abc1234 RED at 12:25", Sha = "abc1234" },
        };
        var html = new PageRenderer(themes).Render(snapshot, remote: false, controlsAllowed: true);
        Assert.Contains("<option value='project'>Lakers</option>", html, StringComparison.Ordinal);
        Assert.Contains("the team cannot play", html, StringComparison.Ordinal);
        Assert.Contains("abc1234 is on the bench", html, StringComparison.Ordinal);
        Assert.Empty(log.Lines);
    }

    // PAGE-11: no theme files beside the exe: the page still renders, in the built-in style, once logged.
    [Fact]
    public void Page_Themes_MissingFolder_BuiltInAndLogged()
    {
        using var temp = new TempDir();
        var missing = Path.Combine(temp.Path, "no-such-exe-folder");
        var log = new ListLog();

        var themes = PageThemeLoader.Load(new SchedulerConfig { Project = "proj", Repo = temp.Path }, log, missing);

        Assert.Same(PageThemes.BuiltIn, themes);
        Assert.Single(themes.Entries);
        Assert.Equal(PageThemes.ProjectKey, themes.Entries[0].Key);
        Assert.Equal("Built-in", themes.Entries[0].Label);

        // The page still renders, and the one log line says the built-in style is in use.
        var html = new PageRenderer(themes).Render(Plain(), remote: false, controlsAllowed: true);
        Assert.Contains("<select id='themesel'", html, StringComparison.Ordinal);
        var line = Assert.Single(log.Lines);
        Assert.Contains("theme", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("built-in", line, StringComparison.OrdinalIgnoreCase);
    }

    // PAGE-11: a theme file that cannot be read is logged once and the built-in style covers it.
    [Fact]
    public void Page_Themes_UnreadableFile_LoggedOnce()
    {
        using var temp = new TempDir();
        var exe = Folder(Path.Combine(temp.Path, "exe"));
        Write(Path.Combine(exe, "theme.default.json"), """
            { "name": "Default", "label": "Modern", "order": 10, "css": [".base { color: black }"] }
            """);
        var themesFolder = Folder(Path.Combine(exe, "themes"));
        Write(Path.Combine(themesFolder, "broken.json"), "{ this is not json ");

        var log = new ListLog();
        var themes = PageThemeLoader.Load(new SchedulerConfig { Project = "proj", Repo = temp.Path }, log, exe);

        // The good themes survive; the unreadable one is not offered.
        Assert.Equal(new[] { "project" }, themes.Entries.Select(entry => entry.Key).ToArray());
        Assert.Equal("Modern", themes.Entries[0].Label);
        Assert.Contains(log.Lines, line => line.Contains("broken", StringComparison.OrdinalIgnoreCase));
        Assert.Single(log.Lines);
    }

    // PAGE-11: with no folder named the loader reads beside the program, where the build copies
    // assets/themes/ (the card's second check, as a test).
    [Fact]
    public void Page_Themes_BesideTheProgram()
    {
        var log = new ListLog();

        var themes = PageThemeLoader.Load(new SchedulerConfig { Project = "proj", Repo = string.Empty }, log);

        Assert.Contains("dark", themes.Entries.Select(entry => entry.Key).ToArray());
        Assert.Contains("lakers", themes.Entries.Select(entry => entry.Key).ToArray());
        Assert.True(themes.Entries.Count >= 2, $"only {themes.Entries.Count} theme(s) beside the program");
        Assert.Empty(log.Lines);
    }

    // Card 962-C: a theme font that is nowhere is said once in the scheduler log, not silently.
    [Fact]
    public void Page_Themes_MissingFontIsLoggedOnce()
    {
        using var temp = new TempDir();
        var exe = Folder(Path.Combine(temp.Path, "exe"));
        Write(Path.Combine(exe, "theme.default.json"), """
            { "name": "Default", "label": "Modern", "css": [], "font": { "file": "tools/fonts/none.ttf" } }
            """);

        var log = new ListLog();
        PageThemeLoader.Load(new SchedulerConfig { Project = "proj", Repo = temp.Path }, log, exe);

        Assert.Single(log.Lines);
    }

    private static PageSnapshot Plain() => new()
    {
        Built = Built,
        Project = "ProjectToolkit",
        Version = CoreInfo.Version,
        State = new PageStateView { State = SchedulerState.Running, Text = "running" },
    };

    private static string Folder(string path) => Directory.CreateDirectory(path).FullName;

    private static void Write(string path, string text) => File.WriteAllText(path, text);
}
