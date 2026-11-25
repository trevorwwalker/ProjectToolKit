// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
using Scheduler.Core.WebContract;
using Scheduler.Web.Tests.Server.Fakes;
using Scheduler.Web.Views;

namespace Scheduler.Web.Tests.Views;

/// <summary>
/// HLTH-6: while main is red the page carries the theme's red-health banner, the sha of the red
/// commit, and a way to the log that says why.
/// </summary>
public sealed class HealthBannerTests
{
    // HLTH-6
    [Fact]
    public void Health_RedBanner_WithShaAndLogLink()
    {
        using var folder = new TempFolder();
        folder.Write("theme.default.json", """
            { "name": "Default", "label": "Modern", "order": 10, "css": [".base { color: black }"],
              "text": { "redHealth": "main does not build or pass its gate.",
                        "redHealthDetail": "Red at {sha}." } }
            """);
        var themes = PageThemes.Load(folder.Path);

        // The snapshot carries the theme's words untouched; the page fills {sha} from Sha (HLTH-6).
        var snapshot = ViewsFixture.Plain();
        snapshot = snapshot with
        {
            Health = new PageHealthView
            {
                Green = false,
                Text = "main abc1234 RED at 12:25",
                Banner = "main does not build or pass its gate.\nRed at {sha}.",
                Sha = "abc1234",
            },
        };

        var html = new PageRenderer(themes).Render(snapshot, remote: false, controlsAllowed: true);

        Assert.Contains("<div class='health-red'>", html, StringComparison.Ordinal);
        Assert.Contains("main does not build or pass its gate.", html, StringComparison.Ordinal);
        Assert.Contains("Red at abc1234.", html, StringComparison.Ordinal);
        Assert.Contains("<a href='/health'>", html, StringComparison.Ordinal);

        // Green main shows no banner.
        var green = new PageRenderer(themes).Render(ViewsFixture.Plain(), remote: false, controlsAllowed: true);
        Assert.DoesNotContain("class='health-red'", green, StringComparison.Ordinal);
    }

    // HLTH-6: banner words with no {sha} token are drawn exactly as written.
    [Fact]
    public void Health_RedBanner_TextWithoutShaToken_Unchanged()
    {
        using var folder = new TempFolder();
        folder.Write("theme.default.json", """
            { "name": "Default", "label": "Modern", "order": 10, "css": [".base { color: black }"],
              "text": { "redHealth": "main does not build or pass its gate.",
                        "redHealthDetail": "Red on main, see the log." } }
            """);
        var themes = PageThemes.Load(folder.Path);

        // No banner of its own, so the page uses the theme's words (PAGE-11); they hold no token.
        var snapshot = ViewsFixture.Plain() with
        {
            Health = new PageHealthView
            {
                Green = false,
                Text = "main RED at 12:25",
                Sha = "abc1234",
            },
        };

        var html = new PageRenderer(themes).Render(snapshot, remote: false, controlsAllowed: true);

        Assert.Contains("main does not build or pass its gate.<br>Red on main, see the log.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("abc1234", html, StringComparison.Ordinal);
    }

    // HLTH-6: before any health check has finished, Sha is "" and {sha} becomes nothing.
    [Fact]
    public void Health_RedBanner_EmptySha_FillsWithNothing()
    {
        using var folder = new TempFolder();
        folder.Write("theme.default.json", """
            { "name": "Default", "label": "Modern", "order": 10, "css": [".base { color: black }"],
              "text": { "redHealth": "main does not build or pass its gate.",
                        "redHealthDetail": "Red at {sha}." } }
            """);
        var themes = PageThemes.Load(folder.Path);

        var snapshot = ViewsFixture.Plain() with
        {
            Health = new PageHealthView
            {
                Green = false,
                Text = "main RED at 12:25",
                Banner = "main does not build or pass its gate.\nRed at {sha}.",
            },
        };

        var html = new PageRenderer(themes).Render(snapshot, remote: false, controlsAllowed: true);

        Assert.Contains("Red at .", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{sha}", html, StringComparison.Ordinal);
    }
}
