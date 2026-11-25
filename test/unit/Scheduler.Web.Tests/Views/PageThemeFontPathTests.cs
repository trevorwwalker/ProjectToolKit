// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>962-C: a theme's font is looked for beside the theme, then from the repository root; a missing one is logged once.</summary>
public sealed class PageThemeFontPathTests
{
    private const string ThemeJson = """{ "name": "P", "label": "P", "css": [], "font": { "file": "tools/fonts/f.ttf" } }""";

    [Fact]
    public void Page_ThemeFont_RepoRelativePathIsFoundFromRepoRoot()
    {
        using var folder = new TempFolder();
        Put(folder, Path.Combine("repo", "tools", "fonts", "f.ttf"), "bytes");
        var theme = Put(folder, Path.Combine("repo", "jobs", "theme.json"), ThemeJson);
        var lines = new List<string>();

        var themes = PageThemes.Load(folder.Path, theme, Path.Combine(folder.Path, "repo"), lines.Add);

        Assert.True(themes.FontExists);
        Assert.Empty(lines);
    }

    [Fact]
    public void Page_ThemeFont_RepoRelativePathIsFoundWithoutAStatedRoot()
    {
        using var folder = new TempFolder();
        Put(folder, Path.Combine("repo", "tools", "fonts", "f.ttf"), "bytes");
        var theme = Put(folder, Path.Combine("repo", "jobs", "theme.json"), ThemeJson);

        Assert.True(PageThemes.Load(folder.Path, theme).FontExists);
    }

    [Fact]
    public void Page_ThemeFont_BesideTheThemeWinsOverTheRepoRoot()
    {
        using var folder = new TempFolder();
        var beside = Put(folder, Path.Combine("repo", "jobs", "tools", "fonts", "f.ttf"), "near");
        Put(folder, Path.Combine("repo", "tools", "fonts", "f.ttf"), "far");
        var theme = Path.Combine(folder.Path, "repo", "jobs", "theme.json");

        Assert.Equal(Path.GetFullPath(beside), Path.GetFullPath(PageThemes.ResolveFont(theme, "tools/fonts/f.ttf", Path.Combine(folder.Path, "repo"))));
    }

    [Fact]
    public void Page_ThemeFont_MissingFontIsLoggedOnce()
    {
        using var folder = new TempFolder();
        var theme = Put(folder, Path.Combine("repo", "jobs", "theme.json"), ThemeJson);
        var lines = new List<string>();

        var themes = PageThemes.Load(folder.Path, theme, Path.Combine(folder.Path, "repo"), lines.Add);

        Assert.False(themes.FontExists);
        var line = Assert.Single(lines);
        Assert.Contains("tools/fonts/f.ttf", line, StringComparison.Ordinal);
    }

    private static string Put(TempFolder folder, string name, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder.Path, name))!);
        return folder.Write(name, text);
    }
}
