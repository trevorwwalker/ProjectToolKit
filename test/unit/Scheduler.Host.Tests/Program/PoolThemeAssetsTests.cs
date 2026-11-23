using System.Text.Json;
using Scheduler.Host.Program;
using Scheduler.Host.Tests.Git;
using Scheduler.Host.Tests.Slots;
using Scheduler.Web.Views;
using Xunit;

namespace Scheduler.Host.Tests.Program;

/// <summary>
/// Card 1027-C: the pool page and the theme files the build puts beside the program (assets/themes/). Every
/// file reaches the page as its own style block, and the staged set's <c>extends</c> builds on the scheduler's
/// default, not on the pool's theme.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class PoolThemeAssetsTests
{
    private static string ExeThemes => Path.Combine(AppContext.BaseDirectory, "themes");

    // 1027-C: for each theme file, the pool page carries a style block under its key with its --bg and .screen rules,
    // and the menu offers it.
    [Fact]
    public void PoolPage_EveryThemeFile_StyleBlockCarriesItsBgAndScreen()
    {
        using var temp = new TempDir();
        var log = new ListLog();

        var themes = PoolThemeLoader.Load("", temp.Path, [], Path.Combine(temp.Path, "logs"), log);
        var html = new CombinedPageRenderer(themes).Render([], "", remote: false, controlsAllowed: true);

        var files = Directory.GetFiles(ExeThemes, "*.json");
        Assert.True(files.Length >= 15, $"only {files.Length} theme file(s) beside the program");
        foreach (var file in files.Append(Path.Combine(AppContext.BaseDirectory, "theme.default.json")))
        {
            var name = Path.GetFileName(file);
            var key = name == "theme.default.json" ? PageThemes.ProjectKey : Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var block = Block(html, key);
            Assert.True(block is not null, $"{name}: no style block '{key}' on the pool page");
            Assert.Contains($"<option value='{key}'>", html, StringComparison.Ordinal);
            foreach (var rule in Rules(file))
            {
                Assert.True(block!.Contains(rule, StringComparison.Ordinal), $"{name}: its block lacks {rule}");
            }
        }

        Assert.Empty(log.Lines);
    }

    // 1027-C: with the pool's own theme first, a staged theme's "extends": "../theme.default.json" is the scheduler's
    // default (as beside the exe), not the pool's theme; and the scheduler's default is still offered.
    [Fact]
    public void PoolThemes_Extends_ResolvesToSchedulersDefault()
    {
        using var temp = new TempDir();
        var poolTheme = Path.Combine(temp.Path, "pool-look.json");
        File.WriteAllText(poolTheme, """{ "label": "Pool look", "order": 1, "css": [".pool-only { color: red }"] }""");
        var log = new ListLog();

        var themes = PoolThemeLoader.Load(poolTheme, temp.Path, [], Path.Combine(temp.Path, "logs"), log);

        var defaultCss = PageThemes.Load(AppContext.BaseDirectory).Entries[0].Css;
        Assert.Equal("Pool look", themes.Entries[0].Label);
        var lakers = themes.Entries.Single(e => e.Key == "lakers");
        Assert.StartsWith(defaultCss, lakers.Css, StringComparison.Ordinal);
        Assert.DoesNotContain(".pool-only", lakers.Css, StringComparison.Ordinal);
        Assert.Contains(themes.Entries, e => e.Key == "default" && e.Css == defaultCss);
        Assert.Empty(log.Lines);
    }

    // The block a theme key is carried under, or null.
    private static string? Block(string html, string key)
    {
        var start = html.IndexOf($"<style data-theme='{key}'", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var end = html.IndexOf("</style>", start, StringComparison.Ordinal);
        return end < 0 ? null : html[start..end];
    }

    // The file's own --bg declaration and .screen rule, as it writes them.
    private static IEnumerable<string> Rules(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var lines = doc.RootElement.TryGetProperty("css", out var css) ? css.EnumerateArray().Select(l => l.GetString() ?? "").ToList() : [];
        var all = string.Join('\n', lines);
        var bg = all.IndexOf("--bg:", StringComparison.Ordinal);
        if (bg >= 0)
        {
            var semi = all.IndexOf(';', bg);
            yield return all[bg..semi];
        }

        foreach (var line in lines.Where(l => l.TrimStart().StartsWith(".screen {", StringComparison.Ordinal)))
        {
            yield return line;
        }
    }
}
