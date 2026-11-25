// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>PAGE-11: the order of the theme selector, extends, and which theme's words are used.</summary>
public sealed class PageThemeTests
{
    // PAGE-11
    [Fact]
    public void Page_Themes_OrderAndExtends()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "themes"));

        folder.Write("proj.woff2", "not really a font");
        var project = folder.Write("project.json", """
            { "name": "Project", "label": "Project", "order": 1, "css": [".p { color: red }"],
              "font": { "file": "proj.woff2" },
              "text": { "redHealth": "proj red", "redHealthDetail": "proj detail {sha}" } }
            """);
        folder.Write("theme.default.json", """
            { "name": "Default", "label": "Modern", "order": 10,
              "css": [".base { color: black }"],
              "text": { "redHealth": "default red", "redHealthDetail": "default detail {sha}" } }
            """);
        folder.Write(Path.Combine("themes", "b.json"), """
            { "name": "B", "label": "Bravo", "order": 30, "extends": "../theme.default.json",
              "css": [".b { color: blue }"],
              "font": { "file": "fonts/b.woff2" },
              "text": { "redHealth": "b red", "redHealthDetail": "b detail {sha}" } }
            """);
        folder.Write(Path.Combine("themes", "a.json"), """
            { "name": "A", "label": "Alpha", "order": 30, "css": [".a { color: green }"] }
            """);

        var themes = PageThemes.Load(folder.Path, project);

        // The project theme first, then the built-ins by order and label.
        Assert.Equal(
            new[] { "project", "default", "a", "b" },
            themes.Entries.Select(entry => entry.Key).ToArray());
        Assert.Equal(
            new[] { "Project", "Modern", "Alpha", "Bravo" },
            themes.Entries.Select(entry => entry.Label).ToArray());

        // extends prepends the base CSS.
        var bravo = themes.Entries.Single(entry => entry.Key == "b");
        Assert.StartsWith(".base { color: black }", bravo.Css, StringComparison.Ordinal);
        Assert.EndsWith(".b { color: blue }", bravo.Css, StringComparison.Ordinal);

        // Only the project theme's font and banner words are used.
        Assert.Equal("proj red", themes.RedHealthText);
        Assert.Equal("proj detail {sha}", themes.RedHealthDetail);
        Assert.Equal("proj.woff2", themes.FontFile);

        // The selector offers them in that order, and the page's head carries every theme.
        var html = new PageRenderer(themes).Render(ViewsFixture.Plain(), remote: false, controlsAllowed: true);
        var selector = html.Substring(html.IndexOf("<select id='themesel'", StringComparison.Ordinal), 400);
        Assert.True(
            selector.IndexOf("Project", StringComparison.Ordinal) < selector.IndexOf("Modern", StringComparison.Ordinal)
            && selector.IndexOf("Modern", StringComparison.Ordinal) < selector.IndexOf("Alpha", StringComparison.Ordinal)
            && selector.IndexOf("Alpha", StringComparison.Ordinal) < selector.IndexOf("Bravo", StringComparison.Ordinal),
            $"selector not in order: {selector}");
        Assert.Contains("<style data-theme='project' media='all'", html, StringComparison.Ordinal);
        Assert.Contains("<style data-theme='b' media='not all'", html, StringComparison.Ordinal);
        Assert.Contains("@font-face", html, StringComparison.Ordinal);
    }

    private static readonly string[] NewThemes = ["eggplant", "rose", "sunflower", "bamboo", "iris", "lafc", "visualstudio"];

    // 1011-C: every theme file in assets/themes loads, and the seven colour themes follow the existing ones in order.
    [Fact]
    public void Page_Themes_ShippedColourThemesLoadInOrder()
    {
        using var folder = new TempFolder();
        var source = ShippedThemes();
        Directory.CreateDirectory(Path.Combine(folder.Path, "themes"));
        var files = Directory.GetFiles(source, "*.json");
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var target = name == "theme.default.json" ? name : Path.Combine("themes", name);
            folder.Write(target, File.ReadAllText(file));
        }

        var themes = PageThemes.Load(folder.Path);
        var keys = themes.Entries.Select(entry => entry.Key).ToList();

        // The project entry stands for the default file, so one entry per file.
        Assert.Equal(files.Length, keys.Count);
        Assert.Equal(NewThemes, keys.Skip(keys.Count - NewThemes.Length).ToArray());
    }

    // 1011-C: text on every surface of the new themes keeps WCAG AA (4.5:1).
    [Theory]
    [InlineData("eggplant")]
    [InlineData("rose")]
    [InlineData("sunflower")]
    [InlineData("bamboo")]
    [InlineData("iris")]
    [InlineData("lafc")]
    [InlineData("visualstudio")]
    public void Page_Themes_ShippedColourThemesKeepContrast(string key)
    {
        var css = File.ReadAllText(Path.Combine(ShippedThemes(), key + ".json"));
        string Var(string name) => System.Text.RegularExpressions.Regex.Match(css, "--" + name + @": (#[0-9a-fA-F]{6})").Groups[1].Value;

        void Check(string text, string background) =>
            Assert.True(Contrast(Var(text), Var(background)) >= 4.5, $"{key}: --{text} on --{background} is {Contrast(Var(text), Var(background)):0.00}");

        Check("text", "panel");
        Check("muted", "panel");
        Check("accent-text", "accent");
        Check("ok", "panel");
        Check("warn", "panel");
        Check("bad", "panel");
        Check("text", "log-bg");
    }

    // 1086-C: the .g hover bubble is drawn only for an element that really carries a tip. A .g
    // wrapper without one (the workspaces form, 1033-C) got an empty rounded box in the theme's
    // text colour above the box on hover, because content: attr(data-tip) on a missing attribute
    // still generates the ::after.
    [Theory]
    [InlineData("theme.default.json")]
    [InlineData("dark.json")]
    [InlineData("light.json")]
    [InlineData("visualstudio.json")]
    public void Page_Themes_HoverBubbleNeedsANonEmptyDataTip(string file)
    {
        var json = File.ReadAllText(Path.Combine(ShippedThemes(), file));
        var css = string.Join(
            "\n",
            System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("css").EnumerateArray().Select(e => e.GetString()));

        var bubbleRules = css
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("attr(data-tip)", StringComparison.Ordinal))
            .ToList();

        // Only the default file draws the bubble; every other theme extends it and must not
        // re-add an unguarded rule of its own.
        Assert.Equal(file == "theme.default.json", bubbleRules.Count > 0);
        foreach (var rule in bubbleRules)
        {
            var selector = rule[..rule.IndexOf('{', StringComparison.Ordinal)];
            Assert.Contains("[data-tip]", selector, StringComparison.Ordinal);
            Assert.Contains(":not([data-tip=''])", selector, StringComparison.Ordinal);
        }
    }

    private static string ShippedThemes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "assets", "themes")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "assets", "themes");
    }

    private static double Contrast(string a, string b)
    {
        static double Lum(string hex)
        {
            double Channel(int at)
            {
                var c = Convert.ToInt32(hex.Substring(at, 2), 16) / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        }

        var (hi, lo) = (Math.Max(Lum(a), Lum(b)), Math.Min(Lum(a), Lum(b)));
        return (hi + 0.05) / (lo + 0.05);
    }
}
